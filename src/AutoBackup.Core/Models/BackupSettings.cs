namespace AutoBackup.Core.Models;

public class BackupSettings
{
    public List<string> SourceFolders { get; set; } = new();
    public string? TargetVolumeSerial { get; set; }
    public string? TargetVolumeLabel { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = new();
    public TimeOnly ScheduleTime { get; set; } = new TimeOnly(22, 0);
    public int RetentionDays { get; set; } = 30;
    public List<string> CustomExcludePatterns { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
}
