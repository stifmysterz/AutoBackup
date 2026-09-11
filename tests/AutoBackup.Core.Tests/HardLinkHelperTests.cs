using AutoBackup.Core.Backup;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class HardLinkHelperTests
{
    [Fact]
    public void CreatesLinkThatSharesContentWithOriginal()
    {
        using var temp = new TempDirectory();
        var original = Path.Combine(temp.Path, "original.txt");
        var link = Path.Combine(temp.Path, "link.txt");
        File.WriteAllText(original, "hello");

        var ok = HardLinkHelper.TryCreateHardLink(link, original, out var error);

        Assert.True(ok, error);
        Assert.Equal("hello", File.ReadAllText(link));
    }

    [Fact]
    public void ReturnsFalse_WhenSourceFileDoesNotExist()
    {
        using var temp = new TempDirectory();
        var link = Path.Combine(temp.Path, "link.txt");

        var ok = HardLinkHelper.TryCreateHardLink(link, Path.Combine(temp.Path, "missing.txt"), out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
