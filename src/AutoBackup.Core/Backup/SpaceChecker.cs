using AutoBackup.Core.Exclusion;

namespace AutoBackup.Core.Backup;

public static class SpaceChecker
{
    public static long EstimateSourceSizeBytes(IReadOnlyList<string> sourceFolders, IReadOnlyList<string> customExcludePatterns)
    {
        long total = 0;
        foreach (var source in sourceFolders)
        {
            if (!Directory.Exists(source)) continue;
            total += SumDirectory(new DirectoryInfo(source), customExcludePatterns);
        }
        return total;
    }

    public static bool HasEnoughSpace(long estimatedBytes, long availableFreeBytes) => availableFreeBytes >= estimatedBytes;

    /// <summary>
    /// Sums the total size of an already-completed snapshot folder. Used to turn the
    /// full-source-size estimate into an approximation of the incremental delta still needed.
    /// </summary>
    public static long GetSnapshotSizeBytes(string snapshotPath)
    {
        if (!Directory.Exists(snapshotPath)) return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(snapshotPath, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; } catch { /* skip unreadable file */ }
        }
        return total;
    }

    private static long SumDirectory(DirectoryInfo dir, IReadOnlyList<string> customExcludePatterns)
    {
        long total = 0;

        FileInfo[] files;
        DirectoryInfo[] subDirs;
        try
        {
            files = dir.GetFiles();
            subDirs = dir.GetDirectories();
        }
        catch
        {
            // This is only a pre-check size estimate, not the backup itself - if a subtree
            // can't be enumerated (permission denied, etc), fail safe by skipping it rather
            // than crashing the whole estimate.
            return 0;
        }

        foreach (var file in files)
        {
            try
            {
                if (ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns)) continue;
                total += file.Length;
            }
            catch
            {
                // skip unreadable file
            }
        }
        foreach (var sub in subDirs)
        {
            try
            {
                if (ExclusionRules.ShouldExclude(sub.FullName, sub.Attributes, customExcludePatterns)) continue;
            }
            catch
            {
                continue;
            }
            total += SumDirectory(sub, customExcludePatterns);
        }
        return total;
    }
}
