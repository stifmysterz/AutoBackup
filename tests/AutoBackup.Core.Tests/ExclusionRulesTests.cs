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
}
