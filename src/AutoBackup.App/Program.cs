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

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
