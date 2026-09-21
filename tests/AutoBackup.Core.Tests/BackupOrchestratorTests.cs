using AutoBackup.Core.Drives;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class FakeDriveScanner : IDriveScanner
{
    public List<DriveInfoRecord> Drives { get; set; } = new();
    public IReadOnlyList<DriveInfoRecord> GetReadyDrives() => Drives;
}

public class BackupOrchestratorTests
{
    private static BackupLogger MakeLogger(string tempDir) => new(Path.Combine(tempDir, "backup.log"));

    [Fact]
    public void RunOnce_ReturnsNoSourceFolders_WhenNoneConfigured()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string>() };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.NoSourceFolders, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsDriveNotConnected_WhenSerialNotFound()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "MISSING" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.DriveNotConnected, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsSourceTargetOverlap_WhenTargetInsideSource()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Source")).FullName;
        var driveRoot = source; // deliberately overlapping: drive root equals the source folder
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 1_000_000_000, TotalBytes = 1_000_000_000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.SourceTargetOverlap, result.Outcome);
    }

    [Fact]
    public void RunOnce_ReturnsInsufficientSpace_WhenFreeBytesTooLow()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), new string('x', 1000));
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 10, TotalBytes = 1000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1" };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.InsufficientSpace, result.Outcome);
    }

    [Fact]
    public void RunOnce_RunsFullBackupAndCleansUp_WhenEverythingIsFine()
    {
        using var temp = new TempDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "Desktop")).FullName;
        File.WriteAllText(Path.Combine(source, "a.txt"), "hello");
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 1_000_000_000, TotalBytes = 1_000_000_000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { SourceFolders = new List<string> { source }, TargetVolumeSerial = "SER-1", RetentionDays = 30 };

        var result = orchestrator.RunOnce(settings);

        Assert.Equal(OrchestrationOutcome.Success, result.Outcome);
        Assert.NotNull(result.BackupResult);
        Assert.Equal(1, result.BackupResult!.FilesCopied);
    }

    [Fact]
    public void RunCleanupOnly_ReturnsFalse_WhenDriveNotConnected()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { TargetVolumeSerial = "MISSING" };

        var ran = orchestrator.RunCleanupOnly(settings);

        Assert.False(ran);
    }

    [Fact]
    public void RunCleanupOnly_ReturnsFalse_WhenNoDriveConfiguredYet()
    {
        using var temp = new TempDirectory();
        var scanner = new FakeDriveScanner();
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings();

        var ran = orchestrator.RunCleanupOnly(settings);

        Assert.False(ran);
    }

    [Fact]
    public void RunCleanupOnly_DeletesExpiredSnapshots_WithoutRunningABackup()
    {
        using var temp = new TempDirectory();
        var driveRoot = Directory.CreateDirectory(Path.Combine(temp.Path, "Drive")).FullName;
        var backupRoot = Path.Combine(driveRoot, "AutoBackup");
        var oldSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2020-01-01_2200")).FullName;
        var recentSnapshot = Directory.CreateDirectory(Path.Combine(backupRoot, "2026-09-10_2200")).FullName;
        var scanner = new FakeDriveScanner
        {
            Drives = new List<DriveInfoRecord>
            {
                new() { DriveLetter = driveRoot, VolumeSerial = "SER-1", FreeBytes = 1_000_000_000, TotalBytes = 1_000_000_000 }
            }
        };
        var orchestrator = new BackupOrchestrator(scanner, MakeLogger(temp.Path));
        var settings = new BackupSettings { TargetVolumeSerial = "SER-1", RetentionDays = 30 };

        var ran = orchestrator.RunCleanupOnly(settings);

        Assert.True(ran);
        Assert.False(Directory.Exists(oldSnapshot));
        Assert.True(Directory.Exists(recentSnapshot));
    }
}
