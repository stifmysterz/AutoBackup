namespace AutoBackup.App;

partial class LogViewerForm
{
    private System.ComponentModel.IContainer components = null;
    private ListView _logListView;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _logListView = new ListView
        {
            Left = 0, Top = 0, Width = 700, Height = 400,
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _logListView.Columns.Add("时间", 140);
        _logListView.Columns.Add("结果", 110);
        _logListView.Columns.Add("详情", 420);

        AutoScaleMode = AutoScaleMode.Font;
        Text = "备份日志";
        ClientSize = new Size(700, 400);
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(_logListView);
    }
}
