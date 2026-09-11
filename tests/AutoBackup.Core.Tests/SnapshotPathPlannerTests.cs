using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SnapshotPathPlannerTests
{
    [Fact]
    public void FindLatestSnapshot_ReturnsNull_WhenBackupRootDoesNotExist()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");

        Assert.Null(SnapshotPathPlanner.FindLatestSnapshot(backupRoot));
    }

    [Fact]
    public void FindLatestSnapshot_ReturnsMostRecentByName()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-12_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));

        var latest = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);

        Assert.Equal(Path.Combine(backupRoot, "2026-09-12_2200"), latest);
    }

    [Fact]
    public void FindLatestSnapshot_IgnoresInProgressFolders()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-13_2200.inprogress"));

        var latest = SnapshotPathPlanner.FindLatestSnapshot(backupRoot);

        Assert.Equal(Path.Combine(backupRoot, "2026-09-11_2200"), latest);
    }

    [Fact]
    public void CreateNewSnapshotWorkingPath_UsesInProgressSuffix()
    {
        var backupRoot = @"E:\AutoBackup";
        var path = SnapshotPathPlanner.CreateNewSnapshotWorkingPath(backupRoot, new DateTime(2026, 9, 11, 22, 0, 0));

        Assert.Equal(Path.Combine(backupRoot, "2026-09-11_2200.inprogress"), path);
    }

    [Fact]
    public void GetFinalPath_StripsInProgressSuffix()
    {
        var working = @"E:\AutoBackup\2026-09-11_2200.inprogress";
        Assert.Equal(@"E:\AutoBackup\2026-09-11_2200", SnapshotPathPlanner.GetFinalPath(working));
    }

    [Fact]
    public void GetAllSnapshots_ReturnsOnlyCompletedSnapshotsWithParsedTimestamps()
    {
        using var temp = new TempDirectory();
        var backupRoot = Path.Combine(temp.Path, "AutoBackup");
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-11_2200"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-12_2200.inprogress"));

        var snapshots = SnapshotPathPlanner.GetAllSnapshots(backupRoot).ToList();

        Assert.Single(snapshots);
        Assert.Equal(new DateTime(2026, 9, 11, 22, 0, 0), snapshots[0].timestamp);
    }
}
