using System.IO;
using System.Text.Json.Nodes;
using System.Windows;

namespace ZipMp3Player;

public partial class MainWindow
{
    private async void ConvertZipToFolder_Click(object sender, RoutedEventArgs e)
    {
        var album = GetSelectedAlbumItem()?.Album;
        if (album is null || !album.Path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase) || !File.Exists(album.Path))
        { MessageBox.Show(this, "ZIP.MP3アルバムを選択してください。", "DIR変換"); return; }
        if (_fullLibraryScanInProgress || _incrementalRefreshInProgress)
        { MessageBox.Show(this, "ライブラリ更新の完了後に実行してください。", "DIR変換"); return; }
        var destination = ZipFolderConversion.DestinationFor(album.Path);
        if (MessageBox.Show(this, $"次のフォルダーへ展開します。\n{destination}\n\nZIP内の共通の親フォルダー名は除き、現在のZIP名を使います。\nDisc1・Disc2などの下位フォルダーはそのまま保持し、全曲を一つのアルバムとして表示します。\n\n展開内容の全ファイル照合・曲の読み込み・関連データの引き継ぎが成功した後、元ZIPを再検証して自動削除します。ごみ箱には入りません。検証に失敗した場合は元ZIPを残します。\n続行しますか？", "DIR変換と元ZIPの削除",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        using var operation = _dataOperations.Begin(); if (operation is null) return;
        var selected = (TrackGrid.SelectedItem as ZipTrack)?.FileName;
        if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, album.Path)) StopPlayback(resetPosition: false);
        _fullLibraryScanInProgress = true; IsEnabled = false;
        try
        {
            StatusText.Text = "ZIP.MP3をDIRへ展開・全ファイルを照合しています…";
            var result = await Task.Run(() => ZipFolderConversion.Convert(album.Path));
            var selectedTrack = selected is not null ? result.FindTrack(selected) : result.FindTrack(album.Tracks[0].FileName);
            var converted = selectedTrack.Album;
            var artwork = GetDownloadedArtworkDirectory(album.Path);
            foreach (var folderAlbum in result.Albums)
            {
                var newArtwork = GetDownloadedArtworkDirectory(folderAlbum.Path);
                PrepareConversionArtwork(artwork, newArtwork);
                if (Directory.Exists(artwork))
                {
                    foreach (var name in new[] { "artwork-roles.json", "artwork-rotations.json", "spine-card-folds.json" })
                    {
                        var path = Path.Combine(newArtwork, name); if (!File.Exists(path)) continue;
                        var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                        foreach (var pair in data.ToArray())
                        {
                            string? key = null;
                            if (pair.Key.StartsWith("zip:") && result.EntryPaths.TryGetValue(pair.Key[4..], out var relative))
                                key = "file:" + ZipFolderConversion.SafePath(result.Destination, relative);
                            else if (pair.Key.StartsWith("file:" + artwork.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                                key = "file:" + Path.Combine(newArtwork, Path.GetRelativePath(artwork, pair.Key[5..]));
                            if (key is not null) { data.Remove(pair.Key); data[key] = pair.Value; }
                        }
                        File.WriteAllText(path, data.ToJsonString());
                    }
                }
            }
            var trackMap = album.Tracks.ToDictionary(t => t.FileName, t => result.FindTrack(t.FileName));
            foreach (var old in album.Tracks)
            {
                var next = trackMap[old.FileName].Track;
                if (_favoritesStore.IsTrackFavorite(old) != _favoritesStore.IsTrackFavorite(next)) _favoritesStore.ToggleTrack(next);
                _usageStore.CopyTrackHistory(old, next);
                var previousLyrics = GetSavedLyricsPathForIdentity(album.Path, old.SourcePath, old.FileName, true);
                var nextLyrics = GetSavedLyricsPathForIdentity(trackMap[old.FileName].Album.Path, next.SourcePath, next.FileName, false);
                if (File.Exists(previousLyrics)) { Directory.CreateDirectory(Path.GetDirectoryName(nextLyrics)!); File.Copy(previousLyrics, nextLyrics, true); }
            }
            foreach (var folderAlbum in result.Albums)
                if (_favoritesStore.IsAlbumFavorite(album) != _favoritesStore.IsAlbumFavorite(folderAlbum)) _favoritesStore.ToggleAlbum(folderAlbum);
            _favoritesStore.Save(); _usageStore.Save();
            if (_favoritesStore.IsDirty || _usageStore.IsDirty) throw new IOException("関連データを保存できません。元ZIPを残します。");
            ZipFolderConversion.CompleteReplacement(result);
            if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, album.Path)) _playingAlbum = converted;
            if (PathsEqual(_settings.LastAlbumPath ?? "", album.Path))
            {
                _settings.LastAlbumPath = converted.Path;
                if (trackMap.TryGetValue(_settings.LastTrackFileName ?? "", out var last))
                {
                    _settings.LastAlbumPath = last.Album.Path;
                    _settings.LastTrackFileName = last.Track.FileName;
                }
            }
            foreach (var folderAlbum in result.Albums.Where(a => !PathsEqual(a.Path, converted.Path)))
                InsertAlbumSorted(new AlbumListItem(folderAlbum));
            ReplaceLibraryAlbum(album, converted, selectedTrack.Track.FileName);
            SaveSettings(); SaveLibraryCache();
            foreach (var folderAlbum in result.Albums)
                _libraryChangeLogStore.Add("DIR変換", folderAlbum.Path, folderAlbum.Tracks[0].Album, folderAlbum.Tracks.Count);
            _libraryChangeLogStore.Save();
            if (_dataOperations.CloseRequested)
            {
                StatusText.Text = "DIR変換完了。終了要求があったため元ZIPは残しています。";
                return;
            }
            StatusText.Text = "展開内容を再照合し、検証済みの元ZIPを削除しています…";
            await Task.Run(() => ZipFolderConversion.DeleteVerifiedSource(result));
            foreach (var old in album.Tracks)
            { _usageStore.RemoveTrackHistory(old); if (_favoritesStore.IsTrackFavorite(old)) _favoritesStore.ToggleTrack(old); }
            if (_favoritesStore.IsAlbumFavorite(album)) _favoritesStore.ToggleAlbum(album);
            _usageStore.Save(); _favoritesStore.Save();
            _libraryChangeLogStore.Add("変換元ZIP削除", album.Path, album.Tracks[0].Album, album.Tracks.Count); _libraryChangeLogStore.Save();
            StatusText.Text = "DIR変換完了。元ZIPを削除しました。内容は展開先に保存されています。";
        }
        catch (Exception ex)
        { IsEnabled = true; StatusText.Text = "DIR変換の処理を中断しました"; MessageBox.Show(this, ex.Message + $"\n出力先: {destination}", "DIR変換", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; _fullLibraryScanInProgress = false; ScheduleLibraryDiscovery(TimeSpan.FromSeconds(1)); }
    }
}
