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
}
