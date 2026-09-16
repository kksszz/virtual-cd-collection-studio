using System.IO;
using System.Windows.Threading;

namespace ZipMp3Player;

public partial class MainWindow
{
    private DispatcherTimer? _automaticArtworkTimer;
    private CancellationTokenSource? _automaticArtworkCancellation;
    private bool _automaticArtworkBusy;

    private void StopAutomaticArtwork()
    {
        _automaticArtworkTimer?.Stop();
        _automaticArtworkCancellation?.Cancel();
        _automaticArtworkCancellation?.Dispose();
        _automaticArtworkCancellation = null;
    }

    private void ConfigureAutomaticArtwork()
    {
        StopAutomaticArtwork();
        if (!_settings.AutomaticArtworkEnabled || _settings.AutomaticArtworkPaused || _dataRestorePendingRestart) return;
        _settings.AutomaticArtworkNextAttempts ??= new();
        if (_settings.AutomaticArtworkProviderVersion < 2)
        {
            // Retry old failures once with the new providers, preserving deletion suppression.
            foreach (var path in _settings.AutomaticArtworkNextAttempts.Where(pair => pair.Value != DateTimeOffset.MaxValue)
                .Select(pair => pair.Key).ToArray()) _settings.AutomaticArtworkNextAttempts.Remove(path);
            _settings.AutomaticArtworkProviderVersion = 2;
            SaveSettings();
        }
        _automaticArtworkCancellation = new CancellationTokenSource();
        if (_automaticArtworkTimer is null)
        {
            _automaticArtworkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
            _automaticArtworkTimer.Tick += async (_, _) => await FetchOneAutomaticArtworkAsync();
        }
        _automaticArtworkTimer.Start();
    }

    private async Task FetchOneAutomaticArtworkAsync()
    {
        if (_automaticArtworkBusy || _automaticArtworkCancellation is null || _dataRestorePendingRestart) return;
        var token = _automaticArtworkCancellation.Token;
        var item = _albums.FirstOrDefault(candidate => candidate.ImageCount == 0
            && IsAlbumFolderEnabled(candidate.Album.Path)
            && (!_settings.AutomaticArtworkNextAttempts.TryGetValue(candidate.Album.Path, out var next)
                || next <= DateTimeOffset.UtcNow));
        if (item is null) return;
        _automaticArtworkBusy = true;
        string? temporary = null;
        var saved = false;
        try
        {
            var directory = GetDownloadedArtworkDirectory(item.Album.Path);
            // Include source-directory and managed images; cached counts alone are insufficient.
            if (await Task.Run(() => GetCaseArtworkSources(item.Album, directory).Count != 0, token))
            {
                _settings.AutomaticArtworkNextAttempts[item.Album.Path] = DateTimeOffset.UtcNow.AddDays(1);
                return;
            }
            token.ThrowIfCancellationRequested();
            _settings.AutomaticArtworkNextAttempts[item.Album.Path] = DateTimeOffset.UtcNow.AddDays(1);
            SaveSettings();
            var outcome = await Task.Run(() => new AlbumArtworkLookupService()
                .FindAutomaticCoverAsync(item.Title, item.Artist, item.Album.Tracks.Select(track => track.Title).ToArray(), token), token);
            var candidate = outcome.Candidate;
            token.ThrowIfCancellationRequested();
            if (candidate is null)
            {
                _settings.AutomaticArtworkNextAttempts[item.Album.Path] = DateTimeOffset.UtcNow.AddDays(outcome.TransientFailure ? 1 : 7);
                _libraryChangeLogStore.Add("画像自動取得: " + outcome.Detail
                    + (outcome.TransientFailure ? "（翌日以降に再試行）" : "（7日後に再試行）"),
                    item.Album.Path, item.Title, item.Album.Tracks.Count);
                _libraryChangeLogStore.Save();
                SaveSettings();
                return;
            }
            if (!_albums.Contains(item) || !IsAlbumFolderEnabled(item.Album.Path)) return;
            if (await Task.Run(() => GetCaseArtworkSources(item.Album, directory).Count != 0, token)) return;
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "auto-front-" + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporary, candidate.ImageBytes, token);
            if (await Task.Run(() => GetCaseArtworkSources(item.Album, directory).Count != 0, token)) return;
            token.ThrowIfCancellationRequested();
            if (!_albums.Contains(item) || !IsAlbumFolderEnabled(item.Album.Path) || item.ImageCount != 0
                || Directory.EnumerateFiles(directory).Any(IsAlbumImage)) return;
            var destination = Path.Combine(directory, "auto-front-" + candidate.ReleaseGroupId + candidate.Extension);
            File.Move(temporary, destination); // No overwrite, never modify music or ZIP files.
            temporary = null;
            saved = true;
            // Deleting an automatically obtained image must not cause it to reappear.
            _settings.AutomaticArtworkNextAttempts[item.Album.Path] = DateTimeOffset.MaxValue;
            _libraryChangeLogStore.Add("画像自動取得: " + candidate.SourceUrl,
                item.Album.Path, item.Title, item.Album.Tracks.Count);
            _libraryChangeLogStore.Save();
            SaveSettings();
            await item.RefreshImageCountAsync(token);
            if (_album?.Path == item.Album.Path) LoadAlbumImages(item.Album);
            RemoveAlbumFromArtistTree(item);
            AddAlbumToArtistTree(item);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Pause should not postpone an unfinished album until tomorrow.
            if (!saved && !_dataRestorePendingRestart)
            {
                _settings.AutomaticArtworkNextAttempts.Remove(item.Album.Path);
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !saved)
            {
                _settings.AutomaticArtworkNextAttempts[item.Album.Path] = DateTimeOffset.UtcNow.AddDays(1);
                var reason = ex is UnauthorizedAccessException ? "保存先のアクセス権限なし"
                    : ex is IOException ? "保存・読み取りI/O失敗" : ex.GetType().Name;
                _libraryChangeLogStore.Add("画像自動取得: " + reason + "（翌日以降に再試行）", item.Album.Path, item.Title, item.Album.Tracks.Count);
                _libraryChangeLogStore.Save();
                SaveSettings();
            }
        }
        finally
        {
            // Only the exact temporary file created by this operation is removed.
            if (temporary is not null) { try { File.Delete(temporary); } catch { } }
            _automaticArtworkBusy = false;
        }
    }
}
