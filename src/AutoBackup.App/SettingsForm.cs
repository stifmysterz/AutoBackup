using AutoBackup.Core.Backup;
using AutoBackup.Core.Drives;
using AutoBackup.Core.Exclusion;
using AutoBackup.Core.Formatting;
using AutoBackup.Core.Models;
using AutoBackup.Core.Orchestration;
using AutoBackup.Core.Logging;
using CoreSettings = AutoBackup.Core.Settings.SettingsStore;

namespace AutoBackup.App;

public partial class SettingsForm : Form
{
    private static readonly DayOfWeek[] DayOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    private readonly IDriveScanner _driveScanner = new DriveScanner();
    private DriveInfoRecord? _selectedDrive;

    public BackupSettings Settings { get; private set; }

    public SettingsForm(BackupSettings current)
    {
        InitializeComponent();
        Settings = current;

        _sourceListBox.Items.AddRange(current.SourceFolders.ToArray());
        RefreshTargetDriveLabel();
        for (var i = 0; i < DayOrder.Length; i++)
            _scheduleDaysListBox.SetItemChecked(i, current.ScheduleDays.Contains(DayOrder[i]));
        _scheduleTimePicker.Value = DateTime.Today.Add(current.ScheduleTime.ToTimeSpan());
        _retentionDaysUpDown.Value = current.RetentionDays;
        _excludeListBox.Items.AddRange(current.CustomExcludePatterns.ToArray());
        _startWithWindowsCheckBox.Checked = current.StartWithWindows;

        _addSourceButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK) _sourceListBox.Items.Add(dialog.SelectedPath);
        };
        _removeSourceButton.Click += (_, _) =>
        {
            if (_sourceListBox.SelectedItem != null) _sourceListBox.Items.Remove(_sourceListBox.SelectedItem);
        };
        _changeDriveButton.Click += (_, _) => PickTargetDrive();
        _addExcludeFolderButton.Click += (_, _) => AddExcludedFolder();
        _addExcludePatternButton.Click += (_, _) => AddExcludePattern();
        _removeExcludeButton.Click += (_, _) =>
        {
            if (_excludeListBox.SelectedItem != null) _excludeListBox.Items.Remove(_excludeListBox.SelectedItem);
        };
        _backupNowButton.Click += (_, _) => RunBackupNow();
        _snapshotHistoryButton.Click += (_, _) => ShowSnapshotHistory();
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Picking a folder instead of typing its path rules out the typos that used to make a
    /// path rule silently match nothing.
    /// </summary>
    private void AddExcludedFolder()
    {
        var sources = _sourceListBox.Items.Cast<string>().ToList();
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择不需要备份的文件夹（必须在备份来源里面）",
            UseDescriptionForTitle = true
        };
        // Exclusions only matter inside a source, so start the user where they'll be looking.
        var start = sources.FirstOrDefault(Directory.Exists);
        if (start != null) dialog.InitialDirectory = start;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var folder = dialog.SelectedPath;

        if (!ExclusionRules.IsInsideAnySource(folder, sources))
        {
            var confirm = MessageBox.Show(
                $"\"{folder}\" 不在任何备份来源里面，排除它不会有任何效果。\n\n仍然要添加吗？",
                "Auto Backup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        AddExcludeEntry(folder);
    }

    private void AddExcludePattern()
    {
        var pattern = Microsoft.VisualBasic.Interaction.InputBox(
            "输入要排除的文件名或文件夹名，可以用 * 通配符。\n\n例如：*.log　*.iso　node_modules\n\n（要排除某个特定文件夹，用「选择文件夹」更方便）",
            "输入排除规则");
        AddExcludeEntry(pattern);
    }

    private void AddExcludeEntry(string entry)
    {
        entry = entry.Trim();
        if (entry.Length == 0) return;

        var alreadyListed = _excludeListBox.Items.Cast<string>()
            .Any(existing => string.Equals(existing.Trim(), entry, StringComparison.OrdinalIgnoreCase));
        if (alreadyListed) return;

        _excludeListBox.Items.Add(entry);
    }

    /// <summary>
    /// Shows the bound drive plus its live free/total space, so the user can see at a glance
    /// whether snapshots are filling the disk without digging through Explorer.
    /// </summary>
    private void RefreshTargetDriveLabel()
    {
        var serial = _selectedDrive?.VolumeSerial ?? Settings.TargetVolumeSerial;
        var label = _selectedDrive?.VolumeLabel ?? Settings.TargetVolumeLabel;

        if (string.IsNullOrEmpty(serial))
        {
            _targetDriveLabel.Text = "备份目标：未设置";
            return;
        }

        var connected = _selectedDrive ?? DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), serial);
        if (connected == null)
        {
            _targetDriveLabel.Text = $"备份目标：{DescribeDrive(label, null)}（当前未连接）";
            return;
        }

        _targetDriveLabel.Text =
            $"备份目标：{DescribeDrive(connected.VolumeLabel, connected.DriveLetter)}　" +
            $"剩余 {ByteSizeFormatter.Format(connected.FreeBytes)} / 共 {ByteSizeFormatter.Format(connected.TotalBytes)}";
    }

    /// <summary>Unlabelled volumes are common; showing just the letter beats a stray gap.</summary>
    private static string DescribeDrive(string? volumeLabel, string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(volumeLabel)) return driveLetter ?? "(无卷标)";
        return driveLetter == null ? volumeLabel : $"{volumeLabel} ({driveLetter})";
    }

    private void ShowSnapshotHistory()
    {
        var serial = _selectedDrive?.VolumeSerial ?? Settings.TargetVolumeSerial;
        if (string.IsNullOrEmpty(serial))
        {
            MessageBox.Show("还没有绑定备份硬盘。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var connected = _selectedDrive ?? DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), serial);
        if (connected == null)
        {
            MessageBox.Show("备份硬盘未连接，插上之后才能查看快照历史。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new SnapshotHistoryForm(SnapshotPathPlanner.GetBackupRoot(connected.DriveLetter));
        form.ShowDialog(this);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Only validate/commit on an accepted save (DialogResult.OK, from the 保存 button).
        // Cancel or closing via the window's X should just discard the in-progress edits.
        if (DialogResult != DialogResult.OK) return;

        var candidate = BuildSettingsFromForm();

        var driveLetter = _selectedDrive?.DriveLetter;
        if (driveLetter == null && !string.IsNullOrEmpty(candidate.TargetVolumeSerial))
        {
            // No drive was (re)picked in this session - fall back to whatever drive is
            // currently connected under the previously saved serial, if any.
            var match = DriveIdentifier.FindBySerial(_driveScanner.GetReadyDrives(), candidate.TargetVolumeSerial);
            driveLetter = match?.DriveLetter;
        }

        if (driveLetter != null && candidate.SourceFolders.Count > 0)
        {
            var backupRoot = SnapshotPathPlanner.GetBackupRoot(driveLetter);
            if (OverlapChecker.TryFindOverlap(candidate.SourceFolders, backupRoot, out var conflictingSource))
            {
                MessageBox.Show($"源文件夹 \"{conflictingSource}\" 与备份目标位置冲突，请调整后再保存。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
                DialogResult = DialogResult.None;
                return;
            }
        }

        Settings = candidate;
    }

    private void PickTargetDrive()
    {
        var drives = _driveScanner.GetReadyDrives();
        if (drives.Count == 0)
        {
            MessageBox.Show("没有检测到可用磁盘。", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var picker = new Form { Text = "选择备份硬盘", Width = 400, Height = 200, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var list = new ListBox { Left = 10, Top = 10, Width = 360, Height = 100 };
        foreach (var drive in drives)
            list.Items.Add($"{drive.DriveLetter} {drive.VolumeLabel} ({drive.FreeBytes / (1024 * 1024 * 1024)} GB 可用)");
        var okButton = new Button { Left = 200, Top = 120, Width = 80, Text = "选择", DialogResult = DialogResult.OK };
        picker.Controls.Add(list);
        picker.Controls.Add(okButton);
        picker.AcceptButton = okButton;

        if (picker.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
        {
            _selectedDrive = drives[list.SelectedIndex];
            RefreshTargetDriveLabel();
        }
    }

    private async void RunBackupNow()
    {
        var settings = BuildSettingsFromForm();
        var logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(CoreSettings.GetDefaultSettingsPath())!, "backup.log"));

        // Copying files on the UI thread locks up this dialog for the whole backup.
        var originalText = _backupNowButton.Text;
        _backupNowButton.Enabled = false;
        var progress = new Progress<int>(processed => _backupNowButton.Text = $"备份中… {processed}");

        try
        {
            var result = await Task.Run(() => new BackupOrchestrator(_driveScanner, logger).RunOnce(settings, progress: progress));
            MessageBox.Show(result.Message, "备份结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"备份出现意外错误：{ex.Message}", "备份结果", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _backupNowButton.Text = originalText;
            _backupNowButton.Enabled = true;
            RefreshTargetDriveLabel();
        }
    }

    private BackupSettings BuildSettingsFromForm()
    {
        var days = new List<DayOfWeek>();
        for (var i = 0; i < DayOrder.Length; i++)
            if (_scheduleDaysListBox.GetItemChecked(i)) days.Add(DayOrder[i]);

        return new BackupSettings
        {
            SourceFolders = _sourceListBox.Items.Cast<string>().ToList(),
            TargetVolumeSerial = _selectedDrive?.VolumeSerial ?? Settings.TargetVolumeSerial,
            TargetVolumeLabel = _selectedDrive?.VolumeLabel ?? Settings.TargetVolumeLabel,
            ScheduleDays = days,
            ScheduleTime = TimeOnly.FromDateTime(_scheduleTimePicker.Value),
            RetentionDays = (int)_retentionDaysUpDown.Value,
            CustomExcludePatterns = _excludeListBox.Items.Cast<string>().ToList(),
            StartWithWindows = _startWithWindowsCheckBox.Checked,
            LastSuccessfulRunAt = Settings.LastSuccessfulRunAt
        };
    }
}
