using System.IO.Enumeration;

namespace AutoBackup.Core.Exclusion;

public static class ExclusionRules
{
    private static readonly string[] BuiltInNamePatterns = { "*.tmp", "*.temp", "~$*", "Thumbs.db" };
    private static readonly char[] Separators = { '\\', '/' };

    public static bool ShouldExclude(string fullPath, FileAttributes attributes, IReadOnlyList<string> customPatterns)
    {
        if ((attributes & FileAttributes.Hidden) != 0) return true;
        if ((attributes & FileAttributes.System) != 0) return true;
        // A junction or symlink could point at an ancestor directory, causing unbounded
        // recursion if followed. Standard practice for backup tools is to treat reparse
        // points as excluded rather than following them.
        if ((attributes & FileAttributes.ReparsePoint) != 0) return true;

        var name = Path.GetFileName(fullPath);
        if (name.Contains("cache", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var pattern in BuiltInNamePatterns)
            if (FileSystemName.MatchesSimpleExpression(pattern, name)) return true;

        foreach (var rawPattern in customPatterns)
        {
            var pattern = rawPattern.Trim();
            if (pattern.Length == 0) continue;

            // A rule is either a specific folder or a name pattern. Names never contain a
            // separator, so anything with one can only mean a path.
            if (IsPathRule(pattern))
            {
                if (string.Equals(NormalizePath(pattern), NormalizePath(fullPath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (FileSystemName.MatchesSimpleExpression(pattern, name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a folder lies strictly inside one of the backup sources - the only place an
    /// exclusion can have any effect. A source's own root, or a folder above it, is never
    /// reached by the walk that checks exclusions, so excluding it would silently do nothing.
    /// </summary>
    public static bool IsInsideAnySource(string folderPath, IEnumerable<string> sourceFolders)
    {
        var folder = NormalizePath(folderPath) + Path.DirectorySeparatorChar;
        foreach (var source in sourceFolders)
        {
            var root = NormalizePath(source) + Path.DirectorySeparatorChar;
            if (folder.Length > root.Length && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsPathRule(string pattern) =>
        pattern.IndexOfAny(Separators) >= 0 || Path.IsPathRooted(pattern);

    // Canonicalises slashes, "." / ".." segments and trailing separators so that rules match
    // regardless of how the user happened to type them.
    private static string NormalizePath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            full = path.Trim();
        }
        return full.TrimEnd(Separators);
    }
}
