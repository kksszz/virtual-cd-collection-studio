using System.Windows;

namespace ZipMp3Player;

public partial class MainWindow
{
    private async void ImportCueMetadata_Click(object sender, RoutedEventArgs e)
    {
        var album = _album;
        if (album is null || !CueAlbumReader.IsCue(album.Path))
        {
            MessageBox.Show(this, "CUE付き音楽CDイメージのアルバムを選択してください。", "CUE");
            return;
        }
        try
        {
            var disc = await Task.Run(() => CueAlbumReader.Read(album.Path));
            var dialog = new CueMetadataWindow(disc) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedMetadata is not { } metadata) return;
            var refreshed = await Task.Run(() =>
            {
                var current = CueAlbumReader.Read(album.Path);
                CueMetadataStore.Save(current, metadata); // Reject if the source changed during lookup.
                return CueAlbumReader.Open(album.Path);
            });
            ReplaceLibraryAlbum(album, refreshed, (TrackGrid.SelectedItem as ZipTrack)?.FileName);
            if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, refreshed.Path))
            {
                _playingAlbum = refreshed;
                if (_currentIndex >= 0 && _currentIndex < refreshed.Tracks.Count) UpdateNowPlayingHeader(refreshed.Tracks[_currentIndex]);
            }
            SaveLibraryCache();
            _libraryChangeLogStore.Add("Metadata", album.Path, metadata.Album, refreshed.Tracks.Count);
            _libraryChangeLogStore.Save();
            StatusText.Text = $"CUEの曲情報を反映しました: {metadata.Album}（元ファイルは変更していません）";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CUEの曲情報", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
