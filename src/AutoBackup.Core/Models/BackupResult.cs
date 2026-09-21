namespace AutoBackup.Core.Models;

public enum BackupOutcome
{
    Success,
    PartialSuccess
}

public class BackupResult
{
    public required BackupOutcome Outcome { get; init; }
    public int FilesCopied { get; init; }
    public int FilesLinked { get; init; }
    public int FilesFailed { get; init; }
    public long BytesCopied { get; init; }
    public List<string> Errors { get; init; } = new();
    public required string SnapshotPath { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime CompletedAt { get; init; }
}
