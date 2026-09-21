namespace AutoBackup.Core.Backup;

public static class RetentionCleaner
{
    /// <summary>
    /// Deletes snapshots older than the retention window, always keeping the most recent one
    /// regardless of age.
    /// </summary>
    /// <returns>Paths of snapshots that could not be deleted, so the caller can report them
    /// without treating an otherwise-successful backup as failed.</returns>
    public static IReadOnlyList<string> CleanOldSnapshots(string backupRoot, int retentionDays, DateTime now)
    {
        var snapshots = SnapshotPathPlanner.GetAllSnapshots(backupRoot)
            .OrderByDescending(s => s.timestamp)
            .ToList();
        if (snapshots.Count <= 1) return Array.Empty<string>();

        var failures = new List<string>();
        var cutoff = now.AddDays(-retentionDays);
        foreach (var snapshot in snapshots.Skip(1))
        {
            if (snapshot.timestamp >= cutoff) continue;

            try
            {
                Directory.Delete(snapshot.path, recursive: true);
            }
            catch (Exception ex)
            {
                // One snapshot being locked (a file open in another program, a transient
                // handle) must not abort the whole sweep - every other expired snapshot
                // still deserves to be pruned on this pass.
                failures.Add($"{Path.GetFileName(snapshot.path)}: {ex.Message}");
            }
        }
        return failures;
    }
}
