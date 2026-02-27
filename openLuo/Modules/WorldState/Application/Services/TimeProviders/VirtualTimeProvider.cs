using openLuo.Core;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Interfaces;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Application.Services.TimeProviders;

public class VirtualTimeProvider : ITimeProvider
{
    public TimeMode Mode => TimeMode.Virtual;

    public TimeSnapshot GetSnapshot(GameState state, string timezone) =>
        TimeSnapshotBuilder.BuildStateSnapshot(state, TimeMode.Virtual);

    public TimeAdvanceResult Advance(GameState state, int minutes, string source, string timezone, string realtimePolicy)
    {
        if (minutes <= 0)
        {
            return new TimeAdvanceResult
            {
                Ok = false,
                RequestedMinutes = minutes,
                AppliedMinutes = 0,
                Reason = "minutes_must_be_positive",
                Snapshot = GetSnapshot(state, "local")
            };
        }

        state.CurrentMinute += minutes;
        while (state.CurrentMinute >= GameConstants.MinutesPerDay)
        {
            state.CurrentMinute -= GameConstants.MinutesPerDay;
            state.CurrentDay++;
        }

        return new TimeAdvanceResult
        {
            Ok = true,
            RequestedMinutes = minutes,
            AppliedMinutes = minutes,
            Reason = "applied",
            Snapshot = GetSnapshot(state, "local")
        };
    }
}
