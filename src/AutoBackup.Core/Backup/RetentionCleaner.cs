namespace AutoBackup.Core.Backup;

public static class RetentionCleaner
{
    public static void CleanOldSnapshots(string backupRoot, int retentionDays, DateTime now)
    {
        var snapshots = SnapshotPathPlanner.GetAllSnapshots(backupRoot)
            .OrderByDescending(s => s.timestamp)
            .ToList();
        if (snapshots.Count <= 1) return;

        var cutoff = now.AddDays(-retentionDays);
        foreach (var snapshot in snapshots.Skip(1))
        {
            if (snapshot.timestamp < cutoff)
                Directory.Delete(snapshot.path, recursive: true);
        }
    }
}
