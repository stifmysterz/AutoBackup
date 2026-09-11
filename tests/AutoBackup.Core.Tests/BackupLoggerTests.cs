using AutoBackup.Core.Logging;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class BackupLoggerTests
{
    [Fact]
    public void Append_WritesReadableLine()
    {
        using var temp = new TempDirectory();
        var logger = new BackupLogger(Path.Combine(temp.Path, "backup.log"));

        logger.Append(new BackupLogEntry { Timestamp = new DateTime(2026, 9, 11, 22, 0, 0), Outcome = "Success", Message = "復制 3，硬链接 10，失败 0" });

        var lines = logger.ReadRecent(10);
        Assert.Single(lines);
        Assert.Contains("Success", lines[0]);
        Assert.Contains("復制 3", lines[0]);
    }

    [Fact]
    public void ReadRecent_ReturnsOnlyLastNLines()
    {
        using var temp = new TempDirectory();
        var logger = new BackupLogger(Path.Combine(temp.Path, "backup.log"));
        for (var i = 0; i < 5; i++)
            logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Success", Message = $"entry {i}" });

        var lines = logger.ReadRecent(2);

        Assert.Equal(2, lines.Count);
        Assert.Contains("entry 4", lines[1]);
    }

    [Fact]
    public void RotateIfNeeded_ArchivesFile_WhenOverSizeLimit()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "backup.log");
        var logger = new BackupLogger(path);
        File.WriteAllText(path, new string('x', 1000));

        logger.RotateIfNeeded(maxSizeBytes: 100, maxAgeDays: 365, now: DateTime.Now);

        Assert.True(!File.Exists(path) || new FileInfo(path).Length == 0);
        Assert.Single(Directory.GetFiles(temp.Path, "backup.log.*.old"));
    }

    [Fact]
    public void RotateIfNeeded_DoesNothing_WhenUnderLimits()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "backup.log");
        var logger = new BackupLogger(path);
        logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Success", Message = "ok" });

        logger.RotateIfNeeded(maxSizeBytes: 10_000_000, maxAgeDays: 365, now: DateTime.Now);

        Assert.Single(logger.ReadRecent(10));
        Assert.Empty(Directory.GetFiles(temp.Path, "backup.log.*.old"));
    }
}
