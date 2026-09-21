namespace AutoBackup.App;

partial class SnapshotHistoryForm
{
    private System.ComponentModel.IContainer components = null;
    private ListView _snapshotListView;
    private Label _summaryLabel;

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

        _summaryLabel = new Label { Left = 10, Top = 350, Width = 560, Height = 30 };

        Text = "快照历史";
        ClientSize = new Size(580, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(_snapshotListView);
        Controls.Add(_summaryLabel);
    }
}
