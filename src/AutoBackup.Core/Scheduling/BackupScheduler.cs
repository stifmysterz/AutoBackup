namespace AutoBackup.Core.Scheduling;

public static class BackupScheduler
{
    /// <summary>
    /// A backup is due when today is a scheduled day, the scheduled time has arrived or
    /// passed, and no backup has succeeded since that time today.
    /// </summary>
    /// <remarks>
    /// The "or passed" half is what makes a missed run catch up. Matching only the exact
    /// scheduled minute meant a PC that was asleep, or a backup drive plugged in late, lost
    /// that day's backup entirely - and silently, since nothing ever ran to report it.
    /// Catch-up deliberately stays within the same day: firing a missed Monday run on Tuesday
    /// would back up on days the user explicitly unchecked.
    /// </remarks>
    public static bool IsDueNow(ScheduleConfig config, DateTime now, DateTime? lastSuccessfulRunAt)
    {
        if (!config.Days.Contains(now.DayOfWeek)) return false;

        var dueAt = now.Date.Add(config.Time.ToTimeSpan());
        if (now < dueAt) return false;

        return lastSuccessfulRunAt == null || lastSuccessfulRunAt.Value < dueAt;
    }
}
