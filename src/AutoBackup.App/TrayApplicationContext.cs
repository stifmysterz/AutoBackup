using AutoBackup.Core.Drives;
using AutoBackup.Core.Logging;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Scheduling;
using AutoBackup.Core.Settings;

namespace AutoBackup.App;

public class TrayApplicationContext : ApplicationContext
{
    private readonly string _settingsPath = SettingsStore.GetDefaultSettingsPath();
    private readonly BackupLogger _logger;
    private readonly IDriveScanner _driveScanner = new DriveScanner();
    private readonly System.Threading.Timer _schedulerTimer;
    private readonly NotifyIcon _trayIcon;
    private BackupSettings _settings;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load(_settingsPath);
        _logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backup.log"));

        var menu = new ContextMenuStrip();
        menu.Items.Add("立即备份", null, (_, _) => RunBackup(manualTrigger: true));
        menu.Items.Add("打开设置", null, OnOpenSettingsClicked);
        menu.Items.Add("查看备份日志", null, (_, _) => new LogViewerForm(_logger).ShowDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, OnExitClicked);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTrayText(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _schedulerTimer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    private void OnTimerTick(object? state)
    {
        var config = new ScheduleConfig { Days = _settings.ScheduleDays, Time = _settings.ScheduleTime };
        if (BackupScheduler.IsDueNow(config, DateTime.Now, _settings.LastRunAt))
        {
            RunBackup(manualTrigger: false);
        }
    }

    private void RunBackup(bool manualTrigger)
    {
        var orchestrator = new BackupOrchestrator(_driveScanner, _logger);
        var result = orchestrator.RunOnce(_settings);

        _settings.LastRunAt = DateTime.Now;
        SettingsStore.Save(_settings, _settingsPath);
        _trayIcon.Text = BuildTrayText();

        switch (result.Outcome)
        {
            case OrchestrationOutcome.Success:
                if (manualTrigger) _trayIcon.ShowBalloonTip(3000, "Auto Backup", "备份完成", ToolTipIcon.Info);
                break;
            case OrchestrationOutcome.PartialSuccess:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", $"备份部分完成：{result.Message}", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.DriveNotConnected:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "备份硬盘未连接，请插入后等待下次备份，或点击“立即备份”重试。", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.InsufficientSpace:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "备份硬盘剩余空间不足，本次备份已跳过。", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.SourceTargetOverlap:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "源文件夹与备份目标重叠，已阻止本次备份，请检查设置。", ToolTipIcon.Error);
                break;
            case OrchestrationOutcome.NoSourceFolders:
                if (manualTrigger) _trayIcon.ShowBalloonTip(5000, "Auto Backup", "还没有设置备份来源文件夹。", ToolTipIcon.Warning);
                break;
        }
    }

    private string BuildTrayText()
    {
        var last = _settings.LastRunAt.HasValue ? _settings.LastRunAt.Value.ToString("MM-dd HH:mm") : "从未";
        return $"Auto Backup - 上次备份: {last}";
    }

    private void OnOpenSettingsClicked(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = form.Settings;
            SettingsStore.Save(_settings, _settingsPath);
            StartupRegistration.Apply(_settings.StartWithWindows, Application.ExecutablePath);
            _trayIcon.Text = BuildTrayText();
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _schedulerTimer.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
