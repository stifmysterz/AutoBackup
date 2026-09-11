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
