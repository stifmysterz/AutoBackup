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
    public DateTime? LastRunAt { get; set; }
}
