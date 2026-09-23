namespace openLuo.AgentContext.Core.Models;

/// <summary>
/// 对话消息（D35）。Metadata 为 EnhanceChat 存储层键值对，永不进入正文；
/// 渲染由标签管道在序列化点完成（D43）。
/// </summary>
public sealed class ConversationTurn
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string SpeakerId { get; init; } = string.Empty;
    public string SpeakerName { get; init; } = string.Empty;
    public string SpeakerRole { get; init; } = string.Empty;   // user | agent | system | outbound
    public string Content { get; init; } = string.Empty;
    /// <summary>工具消息引用的模型 tool_call id（仅内存，不持久化）。</summary>
    public string? ToolCallId { get; init; }
    /// <summary>assistant 消息的 tool_calls 声明 JSON（[{id,type,function:{name,arguments}}]，仅内存）。</summary>
    public string? ToolCallsJson { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public int? GameDay { get; init; }
    public int? GameMinute { get; init; }
    public IReadOnlyList<object>? Blocks { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 对话存储端口（D35）。内核只定义契约；宿主提供 SQLite 实现。
/// </summary>
public interface IConversationStore
{
    Task<IReadOnlyList<ConversationTurn>> GetRecentAsync(
        string sessionId, int limit, CancellationToken ct = default);

    Task AppendAsync(ConversationTurn turn, CancellationToken ct = default);
}
