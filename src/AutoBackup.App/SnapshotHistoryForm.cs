using System.Diagnostics;
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
            var item = new ListViewItem(snapshot.Timestamp.ToString("yyyy-MM-dd HH:mm")) { Tag = snapshot.Path };
            item.SubItems.Add(ByteSizeFormatter.Format(snapshot.TotalBytes));
            item.SubItems.Add(snapshot.FileCount.ToString());
            item.SubItems.Add(Path.GetFileName(snapshot.Path));
            _snapshotListView.Items.Add(item);
        }

        _summaryLabel.Text = history.Count == 0
            ? "还没有任何快照。"
            // Each snapshot lists its full contents, but unchanged files are shared between
            // snapshots via hardlinks - so these sizes overlap heavily and must not be summed.
            : $"共 {history.Count} 个快照。未改动的文件在快照之间共享，实际占用远小于各行相加。";

        _snapshotListView.SelectedIndexChanged += (_, _) =>
            _openFolderButton.Enabled = _snapshotListView.SelectedItems.Count > 0;
        _snapshotListView.DoubleClick += (_, _) => OpenSelectedSnapshot();
        _openFolderButton.Click += (_, _) => OpenSelectedSnapshot();
    }

    /// <summary>
    /// Restoring a file means browsing the snapshot, so give it a one-click path into Explorer
    /// rather than making the user hunt down the backup folder themselves.
    /// </summary>
    private void OpenSelectedSnapshot()
    {
        if (_snapshotListView.SelectedItems.Count == 0) return;
        if (_snapshotListView.SelectedItems[0].Tag is not string path) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打不开这个文件夹：{ex.Message}", "Auto Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
