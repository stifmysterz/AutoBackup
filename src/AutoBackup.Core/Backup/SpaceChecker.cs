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

    private static long SumDirectory(DirectoryInfo dir, IReadOnlyList<string> customExcludePatterns)
    {
        long total = 0;
        foreach (var file in dir.GetFiles())
        {
            if (ExclusionRules.ShouldExclude(file.FullName, file.Attributes, customExcludePatterns)) continue;
            total += file.Length;
        }
        foreach (var sub in dir.GetDirectories())
        {
            if (ExclusionRules.ShouldExclude(sub.FullName, sub.Attributes, customExcludePatterns)) continue;
            total += SumDirectory(sub, customExcludePatterns);
        }
        return total;
    }
}
