using AutoBackup.Core.Exclusion;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Backup;

public class BackupEngine
{
    public BackupResult RunBackup(IReadOnlyList<string> sourceFolders, string driveRoot, IReadOnlyList<string> customExcludePatterns, DateTime? now = null)
    {
        var startedAt = now ?? DateTime.Now;
        var backupRoot = SnapshotPathPlanner.GetBackupRoot(driveRoot);
        Directory.CreateDirectory(backupRoot);

        var previousSnapshot = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);
        var workingPath = SnapshotPathPlanner.CreateNewSnapshotWorkingPath(backupRoot, startedAt);

        // A stale .inprogress folder can be left behind by a previous interrupted/collided
        // run. It may contain hardlinks into the previous real snapshot, so we must not let
        // File.Copy's overwrite mutate through them - start every run from a genuinely clean
        // working folder. Deleting a folder only removes its own references to shared
        // hardlinked files, never the underlying data in another snapshot (same reasoning as
        // RetentionCleaner uses when pruning old snapshots).
        if (Directory.Exists(workingPath))
        {
            Directory.Delete(workingPath, recursive: true);
        }
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

        // Two backups triggered within the same clock minute would otherwise collide here:
        // Directory.Move throws IOException because finalPath already exists, and the just
        // -built workingPath is left behind as a stale .inprogress folder for the next run.
        // Treat "a snapshot already exists for this minute" as an already-completed backup.
        if (Directory.Exists(finalPath))
        {
            try { Directory.Delete(workingPath, recursive: true); } catch { /* best effort cleanup */ }
            return new BackupResult
            {
                Outcome = BackupOutcome.Success,
                FilesCopied = 0,
                FilesLinked = 0,
                FilesFailed = 0,
                Errors = new List<string> { "备份在本分钟内已完成，跳过重复快照" },
                SnapshotPath = finalPath,
                StartedAt = startedAt,
                CompletedAt = DateTime.Now
            };
        }

        Directory.Move(workingPath, finalPath);

        var outcome = failed > 0 ? BackupOutcome.PartialSuccess : BackupOutcome.Success;
        return new BackupResult
        {
            Outcome = outcome,
            FilesCopied = copied,
            FilesLinked = linked,
            FilesFailed = failed,
            Errors = errors,
            SnapshotPath = finalPath,
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

        DirectoryInfo[] subDirs;
        FileInfo[] files;
        try
        {
            subDirs = sourceDir.GetDirectories();
            files = sourceDir.GetFiles();
        }
        catch (Exception ex)
        {
            // Can't enumerate this directory (permission denied, disappeared mid-run, etc).
            // Record the failure and let sibling directories still get processed.
            failed++;
            errors.Add($"{sourceDir.FullName}: {ex.Message}");
            return;
        }

        foreach (var subDir in subDirs)
        {
            bool exclude;
            try
            {
                exclude = ExclusionRules.ShouldExclude(subDir.FullName, subDir.Attributes, customExcludePatterns);
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{subDir.FullName}: {ex.Message}");
                continue;
            }
            if (exclude) continue;

            CopyDirectory(subDir, Path.Combine(relativePath, subDir.Name), workingRoot, previousSnapshotRoot, customExcludePatterns, ref copied, ref linked, ref failed, errors);
        }

        foreach (var file in files)
        {
            bool exclude;
            try
            {
                exclude = ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns);
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{file.FullName}: {ex.Message}");
                continue;
            }
            if (exclude) continue;

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

                // Never rely on File.Copy's overwrite: if destFile already exists as a
                // hardlink into a previous snapshot, overwrite-in-place would mutate that
                // shared, supposedly-immutable historical file. Deleting the link first only
                // removes this specific name - it can never touch the underlying content
                // referenced by another snapshot's hardlink to the same file.
                if (File.Exists(destFile))
                {
                    File.Delete(destFile);
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
