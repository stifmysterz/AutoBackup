using AutoBackup.Core.Exclusion;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Backup;

public class BackupEngine
{
    public BackupResult RunBackup(IReadOnlyList<string> sourceFolders, string driveRoot, IReadOnlyList<string> customExcludePatterns)
    {
        var startedAt = DateTime.Now;
        var backupRoot = SnapshotPathPlanner.GetBackupRoot(driveRoot);
        Directory.CreateDirectory(backupRoot);

        var previousSnapshot = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);
        var workingPath = SnapshotPathPlanner.CreateNewSnapshotWorkingPath(backupRoot, startedAt);
        Directory.CreateDirectory(workingPath);

        var aliases = SourceAliasMapper.BuildAliases(sourceFolders);
        int copied = 0, linked = 0, failed = 0;
        var errors = new List<string>();

        foreach (var source in sourceFolders)
        {
            if (!Directory.Exists(source)) continue;
            var alias = aliases[source];
            CopyDirectory(new DirectoryInfo(source), alias, workingPath, previousSnapshot, customExcludePatterns, ref copied, ref linked, ref failed, errors);
        }

        var finalPath = SnapshotPathPlanner.GetFinalPath(workingPath);

        // Handle the case where the final path already exists (same minute timestamp)
        int counter = 0;
        var targetPath = finalPath;
        while (Directory.Exists(targetPath))
        {
            targetPath = $"{finalPath}_{counter}";
            counter++;
        }

        Directory.Move(workingPath, targetPath);

        var outcome = failed > 0 ? BackupOutcome.PartialSuccess : BackupOutcome.Success;
        return new BackupResult
        {
            Outcome = outcome,
            FilesCopied = copied,
            FilesLinked = linked,
            FilesFailed = failed,
            Errors = errors,
            SnapshotPath = targetPath,
            StartedAt = startedAt,
            CompletedAt = DateTime.Now
        };
    }

    private static void CopyDirectory(
        DirectoryInfo sourceDir,
        string relativePath,
        string workingRoot,
        string? previousSnapshotRoot,
        IReadOnlyList<string> customExcludePatterns,
        ref int copied,
        ref int linked,
        ref int failed,
        List<string> errors)
    {
        var destDir = Path.Combine(workingRoot, relativePath);
        Directory.CreateDirectory(destDir);

        foreach (var subDir in sourceDir.GetDirectories())
        {
            if (ExclusionRules.ShouldExclude(subDir.FullName, subDir.Attributes, customExcludePatterns)) continue;
            CopyDirectory(subDir, Path.Combine(relativePath, subDir.Name), workingRoot, previousSnapshotRoot, customExcludePatterns, ref copied, ref linked, ref failed, errors);
        }

        foreach (var file in sourceDir.GetFiles())
        {
            if (ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns)) continue;

            var destFile = Path.Combine(destDir, file.Name);
            var previousFile = previousSnapshotRoot == null ? null : Path.Combine(previousSnapshotRoot, relativePath, file.Name);

            try
            {
                if (previousFile != null && File.Exists(previousFile) && IsUnchanged(file, previousFile) &&
                    HardLinkHelper.TryCreateHardLink(destFile, previousFile, out _))
                {
                    linked++;
                    continue;
                }

                File.Copy(file.FullName, destFile, overwrite: true);
                var destLength = new FileInfo(destFile).Length;
                if (destLength != file.Length)
                {
                    failed++;
                    errors.Add($"{file.FullName}: 复制后大小不一致 ({destLength} != {file.Length})");
                }
                else
                {
                    copied++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{file.FullName}: {ex.Message}");
            }
        }
    }

    private static bool IsUnchanged(FileInfo source, string previousFilePath)
    {
        var previous = new FileInfo(previousFilePath);
        return source.Length == previous.Length && source.LastWriteTimeUtc == previous.LastWriteTimeUtc;
    }
}
