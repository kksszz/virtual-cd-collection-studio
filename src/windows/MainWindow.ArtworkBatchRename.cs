using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ZipMp3Player;

public partial class MainWindow
{
    private async void RenameAlbumImages_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null || _albumImages.Count == 0)
        {
            MessageBox.Show(this, "先に画像のあるアルバムを選択してください。", "画像名の一括編集",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var album = _album;
        var images = _albumImages.Select(source => new ArtworkRenameImage(
            source.ZipEntry?.SourcePath ?? source.FilePath!, source.ZipEntry?.FileName)).ToArray();
        var dialog = new ArtworkBatchRenameWindow(images) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var changed = dialog.Requests.Where(request =>
        {
            var original = request.ArchiveEntry ?? request.SourcePath;
            return !string.Equals(Path.GetFileNameWithoutExtension(original.Replace('/', Path.DirectorySeparatorChar)),
                request.NewBaseName, StringComparison.Ordinal);
        }).ToArray();
        if (changed.Length == 0) { StatusText.Text = "画像名の変更はありませんでした。"; return; }

        using var operation = _dataOperations.Begin();
        if (operation is null) return;
        var selectedKey = _selectedAlbumImageIndex >= 0 && _selectedAlbumImageIndex < _albumImages.Count
            ? _albumImages[_selectedAlbumImageIndex].RoleKey : null;
        var trackName = (TrackGrid.SelectedItem as ZipTrack)?.FileName;
        if (changed.Any(request => request.ArchiveEntry is not null)
            && string.Equals(_playingAlbum?.Path, album.Path, StringComparison.OrdinalIgnoreCase))
            StopPlayback(resetPosition: false);
        AlbumImagePanel.IsEnabled = false;
        AlbumList.IsEnabled = false;
        TrackGrid.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        var renamed = false;
        try
        {
            StatusText.Text = $"{changed.Length}枚の画像名を保存しています…";
            var result = await Task.Run(() => ArtworkBatchRenameService.Apply(changed));
            renamed = true;
            Remap(_currentArtworkRoles, result.RoleKeys);
            Remap(_currentArtworkRotations, result.RoleKeys);
            var folds = LoadSpineCardFolds(album.Path);
            Remap(folds, result.RoleKeys);
            SaveArtworkRoles(album.Path, _currentArtworkRoles);
            SaveArtworkRotations(album.Path, _currentArtworkRotations);
            SaveSpineCardFolds(album.Path, folds);
            if (changed.Any(request => request.ArchiveEntry is not null))
            {
                ReplaceLibraryAlbum(album, ZipAlbumReader.Open(album.Path), trackName);
                SaveLibraryCache();
            }
            else if (Directory.Exists(album.Path))
            {
                ReplaceLibraryAlbum(album, ZipAlbumReader.OpenFolder(album.Path), trackName);
                SaveLibraryCache();
            }
            else
            {
                _albums.FirstOrDefault(item => string.Equals(item.Album.Path, album.Path, StringComparison.OrdinalIgnoreCase))?.RefreshImageCount();
                LoadAlbumImages(album);
            }
            if (selectedKey is not null)
            {
                var targetKey = result.RoleKeys.GetValueOrDefault(selectedKey, selectedKey);
                var index = _albumImages.FindIndex(image => string.Equals(image.RoleKey, targetKey, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) { _albumImageIndex = index; _selectedAlbumImageIndex = index; ShowCurrentAlbumImage(); }
            }
            QueueCoverFlowRefresh();
            StatusText.Text = $"画像名を{changed.Length}枚まとめて保存しました。";
            if (result.ArchiveBackups.Count > 0)
                MessageBox.Show(this, $"画像名を{changed.Length}枚保存しました。\nZIPのバックアップ: {string.Join("\n", result.ArchiveBackups)}",
                    "画像名の一括編集", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            StatusText.Text = renamed ? "画像名は変更されましたが、表示情報の更新に失敗しました。" : "画像名を保存できませんでした。";
            MessageBox.Show(this, (renamed ? "画像ファイル名は変更済みです。アルバムを再読み込みして確認してください。\n" : "") + error.Message,
                "画像名の一括編集", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            AlbumImagePanel.IsEnabled = true;
            AlbumList.IsEnabled = true;
            TrackGrid.IsEnabled = true;
        }
    }

    private static void Remap<T>(Dictionary<string, T> values, IReadOnlyDictionary<string, string> keys)
    {
        var moved = values.Where(pair => keys.ContainsKey(pair.Key)).ToArray();
        foreach (var pair in moved) values.Remove(pair.Key);
        foreach (var pair in moved) values[keys[pair.Key]] = pair.Value;
    }
}
