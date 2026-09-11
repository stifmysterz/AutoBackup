using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SpaceCheckerTests
{
    [Fact]
    public void EstimateSourceSizeBytes_SumsAllFilesRecursively()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "12345");
        var sub = Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "b.txt"), "1234567890");

        var total = SpaceChecker.EstimateSourceSizeBytes(new[] { temp.Path }, Array.Empty<string>());

        Assert.Equal(15, total);
    }

    [Fact]
    public void EstimateSourceSizeBytes_SkipsExcludedFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "12345");
        File.WriteAllText(Path.Combine(temp.Path, "junk.tmp"), "1234567890");

        var total = SpaceChecker.EstimateSourceSizeBytes(new[] { temp.Path }, Array.Empty<string>());

        Assert.Equal(5, total);
    }

    [Theory]
    [InlineData(100, 200, true)]
    [InlineData(200, 100, false)]
    [InlineData(100, 100, true)]
    public void HasEnoughSpace_ComparesEstimateToFreeBytes(long estimated, long free, bool expected)
    {
        Assert.Equal(expected, SpaceChecker.HasEnoughSpace(estimated, free));
    }

    [Fact]
    public void GetSnapshotSizeBytes_SumsAllFilesRecursively()
    {
        using var temp = new TempDirectory();
        var snapshot = Directory.CreateDirectory(Path.Combine(temp.Path, "2026-09-11_2200"));
        File.WriteAllText(Path.Combine(snapshot.FullName, "a.txt"), "12345");
        var sub = Directory.CreateDirectory(Path.Combine(snapshot.FullName, "sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "b.txt"), "1234567890");

        var total = SpaceChecker.GetSnapshotSizeBytes(snapshot.FullName);

        Assert.Equal(15, total);
    }

    [Fact]
    public void GetSnapshotSizeBytes_ReturnsZero_WhenSnapshotDoesNotExist()
    {
        using var temp = new TempDirectory();

        var total = SpaceChecker.GetSnapshotSizeBytes(Path.Combine(temp.Path, "does-not-exist"));

        Assert.Equal(0, total);
    }
}
