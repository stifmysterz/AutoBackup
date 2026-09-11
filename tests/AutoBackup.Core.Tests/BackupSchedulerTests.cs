using AutoBackup.Core.Scheduling;

namespace AutoBackup.Core.Tests;

public class BackupSchedulerTests
{
    private static readonly ScheduleConfig MondayAt2200 = new()
    {
        Days = new List<DayOfWeek> { DayOfWeek.Monday },
        Time = new TimeOnly(22, 0)
    };

    [Fact]
    public void IsDueNow_True_WhenDayAndMinuteMatchAndNeverRun()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 0); // a Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenWrongDay()
    {
        var now = new DateTime(2026, 9, 15, 22, 0, 0); // a Tuesday
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenWrongTime()
    {
        var now = new DateTime(2026, 9, 14, 21, 59, 0);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenAlreadyRanThisMinute()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 30);
        var lastRunAt = new DateTime(2026, 9, 14, 22, 0, 5);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt));
    }

    [Fact]
    public void IsDueNow_True_WhenLastRunWasADifferentMinute()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 30);
        var lastRunAt = new DateTime(2026, 9, 7, 22, 0, 5); // previous Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastRunAt));
    }
}
