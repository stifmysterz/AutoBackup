using AutoBackup.Core.Drives;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Tests;

public class DriveIdentifierTests
{
    private static readonly List<DriveInfoRecord> Drives = new()
    {
        new DriveInfoRecord { DriveLetter = @"D:\", VolumeSerial = "AAAA-1111", VolumeLabel = "Data" },
        new DriveInfoRecord { DriveLetter = @"E:\", VolumeSerial = "BBBB-2222", VolumeLabel = "Backup" }
    };

    [Fact]
    public void FindBySerial_ReturnsMatchingDrive()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "BBBB-2222");
        Assert.NotNull(found);
        Assert.Equal(@"E:\", found!.DriveLetter);
    }

    [Fact]
    public void FindBySerial_IsCaseInsensitive()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "bbbb-2222");
        Assert.NotNull(found);
    }

    [Fact]
    public void FindBySerial_ReturnsNull_WhenNoMatch()
    {
        var found = DriveIdentifier.FindBySerial(Drives, "ZZZZ-9999");
        Assert.Null(found);
    }
}
