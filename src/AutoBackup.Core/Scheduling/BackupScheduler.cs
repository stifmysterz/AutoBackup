namespace AutoBackup.Core.Scheduling;

public static class BackupScheduler
{
    public static bool IsDueNow(ScheduleConfig config, DateTime now, DateTime? lastRunAt)
    {
        if (!config.Days.Contains(now.DayOfWeek)) return false;

        var nowTime = TimeOnly.FromDateTime(now);
        if (nowTime.Hour != config.Time.Hour || nowTime.Minute != config.Time.Minute) return false;

        if (lastRunAt.HasValue &&
            lastRunAt.Value.Date == now.Date &&
            lastRunAt.Value.Hour == now.Hour &&
            lastRunAt.Value.Minute == now.Minute)
        {
            return false;
        }

        return true;
    }
}
