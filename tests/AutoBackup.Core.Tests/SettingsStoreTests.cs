using AutoBackup.Core.Models;
using AutoBackup.Core.Settings;
using AutoBackup.Core.Tests.TestSupport;

namespace AutoBackup.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaultSettings_WhenFileDoesNotExist()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");

        var settings = SettingsStore.Load(path);

        Assert.Equal(30, settings.RetentionDays);
        Assert.Empty(settings.SourceFolders);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var original = new BackupSettings
        {
            SourceFolders = new List<string> { @"C:\Users\Me\Desktop", @"C:\Users\Me\Documents" },
            TargetVolumeSerial = "ABCD-1234",
            TargetVolumeLabel = "MyDrive",
            ScheduleDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            ScheduleTime = new TimeOnly(22, 30),
            RetentionDays = 45,
            CustomExcludePatterns = new List<string> { "*.log" },
            StartWithWindows = false,
            LastRunAt = new DateTime(2026, 9, 10, 22, 0, 0)
        };

        SettingsStore.Save(original, path);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(original.SourceFolders, loaded.SourceFolders);
        Assert.Equal(original.TargetVolumeSerial, loaded.TargetVolumeSerial);
        Assert.Equal(original.ScheduleDays, loaded.ScheduleDays);
        Assert.Equal(original.ScheduleTime, loaded.ScheduleTime);
        Assert.Equal(original.RetentionDays, loaded.RetentionDays);
        Assert.Equal(original.CustomExcludePatterns, loaded.CustomExcludePatterns);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.LastRunAt, loaded.LastRunAt);
    }
}
