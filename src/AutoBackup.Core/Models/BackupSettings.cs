namespace AutoBackup.Core.Models;

public class BackupSettings
{
    public List<string> SourceFolders { get; set; } = new()
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    };
    public string? TargetVolumeSerial { get; set; }
    public string? TargetVolumeLabel { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };
    public TimeOnly ScheduleTime { get; set; } = new TimeOnly(22, 0);
    public int RetentionDays { get; set; } = 30;
    public List<string> CustomExcludePatterns { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// When a backup last actually completed. Only successful (or partially successful) runs
    /// update it - a run that was skipped because the drive was absent must not look like a
    /// backup in the tray tooltip, and must not satisfy today's schedule.
    /// </summary>
    public DateTime? LastSuccessfulRunAt { get; set; }
}
