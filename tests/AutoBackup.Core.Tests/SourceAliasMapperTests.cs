using AutoBackup.Core.Backup;

namespace AutoBackup.Core.Tests;

public class SourceAliasMapperTests
{
    [Fact]
    public void UsesLeafFolderName_ByDefault()
    {
        var sources = new List<string> { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        Assert.Equal("Desktop", aliases[@"C:\Users\Me\Desktop"]);
        Assert.Equal("Documents", aliases[@"C:\Users\Me\Documents"]);
    }

    [Fact]
    public void DisambiguatesDuplicateLeafNames()
    {
        var sources = new List<string> { @"C:\Data\Notes", @"D:\Backup\Notes" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        Assert.Equal("Notes", aliases[@"C:\Data\Notes"]);
        Assert.Equal("Notes (2)", aliases[@"D:\Backup\Notes"]);
    }

    [Fact]
    public void DriveRootSource_ProducesSafeNonRootedAlias()
    {
        var sources = new List<string> { @"C:\" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        var alias = aliases[@"C:\"];
        Assert.False(string.IsNullOrEmpty(alias));
        Assert.DoesNotContain(':', alias);
        Assert.DoesNotContain('\\', alias);
        // Combining with a safe alias must not resolve back to a drive root: a rooted second
        // argument to Path.Combine would silently discard the first argument, which is the
        // exact bug this alias sanitization prevents.
        var combined = Path.Combine(@"D:\AutoBackup\2026-09-11_2200.inprogress", alias);
        Assert.StartsWith(@"D:\AutoBackup\2026-09-11_2200.inprogress", combined);
    }

    [Fact]
    public void DriveRootSource_DisambiguatesAgainstAnotherDriveRoot()
    {
        var sources = new List<string> { @"C:\", @"D:\" };
        var aliases = SourceAliasMapper.BuildAliases(sources);

        Assert.NotEqual(aliases[@"C:\"], aliases[@"D:\"]);
    }
}
