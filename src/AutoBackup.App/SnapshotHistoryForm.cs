using AutoBackup.Core.Backup;
using AutoBackup.Core.Formatting;

namespace AutoBackup.App;

public partial class SnapshotHistoryForm : Form
{
    public SnapshotHistoryForm(string backupRoot)
    {
        InitializeComponent();

        var history = SnapshotHistory.GetHistory(backupRoot);
        foreach (var snapshot in history)
        {
            var item = new ListViewItem(snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm"));
            item.SubItems.Add(ByteSizeFormatter.Format(snapshot.TotalBytes));
            item.SubItems.Add(snapshot.FileCount.ToString());
            item.SubItems.Add(Path.GetFileName(snapshot.Path));
            _snapshotListView.Items.Add(item);
        }

        _summaryLabel.Text = history.Count == 0
            ? "还没有任何快照。"
            // Each snapshot lists its full contents, but unchanged files are shared between
            // snapshots via hardlinks - so these sizes overlap heavily and must not be summed.
            : $"共 {history.Count} 个快照。注意：未改动的文件在各快照之间共享，实际占用远小于各行相加。";
    }
}
