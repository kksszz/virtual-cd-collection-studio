using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ZipMp3Player;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<MusicFolderOption> _folders;
    private readonly string _dataDirectory;
    private readonly List<FolderRelocation> _relocations = [];
    public IReadOnlyList<string> Folders => _folders.Select(folder => folder.Path).ToList();
    public IReadOnlyList<string> DisabledFolders => _folders.Where(folder => !folder.IsEnabled).Select(folder => folder.Path).ToList();
    public bool RescanRequested { get; private set; }
    internal IReadOnlyList<FolderRelocation> Relocations => _relocations;
    public bool RestoreCompleted { get; private set; }
    public string? RestoreSafetyBackupPath { get; private set; }
    public bool MinimizeOnClose => MinimizeOnCloseCheck.IsChecked == true;
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
            Filter = "zip.mp3 Player and Manager Plus バックアップ (*.zipmp3backup)|*.zipmp3backup",
            FileName = $"zip.mp3-Player-and-Manager-Plus-Backup-{DateTime.Now:yyyyMMdd-HHmm}.zipmp3backup",
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
            Filter = "zip.mp3 Player and Manager Plus バックアップ (*.zipmp3backup)|*.zipmp3backup"
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
    }

    private void UpdateCount() => FolderCountText.Text = LocalizationService.T(
        $"表示 {_folders.Count(folder => folder.IsEnabled)} / 登録 {_folders.Count}フォルダ");
}

public sealed class MusicFolderOption(string path, bool isEnabled)
{
    public string Path { get; } = path;
    public bool IsEnabled { get; set; } = isEnabled;
}
