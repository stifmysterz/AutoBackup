using AutoBackup.Core.Exclusion;

namespace AutoBackup.Core.Tests;

public class ExclusionRulesTests
{
    private static readonly string[] NoCustomPatterns = Array.Empty<string>();

    [Fact]
    public void Excludes_HiddenAttribute()
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\secret.txt", FileAttributes.Hidden, NoCustomPatterns));
    }

    [Fact]
    public void Excludes_SystemAttribute()
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\sys.dat", FileAttributes.System, NoCustomPatterns));
    }

    [Theory]
    [InlineData("notes.tmp")]
    [InlineData("draft.temp")]
    [InlineData("~$report.docx")]
    [InlineData("Thumbs.db")]
    public void Excludes_TempFilePatterns(string fileName)
    {
        Assert.True(ExclusionRules.ShouldExclude($@"C:\Docs\{fileName}", FileAttributes.Normal, NoCustomPatterns));
    }

    [Theory]
    [InlineData("BrowserCache")]
    [InlineData("cache")]
    [InlineData("my_Cache_folder")]
    public void Excludes_NamesContainingCache(string name)
    {
        Assert.True(ExclusionRules.ShouldExclude($@"C:\Docs\{name}", FileAttributes.Directory, NoCustomPatterns));
    }

    [Fact]
    public void Excludes_CustomExactPathMatch()
    {
        var patterns = new[] { @"C:\Docs\Movies" };
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\Movies", FileAttributes.Directory, patterns));
    }

    [Fact]
    public void Excludes_CustomWildcardPattern()
    {
        var patterns = new[] { "*.log" };
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\app.log", FileAttributes.Normal, patterns));
    }

    [Fact]
    public void DoesNotExclude_NormalFile()
    {
        Assert.False(ExclusionRules.ShouldExclude(@"C:\Docs\report.docx", FileAttributes.Normal, NoCustomPatterns));
    }

    // Path rules used to be compared character-for-character, so any of these harmless
    // variations made the rule silently stop working.
    [Theory]
    [InlineData(@"C:\Docs\Movies\")]
    [InlineData(@"  C:\Docs\Movies  ")]
    [InlineData(@"C:/Docs/Movies")]
    [InlineData(@"c:\docs\movies")]
    public void Excludes_CustomPath_DespiteFormattingVariations(string pattern)
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\Movies", FileAttributes.Directory, new[] { pattern }));
    }

    [Fact]
    public void Excludes_CustomWildcard_WithSurroundingWhitespace()
    {
        Assert.True(ExclusionRules.ShouldExclude(@"C:\Docs\app.log", FileAttributes.Normal, new[] { " *.log " }));
    }

    [Fact]
    public void DoesNotExclude_SiblingFolderThatMerelySharesAPrefix()
    {
        var patterns = new[] { @"C:\Docs\Movies" };
        Assert.False(ExclusionRules.ShouldExclude(@"C:\Docs\Movies2", FileAttributes.Directory, patterns));
    }

    [Fact]
    public void IgnoresBlankPatterns()
    {
        Assert.False(ExclusionRules.ShouldExclude(@"C:\Docs\report.docx", FileAttributes.Normal, new[] { "", "   " }));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Downloads\Movies", true)]
    [InlineData(@"C:\Users\me\Downloads\Movies\", true)]
    [InlineData(@"C:\Users\me\Downloads", false)]   // the source itself: its root is never checked
    [InlineData(@"C:\Users\me", false)]             // a parent of a source
    [InlineData(@"C:\Users\me\Downloads2\x", false)] // shares a prefix but is a different folder
    [InlineData(@"D:\Elsewhere", false)]
    public void IsInsideAnySource_OnlyForFoldersABackupWouldActuallyWalk(string folder, bool expected)
    {
        var sources = new[] { @"C:\Users\me\Desktop", @"C:\Users\me\Downloads" };
        Assert.Equal(expected, ExclusionRules.IsInsideAnySource(folder, sources));
    }
}
