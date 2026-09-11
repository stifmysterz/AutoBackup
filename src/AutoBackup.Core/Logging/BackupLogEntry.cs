namespace AutoBackup.Core.Logging;

public class BackupLogEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Outcome { get; init; }
    public required string Message { get; init; }
}
