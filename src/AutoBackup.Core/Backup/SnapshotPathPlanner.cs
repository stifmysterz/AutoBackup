using System.Globalization;

namespace AutoBackup.Core.Backup;

public static class SnapshotPathPlanner
{
    private const string SnapshotFolderFormat = "yyyy-MM-dd_HHmm";
    private const string InProgressSuffix = ".inprogress";
    public const string BackupRootFolderName = "AutoBackup";

    public static string GetBackupRoot(string driveRoot) => Path.Combine(driveRoot, BackupRootFolderName);

    public static string? FindLatestSnapshot(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) return null;
        return Directory.GetDirectories(backupRoot)
            .Where(d => !Path.GetFileName(d).EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            .Where(d => DateTime.TryParseExact(Path.GetFileName(d), SnapshotFolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string CreateNewSnapshotWorkingPath(string backupRoot, DateTime timestamp)
    {
        var name = timestamp.ToString(SnapshotFolderFormat, CultureInfo.InvariantCulture);
        return Path.Combine(backupRoot, name + InProgressSuffix);
    }

    public static string GetFinalPath(string workingPath)
    {
        if (!workingPath.EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a working snapshot path", nameof(workingPath));
        return workingPath[..^InProgressSuffix.Length];
    }

    public static IEnumerable<(string path, DateTime timestamp)> GetAllSnapshots(string backupRoot)
    {
        if (!Directory.Exists(backupRoot)) yield break;
        foreach (var dir in Directory.GetDirectories(backupRoot))
        {
            var name = Path.GetFileName(dir);
            if (name.EndsWith(InProgressSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (DateTime.TryParseExact(name, SnapshotFolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                yield return (dir, ts);
        }
    }
}
