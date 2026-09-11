using System.Threading;
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
    // Used to marshal NotifyIcon access (which is not thread-safe) back from the ThreadPool
    // thread that System.Threading.Timer callbacks run on. Captured at the end of the
    // constructor rather than as a field initializer: field initializers run before the
    // constructor body, i.e. before the ContextMenuStrip below is constructed. It's a
    // Control's own constructor that installs WindowsFormsSynchronizationContext into
    // SynchronizationContext.Current (NotifyIcon is a Component, not a Control, and
    // contributes nothing to this) - capturing any earlier than that first Control's
    // construction would silently pick up null and fall back to running inline.
    private readonly SynchronizationContext? _uiContext;
    private BackupSettings _settings;
    private int _isBackupRunning;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load(_settingsPath);
        _logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backup.log"));

        // Reconcile the registry startup entry with the current setting on every launch, not
        // just when the user opens and saves Settings once.
        StartupRegistration.Apply(_settings.StartWithWindows, Application.ExecutablePath);

        var menu = new ContextMenuStrip();
        menu.Items.Add("立即备份", null, (_, _) => RunBackup(manualTrigger: true));
        menu.Items.Add("打开设置", null, OnOpenSettingsClicked);
        menu.Items.Add("查看备份日志", null, (_, _) => new LogViewerForm(_logger).ShowDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, OnExitClicked);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = BuildTrayText(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.ShowBalloonTip(5000, "Auto Backup", "已启动，正在后台运行", ToolTipIcon.Info);

        _schedulerTimer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));

        // Capture last, after the ContextMenuStrip (a Control) above has been constructed, so
        // this actually picks up the real WindowsFormsSynchronizationContext instead of null
        // (see field comment above).
        _uiContext = SynchronizationContext.Current;
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
        // Guard against a scheduled tick firing while a manual backup (or another tick) is
        // still running - without this, two overlapping runs could collide on the same
        // .inprogress/snapshot folder.
        if (Interlocked.CompareExchange(ref _isBackupRunning, 1, 0) != 0) return;

        try
        {
            OrchestrationResult result;
            try
            {
                var orchestrator = new BackupOrchestrator(_driveScanner, _logger);
                result = orchestrator.RunOnce(_settings);

                _settings.LastRunAt = DateTime.Now;
                SettingsStore.Save(_settings, _settingsPath);
            }
            catch (Exception ex)
            {
                // Never let an unexpected exception escape the timer callback or the click
                // handler - log it and surface a balloon instead of crashing the app.
                try
                {
                    _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Error", Message = ex.Message });
                }
                catch
                {
                    // best effort - logging itself must not crash the process
                }

                PostToUiThread(() =>
                    _trayIcon.ShowBalloonTip(5000, "Auto Backup", $"备份出现意外错误：{ex.Message}", ToolTipIcon.Error));
                return;
            }

            PostToUiThread(() => ApplyResultToUi(result, manualTrigger));
        }
        finally
        {
            Interlocked.Exchange(ref _isBackupRunning, 0);
        }
    }

    private void ApplyResultToUi(OrchestrationResult result, bool manualTrigger)
    {
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

    private void PostToUiThread(Action action)
    {
        if (_uiContext != null)
        {
            _uiContext.Post(_ => action(), null);
        }
        else
        {
            // Fall back to running inline rather than crashing if a UI SynchronizationContext
            // was somehow never captured.
            action();
        }
    }

    private static Icon LoadAppIcon()
    {
        // The app's own icon is embedded into the exe's Win32 resources at build time via
        // <ApplicationIcon> in the csproj, so pulling it back out of the running executable
        // guarantees it matches and needs no separate file alongside a single-file publish.
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null) return icon;
        }
        catch
        {
            // fall through to the system default below
        }
        return SystemIcons.Application;
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
        _trayIcon.Dispose();
        Application.Exit();
    }
}
