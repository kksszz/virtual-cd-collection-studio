using System.IO;
using System.Windows.Threading;

namespace ZipMp3Player;

public partial class MainWindow
{
    private DispatcherTimer? _editBackupTimer;
    private bool _editBackupCleanupRunning;
    private void StartEditBackupRetention()
    {
        _editBackupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _editBackupTimer.Tick += async (_, _) =>
        {
            if (_editBackupCleanupRunning || !_settings.AutoCleanupEditBackups || _dataOperations.ActiveCount > 0
                || _dataOperations.CloseRequested || _fullLibraryScanInProgress || _incrementalRefreshInProgress) return;
            _editBackupTimer.Interval = TimeSpan.FromHours(1);
            var roots = _folders.Where(root => !_disabledFolders.Contains(root)).ToArray();
            var backupFolder = _settings.TagBackupFolder;
            using var operation = _dataOperations.Begin();
            if (operation is null) return;
            _editBackupCleanupRunning = true;
            try
            {
                var cleanup = new EditBackupRetention(Path.Combine(DataDirectory, "edit-backup-retention.json"));
                var result = await Task.Run(() => cleanup.Run(roots, backupFolder, DateTimeOffset.UtcNow,
                    () => !_dataOperations.CloseRequested && _settings.AutoCleanupEditBackups
                        && _dataOperations.ActiveCount == 1 && !_fullLibraryScanInProgress && !_incrementalRefreshInProgress));
                if (result.Deleted > 0)
                    StatusText.Text = LocalizationService.Select(
                        $"7日間保管した編集バックアップを{result.Deleted}件削除しました（元の音楽・画像は保持）。",
                        $"Deleted {result.Deleted} edit backups retained for 7 days. Original music and images were kept.");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Edit backup cleanup skipped: " + ex.Message); }
            finally { _editBackupCleanupRunning = false; }
        };
        _editBackupTimer.Start();
    }
}
