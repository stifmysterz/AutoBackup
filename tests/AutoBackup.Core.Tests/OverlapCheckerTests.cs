using AutoBackup.Core.Backup;

namespace AutoBackup.Core.Tests;

public class OverlapCheckerTests
{
    [Fact]
    public void ReturnsFalse_WhenNoOverlap()
    {
        var sources = new[] { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" };
        Assert.False(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenTargetIsInsideSource()
    {
        var sources = new[] { @"C:\Users\Me" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"C:\Users\Me\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenSourceIsInsideTarget()
    {
        var sources = new[] { @"E:\AutoBackup\Desktop" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }

    [Fact]
    public void ReturnsTrue_WhenPathsAreIdentical()
    {
        var sources = new[] { @"E:\AutoBackup" };
        Assert.True(OverlapChecker.HasOverlap(sources, @"E:\AutoBackup"));
    }
}
