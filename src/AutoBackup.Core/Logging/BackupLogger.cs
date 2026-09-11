namespace AutoBackup.Core.Logging;

public class BackupLogger
{
    private readonly string _logFilePath;

    public BackupLogger(string logFilePath) => _logFilePath = logFilePath;

    public void Append(BackupLogEntry entry)
    {
        var dir = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}\t{entry.Outcome}\t{entry.Message}";
        File.AppendAllLines(_logFilePath, new[] { line });
    }

    public IReadOnlyList<string> ReadRecent(int maxLines)
    {
        if (!File.Exists(_logFilePath)) return Array.Empty<string>();
        return File.ReadLines(_logFilePath).TakeLast(maxLines).ToList();
    }

    public void RotateIfNeeded(long maxSizeBytes, int maxAgeDays, DateTime now)
    {
        if (!File.Exists(_logFilePath)) return;

        var info = new FileInfo(_logFilePath);
        var tooBig = info.Length > maxSizeBytes;
        var tooOld = (now - info.CreationTimeUtc).TotalDays > maxAgeDays;
        if (!tooBig && !tooOld) return;

        var archivePath = _logFilePath + "." + now.ToString("yyyyMMddHHmmss") + ".old";
        File.Move(_logFilePath, archivePath);

        var dir = Path.GetDirectoryName(_logFilePath)!;
        var baseName = Path.GetFileName(_logFilePath);
        var oldArchives = Directory.GetFiles(dir, baseName + ".*.old")
            .OrderByDescending(f => f)
            .Skip(1);
        foreach (var old in oldArchives) File.Delete(old);
    }
}
