using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public class DriveScanner : IDriveScanner
{
    public IReadOnlyList<DriveInfoRecord> GetReadyDrives()
    {
        var result = new List<DriveInfoRecord>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.Fixed) continue;
            if (!drive.IsReady) continue;

            var (serial, label) = VolumeSerialReader.Read(drive.RootDirectory.FullName);
            if (serial == null) continue;

            result.Add(new DriveInfoRecord
            {
                DriveLetter = drive.RootDirectory.FullName,
                VolumeSerial = serial,
                VolumeLabel = label,
                FreeBytes = drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize
            });
        }
        return result;
    }
}
