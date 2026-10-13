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
/// 回合级决策预算（D41/D44）。按回合重置；多 Agent 并发预算实例隔离。
/// 每个字段都必须有真实执行点（值来自配置或 wire，见 <c>DecisionBudgetsDto</c>）——
/// 无消费者的字段一律删除（<c>MaxSkillLoadsPerTurn</c> 曾是这种假旋钮）。
/// </summary>
public sealed class DecisionBudgets
{
    public int MaxDecisions { get; init; } = 8;
    /// <summary>单次决策允许执行的最大工具调用数；超出部分不执行并在轨迹留痕。</summary>
    public int MaxToolCallsPerDecision { get; init; } = 5;
    public int MaxConcurrentTools { get; init; } = 4;
    public TimeSpan OverallDeadline { get; init; } = TimeSpan.FromSeconds(600);
    /// <summary>
    /// 单个工具调用的单步超时（挂死的 MCP/HTTP 工具不再无限阻塞回合——回合总时限只在两次决策之间
    /// 比较时钟，拦不住卡在工具里的调用）。默认 240s：**必须大于最慢的合法工具**
    /// （实测远程 TTS 约 150s），否则会误杀正常调用；<see cref="TimeSpan.Zero"/> 表示关闭。
    /// </summary>
    public TimeSpan StepIdleTimeout { get; init; } = TimeSpan.FromSeconds(240);
    public int MaxToolRetries { get; init; } = 2;

    public static DecisionBudgets Default { get; } = new();
}
