namespace AutoBackup.Core.Backup;

public static class OverlapChecker
{
    public static bool HasOverlap(IEnumerable<string> sourceFolders, string targetRootPath)
    {
        var normalizedTarget = Normalize(targetRootPath);
        foreach (var source in sourceFolders)
        {
            var normalizedSource = Normalize(source);
            if (normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Like <see cref="HasOverlap"/>, but also reports which source folder conflicts so the
    /// caller can show a specific, actionable message to the user.
    /// </summary>
    public static bool TryFindOverlap(IEnumerable<string> sourceFolders, string targetRootPath, out string? conflictingSource)
    {
        var normalizedTarget = Normalize(targetRootPath);
        foreach (var source in sourceFolders)
        {
            var normalizedSource = Normalize(source);
            if (normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                conflictingSource = source;
                return true;
            }
        }
        conflictingSource = null;
        return false;
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }
}
