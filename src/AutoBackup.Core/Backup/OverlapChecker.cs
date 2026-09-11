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

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full + Path.DirectorySeparatorChar;
    }
}
