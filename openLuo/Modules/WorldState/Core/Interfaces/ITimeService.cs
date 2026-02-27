using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Unified time kernel service for all gameplay time operations.</summary>
public interface ITimeService
{
    /// <summary>Get current resolved time snapshot.</summary>
    Task<TimeSnapshot?> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>Get current resolved time snapshot for explicit game id.</summary>
    Task<TimeSnapshot?> GetSnapshotAsync(string gameId, CancellationToken ct = default);

    /// <summary>Advance time by minutes according to current mode policy.</summary>
    Task<TimeAdvanceResult> AdvanceAsync(int minutes, string source = "unknown", CancellationToken ct = default);

    /// <summary>Advance time by minutes for explicit game id.</summary>
    Task<TimeAdvanceResult> AdvanceAsync(string gameId, int minutes, string source = "unknown", CancellationToken ct = default);

    /// <summary>Process periodic synchronization tick.</summary>
    Task<TimeSnapshot?> TickAsync(CancellationToken ct = default);

    /// <summary>Process periodic synchronization tick for explicit game id.</summary>
    Task<TimeSnapshot?> TickAsync(string gameId, CancellationToken ct = default);
}
