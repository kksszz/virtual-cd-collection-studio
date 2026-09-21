using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ZipMp3Player;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<MusicFolderOption> _folders;
    private readonly string _dataDirectory;
    private bool _clearingArtworkCache;
    internal IReadOnlyList<ZipAlbum> LibraryAlbums { get; init; } = [];
    private readonly CancellationTokenSource _sizeCancellation = new();
    private LibrarySizeSummary? _sizeSummary;
    private bool _sizeCalculating;
    private readonly List<FolderRelocation> _relocations = [];
    public IReadOnlyList<string> Folders => _folders.Select(folder => folder.Path).ToList();
    public IReadOnlyList<string> DisabledFolders => _folders.Where(folder => !folder.IsEnabled).Select(folder => folder.Path).ToList();
    public bool RescanRequested { get; private set; }
    internal IReadOnlyList<FolderRelocation> Relocations => _relocations;
    public bool RestoreCompleted { get; private set; }
    public string? RestoreSafetyBackupPath { get; private set; }
    public bool MinimizeOnClose => MinimizeOnCloseCheck.IsChecked == true;
    public bool AutomaticArtworkEnabled
    {
        get => AutomaticArtworkCheck.IsChecked == true;
        set => AutomaticArtworkCheck.IsChecked = value;
    }
    public bool AutomaticArtworkPaused
    {
        get => AutomaticArtworkPauseCheck.IsChecked == true;
        set => AutomaticArtworkPauseCheck.IsChecked = value;
    }
    public bool TagBackupEnabled => TagBackupCheck.IsChecked == true;
    public string TagBackupFolder => TagBackupFolderTextBox.Text.Trim();
    public string DisplayLanguage => (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString()
        ?? LocalizationService.Japanese;
    public bool ExitRequested { get; private set; }

    public SettingsWindow(IEnumerable<string> folders, IEnumerable<string> disabledFolders, bool minimizeOnClose,
        string dataDirectory, string displayLanguage = LocalizationService.Japanese,
        bool tagBackupEnabled = false, string tagBackupFolder = "")
    {
        InitializeComponent();
        _dataDirectory = dataDirectory;
        var disabled = new HashSet<string>(disabledFolders, StringComparer.OrdinalIgnoreCase);
        _folders = new ObservableCollection<MusicFolderOption>(folders.Select(path =>
            new MusicFolderOption(path, !disabled.Contains(path))));
        FolderList.ItemsSource = _folders;
        MinimizeOnCloseCheck.IsChecked = minimizeOnClose;
        TagBackupCheck.IsChecked = tagBackupEnabled;
        TagBackupFolderTextBox.Text = tagBackupFolder;
        UpdateTagBackupOption();
        LanguageCombo.SelectedIndex = string.Equals(displayLanguage, LocalizationService.English,
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        LocalizationService.SetLanguage(displayLanguage);
        LocalizationService.Apply(this);
        UpdateCount();
        Loaded += async (_, _) => await RefreshLibrarySizeAsync();
        Closed += (_, _) => _sizeCancellation.Cancel();
        Closing+=(_,e)=>{if(_clearingArtworkCache)e.Cancel=true;};
    }

    private async void ClearArtworkCache_Click(object sender,RoutedEventArgs e)
    {
        if(_clearingArtworkCache)return;
        if(MessageBox.Show(this,LocalizationService.Select(
            "保存済みの画像サムネイルキャッシュを削除しますか？\n\n元画像・取得画像・音楽・お気に入り・設定は削除しません。\n表示中の画像を読み直すには、削除後にアプリを再起動してください。\nこの操作は設定画面のキャンセルでは取り消されません。",
            "Delete saved image thumbnails?\n\nOriginal/downloaded artwork, music, favorites and settings will remain.\nRestart the app afterwards to reload images held in memory.\nCanceling Settings will not undo this operation."),
            LocalizationService.Select("画像キャッシュの削除","Clear image cache"),MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _clearingArtworkCache=true;ClearArtworkCacheButton.IsEnabled=false;
        ArtworkCacheStatus.Text=LocalizationService.Select("キャッシュを削除しています…","Clearing cache…");
        try{
            var result=await Task.Run(()=>ArtworkThumbnailCache.Clear(_dataDirectory));
            ArtworkCacheStatus.Text=LocalizationService.Select(
                $"{result.Deleted}件（{result.Bytes/1048576d:F1} MiB）を削除しました。アプリを再起動してください。",
                $"Deleted {result.Deleted} files ({result.Bytes/1048576d:F1} MiB). Please restart the app.");
            if(result.Failed>0)ArtworkCacheStatus.Text+=LocalizationService.Select($"\n{result.Failed}件は使用中などの理由で削除できませんでした。",$"\nCould not delete {result.Failed} files; they may be in use.");
        }
        catch(Exception ex){ArtworkCacheStatus.Text=LocalizationService.Select("削除に失敗しました: ","Unable to clear cache: ")+ex.Message;}
        finally{_clearingArtworkCache=false;ClearArtworkCacheButton.IsEnabled=true;}
    }

    private async void LibrarySizeRefresh_Click(object sender, RoutedEventArgs e) => await RefreshLibrarySizeAsync();

    private async Task RefreshLibrarySizeAsync()
    {
        if (_sizeCalculating || _sizeCancellation.IsCancellationRequested) return;
        _sizeCalculating = true;
        LibrarySizeRefreshButton.IsEnabled = false;
        LibrarySizeText.Text = LocalizationService.Select("ライブラリ容量を集計中…", "Calculating library size…");
        UpdateLibrarySizeScope();
        try
        {
            _sizeSummary = await Task.Run(() => LibrarySizeSummary.Calculate(LibraryAlbums, _sizeCancellation.Token));
            if (!_sizeCancellation.IsCancellationRequested) ShowLibrarySize();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_sizeCancellation.IsCancellationRequested)
                LibrarySizeText.Text = LocalizationService.Select("容量を集計できません: ", "Unable to calculate size: ") + ex.Message;
        }
        finally { _sizeCalculating = false; LibrarySizeRefreshButton.IsEnabled = true; }
    }

    private void UpdateLibrarySizeScope() => LibrarySizeScopeText.Text = LocalizationService.Select(
        "設定を開いた時点の登録アルバム（非表示を含む）。保存容量はZIP・ISOとアルバムフォルダ内の全ファイル。音声容量は登録情報による展開後の概算で、画像・変換後の増減は含みません。アプリ側の追加画像・バックアップ等は対象外です。",
        "Registered albums when opened, including hidden albums. Stored size includes ZIP/ISO and all album-folder files. Audio size is the cached, unpacked estimate, excluding artwork and conversion overhead. App-managed artwork and backups are excluded.");

    private void ShowLibrarySize()
    {
        UpdateLibrarySizeScope();
        if (_sizeSummary is not { } size) return;
        LibrarySizeText.Text = LocalizationService.Select(
            $"{size.Albums:N0}アルバム / {size.Tracks:N0}曲\n保存容量: {LibrarySizeSummary.Format(size.StoredBytes)}\n音声のみ（展開後・概算）: {LibrarySizeSummary.Format(size.AudioBytes)}",
            $"{size.Albums:N0} albums / {size.Tracks:N0} tracks\nStored: {LibrarySizeSummary.Format(size.StoredBytes)}\nAudio only (unpacked estimate): {LibrarySizeSummary.Format(size.AudioBytes)}")
            + (size.Unreadable > 0 ? LocalizationService.Select($"\n未集計: {size.Unreadable:N0}件（未接続・読取不可・リンク等）。保存容量は一部のみです。",
                $"\nNot counted: {size.Unreadable:N0} (offline, unreadable or links). Stored total is incomplete.") : "");
    }

    public SettingsWindow(IEnumerable<string> folders, IEnumerable<string> disabledFolders, string dataDirectory)
        : this(folders, disabledFolders, false, dataDirectory) { }

    public SettingsWindow(IEnumerable<string> folders, string dataDirectory) : this(folders, [], false, dataDirectory) { }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = LocalizationService.Select(
            "音楽ファイルをスキャンするフォルダを選択", "Select a folder to scan for music"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var folder = Path.GetFullPath(dialog.FolderName);
        var item = _folders.FirstOrDefault(existing => string.Equals(existing.Path, folder, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = new MusicFolderOption(folder, true);
            _folders.Add(item);
        }
        FolderList.SelectedItem = item;
        FolderList.ScrollIntoView(item);
        UpdateCount();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not MusicFolderOption folder) return;
        _folders.Remove(folder);
        if (_folders.Count > 0) FolderList.SelectedIndex = Math.Min(FolderList.SelectedIndex, _folders.Count - 1);
        UpdateCount();
    }

    private void RelocateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not MusicFolderOption selectedFolder)
        {
            MessageBox.Show(this, LocalizationService.Select("場所を変更する登録フォルダを選択してください。", "Select a registered folder to relocate."),
                LocalizationService.Select("フォルダ移行", "Folder Migration"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var oldPath = selectedFolder.Path;
        var dialog = new OpenFolderDialog { Title = LocalizationService.Select(
            $"「{oldPath}」の新しい場所を選択", $"Select the new location for \"{oldPath}\""), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var newPath = Path.GetFullPath(dialog.FolderName);
        if (_folders.Any(folder => !ReferenceEquals(folder, selectedFolder)
            && string.Equals(folder.Path, newPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, LocalizationService.Select("そのフォルダはすでに登録されています。", "That folder is already registered."),
                LocalizationService.Select("フォルダ移行", "Folder Migration"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var replacement = new MusicFolderOption(newPath, selectedFolder.IsEnabled);
        var index = _folders.IndexOf(selectedFolder);
        _folders[index] = replacement;
        var chained = _relocations.FindIndex(item => string.Equals(item.NewPath, oldPath, StringComparison.OrdinalIgnoreCase));
        if (chained >= 0) _relocations[chained] = _relocations[chained] with { NewPath = newPath };
        else _relocations.Add(new FolderRelocation(oldPath, newPath));
        FolderList.SelectedItem = replacement;
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Select("アプリデータのバックアップ先を選択", "Choose where to save the app-data backup"),
            Filter = "Virtual CD Collection Studio バックアップ (*.zipmp3backup)|*.zipmp3backup",
            FileName = $"Virtual-CD-Collection-Studio-Backup-{DateTime.Now:yyyyMMdd-HHmm}.zipmp3backup",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            AppDataBackupService.CreateBackup(_dataDirectory, dialog.FileName);
            MessageBox.Show(this, LocalizationService.Select($"バックアップを作成しました。\n\n{dialog.FileName}", $"Backup created.\n\n{dialog.FileName}"),
                LocalizationService.Select("バックアップ完了", "Backup Complete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.Select("バックアップできません", "Backup Failed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Select("復元するバックアップを選択", "Select a backup to restore"),
            Filter = "Virtual CD Collection Studio バックアップ (*.zipmp3backup)|*.zipmp3backup"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, LocalizationService.Select(
                "現在の設定とライブラリーデータをバックアップ内容で復元します。\n復元前の安全バックアップは自動作成されます。続行しますか？",
                "Current settings and library data will be replaced from the backup.\nA safety backup will be created automatically first. Continue?"),
            LocalizationService.Select("バックアップから復元", "Restore Backup"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            RestoreSafetyBackupPath = AppDataBackupService.RestoreBackup(_dataDirectory, dialog.FileName);
            RestoreCompleted = true;
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.Select("復元できません", "Restore Failed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TryAccept(rescan: false);

    private void SaveAndRescan_Click(object sender, RoutedEventArgs e)
    {
        TryAccept(rescan: true);
    }

    private void TryAccept(bool rescan)
    {
        if (TagBackupEnabled)
        {
            if (string.IsNullOrWhiteSpace(TagBackupFolder))
            {
                MessageBox.Show(this, LocalizationService.Select("タグバックアップの保存先フォルダを選択してください。", "Select a folder for tag-edit backups."),
                    LocalizationService.Select("バックアップ保存先", "Backup Folder"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var folder = Path.GetFullPath(TagBackupFolder);
                Directory.CreateDirectory(folder);
                TagBackupFolderTextBox.Text = folder;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Select($"指定した保存先を使用できません。\n\n理由: {ex.Message}", $"The selected backup folder cannot be used.\n\nReason: {ex.Message}"),
                    LocalizationService.Select("バックアップ保存先", "Backup Folder"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        RescanRequested = rescan;
        DialogResult = true;
    }

    private void ChooseTagBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Select("タグ編集バックアップの保存先を選択", "Select the folder for tag-edit backups"),
            Multiselect = false
        };
        if (Directory.Exists(TagBackupFolder)) dialog.InitialDirectory = TagBackupFolder;
        if (dialog.ShowDialog(this) != true) return;
        TagBackupFolderTextBox.Text = Path.GetFullPath(dialog.FolderName);
    }

    private void TagBackupOption_Changed(object sender, RoutedEventArgs e) => UpdateTagBackupOption();

    private void UpdateTagBackupOption()
    {
        if (TagBackupFolderPanel is null) return;
        TagBackupFolderPanel.IsEnabled = TagBackupEnabled;
        TagBackupFolderPanel.Opacity = TagBackupEnabled ? 1 : 0.45;
    }

    private void ExitApplication_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, LocalizationService.Select("再生を停止してアプリを終了しますか？", "Stop playback and exit the application?"),
            LocalizationService.Select("アプリを終了", "Exit Application"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ExitRequested = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void FolderEnabled_Changed(object sender, RoutedEventArgs e) => UpdateCount();

    private void Language_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        LocalizationService.SetLanguage(DisplayLanguage);
        LocalizationService.Apply(this);
        UpdateCount();
        ShowLibrarySize();
    }

    private void UpdateCount() => FolderCountText.Text = LocalizationService.T(
        $"表示 {_folders.Count(folder => folder.IsEnabled)} / 登録 {_folders.Count}フォルダ");
}

public sealed class MusicFolderOption(string path, bool isEnabled)
{
    public string Path { get; } = path;
    public bool IsEnabled { get; set; } = isEnabled;
}
