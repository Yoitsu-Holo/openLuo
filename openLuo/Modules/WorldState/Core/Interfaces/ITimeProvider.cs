using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Core.Interfaces;

/// <summary>Strategy provider for a specific runtime time mode.</summary>
public interface ITimeProvider
{
    TimeMode Mode { get; }

    TimeSnapshot GetSnapshot(GameState state, string timezone);

    TimeAdvanceResult Advance(GameState state, int minutes, string source, string timezone, string realtimePolicy);
}
