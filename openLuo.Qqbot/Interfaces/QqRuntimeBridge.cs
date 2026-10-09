using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.Client;
using openLuo.Protocol;

namespace openLuo.Interfaces.QQbot;

/// <summary>
/// QQ ↔ Hub 桥：把一个 QQ 频道（群 / 好友）映射为一个 Hub 会话，经 Wire 协议驱动回合。
/// **不引用内核**（Hub 为唯一权威）。
/// </summary>
public sealed class QqRuntimeBridge
{
    private readonly HubClient _hub;
    private readonly HttpClient _http;
    private readonly string _httpBase;
    private readonly QqBotConfig _config;

    /// <summary>qq-{scene}-{targetId} → Hub sessionId（Task 缓存，避免并发重复开会话）。</summary>
    private readonly ConcurrentDictionary<string, Task<string>> _sessions = new(StringComparer.Ordinal);

    public QqRuntimeBridge(HubClient hub, HttpClient http, string httpBase, QqBotConfig config)
    {
        _hub = hub;
        _http = http;
        _httpBase = httpBase.TrimEnd('/');
        _config = config;
    }

    public static string SessionKey(string scene, long targetId) => $"qq-{scene}-{targetId}";

    /// <summary>确保该 QQ 频道已有 Hub 会话，返回 sessionId。</summary>
    public Task<string> EnsureSessionAsync(string scene, long targetId, CancellationToken ct) =>
        _sessions.GetOrAdd(SessionKey(scene, targetId), _ => OpenAsync(scene, targetId, ct));

    private async Task<string> OpenAsync(string scene, long targetId, CancellationToken ct)
    {
        var session = await _hub.OpenSessionAsync(
            _config.DefaultSubjectId,
            _config.DefaultAgentId,
            conversationId: SessionKey(scene, targetId),
            ct: ct);
        return session.SessionId;
    }

    /// <summary>感知通道：消息写入会话历史但不触发回合（群聊未 @ 的消息）。</summary>
    public async Task ObserveAsync(
        string scene, long targetId, string text, string? senderName,
        IReadOnlyList<JsonNode>? blocks, CancellationToken ct)
    {
        var sessionId = await EnsureSessionAsync(scene, targetId, ct);
        await _hub.SendAsync(EnvelopeFactory.Create(MessageTypes.MessageAppend, new MessageAppendCommand
        {
            SessionId = sessionId,
            SenderName = senderName,
            Text = text,
            Blocks = blocks,
        }, sessionId: sessionId), ct);
    }

    /// <summary>
    /// 执行回合并逐步产出回复片段：回合内的 `output` 事件**即发**（每项一个片段），
    /// 回合结束再产出其 outputs + finalText（可直接逐片段发送）。
    /// </summary>
    public async IAsyncEnumerable<QqReplyPart> TurnAsync(
        string scene, long targetId, long actorId, string text, string? senderName,
        IReadOnlyList<JsonNode>? blocks,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _ = await EnsureSessionAsync(scene, targetId, ct);

        var request = new TurnRequestDto
        {
            Text = text,
            SourceId = "qq",
            SenderName = senderName,
            ChannelId = targetId.ToString(),
            ActorId = actorId.ToString(),
            UserId = actorId.ToString(),
            Blocks = blocks,
        };

        await foreach (var evt in _hub.StreamTurnAsync(request, ct))
        {
            switch (evt.Type)
            {
                case EventTypes.Output:
                    if (evt.DataAs<OutputDto>() is { } interim)
                        yield return ToPart(interim);
                    break;

                case EventTypes.TurnFinal:
                    if (evt.DataAs<TurnResultDto>() is { } result)
                    {
                        foreach (var output in result.Outputs)
                            yield return ToPart(output);
                        if (!string.IsNullOrWhiteSpace(result.FinalText))
                            yield return new QqReplyPart("text", result.FinalText!);
                    }
                    break;

                case EventTypes.Error:
                    yield return new QqReplyPart("text", $"[错误] {evt.ErrorCode}: {evt.ErrorMsg}");
                    break;
            }
        }
    }

    /// <summary>读取会话上下文摘要（协议 `GET /v1/sessions/{id}/context`）。</summary>
    public async Task<string> GetContextSummaryAsync(string scene, long targetId, CancellationToken ct)
    {
        var sessionId = await EnsureSessionAsync(scene, targetId, ct);
        try
        {
            var json = await _http.GetStringAsync($"{_httpBase}/v1/sessions/{sessionId}/context", ct);
            return ProtocolJson.Deserialize<Envelope>(json)?.DataAs<ContextResponse>()?.Summary ?? "session not open";
        }
        catch (Exception)
        {
            return "session not open";
        }
    }

    public static QqReplyPart ToPart(OutputDto item) => item.Kind switch
    {
        OutputKind.Image => new("image", PayloadString(item)),
        OutputKind.Audio => new("record", PayloadString(item)),
        OutputKind.Card => RenderCard(item) ?? new("text", "[card] " + PayloadString(item)),
        _ => new("text", PayloadString(item)),
    };

    private static string PayloadString(OutputDto item) => item.Payload switch
    {
        null => item.AssetRef?.Id ?? string.Empty,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonNode node => node.ToJsonString(),
    };

    /// <summary>163 音乐分享卡 → OneBot music 段（Value = 歌曲 id）；未知结构 → null（调用方降级为文本）。</summary>
    private static QqReplyPart? RenderCard(OutputDto item)
    {
        if (item.Payload is not JsonObject obj)
            return null;

        var platform = obj["platform"]?.GetValue<string>();
        var id = obj["id"];
        return string.Equals(platform, "163", StringComparison.OrdinalIgnoreCase)
               && id is not null
               && long.TryParse(id.ToString(), out var songId)
            ? new QqReplyPart("music", songId.ToString())
            : null;
    }
}

/// <summary>Kind: text | image | music（OneBot music 段，Value 为平台资源 id）| record（语音，Value 为 base64 data URL）。</summary>
public sealed record QqReplyPart(string Kind, string Value);
