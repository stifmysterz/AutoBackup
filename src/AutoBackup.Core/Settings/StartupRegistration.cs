using Microsoft.Win32;

namespace AutoBackup.Core.Settings;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoBackup";

    /// <returns>false if the registry refused the change (locked-down policy, denied
    /// permissions). This runs during tray startup, where throwing would stop the app from
    /// launching at all over a non-essential convenience setting.</returns>
    public static bool Apply(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return false;

            if (enabled)
                key.SetValue(ValueName, $"\"{executablePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
