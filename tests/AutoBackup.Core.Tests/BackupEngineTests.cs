using AutoBackup.Core.Backup;
using AutoBackup.Core.Models;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class BackupEngineTests
{
    [Fact]
    public void FirstBackup_CopiesAllFilesIntoSnapshotFolder()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;

        var engine = new BackupEngine();
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(BackupOutcome.Success, result.Outcome);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesLinked);
        Assert.True(Directory.Exists(result.SnapshotPath));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void SecondBackup_HardLinksUnchangedFiles()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(0, result.FilesCopied);
        Assert.Equal(1, result.FilesLinked);
    }

    [Fact]
    public void SecondBackup_RecopiesModifiedFilesOnly()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(source.FullName, "b.txt"), "world");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Thread.Sleep(50);
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello changed");
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(1, result.FilesLinked);
        Assert.Equal("hello changed", File.ReadAllText(Path.Combine(result.SnapshotPath, "Desktop", "a.txt")));
    }

    [Fact]
    public void DeletedSourceFiles_AreAbsentFromNewSnapshotButKeptInOldOne()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        var deletedFilePath = Path.Combine(source.FullName, "gone.txt");
        File.WriteAllText(deletedFilePath, "bye");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var engine = new BackupEngine();
        var firstResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());
        File.Delete(deletedFilePath);

        var secondResult = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.True(File.Exists(Path.Combine(firstResult.SnapshotPath, "Desktop", "gone.txt")));
        Assert.False(File.Exists(Path.Combine(secondResult.SnapshotPath, "Desktop", "gone.txt")));
    }

    [Fact]
    public void ExcludedFiles_AreNeverCopied()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop"));
        File.WriteAllText(Path.Combine(source.FullName, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(source.FullName, "junk.tmp"), "temp");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;

        var engine = new BackupEngine();
        var result = engine.RunBackup(new[] { source.FullName }, driveRoot, Array.Empty<string>());

        Assert.Equal(1, result.FilesCopied);
        Assert.False(File.Exists(Path.Combine(result.SnapshotPath, "Desktop", "junk.tmp")));
    }
}
