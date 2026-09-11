using AutoBackup.Core.Drives;
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
        _targetDriveLabel.Text = string.IsNullOrEmpty(current.TargetVolumeLabel)
            ? "备份目标：未设置"
            : $"备份目标：{current.TargetVolumeLabel}（未连接时会在备份时提醒）";
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
        _addExcludeButton.Click += (_, _) =>
        {
            var pattern = Microsoft.VisualBasic.Interaction.InputBox("输入要排除的文件夹路径或文件名模式（如 *.log）：", "添加排除项");
            if (!string.IsNullOrWhiteSpace(pattern)) _excludeListBox.Items.Add(pattern);
        };
        _removeExcludeButton.Click += (_, _) =>
        {
            if (_excludeListBox.SelectedItem != null) _excludeListBox.Items.Remove(_excludeListBox.SelectedItem);
        };
        _backupNowButton.Click += (_, _) => RunBackupNow();
        _saveButton.Click += (_, _) => SaveIntoSettings();
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
            _targetDriveLabel.Text = $"备份目标：{_selectedDrive.VolumeLabel} ({_selectedDrive.DriveLetter})";
        }
    }

    private void RunBackupNow()
    {
        var settings = BuildSettingsFromForm();
        var logger = new BackupLogger(Path.Combine(Path.GetDirectoryName(CoreSettings.GetDefaultSettingsPath())!, "backup.log"));
        var orchestrator = new BackupOrchestrator(_driveScanner, logger);
        var result = orchestrator.RunOnce(settings);
        MessageBox.Show(result.Message, "备份结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveIntoSettings() => Settings = BuildSettingsFromForm();

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
            LastRunAt = Settings.LastRunAt
        };
    }
}
