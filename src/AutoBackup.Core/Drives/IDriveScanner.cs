using AutoBackup.Core.Models;

namespace AutoBackup.Core.Drives;

public interface IDriveScanner
{
    IReadOnlyList<DriveInfoRecord> GetReadyDrives();
}
