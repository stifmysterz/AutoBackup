using AutoBackup.Core.Logging;

namespace AutoBackup.App;

public partial class LogViewerForm : Form
{
    private static readonly Dictionary<string, string> OutcomeLabels = new()
    {
        ["Success"] = "成功",
        ["PartialSuccess"] = "部分完成",
        ["Skipped"] = "已跳过",
        ["CleanupFailed"] = "清理失败",
        ["Error"] = "错误",
        ["FatalError"] = "严重错误"
    };

    public LogViewerForm(BackupLogger logger)
    {
        InitializeComponent();

        // Newest first: the reason someone opens this is almost always "what happened just now".
        foreach (var line in logger.ReadRecent(500).Reverse())
        {
            var parts = line.Split('\t', 3);
            var item = new ListViewItem(parts[0]);
            var outcome = parts.Length > 1 ? parts[1] : string.Empty;
            item.SubItems.Add(OutcomeLabels.TryGetValue(outcome, out var label) ? label : outcome);
            item.SubItems.Add(parts.Length > 2 ? parts[2] : string.Empty);
            _logListView.Items.Add(item);
        }

        if (_logListView.Items.Count == 0)
        {
            _logListView.Items.Add(new ListViewItem(new[] { string.Empty, string.Empty, "（还没有备份记录）" }));
        }
    }
}
