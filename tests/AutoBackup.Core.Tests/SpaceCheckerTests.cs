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
}
