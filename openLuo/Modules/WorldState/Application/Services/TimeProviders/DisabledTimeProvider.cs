using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Application.Services.TimeProviders;

public class DisabledTimeProvider : ITimeProvider
{
    public TimeMode Mode => TimeMode.Disabled;

    public TimeSnapshot GetSnapshot(GameState state, string timezone) =>
        TimeSnapshotBuilder.BuildStateSnapshot(state, TimeMode.Disabled);

    public TimeAdvanceResult Advance(GameState state, int minutes, string source, string timezone, string realtimePolicy) =>
        new()
        {
            Ok = true,
            RequestedMinutes = minutes,
            AppliedMinutes = 0,
            Reason = "disabled_mode_no_op",
            Snapshot = GetSnapshot(state, timezone)
        };
}
