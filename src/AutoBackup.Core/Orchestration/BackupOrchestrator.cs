using AutoBackup.Core.Backup;
using AutoBackup.Core.Drives;
using AutoBackup.Core.Formatting;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Orchestration;

public class BackupOrchestrator
{
    private readonly IDriveScanner _driveScanner;
    private readonly BackupLogger _logger;

    public BackupOrchestrator(IDriveScanner driveScanner, BackupLogger logger)
    {
        _driveScanner = driveScanner;
        _logger = logger;
    }

    /// <param name="logSkips">Set false for repeated catch-up attempts, which retry every
    /// minute until the drive reappears - logging each one would bury the log in duplicates.</param>
    /// <param name="progress">Receives the running count of files processed, so a caller can
    /// show that a long backup is alive rather than hung.</param>
    public OrchestrationResult RunOnce(BackupSettings settings, bool logSkips = true, IProgress<int>? progress = null)
    {
        _logger.RotateIfNeeded(maxSizeBytes: 5 * 1024 * 1024, maxAgeDays: 90, now: DateTime.Now);

        var sourceFolders = settings.SourceFolders.Where(Directory.Exists).ToList();
        if (sourceFolders.Count == 0)
            return new OrchestrationResult { Outcome = OrchestrationOutcome.NoSourceFolders, Message = "没有有效的源文件夹" };

        if (string.IsNullOrEmpty(settings.TargetVolumeSerial))
        {
            LogSkip("尚未绑定备份硬盘", logSkips);
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "尚未绑定备份硬盘" };
        }

        var targetDrive = DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), settings.TargetVolumeSerial);
        if (targetDrive == null)
        {
            LogSkip("备份硬盘未连接", logSkips);
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "备份硬盘未连接" };
        }

        var backupRoot = SnapshotPathPlanner.GetBackupRoot(targetDrive.DriveLetter);
        if (OverlapChecker.HasOverlap(sourceFolders, backupRoot))
        {
            LogSkip("源文件夹与备份目标重叠，已阻止本次备份", logSkips);
            return new OrchestrationResult { Outcome = OrchestrationOutcome.SourceTargetOverlap, Message = "源文件夹与备份目标重叠，已阻止本次备份" };
        }

        var estimated = SpaceChecker.EstimateSourceSizeBytes(sourceFolders, settings.CustomExcludePatterns);
        var previousSnapshot = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);
        if (previousSnapshot != null)
        {
            // Real backups after the first are incremental (unchanged files are hardlinked,
            // not re-copied), so comparing against the total source size would permanently
            // block backups once free space drops below that total. Approximate the
            // incremental delta by subtracting what the previous snapshot already holds.
            var alreadyBacked = SpaceChecker.GetSnapshotSizeBytes(previousSnapshot);
            estimated = Math.Max(0, estimated - alreadyBacked);
        }
        if (!SpaceChecker.HasEnoughSpace(estimated, targetDrive.FreeBytes))
        {
            LogSkip("硬盘剩余空间不足，已跳过本次备份", logSkips);
            return new OrchestrationResult { Outcome = OrchestrationOutcome.InsufficientSpace, Message = "硬盘剩余空间不足，已跳过本次备份" };
        }

        var engine = new BackupEngine();
        var backupResult = engine.RunBackup(sourceFolders, targetDrive.DriveLetter, settings.CustomExcludePatterns, progress: progress);

        // Cleanup runs after the data is already safely on disk, so a failure here is a
        // housekeeping problem, not a backup failure. Letting it throw would report a
        // successful backup to the user as an error.
        try
        {
            var cleanupFailures = RetentionCleaner.CleanOldSnapshots(backupRoot, settings.RetentionDays, DateTime.Now);
            foreach (var failure in cleanupFailures)
                _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "CleanupFailed", Message = failure });
        }
        catch (Exception ex)
        {
            _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "CleanupFailed", Message = ex.Message });
        }

        var outcome = backupResult.Outcome == BackupOutcome.Success ? OrchestrationOutcome.Success : OrchestrationOutcome.PartialSuccess;
        var message = $"复制 {backupResult.FilesCopied} ({ByteSizeFormatter.Format(backupResult.BytesCopied)})，硬链接 {backupResult.FilesLinked}，失败 {backupResult.FilesFailed}";
        if (backupResult.Errors.Count > 0)
        {
            var shown = string.Join("; ", backupResult.Errors.Take(5));
            var suffix = backupResult.Errors.Count > 5 ? $"...(共{backupResult.Errors.Count}个)" : string.Empty;
            message += $"；{shown}{suffix}";
        }
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = outcome.ToString(), Message = message });

        return new OrchestrationResult { Outcome = outcome, BackupResult = backupResult, Message = message };
    }

    /// <summary>
    /// Prunes expired snapshots without running a backup. Safe to call frequently (e.g. every
    /// scheduler tick) so retention cleanup no longer depends on a backup actually succeeding -
    /// a backup that keeps getting skipped (drive not connected, wrong day) would otherwise
    /// leave expired snapshots sitting on disk indefinitely.
    /// </summary>
    /// <returns>true if the target drive was connected and cleanup ran; false if there was
    /// nothing to do (no drive configured, or it isn't connected right now).</returns>
    public bool RunCleanupOnly(BackupSettings settings)
    {
        if (string.IsNullOrEmpty(settings.TargetVolumeSerial)) return false;

        var targetDrive = DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), settings.TargetVolumeSerial);
        if (targetDrive == null) return false;

        var backupRoot = SnapshotPathPlanner.GetBackupRoot(targetDrive.DriveLetter);
        RetentionCleaner.CleanOldSnapshots(backupRoot, settings.RetentionDays, DateTime.Now);
        return true;
    }

    private void LogSkip(string message, bool logSkips)
    {
        if (!logSkips) return;
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Skipped", Message = message });
    }
}
