using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Application.Services.TimeProviders;

public class RealtimeTimeProvider : ITimeProvider
{
    public TimeMode Mode => TimeMode.Realtime;

    public TimeSnapshot GetSnapshot(GameState state, string timezone) =>
        TimeSnapshotBuilder.BuildRealtimeSnapshot(state, timezone);

    public TimeAdvanceResult Advance(GameState state, int minutes, string source, string timezone, string realtimePolicy)
    {
        var snapshot = GetSnapshot(state, timezone);
        var reason = realtimePolicy.Equals("no_op", StringComparison.OrdinalIgnoreCase)
            ? "realtime_mode_no_op"
            : "realtime_mode_policy_not_supported";

        return new TimeAdvanceResult
        {
            Ok = true,
            RequestedMinutes = minutes,
            AppliedMinutes = 0,
            Reason = reason,
            Snapshot = snapshot
        };
    }
}
