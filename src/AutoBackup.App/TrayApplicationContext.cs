using System.Threading;
using AutoBackup.Core.Drives;
using AutoBackup.Core.Formatting;
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
    private readonly ToolStripItem _backupNowMenuItem;
    private readonly DeviceArrivalWatcher _deviceArrivalWatcher;
    private BackupSettings _settings;
    private int _isBackupRunning;
    // Set once a due backup has been reported as un-runnable (drive absent, disk full). It
    // both suppresses duplicate balloons and parks the retry loop: rather than re-scanning
    // drives every minute, the app waits to be told a volume arrived.
    private DateTime? _skipReportedForDueDate;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load(_settingsPath);
        _logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backup.log"));

        // Reconcile the registry startup entry with the current setting on every launch, not
        // just when the user opens and saves Settings once.
        StartupRegistration.Apply(_settings.StartWithWindows, Application.ExecutablePath);

        var menu = new ContextMenuStrip();
        _backupNowMenuItem = menu.Items.Add("立即备份", null, (_, _) => StartBackup(manualTrigger: true));
        menu.Items.Add("打开设置", null, OnOpenSettingsClicked);
        menu.Items.Add("查看备份日志", null, OnViewLogClicked);
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

        _deviceArrivalWatcher = new DeviceArrivalWatcher();
        _deviceArrivalWatcher.VolumeArrived += OnVolumeArrived;

        _schedulerTimer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(60));

        // Capture last, after the ContextMenuStrip (a Control) above has been constructed, so
        // this actually picks up the real WindowsFormsSynchronizationContext instead of null
        // (see field comment above).
        _uiContext = SynchronizationContext.Current;

        // One sweep at startup covers snapshots that expired while the app wasn't running;
        // after this, cleanup only happens on a drive arriving or a backup finishing.
        Task.Run(RunStandaloneCleanup);
    }

    /// <summary>
    /// Pure schedule arithmetic - it never touches a disk unless a backup is actually due, so
    /// an idle drive is left alone to sleep.
    /// </summary>
    private void OnTimerTick(object? state)
    {
        if (!IsBackupDue()) return;

        // Already told the user this due period can't run; wait for a volume to arrive rather
        // than re-scanning drives every minute.
        if (_skipReportedForDueDate == DateTime.Now.Date) return;

        RunBackup(manualTrigger: false);
    }

    private bool IsBackupDue()
    {
        var config = new ScheduleConfig { Days = _settings.ScheduleDays, Time = _settings.ScheduleTime };
        return BackupScheduler.IsDueNow(config, DateTime.Now, _settings.LastSuccessfulRunAt);
    }

    /// <summary>
    /// A volume appeared: this is the moment a missed backup can finally run, and the only
    /// time housekeeping is worth doing outside a backup.
    /// </summary>
    private void OnVolumeArrived()
    {
        _skipReportedForDueDate = null;

        Task.Run(() =>
        {
            if (IsBackupDue())
            {
                RunBackup(manualTrigger: false);
                return;
            }

            RunStandaloneCleanup();
        });
    }

    /// <summary>
    /// Prunes expired snapshots independently of whether a backup runs. Without this, a user
    /// whose backups keep getting skipped (drive unplugged at the scheduled time, schedule
    /// day unchecked) would see expired snapshots pile up, because cleanup used to happen
    /// only as a side effect of a successful backup.
    /// </summary>
    private void RunStandaloneCleanup()
    {
        // A backup in progress already runs cleanup itself at the end; skip rather than race it.
        if (Interlocked.CompareExchange(ref _isBackupRunning, 1, 0) != 0) return;

        try
        {
            new BackupOrchestrator(_driveScanner, _logger).RunCleanupOnly(_settings);
        }
        catch (Exception ex)
        {
            try
            {
                _logger.Append(new BackupLogEntry { Timestamp = DateTime.Now, Outcome = "Error", Message = $"清理旧快照失败：{ex.Message}" });
            }
            catch
            {
                // best effort - logging itself must not crash the process
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isBackupRunning, 0);
        }
    }

    /// <summary>
    /// Entry point for a user-initiated backup. Unlike the scheduled path this always gives
    /// feedback, including when it declines to start, so clicking 立即备份 never looks like a
    /// dead button.
    /// </summary>
    private void StartBackup(bool manualTrigger)
    {
        if (Volatile.Read(ref _isBackupRunning) != 0)
        {
            _trayIcon.ShowBalloonTip(3000, "Auto Backup", "备份正在进行中，请稍候…", ToolTipIcon.Info);
            return;
        }

        // Copying files on the UI thread freezes the tray menu and every window for the whole
        // backup - Windows paints the app as "not responding" on a large first run.
        Task.Run(() => RunBackup(manualTrigger));
    }

    private void RunBackup(bool manualTrigger)
    {
        // Guard against a scheduled tick firing while a manual backup (or another tick) is
        // still running - without this, two overlapping runs could collide on the same
        // .inprogress/snapshot folder.
        if (Interlocked.CompareExchange(ref _isBackupRunning, 1, 0) != 0) return;

        try
        {
            PostToUiThread(() => SetBusyUi(true, "备份中…"));

            OrchestrationResult result;
            bool reportSkip;
            try
            {
                var orchestrator = new BackupOrchestrator(_driveScanner, _logger);
                var progress = new Progress<int>(processed =>
                    _trayIcon.Text = Truncate($"Auto Backup - 备份中… 已处理 {processed} 个文件"));

                reportSkip = manualTrigger || _skipReportedForDueDate != DateTime.Now.Date;
                result = orchestrator.RunOnce(_settings, logSkips: reportSkip, progress: progress);

                if (result.Outcome is OrchestrationOutcome.Success or OrchestrationOutcome.PartialSuccess)
                {
                    // Only a run that actually backed something up may claim today's slot or
                    // the tray's "last backup" time; a skipped run must stay due and stay
                    // visibly un-backed-up.
                    _settings.LastSuccessfulRunAt = DateTime.Now;
                    SettingsStore.Save(_settings, _settingsPath);
                }
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
                {
                    SetBusyUi(false, BuildTrayText());
                    _trayIcon.ShowBalloonTip(5000, "Auto Backup", $"备份出现意外错误：{ex.Message}", ToolTipIcon.Error);
                });
                return;
            }

            PostToUiThread(() =>
            {
                SetBusyUi(false, BuildTrayText());
                ApplyResultToUi(result, manualTrigger, reportSkip);
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isBackupRunning, 0);
        }
    }

    private void SetBusyUi(bool busy, string trayText)
    {
        _backupNowMenuItem.Enabled = !busy;
        _trayIcon.Text = Truncate(trayText);
    }

    // NotifyIcon.Text throws above 63 characters.
    private static string Truncate(string text) => text.Length <= 63 ? text : text[..60] + "…";

    private void ApplyResultToUi(OrchestrationResult result, bool manualTrigger, bool reportSkip)
    {
        _trayIcon.Text = Truncate(BuildTrayText());

        if (result.Outcome is OrchestrationOutcome.Success or OrchestrationOutcome.PartialSuccess)
        {
            _skipReportedForDueDate = null;
        }
        else if (!manualTrigger)
        {
            // Remember that this due period's skip has been reported, so the once-a-minute
            // catch-up retries stay silent until the situation actually changes.
            if (!reportSkip) return;
            _skipReportedForDueDate = DateTime.Now.Date;
        }

        switch (result.Outcome)
        {
            case OrchestrationOutcome.Success:
                if (manualTrigger) _trayIcon.ShowBalloonTip(3000, "Auto Backup", BuildSuccessMessage(result), ToolTipIcon.Info);
                break;
            case OrchestrationOutcome.PartialSuccess:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", $"备份部分完成：{result.Message}", ToolTipIcon.Warning);
                break;
            case OrchestrationOutcome.DriveNotConnected:
                _trayIcon.ShowBalloonTip(5000, "Auto Backup", "备份硬盘未连接。插上硬盘后会自动补做这次备份。", ToolTipIcon.Warning);
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

    private static string BuildSuccessMessage(OrchestrationResult result)
    {
        var backup = result.BackupResult;
        if (backup == null) return "备份完成";

        var size = ByteSizeFormatter.Format(backup.BytesCopied);
        return backup.FilesCopied == 0
            ? "备份完成，没有文件发生变化"
            : $"备份完成，新增 {size}（{backup.FilesCopied} 个文件）";
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
        var last = _settings.LastSuccessfulRunAt.HasValue
            ? _settings.LastSuccessfulRunAt.Value.ToString("MM-dd HH:mm")
            : "从未";
        return $"Auto Backup - 上次备份: {last}";
    }

    private void OnViewLogClicked(object? sender, EventArgs e)
    {
        using var form = new LogViewerForm(_logger);
        form.ShowDialog();
    }

    private void OnOpenSettingsClicked(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK) return;

        var updated = form.Settings;
        // The dialog captured its copy when it opened; a backup that finished while it was
        // open must not have its timestamp rolled back by saving the stale value.
        updated.LastSuccessfulRunAt = _settings.LastSuccessfulRunAt;

        _settings = updated;
        SettingsStore.Save(_settings, _settingsPath);
        StartupRegistration.Apply(_settings.StartWithWindows, Application.ExecutablePath);
        _trayIcon.Text = Truncate(BuildTrayText());
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _isBackupRunning) != 0)
        {
            var confirm = MessageBox.Show(
                "备份正在进行中，现在退出会中断它（这次的快照会作废，下次重新备份）。确定要退出吗？",
                "Auto Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        _schedulerTimer.Dispose();
        _deviceArrivalWatcher.VolumeArrived -= OnVolumeArrived;
        _deviceArrivalWatcher.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}
