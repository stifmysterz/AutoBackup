using AutoBackup.Core.Logging;
using AutoBackup.Core.Settings;
using AutoBackup.Core.SingleInstance;

namespace AutoBackup.App;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var guard = new SingleInstanceGuard("AutoBackup_SingleInstance_Mutex_6F1C0B2E");
        if (!guard.IsFirstInstance)
        {
            MessageBox.Show("Auto Backup 已经在运行中，请查看系统托盘。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Process-level safety net: log and report unexpected exceptions instead of letting
        // the process silently die. WinForms needs SetUnhandledExceptionMode(CatchException)
        // before the ThreadException handler is registered for that handler to actually catch
        // UI-thread exceptions.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatalException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ReportFatalException(ex);
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    private static void ReportFatalException(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(Path.GetDirectoryName(SettingsStore.GetDefaultSettingsPath())!, "backup.log");
            var logger = new BackupLogger(logPath);
            logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "FatalError", Message = ex.ToString() });
        }
        catch
        {
            // best effort - logging must never itself crash the process
        }

        try
        {
            MessageBox.Show($"Auto Backup 遇到意外错误：{ex.Message}", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // best effort
        }
    }
}
