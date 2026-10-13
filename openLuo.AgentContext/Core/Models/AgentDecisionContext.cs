using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.AgentContext.Core.Models;

/// <summary>
/// 完整决策上下文（D16/D42）。由 ContextAssembler 生成，宿主转换为
/// openLuo.Capabilities 的最小 CapabilityDecisionContext 后交给决策循环。
/// </summary>
public sealed record AgentDecisionContext
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public long SnapshotVersion { get; init; }
    public IReadOnlyList<ContextContribution> Contributions { get; init; } = [];
    public IReadOnlyList<ContextSourceState> SourceStates { get; init; } = [];
    public IReadOnlyList<ConversationTurn> Conversation { get; init; } = [];
    public IReadOnlyList<CapabilitySummary> Capabilities { get; init; } = [];
    public IReadOnlyList<SkillSummary> Skills { get; init; } = [];
    public IReadOnlyList<WorkflowSummary> Workflows { get; init; } = [];
    public IReadOnlyList<RemoteAgentSummary> RemoteAgents { get; init; } = [];
    public DecisionBudgets Budgets { get; init; } = DecisionBudgets.Default;
    /// <summary>本轮 token 预算账目（D44）：上限、系统块/对话占用、被裁明细。</summary>
    public ContextBudgetSnapshot Budget { get; init; } = ContextBudgetSnapshot.Empty;
    /// <summary>本轮全局块缓存账目（D46）：复用/重算了几块。</summary>
    public ContextCacheStats Cache { get; init; } = ContextCacheStats.Empty;
    public string? UserInput { get; init; }
    public IReadOnlyList<object>? UserBlocks { get; init; }
}
