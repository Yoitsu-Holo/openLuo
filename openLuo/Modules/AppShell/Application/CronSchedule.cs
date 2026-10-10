namespace openLuo.Modules.AppShell.Application;

/// <summary>
/// 最小 cron 解析（标准 5 段：分 时 日 月 周）。支持 <c>*</c>、<c>N</c>、<c>a-b</c>、<c>*/n</c>、
/// <c>a-b/n</c> 与逗号列表；周三段 0/7 均表示周日。仅用于调度器的一次性/周期触发。
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _days = new bool[32];        // 1..31
    private readonly bool[] _months = new bool[13];      // 1..12
    private readonly bool[] _weekdays = new bool[8];     // 0=Sun … 6=Sat，7 亦作 Sun

    public string Expression { get; }

    private CronSchedule(string expression) => Expression = expression;

    public static bool TryParse(string expression, out CronSchedule? schedule)
    {
        schedule = null;
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 5)
            return false;

        var parsed = new CronSchedule(expression);
        if (!Fill(fields[0], 0, 59, parsed._minutes)
            || !Fill(fields[1], 0, 23, parsed._hours)
            || !Fill(fields[2], 1, 31, parsed._days)
            || !Fill(fields[3], 1, 12, parsed._months)
            || !Fill(fields[4], 0, 7, parsed._weekdays))
            return false;

        if (parsed._weekdays[7])                     // 7 == 周日
            parsed._weekdays[0] = true;

        schedule = parsed;
        return true;
    }

    /// <summary>填充字段集合；支持 * N a-b */n a-b/n 与逗号列表。</summary>
    private static bool Fill(string field, int min, int max, bool[] set)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var step = 1;
            var body = part;

            var slash = body.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(body[(slash + 1)..], out step) || step <= 0)
                    return false;
                body = body[..slash];
            }

            int from, to;
            if (body is "*" or "")
            {
                from = min;
                to = max;
            }
            else
            {
                var dash = body.IndexOf('-');
                if (dash >= 0)
                {
                    if (!int.TryParse(body[..dash], out from) || !int.TryParse(body[(dash + 1)..], out to))
                        return false;
                }
                else
                {
                    if (!int.TryParse(body, out from))
                        return false;
                    to = slash >= 0 ? max : from;   // "N/n" 视为从 N 起按步长
                }
            }

            if (from < min || to > max || from > to)
                return false;

            for (var i = from; i <= to; i += step)
                set[i] = true;
        }

        return Array.IndexOf(set, true) >= 0;
    }

    public bool Matches(DateTimeOffset time) =>
        _minutes[time.Minute]
        && _hours[time.Hour]
        && _days[time.Day]
        && _months[time.Month]
        && _weekdays[(int)time.DayOfWeek];

    /// <summary>下一次命中的时刻（分钟精度；最多向前搜索 366 天）。</summary>
    public DateTimeOffset Next(DateTimeOffset after)
    {
        var candidate = new DateTimeOffset(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Offset)
            .AddMinutes(1);

        var limit = 366 * 24 * 60;
        for (var i = 0; i < limit; i++, candidate = candidate.AddMinutes(1))
        {
            if (Matches(candidate))
                return candidate;
        }
        return after.AddYears(1);
    }
}
