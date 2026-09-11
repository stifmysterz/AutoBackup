using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public static class DriveIdentifier
{
    public static DriveInfoRecord? FindBySerial(IEnumerable<DriveInfoRecord> drives, string volumeSerial) =>
        drives.FirstOrDefault(d => string.Equals(d.VolumeSerial, volumeSerial, StringComparison.OrdinalIgnoreCase));
}
