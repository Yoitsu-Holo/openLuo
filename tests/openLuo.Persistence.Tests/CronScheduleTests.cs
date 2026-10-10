using openLuo.Modules.AppShell.Application;
using Xunit;

namespace openLuo.Persistence.Tests;

/// <summary>调度 cron 解析与下一次触发计算（分 时 日 月 周）。</summary>
public sealed class CronScheduleTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void TryParse_RejectsInvalidExpressions()
    {
        Assert.False(CronSchedule.TryParse("60 * * * *", out _));      // 分越界
        Assert.False(CronSchedule.TryParse("* * * *", out _));         // 段数不足
        Assert.False(CronSchedule.TryParse("* * * * * *", out _));     // 段数过多
        Assert.False(CronSchedule.TryParse("*/0 * * * *", out _));     // 步长必须 > 0
        Assert.True(CronSchedule.TryParse("*/15 * * * *", out _));
    }

    [Fact]
    public void Next_StepExpression_RollsToNextQuarter()
    {
        Assert.True(CronSchedule.TryParse("*/15 * * * *", out var schedule));

        var next = schedule!.Next(At(2026, 1, 2, 10, 7));

        Assert.Equal(At(2026, 1, 2, 10, 15), next);
    }

    [Fact]
    public void Next_DailyExpression_RollsToNextDay()
    {
        Assert.True(CronSchedule.TryParse("0 3 * * *", out var schedule));

        var next = schedule!.Next(At(2026, 1, 2, 10, 7));

        Assert.Equal(At(2026, 1, 3, 3, 0), next);
    }

    [Fact]
    public void Next_WeekdayExpression_LandsOnMonday()
    {
        Assert.True(CronSchedule.TryParse("0 9 * * 1", out var schedule));   // 周一 09:00

        var next = schedule!.Next(At(2026, 1, 2, 10, 7));   // 2026-01-02 是周五

        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
        Assert.Equal(At(2026, 1, 5, 9, 0), next);
    }

    [Fact]
    public void Next_ListExpression_PicksEarliestMatch()
    {
        Assert.True(CronSchedule.TryParse("0,30 8,20 * * *", out var schedule));

        var next = schedule!.Next(At(2026, 1, 2, 8, 45));

        Assert.Equal(At(2026, 1, 2, 20, 0), next);
    }
}
