namespace openLuo.Capabilities.Core;

/// <summary>
/// 内核时钟抽象（D21/D47）。默认实现为 UTC wall-clock；领域虚拟时钟后续可替换。
/// 内核仅用 IClock 做超时/重试/deadline，不注入领域时间。
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// 回合级决策预算（D41）。按回合重置；多 Agent 并发预算实例隔离。
/// </summary>
public sealed class DecisionBudgets
{
    public int MaxDecisions { get; init; } = 8;
    public int MaxToolCallsPerDecision { get; init; } = 5;
    public int MaxConcurrentTools { get; init; } = 4;
    public TimeSpan OverallDeadline { get; init; } = TimeSpan.FromSeconds(600);
    public TimeSpan StepIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxToolRetries { get; init; } = 2;
    public int MaxSkillLoadsPerTurn { get; init; } = 3;

    public static DecisionBudgets Default { get; } = new();
}
