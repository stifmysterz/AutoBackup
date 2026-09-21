namespace AutoBackup.Core.Backup;

public class SnapshotSummary
{
    public required DateTime Timestamp { get; init; }
    public required string Path { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
}

public static class SnapshotHistory
{
    public static IReadOnlyList<SnapshotSummary> GetHistory(string backupRoot)
    {
        var summaries = new List<SnapshotSummary>();
        foreach (var (path, timestamp) in SnapshotPathPlanner.GetAllSnapshots(backupRoot))
        {
            long totalBytes = 0;
            var fileCount = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    totalBytes += new FileInfo(file).Length;
                    fileCount++;
                }
                catch
                {
                    // skip unreadable file
                }
            }
            summaries.Add(new SnapshotSummary { Timestamp = timestamp, Path = path, TotalBytes = totalBytes, FileCount = fileCount });
        }
        return summaries.OrderByDescending(s => s.Timestamp).ToList();
    }
}
