namespace openLuo.Modules.WorldState.Core.Models;

/// <summary>Resolved time view consumed by APIs and gameplay logic.</summary>
public sealed class TimeSnapshot
{
    public int Day { get; init; }

    public int Minute { get; init; }

    public string TimeStr { get; init; } = "00:00";

    public bool IsLate { get; init; }

    public TimeMode Mode { get; init; } = TimeMode.Virtual;

    /// <summary>
    /// Epoch milliseconds representation of current time.
    /// For virtual mode this is an internal virtual epoch; for realtime it's Unix epoch.
    /// </summary>
    public long EpochMs { get; init; }
}
