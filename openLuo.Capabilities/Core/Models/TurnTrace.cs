namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 回合执行轨迹（1.13）。记录每轮决策、调用、mutation、输出与预算消耗。
/// </summary>
public sealed class TurnTrace
{
    public string TurnId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public IReadOnlyList<AgentToolUseStep> Steps { get; init; } = [];
    public IReadOnlyList<string> MutationOutcomes { get; init; } = [];
    public IReadOnlyList<string> OutputItemIds { get; init; } = [];
    public int DecisionsUsed { get; init; }
    public TimeSpan Duration { get; init; }
    public string TerminationReason { get; init; } = string.Empty;
    public string? TerminationDetail { get; init; }
}

/// <summary>trace 收集器（宿主可注入以实现日志/持久化）。</summary>
public interface ITraceSink
{
    Task RecordTurnAsync(TurnTrace trace, CancellationToken ct = default);
}
