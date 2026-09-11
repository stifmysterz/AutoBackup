using System.IO.Enumeration;

namespace AutoBackup.Core.Exclusion;

public static class ExclusionRules
{
    private static readonly string[] BuiltInNamePatterns = { "*.tmp", "*.temp", "~$*", "Thumbs.db" };

    public static bool ShouldExclude(string fullPath, FileAttributes attributes, IReadOnlyList<string> customPatterns)
    {
        if ((attributes & FileAttributes.Hidden) != 0) return true;
        if ((attributes & FileAttributes.System) != 0) return true;

        var name = Path.GetFileName(fullPath);
        if (name.Contains("cache", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var pattern in BuiltInNamePatterns)
            if (FileSystemName.MatchesSimpleExpression(pattern, name)) return true;

        foreach (var pattern in customPatterns)
        {
            if (string.Equals(pattern, fullPath, StringComparison.OrdinalIgnoreCase)) return true;
            if (FileSystemName.MatchesSimpleExpression(pattern, name)) return true;
        }

        return false;
    }
}
