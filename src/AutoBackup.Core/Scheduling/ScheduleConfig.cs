namespace AutoBackup.Core.Scheduling;

public class ScheduleConfig
{
    public required IReadOnlyList<DayOfWeek> Days { get; init; }
    public required TimeOnly Time { get; init; }
}
