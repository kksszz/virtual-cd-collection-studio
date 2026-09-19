using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace ZipMp3Player;

public partial class MainWindow
{
    private async void ConvertAllCompressedZip_Click(object sender, RoutedEventArgs e) => await ConvertAllCompressedZipAsync(true);

    private async Task ConvertAllCompressedZipAsync(bool confirm, ZipAlbum? onlyAlbum = null)
    {
        if (_fullLibraryScanInProgress || _incrementalRefreshInProgress || _cachedLibraryMaintenanceInProgress
            || _dataOperations.ActiveCount > 0)
        { StatusText.Text = "ライブラリ更新・データ処理が完了してから一括変換してください。"; return; }
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VirtualCDCollectionStudioBackups");
        var session = Path.Combine(backupRoot, "StoredZip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        var report = new List<object>();
        using var operation = _dataOperations.Begin();
        if (operation is null) return;
        _fullLibraryScanInProgress = true;
        IsEnabled = false;
        var count = 0;
        try
        {
            ZipStorageConversionService.ValidateBackupRoot(session, _folders);
            var archives = onlyAlbum is null ? _albums.Where(a => a.IsArchive).Select(a => a.Album).ToArray() : new[] { onlyAlbum };
            StatusText.Text = "圧縮ZIPを調べています…";
            var targets = await Task.Run(() => archives.Where(a => ZipStorageConversionService.HasCompressedEntries(a.Path)).ToArray());
            if (targets.Length == 0) { StatusText.Text = "変換対象の圧縮ZIPはありません。"; return; }
            // Refuse all collisions before touching any album.
            foreach (var album in targets)
            {
                var destination = ZipStorageConversionService.StoredDestination(album.Path);
                if (!PathsEqual(destination, album.Path) && (File.Exists(destination) || Directory.Exists(destination)))
                    throw new IOException("同名の出力先が存在します。上書きせず中止します: " + destination);
            }
            if (confirm && MessageBox.Show(this, $"圧縮ZIP {targets.Length}件を無圧縮.zip.mp3に統一します。\n音声は再エンコードしません。画像設定・お気に入り・履歴を引き継ぎます。\n\n元ファイルとアプリデータのバックアップ先（ライブラリ外）:\n{session}\n\n検証後、.zipの元ファイルはライブラリから取り除きます。バックアップは残します。続行しますか？",
                "圧縮ZIPの一括変換", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (_dataOperations.CloseRequested) return;
            StopPlayback(resetPosition: false);
            _favoritesStore.Save(); _usageStore.Save(); SaveSettings(); SaveLibraryCache();
            if (_favoritesStore.IsDirty || _usageStore.IsDirty) throw new IOException("お気に入り・履歴を保存できません。");
            Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, "plan.json"), JsonSerializer.Serialize(targets.Select(a => new { Source = a.Path, Destination = ZipStorageConversionService.StoredDestination(a.Path) })));
            StatusText.Text = "変換前のアプリデータをバックアップしています…";
            await Task.Run(() => AppDataBackupService.CreateBackup(DataDirectory, Path.Combine(session, "BeforeConversion.zipmp3backup")));
            foreach (var album in targets)
            {
                if (_dataOperations.CloseRequested) break; // Finish current album, then stop safely.
                StatusText.Text = $"無圧縮ZIP.MP3へ変換・検証中 {count + 1}/{targets.Length}: {Path.GetFileName(album.Path)}";
                var result = await Task.Run(() => ZipStorageConversionService.ConvertWithExternalBackup(album.Path, session));
                report.Add(new { Phase = "VerifiedOutput", Result = result });
                SaveReport();
                var converted = await Task.Run(() => ZipAlbumReader.Open(result.Destination));
                if (!PathsEqual(album.Path, converted.Path)) MigrateStoredZipIdentity(album, converted);
                ReplaceLibraryAlbum(album, converted, null);
                SaveLibraryCache(); SaveSettings();
                // Persist the complete in-memory snapshot explicitly: legacy save methods swallow IO errors.
                var cachePath = LibraryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(cachePath, JsonSerializer.Serialize(new LibraryCache { Version = CurrentLibraryCacheVersion,
                    IsComplete = _libraryCacheComplete, Albums = _albums.Select(a => a.Album).ToList() }, new JsonSerializerOptions { IgnoreReadOnlyProperties = true }));
                File.Move(cachePath, LibraryPath, true);
                using (JsonDocument.Parse(File.ReadAllText(LibraryPath))) { }
                // Surface a settings write failure rather than silently dropping last-played identity.
                using (var settings = JsonDocument.Parse(File.ReadAllText(SettingsPath)))
                    if (settings.RootElement.GetProperty("LastAlbumPath").GetString() != _settings.LastAlbumPath)
                        throw new IOException("最終再生アルバム設定を保存できません。元ZIPを残します。");
                await Task.Run(() => ZipStorageConversionService.FinishExternalConversion(result));
                count++;
                report.Add(new { Phase = "Completed", Result = result });
                SaveReport();
            }
            StatusText.Text = $"無圧縮ZIP.MP3への一括変換: {count}/{targets.Length}件完了。バックアップ: {session}";
            report.Add(new { Phase = "Finished", Completed = count, Total = targets.Length }); SaveReport();
            if (confirm && !_dataOperations.CloseRequested) MessageBox.Show(this, StatusText.Text, "一括変換");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"一括変換を中断しました（{count}件完了）: {ex.Message} / バックアップ: {session}";
            if (Directory.Exists(session)) { report.Add(new { Phase = "Failed", Error = ex.ToString(), Completed = count }); SaveReport(); }
            if (confirm && !_dataOperations.CloseRequested) MessageBox.Show(this, StatusText.Text, "一括変換", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsEnabled = true; _fullLibraryScanInProgress = false; ScheduleLibraryDiscovery(TimeSpan.FromSeconds(2)); }

        void SaveReport() => File.WriteAllText(Path.Combine(session, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void MigrateStoredZipIdentity(ZipAlbum album, ZipAlbum converted)
    {
        var from = GetDownloadedArtworkDirectory(album.Path);
        var to = GetDownloadedArtworkDirectory(converted.Path);
        PrepareConversionArtwork(from, to);
        if (Directory.Exists(to))
            foreach (var name in new[] { "artwork-roles.json", "artwork-rotations.json", "spine-card-folds.json" })
            {
                var path = Path.Combine(to, name);
                if (!File.Exists(path)) continue;
                var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                foreach (var pair in data.ToArray())
                    if (pair.Key.StartsWith("file:" + from.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = "file:" + Path.Combine(to, Path.GetRelativePath(from, pair.Key[5..]));
                        data.Remove(pair.Key); data[key] = pair.Value;
                    }
                File.WriteAllText(path, data.ToJsonString());
            }
        _favoritesStore.RelocateAlbum(album, converted.Path);
        foreach (var track in album.Tracks)
        {
            _favoritesStore.RelocateTrack(track, converted.Path);
            _usageStore.RelocateTrack(track, converted.Path);
            var oldLyrics = GetSavedLyricsPathForIdentity(album.Path, track.SourcePath, track.FileName, true);
            var newLyrics = GetSavedLyricsPathForIdentity(converted.Path, converted.Path, track.FileName, true);
            if (File.Exists(oldLyrics)) { Directory.CreateDirectory(Path.GetDirectoryName(newLyrics)!); File.Copy(oldLyrics, newLyrics, true); }
        }
        _favoritesStore.Save(); _usageStore.Save();
        if (_favoritesStore.IsDirty || _usageStore.IsDirty) throw new IOException("関連データを保存できません。元ZIPを残します。");
        if (_playingAlbum is not null && PathsEqual(_playingAlbum.Path, album.Path)) _playingAlbum = converted;
        if (PathsEqual(_settings.LastAlbumPath ?? "", album.Path)) _settings.LastAlbumPath = converted.Path;
    }
}
