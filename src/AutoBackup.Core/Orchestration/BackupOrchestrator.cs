using AutoBackup.Core.Backup;
using AutoBackup.Core.Drives;
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

    public OrchestrationResult RunOnce(BackupSettings settings)
    {
        var sourceFolders = settings.SourceFolders.Where(Directory.Exists).ToList();
        if (sourceFolders.Count == 0)
            return new OrchestrationResult { Outcome = OrchestrationOutcome.NoSourceFolders, Message = "没有有效的源文件夹" };

        if (string.IsNullOrEmpty(settings.TargetVolumeSerial))
        {
            LogSkip("尚未绑定备份硬盘");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "尚未绑定备份硬盘" };
        }

        var targetDrive = DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), settings.TargetVolumeSerial);
        if (targetDrive == null)
        {
            LogSkip("备份硬盘未连接");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.DriveNotConnected, Message = "备份硬盘未连接" };
        }

        var backupRoot = SnapshotPathPlanner.GetBackupRoot(targetDrive.DriveLetter);
        if (OverlapChecker.HasOverlap(sourceFolders, backupRoot))
        {
            LogSkip("源文件夹与备份目标重叠，已阻止本次备份");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.SourceTargetOverlap, Message = "源文件夹与备份目标重叠，已阻止本次备份" };
        }

        var estimated = SpaceChecker.EstimateSourceSizeBytes(sourceFolders, settings.CustomExcludePatterns);
        if (!SpaceChecker.HasEnoughSpace(estimated, targetDrive.FreeBytes))
        {
            LogSkip("硬盘剩余空间不足，已跳过本次备份");
            return new OrchestrationResult { Outcome = OrchestrationOutcome.InsufficientSpace, Message = "硬盘剩余空间不足，已跳过本次备份" };
        }

        var engine = new BackupEngine();
        var backupResult = engine.RunBackup(sourceFolders, targetDrive.DriveLetter, settings.CustomExcludePatterns);

        RetentionCleaner.CleanOldSnapshots(backupRoot, settings.RetentionDays, DateTime.Now);

        var outcome = backupResult.Outcome == BackupOutcome.Success ? OrchestrationOutcome.Success : OrchestrationOutcome.PartialSuccess;
        var message = $"复制 {backupResult.FilesCopied}，硬链接 {backupResult.FilesLinked}，失败 {backupResult.FilesFailed}";
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = outcome.ToString(), Message = message });

        return new OrchestrationResult { Outcome = outcome, BackupResult = backupResult, Message = message };
    }

    private void LogSkip(string message) =>
        _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Skipped", Message = message });
}
