namespace AutoBackup.Core.Backup;

public static class SourceAliasMapper
{
    public static IReadOnlyDictionary<string, string> BuildAliases(IReadOnlyList<string> sourceFolders)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>();
        foreach (var source in sourceFolders)
        {
            var baseName = new DirectoryInfo(source).Name;
            if (string.IsNullOrEmpty(baseName) || baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                // A drive-root source like "C:\" has a DirectoryInfo.Name of "C:\" (colon and
                // backslash included), which would make the destination folder resolve to the
                // literal drive root via Path.Combine's rooted-path behavior. Sanitize to a
                // safe, non-empty, non-rooted folder name instead.
                baseName = new string(source.Where(char.IsLetterOrDigit).ToArray());
                if (string.IsNullOrEmpty(baseName)) baseName = "Source";
            }
            var candidate = baseName;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            map[source] = candidate;
        }
        return map;
    }
}
