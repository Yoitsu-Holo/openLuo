namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>Result of a time advance request.</summary>
public sealed class TimeAdvanceResult
{
    public bool Ok { get; init; } = true;

    public int RequestedMinutes { get; init; }

    public int AppliedMinutes { get; init; }

    public string Reason { get; init; } = string.Empty;

    public TimeSnapshot Snapshot { get; init; } = new();
}
