namespace AutoBackup.App;

partial class SnapshotHistoryForm
{
    private System.ComponentModel.IContainer components = null;
    private ListView _snapshotListView;
    private Label _summaryLabel;
    private Button _openFolderButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _snapshotListView = new ListView
        {
            Left = 10, Top = 10, Width = 560, Height = 330,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _snapshotListView.Columns.Add("备份时间", 160);
        _snapshotListView.Columns.Add("大小", 100);
        _snapshotListView.Columns.Add("文件数", 80);
        _snapshotListView.Columns.Add("文件夹", 200);

        _openFolderButton = new Button { Left = 10, Top = 348, Width = 150, Height = 28, Text = "打开选中的快照", Enabled = false };
        _summaryLabel = new Label { Left = 170, Top = 352, Width = 400, Height = 34 };

        AutoScaleMode = AutoScaleMode.Font;
        Text = "快照历史";
        ClientSize = new Size(580, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(_snapshotListView);
        Controls.Add(_openFolderButton);
        Controls.Add(_summaryLabel);
    }
}
