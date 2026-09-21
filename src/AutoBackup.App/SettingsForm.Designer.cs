namespace AutoBackup.App;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;
    private ListBox _sourceListBox;
    private Button _addSourceButton;
    private Button _removeSourceButton;
    private Label _targetDriveLabel;
    private Button _changeDriveButton;
    private Button _snapshotHistoryButton;
    private CheckedListBox _scheduleDaysListBox;
    private DateTimePicker _scheduleTimePicker;
    private NumericUpDown _retentionDaysUpDown;
    private ListBox _excludeListBox;
    private Button _addExcludeButton;
    private Button _removeExcludeButton;
    private CheckBox _startWithWindowsCheckBox;
    private Button _saveButton;
    private Button _cancelButton;
    private Button _backupNowButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _sourceListBox = new ListBox { Left = 10, Top = 30, Width = 300, Height = 80 };
        var sourceLabel = new Label { Left = 10, Top = 10, Width = 200, Text = "备份来源：" };
        _addSourceButton = new Button { Left = 320, Top = 30, Width = 100, Text = "添加文件夹" };
        _removeSourceButton = new Button { Left = 320, Top = 60, Width = 100, Text = "删除选中" };

        _targetDriveLabel = new Label { Left = 10, Top = 120, Width = 400, Text = "备份目标：未设置" };
        _snapshotHistoryButton = new Button { Left = 200, Top = 145, Width = 110, Text = "查看快照历史" };
        _changeDriveButton = new Button { Left = 320, Top = 145, Width = 100, Text = "更改硬盘" };

        var scheduleLabel = new Label { Left = 10, Top = 180, Width = 200, Text = "备份计划（星期几）：" };
        _scheduleDaysListBox = new CheckedListBox { Left = 10, Top = 200, Width = 150, Height = 100 };
        _scheduleDaysListBox.Items.AddRange(new object[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" });
        var timeLabel = new Label { Left = 170, Top = 200, Width = 60, Text = "时间：" };
        _scheduleTimePicker = new DateTimePicker { Left = 170, Top = 220, Width = 100, Format = DateTimePickerFormat.Time, ShowUpDown = true };

        var retentionLabel = new Label { Left = 170, Top = 260, Width = 100, Text = "保留天数：" };
        _retentionDaysUpDown = new NumericUpDown { Left = 170, Top = 280, Width = 100, Minimum = 1, Maximum = 3650, Value = 30 };

        var excludeLabel = new Label { Left = 10, Top = 310, Width = 200, Text = "自定义排除清单：" };
        _excludeListBox = new ListBox { Left = 10, Top = 330, Width = 300, Height = 80 };
        _addExcludeButton = new Button { Left = 320, Top = 330, Width = 100, Text = "添加" };
        _removeExcludeButton = new Button { Left = 320, Top = 360, Width = 100, Text = "删除选中" };

        _startWithWindowsCheckBox = new CheckBox { Left = 10, Top = 420, Width = 200, Text = "开机自动启动" };

        _backupNowButton = new Button { Left = 10, Top = 450, Width = 120, Text = "立即备份一次" };
        _saveButton = new Button { Left = 250, Top = 450, Width = 80, Text = "保存", DialogResult = DialogResult.OK };
        _cancelButton = new Button { Left = 340, Top = 450, Width = 80, Text = "取消", DialogResult = DialogResult.Cancel };

        AutoScaleMode = AutoScaleMode.Font;
        Text = "Auto Backup 设置";
        ClientSize = new Size(440, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            sourceLabel, _sourceListBox, _addSourceButton, _removeSourceButton,
            _targetDriveLabel, _snapshotHistoryButton, _changeDriveButton,
            scheduleLabel, _scheduleDaysListBox, timeLabel, _scheduleTimePicker,
            retentionLabel, _retentionDaysUpDown,
            excludeLabel, _excludeListBox, _addExcludeButton, _removeExcludeButton,
            _startWithWindowsCheckBox,
            _backupNowButton, _saveButton, _cancelButton
        });
    }
}
