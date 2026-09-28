using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ZipMp3Player;

public partial class AlbumPropertiesWindow : Window
{
    private readonly Func<AlbumProperties> _load;
    private readonly bool _isTrack;
    private string _sourcePath;
    private bool _closed;
    public AlbumPropertiesWindow(ZipAlbum album) : this(() => AlbumProperties.Load(album), album.Path, false) { }
    public AlbumPropertiesWindow(ZipTrack track) : this(() => TrackProperties.Load(track), track.SourcePath, true) { }

    private AlbumPropertiesWindow(Func<AlbumProperties> load, string sourcePath, bool isTrack)
    {
        _load = load;
        _sourcePath = sourcePath;
        _isTrack = isTrack;
        InitializeComponent();
        if (isTrack) Title = HeadingText.Text = "曲のプロパティ";
        LocalizationService.Apply(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var properties = await Task.Run(_load);
            if (_closed) return;
            _sourcePath = properties.SourcePath;
            PropertyRows.ItemsSource = properties.Rows;
            OpenLocationButton.IsEnabled = properties.Exists;
            StatusText.Text = LocalizationService.Select("参照専用です。各項目の文字列を選択してコピーできます。",
                "Read-only. Select any value to copy it.");
            if (_isTrack) StatusText.Text += LocalizationService.Select(
                " 音声情報とZIP内のサイズは登録時の情報です。",
                " Audio details and sizes inside ZIPs are from the library index.");
        }
        catch (Exception ex)
        {
            if (!_closed) StatusText.Text = LocalizationService.Select("情報を取得できませんでした: ", "Could not read information: ") + ex.Message;
        }
    }

    private void Window_Closed(object? sender, EventArgs e) => _closed = true;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_sourcePath); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(_sourcePath)) Process.Start(new ProcessStartInfo(_sourcePath) { UseShellExecute = true });
            else if (File.Exists(_sourcePath)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_sourcePath}\"") { UseShellExecute = true });
            else throw new FileNotFoundException(LocalizationService.Select("元のファイルまたはフォルダが見つかりません", "Source file or folder not found"));
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
}
