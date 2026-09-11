using AutoBackup.Core.Logging;

namespace AutoBackup.App;

public partial class LogViewerForm : Form
{
    public LogViewerForm(BackupLogger logger)
    {
        InitializeComponent();
        var lines = logger.ReadRecent(200);
        _logTextBox.Text = lines.Count == 0 ? "（还没有备份记录）" : string.Join(Environment.NewLine, lines);
    }
}
