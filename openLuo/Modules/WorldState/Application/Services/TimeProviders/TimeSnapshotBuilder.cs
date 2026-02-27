using openLuo.Core;
using openLuo.Core.Models;
using openLuo.Modules.WorldState.Core.Models;

namespace openLuo.Modules.WorldState.Application.Services.TimeProviders;

internal static class TimeSnapshotBuilder
{
    public static TimeSnapshot BuildStateSnapshot(GameState state, TimeMode mode)
    {
        var minute = NormalizeMinute(state.CurrentMinute);
        var day = Math.Max(1, state.CurrentDay);
        return new TimeSnapshot
        {
            Day = day,
            Minute = minute,
            TimeStr = FormatTime(minute),
            IsLate = minute >= 1320,
            Mode = mode,
            EpochMs = BuildVirtualEpoch(day, minute)
        };
    }

    public static TimeSnapshot BuildRealtimeSnapshot(GameState state, string timezone)
    {
        var zone = ResolveTimezone(timezone);
        var nowUtc = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone);

        var createdUtc = state.CreatedAt.Kind switch
        {
            DateTimeKind.Utc => state.CreatedAt,
            DateTimeKind.Local => state.CreatedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(state.CreatedAt, DateTimeKind.Utc)
        };

        var createdLocal = TimeZoneInfo.ConvertTime(new DateTimeOffset(createdUtc), zone);
        var day = Math.Max(1, (localNow.Date - createdLocal.Date).Days + 1);
        var minute = localNow.Hour * 60 + localNow.Minute;

        return new TimeSnapshot
        {
            Day = day,
            Minute = minute,
            TimeStr = FormatTime(minute),
            IsLate = minute >= 1320,
            Mode = TimeMode.Realtime,
            EpochMs = localNow.ToUnixTimeMilliseconds()
        };
    }

    public static int NormalizeMinute(int minute)
    {
        if (minute < 0) return 0;
        if (minute >= GameConstants.MinutesPerDay)
            return minute % GameConstants.MinutesPerDay;
        return minute;
    }

    public static string FormatTime(int minute) =>
        $"{minute / 60:D2}:{minute % 60:D2}";

    public static long BuildVirtualEpoch(int day, int minute)
    {
        var safeDay = Math.Max(1, day);
        var safeMinute = NormalizeMinute(minute);
        var totalMinutes = (long)(safeDay - 1) * GameConstants.MinutesPerDay + safeMinute;
        return totalMinutes * 60_000L;
    }

    public static TimeZoneInfo ResolveTimezone(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)
            || timezone.Equals("local", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Local;

        if (timezone.Equals("utc", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }
}
