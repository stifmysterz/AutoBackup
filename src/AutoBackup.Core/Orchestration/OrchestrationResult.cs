using AutoBackup.Core.Models;

namespace AutoBackup.Core.Orchestration;

public enum OrchestrationOutcome
{
    Success,
    PartialSuccess,
    DriveNotConnected,
    InsufficientSpace,
    SourceTargetOverlap,
    NoSourceFolders
}

public class OrchestrationResult
{
    public required OrchestrationOutcome Outcome { get; init; }
    public BackupResult? BackupResult { get; init; }
    public required string Message { get; init; }
}
