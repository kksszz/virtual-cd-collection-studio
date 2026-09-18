using System.IO;
using System.Windows;
using System.Text.Json.Nodes;

namespace ZipMp3Player;

public partial class MainWindow
{
    private async void ConvertFolderToZip_Click(object sender, RoutedEventArgs e)
    {
        var album = GetSelectedAlbumItem()?.Album;
        if (album is null || !Directory.Exists(album.Path) || album.Tracks.Any(t => t.IsArchiveEntry || t.CuePath.Length > 0))
        { MessageBox.Show(this, "DIR形式のアルバムを選択してください。", "ZIP.MP3変換"); return; }
        if (_fullLibraryScanInProgress || _incrementalRefreshInProgress)
        { MessageBox.Show(this, "ライブラリ更新が完了してから実行してください。", "ZIP.MP3変換"); return; }
        if (_folders.Any(root => PathsEqual(root, album.Path)))
        { MessageBox.Show(this, "このアルバム自体がスキャン対象です。親フォルダーを登録し、このアルバム単独の登録を外してから変換してください。", "ZIP.MP3変換"); return; }
        if (MessageBox.Show(this, $"次のフォルダー内の全ファイルを無圧縮ZIPに格納します。\n{album.Path}\n\n作成先:\n{album.Path}.zip.mp3\n\n元フォルダーの削除は検証後に別途確認します。続行しますか？",
            "ZIP.MP3変換", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var selected = (TrackGrid.SelectedItem as ZipTrack)?.FileName;
        using var dataOperation = _dataOperations.Begin();
        if (dataOperation is null) return;
        if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, album.Path)) StopPlayback(resetPosition: false);
        FolderZipConversion.Result? result = null;
        _fullLibraryScanInProgress = true;
        IsEnabled = false;
        try
        {
            StatusText.Text = "DIRをZIP.MP3へ変換・全ファイルを照合しています…";
            result = await Task.Run(() => FolderZipConversion.Convert(album.Path));
            var converted = result.Album;
            var artwork = GetDownloadedArtworkDirectory(album.Path);
            var newArtwork = GetDownloadedArtworkDirectory(converted.Path);
            PrepareConversionArtwork(artwork, newArtwork);
            if (Directory.Exists(artwork))
            {
                foreach (var name in new[] { "artwork-roles.json", "artwork-rotations.json", "spine-card-folds.json" })
                {
                    var path = Path.Combine(newArtwork, name);
                    if (!File.Exists(path)) continue;
                    var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                    foreach (var pair in data.ToArray())
                    {
                        if (!pair.Key.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                        var oldPath = pair.Key[5..]; string? key = null;
                        if (oldPath.StartsWith(album.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            key = "zip:" + Path.GetRelativePath(album.Path, oldPath).Replace('\\', '/');
                        else if (oldPath.StartsWith(artwork.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            key = "file:" + Path.Combine(newArtwork, Path.GetRelativePath(artwork, oldPath));
                        if (key is not null) { data.Remove(pair.Key); data[key] = pair.Value; }
                    }
                    File.WriteAllText(path, data.ToJsonString());
                }
            }
            foreach (var old in album.Tracks)
            {
                var relative = Path.GetRelativePath(album.Path, old.SourcePath).Replace('\\', '/');
                var next = converted.Tracks.Single(t => t.FileName == relative);
                if (_favoritesStore.IsTrackFavorite(old) != _favoritesStore.IsTrackFavorite(next)) _favoritesStore.ToggleTrack(next);
                _usageStore.CopyTrackHistory(old, next);
                var oldLyrics = GetSavedLyricsPathForIdentity(album.Path, old.SourcePath, old.FileName, false);
                var newLyrics = GetSavedLyricsPathForIdentity(converted.Path, next.SourcePath, next.FileName, true);
                if (File.Exists(oldLyrics))
                { Directory.CreateDirectory(Path.GetDirectoryName(newLyrics)!); File.Copy(oldLyrics, newLyrics, true); }
            }
            if (_favoritesStore.IsAlbumFavorite(album) != _favoritesStore.IsAlbumFavorite(converted)) _favoritesStore.ToggleAlbum(converted);
            _favoritesStore.Save(); _usageStore.Save();
            if (_favoritesStore.IsDirty || _usageStore.IsDirty) throw new IOException("設定・履歴を保存できませんでした。元フォルダーを残します。");
            if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, album.Path)) _playingAlbum = converted;
            if (PathsEqual(_settings.LastAlbumPath ?? "", album.Path)) _settings.LastAlbumPath = converted.Path;
            ReplaceLibraryAlbum(album, converted, selected);
            SaveLibraryCache(); SaveSettings();
            _libraryChangeLogStore.Add("ZIP.MP3変換", converted.Path, converted.Tracks[0].Album, converted.Tracks.Count); _libraryChangeLogStore.Save();
            IsEnabled = true;
            if (_dataOperations.CloseRequested) return; // No new destructive confirmation during shutdown; retain source.
            if (MessageBox.Show(this, $"ZIP作成・内容検証・関連データ引き継ぎが完了しました。\n{result.Destination}\n\n元フォルダーを完全削除しますか？\n{result.Source}\nファイル数: {result.Files.Count}\n\nごみ箱には入りません。復元には作成したZIPを展開してください。\n「いいえ」なら元フォルダーも残します。",
                "元フォルダーの削除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                IsEnabled = false;
                await Task.Run(() => FolderZipConversion.DeleteVerifiedSource(result));
                foreach (var old in album.Tracks)
                {
                    _usageStore.RemoveTrackHistory(old);
                    if (_favoritesStore.IsTrackFavorite(old)) _favoritesStore.ToggleTrack(old);
                }
                if (_favoritesStore.IsAlbumFavorite(album)) _favoritesStore.ToggleAlbum(album);
                _usageStore.Save(); _favoritesStore.Save();
                _libraryChangeLogStore.Add("変換元フォルダー削除", result.Source, converted.Tracks[0].Album, converted.Tracks.Count); _libraryChangeLogStore.Save();
                StatusText.Text = "ZIP.MP3変換完了。検証済みの元フォルダーを削除しました（ZIPから復元可能）。";
            }
            else StatusText.Text = "ZIP.MP3変換完了。元フォルダーは残しています。";
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            StatusText.Text = "ZIP.MP3変換または後処理を中断しました";
            MessageBox.Show(this, ex.Message + (result is null ? "\n元フォルダーの削除は実行していません。" : $"\n検証済みZIP: {result.Destination}\n削除中の失敗時は元フォルダーの一部が残る場合があります。"), "ZIP.MP3変換", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsEnabled = true; _fullLibraryScanInProgress = false; ScheduleLibraryDiscovery(TimeSpan.FromSeconds(1)); }
    }

    private static void PrepareConversionArtwork(string source, string destination)
    {
        var managedRoot = Path.GetFullPath(Path.Combine(DataDirectory, "artwork")).TrimEnd('\\') + "\\";
        var from = Path.GetFullPath(source); var to = Path.GetFullPath(destination);
        if (!from.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase) || !to.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) throw new IOException("画像設定の保存先が不正です。");
        if (Directory.Exists(to))
        {
            if ((File.GetAttributes(to) & FileAttributes.ReparsePoint) != 0) throw new IOException("画像保存先がリンクになっています。");
            // Keep old managed data recoverable; do not revive stale images on round trips.
            var backup = to + ".before-conversion-" + Guid.NewGuid().ToString("N");
            Directory.Move(to, backup);
        }
        if (Directory.Exists(from)) AppDataBackupService.CopyDirectory(from, to);
    }
}
