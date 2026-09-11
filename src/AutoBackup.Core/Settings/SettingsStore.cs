using System.Text.Json;
using AutoBackup.Core.Models;

namespace AutoBackup.Core.Settings;

public static class SettingsStore
{
    public static string GetDefaultSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoBackup", "settings.json");

    public static BackupSettings Load(string path)
    {
        if (!File.Exists(path)) return new BackupSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();
        }
        catch (Exception)
        {
            // Malformed/truncated JSON (e.g. from a crash mid-write) must not brick startup -
            // Load is called from the tray app's constructor. Move the bad file aside on a
            // best-effort basis and fall back to defaults.
            try
            {
                File.Move(path, path + ".corrupt." + DateTime.Now.Ticks, overwrite: false);
            }
            catch
            {
                // best effort - if even this fails, just proceed with defaults
            }
            return new BackupSettings();
        }
    }

    public static void Save(BackupSettings settings, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        // Write-then-move is atomic on the same volume, so a crash/power-loss mid-write can
        // never leave a truncated settings.json behind - Save runs after every backup.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
