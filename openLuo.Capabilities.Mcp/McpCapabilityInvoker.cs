using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Mcp;

/// <summary>MCP tool invoker. The source owns the connected client lifetime.</summary>
public sealed class McpCapabilityInvoker : ICapabilityInvoker
{
    private readonly McpClient _client;
    private readonly string _serverId;
    private readonly bool _injectContextKeys;
    private readonly Func<CancellationToken, Task<bool>>? _reconnect;

    public McpCapabilityInvoker(McpClient client, string serverId, bool injectContextKeys = true, Func<CancellationToken, Task<bool>>? reconnect = null)
    {
        _client = client;
        _serverId = serverId;
        _injectContextKeys = injectContextKeys;
        _reconnect = reconnect;
    }

    public async Task<CapabilityResult> InvokeAsync(
        CapabilityCall call,
        CapabilityExecutionContext context,
        CancellationToken ct = default)
    {
        var toolName = ResolveToolName(call.CanonicalId);
        try
        {
            return await InvokeCoreAsync(toolName, call, context, ct);
        }
        catch (Exception ex) when (IsSessionExpired(ex) && _reconnect is not null)
        {
            // Streamable HTTP 会话过期（服务端 TTL）：重建客户端后重试一次
            if (await _reconnect(ct))
                return await InvokeCoreAsync(toolName, call, context, ct);
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed, Error = ex.Message };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Cancelled, Error = "cancelled" };
        }
        catch (Exception ex)
        {
            return new CapabilityResult { InvocationId = call.InvocationId, Success = false, Status = CapabilityStatus.Failed, Error = ex.Message };
        }
    }

    private async Task<CapabilityResult> InvokeCoreAsync(string toolName, CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct)
    {
        // 参数：优先用模型原生 tool_calls 的原始参数 JSON（顶层键即 MCP 参数，
        // 例如 {"stationNames":"深圳北|长沙"}）。旧包装格式 {"args":[...],"options":{...}}
        // 中 args/options 键跳过，options 合并进顶层。
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(call.RawArgumentsJson))
        {
            try
            {
                if (JsonNode.Parse(call.RawArgumentsJson)?.AsObject() is { } raw)
                {
                    foreach (var pair in raw)
                    {
                        if (pair.Key.Equals("args", StringComparison.OrdinalIgnoreCase) ||
                            pair.Key.Equals("options", StringComparison.OrdinalIgnoreCase))
                            continue;
                        arguments[pair.Key] = pair.Value is null
                            ? null
                            : JsonSerializer.SerializeToElement(pair.Value);
                    }
                }
            }
            catch
            {
                // 解析失败时退回 options 注入
            }
        }
        foreach (var pair in call.Options)
            arguments[pair.Key] = pair.Value;

        // 调用方上下文注入（MCP 协议保留键，不改工具定义）：
        // 服务器端工具可据此做 gameId 空间隔离/审计。带 _openluo_ 前缀避免与
        // 真实参数冲突；服务器拒绝未知参数时可配置关闭。
        if (_injectContextKeys)
        {
            arguments["_openluo_game_id"] = context.GameId;
            arguments["_openluo_session_id"] = context.SessionId;
            arguments["_openluo_turn_id"] = context.TurnId;
        }

        var result = await _client.CallToolAsync(toolName, arguments, cancellationToken: ct);
        var blocks = result.Content.OfType<TextContentBlock>().Select(b => b.Text).ToList();

        // 内联 data URL（图片/音频）转公共输出项；剩余文本回传 LLM
        var outputs = new List<OutputItem>();
        var textParts = new List<string>();
        foreach (var block in blocks)
        {
            if (TryExtractDataUrl(block, out var dataUrl, out var isAudio, out var leading))
            {
                outputs.Add(new OutputItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = isAudio ? ReplyItemKind.Audio : ReplyItemKind.Image,
                    Payload = dataUrl,
                    SourceCapability = call.CanonicalId,
                    Fingerprint = $"mcp:{_serverId}:{dataUrl.GetHashCode()}"
                });
                // 前置文本(如"本次朗读完成...")保留给 LLM，让模型知道本次结果的语义；
                // 仅当无前置文本时用通用占位符。
                if (!string.IsNullOrWhiteSpace(leading))
                    textParts.Add(leading.Trim());
                else
                    textParts.Add(isAudio
                        ? "audio fetched; the audio is delivered to the user with this reply"
                        : "image fetched; the image is delivered to the user with this reply");
            }
            else
            {
                textParts.Add(block);
            }
        }
        var text = string.Join("\n", textParts);
        return new CapabilityResult
        {
            InvocationId = call.InvocationId,
            Success = result.IsError is not true,
            Status = result.IsError is true ? CapabilityStatus.Failed : CapabilityStatus.Ok,
            Text = text,
            Outputs = outputs,
            Error = result.IsError is true ? (string.IsNullOrWhiteSpace(text) ? $"mcp tool failed: {toolName}" : text) : null
        };
    }

    private static bool IsSessionExpired(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("SessionExpired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("is expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("401", StringComparison.Ordinal)
            && message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveToolName(string canonicalId)
    {
        var prefix = $"mcp:{_serverId}:";
        return canonicalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? canonicalId[prefix.Length..] : canonicalId;
    }

    /// <summary>从文本块提取内联 data URL(图片/音频)，并回传 data URL 之前的前置文本。</summary>
    /// <remarks>
    /// 兼容两种形态：① 整块仅为 data URL(旧行为,前置文本为空)；
    /// ② 块内含 `<前置文本>\ndata:&lt;mime&gt;;base64,...`(如 TTS 返回的"本次朗读完成...\ndata:audio/...")。
    /// data URL 从 `data:` 起到**该行末尾**或块尾,避免吞掉其后文本。
    /// </remarks>
    private static bool TryExtractDataUrl(string text, out string dataUrl, out bool isAudio, out string leading)
    {
        dataUrl = string.Empty;
        isAudio = false;
        leading = string.Empty;

        var start = text.IndexOf("data:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;
        var base64 = text.IndexOf(";base64,", start, StringComparison.OrdinalIgnoreCase);
        if (base64 < 0)
            return false;
        var mime = text[(start + 5)..base64];
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            isAudio = false;
        else if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            isAudio = true;
        else
            return false;

        // data URL 到行尾/块尾(避免吞掉其后文本)
        var newline = text.IndexOf('\n', base64);
        var end = newline < 0 ? text.Length : newline;
        dataUrl = text[start..end].TrimEnd();

        leading = text[..start].TrimEnd();
        return true;
    }
}
