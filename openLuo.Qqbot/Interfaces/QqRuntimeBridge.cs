using System.Text.Json;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Interfaces.QQbot;

public sealed class QqRuntimeBridge
{
    private readonly IAgentRuntime _runtime;
    private readonly QqBotConfig _config;

    public QqRuntimeBridge(IAgentRuntime runtime, QqBotConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    public async Task<TurnResult> HandleAsync(string scene, long targetId, long actorId, string text, string? senderName = null, IReadOnlyList<object>? blocks = null, CancellationToken ct = default)
    {
        var sessionId = $"qq-{scene}-{targetId}";
        await _runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = sessionId, SubjectId = _config.DefaultSubjectId, AgentId = _config.DefaultAgentId,
            ClientType = "qqbot", ClientId = targetId.ToString(), ConversationId = sessionId
        }, ct);
        return await _runtime.RunTurnAsync(new TurnRequest
        {
            SessionId = sessionId, TurnId = Guid.NewGuid().ToString("N"), SourceId = "qqbot",
            ChannelId = targetId.ToString(), ActorId = actorId.ToString(), SenderName = senderName, Text = text,
            Blocks = blocks,
            Meta = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["scene"] = scene,
                ["senderName"] = senderName ?? actorId.ToString(),
                ["channelId"] = targetId.ToString()
            }
        }, ct);
    }

    /// <summary>感知通道：消息写入会话历史但不触发 LLM 回合（群聊未 @ 消息）。</summary>
    public async Task ObserveAsync(string scene, long targetId, string text, string? senderName = null, IReadOnlyList<object>? blocks = null, CancellationToken ct = default)
    {
        var sessionId = $"qq-{scene}-{targetId}";
        await _runtime.OpenSessionAsync(new SessionOpenRequest
        {
            SessionId = sessionId, SubjectId = _config.DefaultSubjectId, AgentId = _config.DefaultAgentId,
            ClientType = "qqbot", ClientId = targetId.ToString(), ConversationId = sessionId
        }, ct);
        await _runtime.AppendMessageAsync(sessionId, senderName, text, blocks, ct);
    }

    public static IReadOnlyList<QqReplyPart> Render(TurnResult result)
    {
        var parts = result.Outputs.Select(Render).Where(p => p is not null).Cast<QqReplyPart>().ToList();
        if (!string.IsNullOrWhiteSpace(result.FinalText)) parts.Add(new QqReplyPart("text", result.FinalText));
        return parts;
    }

    private static QqReplyPart? Render(OutputItem item) => item.Kind switch
    {
        ReplyItemKind.Text => new("text", Convert.ToString(item.Payload) ?? string.Empty),
        ReplyItemKind.Image => new("image", Convert.ToString(item.Payload) ?? string.Empty),
        ReplyItemKind.Audio => new("record", Convert.ToString(item.Payload) ?? string.Empty),
        // Card = 结构化不透明载荷：与产出扩展（music:share_song）联合契约
        // { Platform="163", Id, Title?, Url? }。解析失败按 Url/JSON 文本降级。
        ReplyItemKind.Card => RenderCard(item),
        _ => new("text", $"[{item.Kind.ToString().ToLowerInvariant()}] {item.Payload}")
    };

    /// <summary>供 QqBotApplication 复用（interim 队列等非回合路径）。</summary>
    internal static QqReplyPart? TryRenderCard(OutputItem item) => RenderCard(item);

    /// <summary>把 163 音乐分享卡渲染为 OneBot music 段（QQ 原生卡）；其它/未知结构 → 文本降级。</summary>
    private static QqReplyPart? RenderCard(OutputItem item)
    {
        JsonElement root;
        try
        {
            root = JsonSerializer.SerializeToElement(item.Payload);
        }
        catch
        {
            return new("text", Convert.ToString(item.Payload) ?? string.Empty);
        }
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("Platform", out var platform)
            && platform.GetString() == "163"
            && root.TryGetProperty("Id", out var id)
            && id.ValueKind == JsonValueKind.Number
            && id.TryGetInt64(out var songId))
        {
            return new QqReplyPart("music", songId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        // 降级：优先可点链接，其次紧凑 JSON
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Url", out var url) && url.ValueKind == JsonValueKind.String)
        {
            var text = url.GetString();
            return new("text", string.IsNullOrWhiteSpace(text) ? "[card]" : text);
        }
        return new("text", $"[card] {JsonSerializer.Serialize(item.Payload)}");
    }
}

/// <summary>Kind: text | image | music（OneBot music 段,Value 为平台资源 id）| record（语音,Value 为 data URL）。</summary>
public sealed record QqReplyPart(string Kind, string Value);
