using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Core;

/// <summary>回合完成信息。</summary>
public sealed class TurnCompletion
{
    public string TurnId { get; init; } = string.Empty;
    public string? FinalText { get; init; }
    public IReadOnlyList<OutputItem> Outputs { get; init; } = [];
    public bool Success { get; init; }
    /// <summary>本回合用户原始输入（写入对话历史，含发送者标识）。</summary>
    public string? UserText { get; init; }
    /// <summary>发送者显示名（群聊昵称等）；为空回退到会话主体。</summary>
    public string? SenderName { get; init; }
}

/// <summary>
/// 会话级上下文（D15/D7）。每会话独立实例；快照不可变；
/// 工具结果经 ApplyToolResultsAsync 生成新快照（前线快照推进）。
/// </summary>
public interface IAgentContextSession
{
    string SessionId { get; }
    string SubjectId { get; }
    AgentDecisionContext Current { get; }

    Task<AgentDecisionContext> CreateTurnSnapshotAsync(
        ContextBuildRequest request,
        CancellationToken ct = default);

    Task<AgentDecisionContext> ApplyToolResultsAsync(
        IReadOnlyList<CapabilityCall> calls,
        IReadOnlyList<CapabilityResult> results,
        CancellationToken ct = default);

    Task CommitTurnAsync(
        TurnCompletion completion,
        CancellationToken ct = default);
}
