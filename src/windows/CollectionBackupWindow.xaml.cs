using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;

namespace ZipMp3Player;

internal sealed class BackupFolderChoice(string path)
{
    public string Path { get; } = path;
    public bool Include { get; set; }
}

public partial class CollectionBackupWindow : Window
{
    private readonly string dataDirectory;
    private readonly string applicationDirectory;
    private readonly ObservableCollection<BackupFolderChoice> folders;
    private CancellationTokenSource? operation;
    private bool busy;

    internal CollectionBackupWindow(string dataDirectory, string applicationDirectory, IEnumerable<string> registeredFolders)
    {
        InitializeComponent();
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.applicationDirectory = Path.GetFullPath(applicationDirectory);
        folders = new(registeredFolders.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new BackupFolderChoice(path)));
        FolderChoices.ItemsSource = folders;
        if (folders.Count == 0) EstimateText.Text += "\n登録された音楽フォルダーはありません。";
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }

    private IReadOnlyList<CollectionBackupSource> SelectedSources() =>
        CollectionBackupService.Sources(dataDirectory, applicationDirectory, DataCheck.IsChecked == true,
            ApplicationCheck.IsChecked == true, folders.Where(folder => folder.Include).Select(folder => folder.Path).ToArray());

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var folder in folders) folder.Include = true;
        FolderChoices.Items.Refresh();
    }

    private void SelectEverything_Click(object sender, RoutedEventArgs e)
    {
        DataCheck.IsChecked = true;
        ApplicationCheck.IsChecked = true;
        foreach (var folder in folders) folder.Include = true;
        FolderChoices.Items.Refresh();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        DataCheck.IsChecked = false;
        ApplicationCheck.IsChecked = false;
        foreach (var folder in folders) folder.Include = false;
        FolderChoices.Items.Refresh();
    }

    private void ChooseBackup_Click(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog
        {
            Title = "完全バックアップの保存先を選択",
            Filter = "Virtual CD Collection Studio 完全バックアップ (*.vccsbackup)|*.vccsbackup",
            FileName = $"Virtual-CD-Collection-Studio-Full-{DateTime.Now:yyyyMMdd-HHmm}.vccsbackup",
            AddExtension = true,
            OverwritePrompt = false
        };
        if (picker.ShowDialog(this) == true) BackupPathText.Text = Path.GetFullPath(picker.FileName);
    }

    private void ChooseRestoreArchive_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "完全バックアップを選択",
            Filter = "Virtual CD Collection Studio 完全バックアップ (*.vccsbackup)|*.vccsbackup"
        };
        if (picker.ShowDialog(this) == true) RestoreArchiveText.Text = Path.GetFullPath(picker.FileName);
    }

    private void ChooseRestoreParent_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "復元先の親フォルダーを選択", Multiselect = false };
        if (picker.ShowDialog(this) == true)
            RestoreTargetText.Text = Path.Combine(Path.GetFullPath(picker.FolderName),
                "Virtual-CD-Collection-Restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }

    private bool Begin(string message)
    {
        if (busy) return false;
        busy = true;
        operation = new CancellationTokenSource();
        CreateButton.IsEnabled = RestoreButton.IsEnabled = CloseButton.IsEnabled = false;
        ModeTabs.IsEnabled = false;
        CancelOperationButton.IsEnabled = true;
        OperationProgress.Value = 0;
        OperationStatus.Text = message;
        return true;
    }

    private void Finish()
    {
        busy = false;
        operation?.Dispose(); operation = null;
        CreateButton.IsEnabled = RestoreButton.IsEnabled = CloseButton.IsEnabled = true;
        ModeTabs.IsEnabled = true;
        CancelOperationButton.IsEnabled = false;
    }

    private IProgress<CollectionBackupProgress> Progress() => new Progress<CollectionBackupProgress>(update =>
    {
        OperationProgress.Value = update.Total == 0 ? 100 : update.Completed * 100d / update.Total;
        OperationStatus.Text = $"{update.Completed:N0}/{update.Total:N0}: {update.Path}";
    });

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        operation?.Cancel();
        OperationStatus.Text = "中断しています…";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Estimate_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<CollectionBackupSource> selected;
        string destination;
        try
        {
            selected = SelectedSources();
            if (string.IsNullOrWhiteSpace(BackupPathText.Text))
                throw new InvalidDataException("バックアップの保存先を指定してください。");
            destination = Path.GetFullPath(BackupPathText.Text.Trim());
        }
        catch (Exception ex) { MessageBox.Show(this, "先に対象と保存先を選択してください。\n" + ex.Message); return; }
        if (!Begin("対象ファイルを数えています…")) return;
        try
        {
            var result = await Task.Run(() => CollectionBackupService.Estimate(selected, destination));
            EstimateText.Text = $"{result.Files:N0}ファイル / 元データ {result.Bytes / 1073741824d:N2} GiB。実際のバックアップ容量は圧縮方式によって変わります。";
            OperationStatus.Text = "見積もり完了";
        }
        catch (Exception ex) { OperationStatus.Text = "見積もり失敗: " + ex.Message; }
        finally { Finish(); }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<CollectionBackupSource> selected;
        string destination;
        try
        {
            selected = SelectedSources();
            if (string.IsNullOrWhiteSpace(BackupPathText.Text))
                throw new InvalidDataException("バックアップの保存先を指定してください。");
            destination = Path.GetFullPath(BackupPathText.Text.Trim());
            if (!destination.EndsWith(".vccsbackup", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("保存先には .vccsbackup を指定してください。");
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("既存ファイルは上書きしません。別の保存名を指定してください。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "バックアップ対象", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var folderCount = selected.Count(source => source.Id.StartsWith("Libraries/", StringComparison.Ordinal));
        if (MessageBox.Show(this,
            $"管理データ: {(DataCheck.IsChecked == true ? "含む" : "含まない")}\nアプリ本体: {(ApplicationCheck.IsChecked == true ? "含む" : "含まない")}\n音楽フォルダー: {folderCount}件\n\n保存先: {destination}\n\n大容量の可能性があります。作成しますか？",
            "完全バックアップの確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!Begin("バックアップを作成しています…")) return;
        try
        {
            var compression = CompressionChoice.SelectedIndex switch
            {
                1 => CompressionLevel.NoCompression,
                2 => CompressionLevel.Optimal,
                _ => CompressionLevel.Fastest
            };
            var result = await Task.Run(() => CollectionBackupService.Create(selected, destination, compression, Progress(), operation!.Token));
            OperationStatus.Text = $"作成・検証完了: {result.Files:N0}ファイル";
            MessageBox.Show(this, $"完全バックアップを作成し、SHA-256で検証しました。\n\n{result.Path}", "バックアップ完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { OperationStatus.Text = "中断しました。未完成ファイルは残していません。"; }
        catch (Exception ex) { OperationStatus.Text = "失敗: " + ex.Message; MessageBox.Show(this, ex.Message, "バックアップ失敗", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Finish(); }
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        var path = RestoreArchiveText.Text.Trim();
        if (!File.Exists(path)) { InspectText.Text = "バックアップファイルを選択してください。"; return; }
        if (!Begin("全ファイルのSHA-256を検証しています…")) return;
        try
        {
            var manifest = await Task.Run(() => CollectionBackupService.Validate(path, operation!.Token));
            InspectText.Text = $"検証成功: {manifest.Files.Count:N0}ファイル / {manifest.Files.Sum(file => file.Length) / 1073741824d:N2} GiB\n" +
                string.Join("\n", manifest.Sources.Select(source => $"{source.Id}: {source.OriginalPath}"));
            OperationStatus.Text = "バックアップは正常です";
        }
        catch (OperationCanceledException) { OperationStatus.Text = "検証を中断しました。"; }
        catch (Exception ex) { InspectText.Text = "検証失敗: " + ex.Message; OperationStatus.Text = "バックアップを確認してください。"; }
        finally { Finish(); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var archivePath = RestoreArchiveText.Text.Trim();
        string target;
        try
        {
            if (!File.Exists(archivePath)) throw new FileNotFoundException("バックアップファイルを選択してください。");
            target = Path.GetFullPath(RestoreTargetText.Text.Trim());
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("復元先には、まだ存在しない新しいフォルダー名を指定してください。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "復元先", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show(this,
            $"検証して新しいフォルダーへ展開します。既存のアプリ・音楽・設定は上書きしません。\n\n復元先: {target}\n\n続行しますか？",
            "復元の確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!Begin("バックアップを検証・復元しています…")) return;
        try
        {
            var result = await Task.Run(() => CollectionBackupService.RestoreToNewFolder(archivePath, target, Progress(), operation!.Token));
            OperationStatus.Text = $"復元完了: {result.Files:N0}ファイル";
            MessageBox.Show(this,
                $"新しい場所へ復元しました。既存データは変更していません。\n\n{result.Path}\n\nRESTORE-README.txtを確認し、必要な音楽フォルダーを設定画面で登録してください。",
                "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { OperationStatus.Text = "中断しました。既存データは変更していません。"; }
        catch (Exception ex) { OperationStatus.Text = "復元失敗: " + ex.Message; MessageBox.Show(this, ex.Message, "復元失敗", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Finish(); }
    }
}
