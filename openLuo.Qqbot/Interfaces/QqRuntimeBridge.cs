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
        _ => new("text", $"[{item.Kind.ToString().ToLowerInvariant()}] {item.Payload}")
    };
}

public sealed record QqReplyPart(string Kind, string Value);
