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
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();
    }

    public static void Save(BackupSettings settings, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
