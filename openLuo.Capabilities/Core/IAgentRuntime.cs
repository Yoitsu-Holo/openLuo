using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>会话开启请求。</summary>
public sealed class SessionOpenRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;      // 角色/Agent 身份
    public string ClientType { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string? ConversationId { get; init; }
}

/// <summary>会话对象（平台适配层持有的句柄）。</summary>
public sealed class AgentSession
{
    public string SessionId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string AgentId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
}

/// <summary>回合请求。</summary>
public sealed class TurnRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string ChannelId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;      // "player" / 角色 id
    public string? SenderName { get; init; }                  // 平台层发送者显示名（群聊昵称等）
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<object>? Blocks { get; init; }
    /// <summary>平台元数据（scene/sender/channel 等），透传到上下文构建的 Extras。</summary>
    public IReadOnlyDictionary<string, object?>? Meta { get; init; }
    public DecisionBudgets? Budgets { get; init; }
}

/// <summary>回合结果（最终回复 + 输出 + 轨迹）。</summary>
public sealed class TurnResult
{
    public bool Success { get; init; }
    public string? FinalText { get; init; }
    public IReadOnlyList<OutputItem> Outputs { get; init; } = [];
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public TerminationReason TerminationReason { get; init; } = TerminationReason.FinalReply;
    public string? TerminationDetail { get; init; }
    public long StateVersion { get; init; }
}

/// <summary>回合事件（流式）。</summary>
public sealed class TurnEvent
{
    public string TurnId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;        // decision | tool_result | output | final
    public object? Payload { get; init; }
}

/// <summary>
/// Agent 运行时门面（平台适配层统一入口）。内核不感知角色/游戏/频道，只感知
/// subjectId + 通用消息流 + 扩展能力。输出经 IOutputQueue 即时推送（D50）。
/// </summary>
public interface IAgentRuntime
{
    Task<AgentSession> OpenSessionAsync(SessionOpenRequest request, CancellationToken ct = default);
    Task<TurnResult> RunTurnAsync(TurnRequest request, CancellationToken ct = default);
    IAsyncEnumerable<TurnEvent> StreamTurnAsync(TurnRequest request, CancellationToken ct = default);

    /// <summary>轻量感知通道：将一条入站消息写入会话历史但不触发 LLM 回合（"看但不回"场景）。</summary>
    Task AppendMessageAsync(string sessionId, string? senderName, string text, IReadOnlyList<object>? blocks = null, CancellationToken ct = default);

    /// <summary>当前会话决策上下文摘要（调试/平台命令用；未开启会话返回 null）。</summary>
    Task<string?> GetContextSummaryAsync(string sessionId, CancellationToken ct = default);
}
