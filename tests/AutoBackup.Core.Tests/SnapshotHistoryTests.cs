using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SnapshotHistoryTests
{
    [Fact]
    public void GetHistory_ReturnsSizeAndFileCountPerSnapshot_MostRecentFirst()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        var older = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        File.WriteAllText(Path.Combine(older, "a.txt"), "hello");
        var newer = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200")).FullName;
        File.WriteAllText(Path.Combine(newer, "a.txt"), "hello world");
        File.WriteAllText(Path.Combine(newer, "b.txt"), "x");

        var history = SnapshotHistory.GetHistory(backupRoot);

        Assert.Equal(2, history.Count);
        Assert.Equal(new DateTime(2026, 9, 11, 22, 0, 0), history[0].Timestamp);
        Assert.Equal(12, history[0].TotalBytes);
        Assert.Equal(2, history[0].FileCount);
        Assert.Equal(new DateTime(2026, 9, 10, 22, 0, 0), history[1].Timestamp);
        Assert.Equal(5, history[1].TotalBytes);
        Assert.Equal(1, history[1].FileCount);
    }

    [Fact]
    public void GetHistory_ReturnsEmptyList_WhenBackupRootDoesNotExist()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");

        var history = SnapshotHistory.GetHistory(backupRoot);

        Assert.Empty(history);
    }

    [Fact]
    public void GetHistory_IgnoresInProgressSnapshots()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200.inprogress"));

        var history = SnapshotHistory.GetHistory(backupRoot);

        Assert.Empty(history);
    }
}
