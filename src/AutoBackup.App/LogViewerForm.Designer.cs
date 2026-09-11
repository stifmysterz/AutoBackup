namespace AutoBackup.App;

partial class LogViewerForm
{
    private System.ComponentModel.IContainer components = null;
    private TextBox _logTextBox;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _logTextBox = new TextBox
        {
            Left = 0, Top = 0, Width = 600, Height = 400,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9)
        };
        Text = "备份日志";
        ClientSize = new Size(600, 400);
        Controls.Add(_logTextBox);
    }
}
