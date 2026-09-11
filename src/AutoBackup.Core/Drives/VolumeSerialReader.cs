using System.Runtime.InteropServices;
using System.Text;

namespace AutoBackup.Core.Drives;

internal static class VolumeSerialReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    public static (string? serial, string? label) Read(string rootPath)
    {
        var volumeName = new StringBuilder(261);
        var fileSystemName = new StringBuilder(261);
        var ok = GetVolumeInformation(rootPath, volumeName, volumeName.Capacity, out var serial, out _, out _, fileSystemName, fileSystemName.Capacity);
        if (!ok) return (null, null);
        return (serial.ToString("X8"), volumeName.ToString());
    }
}
