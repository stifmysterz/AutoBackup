using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoBackup.Core.Backup;

public static class HardLinkHelper
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static bool TryCreateHardLink(string newFilePath, string existingFilePath, out string? error)
    {
        var ok = CreateHardLink(newFilePath, existingFilePath, IntPtr.Zero);
        if (!ok)
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        error = null;
        return true;
    }
}
