using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>回合终止原因（D41）。</summary>
public enum TerminationReason
{
    FinalReply,
    MaxDecisionsReached,
    OverallTimeout,
    TerminalCapability,
    NoProgress,
    Cancelled,
    EmptyReply
}

/// <summary>单步执行轨迹记录。</summary>
public sealed class AgentToolUseStep
{
    public int Decision { get; init; }
    public string Action { get; init; } = string.Empty;   // call_tool | final_reply | rejected | error
    public string Name { get; init; } = string.Empty;      // CanonicalId 或 "final_text"
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public long DurationMs { get; init; }
}

/// <summary>回合结果。</summary>
public sealed class DecisionLoopResult
{
    public bool Success { get; init; }
    public string? FinalText { get; init; }
    public IReadOnlyList<OutputItem> Outputs { get; init; } = [];
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public int DecisionsUsed { get; init; }
    public TerminationReason TerminationReason { get; init; } = TerminationReason.FinalReply;
    public string? TerminationDetail { get; init; }
}

/// <summary>决策循环请求。</summary>
public sealed class DecisionLoopRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string TurnId { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public CapabilityDecisionContext Context { get; init; } = new();
    public CapabilityCatalogSnapshot Catalog { get; init; } = new();
    public DecisionBudgets Budgets { get; init; } = DecisionBudgets.Default;
    public CapabilityExecutionContext BaseExecutionContext { get; init; } = new();
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>决策循环事件种类（流式）。</summary>
public enum DecisionEventKind
{
    /// <summary>一轮决策开始（Payload: <c>int</c> step）。</summary>
    Decision,
    /// <summary>即将执行的能力调用（Payload: <see cref="CapabilityCall"/>）。</summary>
    ToolCall,
    /// <summary>能力调用结果（Payload: <see cref="CapabilityResult"/>）。</summary>
    ToolResult,
    /// <summary>已即时入队的输出项（Payload: <see cref="OutputItem"/>）。</summary>
    Output,
    /// <summary>回合结束（Payload: <see cref="DecisionLoopResult"/>）。</summary>
    Final
}

/// <summary>决策循环流式事件。<see cref="TurnId"/> 标识所属回合；<see cref="Kind"/> 决定 Payload 类型。</summary>
public sealed class DecisionEvent
{
    public string TurnId { get; init; } = string.Empty;
    public DecisionEventKind Kind { get; init; }
    public object? Payload { get; init; }
}

/// <summary>
/// 决策循环（D2/D17/D18/D41）：
/// 循环调用 ICapabilityDecisionModel → 无 tool_call 非空文本 = 最终回复；
/// 有 tool_call → 校验/调度执行 → 结果回填 → 继续。预算与终止条件统一收口。
/// </summary>
public interface ICapabilityDecisionLoop
{
    /// <summary>执行整个回合，返回聚合结果（非流式）。</summary>
    Task<DecisionLoopResult> RunAsync(DecisionLoopRequest request, CancellationToken ct = default);

    /// <summary>
    /// 执行回合并逐步产出事件（真流式）。事件顺序：<c>Decision → (ToolCall/ToolResult/Output)* → Final</c>。
    /// 循环体顺序 emit（单写者）；消费者可跨迭代安全读取。
    /// </summary>
    IAsyncEnumerable<DecisionEvent> RunStreamAsync(DecisionLoopRequest request, CancellationToken ct = default);
}
