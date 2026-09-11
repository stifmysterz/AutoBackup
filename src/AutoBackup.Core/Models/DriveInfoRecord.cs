namespace AutoBackup.Core.Models;

public class DriveInfoRecord
{
    public required string DriveLetter { get; init; }
    public required string VolumeSerial { get; init; }
    public string? VolumeLabel { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }
}
