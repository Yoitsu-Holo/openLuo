using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 最小决策上下文（D18）。宿主由 AgentDecisionContext 转换为本类型；
/// openLuo.Capabilities 不认识 Memory/WorldState/Character 等宿主类型。
/// </summary>
public sealed class CapabilityDecisionContext
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public long SnapshotVersion { get; init; }

    /// <summary>系统/增强消息（已由 LLM 适配层序列化好）。</summary>
    public IReadOnlyList<string> SystemBlocks { get; init; } = [];

    /// <summary>对话消息（含渲染后的时间/标签标记）。</summary>
    public IReadOnlyList<ContextMessage> Conversation { get; init; } = [];

    /// <summary>当前用户输入。</summary>
    public string? UserInput { get; init; }

    /// <summary>当前用户输入附带的多模态块（图片等），随 UserInput 一起送 LLM。</summary>
    public IReadOnlyList<object>? UserBlocks { get; init; }

    /// <summary>能力摘要（注入上下文用）。</summary>
    public IReadOnlyList<CapabilitySummary> Capabilities { get; init; } = [];

    /// <summary>Skill 摘要。</summary>
    public IReadOnlyList<SkillSummary> Skills { get; init; } = [];

    /// <summary>Workflow 摘要。</summary>
    public IReadOnlyList<WorkflowSummary> Workflows { get; init; } = [];

    /// <summary>RemoteAgent 摘要。</summary>
    public IReadOnlyList<RemoteAgentSummary> RemoteAgents { get; init; } = [];

    public DecisionBudgets Budgets { get; init; } = DecisionBudgets.Default;
}

/// <summary>单条上下文消息（角色 + 内容 + 可选块）。</summary>
public sealed class ContextMessage
{
    public string Role { get; init; } = "user";      // system | user | assistant | tool
    public string Content { get; init; } = string.Empty;
    public string? ToolCallId { get; init; }
    /// <summary>assistant 消息的 tool_calls 声明 JSON（[{id,type,function:{name,arguments}}]）。</summary>
    public string? ToolCallsJson { get; init; }
    public IReadOnlyList<object>? Blocks { get; init; }
}

/// <summary>能力摘要（注入上下文，D30）。</summary>
public sealed class CapabilitySummary
{
    public string CanonicalId { get; init; } = string.Empty;
    public string ModelToolName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Usage { get; init; } = string.Empty;
    /// <summary>工具输入 JSON Schema（原生 tools 参数声明用）。</summary>
    public object InputSchema { get; init; } = new();
}

/// <summary>Skill 摘要（D12/D30）。</summary>
public sealed class SkillSummary
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WhenToUse { get; init; } = string.Empty;
}

/// <summary>Workflow 摘要（D13）。</summary>
public sealed class WorkflowSummary
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>RemoteAgent 摘要。</summary>
public sealed class RemoteAgentSummary
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
