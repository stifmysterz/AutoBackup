using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class RetentionCleanerTests
{
    [Fact]
    public void DeletesSnapshotsOlderThanRetentionDays()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var oldSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-01-01_2200")).FullName;
        var recentSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.False(Directory.Exists(oldSnapshot));
        Assert.True(Directory.Exists(recentSnapshot));
    }

    [Fact]
    public void AlwaysKeepsAtLeastTheMostRecentSnapshot()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var onlySnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2020-01-01_2200")).FullName;
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.True(Directory.Exists(onlySnapshot));
    }

    [Fact]
    public void SharedHardLinkedFile_SurvivesDeletionOfOneSnapshot()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var oldSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-01-01_2200")).FullName;
        var recentSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        var oldFile = Path.Combine(oldSnapshot, "a.txt");
        File.WriteAllText(oldFile, "shared");
        var recentFile = Path.Combine(recentSnapshot, "a.txt");
        HardLinkHelper.TryCreateHardLink(recentFile, oldFile, out _);
        var now = new DateTime(2026, 9, 11);

        RetentionCleaner.CleanOldSnapshots(backupRoot, retentionDays: 30, now: now);

        Assert.False(Directory.Exists(oldSnapshot));
        Assert.Equal("shared", File.ReadAllText(recentFile));
    }
}
