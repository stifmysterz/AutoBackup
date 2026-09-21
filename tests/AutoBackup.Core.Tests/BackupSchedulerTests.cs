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
    public void IsDueNow_True_AtTheScheduledMinute_WhenNeverRun()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 0); // a Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccessfulRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenWrongDay()
    {
        var now = new DateTime(2026, 9, 15, 22, 0, 0); // a Tuesday
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccessfulRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_BeforeTheScheduledTime()
    {
        var now = new DateTime(2026, 9, 14, 21, 59, 0);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccessfulRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenAlreadySucceededAfterTodaysScheduledTime()
    {
        var now = new DateTime(2026, 9, 14, 22, 30, 0);
        var lastSuccess = new DateTime(2026, 9, 14, 22, 0, 5);
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccess));
    }

    [Fact]
    public void IsDueNow_True_WhenLastSuccessWasAnEarlierDay()
    {
        var now = new DateTime(2026, 9, 14, 22, 0, 30);
        var lastSuccess = new DateTime(2026, 9, 7, 22, 0, 5); // previous Monday
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccess));
    }

    // The catch-up behaviour: the old scheduler only fired during the exact scheduled minute,
    // so a PC that was asleep, or a backup drive plugged in late, silently lost that day's run.
    [Fact]
    public void IsDueNow_True_LaterTheSameDay_WhenScheduledTimeWasMissed()
    {
        var now = new DateTime(2026, 9, 14, 23, 47, 0); // nearly two hours late
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccessfulRunAt: null));
    }

    [Fact]
    public void IsDueNow_True_WhenTodaysOnlySuccessWasBeforeTheScheduledTime()
    {
        // A manual backup in the morning must not count as today's scheduled run.
        var now = new DateTime(2026, 9, 14, 22, 5, 0);
        var lastSuccess = new DateTime(2026, 9, 14, 9, 30, 0);
        Assert.True(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccess));
    }

    [Fact]
    public void IsDueNow_False_OnANonScheduledDay_EvenIfYesterdaysRunWasMissed()
    {
        // Catch-up is same-day only: waking up on Tuesday must not fire Monday's missed run,
        // otherwise an unchecked day would still get backups.
        var now = new DateTime(2026, 9, 15, 10, 0, 0); // Tuesday
        Assert.False(BackupScheduler.IsDueNow(MondayAt2200, now, lastSuccessfulRunAt: null));
    }

    [Fact]
    public void IsDueNow_False_WhenNoDaysAreScheduled()
    {
        var config = new ScheduleConfig { Days = new List<DayOfWeek>(), Time = new TimeOnly(22, 0) };
        var now = new DateTime(2026, 9, 14, 22, 0, 0);
        Assert.False(BackupScheduler.IsDueNow(config, now, lastSuccessfulRunAt: null));
    }
}
