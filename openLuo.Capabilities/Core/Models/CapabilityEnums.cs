namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 能力来源类型。决定执行分发的路由目标，但不改变统一的能力目录抽象。
/// </summary>
public enum CapabilityKind
{
    Builtin,
    Mcp,
    Workflow,
    RemoteAgent
}

/// <summary>
/// 副作用等级。决定并行、mutation 与确认策略。
/// </summary>
public enum SideEffectClass
{
    Pure,
    ReadOnly,
    External,
    Mutation,
    Delegation
}

/// <summary>
/// 完成策略：工具执行后如何影响回合结束。
/// </summary>
public enum CompletionPolicy
{
    Continue,
    Terminal
}

/// <summary>
/// 风险等级：影响确认与策略拦截。
/// </summary>
public enum RiskLevel
{
    Low,
    Medium,
    High
}

/// <summary>
/// 幂等性声明：决定重试是否安全。
/// </summary>
public enum IdempotencyKind
{
    Idempotent,
    NonIdempotent,
    Unknown
}
