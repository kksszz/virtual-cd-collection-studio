using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MessageBox = ZipMp3Player.LocalizedMessageBox;

namespace ZipMp3Player;

public partial class MainWindow : Window
{
    private ZipAlbum? _album;
    private ZipAlbum? _playingAlbum;
    private IWavePlayer? _output;
    private WaveStream? _reader;
    private TrackAudioReader? _trackReader;
    private GaplessPlaybackStream? _gapless;
    private int _gaplessRevision;
    private bool _gaplessEnabled = true;
    private RemasterSampleProvider? _remaster;
    private EqualizerSampleProvider? _equalizer;
    private BassBoostSampleProvider? _bassBoost;
    private LowVolumeClaritySampleProvider? _lowVolumeClarity;
    private NAudio.Wave.SampleProviders.VolumeSampleProvider? _volumeGain;
    private WaveformCaptureSampleProvider? _waveform;
    private SpectrumCaptureSampleProvider? _spectrum;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _cacheSaveTimer;
    private readonly DispatcherTimer _usageSaveTimer;
    private readonly DispatcherTimer _localizationTimer;
    private readonly DispatcherTimer _libraryChangeTimer;
    private int _currentIndex = -1;
    private bool _ignoreStopped;
    private bool _draggingPosition;
    private bool _shuffle;
    private RepeatMode _repeat;
    private readonly Random _random = new();
    private Func<bool>? _tryAdvanceAttractMode;
    private readonly double[] _eqGains = new double[10];
    private bool _applyingEqPreset;
    private double _playbackSpeed = 1.0;
    private bool _preservePitch = true;
    private bool _faithfulMode;
    private bool _faithfulExclusiveActive;
    private RemasterMode _remasterMode = RemasterMode.Off;
    private static readonly Dictionary<string, double[]> EqPresets = new()
    {
        ["Flat"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["Rock"] = [4, 3, 1, -1, -2, 1, 3, 5, 5, 4],
        ["HardRock"] = [3, 4, 3, 1, -1, 1, 3, 4, 3, 2],
        ["HeavyMetal"] = [3, 5, 4, 1, -2, 0, 3, 5, 4, 2],
        ["ThrashMetal"] = [1, 3, 4, 1, -3, -1, 4, 6, 5, 3],
        ["DeathMetal"] = [4, 6, 5, 2, -2, 0, 3, 4, 3, 1],
        ["PowerMetal"] = [2, 3, 2, 0, -2, 1, 4, 6, 6, 4],
        ["Pop"] = [-1, 1, 3, 4, 2, 0, -1, 1, 2, 2],
        ["Jazz"] = [3, 2, 1, 2, -1, -1, 0, 1, 3, 4],
        ["Classical"] = [4, 3, 2, 0, -1, -1, 0, 2, 3, 4],
        ["Dance"] = [5, 4, 1, 0, 0, -2, -1, 1, 4, 5],
        ["Bass"] = [7, 6, 5, 3, 1, 0, 0, 0, 0, 0],
        ["Vocal"] = [-2, -2, -1, 1, 4, 5, 4, 2, 0, -1],
        ["Treble"] = [0, 0, 0, 0, 0, 1, 3, 5, 6, 7]
    };
    private readonly ObservableCollection<string> _folders = [];
    private readonly HashSet<string> _disabledFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly BulkObservableCollection<AlbumListItem> _albums = [];
    private readonly ObservableCollection<ArtistTreeNode> _artistTreeRoots = [];
    private readonly ICollectionView _albumView;
    private readonly DispatcherTimer _albumSearchTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
    private string[] _albumSearchWords = [];
    private bool _albumSearchComposing;
    private bool _albumSearchPending;
    private bool _artistTreeDirty = true;
    private readonly List<AlbumImageSource> _albumImages = [];
    private Dictionary<string, string> _currentArtworkRoles = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _currentArtworkRotations = new(StringComparer.OrdinalIgnoreCase);
    private string _currentTrayColor = "Auto";
    private int _albumImageIndex = -1;
    private int _selectedAlbumImageIndex = -1;
    private int _visibleAlbumImageCount = 1;
    private int _albumImagePageSize = 1;
    private bool _imageLayoutUpdatePending;
    private bool _updatingArtworkRoleCombo;
    private bool _updatingTrayColorCombo;
    private ZipAlbum? _lyricsAlbum;
    private ZipTrack? _lyricsTrack;
    private bool _loadingLyrics;
    private LyricsDocument _lyricsDocument = LyricsDocument.Empty;
    private string _lyricsRawText = "";
    private string _lyricsDisplaySnapshot = "";
    private int _lastAutoLyricsLine = -1;
    private readonly HashSet<string> _albumPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedAlbumPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _incrementalRefreshCancellation;
    private readonly List<FileSystemWatcher> _libraryWatchers = [];
    private readonly Dictionary<string, byte> _pendingLibraryChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _libraryChangeRetryCounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _libraryWatcherNeedsRescan;
    private bool _fullLibraryScanInProgress;
    private bool _incrementalRefreshInProgress;
    private readonly PlaybackUsageStore _usageStore;
    private readonly FavoritesStore _favoritesStore;
    private IReadOnlyList<FavoriteTrackEntry> _favoriteQueue = [];
    private int _favoriteQueueIndex = -1;
    private PlaybackUsageEntry? _activeUsageEntry;
    private DateTime _usageLastTickUtc;
    private double _activeUsageSessionSeconds;
    private bool _activeUsagePlayCommitted;
    private PlayerSettings _settings = new();
    private string _applicationTitle = "Virtual CD Collection Studio";
    private bool _loadingCache;
    private bool _dataRestorePendingRestart;
    private bool _forceClose;
    private bool _cacheNeedsRefresh;
    private bool _libraryCacheComplete = true;
    private bool _hasCompleteLibraryCache;
    private int _scanGeneration;
    private int _completedScanGeneration;
    private bool _coverFlowRefreshPending;
    private CancellationTokenSource? _coverFlowHighResolutionCancellation;
    private AlbumSortMode _albumSortMode = AlbumSortMode.Artist;
    private ImagePanelLayout _imagePanelLayout = ImagePanelLayout.Bottom;
    private bool _lyricsPanelExpanded = true;
    // v15 adds MPEG Layer II detection for files stored with an .mp3 name.
    // Older caches may have persisted those tracks as unsupported.
    // v20 also repairs malformed UTF-16 tags and uses Xing duration metadata.
    private const int CurrentLibraryCacheVersion = 20;
    private static readonly string DataDirectory = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipMp3Player");
    private static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.json");
    private static readonly string LibraryPath = Path.Combine(DataDirectory, "library.json");
    private static readonly string PartialLibraryPath = Path.Combine(DataDirectory, "library.partial.json");
    private static readonly string UsagePath = Path.Combine(DataDirectory, "usage.json");
    private static readonly string FavoritesPath = Path.Combine(DataDirectory, "favorites.json");

    private enum RepeatMode { Off, All, One }
    private enum AlbumSortMode { Artist, Album, ArtistTree, CoverFlow }
    private enum ImagePanelLayout { Right, Bottom }

    public MainWindow()
    {
        LocalizationService.InitializeFromSettings(SettingsPath);
        InitializeComponent();
        LocalizationService.Apply(this);
        AlbumCoverFlow.PlaybackActiveProvider = IsAlbumActivelyPlaying;
        AlbumCoverFlow.PlaybackStateProvider = GetJewelCasePlaybackState;
        AlbumCoverFlow.PreviousTrackRequested += (_, _) => Previous_Click(AlbumCoverFlow, new RoutedEventArgs());
        AlbumCoverFlow.PlayPauseRequested += (_, _) => PlayPause_Click(AlbumCoverFlow, new RoutedEventArgs());
        AlbumCoverFlow.NextTrackRequested += (_, _) => Next_Click(AlbumCoverFlow, new RoutedEventArgs());
        AlbumCoverFlow.VolumeChangedRequested += (_, args) => VolumeSlider.Value = args.Volume;
        _usageStore = new PlaybackUsageStore(UsagePath);
        _usageStore.Load();
        _favoritesStore = new FavoritesStore(FavoritesPath);
        _favoritesStore.Load();
        var appVersion = typeof(MainWindow).Assembly.GetName().Version;
        _applicationTitle = appVersion is null ? "Virtual CD Collection Studio" : $"Virtual CD Collection Studio v{appVersion.Major}.{appVersion.Minor}";
        Title = _applicationTitle;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => UpdatePosition();
        _cacheSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _cacheSaveTimer.Tick += (_, _) => { _cacheSaveTimer.Stop(); SaveLibraryCache(); };
        _usageSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _usageSaveTimer.Tick += (_, _) => _usageStore.Save();
        _usageSaveTimer.Start();
        _localizationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _localizationTimer.Tick += (_, _) =>
        {
            if (LocalizationService.IsEnglish) LocalizationService.Apply(this);
        };
        _localizationTimer.Start();
        _libraryChangeTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(900) };
        _libraryChangeTimer.Tick += LibraryChangeTimer_Tick;
        _albumView = System.Windows.Data.CollectionViewSource.GetDefaultView(_albums);
        _albumView.Filter = item => item is AlbumListItem album && MatchesAlbumFilter(album);
        AlbumList.ItemsSource = _albumView;
        ArtistTree.ItemsSource = _artistTreeRoots;
        _albumSearchTimer.Tick += (_, _) => ApplyAlbumSearch();
        TextCompositionManager.AddPreviewTextInputStartHandler(AlbumFilterTextBox, (_, _) =>
        { _albumSearchComposing = true; _albumSearchTimer.Stop(); });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(AlbumFilterTextBox, (_, _) =>
        { _albumSearchComposing = true; _albumSearchTimer.Stop(); });
        TextCompositionManager.AddPreviewTextInputHandler(AlbumFilterTextBox, (_, _) =>
        { _albumSearchComposing = false; _albumSearchTimer.Stop(); _albumSearchTimer.Start(); });
        AlbumFilterTextBox.LostKeyboardFocus += (_, _) => { _albumSearchComposing = false; if (_albumSearchPending) ApplyAlbumSearch(); };
        Closed += (_, _) =>
        {
            _albumSearchTimer.Stop();
            _libraryChangeTimer.Stop();
            DisposeLibraryWatchers();
            _incrementalRefreshCancellation?.Cancel();
            _coverFlowHighResolutionCancellation?.Cancel();
        };
        UpdateAlbumFilterResult();
        EqPresetCombo.SelectedIndex = 0;
        Application.Current.SessionEnding += (_, _) => _forceClose = true;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        ApplySavedAudioSettings();
        await ShowLibraryLoadingAsync(
            LocalizationService.Select("音楽ファイルを読み込んでいます…", "Loading music files…"),
            LocalizationService.Select("保存済みライブラリを復元しています", "Restoring the saved library"));
        await LoadLibraryCacheAsync();
        HideLibraryLoading();
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1])) await OpenAlbumAsync(args[1]);
        else if (_folders.Count > 0 && (_albums.Count == 0 || _cacheNeedsRefresh))
        {
            await ScanFoldersAsync();
            if (_cacheNeedsRefresh && _albums.Count > 0) RestoreLastSelection();
        }
        else if (_albums.Count > 0) RestoreLastSelection();

        ConfigureLibraryWatchers();
        ScheduleRequested3dPreview(args);
    }

    private void ScheduleRequested3dPreview(IReadOnlyList<string> args)
    {
        const string option = "--preview-3d=";
        var argument = args.FirstOrDefault(value =>
            value.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return;

        var query = argument[option.Length..].Trim().Trim('"');
        if (query.Length == 0) return;
        var item = _albums.FirstOrDefault(album =>
            album.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || album.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
            || album.Album.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            StatusText.Text = LocalizationService.Select(
                $"3D確認対象が見つかりません: {query}",
                $"3D preview target was not found: {query}");
            return;
        }

        AlbumList.SelectedItem = item;
        AlbumList.ScrollIntoView(item);
        SetCurrentAlbum(item.Album);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => ShowAlbum3DFullScreen_Click(this, new RoutedEventArgs())));
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Select("アルバムまたは音楽ファイルを開く", "Open an album or music file"),
            Filter = LocalizationService.Select(
                "対応音楽ファイル (*.zip.mp3;*.zip;*.mp3;*.wav;*.flac;*.m4a)|*.zip.mp3;*.zip;*.mp3;*.wav;*.flac;*.m4a|すべてのファイル (*.*)|*.*",
                "Supported music files (*.zip.mp3;*.zip;*.mp3;*.wav;*.flac;*.m4a)|*.zip.mp3;*.zip;*.mp3;*.wav;*.flac;*.m4a|All files (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) == true) await OpenAlbumAsync(dialog.FileName);
    }

    private async Task OpenAlbumAsync(string path)
    {
        await ShowLibraryLoadingAsync(
            LocalizationService.Select("音楽ファイルを読み込んでいます…", "Loading music files…"),
            LocalizationService.Select($"解析中: {Path.GetFileName(path)}", $"Analyzing: {Path.GetFileName(path)}"));
        try
        {
            StatusText.Text = "アルバムを解析しています…";
            var standardAudio = ZipAlbumReader.IsStandardAudioPath(path);
            var albumPath = standardAudio ? Path.GetDirectoryName(Path.GetFullPath(path))! : path;
            var album = await Task.Run(() => standardAudio
                ? ZipAlbumReader.OpenFolder(albumPath)
                : ZipAlbumReader.Open(path));
            var item = new AlbumListItem(album);
            var existing = _albums.FirstOrDefault(a => string.Equals(a.Album.Path, albumPath, StringComparison.OrdinalIgnoreCase));
            if (existing is null) { InsertAlbumSorted(item); existing = item; }
            AlbumList.SelectedItem = existing;
            SetCurrentAlbum(existing.Album);
            if (standardAudio)
            {
                var trackIndex = existing.Album.Tracks.ToList().FindIndex(track =>
                    string.Equals(track.SourcePath, path, StringComparison.OrdinalIgnoreCase));
                if (trackIndex >= 0) TrackGrid.SelectedIndex = trackIndex;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "読み込みに失敗しました";
            MessageBox.Show(this, ex.Message, "音楽ファイルを開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { HideLibraryLoading(); }
    }

    private async Task ShowLibraryLoadingAsync(string title, string detail, bool background = false)
    {
        LibraryLoadingTitle.Text = title;
        LibraryLoadingDetail.Text = detail;
        LibraryLoadingProgress.IsIndeterminate = true;
        LibraryLoadingProgress.Minimum = 0;
        LibraryLoadingProgress.Maximum = 1;
        LibraryLoadingProgress.Value = 0;
        LibraryLoadingOverlay.IsHitTestVisible = !background;
        LibraryLoadingOverlay.Background = background
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(217, 21, 23, 27));
        LibraryLoadingCard.HorizontalAlignment = background ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        LibraryLoadingCard.VerticalAlignment = background ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        LibraryLoadingCard.Margin = background ? new Thickness(0, 0, 24, 72) : new Thickness(0);
        LibraryLoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = detail;
        Mouse.OverrideCursor = background ? null : Cursors.Wait;
        // Allow the overlay to reach the compositor before any parsing or
        // filesystem enumeration begins.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private void UpdateLibraryLoading(string detail, int current = 0, int total = 0)
    {
        LibraryLoadingDetail.Text = detail;
        StatusText.Text = detail;
        if (total <= 0) return;
        LibraryLoadingProgress.IsIndeterminate = false;
        LibraryLoadingProgress.Maximum = total;
        LibraryLoadingProgress.Value = Math.Clamp(current, 0, total);
    }

    private void HideLibraryLoading()
    {
        LibraryLoadingOverlay.Visibility = Visibility.Collapsed;
        Mouse.OverrideCursor = null;
    }

    private void SetCurrentAlbum(ZipAlbum album)
    {
        _album = album;
        UpdateLyricsIndicators(album);
        TrackGrid.ItemsSource = album.Tracks;
        var first = album.Tracks.First();
        var albumTitle = string.IsNullOrWhiteSpace(first.Album) ? Path.GetFileName(album.Path) : first.Album;
        AlbumTitleText.Text = albumTitle;
        var artist = string.IsNullOrWhiteSpace(first.Artist) ? "アーティスト不明" : first.Artist;
        var supported = album.Tracks.Count(t => t.IsSupported);
        var duration = TimeSpan.FromSeconds(album.Tracks.Sum(t => t.Duration.TotalSeconds));
        var hasCompressedZipEntries = false;
        if (first.IsArchiveEntry && File.Exists(album.Path))
            try { hasCompressedZipEntries = ZipStorageConversionService.HasCompressedEntries(album.Path); } catch { }
        var summary = new List<string>();
        if (!AreDisplayValuesEquivalent(artist, albumTitle)) summary.Add(artist);
        summary.Add($"{album.Tracks.Count}曲");
        summary.Add($"{(int)duration.TotalMinutes}:{duration.Seconds:00}");
        if (supported < album.Tracks.Count) summary.Add($"再生可能 {supported}/{album.Tracks.Count}曲");
        if (hasCompressedZipEntries) summary.Add("圧縮ZIP・変換可能");
        AlbumInfoText.Text = string.Join("  •  ", summary);
        AlbumInfoText.ToolTip = $"{albumTitle}\nアーティスト: {artist}\n{album.Tracks.Count}曲・{(int)duration.TotalMinutes}:{duration.Seconds:00}・再生可能 {supported}曲";
        if (_playingAlbum is not null && _currentIndex >= 0 && _currentIndex < _playingAlbum.Tracks.Count)
            UpdateNowPlayingHeader(_playingAlbum.Tracks[_currentIndex]);
        LoadAlbumImages(album);
        TrackGrid.SelectedIndex = 0;
        if (_output is null)
            StatusText.Text = hasCompressedZipEntries
                ? "圧縮ZIP.MP3です。アルバムを右クリックすると無圧縮へ変換できます"
                : supported == album.Tracks.Count ? album.Path : "未対応形式の曲があります";
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        AccumulateUsageTime();
        _usageStore.Save();
        _favoritesStore.Save();
        SaveSettings();
        SaveLibraryCache();
        var previousLanguage = _settings.DisplayLanguage;
        var dialog = new SettingsWindow(_folders, _disabledFolders, _settings.MinimizeOnClose,
            DataDirectory, previousLanguage, _settings.TagBackupEnabled, _settings.TagBackupFolder) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            LocalizationService.SetLanguage(previousLanguage);
            LocalizationService.Apply(this);
            return;
        }
        _settings.MinimizeOnClose = dialog.MinimizeOnClose;
        _settings.DisplayLanguage = dialog.DisplayLanguage;
        _settings.TagBackupEnabled = dialog.TagBackupEnabled;
        _settings.TagBackupFolder = dialog.TagBackupFolder;
        LocalizationService.SetLanguage(_settings.DisplayLanguage);
        LocalizationService.Apply(this);
        _albumView.Refresh();
        AlbumList.Items.Refresh();
        TrackGrid.Items.Refresh();
        UpdateAlbumFilterResult();
        if (dialog.RestoreCompleted)
        {
            _dataRestorePendingRestart = true;
            _cacheSaveTimer.Stop();
            _usageSaveTimer.Stop();
            _scanCancellation?.Cancel();
            MessageBox.Show(this,
                $"バックアップを復元しました。アプリを終了しますので、もう一度起動してください。\n\n復元前の安全バックアップ:\n{dialog.RestoreSafetyBackupPath}",
                "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }
        if (dialog.Relocations.Count > 0)
        {
            StopPlayback(resetPosition: false);
            foreach (var relocation in dialog.Relocations) MigrateFolderData(relocation);
            _usageStore.Save();
            _favoritesStore.Save();
        }
        var foldersChanged = !_folders.SequenceEqual(dialog.Folders, StringComparer.OrdinalIgnoreCase);
        var visibilityChanged = !_disabledFolders.SetEquals(dialog.DisabledFolders);
        _folders.Clear();
        foreach (var folder in dialog.Folders) _folders.Add(folder);
        _disabledFolders.Clear();
        foreach (var folder in dialog.DisabledFolders) _disabledFolders.Add(folder);
        ConfigureLibraryWatchers();
        ApplyFolderVisibility();
        SaveSettings();
        StatusText.Text = foldersChanged || visibilityChanged ? "フォルダの表示設定を保存しました" : "設定を保存しました";
        if (dialog.ExitRequested)
        {
            _forceClose = true;
            Close();
            return;
        }
        if (dialog.RescanRequested || dialog.Relocations.Count > 0) await ScanFoldersAsync();
    }

    private void MigrateFolderData(FolderRelocation relocation)
    {
        var albums = new List<ZipAlbum>();
        try
        {
            if (File.Exists(LibraryPath))
            {
                var cache = JsonSerializer.Deserialize<LibraryCache>(File.ReadAllText(LibraryPath));
                if (cache is not null) albums.AddRange(cache.Albums);
            }
        }
        catch { }
        albums.AddRange(_albums.Select(item => item.Album));

        foreach (var album in albums.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
        {
            var newAlbumPath = TranslateFolderPath(album.Path, relocation.OldPath, relocation.NewPath);
            if (newAlbumPath is null) continue;
            _favoritesStore.RelocateAlbum(album, newAlbumPath);

            var oldArtwork = GetDownloadedArtworkDirectory(album.Path);
            var newArtwork = GetDownloadedArtworkDirectory(newAlbumPath);
            if (Directory.Exists(oldArtwork)) AppDataBackupService.CopyDirectory(oldArtwork, newArtwork);

            foreach (var track in album.Tracks)
            {
                var newSourcePath = TranslateFolderPath(track.SourcePath, relocation.OldPath, relocation.NewPath);
                if (newSourcePath is null) continue;
                var oldLyrics = GetSavedLyricsPathForIdentity(album.Path, track.SourcePath, track.FileName, track.IsArchiveEntry);
                var newLyrics = GetSavedLyricsPathForIdentity(newAlbumPath, newSourcePath, track.FileName, track.IsArchiveEntry);
                if (File.Exists(oldLyrics) && !File.Exists(newLyrics))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newLyrics)!);
                    File.Copy(oldLyrics, newLyrics, overwrite: false);
                }
                _usageStore.RelocateTrack(track, newSourcePath);
                _favoritesStore.RelocateTrack(track, newSourcePath);
            }
        }
        StatusText.Text = $"関連データを新しいフォルダへ引き継ぎました: {relocation.NewPath}";
    }

    private static string? TranslateFolderPath(string path, string oldRoot, string newRoot)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullOld = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullNew = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, fullOld, StringComparison.OrdinalIgnoreCase)) return fullNew;
            var prefix = fullOld + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return Path.Combine(fullNew, fullPath[prefix.Length..]);
        }
        catch { return null; }
    }

    private async Task ScanFoldersAsync()
    {
        _incrementalRefreshCancellation?.Cancel();
        var scanGeneration = ++_scanGeneration;
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        _libraryCacheComplete = false;
        if (_folders.Count == 0)
        {
            _albums.Clear();
            _artistTreeRoots.Clear();
            StatusText.Text = "音楽フォルダを登録してください";
            return;
        }

        _fullLibraryScanInProgress = true;

        var keepLibraryAvailable = _albums.Count > 0;
        _removedAlbumPaths.Clear();
        if (!keepLibraryAvailable)
        {
            _album = null;
            TrackGrid.ItemsSource = null;
            ClearAlbumImages();
            AlbumTitleText.Text = "ライブラリをスキャン中";
            AlbumInfoText.Text = "登録フォルダからZIP／ZIP.MP3・MP3・WAV・FLAC・M4Aを検索しています…";
        }
        await ShowLibraryLoadingAsync(
            LocalizationService.Select("音楽ファイルを読み込んでいます…", "Loading music files…"),
            LocalizationService.Select("登録フォルダを確認しています", "Checking registered folders"),
            background: keepLibraryAvailable);

        ScanUpdate? latestUpdate = null;
        var progressTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(100) };
        progressTimer.Tick += (_, _) =>
        {
            var update = Interlocked.Exchange(ref latestUpdate, null);
            if (update is not null) UpdateLibraryLoading(update.Message, update.Current, update.Total);
        };
        try
        {
            var folders = _folders.ToArray();
            var progress = new DirectProgress<ScanUpdate>(update =>
            {
                if (!token.IsCancellationRequested) Interlocked.Exchange(ref latestUpdate, update);
            });
            progressTimer.Start();
            var found = await Task.Run(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                return ScanWorker(folders, progress, token);
            }, token);
            if (token.IsCancellationRequested) return;
            var finalUpdate = Interlocked.Exchange(ref latestUpdate, null);
            if (finalUpdate is not null)
                UpdateLibraryLoading(finalUpdate.Message, finalUpdate.Current, finalUpdate.Total);
            ApplyScannedLibrary(found);
            _completedScanGeneration = scanGeneration;
            _libraryCacheComplete = true;
            _cacheNeedsRefresh = false;
            if (_album is null)
            {
                AlbumTitleText.Text = "音楽ライブラリ";
                AlbumInfoText.Text = $"登録フォルダ {_folders.Count}件  •  アルバム {_albums.Count}件";
            }
            StatusText.Text = _albums.Count > 0 ? $"スキャン完了: {_albums.Count}アルバム" : "アルバムは見つかりませんでした";
            if (_albums.Count > 0 && AlbumList.SelectedItem is null) AlbumList.SelectedIndex = 0;
            SaveLibraryCache();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = "スキャン中にエラーが発生しました";
            MessageBox.Show(this, ex.Message, "スキャンエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            progressTimer.Stop();
            if (scanGeneration == _scanGeneration)
            {
                _fullLibraryScanInProgress = false;
                HideLibraryLoading();
            }
            if (_pendingLibraryChanges.Count > 0 || _libraryWatcherNeedsRescan)
            {
                _libraryChangeTimer.Stop();
                _libraryChangeTimer.Start();
            }
        }
    }

    private void ConfigureLibraryWatchers()
    {
        DisposeLibraryWatchers();
        _pendingLibraryChanges.Clear();
        _libraryChangeRetryCounts.Clear();
        _libraryWatcherNeedsRescan = false;
        _incrementalRefreshCancellation?.Cancel();

        foreach (var folder in _folders
                     .Select(NormalizeLibraryPath)
                     .Where(path => path is not null && Directory.Exists(path))
                     .Cast<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                        | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                watcher.Created += LibraryWatcher_Changed;
                watcher.Changed += LibraryWatcher_Changed;
                watcher.Deleted += LibraryWatcher_Changed;
                watcher.Renamed += LibraryWatcher_Renamed;
                watcher.Error += LibraryWatcher_Error;
                watcher.EnableRaisingEvents = true;
                _libraryWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Library watcher could not start for {folder}: {ex.Message}");
            }
        }
    }

    private void DisposeLibraryWatchers()
    {
        foreach (var watcher in _libraryWatchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _libraryWatchers.Clear();
    }

    private void LibraryWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Changed && !IsLibraryContentPath(e.FullPath)) return;
        QueueLibraryChange(e.FullPath);
    }

    private void LibraryWatcher_Renamed(object sender, RenamedEventArgs e)
    {
        QueueLibraryChange(e.OldFullPath);
        QueueLibraryChange(e.FullPath);
    }

    private void LibraryWatcher_Error(object sender, ErrorEventArgs e)
    {
        Debug.WriteLine($"Library watcher lost events: {e.GetException().Message}");
        _ = Dispatcher.BeginInvoke(() =>
        {
            _libraryWatcherNeedsRescan = true;
            _libraryChangeTimer.Stop();
            _libraryChangeTimer.Start();
        });
    }

    private void QueueLibraryChange(string path)
    {
        var normalized = NormalizeLibraryPath(path);
        if (normalized is null) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _pendingLibraryChanges[normalized] = 0;
            _libraryChangeRetryCounts[normalized] = 0;
            _libraryChangeTimer.Stop();
            _libraryChangeTimer.Start();
        });
    }

    private async void LibraryChangeTimer_Tick(object? sender, EventArgs e)
    {
        _libraryChangeTimer.Stop();
        if (_fullLibraryScanInProgress || _incrementalRefreshInProgress)
        {
            _libraryChangeTimer.Start();
            return;
        }

        if (_libraryWatcherNeedsRescan)
        {
            _libraryWatcherNeedsRescan = false;
            _pendingLibraryChanges.Clear();
            StatusText.Text = LocalizationService.Select(
                "フォルダー監視を再同期しています…", "Resynchronizing folder monitoring…");
            await ScanFoldersAsync();
            ConfigureLibraryWatchers();
            return;
        }

        var changes = _pendingLibraryChanges.Keys.ToArray();
        _pendingLibraryChanges.Clear();
        if (changes.Length == 0) return;

        _incrementalRefreshInProgress = true;
        _incrementalRefreshCancellation?.Cancel();
        _incrementalRefreshCancellation?.Dispose();
        _incrementalRefreshCancellation = new CancellationTokenSource();
        var token = _incrementalRefreshCancellation.Token;
        try
        {
            var existing = _albums.Select(item => new ExistingLibraryAlbum(
                item.Album.Path, item.IsArchive)).ToArray();
            var result = await Task.Run(() => RefreshChangedAlbums(changes, existing, token), token);
            if (token.IsCancellationRequested) return;
            ApplyIncrementalLibraryRefresh(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Incremental library refresh failed: {ex.Message}");
            StatusText.Text = LocalizationService.Select(
                "ライブラリの差分更新でエラーが発生しました", "An incremental library update failed");
        }
        finally
        {
            _incrementalRefreshInProgress = false;
            if (_pendingLibraryChanges.Count > 0)
            {
                _libraryChangeTimer.Stop();
                _libraryChangeTimer.Start();
            }
        }
    }

    private static IncrementalLibraryResult RefreshChangedAlbums(
        IReadOnlyList<string> changes, IReadOnlyList<ExistingLibraryAlbum> existing, CancellationToken token)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveryScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var changedPath in changes)
        {
            token.ThrowIfCancellationRequested();
            if (ZipAlbumReader.IsSupportedArchivePath(changedPath))
                candidates.Add(changedPath);
            else if (ZipAlbumReader.IsStandardAudioPath(changedPath))
            {
                var parent = Path.GetDirectoryName(changedPath);
                if (!string.IsNullOrWhiteSpace(parent)) candidates.Add(parent);
            }

            if (Directory.Exists(changedPath)) discoveryScopes.Add(changedPath);
            foreach (var album in existing)
            {
                if (PathsEqual(album.Path, changedPath) || IsPathWithin(album.Path, changedPath)
                    || (!album.IsArchive && IsLibraryContentPath(changedPath)
                        && IsPathWithin(changedPath, album.Path)))
                    candidates.Add(album.Path);
            }
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var scope in discoveryScopes)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var file in Directory.EnumerateFiles(scope, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (ZipAlbumReader.IsSupportedArchivePath(file)) candidates.Add(file);
                    else if (ZipAlbumReader.IsStandardAudioPath(file))
                    {
                        var parent = Path.GetDirectoryName(file);
                        if (!string.IsNullOrWhiteSpace(parent)) candidates.Add(parent);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var refreshed = new List<AlbumListItem>();
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ZipAlbum? album = null;
                if (ZipAlbumReader.IsSupportedArchivePath(candidate))
                {
                    if (File.Exists(candidate)) album = ZipAlbumReader.Open(candidate);
                }
                else if (Directory.Exists(candidate)
                         && Directory.EnumerateFiles(candidate, "*", SearchOption.TopDirectoryOnly)
                             .Any(ZipAlbumReader.IsStandardAudioPath))
                {
                    album = ZipAlbumReader.OpenFolder(candidate);
                }

                if (album is null) removed.Add(candidate);
                else
                {
                    UpdateLyricsIndicators(album);
                    refreshed.Add(new AlbumListItem(album));
                }
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"Changed album is not readable: {candidate}: {ex.Message}");
            }
            catch (IOException) { retry.Add(candidate); }
            catch (UnauthorizedAccessException) { retry.Add(candidate); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Changed album could not be refreshed: {candidate}: {ex.Message}");
                retry.Add(candidate);
            }
        }
        return new IncrementalLibraryResult(refreshed, removed, retry);
    }

    private void ApplyIncrementalLibraryRefresh(IncrementalLibraryResult result)
    {
        var refreshedByPath = result.Refreshed
            .GroupBy(item => item.Album.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var affectedPaths = new HashSet<string>(result.Removed, StringComparer.OrdinalIgnoreCase);
        affectedPaths.UnionWith(refreshedByPath.Keys);
        var selectedPath = _album?.Path;
        var selectedFileName = TrackGrid.SelectedItem is ZipTrack selectedTrack ? selectedTrack.FileName : null;

        foreach (var oldItem in _albums.Where(item => affectedPaths.Contains(item.Album.Path)).ToArray())
        {
            RemoveAlbumFromArtistTree(oldItem);
            _albums.Remove(oldItem);
            _albumPaths.Remove(oldItem.Album.Path);
        }
        foreach (var item in refreshedByPath.Values) InsertAlbumSorted(item);

        if (selectedPath is not null && refreshedByPath.TryGetValue(selectedPath, out var selected))
        {
            AlbumList.SelectedItem = selected;
            var index = selected.Album.Tracks.ToList().FindIndex(track =>
                string.Equals(track.FileName, selectedFileName, StringComparison.Ordinal));
            TrackGrid.SelectedIndex = index >= 0 ? index : 0;
        }
        else if (selectedPath is not null && result.Removed.Contains(selectedPath))
        {
            _album = null;
            TrackGrid.ItemsSource = null;
            ClearAlbumImages();
            if (_albums.Count > 0) AlbumList.SelectedItem = _albums[0];
        }

        UpdateAlbumFilterResult();
        _libraryCacheComplete = true;
        _completedScanGeneration = _scanGeneration;
        _cacheSaveTimer.Stop();
        _cacheSaveTimer.Start();
        var changedCount = affectedPaths.Count;
        if (changedCount > 0)
            StatusText.Text = LocalizationService.Select(
                $"ライブラリを自動更新しました: {changedCount}アルバム",
                $"Library updated automatically: {changedCount} album(s)");
        else if (result.Retry.Count > 0)
            StatusText.Text = LocalizationService.Select(
                "変更された音楽ファイルの書き込み完了を待っています…",
                "Waiting for the changed music file to finish writing…");
        if (result.Retry.Count > 0)
        {
            foreach (var path in result.Retry)
            {
                var retryCount = _libraryChangeRetryCounts.GetValueOrDefault(path);
                if (retryCount >= 2)
                {
                    _libraryChangeRetryCounts.Remove(path);
                    continue;
                }
                _libraryChangeRetryCounts[path] = retryCount + 1;
                _pendingLibraryChanges[path] = 0;
            }
            if (_pendingLibraryChanges.Count > 0)
            {
                _libraryChangeTimer.Interval = TimeSpan.FromSeconds(2);
                _libraryChangeTimer.Start();
            }
            else _libraryChangeTimer.Interval = TimeSpan.FromMilliseconds(900);
        }
        else _libraryChangeTimer.Interval = TimeSpan.FromMilliseconds(900);
        foreach (var path in affectedPaths) _libraryChangeRetryCounts.Remove(path);
    }

    private static bool IsLibraryContentPath(string path)
    {
        if (ZipAlbumReader.IsSupportedArchivePath(path)
            || ZipAlbumReader.IsStandardAudioPath(path)) return true;
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lrc", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeLibraryPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return null; }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(NormalizeLibraryPath(left), NormalizeLibraryPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathWithin(string path, string possibleParent)
    {
        var fullPath = NormalizeLibraryPath(path);
        var fullParent = NormalizeLibraryPath(possibleParent);
        if (fullPath is null || fullParent is null) return false;
        var prefix = fullParent.EndsWith(Path.DirectorySeparatorChar)
            || fullParent.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullParent : fullParent + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyScannedLibrary(IReadOnlyList<AlbumListItem> found)
    {
        var selectedPath = (AlbumList.SelectedItem as AlbumListItem)?.Album.Path ?? _album?.Path;
        var ordered = found
            .Where(album => !_removedAlbumPaths.Contains(album.Album.Path))
            .GroupBy(album => album.Album.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        foreach (var album in ordered) PrepareAlbumItem(album, updateLyrics: false);
        ordered.Sort(CompareAlbums);

        _loadingCache = true;
        _albumPaths.Clear();
        foreach (var album in ordered) _albumPaths.Add(album.Album.Path);
        _albums.ReplaceAll(ordered);
        _loadingCache = false;

        _artistTreeRoots.Clear();
        _artistTreeDirty = true;
        RebuildArtistTree();
        UpdateAlbumFilterResult();
        var selected = ordered.FirstOrDefault(album =>
            string.Equals(album.Album.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault();
        if (selected is not null) AlbumList.SelectedItem = selected;
    }

    private void InsertAlbumSorted(AlbumListItem album)
    {
        if (_removedAlbumPaths.Contains(album.Album.Path) || !_albumPaths.Add(album.Album.Path)) return;
        PrepareAlbumItem(album);
        var index = 0;
        while (index < _albums.Count && CompareAlbums(_albums[index], album) <= 0) index++;
        _albums.Insert(index, album);
        AddAlbumToArtistTree(album);
        UpdateAlbumFilterResult();
        if (!_loadingCache) { _cacheSaveTimer.Stop(); _cacheSaveTimer.Start(); }
    }

    private void PrepareAlbumItem(AlbumListItem album, bool updateLyrics = true)
    {
        album.SetFavorite(_favoritesStore.IsAlbumFavorite(album.Album));
        album.SetPlaying(_playingAlbum is not null
            && string.Equals(album.Album.Path, _playingAlbum.Path, StringComparison.OrdinalIgnoreCase));
        foreach (var track in album.Album.Tracks) track.IsFavorite = _favoritesStore.IsTrackFavorite(track);
        if (updateLyrics) UpdateLyricsIndicators(album.Album);
    }

    private int CompareAlbums(AlbumListItem left, AlbumListItem right)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var artistFirst = _albumSortMode is AlbumSortMode.Artist or AlbumSortMode.ArtistTree or AlbumSortMode.CoverFlow;
        var primary = artistFirst
            ? comparer.Compare(left.Artist, right.Artist)
            : comparer.Compare(left.Title, right.Title);
        if (primary != 0) return primary;
        return artistFirst
            ? comparer.Compare(left.Title, right.Title)
            : comparer.Compare(left.Artist, right.Artist);
    }

    private void AlbumSort_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AlbumList is null || AlbumSortCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        _albumSortMode = item.Tag?.ToString() switch
        {
            "Album" => AlbumSortMode.Album,
            "ArtistTree" => AlbumSortMode.ArtistTree,
            "CoverFlow" => AlbumSortMode.CoverFlow,
            _ => AlbumSortMode.Artist
        };
        ApplyAlbumViewMode();
        if (_albumSortMode != AlbumSortMode.ArtistTree) SortAlbums();
        if (IsLoaded) SaveSettings();
    }

    private void AlbumFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_albumView is null) return;
        // Normalize once per input change, not once for every album/predicate pass.
        _albumSearchWords = NormalizeAlbumSearch(AlbumFilterTextBox.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _albumSearchPending = true;
        _albumSearchTimer.Stop();
        _coverFlowHighResolutionCancellation?.Cancel();
        if (_albumSearchWords.Length == 0 && !_albumSearchComposing) ApplyAlbumSearch();
        else if (!_albumSearchComposing) _albumSearchTimer.Start();
    }

    private void ApplyAlbumSearch()
    {
        _albumSearchTimer.Stop();
        if (_albumSearchComposing) return;
        _albumSearchPending = false;
        _albumView.Refresh();
        RebuildArtistTree();
        UpdateAlbumFilterResult();
    }

    private void ClearAlbumFilter_Click(object sender, RoutedEventArgs e)
    {
        AlbumFilterTextBox.Clear();
        AlbumFilterTextBox.Focus();
    }

    private bool MatchesAlbumFilter(AlbumListItem album)
    {
        if (!IsAlbumFolderEnabled(album.Album.Path)) return false;
        foreach (var word in _albumSearchWords)
            if (!album.SearchText.Contains(word, StringComparison.Ordinal)) return false;
        return true;
    }

    private static string NormalizeAlbumSearch(string value) => string.Join(' ',
        value.Normalize(NormalizationForm.FormKC).ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private bool IsAlbumFolderEnabled(string albumPath) => !_disabledFolders.Any(folder => IsPathInsideFolder(albumPath, folder));

    private static bool IsPathInsideFolder(string path, string folder)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
            return relative == "." || (!Path.IsPathRooted(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
        }
        catch { return false; }
    }

    private void ApplyFolderVisibility()
    {
        _albumView.Refresh();
        RebuildArtistTree();
        UpdateAlbumFilterResult();
    }

    private void RebuildArtistTree()
    {
        _artistTreeDirty = true;
        if (_albumSortMode != AlbumSortMode.ArtistTree) return;
        _artistTreeRoots.Clear();
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        foreach (var albums in _albums.Where(MatchesAlbumFilter).GroupBy(album => album.Artist, comparer).OrderBy(group => group.Key, comparer))
        {
            var group = ArtistTreeNode.CreateGroup(albums.Key);
            foreach (var album in albums.OrderBy(album => album.Title, comparer)) group.Children.Add(ArtistTreeNode.CreateAlbum(album));
            _artistTreeRoots.Add(group);
        }
        _artistTreeDirty = false;
    }

    private void UpdateAlbumFilterResult()
    {
        if (AlbumFilterResultText is null) return;
        var enabled = _albums.Count(album => IsAlbumFolderEnabled(album.Album.Path));
        var visible = _albumView.Cast<object>().Count();
        AlbumFilterResultText.Text = string.IsNullOrWhiteSpace(AlbumFilterTextBox?.Text)
            ? $"{enabled}件" : $"{visible}/{enabled}件";
        QueueCoverFlowRefresh();
    }

    private void ApplyAlbumViewMode()
    {
        var treeMode = _albumSortMode == AlbumSortMode.ArtistTree;
        var coverFlowMode = _albumSortMode == AlbumSortMode.CoverFlow;
        AlbumList.Visibility = treeMode || coverFlowMode ? Visibility.Collapsed : Visibility.Visible;
        ArtistTree.Visibility = treeMode ? Visibility.Visible : Visibility.Collapsed;
        AlbumCoverFlow.Visibility = coverFlowMode ? Visibility.Visible : Visibility.Collapsed;
        if (treeMode && _artistTreeDirty) RebuildArtistTree();
        if (!coverFlowMode) _coverFlowHighResolutionCancellation?.Cancel();
        if (coverFlowMode)
        {
            RefreshCoverFlowItems();
            AlbumCoverFlow.Focus();
        }
    }

    private void QueueCoverFlowRefresh()
    {
        if (_albumSortMode != AlbumSortMode.CoverFlow || _coverFlowRefreshPending || AlbumCoverFlow is null) return;
        _coverFlowRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _coverFlowRefreshPending = false;
            RefreshCoverFlowItems();
        }));
    }

    private void RefreshCoverFlowItems()
    {
        if (_albumSortMode != AlbumSortMode.CoverFlow || _albumSearchTimer.IsEnabled || _albumSearchComposing || AlbumCoverFlow is null || _albumView is null) return;
        const int nearbyArtworkWidth = 640;
        var selectedPath = (AlbumList.SelectedItem as AlbumListItem)?.Album.Path ?? _album?.Path;
        var visibleAlbums = _albumView.Cast<object>().OfType<AlbumListItem>().ToList();
        var selectedIndex = visibleAlbums.FindIndex(album =>
            string.Equals(album.Album.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (_albumSortMode == AlbumSortMode.CoverFlow && visibleAlbums.Count > 0)
        {
            if (selectedIndex < 0) selectedIndex = 0;
            selectedPath = visibleAlbums[selectedIndex].Album.Path;
            foreach (var album in visibleAlbums)
                if (album != visibleAlbums[selectedIndex] && album.CaseArtworkDecodeWidth > nearbyArtworkWidth)
                    album.ReleaseHighResolutionCaseArtwork(nearbyArtworkWidth);
            QueueCoverFlowArtwork(visibleAlbums, selectedIndex);
        }
        var items = visibleAlbums
            .Select(album => new JewelCaseCoverFlowItem(album.Album.Path, album.Title,
                album.Artist == "アーティスト不明" ? LocalizationService.Select("アーティスト不明", "Unknown Artist") : album.Artist,
                album.SourceBadge, album.TrayColorMode, album.CaseFrontThumbnail, album.InsideFrontThumbnail,
                album.BackCoverThumbnail, album.SpineThumbnail,
                album.RightSpineThumbnail, album.InlayThumbnail, album.DiscThumbnail, album.IsPlaying)
                { LoadBooklet = album.HasFrontSpread ? album.LoadBooklet : null, SpineCard = album.SpineCardThumbnail,
                    SecondDiscImage = album.SecondDiscThumbnail })
            .ToList();
        AlbumCoverFlow.SetItems(items, selectedPath);
    }

    private void QueueCoverFlowArtwork(IReadOnlyList<AlbumListItem> albums, int selectedIndex)
    {
        var selected = albums[selectedIndex];
        var requests = new List<(AlbumListItem Album, int Width)> { (selected, 2048) };
        for (var index = Math.Max(0, selectedIndex - 5); index <= Math.Min(albums.Count - 1, selectedIndex + 5); index++)
            if (index != selectedIndex) requests.Add((albums[index], 640));
        requests.RemoveAll(request => request.Album.CaseArtworkDecodeWidth >= request.Width);
        if (requests.Count == 0) return;
        _coverFlowHighResolutionCancellation?.Cancel();
        _coverFlowHighResolutionCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _coverFlowHighResolutionCancellation = cancellation;
        _ = LoadCoverFlowArtworkAsync(requests, cancellation.Token);
    }

    private async Task LoadCoverFlowArtworkAsync(IReadOnlyList<(AlbumListItem Album, int Width)> requests,
        CancellationToken cancellationToken)
    {
        try
        {
            // Avoid decoding every album while the user is rapidly browsing.
            await Task.Delay(140, cancellationToken);
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await request.Album.EnsureCaseArtworkLoadedAsync(request.Width, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCoverFlowItems();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Cover artwork load failed: {ex.Message}"); }
    }

    private void AddAlbumToArtistTree(AlbumListItem album)
    {
        if (_albumSortMode != AlbumSortMode.ArtistTree) { _artistTreeDirty = true; return; }
        if (!MatchesAlbumFilter(album)) return;
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        var group = _artistTreeRoots.FirstOrDefault(node => comparer.Equals(node.ArtistKey, album.Artist));
        if (group is null)
        {
            group = ArtistTreeNode.CreateGroup(album.Artist);
            var groupIndex = 0;
            while (groupIndex < _artistTreeRoots.Count && comparer.Compare(_artistTreeRoots[groupIndex].ArtistKey, album.Artist) < 0) groupIndex++;
            _artistTreeRoots.Insert(groupIndex, group);
        }
        var node = ArtistTreeNode.CreateAlbum(album);
        var index = 0;
        while (index < group.Children.Count && comparer.Compare(group.Children[index].AlbumItem?.Title, album.Title) <= 0) index++;
        group.Children.Insert(index, node);
    }

    private void RemoveAlbumFromArtistTree(AlbumListItem album)
    {
        if (_albumSortMode != AlbumSortMode.ArtistTree) { _artistTreeDirty = true; return; }
        var group = _artistTreeRoots.FirstOrDefault(node => node.Children.Any(child => ReferenceEquals(child.AlbumItem, album)));
        if (group is null) return;
        var child = group.Children.First(node => ReferenceEquals(node.AlbumItem, album));
        group.Children.Remove(child);
        if (group.Children.Count == 0) _artistTreeRoots.Remove(group);
    }

    private void SortAlbums()
    {
        if (_albums.Count < 2) return;
        var selectedPath = _album?.Path;
        var selectedTrack = TrackGrid.SelectedIndex;
        var sorted = _albums.OrderBy(item => item, Comparer<AlbumListItem>.Create(CompareAlbums)).ToList();
        _albums.Clear();
        foreach (var item in sorted) _albums.Add(item);
        if (selectedPath is not null)
        {
            AlbumList.SelectedItem = _albums.FirstOrDefault(item => string.Equals(item.Album.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (selectedTrack >= 0 && selectedTrack < TrackGrid.Items.Count) TrackGrid.SelectedIndex = selectedTrack;
        }
    }

    private static List<AlbumListItem> ScanWorker(string[] folders, IProgress<ScanUpdate> progress, CancellationToken token)
    {
        var archivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var albumFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var folder in folders)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                if (ZipAlbumReader.IsSupportedArchivePath(file)) archivePaths.Add(file);
                else if (ZipAlbumReader.IsStandardAudioPath(file)) albumFolders.Add(Path.GetDirectoryName(file)!);
            }
        }

        var result = new List<AlbumListItem>();
        var number = 0;
        var total = archivePaths.Count + albumFolders.Count;
        foreach (var path in archivePaths.OrderBy(p => p))
        {
            token.ThrowIfCancellationRequested();
            number++;
            progress.Report(new ScanUpdate($"解析中 {number}/{total}: {Path.GetFileName(path)}", null, number, total));
            try
            {
                var album = new AlbumListItem(OpenArchiveForScan(path, token));
                UpdateLyricsIndicators(album.Album);
                result.Add(album);
                progress.Report(new ScanUpdate($"完了 {number}/{total}: {album.Title}", album, number, total));
            }
            catch { progress.Report(new ScanUpdate($"読み取りを省略 {number}/{total}: {Path.GetFileName(path)}", null, number, total)); }
        }
        foreach (var folder in albumFolders.OrderBy(p => p))
        {
            token.ThrowIfCancellationRequested();
            number++;
            progress.Report(new ScanUpdate($"解析中 {number}/{total}: {Path.GetFileName(folder)}", null, number, total));
            try
            {
                var album = new AlbumListItem(ZipAlbumReader.OpenFolder(folder));
                UpdateLyricsIndicators(album.Album);
                result.Add(album);
                progress.Report(new ScanUpdate($"完了 {number}/{total}: {album.Title}", album, number, total));
            }
            catch { progress.Report(new ScanUpdate($"読み取りを省略 {number}/{total}: {Path.GetFileName(folder)}", null, number, total)); }
        }
        return result;
    }

    private static ZipAlbum OpenArchiveForScan(string path, CancellationToken token)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return ZipAlbumReader.Open(path); }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 3)
            {
                lastError = ex;
                if (token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(200 * attempt)))
                    token.ThrowIfCancellationRequested();
            }
        }
        throw lastError ?? new IOException($"ZIP archive could not be read: {path}");
    }

    private void AlbumList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AlbumList.SelectedItem is AlbumListItem item)
        {
            AlbumCoverFlow.SelectByKey(item.Album.Path);
            SetCurrentAlbum(item.Album);
        }
    }

    private void AlbumCoverFlow_SelectionChanged(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        var item = _albums.FirstOrDefault(album =>
            string.Equals(album.Album.Path, e.Item.Key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        AlbumList.SelectedItem = item;
        SetCurrentAlbum(item.Album);
        QueueCoverFlowRefresh();
    }

    private void AlbumCoverFlow_ItemActivated(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        AlbumCoverFlow_SelectionChanged(sender, e);
        if (_album is null || _album.Tracks.Count == 0) return;
        TrackGrid.SelectedIndex = 0;
        PlayTrack(0);
    }

    private void AlbumCoverFlow_DiscActivated(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        if (IsAlbumActivelyPlaying(e.Item.Key))
        {
            StopPlayback(resetPosition: true);
            return;
        }
        AlbumCoverFlow_ItemActivated(sender, e);
    }

    private void OpenAlbumBrowser_Click(object sender, RoutedEventArgs e)
    {
        var allAlbums = GetAlbumBrowserSourceAlbums();
        if (allAlbums.Count == 0) return;
        var visibleAlbums = _albumView.Cast<object>().OfType<AlbumListItem>().ToList();
        var selectedPath = (AlbumList.SelectedItem as AlbumListItem)?.Album.Path ?? _album?.Path;
        var selected = visibleAlbums.FirstOrDefault(album =>
            string.Equals(album.Album.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? allAlbums.FirstOrDefault(album =>
                string.Equals(album.Album.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? visibleAlbums.FirstOrDefault() ?? allAlbums[0];
        selected.EnsureCaseArtworkLoaded(640);
        // Give the browser the complete enabled library. Its own search box receives
        // the main-window query, so clearing it can reveal every enabled album again.
        var items = allAlbums.Select(CreateAlbumBrowserItem).ToList();
        AlbumBrowserPlaybackState BrowserPlaybackState()
        {
            ZipTrack? track = _playingAlbum is not null && _currentIndex >= 0 && _currentIndex < _playingAlbum.Tracks.Count
                ? _playingAlbum.Tracks[_currentIndex]
                : TrackGrid.SelectedItem as ZipTrack ?? _album?.Tracks.FirstOrDefault();
            var title = track is null ? LocalizationService.Select("停止中", "Stopped")
                : string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Title}  •  {track.Artist}";
            return new AlbumBrowserPlaybackState(title,
                _output?.PlaybackState == PlaybackState.Playing,
                track is not null,
                VolumeSlider.Value);
        }
        var browserSort = _albumSortMode == AlbumSortMode.Album
            ? AlbumBrowserSortMode.Album : AlbumBrowserSortMode.Artist;
        var browser = new AlbumLibraryBrowserWindow(items, selected.Album.Path, BrowserPlaybackState, browserSort,
            AlbumFilterTextBox.Text)
        { Owner = this };

        void SelectAlbum(string key)
        {
            var item = _albums.FirstOrDefault(album =>
                string.Equals(album.Album.Path, key, StringComparison.OrdinalIgnoreCase));
            if (item is null) return;
            AlbumList.SelectedItem = item;
            SetCurrentAlbum(item.Album);
        }

        browser.SelectionChanged += (_, args) => SelectAlbum(args.Item.Key);
        browser.ItemActivated += (_, args) =>
        {
            SelectAlbum(args.Item.Key);
            if (_album is null || _album.Tracks.Count == 0) return;
            TrackGrid.SelectedIndex = 0;
            PlayTrack(0);
        };
        browser.DiscActivated += (_, args) =>
        {
            if (IsAlbumActivelyPlaying(args.Item.Key))
            {
                StopPlayback(resetPosition: true);
                return;
            }
            SelectAlbum(args.Item.Key);
            if (_album is null || _album.Tracks.Count == 0) return;
            TrackGrid.SelectedIndex = 0;
            PlayTrack(0);
        };
        browser.PreviousTrackRequested += (_, _) => Previous_Click(browser, new RoutedEventArgs());
        browser.PlayPauseRequested += (_, _) => PlayPause_Click(browser, new RoutedEventArgs());
        browser.NextTrackRequested += (_, _) => Next_Click(browser, new RoutedEventArgs());
        browser.VolumeChangedRequested += (_, args) => VolumeSlider.Value = args.Volume;
        browser.AttractTrackRequested += (_, args) =>
        {
            SelectAlbum(args.AlbumKey);
            if (_album is null || args.TrackIndex < 0 || args.TrackIndex >= _album.Tracks.Count) return;
            TrackGrid.SelectedIndex = args.TrackIndex;
            PlayTrack(_album, args.TrackIndex, forceStandardPlayback: true);
        };
        _tryAdvanceAttractMode = browser.AdvanceAttractMode;
        try { browser.ShowDialog(); }
        finally { _tryAdvanceAttractMode = null; }
        if (browser.SelectedKey is { } key) SelectAlbum(key);
        Focus();
    }

    private List<AlbumListItem> GetAlbumBrowserSourceAlbums()
        => _albums.Where(album => IsAlbumFolderEnabled(album.Album.Path)).ToList();

    private static AlbumLibraryBrowserItem CreateAlbumBrowserItem(AlbumListItem album)
    {
        JewelCaseCoverFlowItem CreateCaseItem() => new(album.Album.Path, album.Title,
            album.Artist == "アーティスト不明" ? LocalizationService.Select("アーティスト不明", "Unknown Artist") : album.Artist,
            album.SourceBadge, album.TrayColorMode, album.CaseFrontThumbnail ?? album.CoverThumbnail,
            album.InsideFrontThumbnail, album.BackCoverThumbnail, album.SpineThumbnail,
            album.RightSpineThumbnail, album.InlayThumbnail, album.DiscThumbnail, album.IsPlaying)
        { LoadBooklet = album.HasFrontSpread ? album.LoadBooklet : null, SpineCard = album.SpineCardThumbnail,
            SecondDiscImage = album.SecondDiscThumbnail };
        return new AlbumLibraryBrowserItem(CreateCaseItem(), album.CoverThumbnail, async (width, cancellationToken) =>
        {
            await album.EnsureCaseArtworkLoadedAsync(width, cancellationToken);
            return CreateCaseItem();
        }, album.IsFavorite, album.Album.Tracks.Count);
    }

    private void AlbumList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(AlbumList, e.OriginalSource as DependencyObject)
            is not System.Windows.Controls.ListBoxItem || _album is null) return;
        TrackGrid.SelectedIndex = 0;
        PlayTrack(0);
        e.Handled = true;
    }

    private void AlbumList_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(AlbumList, e.OriginalSource as DependencyObject)
            is System.Windows.Controls.ListBoxItem item) item.IsSelected = true;
    }

    private void AlbumContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AlbumPropertiesContextMenu_Opened(sender, e);
        var item = GetSelectedAlbumItem();
        var isArchive = item?.IsArchive == true && File.Exists(item.Album.Path);
        var isCompressed = false;
        if (isArchive)
        {
            try { isCompressed = ZipStorageConversionService.HasCompressedEntries(item!.Album.Path); }
            catch { }
        }
        ConvertToStoredZipMenuItem.IsEnabled = isCompressed;
        ConvertToStoredZipMenuItem.ToolTip = !isArchive
            ? LocalizationService.Select("通常フォルダのアルバムは変換対象外です", "Folder albums do not require conversion")
            : isCompressed
                ? LocalizationService.Select("音声を再エンコードせず、ZIP内の全収録物をStore方式へ変換", "Convert every ZIP entry to Store without re-encoding audio")
                : LocalizationService.Select("このZIP.MP3はすでに無圧縮です", "This ZIP.MP3 is already stored without compression");
    }

    private async void ConvertToStoredZip_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item?.IsArchive != true) return;
        var confirmation = MessageBox.Show(this,
            LocalizationService.Select(
                $"「{item.Title}」を無圧縮ZIP.MP3へ変換します。\n\n音声は再エンコードしません。変換後は容量が少し増える場合があります。\n元のZIP.MP3は同じフォルダへバックアップします。続行しますか？",
                $"Convert “{item.Title}” to an uncompressed ZIP.MP3?\n\nAudio will not be re-encoded. The converted file may be slightly larger.\nThe original ZIP.MP3 will be backed up in the same folder."),
            "無圧縮ZIP.MP3へ変換", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        var album = item.Album;
        var selectedFileName = (TrackGrid.SelectedItem as ZipTrack)?.FileName;
        if (_playingAlbum is not null && string.Equals(_playingAlbum.Path, album.Path, StringComparison.OrdinalIgnoreCase))
            StopPlayback(resetPosition: false);
        AlbumList.IsEnabled = false;
        TrackGrid.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        StatusText.Text = "無圧縮ZIP.MP3へ変換・検証しています…";
        try
        {
            var result = await Task.Run(() => ZipStorageConversionService.ConvertToStored(album.Path));
            var refreshed = ZipAlbumReader.Open(album.Path);
            ReplaceLibraryAlbum(album, refreshed, selectedFileName);
            SaveLibraryCache();
            StatusText.Text = "無圧縮ZIP.MP3への変換が完了しました";
            MessageBox.Show(this,
                LocalizationService.Select(
                    $"無圧縮ZIP.MP3へ変換しました。\n収録物: {result.EntryCount}件\n変換前: {FormatStorageSize(result.OriginalSize)}\n変換後: {FormatStorageSize(result.ConvertedSize)}\n\n元ファイルのバックアップ:\n{result.BackupPath}",
                    $"Converted to an uncompressed ZIP.MP3.\nEntries: {result.EntryCount}\nBefore: {FormatStorageSize(result.OriginalSize)}\nAfter: {FormatStorageSize(result.ConvertedSize)}\n\nOriginal-file backup:\n{result.BackupPath}"),
                "変換完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "無圧縮ZIP.MP3へ変換できませんでした";
            MessageBox.Show(this,
                LocalizationService.Select(
                    $"変換を完了できませんでした。検証に合格するまでは元ファイルを置換しません。\n\n理由: {ex.Message}",
                    $"The conversion could not be completed. The source is not replaced until verification succeeds.\n\nReason: {ex.Message}"),
                "ZIP変換エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            AlbumList.IsEnabled = true;
            TrackGrid.IsEnabled = true;
        }
    }

    private static string FormatStorageSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private void AlbumFavorite_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext switch
        {
            AlbumListItem album => album,
            ArtistTreeNode { AlbumItem: not null } node => node.AlbumItem,
            _ => null
        };
        if (item is null) return;
        item.SetFavorite(_favoritesStore.ToggleAlbum(item.Album));
        _favoritesStore.Save();
        StatusText.Text = item.IsFavorite ? $"アルバムをお気に入りに登録しました: {item.Title}" : $"アルバムのお気に入りを解除しました: {item.Title}";
        e.Handled = true;
    }

    private void TrackFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ZipTrack track) return;
        track.IsFavorite = _favoritesStore.ToggleTrack(track);
        TrackGrid.Items.Refresh();
        _favoritesStore.Save();
        StatusText.Text = track.IsFavorite ? $"曲をお気に入りに登録しました: {track.Title}" : $"曲のお気に入りを解除しました: {track.Title}";
        e.Handled = true;
    }

    private void ArtistTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not ArtistTreeNode { AlbumItem: not null } node) return;
        AlbumList.SelectedItem = node.AlbumItem;
    }

    private void ArtistTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ArtistTree.SelectedItem is not ArtistTreeNode { AlbumItem: not null } || _album is null) return;
        TrackGrid.SelectedIndex = 0;
        PlayTrack(0);
        e.Handled = true;
    }

    private void ArtistTree_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(ArtistTree, e.OriginalSource as DependencyObject)
            is System.Windows.Controls.TreeViewItem item) item.IsSelected = true;
    }

    private void RemoveAlbumFromList_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is null) return;
        var index = AlbumList.SelectedIndex;
        _removedAlbumPaths.Add(item.Album.Path);
        _albumPaths.Remove(item.Album.Path);
        if (string.Equals(_album?.Path, item.Album.Path, StringComparison.OrdinalIgnoreCase))
        {
            _album = null;
            TrackGrid.ItemsSource = null;
            ClearAlbumImages();
        }
        _albums.Remove(item);
        RemoveAlbumFromArtistTree(item);
        UpdateAlbumFilterResult();
        SaveLibraryCache();
        if (_albums.Count > 0) AlbumList.SelectedIndex = Math.Min(index, _albums.Count - 1);
        else
        {
            AlbumTitleText.Text = "音楽ライブラリ";
            AlbumInfoText.Text = "アルバムは登録されていません";
        }
        StatusText.Text = "リストから削除しました（元ファイルは変更していません）";
    }

    private void AlbumPropertiesContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ContextMenu menu) return;
        var selected = GetSelectedAlbumItem();
        foreach (var menuItem in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (Equals(menuItem.Tag, "AlbumProperties") || Equals(menuItem.Tag, "WikipediaAlbum"))
                menuItem.IsEnabled = selected is not null;
            else if (Equals(menuItem.Tag, "WikipediaArtist"))
                menuItem.IsEnabled = selected is not null && IsKnownArtist(selected.Artist);
        }
    }

    private void AlbumProperties_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is not null) new AlbumPropertiesWindow(item.Album) { Owner = this }.ShowDialog();
    }

    private void WikipediaArtistSearch_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is null || !IsKnownArtist(item.Artist))
        {
            MessageBox.Show(this, LocalizationService.Select(
                    "検索できるアーティスト名が登録されていません。",
                    "No artist name is available to search."),
                "Wikipedia", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenWikipediaSearch(item.Artist, item.Artist);
    }

    private void WikipediaAlbumSearch_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is null) return;
        var query = IsKnownArtist(item.Artist)
            ? $"{item.Artist} {item.Title}"
            : item.Title;
        OpenWikipediaSearch(query, item.Title);
    }

    private void OpenWikipediaSearch(string query, string displayName)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = BuildWikipediaSearchUrl(query),
                UseShellExecute = true
            });
            StatusText.Text = LocalizationService.Select(
                $"Wikipedia検索を開きました: {displayName}",
                $"Opened Wikipedia search: {displayName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                LocalizationService.Select("Wikipedia検索を開けません", "Could Not Open Wikipedia Search"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static string BuildWikipediaSearchUrl(string query)
        => "https://ja.wikipedia.org/w/index.php?search=" + Uri.EscapeDataString(query.Trim());

    private static bool IsKnownArtist(string? artist)
        => !string.IsNullOrWhiteSpace(artist)
            && !string.Equals(artist, "アーティスト不明", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(artist, "Unknown Artist", StringComparison.OrdinalIgnoreCase);

    private void OpenAlbumInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is null) return;
        try
        {
            if (Directory.Exists(item.Album.Path))
                Process.Start(new ProcessStartInfo { FileName = item.Album.Path, UseShellExecute = true });
            else if (File.Exists(item.Album.Path))
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe", Arguments = $"/select,\"{item.Album.Path}\"", UseShellExecute = true
                });
            else
                MessageBox.Show(this, "元のファイルまたはフォルダが見つかりません。", "場所を開けません", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エクスプローラーを開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ShowAlbum3DFullScreen_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem();
        if (item is null) return;

        Mouse.OverrideCursor = Cursors.Wait;
        StatusText.Text = LocalizationService.Select(
            "3Dケース用の高解像度画像を読み込んでいます…",
            "Loading high-resolution artwork for the 3D case…");
        try
        {
            await item.EnsureCaseArtworkLoadedAsync(2048, CancellationToken.None);
            var flowItem = new JewelCaseCoverFlowItem(
                item.Album.Path,
                item.Title,
                item.Artist == "アーティスト不明"
                    ? LocalizationService.Select("アーティスト不明", "Unknown Artist")
                    : item.Artist,
                item.SourceBadge,
                item.TrayColorMode,
                item.CaseFrontThumbnail,
                item.InsideFrontThumbnail,
                item.BackCoverThumbnail,
                item.SpineThumbnail,
                item.RightSpineThumbnail,
                item.InlayThumbnail,
                item.DiscThumbnail,
                item.IsPlaying) { LoadBooklet = item.HasFrontSpread ? item.LoadBooklet : null, SpineCard = item.SpineCardThumbnail,
                    SecondDiscImage = item.SecondDiscThumbnail };

            StatusText.Text = LocalizationService.Select(
                $"3Dケースを表示しています: {item.Title}",
                $"Viewing 3D case: {item.Title}");
            // The potentially slow work is complete. Clear the application-wide
            // wait cursor before entering the modal full-screen viewer; otherwise
            // it remains a spinning busy cursor until that viewer is closed.
            Mouse.OverrideCursor = null;
            JewelCaseCoverFlow.ShowItemFullScreen(flowItem, this,
                activated => AlbumCoverFlow_ItemActivated(this,
                    new JewelCaseCoverFlowSelectionChangedEventArgs(activated)),
                IsAlbumActivelyPlaying,
                activated => AlbumCoverFlow_DiscActivated(this,
                    new JewelCaseCoverFlowSelectionChangedEventArgs(activated)),
                playbackStateProvider: GetJewelCasePlaybackState,
                previousTrackRequested: () => Previous_Click(this, new RoutedEventArgs()),
                playPauseRequested: () => PlayPause_Click(this, new RoutedEventArgs()),
                nextTrackRequested: () => Next_Click(this, new RoutedEventArgs()),
                volumeChangedRequested: value => VolumeSlider.Value = value);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Select(
                "3Dケースを表示できませんでした",
                "Could not display the 3D case");
            MessageBox.Show(this,
                LocalizationService.Select(
                    $"3Dケースを表示できませんでした。\n\n理由: {ex.Message}",
                    $"The 3D case could not be displayed.\n\nReason: {ex.Message}"),
                LocalizationService.Select("3Dケース表示エラー", "3D Case Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private AlbumListItem? GetSelectedAlbumItem() => _albumSortMode == AlbumSortMode.ArtistTree
        ? (ArtistTree.SelectedItem as ArtistTreeNode)?.AlbumItem
        : AlbumList.SelectedItem as AlbumListItem;

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(SettingsPath)) ?? new PlayerSettings();
            LocalizationService.SetLanguage(_settings.DisplayLanguage);
            LocalizationService.Apply(this);
            foreach (var folder in _settings.MusicFolders)
                if (!_folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _folders.Add(folder);
            foreach (var folder in _settings.DisabledMusicFolders)
                if (_folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _disabledFolders.Add(folder);
        }
        catch { StatusText.Text = "設定ファイルを読み込めませんでした"; }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            _settings.MusicFolders = _folders.ToList();
            _settings.DisabledMusicFolders = _disabledFolders
                .Where(folder => _folders.Contains(folder, StringComparer.OrdinalIgnoreCase)).ToList();
            var lastAlbum = _playingAlbum ?? _album;
            _settings.LastAlbumPath = lastAlbum?.Path;
            _settings.LastTrackIndex = _playingAlbum is not null
                ? Math.Max(0, _currentIndex)
                : (TrackGrid.SelectedIndex >= 0 ? TrackGrid.SelectedIndex : 0);
            _settings.LastPositionSeconds = _reader?.CurrentTime.TotalSeconds ?? PositionSlider.Value;
            _settings.Volume = VolumeSlider.Value;
            _settings.PlaybackSpeed = _playbackSpeed;
            _settings.PreservePitch = _preservePitch;
            _settings.FaithfulMode = _faithfulMode;
            _settings.GaplessPlayback = _gaplessEnabled;
            _settings.EqEnabled = EqEnabledCheck.IsChecked == true;
            _settings.EqGains = _eqGains.ToArray();
            _settings.BassBoostEnabled = BassBoostEnabledCheck.IsChecked == true;
            _settings.BassBoostAmount = BassBoostSlider.Value;
            _settings.LowVolumeClarityEnabled = LowVolumeClarityCheck.IsChecked == true;
            _settings.RemasterMode = _remasterMode.ToString();
            _settings.Shuffle = _shuffle;
            _settings.RepeatMode = (int)_repeat;
            _settings.AlbumSort = _albumSortMode.ToString();
            _settings.ImagePanelLayout = _imagePanelLayout.ToString();
            if (_imagePanelLayout == ImagePanelLayout.Bottom
                && TrackContentRow.ActualHeight > 0 && ImageBottomRow.ActualHeight > 0)
            {
                var imagePanelTotal = TrackContentRow.ActualHeight + ImageBottomRow.ActualHeight;
                _settings.ImageBottomRatio = Math.Clamp(ImageBottomRow.ActualHeight / imagePanelTotal, 0.30, 0.75);
            }
            if (_lyricsPanelExpanded)
            {
                var imageLyricsWidth = ArtworkColumn.ActualWidth + LyricsColumn.ActualWidth;
                if (imageLyricsWidth > 0) _settings.ImageLyricsRatio = Math.Clamp(ArtworkColumn.ActualWidth / imageLyricsWidth, 0.15, 0.85);
            }
            _settings.LyricsPanelExpanded = _lyricsPanelExpanded;
            _settings.LyricsAutoScroll = LyricsAutoScrollCheck.IsChecked == true;
            _settings.ExtensionPanelExpanded = ExtensionPanel.Visibility == Visibility.Visible;
            if (_settings.ExtensionPanelExpanded && ExtensionColumn.ActualWidth >= 240)
                _settings.ExtensionPanelWidth = ExtensionColumn.ActualWidth;
            _settings.VisualizerMode = (VisualizerModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Spectrum";
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { StatusText.Text = "フォルダ設定を保存できませんでした"; }
    }

    private void ApplySavedAudioSettings()
    {
        // 各コントロールの変更イベントが設定を再保存する前に、右パネルの保存状態を反映する。
        ApplyExtensionPanelState();
        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 2);
        _preservePitch = _settings.PreservePitch;
        _faithfulMode = _settings.FaithfulMode;
        _gaplessEnabled = _settings.GaplessPlayback;
        GaplessCheck.IsChecked = _gaplessEnabled;
        FaithfulModeCheck.IsChecked = _faithfulMode;
        EqEnabledCheck.IsChecked = _settings.EqEnabled;
        BassBoostEnabledCheck.IsChecked = _settings.BassBoostEnabled;
        BassBoostSlider.Value = Math.Clamp(_settings.BassBoostAmount, 0, 100);
        LowVolumeClarityCheck.IsChecked = _settings.LowVolumeClarityEnabled;
        _remasterMode = Enum.TryParse<RemasterMode>(_settings.RemasterMode, true, out var remasterMode)
            ? remasterMode : RemasterMode.Off;
        RemasterModeCombo.SelectedItem = RemasterModeCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _remasterMode.ToString(), StringComparison.OrdinalIgnoreCase));
        System.Windows.Controls.Slider[] sliders = [Eq0, Eq1, Eq2, Eq3, Eq4, Eq5, Eq6, Eq7, Eq8, Eq9];
        for (var i = 0; i < sliders.Length && i < _settings.EqGains.Length; i++) sliders[i].Value = Math.Clamp(_settings.EqGains[i], -12, 12);
        var preset = EqPresets.FirstOrDefault(pair => pair.Value.SequenceEqual(_settings.EqGains)).Key;
        EqPresetCombo.SelectedItem = EqPresetCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), preset ?? "Custom", StringComparison.OrdinalIgnoreCase));
        _playbackSpeed = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 }
            .OrderBy(value => Math.Abs(value - _settings.PlaybackSpeed)).First();
        foreach (var candidate in PlaybackRateMenu.Items.OfType<System.Windows.Controls.MenuItem>()
            .Where(item => item.Tag is not null))
            candidate.IsChecked = double.TryParse(candidate.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && Math.Abs(value - _playbackSpeed) < 0.001;
        PreservePitchMenuItem.IsChecked = _preservePitch;
        UpdatePlaybackRateButton();
        UpdateFaithfulModeUi();
        _shuffle = _settings.Shuffle;
        ShuffleButton.Content = _shuffle ? "⤨ ON" : "⤨ OFF";
        _repeat = Enum.IsDefined(typeof(RepeatMode), _settings.RepeatMode) ? (RepeatMode)_settings.RepeatMode : RepeatMode.Off;
        RepeatButton.Content = _repeat switch { RepeatMode.All => "↻ 全曲", RepeatMode.One => "↻ 1曲", _ => "↻ OFF" };
        _albumSortMode = Enum.TryParse<AlbumSortMode>(_settings.AlbumSort, true, out var albumSort) ? albumSort : AlbumSortMode.Artist;
        AlbumSortCombo.SelectedIndex = _albumSortMode switch
        {
            AlbumSortMode.Album => 1,
            AlbumSortMode.ArtistTree => 2,
            AlbumSortMode.CoverFlow => 3,
            _ => 0
        };
        ApplyAlbumViewMode();
        _imagePanelLayout = ImagePanelLayout.Bottom;
        _settings.ImagePanelLayout = ImagePanelLayout.Bottom.ToString();
        ApplyImagePanelLayout();
        _lyricsPanelExpanded = _settings.LyricsPanelExpanded;
        ApplyImageLyricsRatio();
        LyricsAutoScrollCheck.IsChecked = _settings.LyricsAutoScroll;
        VisualizerModeCombo.SelectedItem = VisualizerModeCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _settings.VisualizerMode, StringComparison.OrdinalIgnoreCase))
            ?? VisualizerModeCombo.Items[1];
    }

    private async Task LoadLibraryCacheAsync()
    {
        try
        {
            var sourcePath = File.Exists(LibraryPath) ? LibraryPath
                : File.Exists(PartialLibraryPath) ? PartialLibraryPath : null;
            if (sourcePath is null) return;
            var cache = await Task.Run(() =>
                JsonSerializer.Deserialize<LibraryCache>(File.ReadAllText(sourcePath)));
            if (cache is null || cache.Version < 10)
            {
                _cacheNeedsRefresh = true;
                return;
            }
            _libraryCacheComplete = cache.IsComplete;
            _hasCompleteLibraryCache = cache.IsComplete
                && string.Equals(sourcePath, LibraryPath, StringComparison.OrdinalIgnoreCase);
            _cacheNeedsRefresh = cache.Version < CurrentLibraryCacheVersion || !cache.IsComplete;
            _loadingCache = true;
            var total = cache.Albums.Count;
            var restored = 0;
            var removedTagArtifacts = 0;
            foreach (var album in cache.Albums)
            {
                var restoredAlbum = ExcludeCachedTagEditingArtifacts(album, ref removedTagArtifacts);
                var exists = restoredAlbum.Tracks.FirstOrDefault()?.IsArchiveEntry == true
                    ? File.Exists(restoredAlbum.Path) : Directory.Exists(restoredAlbum.Path);
                if (exists && restoredAlbum.Tracks.Count > 0) InsertAlbumSorted(new AlbumListItem(restoredAlbum));
                restored++;
                if (restored % 12 != 0 && restored != total) continue;
                UpdateLibraryLoading(LocalizationService.Select(
                    $"保存済みライブラリを復元中 {restored}/{total}",
                    $"Restoring saved library {restored}/{total}"));
                // Batch UI insertion so the progress indicator and window keep
                // repainting even with a very large cached music library.
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            _loadingCache = false;
            if (removedTagArtifacts > 0) SaveLibraryCache();
            if (_albums.Count > 0)
            {
                AlbumTitleText.Text = "音楽ライブラリ";
                AlbumInfoText.Text = _libraryCacheComplete
                    ? $"保存済みアルバム {_albums.Count}件  •  再スキャンで更新"
                    : $"解析途中のアルバム {_albums.Count}件  •  設定から再スキャンで更新";
                StatusText.Text = _libraryCacheComplete
                    ? "前回のライブラリを復元しました"
                    : "前回終了時までに解析できたライブラリを復元しました";
            }
        }
        catch
        {
            _loadingCache = false;
            StatusText.Text = "保存済みライブラリを読み込めませんでした";
        }
    }

    private void LoadLibraryCache()
    {
        try
        {
            var sourcePath = File.Exists(LibraryPath) ? LibraryPath
                : File.Exists(PartialLibraryPath) ? PartialLibraryPath : null;
            if (sourcePath is null) return;
            var cache = JsonSerializer.Deserialize<LibraryCache>(File.ReadAllText(sourcePath));
            if (cache is null || cache.Version < 10)
            {
                _cacheNeedsRefresh = true;
                return;
            }
            _libraryCacheComplete = cache.IsComplete;
            _hasCompleteLibraryCache = cache.IsComplete && string.Equals(sourcePath, LibraryPath, StringComparison.OrdinalIgnoreCase);
            _cacheNeedsRefresh = cache.Version < CurrentLibraryCacheVersion || !cache.IsComplete;
            _loadingCache = true;
            var removedTagArtifacts = 0;
            foreach (var album in cache?.Albums ?? [])
            {
                var restoredAlbum = ExcludeCachedTagEditingArtifacts(album, ref removedTagArtifacts);
                var exists = restoredAlbum.Tracks.FirstOrDefault()?.IsArchiveEntry == true ? File.Exists(restoredAlbum.Path) : Directory.Exists(restoredAlbum.Path);
                if (exists && restoredAlbum.Tracks.Count > 0) InsertAlbumSorted(new AlbumListItem(restoredAlbum));
            }
            _loadingCache = false;
            if (removedTagArtifacts > 0) SaveLibraryCache();
            if (_albums.Count > 0)
            {
                AlbumTitleText.Text = "音楽ライブラリ";
                AlbumInfoText.Text = _libraryCacheComplete
                    ? $"保存済みアルバム {_albums.Count}件  •  再スキャンで更新"
                    : $"解析途中のアルバム {_albums.Count}件  •  設定から再スキャンで更新";
                StatusText.Text = _libraryCacheComplete
                    ? "前回のライブラリを復元しました"
                    : "前回終了時までに解析できたライブラリを復元しました";
            }
        }
        catch { _loadingCache = false; StatusText.Text = "保存済みライブラリを読み込めませんでした"; }
    }

    private void SaveLibraryCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
            var isComplete = !_cacheNeedsRefresh && _libraryCacheComplete
                && _completedScanGeneration == _scanGeneration;
            var cache = new LibraryCache
            {
                Version = CurrentLibraryCacheVersion,
                IsComplete = isComplete,
                Albums = _albums.Select(item => item.Album).ToList()
            };
            var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true };
            var destination = isComplete || !_hasCompleteLibraryCache ? LibraryPath : PartialLibraryPath;
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache, options));
            File.Move(temporary, destination, overwrite: true);
            if (isComplete)
            {
                _hasCompleteLibraryCache = true;
                if (File.Exists(PartialLibraryPath)) File.Delete(PartialLibraryPath);
            }
        }
        catch { /* 次回の再スキャンで復旧できるため再生は継続 */ }
    }

    private static ZipAlbum ExcludeCachedTagEditingArtifacts(ZipAlbum album, ref int removedCount)
    {
        if (album.Tracks.FirstOrDefault()?.IsArchiveEntry == true) return album;
        var tracks = album.Tracks
            .Where(track => !ZipAlbumReader.IsTagEditingArtifact(track.SourcePath)
                && !ZipAlbumReader.IsTagEditingArtifact(track.FileName))
            .ToList();
        if (tracks.Count == album.Tracks.Count) return album;
        removedCount += album.Tracks.Count - tracks.Count;
        return new ZipAlbum
        {
            Path = album.Path,
            Tracks = tracks,
            Images = album.Images,
            TextFiles = album.TextFiles,
            ImageCount = album.ImageCount
        };
    }

    private void RestoreLastSelection()
    {
        var enabledAlbums = _albums.Where(candidate => IsAlbumFolderEnabled(candidate.Album.Path)).ToList();
        if (enabledAlbums.Count == 0) return;
        var item = enabledAlbums.FirstOrDefault(candidate => string.Equals(candidate.Album.Path, _settings.LastAlbumPath, StringComparison.OrdinalIgnoreCase))
            ?? enabledAlbums.First();
        AlbumList.SelectedItem = item;
        var trackIndex = Math.Clamp(_settings.LastTrackIndex, 0, item.Album.Tracks.Count - 1);
        _currentIndex = trackIndex;
        TrackGrid.SelectedIndex = trackIndex;
        TrackGrid.ScrollIntoView(item.Album.Tracks[trackIndex]);
        var duration = item.Album.Tracks[trackIndex].Duration.TotalSeconds;
        PositionSlider.Maximum = Math.Max(0.1, duration);
        PositionSlider.Value = string.Equals(item.Album.Path, _settings.LastAlbumPath, StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(_settings.LastPositionSeconds, 0, duration) : 0;
        ElapsedText.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
        TotalText.Text = FormatTime(item.Album.Tracks[trackIndex].Duration);
    }

    private void PlayTrack(int index, TimeSpan? startAt = null, bool startPlaying = true)
        => PlayTrack(_album, index, startAt, startPlaying, countAsNewUsageSession: true);

    private void PlayTrack(ZipAlbum? album, int index, TimeSpan? startAt = null, bool startPlaying = true,
        bool countAsNewUsageSession = true, bool fromFavorites = false,
        IReadOnlyList<PlaybackQueueEntry>? queueOverride = null, bool forceStandardPlayback = false)
    {
        if (album is null || index < 0 || index >= album.Tracks.Count) return;
        var track = album.Tracks[index];
        // A previous cache version classified MPEG Layer II streams stored with an
        // .mp3 extension as invalid. Re-read just the selected album on demand so
        // playback does not have to wait for a large background library scan.
        if (!track.IsSupported && TryRefreshUnsupportedTrack(album, index, out var refreshedTrack))
            track = refreshedTrack;
        if (countAsNewUsageSession && !fromFavorites)
        {
            _favoriteQueue = [];
            _favoriteQueueIndex = -1;
        }
        if (!track.IsSupported)
        {
            MessageBox.Show(this, LocalizationService.Select(
                    $"「{track.Title}」は再生できません。\n\n{track.UnsupportedMessage}",
                    $"\"{track.Title}\" cannot be played.\n\n{track.UnsupportedMessage}"),
                LocalizationService.Select("未対応の形式", "Unsupported Format"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            AccumulateUsageTime();
            DisposeAudio();
            _playingAlbum = album;
            _currentIndex = index;
            UpdatePlayingAlbumIndicator(album);
            SelectNowPlayingButton.IsEnabled = true;
            if (_gaplessEnabled && !forceStandardPlayback)
            {
                var queue = queueOverride ?? BuildGaplessQueue(album);
                var queueIndex = queue.ToList().FindIndex(entry => ReferenceEquals(entry.Album, album) && entry.TrackIndex == index);
                _gapless = new GaplessPlaybackStream(queue, queueIndex, _faithfulMode, _shuffle, (int)_repeat);
                var activeStream = _gapless;
                _gapless.TrackChanged += () => Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_gapless, activeStream)) SynchronizeGaplessTrack();
                });
                _gaplessRevision = 0;
                _reader = _gapless;
            }
            else
            {
                _trackReader = TrackAudioReader.Open(track);
                _reader = _trackReader.Reader;
            }
            if (_faithfulMode)
            {
                _output = CreateFaithfulOutput(_reader, out _faithfulExclusiveActive);
                FaithfulModeStatusText.Text = _faithfulExclusiveActive
                    ? "ON：WASAPI排他・DSPなし・音量固定"
                    : "ON：WASAPI共有・DSPなし・音量固定（排他形式非対応）";
            }
            else
            {
                _faithfulExclusiveActive = false;
                var speedProvider = PlaybackSpeedSampleProvider.Create(_reader.ToSampleProvider(), _playbackSpeed, _preservePitch);
                _remaster = new RemasterSampleProvider(speedProvider, _remasterMode);
                _equalizer = new EqualizerSampleProvider(_remaster, _eqGains)
                {
                    Enabled = EqEnabledCheck.IsChecked == true,
                    ClampOutput = false
                };
                _bassBoost = new BassBoostSampleProvider(_equalizer, BassBoostEnabledCheck.IsChecked == true, BassBoostSlider.Value);
                _lowVolumeClarity = new LowVolumeClaritySampleProvider(_bassBoost, LowVolumeClarityCheck.IsChecked == true);
                _volumeGain = new NAudio.Wave.SampleProviders.VolumeSampleProvider(_lowVolumeClarity) { Volume = (float)VolumeSlider.Value };
                _waveform = new WaveformCaptureSampleProvider(new SoftLimiterSampleProvider(_volumeGain));
                _spectrum = new SpectrumCaptureSampleProvider(_waveform);
                var waveOut = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3, Volume = 1.0f };
                waveOut.Init(_spectrum);
                _output = waveOut;
            }
            _output.PlaybackStopped += Output_PlaybackStopped;
            if (startAt is not null) SeekReaderSafely(startAt.Value.TotalSeconds);
            PositionSlider.Maximum = Math.Max(0.1, _reader.TotalTime.TotalSeconds);
            PositionSlider.Value = _reader.CurrentTime.TotalSeconds;
            TotalText.Text = FormatTime(_reader.TotalTime);
            if (string.Equals(_album?.Path, album.Path, StringComparison.OrdinalIgnoreCase))
            {
                TrackGrid.SelectedIndex = index;
                TrackGrid.ScrollIntoView(track);
            }
            ShowNowPlaying(track);
            if (startPlaying)
            {
                _output.Play(); _timer.Start(); PlayButton.Content = "⏸ 一時停止";
                StartUsageTracking(track, countAsNewUsageSession);
                PlaybackStatusText.Text = _faithfulMode
                    ? $"再生中: {track.Title}  •  原音忠実  •  WASAPI {(_faithfulExclusiveActive ? "排他" : "共有")}"
                    : $"再生中: {track.Title}  ({_playbackSpeed:0.##}×)";
                PlaybackBadgeText.Text = "再生中";
            }
            else
            {
                _timer.Stop(); PlayButton.Content = "▶ 再生"; PlaybackStatusText.Text = "一時停止"; PlaybackBadgeText.Text = "一時停止";
            }
            Title = $"{track.Title} — {_applicationTitle}";
        }
        catch (Exception ex)
        {
            DisposeAudio();
            _favoriteQueue = [];
            _favoriteQueueIndex = -1;
            _playingAlbum = null;
            _currentIndex = -1;
            UpdatePlayingAlbumIndicator(null);
            SelectNowPlayingButton.IsEnabled = false;
            ClearNowPlaying();
            PlaybackStatusText.Text = "再生を開始できませんでした";
            MessageBox.Show(this, ex.Message, "再生エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryRefreshUnsupportedTrack(ZipAlbum album, int index, out ZipTrack refreshedTrack)
    {
        refreshedTrack = album.Tracks[index];
        if (refreshedTrack.IsEncrypted
            || !string.IsNullOrWhiteSpace(refreshedTrack.ReadError)
            || refreshedTrack.CompressionMethod is not (0 or 8)
            || refreshedTrack.AudioFormat is not ("MP3" or "MP2"))
            return false;

        try
        {
            var originalFileName = refreshedTrack.FileName;
            var refreshedAlbum = Directory.Exists(album.Path)
                ? ZipAlbumReader.OpenFolder(album.Path)
                : ZipAlbumReader.Open(album.Path);
            var candidate = refreshedAlbum.Tracks.FirstOrDefault(item =>
                string.Equals(item.FileName, originalFileName, StringComparison.OrdinalIgnoreCase));
            if (candidate is null || !candidate.IsSupported) return false;

            refreshedTrack = candidate;
            if (album.Tracks is IList<ZipTrack> mutableTracks)
            {
                mutableTracks[index] = candidate;
                if (ReferenceEquals(album, _album))
                {
                    TrackGrid.Items.Refresh();
                    PositionSlider.Maximum = Math.Max(0.1, candidate.Duration.TotalSeconds);
                    TotalText.Text = FormatTime(candidate.Duration);
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static IWavePlayer CreateFaithfulOutput(WaveStream reader, out bool exclusiveActive)
    {
        var position = reader.CurrentTime;
        var normalizedPcm = CreateFaithfulWaveProvider(reader);
        WasapiOut? exclusive = null;
        Exception? exclusiveError = null;
        try
        {
            exclusive = new WasapiOut(AudioClientShareMode.Exclusive, useEventSync: true, latency: 100);
            exclusive.Init(normalizedPcm);
            exclusiveActive = true;
            return exclusive;
        }
        catch (Exception ex)
        {
            exclusiveError = ex;
            exclusive?.Dispose();
            reader.CurrentTime = position;
        }

        WasapiOut? shared = null;
        try
        {
            shared = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            shared.Init(normalizedPcm);
            exclusiveActive = false;
            return shared;
        }
        catch (Exception sharedError)
        {
            shared?.Dispose();
            throw new InvalidOperationException(
                "原音忠実モードで音声機器を開始できませんでした。Windowsの既定出力機器を確認するか、原音忠実モードをOFFにしてください。",
                new AggregateException(exclusiveError!, sharedError));
        }
    }

    private static IWaveProvider CreateFaithfulWaveProvider(WaveStream reader)
        => reader.ToSampleProvider().ToWaveProvider();

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _output)) return;
            if (_ignoreStopped) return;
            if (e.Exception is not null)
            {
                // Media Foundation and some output drivers can reject a FLAC
                // callback even though the same stream decodes correctly. If
                // this happened on the gapless wrapper, resume the exact track
                // and position with its direct reader instead of stopping.
                if (_gapless is { } failedGapless)
                {
                    var snapshot = failedGapless.Snapshot;
                    System.Diagnostics.Debug.WriteLine($"Gapless output failed; retrying direct playback: {e.Exception}");
                    PlaybackStatusText.Text = "通常再生へ切り替えています…";
                    PlayTrack(snapshot.Entry.Album, snapshot.Entry.TrackIndex, snapshot.Position,
                        startPlaying: true, countAsNewUsageSession: false,
                        fromFavorites: snapshot.Entry.FavoriteIndex >= 0,
                        forceStandardPlayback: true);
                    if (_reader is not null)
                        StatusText.Text = "FLACを通常再生へ自動的に切り替えました";
                    return;
                }
                PlaybackStatusText.Text = "再生中にエラーが発生しました";
                MessageBox.Show(this, e.Exception.Message, "再生エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (_gapless is not null)
            {
                SynchronizeGaplessTrack();
                var fallback = _gapless.FallbackEntry;
                if (fallback is not null)
                {
                    PlayQueueEntry(fallback);
                    StatusText.Text = LocalizationService.Select("音声形式の変更または先読み待ちのため通常の曲切り替えを使用しました",
                        "Used a standard track transition for a format change or pending prefetch");
                }
                else
                {
                    StopPlayback(resetPosition: true);
                    PlaybackStatusText.Text = LocalizationService.Select("再生リストの再生が終了しました", "Playback queue finished");
                }
                return;
            }
            if (_tryAdvanceAttractMode?.Invoke() == true)
            {
                StopPlayback(resetPosition: true);
                PlaybackStatusText.Text = LocalizationService.Select(
                    "Attractモード：次のCDを選んでいます…", "Attract: choosing the next CD…");
                return;
            }
            PlayFollowingTrack(naturalEnd: true);
        });
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null) return;
        if (_output is null)
        {
            var index = TrackGrid.SelectedIndex >= 0 ? TrackGrid.SelectedIndex : Math.Max(0, _currentIndex);
            TimeSpan? resume = string.Equals(_album.Path, _settings.LastAlbumPath, StringComparison.OrdinalIgnoreCase)
                && index == _settings.LastTrackIndex && PositionSlider.Value > 0
                ? TimeSpan.FromSeconds(PositionSlider.Value) : null;
            PlayTrack(index, resume);
            return;
        }
        if (_output.PlaybackState == PlaybackState.Playing)
        {
            AccumulateUsageTime();
            _output.Pause(); _timer.Stop(); PlayButton.Content = "▶ 再生"; PlaybackStatusText.Text = "一時停止"; PlaybackBadgeText.Text = "一時停止";
        }
        else
        {
            _output.Play(); _timer.Start(); PlayButton.Content = "⏸ 一時停止";
            if (_playingAlbum is not null && _currentIndex >= 0)
                StartUsageTracking(_playingAlbum.Tracks[_currentIndex], countAsNewSession: false);
            PlaybackStatusText.Text = $"再生中: {_playingAlbum?.Tracks[_currentIndex].Title}";
            PlaybackBadgeText.Text = "再生中";
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback(resetPosition: true);
    private void Previous_Click(object sender, RoutedEventArgs e) => PlayRelative(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => PlayFollowingTrack(naturalEnd: false);

    private void PlayRelative(int delta)
    {
        if (_gapless is not null)
        {
            SynchronizeGaplessTrack();
            _gapless.ConfigureNavigation(_shuffle, (int)_repeat);
            PlayQueueEntry(_gapless.RelativeEntry(delta));
            return;
        }
        if (_favoriteQueue.Count > 0)
        {
            var nextFavorite = _favoriteQueueIndex + delta;
            if (_repeat == RepeatMode.All) nextFavorite = (nextFavorite + _favoriteQueue.Count) % _favoriteQueue.Count;
            PlayFavoriteTrack(Math.Clamp(nextFavorite, 0, _favoriteQueue.Count - 1));
            return;
        }
        var album = _playingAlbum ?? _album;
        if (album is null) return;
        var next = _currentIndex < 0 ? 0 : _currentIndex + delta;
        if (next < 0) next = _repeat == RepeatMode.All ? album.Tracks.Count - 1 : 0;
        if (next >= album.Tracks.Count) next = _repeat == RepeatMode.All ? 0 : album.Tracks.Count - 1;
        PlayTrack(album, next);
    }

    private void PlayFollowingTrack(bool naturalEnd)
    {
        if (_gapless is not null)
        {
            SynchronizeGaplessTrack();
            _gapless.ConfigureNavigation(_shuffle, (int)_repeat);
            var nextEntry = _gapless.FollowingEntry(naturalEnd);
            if (nextEntry is null) { StopPlayback(resetPosition: true); return; }
            PlayQueueEntry(nextEntry);
            return;
        }
        if (_favoriteQueue.Count > 0)
        {
            var nextFavorite = _favoriteQueueIndex + 1;
            if (naturalEnd && _repeat == RepeatMode.One) nextFavorite = _favoriteQueueIndex;
            else if (_shuffle && _favoriteQueue.Count > 1)
            {
                do nextFavorite = _random.Next(_favoriteQueue.Count); while (nextFavorite == _favoriteQueueIndex);
            }
            if (nextFavorite >= _favoriteQueue.Count)
            {
                if (_repeat == RepeatMode.All) nextFavorite = 0;
                else
                {
                    StopPlayback(resetPosition: true);
                    PlaybackStatusText.Text = LocalizationService.Select("お気に入りの再生が終了しました", "Favorites playback finished");
                    return;
                }
            }
            PlayFavoriteTrack(nextFavorite);
            return;
        }
        var album = _playingAlbum ?? _album;
        if (album is null) return;
        if (naturalEnd && _repeat == RepeatMode.One) { PlayTrack(album, _currentIndex); return; }
        int next;
        if (_shuffle && album.Tracks.Count > 1)
        {
            do next = _random.Next(album.Tracks.Count); while (next == _currentIndex);
        }
        else next = _currentIndex + 1;

        if (next >= album.Tracks.Count)
        {
            if (_repeat == RepeatMode.All) next = 0;
            else { StopPlayback(resetPosition: true); PlaybackStatusText.Text = "アルバムの再生が終了しました"; return; }
        }
        PlayTrack(album, next);
    }

    private void TrackGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrackGrid.SelectedIndex >= 0) PlayTrack(TrackGrid.SelectedIndex);
    }

    private void TrackGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_album is not null && TrackGrid.SelectedItem is ZipTrack track) LoadLyrics(_album, track);
    }

    private void TrackGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null) TrackGrid.SelectedItem = row.Item;
    }

    private void TrackContextMenu_Opened(object sender, RoutedEventArgs e)
        => TrackPropertiesMenuItem.IsEnabled = TrackGrid.SelectedItem is ZipTrack;

    private void TrackProperties_Click(object sender, RoutedEventArgs e)
    {
        if (TrackGrid.SelectedItem is ZipTrack track)
            new AlbumPropertiesWindow(track) { Owner = this }.ShowDialog();
    }

    private async void EditTrackTags_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null || _album.Tracks.Count == 0)
        {
            MessageBox.Show(this, "先にアルバムを選択してください。", "タグ編集", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var album = _album;
        var selectedFileName = (TrackGrid.SelectedItem as ZipTrack)?.FileName;
        var dialog = new TagEditorWindow(album, selectedFileName) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.EditedTracks.Count == 0) return;
        var selectedResultFileName = dialog.EditedTracks.FirstOrDefault(update =>
            string.Equals(update.FileName, selectedFileName, StringComparison.Ordinal))?.EffectiveTargetFileName ?? selectedFileName;

        if (_playingAlbum is not null && string.Equals(_playingAlbum.Path, album.Path, StringComparison.OrdinalIgnoreCase))
            StopPlayback(resetPosition: false);
        TrackGrid.IsEnabled = false;
        StatusText.Text = album.Tracks.First().IsArchiveEntry
            ? "ZIP.MP3へタグを書き込んでいます…"
            : "音楽ファイルへタグを書き込んでいます…";
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var backupOptions = new TrackTagBackupOptions(_settings.TagBackupEnabled, _settings.TagBackupFolder);
            var result = await Task.Run(() => TrackTagWriteService.WriteAlbum(album, dialog.EditedTracks, backupOptions));
            var refreshed = album.Tracks.First().IsArchiveEntry
                ? ZipAlbumReader.Open(album.Path)
                : ZipAlbumReader.OpenFolder(album.Path);
            ReplaceLibraryAlbum(album, refreshed, selectedResultFileName);
            SaveLibraryCache();
            StatusText.Text = $"タグ・ファイル名の変更を{dialog.EditedTracks.Count}曲へ保存しました";
            var backupMessage = result.BackupPaths.Count switch
            {
                0 => "\nバックアップ: 作成しない設定",
                1 => $"\n\nバックアップ:\n{result.BackupPaths[0]}",
                _ => $"\n\nバックアップ: {result.BackupPaths.Count}個\n保存先: {_settings.TagBackupFolder}"
            };
            MessageBox.Show(this,
                $"{dialog.EditedTracks.Count}曲のタグ・ファイル名の変更を保存しました。\n音声データは再エンコードしていません。{backupMessage}",
                "タグ編集完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "タグを保存できませんでした";
            MessageBox.Show(this,
                $"タグを保存できませんでした。\n元ファイルの置換前に処理を停止しています。\n\n理由: {ex.Message}",
                "タグ編集エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            TrackGrid.IsEnabled = true;
        }
    }

    private void ReplaceLibraryAlbum(ZipAlbum previous, ZipAlbum refreshed, string? selectedFileName)
    {
        var oldItem = _albums.FirstOrDefault(item => string.Equals(item.Album.Path, previous.Path, StringComparison.OrdinalIgnoreCase));
        if (oldItem is not null)
        {
            RemoveAlbumFromArtistTree(oldItem);
            _albums.Remove(oldItem);
            _albumPaths.Remove(previous.Path);
        }
        var newItem = new AlbumListItem(refreshed);
        InsertAlbumSorted(newItem);
        AlbumList.SelectedItem = newItem;
        SetCurrentAlbum(refreshed);
        var selectedIndex = refreshed.Tracks.ToList().FindIndex(track => string.Equals(track.FileName, selectedFileName, StringComparison.Ordinal));
        TrackGrid.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        if (TrackGrid.SelectedItem is not null) TrackGrid.ScrollIntoView(TrackGrid.SelectedItem);
    }

    private void SelectNowPlaying_Click(object sender, RoutedEventArgs e)
    {
        if (_playingAlbum is null || _currentIndex < 0 || _currentIndex >= _playingAlbum.Tracks.Count)
        {
            StatusText.Text = "現在再生中の曲はありません";
            return;
        }
        var item = _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _playingAlbum.Path, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            StatusText.Text = "再生中のアルバムは現在のライブラリ一覧にありません";
            return;
        }

        if (!MatchesAlbumFilter(item)) AlbumFilterTextBox.Clear();
        AlbumList.SelectedItem = item;
        AlbumList.ScrollIntoView(item);
        SetCurrentAlbum(item.Album);
        TrackGrid.SelectedIndex = _currentIndex;
        TrackGrid.ScrollIntoView(item.Album.Tracks[_currentIndex]);
        if (_albumSortMode == AlbumSortMode.ArtistTree) SelectArtistTreeAlbum(item);
        StatusText.Text = $"再生中を選択しました: {item.Title} / {item.Album.Tracks[_currentIndex].Title}";
    }

    private void SelectArtistTreeAlbum(AlbumListItem album)
    {
        var group = _artistTreeRoots.FirstOrDefault(root => root.Children.Any(child => ReferenceEquals(child.AlbumItem, album)));
        var node = group?.Children.FirstOrDefault(child => ReferenceEquals(child.AlbumItem, album));
        if (group is null || node is null) return;
        ArtistTree.UpdateLayout();
        if (ArtistTree.ItemContainerGenerator.ContainerFromItem(group) is not System.Windows.Controls.TreeViewItem groupContainer) return;
        groupContainer.IsExpanded = true;
        groupContainer.UpdateLayout();
        if (groupContainer.ItemContainerGenerator.ContainerFromItem(node) is System.Windows.Controls.TreeViewItem nodeContainer)
        {
            nodeContainer.IsSelected = true;
            nodeContainer.BringIntoView();
        }
    }

    private void UpdatePlayingAlbumIndicator(ZipAlbum? playingAlbum)
    {
        foreach (var item in _albums)
        {
            item.SetPlaying(playingAlbum is not null
                && string.Equals(item.Album.Path, playingAlbum.Path, StringComparison.OrdinalIgnoreCase));
            for (var index = 0; index < item.Album.Tracks.Count; index++)
                item.Album.Tracks[index].IsPlaying = playingAlbum is not null
                    && string.Equals(item.Album.Path, playingAlbum.Path, StringComparison.OrdinalIgnoreCase)
                    && index == _currentIndex;
        }
        TrackGrid.Items.Refresh();
        QueueCoverFlowRefresh();
    }

    private bool IsAlbumActivelyPlaying(string albumPath) =>
        _output?.PlaybackState == PlaybackState.Playing
        && _playingAlbum is not null
        && string.Equals(_playingAlbum.Path, albumPath, StringComparison.OrdinalIgnoreCase);

    private JewelCasePlaybackState GetJewelCasePlaybackState()
    {
        ZipTrack? track = _playingAlbum is not null
            && _currentIndex >= 0
            && _currentIndex < _playingAlbum.Tracks.Count
            ? _playingAlbum.Tracks[_currentIndex]
            : null;
        var title = track is null
            ? LocalizationService.Select("停止中", "Stopped")
            : string.IsNullOrWhiteSpace(track.Artist)
                ? track.Title
                : $"{track.Title}  •  {track.Artist}";
        return new JewelCasePlaybackState(
            title,
            _output?.PlaybackState == PlaybackState.Playing,
            track is not null && _output is not null,
            VolumeSlider.Value);
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        _shuffle = !_shuffle;
        _gapless?.ConfigureNavigation(_shuffle, (int)_repeat);
        ShuffleButton.Content = _shuffle ? "⤨ ON" : "⤨ OFF";
        ShuffleButton.Background = _shuffle ? System.Windows.Media.Brushes.SteelBlue : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 49, 58));
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeat = _repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        _gapless?.ConfigureNavigation(_shuffle, (int)_repeat);
        RepeatButton.Content = _repeat switch { RepeatMode.All => "↻ 全曲", RepeatMode.One => "↻ 1曲", _ => "↻ OFF" };
    }

    private void PositionSlider_DragStarted(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<System.Windows.Controls.Primitives.Thumb>(e.OriginalSource as DependencyObject) is not null)
        {
            _draggingPosition = true;
            return;
        }
        if (PositionSlider.ActualWidth <= 0) return;
        var ratio = Math.Clamp(e.GetPosition(PositionSlider).X / PositionSlider.ActualWidth, 0, 1);
        PositionSlider.Value = PositionSlider.Minimum + ratio * (PositionSlider.Maximum - PositionSlider.Minimum);
        SeekReaderSafely(PositionSlider.Value);
        ElapsedText.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
        _draggingPosition = false;
        e.Handled = true;
    }
    private void PositionSlider_DragCompleted(object sender, MouseButtonEventArgs e)
    {
        SeekReaderSafely(PositionSlider.Value);
        _draggingPosition = false;
        UpdatePosition();
    }

    private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeGain is not null) _volumeGain.Volume = (float)e.NewValue;
        UpdateVolumeDisplay(e.NewValue);
    }

    private void UpdateVolumeDisplay(double value)
    {
        if (VolumeValueText is null || VolumeSlider is null) return;
        if (_faithfulMode)
        {
            VolumeValueText.Text = "固定";
            VolumeValueText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(126, 222, 162));
            VolumeSlider.ToolTip = "原音忠実モードではアプリ側の音量を変更しません（Windows／アンプ側で調整）";
            return;
        }

        var percent = $"{Math.Round(value * 100):0}%";
        VolumeValueText.Text = percent;
        VolumeValueText.Foreground = value > 1
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 176, 74))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245));
        VolumeSlider.ToolTip = value > 1
            ? $"増幅音量 {percent}（リミッターで音割れを抑制）"
            : $"音量 {percent}（矢印キーで1%調整）";
    }

    private void FaithfulMode_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = FaithfulModeCheck?.IsChecked == true;
        var changed = _faithfulMode != enabled;
        _faithfulMode = enabled;
        UpdateFaithfulModeUi();

        if (!changed || !IsLoaded) return;
        RebuildAudioEffects();
        SaveSettings();
    }

    private void UpdateFaithfulModeUi()
    {
        if (AudioEnhancementControls is null || EqualizerControls is null || EqualizerExpander is null
            || PlaybackRateButton is null || VolumeSlider is null || VolumeControls is null
            || FaithfulModeStatusText is null) return;

        var adjustable = !_faithfulMode;
        AudioEnhancementControls.IsEnabled = adjustable;
        AudioEnhancementControls.Opacity = adjustable ? 1 : 0.42;
        EqualizerControls.IsEnabled = adjustable;
        EqualizerExpander.IsEnabled = adjustable;
        EqualizerExpander.Opacity = adjustable ? 1 : 0.42;
        PlaybackRateButton.IsEnabled = adjustable;
        PlaybackRateButton.Opacity = adjustable ? 1 : 0.42;
        VolumeControls.IsEnabled = adjustable;
        VolumeControls.Opacity = adjustable ? 1 : 0.42;
        var disabledReason = LocalizationService.Select(
            "原音忠実モード中は使用できません", "Unavailable in Source-Faithful Mode");
        AudioEnhancementControls.ToolTip = adjustable ? null : disabledReason;
        EqualizerExpander.ToolTip = adjustable ? null : disabledReason;
        VolumeControls.ToolTip = adjustable ? null : disabledReason;
        UpdatePlaybackRateButton();
        UpdateVolumeDisplay(VolumeSlider.Value);

        if (!_faithfulMode)
        {
            FaithfulModeStatusText.Text = "OFF：音質向上・EQ・音量調整を使用します";
        }
        else if (_output is null)
        {
            FaithfulModeStatusText.Text = "ON：WASAPI排他を優先・DSPなし・音量固定";
        }
    }

    private void EqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not System.Windows.Controls.Slider slider || !int.TryParse(slider.Tag?.ToString(), out var band)) return;
        _eqGains[band] = e.NewValue;
        _equalizer?.SetGain(band, e.NewValue);
        slider.ToolTip = $"{EqualizerSampleProvider.Frequencies[band]:0} Hz: {e.NewValue:+0.0;-0.0;0.0} dB";
        if (!_applyingEqPreset && IsLoaded)
            EqPresetCombo.SelectedItem = EqPresetCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                .First(item => item.Tag?.ToString() == "Custom");
    }

    private void RemasterMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RemasterModeCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item
            || !Enum.TryParse<RemasterMode>(item.Tag?.ToString(), true, out var mode)) return;
        _remasterMode = mode;
        _remaster?.SetMode(mode);
        if (IsLoaded) SaveSettings();
    }

#if AI_FEATURE
    private void AiMixSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AiMixValueText is not null) AiMixValueText.Text = $"{e.NewValue:0}%";
    }

    private void KaraokeRemovalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (KaraokeRemovalValueText is not null) KaraokeRemovalValueText.Text = $"{e.NewValue:0}%";
    }

    private async void AiPrepare_Click(object sender, RoutedEventArgs e)
    {
        if (_aiGuitarService.IsBusy) return;
        _aiPreparationFailed = false;
        _aiCancellation?.Cancel();
        _aiCancellation = new CancellationTokenSource();
        SetAiUiBusy(true);
        AiProgressBar.IsIndeterminate = true;
        try
        {
            await _aiGuitarService.PrepareAsync(message => Dispatcher.BeginInvoke(() =>
            {
                AiStatusText.Text = message;
                KaraokeStatusText.Text = message;
                StatusText.Text = message;
            }), _aiCancellation.Token);
            _aiPreparationFailed = false;
            AiStatusText.Text = "AI環境は準備済みです。初回プレビュー時に分離モデルを取得します。";
            KaraokeStatusText.Text = "AI環境は準備済みです";
            StatusText.Text = "AI追加機能の準備が完了しました";
        }
        catch (OperationCanceledException)
        {
            _aiPreparationFailed = true;
            AiStatusText.Text = "AI機能の準備を中止しました";
            KaraokeStatusText.Text = "AI機能の準備を中止しました";
        }
        catch (Exception ex)
        {
            _aiPreparationFailed = true;
            AiStatusText.Text = "AI機能を準備できませんでした";
            KaraokeStatusText.Text = "AI機能を準備できませんでした";
            MessageBox.Show(this, ex.Message, "AI準備エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            AiProgressBar.IsIndeterminate = false;
            SetAiUiBusy(false);
        }
    }

    private async void AiCreatePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_aiGuitarService.IsBusy) return;
        var track = GetAiTargetTrack();
        if (track is null)
        {
            MessageBox.Show(this, "プレビューを作る曲を曲目リストで選択してください。", "曲を選択してください",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_aiGuitarService.IsRuntimePresent)
        {
            MessageBox.Show(this, "先に「AI機能を準備」を実行してください。", "AI機能は未準備です",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var style = (AiGuitarStyleCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "modern_metal";
        var startSeconds = _reader is not null && _playingAlbum is not null && _currentIndex >= 0
            && ReferenceEquals(_playingAlbum.Tracks[_currentIndex], track) ? _reader.CurrentTime.TotalSeconds : 0;
        if (_output?.PlaybackState == PlaybackState.Playing)
        {
            AccumulateUsageTime();
            _output.Pause();
            _timer.Stop();
            PlayButton.Content = "▶ 再生";
            PlaybackStatusText.Text = "一時停止（AI処理中）";
            PlaybackBadgeText.Text = "一時停止";
        }

        _aiCancellation?.Cancel();
        _aiCancellation = new CancellationTokenSource();
        SetAiUiBusy(true);
        AiProgressBar.Value = 0;
        AiPlayPreviewButton.IsEnabled = false;
        try
        {
            _aiPreviewPath = await _aiGuitarService.CreatePreviewAsync(track, startSeconds, style, AiMixSlider.Value,
                (message, percent) => Dispatcher.BeginInvoke(() =>
                {
                    AiStatusText.Text = message;
                    AiProgressBar.Value = percent;
                    StatusText.Text = $"AI処理 {percent}%: {message}";
                }), _aiCancellation.Token);
            AiProgressBar.Value = 100;
            AiStatusText.Text = $"完成: {track.Title}（{startSeconds:0}秒から最大{AiGuitarPreviewService.PreviewDurationSeconds}秒）";
            StatusText.Text = "AIギター・プレビューが完成しました";
            AiPlayPreviewButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { AiStatusText.Text = "AI処理を中止しました"; }
        catch (Exception ex)
        {
            AiStatusText.Text = "AIプレビューを作成できませんでした";
            MessageBox.Show(this, ex.Message, "AI処理エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetAiUiBusy(false); }
    }

    private async void KaraokeCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_aiGuitarService.IsBusy) return;
        var track = GetAiTargetTrack();
        if (track is null)
        {
            MessageBox.Show(this, "カラオケを作る曲を曲目リストで選択してください。", "曲を選択してください",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_aiGuitarService.IsRuntimePresent)
        {
            MessageBox.Show(this, "先に「AI機能を準備」を実行してください。", "AI機能は未準備です",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startSeconds = _reader is not null && _playingAlbum is not null && _currentIndex >= 0
            && ReferenceEquals(_playingAlbum.Tracks[_currentIndex], track) ? _reader.CurrentTime.TotalSeconds : 0;
        if (_output?.PlaybackState == PlaybackState.Playing)
        {
            AccumulateUsageTime();
            _output.Pause();
            _timer.Stop();
            PlayButton.Content = "▶ 再生";
            PlaybackStatusText.Text = "一時停止（AIカラオケ処理中）";
            PlaybackBadgeText.Text = "一時停止";
        }

        _aiCancellation?.Cancel();
        _aiCancellation = new CancellationTokenSource();
        SetAiUiBusy(true);
        KaraokeProgressBar.Value = 0;
        try
        {
            _karaokePreviewPath = await _aiGuitarService.CreateKaraokePreviewAsync(track, startSeconds,
                KaraokeRemovalSlider.Value, (message, percent) => Dispatcher.BeginInvoke(() =>
                {
                    KaraokeStatusText.Text = message;
                    KaraokeProgressBar.Value = percent;
                    StatusText.Text = $"AIカラオケ処理 {percent}%: {message}";
                }), _aiCancellation.Token);
            KaraokeProgressBar.Value = 100;
            KaraokeStatusText.Text = $"完成: {track.Title}（{startSeconds:0}秒から最大{AiGuitarPreviewService.PreviewDurationSeconds}秒）";
            StatusText.Text = "AIカラオケが完成しました";
        }
        catch (OperationCanceledException) { KaraokeStatusText.Text = "AIカラオケ処理を中止しました"; }
        catch (Exception ex)
        {
            KaraokeStatusText.Text = "AIカラオケを作成できませんでした";
            MessageBox.Show(this, ex.Message, "AIカラオケ処理エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetAiUiBusy(false); }
    }

    private void AiPlayPreview_Click(object sender, RoutedEventArgs e)
        => ToggleAiPreviewPlayback(_aiPreviewPath, AiPlayPreviewButton, "AIリメイク");

    private void KaraokePlay_Click(object sender, RoutedEventArgs e)
        => ToggleAiPreviewPlayback(_karaokePreviewPath, KaraokePlayButton, "AIカラオケ");

    private void ToggleAiPreviewPlayback(string? previewPath, System.Windows.Controls.Button button, string label)
    {
        if (_faithfulMode)
        {
            MessageBox.Show(this,
                "原音忠実モードでは加工済みのAI試聴音を再生しません。比較する場合は原音忠実モードをOFFにしてください。",
                "原音忠実モード", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_aiPreviewOutput is not null && ReferenceEquals(_activeAiPreviewButton, button)
            && _aiPreviewOutput.PlaybackState == PlaybackState.Playing)
        {
            _aiPreviewOutput.Pause();
            _timer.Stop();
            button.Content = label == "AIカラオケ" ? "カラオケ版を再開" : "AI版を再開";
            PlaybackStatusText.Text = $"{label} 一時停止";
            return;
        }
        if (_aiPreviewOutput is not null && ReferenceEquals(_activeAiPreviewButton, button)
            && _aiPreviewOutput.PlaybackState == PlaybackState.Paused)
        {
            _aiPreviewOutput.Play();
            _timer.Start();
            button.Content = label == "AIカラオケ" ? "カラオケ版を一時停止" : "AI版を一時停止";
            UpdateAiPlaybackStatus();
            return;
        }
        if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath)) return;

        var previewTrack = GetAiTargetTrack();
        StopPlayback(resetPosition: false);
        try
        {
            if (previewTrack is not null) ShowNowPlaying(previewTrack);
            _activeAiPreviewButton = button;
            _activeAiPreviewLabel = label;
            _aiPreviewReader = new AudioFileReader(previewPath);
            _aiRemaster = new RemasterSampleProvider(_aiPreviewReader.ToSampleProvider(), _remasterMode);
            _aiEqualizer = new EqualizerSampleProvider(_aiRemaster, _eqGains)
            {
                Enabled = EqEnabledCheck.IsChecked == true,
                ClampOutput = false
            };
            _aiBassBoost = new BassBoostSampleProvider(_aiEqualizer, BassBoostEnabledCheck.IsChecked == true, BassBoostSlider.Value);
            _aiLowVolumeClarity = new LowVolumeClaritySampleProvider(_aiBassBoost, LowVolumeClarityCheck.IsChecked == true);
            _aiVolumeGain = new NAudio.Wave.SampleProviders.VolumeSampleProvider(_aiLowVolumeClarity) { Volume = (float)VolumeSlider.Value };
            _aiWaveform = new WaveformCaptureSampleProvider(new SoftLimiterSampleProvider(_aiVolumeGain));
            _aiSpectrum = new SpectrumCaptureSampleProvider(_aiWaveform);
            _aiPreviewOutput = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3, Volume = 1.0f };
            _aiPreviewOutput.PlaybackStopped += AiPreviewOutput_PlaybackStopped;
            _aiPreviewOutput.Init(_aiSpectrum);
            _aiPreviewOutput.Play();
            PositionSlider.Maximum = Math.Max(0.1, _aiPreviewReader.TotalTime.TotalSeconds);
            PositionSlider.Value = 0;
            ElapsedText.Text = "0:00";
            TotalText.Text = FormatTime(_aiPreviewReader.TotalTime);
            _timer.Start();
            button.Content = label == "AIカラオケ" ? "カラオケ版を一時停止" : "AI版を一時停止";
            UpdateAiPlaybackStatus();
            PlaybackBadgeText.Text = "AI試聴中";
        }
        catch (Exception ex)
        {
            DisposeAiPreviewPlayback();
            MessageBox.Show(this, ex.Message, "AIプレビュー再生エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AiPreviewOutput_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _aiPreviewOutput)) return;
            DisposeAiPreviewPlayback();
            AiPlayPreviewButton.IsEnabled = _aiPreviewPath is not null && File.Exists(_aiPreviewPath);
            KaraokePlayButton.IsEnabled = _karaokePreviewPath is not null && File.Exists(_karaokePreviewPath);
            PlaybackStatusText.Text = e.Exception is null ? "AIプレビューの再生が終了しました" : "AIプレビュー再生エラー";
            PlaybackBadgeText.Text = "停止";
        });
    }

    private void AiDeleteData_Click(object sender, RoutedEventArgs e)
    {
        if (_aiGuitarService.IsBusy) return;
        if (MessageBox.Show(this, "AI実行環境、分離モデル、作成済みプレビューを削除します。\n必要になった時は再取得できます。",
            "AIデータを削除しますか？", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            DisposeAiPreviewPlayback();
            _aiPreviewPath = null;
            _karaokePreviewPath = null;
            _aiGuitarService.DeleteAllData();
            _aiPreparationFailed = false;
            AiProgressBar.Value = 0;
            AiPlayPreviewButton.IsEnabled = false;
            AiPlayPreviewButton.Content = "AI版を試聴";
            KaraokeProgressBar.Value = 0;
            KaraokePlayButton.IsEnabled = false;
            KaraokePlayButton.Content = "カラオケ版を試聴";
            KaraokeStatusText.Text = "AIデータを削除しました";
            AiStatusText.Text = "AIデータを削除しました";
            StatusText.Text = "AI追加データを削除しました";
            UpdateAiPrepareButton();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "AIデータ削除エラー", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private ZipTrack? GetAiTargetTrack()
    {
        if (TrackGrid.SelectedItem is ZipTrack selected) return selected;
        if (_playingAlbum is not null && _currentIndex >= 0 && _currentIndex < _playingAlbum.Tracks.Count)
            return _playingAlbum.Tracks[_currentIndex];
        return _album?.Tracks.FirstOrDefault(track => track.IsSupported);
    }

    private void SetAiUiBusy(bool busy)
    {
        UpdateAiPrepareButton(busy);
        AiCreatePreviewButton.IsEnabled = !busy;
        KaraokeCreateButton.IsEnabled = !busy;
        AiDeleteDataButton.IsEnabled = !busy;
        AiPlayPreviewButton.IsEnabled = !busy && _aiPreviewPath is not null && File.Exists(_aiPreviewPath);
        KaraokePlayButton.IsEnabled = !busy && _karaokePreviewPath is not null && File.Exists(_karaokePreviewPath);
    }

    private void UpdateAiPrepareButton(bool busy = false)
    {
        var prepared = _aiGuitarService.IsRuntimePresent && !_aiPreparationFailed;
        AiPrepareButton.IsEnabled = !busy && !prepared;
        KaraokePrepareButton.IsEnabled = !busy && !prepared;
        AiPrepareButton.ToolTip = prepared
            ? "AI機能は準備済みです。再準備は必要ありません。"
            : "AI機能に必要な追加データを準備します。";
        KaraokePrepareButton.ToolTip = AiPrepareButton.ToolTip;
    }
#endif

    private void EqEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_equalizer is not null) _equalizer.Enabled = EqEnabledCheck.IsChecked == true;
    }

    private void BassBoostEnabled_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = BassBoostEnabledCheck.IsChecked == true;
        if (_bassBoost is not null) _bassBoost.Enabled = enabled;
        if (IsLoaded) SaveSettings();
    }

    private void BassBoostSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BassBoostValueText is not null) BassBoostValueText.Text = $"{e.NewValue:0}%";
        _bassBoost?.SetAmount(e.NewValue);
        if (IsLoaded) SaveSettings();
    }

    private void LowVolumeClarity_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = LowVolumeClarityCheck.IsChecked == true;
        if (_lowVolumeClarity is not null) _lowVolumeClarity.Enabled = enabled;
        if (IsLoaded) SaveSettings();
    }

#if AI_FEATURE
    private void UpdateAiPlaybackStatus()
    {
        if (_aiPreviewOutput?.PlaybackState != PlaybackState.Playing) return;
        var enhancement = (RemasterModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()
            ?? _remasterMode.ToString();
        PlaybackStatusText.Text = $"{_activeAiPreviewLabel}再生中  •  音質向上: {enhancement}  •  EQ: {(EqEnabledCheck.IsChecked == true ? "ON" : "OFF")}  •  EXTRA BASS: {(BassBoostEnabledCheck.IsChecked == true ? "ON" : "OFF")}  •  小音量: {(LowVolumeClarityCheck.IsChecked == true ? "ON" : "OFF")}";
    }
#endif

    private void EqReset_Click(object sender, RoutedEventArgs e)
    {
        EqPresetCombo.SelectedItem = EqPresetCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>().First(item => item.Tag?.ToString() == "Flat");
    }

    private void EqPreset_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (EqPresetCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        var key = item.Tag?.ToString();
        if (key is null || key == "Custom" || !EqPresets.TryGetValue(key, out var gains)) return;
        _applyingEqPreset = true;
        try
        {
            System.Windows.Controls.Slider[] sliders = [Eq0, Eq1, Eq2, Eq3, Eq4, Eq5, Eq6, Eq7, Eq8, Eq9];
            for (var i = 0; i < sliders.Length; i++) sliders[i].Value = gains[i];
        }
        finally { _applyingEqPreset = false; }
    }

    private void PlaybackRateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (PlaybackRateButton.ContextMenu is null) return;
        PlaybackRateButton.ContextMenu.PlacementTarget = PlaybackRateButton;
        PlaybackRateButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PlaybackRateButton.ContextMenu.IsOpen = true;
    }

    private void PlaybackSpeedMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem selected
            || !double.TryParse(selected.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var speed)) return;
        foreach (var item in PlaybackRateMenu.Items.OfType<System.Windows.Controls.MenuItem>()
            .Where(item => item.Tag is not null)) item.IsChecked = ReferenceEquals(item, selected);
        _playbackSpeed = speed;
        UpdatePlaybackRateButton();
        RebuildAudioEffects();
        if (IsLoaded) SaveSettings();
        e.Handled = true;
    }

    private void PreservePitchMenu_Click(object sender, RoutedEventArgs e)
    {
        _preservePitch = PreservePitchMenuItem.IsChecked;
        UpdatePlaybackRateButton();
        RebuildAudioEffects();
        if (IsLoaded) SaveSettings();
        e.Handled = true;
    }

    private void UpdatePlaybackRateButton()
    {
        if (PlaybackRateButton is null) return;
        if (_faithfulMode)
        {
            PlaybackRateButton.Content = "原音忠実  1.0×  │  音程固定";
            PlaybackRateButton.ToolTip = "速度・音程を変更せず再生します（OFFにすると以前の速度設定へ戻ります）";
            PlaybackRateButton.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(38, 91, 69));
            return;
        }

        PlaybackRateButton.Content = $"再生速度  {_playbackSpeed:0.##}×  │  {(_preservePitch ? "音程 ✓" : "音程 可変")}";
        PlaybackRateButton.ToolTip = _preservePitch
            ? $"{_playbackSpeed:0.##}倍速・音程を維持" : $"{_playbackSpeed:0.##}倍速・速度に合わせて音程も変化";
        PlaybackRateButton.Background = new System.Windows.Media.SolidColorBrush(
            Math.Abs(_playbackSpeed - 1) < 0.001
                ? System.Windows.Media.Color.FromRgb(36, 55, 70)
                : System.Windows.Media.Color.FromRgb(38, 79, 104));
    }

    private void VisualizerMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (WaveformDisplay is null || SpectrumDisplay is null || SpectrumTrackOverlay is null) return;
        var spectrum = (VisualizerModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() == "Spectrum";
        WaveformDisplay.Visibility = spectrum ? Visibility.Collapsed : Visibility.Visible;
        SpectrumDisplay.Visibility = spectrum ? Visibility.Visible : Visibility.Collapsed;
        SpectrumTrackOverlay.Visibility = spectrum ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) SaveSettings();
    }

    private void RebuildAudioEffects()
    {
        SynchronizeGaplessTrack();
        if (_reader is null || _output is null || _playingAlbum is null || _currentIndex < 0) return;
        var position = _reader.CurrentTime;
        var wasPlaying = _output.PlaybackState == PlaybackState.Playing;
        PlayTrack(_playingAlbum, _currentIndex, position, wasPlaying, countAsNewUsageSession: false,
            queueOverride: _gapless?.Entries);
    }

    private void SeekReaderSafely(double seconds)
    {
        SynchronizeGaplessTrack();
        if (_reader is not null)
        {
            var safeMaximum = Math.Max(0, _reader.TotalTime.TotalSeconds - 0.05);
            _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, safeMaximum));
            return;
        }
    }

    private void UpdatePosition()
    {
        SynchronizeGaplessTrack();
        AccumulateUsageTime();
        if (_reader is not null)
        {
            if (!_draggingPosition) PositionSlider.Value = Math.Min(PositionSlider.Maximum, _reader.CurrentTime.TotalSeconds);
            ElapsedText.Text = FormatTime(_reader.CurrentTime);
            WaveformDisplay.SetSamples(_waveform?.GetSnapshot());
            SpectrumDisplay.SetBands(_spectrum?.GetSnapshot());
            UpdateLyricsAutoScroll(_reader.CurrentTime.TotalSeconds, _reader.TotalTime.TotalSeconds);
            return;
        }
    }

    private void Gapless_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = GaplessCheck?.IsChecked == true;
        if (_gaplessEnabled == enabled) return;
        _gaplessEnabled = enabled;
        RebuildAudioEffects();
    }

    private List<PlaybackQueueEntry> BuildGaplessQueue(ZipAlbum current)
    {
        if (_favoriteQueue.Count > 0)
            return _favoriteQueue.Select((entry, index) => new PlaybackQueueEntry(entry.Album,
                entry.Album.Tracks.ToList().IndexOf(entry.Track), index))
                .Where(entry => entry.TrackIndex >= 0 && entry.Track.IsSupported).ToList();
        // Capture the currently displayed album order. Browsing during playback
        // does not mutate the active queue; a new explicit play captures it again.
        var albums = _albumView.Cast<AlbumListItem>().Select(item => item.Album).ToList();
        if (!albums.Any(album => ReferenceEquals(album, current))) albums.Insert(0, current);
        return albums.SelectMany(album => album.Tracks.Select((track, index) => new PlaybackQueueEntry(album, index)))
            .Where(entry => entry.Track.IsSupported).ToList();
    }

    private void PlayQueueEntry(PlaybackQueueEntry entry)
    {
        var queue = _gapless?.Entries;
        var fromFavorites = entry.FavoriteIndex >= 0 && entry.FavoriteIndex < _favoriteQueue.Count;
        if (fromFavorites) _favoriteQueueIndex = entry.FavoriteIndex;
        SetCurrentAlbum(entry.Album);
        PlayTrack(entry.Album, entry.TrackIndex, fromFavorites: fromFavorites, queueOverride: queue);
    }

    private void SynchronizeGaplessTrack()
    {
        if (_gapless is null) return;
        var snapshot = _gapless.Snapshot;
        if (snapshot.Revision == _gaplessRevision) return;
        AccumulateUsageTime();
        var wasViewingPlayingAlbum = ReferenceEquals(_album, _playingAlbum);
        _gaplessRevision = snapshot.Revision;
        _playingAlbum = snapshot.Entry.Album;
        _currentIndex = snapshot.Entry.TrackIndex;
        if (snapshot.Entry.FavoriteIndex >= 0) _favoriteQueueIndex = snapshot.Entry.FavoriteIndex;
        UpdatePlayingAlbumIndicator(_playingAlbum);
        if (wasViewingPlayingAlbum)
        {
            var albumItem = _albums.FirstOrDefault(item => ReferenceEquals(item.Album, _playingAlbum));
            if (albumItem is not null) AlbumList.SelectedItem = albumItem;
            SetCurrentAlbum(_playingAlbum);
            TrackGrid.SelectedIndex = _currentIndex;
        }
        PositionSlider.Maximum = Math.Max(0.1, snapshot.Duration.TotalSeconds);
        if (!_draggingPosition) PositionSlider.Value = Math.Min(PositionSlider.Maximum, snapshot.Position.TotalSeconds);
        TotalText.Text = FormatTime(snapshot.Duration);
        ShowNowPlaying(snapshot.Entry.Track);
        StartUsageTracking(snapshot.Entry.Track, countAsNewSession: true);
        Title = $"{snapshot.Entry.Track.Title} — {_applicationTitle}";
        PlaybackStatusText.Text = _output?.PlaybackState == PlaybackState.Paused
            ? LocalizationService.Select("一時停止", "Paused")
            : LocalizationService.Select($"再生中: {snapshot.Entry.Track.Title}  •  ギャップレス",
                $"Playing: {snapshot.Entry.Track.Title}  •  Gapless");
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FavoritesWindow(BuildFavoriteEntries()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _favoriteQueue = dialog.PlaybackQueue;
        PlayFavoriteTrack(dialog.PlaybackIndex);
    }

    private IReadOnlyList<FavoriteTrackEntry> BuildFavoriteEntries() =>
        _albums.SelectMany(album => album.Album.Tracks.Select(track => new FavoriteTrackEntry(
            album.Album, track, _favoritesStore.IsAlbumFavorite(album.Album), _favoritesStore.IsTrackFavorite(track))))
            .Where(entry => entry.AlbumFavorite || entry.TrackFavorite).ToList();

    private void PlayFavoriteTrack(int index)
    {
        if (index < 0 || index >= _favoriteQueue.Count) return;
        var entry = _favoriteQueue[index];
        var trackIndex = entry.Album.Tracks.ToList().IndexOf(entry.Track);
        if (trackIndex < 0) { StopPlayback(resetPosition: true); return; }
        _favoriteQueueIndex = index;
        var albumItem = _albums.FirstOrDefault(item => ReferenceEquals(item.Album, entry.Album));
        if (albumItem is not null)
        {
            if (!MatchesAlbumFilter(albumItem)) AlbumFilterTextBox.Clear();
            AlbumList.SelectedItem = albumItem;
            AlbumList.ScrollIntoView(albumItem);
        }
        SetCurrentAlbum(entry.Album);
        PlayTrack(entry.Album, trackIndex, fromFavorites: true);
        if (_favoriteQueue.Count > 0)
            StatusText.Text = LocalizationService.Select($"お気に入り {index + 1} / {_favoriteQueue.Count}: {entry.Title}",
                $"Favorites {index + 1} / {_favoriteQueue.Count}: {entry.Title}");
    }

    private void Usage_Click(object sender, RoutedEventArgs e)
    {
        AccumulateUsageTime();
        _usageStore.Save();
        var dialog = new UsageWindow(_usageStore.Snapshot()) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedForPlayback is not null)
            PlayFromUsageHistory(dialog.SelectedForPlayback);
    }

    private void PlayFromUsageHistory(PlaybackUsageEntry entry)
    {
        var match = FindUsageHistoryTrack(entry);
        if (match is null)
        {
            MessageBox.Show(this,
                "履歴に記録された元の曲をライブラリで見つけられませんでした。\n音楽フォルダを移動した場合は、設定画面から移動先を指定してください。",
                "履歴から再生できません", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"履歴の曲が見つかりません: {entry.Title}";
            return;
        }
        var (albumItem, trackIndex) = match.Value;

        if (!MatchesAlbumFilter(albumItem)) AlbumFilterTextBox.Clear();
        AlbumList.SelectedItem = albumItem;
        AlbumList.ScrollIntoView(albumItem);
        SetCurrentAlbum(albumItem.Album);
        TrackGrid.SelectedIndex = trackIndex;
        TrackGrid.ScrollIntoView(albumItem.Album.Tracks[trackIndex]);
        if (_albumSortMode == AlbumSortMode.ArtistTree) SelectArtistTreeAlbum(albumItem);
        PlayTrack(albumItem.Album, trackIndex);
        StatusText.Text = $"履歴から再生しました: {entry.Title}";
    }

    private (AlbumListItem Album, int TrackIndex)? FindUsageHistoryTrack(PlaybackUsageEntry entry)
    {
        foreach (var candidate in _albums)
        {
            var trackIndex = candidate.Album.Tracks.ToList().FindIndex(track =>
                string.Equals(PlaybackUsageStore.CreateTrackKey(track), entry.Key, StringComparison.Ordinal));
            if (trackIndex >= 0) return (candidate, trackIndex);
        }
        return null;
    }

    private void StartUsageTracking(ZipTrack track, bool countAsNewSession)
    {
        var entry = _usageStore.GetOrCreate(track);
        if (countAsNewSession || !ReferenceEquals(_activeUsageEntry, entry))
        {
            _activeUsageEntry = entry;
            _activeUsageSessionSeconds = 0;
            _activeUsagePlayCommitted = false;
        }
        _usageLastTickUtc = DateTime.UtcNow;
    }

    private void AccumulateUsageTime()
    {
        if (_activeUsageEntry is null || _output?.PlaybackState != PlaybackState.Playing) return;
        var now = DateTime.UtcNow;
        if (_usageLastTickUtc == default) { _usageLastTickUtc = now; return; }
        var elapsed = Math.Clamp((now - _usageLastTickUtc).TotalSeconds, 0, 2);
        _usageLastTickUtc = now;
        if (elapsed <= 0) return;
        _usageStore.AddPlaybackTime(_activeUsageEntry, elapsed);
        _activeUsageSessionSeconds += elapsed;
        if (!_activeUsagePlayCommitted && _activeUsageSessionSeconds >= 5)
        {
            _usageStore.CommitPlay(_activeUsageEntry);
            _activeUsagePlayCommitted = true;
        }
    }

    private void ShowNowPlaying(ZipTrack track)
    {
        UpdateNowPlayingHeader(track);
        if (_playingAlbum is not null) LoadLyrics(_playingAlbum, track);
    }

    private void UpdateNowPlayingHeader(ZipTrack track)
    {
        NowPlayingTitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title;
        var artist = string.IsNullOrWhiteSpace(track.Artist) ? "アーティスト不明" : track.Artist;
        SpectrumTitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title;
        SpectrumArtistText.Text = artist;
        var album = string.IsNullOrWhiteSpace(track.Album) ? "アルバム不明" : track.Album;
        var details = new List<string> { artist };
        var selectedAlbumTitle = _album?.Tracks.FirstOrDefault()?.Album;
        if (!AreDisplayValuesEquivalent(album, artist) && !AreDisplayValuesEquivalent(album, selectedAlbumTitle))
            details.Add(album);
        details.Add($"#{track.TrackNumber}");
        details.Add(track.DurationText);
        details.Add(track.AudioText);
        if (!string.IsNullOrWhiteSpace(track.Year)) details.Add(track.Year);
        if (!string.IsNullOrWhiteSpace(track.Genre)) details.Add(track.Genre);
        NowPlayingTagText.Text = string.Join("  •  ", details);
        NowPlayingTagText.ToolTip = $"タイトル: {NowPlayingTitleText.Text}\nアーティスト: {artist}\nアルバム: {album}\nトラック: {track.TrackNumber}\n音質: {track.AudioText}\n時間: {track.DurationText}"
            + (string.IsNullOrWhiteSpace(track.Year) ? "" : $"\n年: {track.Year}")
            + (string.IsNullOrWhiteSpace(track.Genre) ? "" : $"\nジャンル: {track.Genre}");
    }

    private static bool AreDisplayValuesEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());
        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

    private void ClearNowPlaying()
    {
        PlaybackBadgeText.Text = "停止";
        NowPlayingTitleText.Text = "再生していません";
        NowPlayingTagText.Text = "曲を再生するとタグ情報を表示します";
        SpectrumTitleText.Text = "";
        SpectrumArtistText.Text = "";
    }

    private void LoadLyrics(ZipAlbum album, ZipTrack track, bool force = false)
    {
        if (!force && ReferenceEquals(_lyricsAlbum, album) && ReferenceEquals(_lyricsTrack, track)) return;
        _lyricsAlbum = album;
        _lyricsTrack = track;
        _loadingLyrics = true;
        try
        {
            var savedPath = GetSavedLyricsPath(album, track);
            if (File.Exists(savedPath))
            {
                var savedLyrics = DecodeLyricsText(File.ReadAllBytes(savedPath), stripLrcTiming: false);
                SetLyricsContent(savedLyrics);
                LyricsStatusText.Text = string.IsNullOrWhiteSpace(savedLyrics)
                    ? $"空欄保存: {track.Title}" : $"アプリ保存: {track.Title}";
                return;
            }

            var external = FindExternalLyricsFile(album, track);
            if (external is not null)
            {
                SetLyricsContent(DecodeLyricsText(File.ReadAllBytes(external), stripLrcTiming: false));
                LyricsStatusText.Text = $"テキスト読込: {Path.GetFileName(external)}";
                return;
            }

            var zipText = FindZipLyricsFile(album, track);
            if (zipText is not null)
            {
                SetLyricsContent(DecodeLyricsText(ReadZipTextBytes(zipText), stripLrcTiming: false));
                LyricsStatusText.Text = $"ZIP内テキスト: {zipText.FileName}";
                return;
            }

            SetLyricsContent("");
            LyricsStatusText.Text = $"歌詞なし: {track.Title}";
        }
        catch
        {
            SetLyricsContent("");
            LyricsStatusText.Text = "歌詞テキストを読み込めませんでした";
        }
        finally
        {
            _loadingLyrics = false;
            LyricsEmptyText.Visibility = string.IsNullOrWhiteSpace(LyricsTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static string? FindExternalLyricsFile(ZipAlbum album, ZipTrack track)
    {
        var candidates = new List<string>();
        if (!track.IsArchiveEntry)
        {
            var directory = Path.GetDirectoryName(track.SourcePath);
            var baseName = Path.GetFileNameWithoutExtension(track.SourcePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                candidates.Add(Path.Combine(directory, baseName + ".lrc"));
                candidates.Add(Path.Combine(directory, baseName + ".txt"));
                candidates.Add(Path.Combine(directory, "Lyrics", baseName + ".lrc"));
                candidates.Add(Path.Combine(directory, "Lyrics", baseName + ".txt"));
            }
        }
        else if (album.Path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase))
        {
            var extractedDirectory = album.Path[..^4];
            var relative = track.FileName.Replace('/', Path.DirectorySeparatorChar);
            candidates.Add(Path.Combine(extractedDirectory, Path.ChangeExtension(relative, ".lrc")));
            candidates.Add(Path.Combine(extractedDirectory, Path.ChangeExtension(relative, ".txt")));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static ZipTextFile? FindZipLyricsFile(ZipAlbum album, ZipTrack track)
    {
        var lrcName = NormalizeZipPath(Path.ChangeExtension(track.FileName, ".lrc"));
        var textName = NormalizeZipPath(Path.ChangeExtension(track.FileName, ".txt"));
        return album.TextFiles.FirstOrDefault(file => string.Equals(NormalizeZipPath(file.FileName), lrcName, StringComparison.OrdinalIgnoreCase))
            ?? album.TextFiles.FirstOrDefault(file => string.Equals(NormalizeZipPath(file.FileName), textName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeZipPath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static byte[] ReadZipTextBytes(ZipTextFile file)
    {
        using var bounded = new BoundedFileStream(file.SourcePath, file.DataOffset, file.CompressedSize);
        using var memory = new MemoryStream(file.UncompressedSize > 0 && file.UncompressedSize <= int.MaxValue
            ? (int)file.UncompressedSize : 0);
        if (file.CompressionMethod == 0) bounded.CopyTo(memory);
        else if (file.CompressionMethod == 8)
        {
            using var deflate = new DeflateStream(bounded, CompressionMode.Decompress);
            deflate.CopyTo(memory);
        }
        else throw new NotSupportedException("この歌詞テキストのZIP圧縮方式には対応していません。");
        return memory.ToArray();
    }

    private static string DecodeLyricsText(byte[] bytes, bool stripLrcTiming)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(932).GetString(bytes); }
        }
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\0', '\n', ' ');
        if (!stripLrcTiming) return text;

        var lines = text.Split('\n').Select(line =>
            Regex.Replace(line, @"(?:\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\])+", "").TrimEnd());
        return string.Join(Environment.NewLine, lines.Where(line =>
            !Regex.IsMatch(line, @"^\[(?:ar|ti|al|by|offset|re|ve):.*\]$", RegexOptions.IgnoreCase))).Trim();
    }

    private static string GetSavedLyricsPath(ZipAlbum album, ZipTrack track) =>
        GetSavedLyricsPathForIdentity(album.Path, track.SourcePath, track.FileName, track.IsArchiveEntry);

    private static void UpdateLyricsIndicators(ZipAlbum album)
    {
        foreach (var track in album.Tracks)
            track.HasLyrics = HasRegisteredLyrics(album, track);
    }

    private static bool HasRegisteredLyrics(ZipAlbum album, ZipTrack track)
    {
        var savedPath = GetSavedLyricsPath(album, track);
        if (File.Exists(savedPath))
            return new FileInfo(savedPath).Length > 0;

        var externalPath = FindExternalLyricsFile(album, track);
        if (externalPath is not null)
            return new FileInfo(externalPath).Length > 0;

        var zipText = FindZipLyricsFile(album, track);
        return zipText is not null && zipText.UncompressedSize > 0;
    }

    private static string GetSavedLyricsPathForIdentity(string albumPath, string sourcePath, string fileName, bool isArchiveEntry)
    {
        var normalizedAlbumPath = Path.GetFullPath(albumPath).ToUpperInvariant();
        var identity = isArchiveEntry ? normalizedAlbumPath + "|" + NormalizeZipPath(fileName) : Path.GetFullPath(sourcePath).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return Path.Combine(DataDirectory, "lyrics", key + ".txt");
    }

    private void SaveLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (_lyricsAlbum is null || _lyricsTrack is null)
        {
            MessageBox.Show(this, "先に曲を選択してください。", "歌詞を保存", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var path = GetSavedLyricsPath(_lyricsAlbum, _lyricsTrack);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var lyricsToSave = _lyricsDocument.HasTiming
                && string.Equals(LyricsTextBox.Text, _lyricsDisplaySnapshot, StringComparison.Ordinal)
                ? _lyricsRawText.Trim()
                : LyricsTextBox.Text.Trim();
            File.WriteAllText(path, lyricsToSave, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var blank = string.IsNullOrWhiteSpace(LyricsTextBox.Text);
            LyricsStatusText.Text = blank
                ? $"空欄として保存済み: {_lyricsTrack.Title}"
                : $"アプリへ保存済み: {_lyricsTrack.Title}";
            StatusText.Text = blank
                ? "歌詞を空欄として保存しました（元ファイルは変更していません）"
                : "歌詞を保存しました（元ファイルは変更していません）";
            _lyricsTrack.HasLyrics = !blank;
            TrackGrid.Items.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "歌詞を保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LyricsTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (LyricsEmptyText is not null)
            LyricsEmptyText.Visibility = string.IsNullOrWhiteSpace(LyricsTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!_loadingLyrics && _lyricsTrack is not null)
        {
            var enteredText = LyricsTextBox.Text;
            var parsed = LyricsDocument.Parse(enteredText);
            _lyricsRawText = enteredText;
            _lyricsDocument = parsed;
            _lyricsDisplaySnapshot = parsed.DisplayText;
            _lastAutoLyricsLine = -1;
            HideLyricsCurrentLineUnderline();
            if (parsed.HasTiming && !string.Equals(enteredText, parsed.DisplayText, StringComparison.Ordinal))
            {
                _loadingLyrics = true;
                try
                {
                    LyricsTextBox.Text = parsed.DisplayText;
                    LyricsTextBox.Select(0, 0);
                }
                finally { _loadingLyrics = false; }
            }
            if (LyricsStatusText is not null) LyricsStatusText.Text = $"未保存の変更: {_lyricsTrack.Title}";
        }
    }

    private void SetLyricsContent(string text)
    {
        var wasLoading = _loadingLyrics;
        _loadingLyrics = true;
        try
        {
            _lyricsRawText = text;
            _lyricsDocument = LyricsDocument.Parse(text);
            _lyricsDisplaySnapshot = _lyricsDocument.DisplayText;
            _lastAutoLyricsLine = -1;
            LyricsTextBox.Text = _lyricsDisplaySnapshot;
            LyricsTextBox.Select(0, 0);
            HideLyricsCurrentLineUnderline();
            LyricsTextBox.ScrollToHome();
        }
        finally
        {
            _loadingLyrics = wasLoading;
        }
    }

    private void LyricsAutoScroll_Changed(object sender, RoutedEventArgs e)
    {
        _lastAutoLyricsLine = -1;
        if (LyricsAutoScrollCheck.IsChecked != true && LyricsTextBox is not null)
            HideLyricsCurrentLineUnderline();
        if (IsLoaded) SaveSettings();
    }

    private void UpdateLyricsAutoScroll(double positionSeconds, double durationSeconds)
    {
        if (LyricsAutoScrollCheck.IsChecked != true || _lyricsTrack is null || _playingAlbum is null
            || _currentIndex < 0 || _currentIndex >= _playingAlbum.Tracks.Count
            || !ReferenceEquals(_lyricsTrack, _playingAlbum.Tracks[_currentIndex])) return;
        if (!_lyricsDocument.HasTiming) HideLyricsCurrentLineUnderline();
        var line = _lyricsDocument.GetLineAt(positionSeconds, durationSeconds);
        if (line < 0)
        {
            if (_lastAutoLyricsLine >= 0 && _lyricsDocument.HasTiming)
                HideLyricsCurrentLineUnderline();
            _lastAutoLyricsLine = -1;
            return;
        }
        if (line == _lastAutoLyricsLine) return;
        _lastAutoLyricsLine = line;
        ScrollLyricsLineIntoView(line);
        if (_lyricsDocument.HasTiming)
            UpdateLyricsCurrentLineUnderline(line);
    }

    private void ScrollLyricsLineIntoView(int line)
    {
        var (start, _) = _lyricsDocument.GetTextSpan(line);
        if (start < 0 || start > LyricsTextBox.Text.Length) return;

        // TextBox.ScrollToLine expects a visual line index. LRC line indices are logical
        // lines, so wrapped lyrics need to be converted through their character position.
        LyricsTextBox.UpdateLayout();
        var visualLine = LyricsTextBox.GetLineIndexFromCharacterIndex(start);
        if (visualLine < 0) return;
        LyricsTextBox.ScrollToLine(Math.Max(0, visualLine - 2));

        // Only timestamped LRC lyrics have an exact current line. Plain lyrics may still
        // scroll approximately, but must not show a misleading current-line underline.
        if (_lyricsDocument.HasTiming)
            LyricsTextBox.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() =>
                {
                    if (_lyricsDocument.HasTiming) UpdateLyricsCurrentLineUnderline(line);
                    else HideLyricsCurrentLineUnderline();
                }));
        else
            HideLyricsCurrentLineUnderline();
    }

    private void LyricsTextBox_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (_lyricsDocument.HasTiming && _lastAutoLyricsLine >= 0)
            UpdateLyricsCurrentLineUnderline(_lastAutoLyricsLine);
    }

    private void UpdateLyricsCurrentLineUnderline(int line)
    {
        if (LyricsCurrentLineUnderline is null || LyricsTextBox is null) return;
        var (start, length) = _lyricsDocument.GetTextSpan(line);
        if (start < 0 || length <= 0)
        {
            HideLyricsCurrentLineUnderline();
            return;
        }

        var startRect = LyricsTextBox.GetRectFromCharacterIndex(start, trailingEdge: false);
        var endRect = LyricsTextBox.GetRectFromCharacterIndex(Math.Min(LyricsTextBox.Text.Length, start + length), trailingEdge: true);
        if (startRect.IsEmpty || endRect.IsEmpty || startRect.Bottom < 0 || startRect.Top > LyricsTextBox.ActualHeight)
        {
            HideLyricsCurrentLineUnderline();
            return;
        }

        var left = Math.Max(0, startRect.Left);
        var sameVisualLine = Math.Abs(startRect.Top - endRect.Top) < Math.Max(2, startRect.Height * 0.5);
        var width = sameVisualLine ? endRect.Right - left : LyricsTextBox.ActualWidth - left - 16;
        LyricsCurrentLineUnderline.Width = Math.Max(24, width);
        System.Windows.Controls.Canvas.SetLeft(LyricsCurrentLineUnderline, left);
        System.Windows.Controls.Canvas.SetTop(LyricsCurrentLineUnderline,
            Math.Clamp(startRect.Bottom + 1, 0, Math.Max(0, LyricsTextBox.ActualHeight - LyricsCurrentLineUnderline.Height)));
        LyricsCurrentLineUnderline.Visibility = Visibility.Visible;
    }

    private void HideLyricsCurrentLineUnderline()
    {
        if (LyricsCurrentLineUnderline is not null)
            LyricsCurrentLineUnderline.Visibility = Visibility.Collapsed;
    }

    private void LyricsOcrMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private async void OcrCurrentAlbumImage_Click(object sender, RoutedEventArgs e)
    {
        if (_albumImageIndex < 0 || _albumImageIndex >= _albumImages.Count)
        {
            MessageBox.Show(this, "先にOCRするアルバム画像を表示してください。", "歌詞OCR",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { await RunLyricsOcrAsync(LoadBitmap(_albumImages[_albumImageIndex], 0), "表示中のアルバム画像"); }
        catch (Exception ex) { ShowLyricsOcrError(ex); }
    }

    private async void OcrClipboardImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
            {
                MessageBox.Show(this, "クリップボードに画像がありません。\nSnipping Toolで範囲を切り取ってからお試しください。",
                    "歌詞OCR", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await RunLyricsOcrAsync(image, "クリップボード画像");
        }
        catch (Exception ex) { ShowLyricsOcrError(ex); }
    }

    private async void OcrImageFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "OCRする歌詞画像を選択",
            Filter = "画像ファイル (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
        };
        if (dialog.ShowDialog(this) != true) return;
        try { await RunLyricsOcrAsync(LoadBitmap(dialog.FileName, 0), Path.GetFileName(dialog.FileName)); }
        catch (Exception ex) { ShowLyricsOcrError(ex); }
    }

    private async Task RunLyricsOcrAsync(BitmapSource image, string sourceName)
    {
        if (_lyricsTrack is null)
        {
            MessageBox.Show(this, "歌詞を登録する曲を曲目リストで選択してください。", "歌詞OCR",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        LyricsOcrButton.IsEnabled = false;
        LyricsStatusText.Text = $"OCR処理中: {sourceName}";
        try
        {
            var result = await LyricsOcrService.RecognizeJapaneseAsync(image, verticalLayout: false);
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                MessageBox.Show(this, "文字を認識できませんでした。\n文字部分だけを大きく切り取り、傾きや文字方向を確認してください。",
                    "歌詞OCR", MessageBoxButton.OK, MessageBoxImage.Information);
                LyricsStatusText.Text = "OCRで文字を認識できませんでした";
                return;
            }

            var replacement = true;
            if (!string.IsNullOrWhiteSpace(LyricsTextBox.Text))
            {
                var choice = MessageBox.Show(this,
                    "歌詞欄にはすでに文字があります。\n\n［はい］現在の内容をOCR結果で置換\n［いいえ］現在の内容の末尾へ追加\n［キャンセル］取り込みを中止",
                    "OCR結果の取り込み方法", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) { LyricsStatusText.Text = "OCR結果の取り込みを中止しました"; return; }
                replacement = choice == MessageBoxResult.Yes;
            }
            LyricsTextBox.Text = replacement || string.IsNullOrWhiteSpace(LyricsTextBox.Text)
                ? result.Text
                : LyricsTextBox.Text.TrimEnd() + Environment.NewLine + Environment.NewLine + result.Text;
            LyricsTextBox.CaretIndex = LyricsTextBox.Text.Length;
            LyricsTextBox.ScrollToEnd();
            LyricsTextBox.Focus();
            LyricsStatusText.Text = $"OCR結果（未保存）: {sourceName}・{result.Language}・{result.LineCount}行";
            StatusText.Text = $"歌詞OCRが完了しました: {_lyricsTrack.Title}";
        }
        finally { LyricsOcrButton.IsEnabled = true; }
    }

    private void ShowLyricsOcrError(Exception ex)
    {
        LyricsStatusText.Text = "歌詞OCRを実行できませんでした";
        MessageBox.Show(this, ex.Message, "歌詞OCRエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void GoogleLyricsSearch_Click(object sender, RoutedEventArgs e)
    {
        var track = _lyricsTrack ?? (_playingAlbum is not null && _currentIndex >= 0 && _currentIndex < _playingAlbum.Tracks.Count
            ? _playingAlbum.Tracks[_currentIndex] : TrackGrid.SelectedItem as ZipTrack);
        if (track is null)
        {
            MessageBox.Show(this, "歌詞を検索する曲を選択してください。", "Google歌詞検索",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var albumFallback = _lyricsAlbum is null ? null : _albums.FirstOrDefault(item =>
                string.Equals(item.Album.Path, _lyricsAlbum.Path, StringComparison.OrdinalIgnoreCase))?.Title;
            var query = BuildGoogleLyricsQuery(track, albumFallback);
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            StatusText.Text = $"Google歌詞検索を開きました: {track.Title} / {(IsUsefulSearchTerm(track.Album) ? track.Album : albumFallback)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Google歌詞検索を開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string BuildGoogleLyricsQuery(ZipTrack track, string? albumFallback)
    {
        var artist = IsUsefulSearchTerm(track.Artist) ? track.Artist : null;
        var title = IsUsefulSearchTerm(track.Title) ? track.Title : track.FileName;
        var album = IsUsefulSearchTerm(track.Album) ? track.Album : albumFallback;
        return string.Join(' ', new[] { artist, title, album, "歌詞" }
            .Where(IsUsefulSearchTerm).Select(value => value!.Trim()));
    }

    private static bool IsUsefulSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        return !normalized.Equals("アーティスト不明", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("アルバム不明", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadAlbumImages(ZipAlbum album)
    {
        _albumImages.Clear();
        _currentArtworkRoles = LoadArtworkRoles(album.Path);
        _currentArtworkRotations = LoadArtworkRotations(album.Path);
        _currentTrayColor = LoadTrayColor(album.Path);
        UpdateTrayColorCombo();
        _albumImageIndex = -1;
        _selectedAlbumImageIndex = -1;
        try
        {
            _albumImages.AddRange(album.Images
                .OrderBy(image => GetAlbumImagePriority(image.FileName))
                .ThenBy(image => GetAlbumImageSequence(image.FileName))
                .ThenBy(image => image.FileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(image => WithArtworkRotation(new AlbumImageSource(image.FileName, null, image), _currentArtworkRotations)));

            var downloadedDirectory = GetDownloadedArtworkDirectory(album.Path);
            if (Directory.Exists(downloadedDirectory))
            {
                _albumImages.AddRange(Directory.EnumerateFiles(downloadedDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsAlbumImage)
                    .OrderBy(GetAlbumImagePriority)
                    .ThenBy(GetAlbumImageSequence)
                    .ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(path => WithArtworkRotation(new AlbumImageSource(GetManagedArtworkDisplayName(path), path, null), _currentArtworkRotations)));
            }

            _albumImages.AddRange(EnumerateExternalAlbumImagePaths(album)
                .Select(path => WithArtworkRotation(new AlbumImageSource(Path.GetFileName(path), path, null), _currentArtworkRotations)));

            UpdateTrayColorCombo();
            if (_albumImages.Count > 0) { _albumImageIndex = 0; _selectedAlbumImageIndex = 0; ShowCurrentAlbumImage(); }
            else ClearAlbumImages();
        }
        catch { ClearAlbumImages(); }
    }

    private static bool IsAlbumImage(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> EnumerateExternalAlbumImagePaths(ZipAlbum album)
    {
        try
        {
            var isFolderAlbum = Directory.Exists(album.Path);
            var sidecarDirectory = !isFolderAlbum && album.Path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
                ? album.Path[..^4] : null;
            var hasSidecarDirectory = sidecarDirectory is not null && Directory.Exists(sidecarDirectory);
            var directory = isFolderAlbum ? album.Path : hasSidecarDirectory ? sidecarDirectory : Path.GetDirectoryName(album.Path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];

            var imageSearch = isFolderAlbum || hasSidecarDirectory ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var images = Directory.EnumerateFiles(directory, "*", imageSearch).Where(IsAlbumImage).ToList();
            if (!isFolderAlbum && !hasSidecarDirectory)
            {
                var fileName = Path.GetFileName(album.Path);
                var baseName = fileName.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^8] : Path.GetFileNameWithoutExtension(fileName);
                var archiveCount = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Count(ZipAlbumReader.IsSupportedArchivePath);
                if (archiveCount > 1)
                    images = images.Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(baseName, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            return images.OrderBy(GetAlbumImagePriority)
                .ThenBy(GetAlbumImageSequence)
                .ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static int GetAlbumImagePriority(string path)
    {
        string[] preferred = ["cover", "folder", "front", "jacket", "album", "albumart"];
        var name = Path.GetFileNameWithoutExtension(path);
        var index = Array.FindIndex(preferred, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private static int GetAlbumImageSequence(string path)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var sequence) ? sequence : int.MaxValue;
    }

    private static string GetManagedArtworkDisplayName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("clipboard-", StringComparison.OrdinalIgnoreCase)) return $"貼り付け画像: {name}";
        if (name.StartsWith("manual-", StringComparison.OrdinalIgnoreCase)) return $"手動追加: {name}";
        return $"オンライン取得: {name}";
    }

    private async void DownloadAlbumArtwork_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem() ?? (_album is null ? null : _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase)));
        if (item is null)
        {
            MessageBox.Show(this, "先にアルバムを選択してください。", "アルバム画像", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ArtworkSearchButton.IsEnabled = false;
        try
        {
            var lookup = new ArtworkLookupWindow(item.Title, item.Artist) { Owner = this };
            if (lookup.ShowDialog() != true || lookup.SelectedCandidate is not { } selected) return;

            var directory = GetDownloadedArtworkDirectory(item.Album.Path);
            Directory.CreateDirectory(directory);
            foreach (var oldFile in Directory.EnumerateFiles(directory, "online-cover.*", SearchOption.TopDirectoryOnly))
                File.Delete(oldFile);
            var destination = Path.Combine(directory, "online-cover" + selected.Extension);
            await File.WriteAllBytesAsync(destination, selected.ImageBytes);

            item.RefreshImageCount();
            if (_album is not null && string.Equals(_album.Path, item.Album.Path, StringComparison.OrdinalIgnoreCase))
                LoadAlbumImages(item.Album);
            RemoveAlbumFromArtistTree(item);
            AddAlbumToArtistTree(item);
            StatusText.Text = $"アルバム画像を保存しました: {selected.Album}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像を保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ArtworkSearchButton.IsEnabled = true;
        }
    }

    private void GoogleImageSearch_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem() ?? (_album is null ? null : _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase)));
        if (item is null)
        {
            MessageBox.Show(this, "先にアルバムを選択してください。", "Google画像検索", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var artist = item.Artist == "アーティスト不明" ? "" : item.Artist;
            var query = string.Join(' ', new[] { artist, item.Title, "album cover" }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var url = "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(query);
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            StatusText.Text = $"Google画像検索を開きました: {item.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像検索を開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportAlbumArtwork_Click(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedAlbumItem() ?? (_album is null ? null : _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase)));
        if (item is null)
        {
            MessageBox.Show(this, "先にアルバムを選択してください。", "画像を追加", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"「{item.Title}」へ追加する画像を選択",
            Filter = "画像ファイル (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var directory = GetDownloadedArtworkDirectory(item.Album.Path);
            Directory.CreateDirectory(directory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var imported = 0;
            foreach (var source in dialog.FileNames)
            {
                _ = LoadBitmap(source, 160);
                var extension = Path.GetExtension(source).ToLowerInvariant();
                var destination = Path.Combine(directory, $"manual-{stamp}-{imported + 1:00}{extension}");
                File.Copy(source, destination, overwrite: false);
                imported++;
            }
            item.RefreshImageCount();
            if (_album is not null && string.Equals(_album.Path, item.Album.Path, StringComparison.OrdinalIgnoreCase))
                LoadAlbumImages(item.Album);
            RemoveAlbumFromArtistTree(item);
            AddAlbumToArtistTree(item);
            StatusText.Text = $"選択した画像を{imported}枚追加しました（元アルバムは変更していません）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像を追加できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool PasteAlbumArtworkFromClipboard()
    {
        var item = GetSelectedAlbumItem() ?? (_album is null ? null : _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase)));
        if (item is null)
        {
            MessageBox.Show(this, "貼り付け先のアルバムを選択してください。", "画像を貼り付け",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        try
        {
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
            {
                MessageBox.Show(this, "クリップボードに画像がありません。\nSnipping Toolで範囲を切り取ってから、もう一度お試しください。",
                    "画像を貼り付け", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            var directory = GetDownloadedArtworkDirectory(item.Album.Path);
            var destination = ClipboardArtworkStorage.SavePng(image, directory);
            item.RefreshImageCount();
            if (_album is not null && string.Equals(_album.Path, item.Album.Path, StringComparison.OrdinalIgnoreCase))
            {
                LoadAlbumImages(item.Album);
                _albumImageIndex = _albumImages.FindIndex(source => string.Equals(source.FilePath, destination, StringComparison.OrdinalIgnoreCase));
                if (_albumImageIndex >= 0) ShowCurrentAlbumImage();
            }
            RemoveAlbumFromArtistTree(item);
            AddAlbumToArtistTree(item);
            StatusText.Text = $"クリップボード画像を追加しました: {item.Title}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像を貼り付けできません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static string GetDownloadedArtworkDirectory(string albumPath)
    {
        var normalized = Path.GetFullPath(albumPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20];
        return Path.Combine(DataDirectory, "artwork", key);
    }

    private static string GetArtworkRolesPath(string albumPath) =>
        Path.Combine(GetDownloadedArtworkDirectory(albumPath), "artwork-roles.json");

    private static string GetArtworkRotationsPath(string albumPath) =>
        Path.Combine(GetDownloadedArtworkDirectory(albumPath), "artwork-rotations.json");

    private static string GetSpineCardFoldsPath(string albumPath) =>
        Path.Combine(GetDownloadedArtworkDirectory(albumPath), "spine-card-folds.json");

    private static string GetCaseAppearancePath(string albumPath) =>
        Path.Combine(GetDownloadedArtworkDirectory(albumPath), "case-appearance.json");

    private static string LoadTrayColor(string albumPath)
    {
        try
        {
            var path = GetCaseAppearancePath(albumPath);
            if (!File.Exists(path)) return "Auto";
            return JsonSerializer.Deserialize<CaseAppearanceSettings>(File.ReadAllText(path))?.TrayColor ?? "Auto";
        }
        catch { return "Auto"; }
    }

    private static void SaveTrayColor(string albumPath, string trayColor)
    {
        var path = GetCaseAppearancePath(albumPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(
            new CaseAppearanceSettings { TrayColor = trayColor },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool HasInlayArtwork(IEnumerable<AlbumImageSource> sources, IReadOnlyDictionary<string, string> roles) =>
        sources.Any(source => string.Equals(GetEffectiveArtworkRole(source, roles), "Inlay", StringComparison.OrdinalIgnoreCase));

    private static List<AlbumImageSource> GetCaseArtworkSources(ZipAlbum album, string downloadedDirectory)
    {
        var rotations = LoadArtworkRotations(album.Path);
        var sources = new List<AlbumImageSource>();
        if (Directory.Exists(downloadedDirectory))
            sources.AddRange(Directory.EnumerateFiles(downloadedDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsAlbumImage).OrderBy(GetAlbumImagePriority).ThenBy(GetAlbumImageSequence)
                .ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(path => WithArtworkRotation(new AlbumImageSource(GetManagedArtworkDisplayName(path), path, null), rotations)));
        sources.AddRange(album.Images.OrderBy(image => GetAlbumImagePriority(image.FileName))
            .ThenBy(image => GetAlbumImageSequence(image.FileName))
            .ThenBy(image => image.FileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(image => WithArtworkRotation(new AlbumImageSource(image.FileName, null, image), rotations)));
        sources.AddRange(EnumerateExternalAlbumImagePaths(album)
            .Select(path => WithArtworkRotation(new AlbumImageSource(Path.GetFileName(path), path, null), rotations)));
        return sources;
    }

    private static int NormalizeArtworkRotation(int degrees)
    {
        degrees %= 360;
        if (degrees < 0) degrees += 360;
        return degrees / 90 * 90;
    }

    private static AlbumImageSource WithArtworkRotation(AlbumImageSource source, IReadOnlyDictionary<string, int> rotations) =>
        source with { RotationDegrees = rotations.TryGetValue(source.RoleKey, out var value) ? NormalizeArtworkRotation(value) : 0 };

    private static Dictionary<string, int> LoadArtworkRotations(string albumPath)
    {
        try
        {
            var path = GetArtworkRotationsPath(albumPath);
            if (!File.Exists(path)) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var stored = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? [];
            return new Dictionary<string, int>(stored.ToDictionary(pair => pair.Key,
                pair => NormalizeArtworkRotation(pair.Value)), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveArtworkRotations(string albumPath, Dictionary<string, int> rotations)
    {
        var path = GetArtworkRotationsPath(albumPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rotations, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, SpineCardFoldSetting> LoadSpineCardFolds(string albumPath)
    {
        try
        {
            var path = GetSpineCardFoldsPath(albumPath);
            if (!File.Exists(path)) return new Dictionary<string, SpineCardFoldSetting>(StringComparer.OrdinalIgnoreCase);
            var stored = JsonSerializer.Deserialize<Dictionary<string, SpineCardFoldSetting>>(File.ReadAllText(path)) ?? [];
            return new Dictionary<string, SpineCardFoldSetting>(stored.Where(pair => pair.Value.Left is > 0 and < 1
                    && pair.Value.Right > pair.Value.Left && pair.Value.Right < 1)
                .ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, SpineCardFoldSetting>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveSpineCardFolds(string albumPath, Dictionary<string, SpineCardFoldSetting> folds)
    {
        var path = GetSpineCardFoldsPath(albumPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(folds, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static BitmapSource ApplySpineCardFoldOverride(string albumPath, AlbumImageSource source, BitmapSource bitmap)
    {
        var folds = LoadSpineCardFolds(albumPath);
        if (folds.TryGetValue(source.RoleKey, out var fold))
            SpineCardArtwork.SetManualFolds(bitmap, fold.Left, fold.Right);
        return bitmap;
    }

    private sealed class CaseAppearanceSettings
    {
        public string TrayColor { get; set; } = "Auto";
    }

    private static Dictionary<string, string> LoadArtworkRoles(string albumPath)
    {
        try
        {
            var path = GetArtworkRolesPath(albumPath);
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
            return new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private static void SaveArtworkRoles(string albumPath, Dictionary<string, string> roles)
    {
        var path = GetArtworkRolesPath(albumPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(roles, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetEffectiveArtworkRole(AlbumImageSource source, IReadOnlyDictionary<string, string> roles)
    {
        if (roles.TryGetValue(source.RoleKey, out var stored) && !string.Equals(stored, "Auto", StringComparison.OrdinalIgnoreCase))
            return stored;
        var name = Path.GetFileNameWithoutExtension(source.RoleHint).ToLowerInvariant();
        // An obi is a separate paper band, not one of the case's printed spines.
        if (Regex.IsMatch(name, @"spine[\s_-]*card|(?:^|[\s_-])obi(?:$|[\s_\d-])")
            || name.Contains("帯", StringComparison.Ordinal)) return "SpineCard";
        if (Regex.IsMatch(name, @"liner[\s_-]*notes?")
            || name.Contains("ライナーノー", StringComparison.Ordinal)) return "LinerNotes";
        if (Regex.IsMatch(name, @"(?:^|[\s_\-])pages?(?:$|[\s_\-\d])") || name.Contains("冊子ページ", StringComparison.Ordinal)) return "Page";
        if (Regex.IsMatch(name, @"(?:^|[\s_-])flyers?(?:$|[\s_\d-])")
            || name.Contains("フライヤー", StringComparison.Ordinal) || name.Contains("チラシ", StringComparison.Ordinal)) return "Flyer";
        if (Regex.IsMatch(name, @"(?:^|[\s_-])posters?(?:$|[\s_\d-])")
            || name.Contains("ポスター", StringComparison.Ordinal)) return "Poster";
        if (name.Contains("spine", StringComparison.Ordinal) || name.Contains("背表紙", StringComparison.Ordinal)) return "Spine";
        if (name.Contains("inlay", StringComparison.Ordinal) || name.Contains("inray", StringComparison.Ordinal)
            || name.Contains("tray", StringComparison.Ordinal) || name.Contains("インレイ", StringComparison.Ordinal)) return "Inlay";
        if (Regex.IsMatch(name, @"(?:2[\s_-]*discs?|discs?[\s_-]*2|two[\s_-]*discs?)")
            || name.Contains("2枚", StringComparison.Ordinal)) return "Disc2";
        if (name.Contains("disc", StringComparison.Ordinal) || name.Contains("disk", StringComparison.Ordinal)
            || name.Contains("cd_label", StringComparison.Ordinal) || name.Contains("レーベル", StringComparison.Ordinal)) return "Disc";
        if (name.Contains("cover_back", StringComparison.Ordinal) || name.Contains("cover-back", StringComparison.Ordinal)
            || name.Contains("back", StringComparison.Ordinal) || name.Contains("rear", StringComparison.Ordinal)
            || name.Contains("裏表紙", StringComparison.Ordinal)) return "Back";
        if (name.Contains("front", StringComparison.Ordinal) || name.Contains("cover", StringComparison.Ordinal)
            || name.Contains("folder", StringComparison.Ordinal) || name.Contains("jacket", StringComparison.Ordinal)
            || name.Contains("ジャケット", StringComparison.Ordinal)) return "Front";
        return "Other";
    }

    private void UpdateArtworkRoleCombo()
    {
        if (ArtworkRoleCombo is null) return;
        _updatingArtworkRoleCombo = true;
        try
        {
            var selectedRole = _selectedAlbumImageIndex >= 0 && _selectedAlbumImageIndex < _albumImages.Count
                && _currentArtworkRoles.TryGetValue(_albumImages[_selectedAlbumImageIndex].RoleKey, out var stored)
                ? stored : "Auto";
            ArtworkRoleCombo.SelectedItem = ArtworkRoleCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedRole, StringComparison.OrdinalIgnoreCase))
                ?? ArtworkRoleCombo.Items[0];
            ArtworkRoleCombo.IsEnabled = _selectedAlbumImageIndex >= 0 && _selectedAlbumImageIndex < _albumImages.Count;
            if (AdjustSpineCardFoldsButton is not null)
            {
                var showSpineAdjustment = _album is not null
                    && _selectedAlbumImageIndex >= 0 && _selectedAlbumImageIndex < _albumImages.Count
                    && string.Equals(GetEffectiveArtworkRole(_albumImages[_selectedAlbumImageIndex], _currentArtworkRoles),
                        "SpineCard", StringComparison.OrdinalIgnoreCase);
                AdjustSpineCardFoldsButton.Visibility = showSpineAdjustment
                    ? Visibility.Visible : Visibility.Collapsed;
                AdjustSpineCardFoldsButton.IsEnabled = showSpineAdjustment;
            }
        }
        finally { _updatingArtworkRoleCombo = false; }
    }

    private void ArtworkRole_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingArtworkRoleCombo || _album is null || _selectedAlbumImageIndex < 0
            || _selectedAlbumImageIndex >= _albumImages.Count
            || ArtworkRoleCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem selected) return;
        var source = _albumImages[_selectedAlbumImageIndex];
        var role = selected.Tag?.ToString() ?? "Auto";
        if (role == "Auto") _currentArtworkRoles.Remove(source.RoleKey);
        else _currentArtworkRoles[source.RoleKey] = role;
        try
        {
            SaveArtworkRoles(_album.Path, _currentArtworkRoles);
            UpdateArtworkRoleCombo();
            UpdateTrayColorCombo();
            var item = _albums.FirstOrDefault(candidate =>
                string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase));
            item?.RefreshImageCount();
            QueueCoverFlowRefresh();
            StatusText.Text = LocalizationService.Select(
                $"画像の用途を{role}に設定しました: {source.DisplayName}",
                $"Artwork role set to {role}: {source.DisplayName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像の用途を保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AdjustSpineCardFolds_Click(object sender, RoutedEventArgs e)
    {
        if (_album is null || _selectedAlbumImageIndex < 0 || _selectedAlbumImageIndex >= _albumImages.Count) return;
        var source = _albumImages[_selectedAlbumImageIndex];
        if (!string.Equals(GetEffectiveArtworkRole(source, _currentArtworkRoles), "SpineCard", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var bitmap = LoadBitmap(source, 3000);
            var folds = LoadSpineCardFolds(_album.Path);
            var hasManual = folds.TryGetValue(source.RoleKey, out var manual);
            var automatic = SpineCardArtwork.GetRegions(bitmap);
            var left = hasManual ? manual!.Left : automatic.Spine.X / (double)bitmap.PixelWidth;
            var right = hasManual ? manual!.Right : automatic.Front.X / (double)bitmap.PixelWidth;
            var dialog = new SpineCardFoldEditorWindow(bitmap, left, right, hasManual) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (dialog.UseAutomatic) folds.Remove(source.RoleKey);
            else folds[source.RoleKey] = new SpineCardFoldSetting { Left = dialog.LeftFold, Right = dialog.RightFold };
            SaveSpineCardFolds(_album.Path, folds);
            var item = _albums.FirstOrDefault(candidate =>
                string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase));
            item?.RefreshImageCount();
            item?.EnsureCaseArtworkLoaded(1200);
            QueueCoverFlowRefresh();
            StatusText.Text = dialog.UseAutomatic
                ? LocalizationService.Select("Spine Cardの折り目を自動検出へ戻しました", "Spine Card folds returned to automatic detection")
                : LocalizationService.Select("Spine Cardの折り目を保存しました", "Spine Card folds saved");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Select("折り目を保存できません", "Could Not Save Folds"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateTrayColorCombo()
    {
        if (TrayColorCombo is null) return;
        _updatingTrayColorCombo = true;
        try
        {
            // Preserve the saved preference, but expose the Inlay whenever present.
            var hasInlay = HasInlayArtwork(_albumImages, _currentArtworkRoles);
            var displayedColor = hasInlay ? "Clear" : _currentTrayColor;
            TrayColorCombo.SelectedItem = TrayColorCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), displayedColor,
                    StringComparison.OrdinalIgnoreCase)) ?? TrayColorCombo.Items[0];
            TrayColorCombo.IsEnabled = _album is not null && !hasInlay;
            TrayColorCombo.ToolTip = hasInlay
                ? LocalizationService.Select("Inlay画像があるため、自動的に透明トレイを使用します", "A clear tray is selected automatically while Inlay artwork is present")
                : LocalizationService.Select("3D CDケースのインナートレイ色", "Inner tray color of the 3D CD case");
        }
        finally { _updatingTrayColorCombo = false; }
    }

    private void TrayColor_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingTrayColorCombo || _album is null
            || TrayColorCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem selected) return;
        if (HasInlayArtwork(_albumImages, _currentArtworkRoles)) { UpdateTrayColorCombo(); return; }
        var color = selected.Tag?.ToString() ?? "Auto";
        try
        {
            SaveTrayColor(_album.Path, color);
            _currentTrayColor = color;
            var item = _albums.FirstOrDefault(candidate =>
                string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase));
            item?.RefreshImageCount();
            QueueCoverFlowRefresh();
            StatusText.Text = LocalizationService.Select(
                $"インナートレイの色を{selected.Content}に設定しました",
                $"Inner tray color set to {selected.Content}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                LocalizationService.Select("トレイ色を保存できません", "Could Not Save Tray Color"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowCurrentAlbumImage()
    {
        if (_albumImageIndex < 0 || _albumImageIndex >= _albumImages.Count) { ClearAlbumImages(); return; }
        try
        {
            var firstBitmap = LoadBitmap(_albumImages[_albumImageIndex], 900);
            var (visibleCount, horizontal) = CalculateAlbumImageLayout(firstBitmap);
            _albumImagePageSize = visibleCount;
            _visibleAlbumImageCount = Math.Min(_albumImagePageSize, _albumImages.Count - _albumImageIndex);
            AlbumImageGallery.Rows = horizontal ? 1 : _visibleAlbumImageCount;
            AlbumImageGallery.Columns = horizontal ? _visibleAlbumImageCount : 1;
            AlbumImageGallery.Children.Clear();

            var displayedIndices = new List<int>();
            if (_selectedAlbumImageIndex < _albumImageIndex
                || _selectedAlbumImageIndex >= _albumImageIndex + _visibleAlbumImageCount)
                _selectedAlbumImageIndex = _albumImageIndex;
            for (var offset = 0; offset < _visibleAlbumImageCount; offset++)
            {
                var imageIndex = _albumImageIndex + offset;
                var source = _albumImages[imageIndex];
                var image = new System.Windows.Controls.Image
                {
                    Source = offset == 0 ? firstBitmap : LoadBitmap(source, 900),
                    Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(2),
                    Cursor = System.Windows.Input.Cursors.Hand, Tag = imageIndex
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
                image.MouseLeftButtonDown += AlbumImage_DoubleClick;
                var frame = new System.Windows.Controls.Border
                {
                    Child = image, Tag = imageIndex, Margin = new Thickness(2),
                    BorderBrush = imageIndex == _selectedAlbumImageIndex
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 169, 229))
                        : System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(imageIndex == _selectedAlbumImageIndex ? 2 : 1),
                    ToolTip = $"{source.DisplayName}\nクリックで選択／ダブルクリックで拡大表示"
                };
                AlbumImageGallery.Children.Add(frame);
                displayedIndices.Add(imageIndex);
            }

            AlbumImageEmptyText.Visibility = Visibility.Collapsed;
            AlbumImageCountText.Text = displayedIndices.Count == 1
                ? $"{displayedIndices[0] + 1} / {_albumImages.Count}"
                : $"{displayedIndices[0] + 1}–{displayedIndices[^1] + 1} / {_albumImages.Count}";
            AlbumImageNameText.Text = string.Join("  /  ", displayedIndices.Select(index => _albumImages[index].DisplayName));
            AlbumImageNameText.ToolTip = string.Join("\n\n", displayedIndices.Select(index => _albumImages[index].Description));
            PreviousImageButton.IsEnabled = _albumImages.Count > _albumImagePageSize;
            NextImageButton.IsEnabled = _albumImages.Count > _albumImagePageSize;
            UpdateDeleteAlbumImageButton();
            UpdateArtworkRoleCombo();
            UpdateArtworkRotationButtons();
        }
        catch
        {
            AlbumImageGallery.Children.Clear();
            AlbumImageEmptyText.Text = "画像を開けません";
            AlbumImageEmptyText.Visibility = Visibility.Visible;
        }
    }

    private (int Count, bool Horizontal) CalculateAlbumImageLayout(BitmapSource bitmap)
    {
        var width = Math.Max(1, AlbumImageViewport.ActualWidth - 10);
        var height = Math.Max(1, AlbumImageViewport.ActualHeight - 10);
        if (width < 80 || height < 80 || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return (1, true);

        var aspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
        var renderedWidth = Math.Min(width, height * aspect);
        var renderedHeight = renderedWidth / aspect;
        const double gap = 8;
        var horizontalCapacity = (int)Math.Floor((width + gap) / (renderedWidth + gap));
        var verticalCapacity = (int)Math.Floor((height + gap) / (renderedHeight + gap));
        var horizontal = horizontalCapacity >= verticalCapacity;
        var capacity = Math.Max(horizontalCapacity, verticalCapacity);
        return (Math.Clamp(capacity, 1, Math.Min(3, _albumImages.Count)), horizontal);
    }

    private void AlbumImageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_albumImages.Count == 0 || _imageLayoutUpdatePending) return;
        _imageLayoutUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _imageLayoutUpdatePending = false;
            if (_albumImages.Count > 0) ShowCurrentAlbumImage();
        }));
    }

    private static BitmapImage LoadBitmap(string path, int decodePixelWidth)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return LoadBitmap(stream, decodePixelWidth);
    }

    private static BitmapSource LoadBitmap(AlbumImageSource source, int decodePixelWidth)
    {
        BitmapImage bitmap;
        var quarterTurn = source.RotationDegrees is 90 or 270;
        if (source.FilePath is not null)
        {
            using var stream = new FileStream(source.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bitmap = LoadBitmap(stream, decodePixelWidth, quarterTurn);
            return RotateArtworkBitmap(bitmap, source.RotationDegrees);
        }
        if (source.ZipEntry is null) throw new InvalidDataException("画像データがありません。");
        using var bounded = new BoundedFileStream(source.ZipEntry.SourcePath, source.ZipEntry.DataOffset, source.ZipEntry.CompressedSize);
        if (source.ZipEntry.CompressionMethod == 0)
        {
            bitmap = LoadBitmap(bounded, decodePixelWidth, quarterTurn);
            return RotateArtworkBitmap(bitmap, source.RotationDegrees);
        }
        if (source.ZipEntry.CompressionMethod == 8)
        {
            using var deflate = new DeflateStream(bounded, CompressionMode.Decompress);
            using var memory = new MemoryStream(source.ZipEntry.UncompressedSize > 0 && source.ZipEntry.UncompressedSize <= int.MaxValue
                ? (int)source.ZipEntry.UncompressedSize : 0);
            deflate.CopyTo(memory);
            memory.Position = 0;
            bitmap = LoadBitmap(memory, decodePixelWidth, quarterTurn);
            return RotateArtworkBitmap(bitmap, source.RotationDegrees);
        }
        throw new NotSupportedException("この画像のZIP圧縮方式には対応していません。");
    }

    private static BitmapImage LoadBitmap(Stream stream, int decodePixelWidth, bool decodeByHeight = false)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
        {
            if (decodeByHeight) image.DecodePixelHeight = decodePixelWidth;
            else image.DecodePixelWidth = decodePixelWidth;
        }
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapSource RotateArtworkBitmap(BitmapSource source, int degrees)
    {
        degrees = NormalizeArtworkRotation(degrees);
        if (degrees == 0) return source;
        var rotated = new TransformedBitmap(source, new System.Windows.Media.RotateTransform(degrees));
        rotated.Freeze();
        return rotated;
    }

    private void ApplyImageLyricsRatio()
    {
        if (ArtworkColumn is null || LyricsColumn is null || ImageLyricsSplitterColumn is null
            || ImageLyricsSplitter is null || LyricsContent is null || LyricsPanelToggleButton is null) return;
        if (!_lyricsPanelExpanded)
        {
            ArtworkColumn.Width = new GridLength(1, GridUnitType.Star);
            LyricsColumn.MinWidth = 0;
            LyricsColumn.Width = new GridLength(0);
            // Keep only a narrow claw in the former divider, matching the
            // collapsible equalizer panel at the right edge of the window.
            ImageLyricsSplitterColumn.Width = new GridLength(24);
            ImageLyricsSplitter.Visibility = Visibility.Collapsed;
            LyricsContent.Visibility = Visibility.Collapsed;
            LyricsPanelToggleButton.Content = "◀";
            LyricsPanelToggleButton.ToolTip = LocalizationService.Select(
                "歌詞欄を表示して画像との分割表示に戻す", "Show lyrics and restore the split view");
            return;
        }
        var ratio = Math.Clamp(_settings.ImageLyricsRatio, 0.15, 0.85);
        LyricsColumn.MinWidth = 180;
        ArtworkColumn.Width = new GridLength(ratio, GridUnitType.Star);
        LyricsColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        ImageLyricsSplitterColumn.Width = new GridLength(8);
        ImageLyricsSplitter.Visibility = Visibility.Visible;
        LyricsContent.Visibility = Visibility.Visible;
        LyricsPanelToggleButton.Content = "▶";
        LyricsPanelToggleButton.ToolTip = LocalizationService.Select(
            "歌詞欄を折りたたんでアルバム画像を拡大", "Hide lyrics and enlarge the album artwork");
    }

    private void LyricsPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_lyricsPanelExpanded)
        {
            var total = ArtworkColumn.ActualWidth + LyricsColumn.ActualWidth;
            if (total > 0) _settings.ImageLyricsRatio = Math.Clamp(ArtworkColumn.ActualWidth / total, 0.15, 0.85);
        }
        _lyricsPanelExpanded = !_lyricsPanelExpanded;
        _settings.LyricsPanelExpanded = _lyricsPanelExpanded;
        ApplyImageLyricsRatio();
        if (IsLoaded) SaveSettings();
    }

    private void ImageLyricsSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (IsLoaded) SaveSettings();
    }

    private void ImagePanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (IsLoaded) SaveSettings();
    }

    private void ExtensionPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ExtensionPanelExpanded = ExtensionPanel.Visibility != Visibility.Visible;
        ApplyExtensionPanelState();
        if (IsLoaded) SaveSettings();
    }

    private void ApplyExtensionPanelState()
    {
        if (ExtensionPanel is null || ExtensionColumn is null || ExtensionSplitterColumn is null) return;
        if (_settings.ExtensionPanelExpanded)
        {
            ExtensionColumn.MinWidth = 240;
            ExtensionColumn.Width = new GridLength(Math.Clamp(_settings.ExtensionPanelWidth, 240, 600));
            ExtensionSplitterColumn.Width = new GridLength(8);
            ExtensionPanel.Visibility = Visibility.Visible;
            ExtensionPanelSplitter.Visibility = Visibility.Visible;
            ExtensionPanelToggleButton.Content = "▶";
            ExtensionPanelToggleButton.ToolTip = "右側の拡張機能を折りたたむ";
        }
        else
        {
            if (ExtensionColumn.ActualWidth >= 240) _settings.ExtensionPanelWidth = ExtensionColumn.ActualWidth;
            ExtensionColumn.MinWidth = 0;
            ExtensionColumn.Width = new GridLength(0);
            ExtensionSplitterColumn.Width = new GridLength(28);
            ExtensionPanel.Visibility = Visibility.Collapsed;
            ExtensionPanelSplitter.Visibility = Visibility.Collapsed;
            ExtensionPanelToggleButton.Content = "◀";
            ExtensionPanelToggleButton.ToolTip = "右側の拡張機能を開く";
        }
    }

    private void ApplyImagePanelLayout()
    {
        if (AlbumImagePanel is null || ImagePanelSplitter is null) return;
        if (_imagePanelLayout == ImagePanelLayout.Bottom)
        {
            var imageRatio = Math.Clamp(_settings.ImageBottomRatio, 0.30, 0.75);
            ImageRightColumn.MinWidth = 0;
            ImageRightColumn.Width = new GridLength(0);
            ImageRightSplitterColumn.Width = new GridLength(0);
            TrackContentRow.Height = new GridLength(1 - imageRatio, GridUnitType.Star);
            ImageBottomSplitterRow.Height = new GridLength(8);
            ImageBottomRow.Height = new GridLength(imageRatio, GridUnitType.Star);
            System.Windows.Controls.Grid.SetColumn(AlbumImagePanel, 2); System.Windows.Controls.Grid.SetRow(AlbumImagePanel, 2); System.Windows.Controls.Grid.SetRowSpan(AlbumImagePanel, 1);
            System.Windows.Controls.Grid.SetColumn(ImagePanelSplitter, 2); System.Windows.Controls.Grid.SetRow(ImagePanelSplitter, 1); System.Windows.Controls.Grid.SetRowSpan(ImagePanelSplitter, 1);
            ImagePanelSplitter.Width = double.NaN; ImagePanelSplitter.Height = 8;
            ImagePanelSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            ImagePanelSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            ImagePanelSplitter.ResizeDirection = System.Windows.Controls.GridResizeDirection.Rows;
        }
        else
        {
            TrackContentRow.Height = new GridLength(1, GridUnitType.Star);
            ImageBottomSplitterRow.Height = new GridLength(0);
            ImageBottomRow.Height = new GridLength(0);
            ImageRightSplitterColumn.Width = new GridLength(8);
            ImageRightColumn.MinWidth = 420;
            ImageRightColumn.Width = new GridLength(520);
            System.Windows.Controls.Grid.SetColumn(AlbumImagePanel, 4); System.Windows.Controls.Grid.SetRow(AlbumImagePanel, 0); System.Windows.Controls.Grid.SetRowSpan(AlbumImagePanel, 3);
            System.Windows.Controls.Grid.SetColumn(ImagePanelSplitter, 3); System.Windows.Controls.Grid.SetRow(ImagePanelSplitter, 0); System.Windows.Controls.Grid.SetRowSpan(ImagePanelSplitter, 3);
            ImagePanelSplitter.Width = 8; ImagePanelSplitter.Height = double.NaN;
            ImagePanelSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            ImagePanelSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            ImagePanelSplitter.ResizeDirection = System.Windows.Controls.GridResizeDirection.Columns;
        }
    }

    private void AlbumImage_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        var selectedImageIndex = sender is System.Windows.Controls.Image { Tag: int imageIndex }
            ? imageIndex : _albumImageIndex;
        if (selectedImageIndex < 0 || selectedImageIndex >= _albumImages.Count) return;
        _selectedAlbumImageIndex = selectedImageIndex;
        UpdateAlbumImageSelectionFrames();
        UpdateDeleteAlbumImageButton();
        UpdateArtworkRoleCombo();
        UpdateArtworkRotationButtons();
        if (e.ClickCount < 2) return;
        try
        {
            var images = _albumImages.ToList();
            var popupIndex = selectedImageIndex;
            var popupZoom = 1.0;
            var popupRotation = 0;
            var fitToWindow = true;
            var fullScreen = false;
            var scale = new System.Windows.Media.ScaleTransform(1, 1);
            var rotation = new System.Windows.Media.RotateTransform(0);
            var imageTransform = new System.Windows.Media.TransformGroup();
            imageTransform.Children.Add(rotation);
            imageTransform.Children.Add(scale);
            var popupImage = new System.Windows.Controls.Image
            {
                // Use an explicit pixel-sized layout surface below.  Stretch.None would
                // otherwise make WPF honor the scanner DPI stored in the file, so a
                // 300-dpi scan is rendered at only 96/300 of the size used by FitImage.
                Stretch = System.Windows.Media.Stretch.Fill, Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                LayoutTransform = imageTransform
            };
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(popupImage, System.Windows.Media.BitmapScalingMode.HighQuality);
            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                Content = popupImage, Background = System.Windows.Media.Brushes.Black,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                CanContentScroll = false
            };
            var previousButton = new System.Windows.Controls.Button { Content = "◀ 前の画像", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(4) };
            var nextButton = new System.Windows.Controls.Button { Content = "次の画像 ▶", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(4) };
            var rotateLeftButton = new System.Windows.Controls.Button { Content = "↶ 左90°", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4), ToolTip = "画像を左へ90度回転" };
            var rotateRightButton = new System.Windows.Controls.Button { Content = "右90° ↷", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4), ToolTip = "画像を右へ90度回転" };
            var zoomOutButton = new System.Windows.Controls.Button { Content = "－", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
            var zoomInButton = new System.Windows.Controls.Button { Content = "＋", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
            var actualSizeButton = new System.Windows.Controls.Button { Content = "100%", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
            var fitButton = new System.Windows.Controls.Button { Content = "全体表示", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
            var fullScreenButton = new System.Windows.Controls.Button { Content = "全画面", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(4) };
            var countText = new System.Windows.Controls.TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var zoomText = new System.Windows.Controls.TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White, Width = 58, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var nameText = new System.Windows.Controls.TextBlock
            {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 194, 206)),
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 8)
            };
            var controls = new System.Windows.Controls.WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            controls.Children.Add(previousButton); controls.Children.Add(countText); controls.Children.Add(nextButton);
            controls.Children.Add(rotateLeftButton); controls.Children.Add(rotateRightButton);
            controls.Children.Add(zoomOutButton); controls.Children.Add(zoomText); controls.Children.Add(zoomInButton);
            controls.Children.Add(actualSizeButton); controls.Children.Add(fitButton); controls.Children.Add(fullScreenButton);
            var popupGrid = new System.Windows.Controls.Grid();
            popupGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            popupGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            popupGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            popupGrid.Children.Add(scrollViewer);
            System.Windows.Controls.Grid.SetRow(controls, 1); popupGrid.Children.Add(controls);
            System.Windows.Controls.Grid.SetRow(nameText, 2); popupGrid.Children.Add(nameText);
            var popup = new Window
            {
                Owner = this, Background = System.Windows.Media.Brushes.Black,
                Content = popupGrid, Width = Math.Min(1000, SystemParameters.WorkArea.Width * 0.85),
                Height = Math.Min(760, SystemParameters.WorkArea.Height * 0.85),
                MinWidth = 420, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResizeWithGrip, WindowState = WindowState.Maximized
            };
            void ApplyZoom(double zoom)
            {
                // Very large scans may need less than 10% to fit on screen.
                popupZoom = Math.Clamp(zoom, 0.01, 4.0);
                scale.ScaleX = popupZoom; scale.ScaleY = popupZoom;
                zoomText.Text = $"{popupZoom * 100:0}%";
            }
            void FitImage()
            {
                if (popupImage.Source is not BitmapSource bitmap) return;
                var viewportWidth = scrollViewer.ViewportWidth > 0 && double.IsFinite(scrollViewer.ViewportWidth)
                    ? scrollViewer.ViewportWidth : scrollViewer.ActualWidth;
                var viewportHeight = scrollViewer.ViewportHeight > 0 && double.IsFinite(scrollViewer.ViewportHeight)
                    ? scrollViewer.ViewportHeight : scrollViewer.ActualHeight;
                ApplyZoom(CalculateArtworkPopupFitScale(bitmap.PixelWidth, bitmap.PixelHeight,
                    viewportWidth, viewportHeight, popupRotation, 24));
                scrollViewer.ScrollToHome();
            }
            void ScheduleFit() => popup.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FitImage));
            void RotateImage(int degrees)
            {
                images[popupIndex] = SetArtworkRotation(images[popupIndex],
                    images[popupIndex].RotationDegrees + degrees);
                popupRotation = 0;
                rotation.Angle = 0;
                UpdatePopup();
            }
            void ToggleFullScreen()
            {
                fullScreen = !fullScreen;
                popup.WindowState = WindowState.Normal;
                popup.WindowStyle = fullScreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                popup.WindowState = WindowState.Maximized;
                fullScreenButton.Content = fullScreen ? "全画面解除" : "全画面";
                // Entering full screen always returns to fit mode so the entire image
                // uses the largest possible area while preserving its aspect ratio.
                fitToWindow = true;
                popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScheduleFit));
            }
            void UpdatePopup()
            {
                var currentImage = images[popupIndex];
                SetArtworkPopupImage(popupImage, LoadBitmap(currentImage, 0));
                countText.Text = $"{popupIndex + 1} / {images.Count}";
                nameText.Text = currentImage.DisplayName;
                nameText.ToolTip = currentImage.Description;
                popup.Title = currentImage.DisplayName;
                previousButton.IsEnabled = images.Count > 1;
                nextButton.IsEnabled = images.Count > 1;
                if (fitToWindow) ScheduleFit();
                else ApplyZoom(popupZoom);
                scrollViewer.ScrollToHome();
            }
            void PreviousPopupImage() { popupIndex = (popupIndex - 1 + images.Count) % images.Count; UpdatePopup(); }
            void NextPopupImage() { popupIndex = (popupIndex + 1) % images.Count; UpdatePopup(); }
            previousButton.Click += (_, _) => PreviousPopupImage();
            nextButton.Click += (_, _) => NextPopupImage();
            rotateLeftButton.Click += (_, _) => RotateImage(-90);
            rotateRightButton.Click += (_, _) => RotateImage(90);
            zoomOutButton.Click += (_, _) => { fitToWindow = false; ApplyZoom(popupZoom / 1.25); };
            zoomInButton.Click += (_, _) => { fitToWindow = false; ApplyZoom(popupZoom * 1.25); };
            actualSizeButton.Click += (_, _) => { fitToWindow = false; ApplyZoom(1.0); };
            fitButton.Click += (_, _) => { fitToWindow = true; ScheduleFit(); };
            fullScreenButton.Click += (_, _) => ToggleFullScreen();
            scrollViewer.PreviewMouseWheel += (_, wheelEvent) =>
            {
                fitToWindow = false;
                var oldZoom = popupZoom;
                var pointer = wheelEvent.GetPosition(scrollViewer);
                var contentX = scrollViewer.HorizontalOffset + pointer.X;
                var contentY = scrollViewer.VerticalOffset + pointer.Y;
                ApplyZoom(wheelEvent.Delta > 0 ? popupZoom * 1.15 : popupZoom / 1.15);
                var ratio = popupZoom / oldZoom;
                popup.Dispatcher.BeginInvoke(() =>
                {
                    scrollViewer.ScrollToHorizontalOffset(contentX * ratio - pointer.X);
                    scrollViewer.ScrollToVerticalOffset(contentY * ratio - pointer.Y);
                });
                wheelEvent.Handled = true;
            };
            popup.Loaded += (_, _) => ScheduleFit();
            popup.SizeChanged += (_, _) => { if (fitToWindow) ScheduleFit(); };
            popup.KeyDown += (_, keyEvent) =>
            {
                if (keyEvent.Key == Key.Escape) popup.Close();
                else if (keyEvent.Key == Key.Left) PreviousPopupImage();
                else if (keyEvent.Key == Key.Right) NextPopupImage();
                else if (keyEvent.Key == Key.F11) ToggleFullScreen();
                else if (keyEvent.Key == Key.R) RotateImage(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -90 : 90);
            };
            UpdatePopup();
            popup.Show();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像を開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void SetArtworkPopupImage(System.Windows.Controls.Image image, BitmapSource bitmap)
    {
        image.Source = bitmap;
        // BitmapSource.Width/Height are DPI-adjusted device-independent units.  The
        // viewer's zoom calculation deliberately uses pixels, so make the layout use
        // the same basis. This keeps 96/300/600-dpi scans visually identical.
        image.Width = Math.Max(1, bitmap.PixelWidth);
        image.Height = Math.Max(1, bitmap.PixelHeight);
    }

    private static double CalculateArtworkPopupFitScale(int pixelWidth, int pixelHeight,
        double viewportWidth, double viewportHeight, int rotationDegrees, double reservedSpace = 0)
    {
        var width = Math.Max(1, viewportWidth - reservedSpace);
        var height = Math.Max(1, viewportHeight - reservedSpace);
        var quarterTurn = Math.Abs(rotationDegrees % 180) == 90;
        var imageWidth = Math.Max(1, quarterTurn ? pixelHeight : pixelWidth);
        var imageHeight = Math.Max(1, quarterTurn ? pixelWidth : pixelHeight);
        return Math.Clamp(Math.Min(width / imageWidth, height / imageHeight), 0.01, 4.0);
    }

    private void UpdateAlbumImageSelectionFrames()
    {
        foreach (var frame in AlbumImageGallery.Children.OfType<System.Windows.Controls.Border>())
        {
            var selected = frame.Tag is int index && index == _selectedAlbumImageIndex;
            frame.BorderBrush = selected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 169, 229))
                : System.Windows.Media.Brushes.Transparent;
            frame.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void UpdateArtworkRotationButtons()
    {
        if (RotateAlbumImageLeftButton is null) return;
        var enabled = _album is not null && _selectedAlbumImageIndex >= 0
            && _selectedAlbumImageIndex < _albumImages.Count;
        RotateAlbumImageLeftButton.IsEnabled = enabled;
        RotateAlbumImageRightButton.IsEnabled = enabled;
        ResetAlbumImageRotationButton.IsEnabled = enabled
            && _albumImages[_selectedAlbumImageIndex].RotationDegrees != 0;
    }

    private AlbumImageSource SetArtworkRotation(AlbumImageSource source, int degrees)
    {
        if (_album is null) return source;
        degrees = NormalizeArtworkRotation(degrees);
        if (degrees == 0) _currentArtworkRotations.Remove(source.RoleKey);
        else _currentArtworkRotations[source.RoleKey] = degrees;
        SaveArtworkRotations(_album.Path, _currentArtworkRotations);
        var folds = LoadSpineCardFolds(_album.Path);
        if (folds.Remove(source.RoleKey)) SaveSpineCardFolds(_album.Path, folds);
        var updated = source with { RotationDegrees = degrees };
        for (var index = 0; index < _albumImages.Count; index++)
            if (string.Equals(_albumImages[index].RoleKey, source.RoleKey, StringComparison.OrdinalIgnoreCase))
                _albumImages[index] = updated;
        var item = _albums.FirstOrDefault(candidate =>
            string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase));
        item?.RefreshImageCount();
        ShowCurrentAlbumImage();
        QueueCoverFlowRefresh();
        StatusText.Text = degrees == 0
            ? $"画像の向きを元に戻しました: {source.DisplayName}"
            : $"画像を{degrees}度回転しました: {source.DisplayName}";
        return updated;
    }

    private void RotateAlbumImageLeft_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbumImageIndex < 0 || _selectedAlbumImageIndex >= _albumImages.Count) return;
        var source = _albumImages[_selectedAlbumImageIndex];
        SetArtworkRotation(source, source.RotationDegrees - 90);
    }

    private void RotateAlbumImageRight_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbumImageIndex < 0 || _selectedAlbumImageIndex >= _albumImages.Count) return;
        var source = _albumImages[_selectedAlbumImageIndex];
        SetArtworkRotation(source, source.RotationDegrees + 90);
    }

    private void ResetAlbumImageRotation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbumImageIndex < 0 || _selectedAlbumImageIndex >= _albumImages.Count) return;
        SetArtworkRotation(_albumImages[_selectedAlbumImageIndex], 0);
    }

    private bool CanDeleteAlbumImage(int index)
    {
        if (_album is null || index < 0 || index >= _albumImages.Count || _albumImages[index].FilePath is not { } path) return false;
        return IsManagedArtworkPath(_album.Path, path) && File.Exists(path);
    }

    private static bool IsManagedArtworkPath(string albumPath, string path)
    {
        try
        {
            var managedDirectory = Path.GetFullPath(GetDownloadedArtworkDirectory(albumPath))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(Path.GetFullPath(path))?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(parent, managedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void UpdateDeleteAlbumImageButton()
    {
        if (DeleteAlbumImageButton is null) return;
        var canDelete = CanDeleteAlbumImage(_selectedAlbumImageIndex);
        DeleteAlbumImageButton.IsEnabled = canDelete;
        DeleteAlbumImageButton.ToolTip = canDelete
            ? "選択したアプリ保存画像を削除（ごみ箱へ移動）"
            : "アルバムフォルダ内画像とZIP内部画像は保護されています";
    }

    private void DeleteAlbumImage_Click(object sender, RoutedEventArgs e)
    {
        if (!CanDeleteAlbumImage(_selectedAlbumImageIndex) || _album is null) return;
        var source = _albumImages[_selectedAlbumImageIndex];
        var path = source.FilePath!;
        if (MessageBox.Show(this, $"選択した画像を削除しますか？\n\n{source.DisplayName}\n\nファイルはごみ箱へ移動します。",
            "アルバム画像を削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var deletedIndex = _selectedAlbumImageIndex;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            if (_currentArtworkRoles.Remove(source.RoleKey)) SaveArtworkRoles(_album.Path, _currentArtworkRoles);
            if (_currentArtworkRotations.Remove(source.RoleKey)) SaveArtworkRotations(_album.Path, _currentArtworkRotations);
            var folds = LoadSpineCardFolds(_album.Path);
            if (folds.Remove(source.RoleKey)) SaveSpineCardFolds(_album.Path, folds);
            var item = _albums.FirstOrDefault(candidate =>
                string.Equals(candidate.Album.Path, _album.Path, StringComparison.OrdinalIgnoreCase));
            item?.RefreshImageCount();
            if (item is not null) { RemoveAlbumFromArtistTree(item); AddAlbumToArtistTree(item); }
            LoadAlbumImages(_album);
            if (_albumImages.Count > 0)
            {
                _albumImageIndex = Math.Min(deletedIndex, _albumImages.Count - 1);
                _selectedAlbumImageIndex = _albumImageIndex;
                ShowCurrentAlbumImage();
            }
            StatusText.Text = $"画像をごみ箱へ移動しました: {source.DisplayName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "画像を削除できません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviousImage_Click(object sender, RoutedEventArgs e)
    {
        if (_albumImages.Count == 0) return;
        _albumImageIndex = _albumImageIndex <= 0
            ? ((_albumImages.Count - 1) / Math.Max(1, _albumImagePageSize)) * Math.Max(1, _albumImagePageSize)
            : Math.Max(0, _albumImageIndex - Math.Max(1, _albumImagePageSize));
        _selectedAlbumImageIndex = _albumImageIndex;
        ShowCurrentAlbumImage();
    }

    private void NextImage_Click(object sender, RoutedEventArgs e)
    {
        if (_albumImages.Count == 0) return;
        _albumImageIndex = _albumImageIndex + Math.Max(1, _albumImagePageSize) >= _albumImages.Count
            ? 0 : _albumImageIndex + Math.Max(1, _albumImagePageSize);
        _selectedAlbumImageIndex = _albumImageIndex;
        ShowCurrentAlbumImage();
    }

    private void ClearAlbumImages()
    {
        _albumImages.Clear();
        _albumImageIndex = -1;
        _selectedAlbumImageIndex = -1;
        _visibleAlbumImageCount = 1;
        _albumImagePageSize = 1;
        AlbumImageGallery.Children.Clear();
        AlbumImageEmptyText.Text = "画像はありません";
        AlbumImageEmptyText.Visibility = Visibility.Visible;
        AlbumImageCountText.Text = "0 / 0";
        AlbumImageNameText.Text = "";
        AlbumImageNameText.ToolTip = null;
        PreviousImageButton.IsEnabled = false;
        NextImageButton.IsEnabled = false;
        DeleteAlbumImageButton.IsEnabled = false;
        RotateAlbumImageLeftButton.IsEnabled = false;
        RotateAlbumImageRightButton.IsEnabled = false;
        ResetAlbumImageRotationButton.IsEnabled = false;
        AdjustSpineCardFoldsButton.IsEnabled = false;
        AdjustSpineCardFoldsButton.Visibility = Visibility.Collapsed;
        ArtworkRoleCombo.IsEnabled = false;
        TrayColorCombo.IsEnabled = _album is not null;
    }

    private void StopPlayback(bool resetPosition)
    {
        _favoriteQueue = [];
        _favoriteQueueIndex = -1;
        AccumulateUsageTime();
        _ignoreStopped = true;
        try { _output?.Stop(); } catch { }
        _timer.Stop();
        DisposeAudio();
        _playingAlbum = null;
        _currentIndex = -1;
        UpdatePlayingAlbumIndicator(null);
        SelectNowPlayingButton.IsEnabled = false;
        _activeUsageEntry = null;
        _activeUsageSessionSeconds = 0;
        _activeUsagePlayCommitted = false;
        ClearNowPlaying();
        _ignoreStopped = false;
        PlayButton.Content = "▶ 再生";
        if (resetPosition) { PositionSlider.Value = 0; ElapsedText.Text = "0:00"; }
        Title = _applicationTitle;
        PlaybackStatusText.Text = "停止";
    }

    private void DisposeAudio()
    {
        if (_output is not null) _output.PlaybackStopped -= Output_PlaybackStopped;
        _output?.Dispose(); _gapless?.Dispose(); _trackReader?.Dispose();
        _gapless = null;
        _output = null; _reader = null; _trackReader = null; _remaster = null; _equalizer = null;
        _bassBoost = null; _lowVolumeClarity = null; _volumeGain = null; _waveform = null; _spectrum = null;
        _faithfulExclusiveActive = false;
        WaveformDisplay.SetSamples(null);
        SpectrumDisplay.SetBands(null);
    }

#if AI_FEATURE
    private void DisposeAiPreviewPlayback()
    {
        var hadAiPlayback = _aiPreviewOutput is not null || _aiPreviewReader is not null;
        if (_aiPreviewOutput is not null) _aiPreviewOutput.PlaybackStopped -= AiPreviewOutput_PlaybackStopped;
        _aiPreviewOutput?.Dispose();
        _aiPreviewReader?.Dispose();
        _aiPreviewOutput = null;
        _aiPreviewReader = null;
        _aiRemaster = null;
        _aiEqualizer = null;
        _aiBassBoost = null;
        _aiLowVolumeClarity = null;
        _aiVolumeGain = null;
        _aiWaveform = null;
        _aiSpectrum = null;
        if (hadAiPlayback)
        {
            _timer.Stop();
            WaveformDisplay.SetSamples(null);
            SpectrumDisplay.SetBands(null);
        }
        _activeAiPreviewButton = null;
        _activeAiPreviewLabel = "AIプレビュー";
        if (AiPlayPreviewButton is not null) AiPlayPreviewButton.Content = "AI版を試聴";
        if (KaraokePlayButton is not null) KaraokePlayButton.Content = "カラオケ版を試聴";
    }
#endif

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        if (Directory.Exists(files[0]))
        {
            var folder = Path.GetFullPath(files[0]);
            if (!_folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))) _folders.Add(folder);
            _disabledFolders.Remove(folder);
            SaveSettings();
            ConfigureLibraryWatchers();
            _ = ScanFoldersAsync();
        }
        else await OpenAlbumAsync(files[0]);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase
            && Clipboard.ContainsImage())
        {
            e.Handled = PasteAlbumArtworkFromClipboard();
        }
        else if (e.Key == Key.Space) { PlayPause_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Next_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Previous_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Open_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
        { EditTrackTags_Click(sender, e); e.Handled = true; }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_dataRestorePendingRestart)
        {
            _cacheSaveTimer.Stop();
            _usageSaveTimer.Stop();
            _localizationTimer.Stop();
            _scanCancellation?.Cancel();
            _incrementalRefreshCancellation?.Cancel();
            DisposeLibraryWatchers();
            _coverFlowHighResolutionCancellation?.Cancel();
            StopPlayback(resetPosition: false);
            return;
        }
        if (!_forceClose && _settings.MinimizeOnClose)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            StatusText.Text = "タスクバーへ最小化しました（設定画面から終了できます）";
            return;
        }
        AccumulateUsageTime();
        SaveSettings();
        SaveLibraryCache();
        _scanCancellation?.Cancel();
        _incrementalRefreshCancellation?.Cancel();
        DisposeLibraryWatchers();
        _coverFlowHighResolutionCancellation?.Cancel();
        StopPlayback(resetPosition: false);
        _usageSaveTimer.Stop();
        _localizationTimer.Stop();
        _usageStore.Save();
        _favoritesStore.Save();
    }
    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private sealed class PlayerSettings
    {
        public List<string> MusicFolders { get; set; } = [];
        public List<string> DisabledMusicFolders { get; set; } = [];
        public bool MinimizeOnClose { get; set; }
        public bool TagBackupEnabled { get; set; }
        public string TagBackupFolder { get; set; } = "";
        public string DisplayLanguage { get; set; } = LocalizationService.Japanese;
        public string? LastAlbumPath { get; set; }
        public int LastTrackIndex { get; set; }
        public double LastPositionSeconds { get; set; }
        public double Volume { get; set; } = 0.8;
        public double PlaybackSpeed { get; set; } = 1.0;
        public bool PreservePitch { get; set; } = true;
        public bool FaithfulMode { get; set; }
        public bool GaplessPlayback { get; set; } = true;
        public bool EqEnabled { get; set; } = true;
        public double[] EqGains { get; set; } = new double[10];
        public bool BassBoostEnabled { get; set; }
        public double BassBoostAmount { get; set; } = 60;
        public bool LowVolumeClarityEnabled { get; set; }
        public string RemasterMode { get; set; } = "Off";
        public bool Shuffle { get; set; }
        public int RepeatMode { get; set; }
        public string AlbumSort { get; set; } = "Artist";
        public string ImagePanelLayout { get; set; } = "Bottom";
        public double ImageBottomRatio { get; set; } = 0.56;
        public double ImageLyricsRatio { get; set; } = 0.6;
        public bool LyricsPanelExpanded { get; set; } = true;
        public bool LyricsAutoScroll { get; set; } = true;
        public bool ExtensionPanelExpanded { get; set; } = true;
        public double ExtensionPanelWidth { get; set; } = 300;
        public string VisualizerMode { get; set; } = "Spectrum";
    }
    private sealed class LibraryCache
    {
        public int Version { get; set; }
        public bool IsComplete { get; set; }
        public List<ZipAlbum> Albums { get; set; } = [];
    }
    private sealed record ScanUpdate(string Message, AlbumListItem? Album, int Current, int Total);
    private sealed record ExistingLibraryAlbum(string Path, bool IsArchive);
    private sealed record IncrementalLibraryResult(
        IReadOnlyList<AlbumListItem> Refreshed,
        HashSet<string> Removed,
        HashSet<string> Retry);
    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
    private sealed record AlbumImageSource(string DisplayName, string? FilePath, ZipImage? ZipEntry, int RotationDegrees = 0)
    {
        public string RoleKey => FilePath is not null
            ? $"file:{Path.GetFullPath(FilePath)}"
            : $"zip:{ZipEntry?.FileName}";
        public string RoleHint => FilePath ?? ZipEntry?.FileName ?? DisplayName;
        public string Description => FilePath ?? LocalizationService.Select(
            $"{ZipEntry?.SourcePath}\nZIP内: {ZipEntry?.FileName}", $"{ZipEntry?.SourcePath}\nInside ZIP: {ZipEntry?.FileName}");
    }

    private sealed class ArtistTreeNode
    {
        public string Display { get; private init; } = "";
        public string ArtistKey { get; private init; } = "";
        public bool IsGroup { get; private init; }
        public AlbumListItem? AlbumItem { get; private init; }
        public ObservableCollection<ArtistTreeNode> Children { get; } = [];

        public static ArtistTreeNode CreateGroup(string artist) => new() { Display = artist, ArtistKey = artist, IsGroup = true };
        public static ArtistTreeNode CreateAlbum(AlbumListItem album) => new()
        {
            Display = $"[{album.SourceBadge}] {album.Title}{(album.HasImages ? $"  ({album.ImageBadge})" : "")}",
            ArtistKey = album.Artist, AlbumItem = album
        };
    }

    private sealed class AlbumListItem : INotifyPropertyChanged
    {
        public ZipAlbum Album { get; }
        public string Title { get; }
        public string Artist { get; }
        public string SearchText { get; }
        public string Detail => $"{(Artist == "アーティスト不明" ? LocalizationService.Select("アーティスト不明", "Unknown Artist") : Artist)}  •  "
            + LocalizationService.Select($"{Album.Tracks.Count}曲", $"{Album.Tracks.Count} tracks");
        private int _downloadedImageCount;
        private BitmapSource? _coverThumbnail;
        private BitmapSource? _caseFrontThumbnail;
        private BitmapSource? _insideFrontThumbnail;
        private BitmapSource? _backCoverThumbnail;
        private BitmapSource? _spineThumbnail;
        private BitmapSource? _rightSpineThumbnail;
        private BitmapSource? _inlayThumbnail;
        private BitmapSource? _discThumbnail;
        private BitmapSource? _secondDiscThumbnail;
        private BitmapSource? _spineCardThumbnail;
        private BitmapSource? _mediumCaseFrontThumbnail;
        private BitmapSource? _mediumInsideFrontThumbnail;
        private BitmapSource? _mediumBackCoverThumbnail;
        private BitmapSource? _mediumSpineThumbnail;
        private BitmapSource? _mediumRightSpineThumbnail;
        private BitmapSource? _mediumInlayThumbnail;
        private BitmapSource? _mediumDiscThumbnail;
        private BitmapSource? _mediumSecondDiscThumbnail;
        private BitmapSource? _mediumSpineCardThumbnail;
        private bool _caseArtworkLoaded;
        private int _caseArtworkDecodeWidth;
        private string _coverDescription = "画像はありません";
        private string _trayColorMode = "Auto";
        private bool _isFavorite;
        private bool _isPlaying;
        public int ImageCount => Math.Max(Album.ImageCount, Album.Images.Count) + _downloadedImageCount;
        public bool HasImages => ImageCount > 0;
        public string ImageBadge => LocalizationService.Select($"画像 {ImageCount}", $"Images {ImageCount}");
        public BitmapSource? CoverThumbnail => _coverThumbnail;
        // Gallery thumbnails may be supplementary scans. Only role-resolved
        // artwork is safe to use on the physical case.
        public BitmapSource? CaseFrontThumbnail => _caseFrontThumbnail;
        public BitmapSource? InsideFrontThumbnail => _insideFrontThumbnail;
        public BitmapSource? BackCoverThumbnail => _backCoverThumbnail;
        public BitmapSource? SpineThumbnail => _spineThumbnail;
        public BitmapSource? RightSpineThumbnail => _rightSpineThumbnail;
        public BitmapSource? InlayThumbnail => _inlayThumbnail;
        public BitmapSource? DiscThumbnail => _discThumbnail;
        public BitmapSource? SecondDiscThumbnail => _secondDiscThumbnail;
        public BitmapSource? SpineCardThumbnail => _spineCardThumbnail;
        public int CaseArtworkDecodeWidth => _caseArtworkDecodeWidth;
        public bool HasCoverThumbnail => _coverThumbnail is not null;
        public string CoverDescription => _coverDescription;
        public string TrayColorMode => _trayColorMode;
        public bool HasFrontSpread { get; private set; }

        public BookletContent LoadBooklet()
        {
            var roles = LoadArtworkRoles(Album.Path);
            var sources = GetCaseArtworkSources(Album, GetDownloadedArtworkDirectory(Album.Path));
            var spread = sources.FirstOrDefault(source => GetEffectiveArtworkRole(source, roles)
                is "FrontSpread" or "FrontSpreadReversed" or "FrontSpreadVertical");
            var spreadRole = spread is null ? null : GetEffectiveArtworkRole(spread, roles);
            var verticalSpread = spreadRole == "FrontSpreadVertical";
            var reversedSpread = spreadRole == "FrontSpreadReversed";
            var front = sources.FirstOrDefault(source => GetEffectiveArtworkRole(source, roles) == "Front");
            var frontInside = sources.FirstOrDefault(source => GetEffectiveArtworkRole(source, roles) == "FrontInside");
            if (spread is null && (front is null || frontInside is null))
                throw new InvalidOperationException(LocalizationService.Select(
                    "Front見開き、またはFrontとFront背面の画像が必要です。",
                    "A Front Spread, or both Front and Inside Front artwork, is required."));
            var pages = new List<BookletPage>
            {
                front is not null
                    ? new BookletPage(front.DisplayName, "Front", () => LoadBitmap(front, 3000))
                    : new BookletPage(LocalizationService.Select("Front（表紙）", "Front cover"), "Front",
                        () => CropArtwork(LoadBitmap(spread!, verticalSpread ? 3000 : 6000),
                            verticalSpread ? "TopHalf" : reversedSpread ? "LeftHalf" : "RightHalf"))
            };
            pages.AddRange(sources.Where(source => GetEffectiveArtworkRole(source, roles) is "Page" or "LinerNotes" or "Flyer")
                .OrderBy(source => GetEffectiveArtworkRole(source, roles) switch
                {
                    "Page" => 0,
                    "LinerNotes" => 1,
                    _ => 2
                })
                .ThenBy(source => GetAlbumImageSequence(source.DisplayName))
                .ThenBy(source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(source => new BookletPage(source.DisplayName, GetEffectiveArtworkRole(source, roles), () => LoadBitmap(source, 3000)))
                .ToList());
            pages.Add(frontInside is not null
                ? new BookletPage(frontInside.DisplayName, "FrontInside", () => LoadBitmap(frontInside, 3000))
                : new BookletPage(LocalizationService.Select("Front背面", "Inside front cover"), "FrontInside",
                    () => CropArtwork(LoadBitmap(spread!, verticalSpread ? 3000 : 6000),
                        verticalSpread ? "BottomHalfRotated" : reversedSpread ? "RightHalf" : "LeftHalf")));
            // When both individual sides exist, the spread scan is redundant
            // and is not even used for the opening frame.
            var openingArtwork = front is not null && frontInside is not null
                ? LoadBitmap(front, 2400)
                : verticalSpread
                    ? CreateHorizontalFrontSpread(
                        CropArtwork(LoadBitmap(spread!, 2400), "BottomHalfRotated"),
                        CropArtwork(LoadBitmap(spread!, 2400), "TopHalf"))
                    : reversedSpread
                        ? CreateHorizontalFrontSpread(
                            CropArtwork(LoadBitmap(spread!, 4800), "RightHalf"),
                            CropArtwork(LoadBitmap(spread!, 4800), "LeftHalf"))
                    : LoadBitmap(spread!, 2400);
            return new BookletContent(openingArtwork, pages);
        }
        public bool IsFavorite => _isFavorite;
        public string FavoriteGlyph => IsFavorite ? "★" : "☆";
        public bool IsPlaying => _isPlaying;
        public bool IsArchive => Album.Tracks.FirstOrDefault()?.IsArchiveEntry == true;
        public bool IsZipMp3 => IsArchive && Album.Path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase);
        public string SourceBadge => IsZipMp3 ? "ZIP.MP3" : IsArchive ? "ZIP" : "DIR";
        public string SourceDescription => LocalizationService.Select(
            $"{Title}\n形式: {(IsZipMp3 ? "ZIP.MP3" : IsArchive ? "ZIP" : "音楽フォルダ")}\n場所: {Album.Path}",
            $"{Title}\nType: {(IsZipMp3 ? "ZIP.MP3" : IsArchive ? "ZIP" : "Music folder")}\nLocation: {Album.Path}");
        public AlbumListItem(ZipAlbum album)
        {
            Album = album;
            var first = album.Tracks.First();
            var fallbackName = Path.GetFileName(album.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var fallback = fallbackName.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
                ? fallbackName[..^8]
                : fallbackName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? fallbackName[..^4]
                    : fallbackName;
            Title = string.IsNullOrWhiteSpace(first.Album) ? fallback : first.Album;
            Artist = string.IsNullOrWhiteSpace(first.Artist) ? "アーティスト不明" : first.Artist;
            SearchText = NormalizeAlbumSearch(string.Join(' ', new[] { Title, Artist, album.Path }
                .Concat(album.Tracks.SelectMany(track => new[] { track.Title, track.Artist, track.Album, track.FileName }))));
            RefreshImageCount();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetFavorite(bool value)
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyph)));
        }

        public void SetPlaying(bool value)
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        }

        public void RefreshImageCount()
        {
            var directory = GetDownloadedArtworkDirectory(Album.Path);
            var sources = GetCaseArtworkSources(Album, directory);
            var roles = LoadArtworkRoles(Album.Path);
            var roleSet = sources.Select(source => GetEffectiveArtworkRole(source, roles)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            HasFrontSpread = roleSet.Contains("FrontSpread") || roleSet.Contains("FrontSpreadReversed")
                || roleSet.Contains("FrontSpreadVertical")
                || (roleSet.Contains("Front") && roleSet.Contains("FrontInside"));
            _trayColorMode = HasInlayArtwork(sources, roles)
                ? "Clear" : LoadTrayColor(Album.Path);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrayColorMode)));
            _downloadedImageCount = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Count(IsAlbumImage) : 0;
            (_coverThumbnail, _coverDescription) = LoadCoverThumbnail(directory);
            _caseFrontThumbnail = _insideFrontThumbnail = _backCoverThumbnail = _spineThumbnail = _rightSpineThumbnail = _inlayThumbnail = _discThumbnail = _secondDiscThumbnail = _spineCardThumbnail = null;
            _mediumCaseFrontThumbnail = _mediumInsideFrontThumbnail = _mediumBackCoverThumbnail = _mediumSpineThumbnail = _mediumRightSpineThumbnail = null;
            _mediumInlayThumbnail = _mediumDiscThumbnail = _mediumSecondDiscThumbnail = _mediumSpineCardThumbnail = null;
            _caseArtworkLoaded = false;
            _caseArtworkDecodeWidth = 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImages)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageBadge)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InsideFrontThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackCoverThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpineThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightSpineThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InlayThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondDiscThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCoverThumbnail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverDescription)));
        }

        public void EnsureCaseArtworkLoaded(int decodePixelWidth = 640)
        {
            decodePixelWidth = Math.Max(120, decodePixelWidth);
            if (_caseArtworkLoaded && _caseArtworkDecodeWidth >= decodePixelWidth) return;
            _caseArtworkLoaded = true;
            _caseArtworkDecodeWidth = decodePixelWidth;
            var directory = GetDownloadedArtworkDirectory(Album.Path);
            (_caseFrontThumbnail, _insideFrontThumbnail, _backCoverThumbnail, _spineThumbnail,
                _rightSpineThumbnail, _inlayThumbnail, _discThumbnail, _secondDiscThumbnail, _spineCardThumbnail, _)
                = LoadCaseArtwork(directory, decodePixelWidth);
            if (decodePixelWidth <= 640)
            {
                _mediumCaseFrontThumbnail = _caseFrontThumbnail;
                _mediumInsideFrontThumbnail = _insideFrontThumbnail;
                _mediumBackCoverThumbnail = _backCoverThumbnail;
                _mediumSpineThumbnail = _spineThumbnail;
                _mediumRightSpineThumbnail = _rightSpineThumbnail;
                _mediumInlayThumbnail = _inlayThumbnail;
                _mediumDiscThumbnail = _discThumbnail;
                _mediumSecondDiscThumbnail = _secondDiscThumbnail;
                _mediumSpineCardThumbnail = _spineCardThumbnail;
            }
        }

        public async Task EnsureCaseArtworkLoadedAsync(int decodePixelWidth, CancellationToken cancellationToken)
        {
            decodePixelWidth = Math.Max(120, decodePixelWidth);
            if (_caseArtworkLoaded && _caseArtworkDecodeWidth >= decodePixelWidth) return;
            var directory = GetDownloadedArtworkDirectory(Album.Path);
            var artwork = await Task.Run(() => LoadCaseArtwork(directory, decodePixelWidth), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_caseArtworkLoaded && _caseArtworkDecodeWidth >= decodePixelWidth) return;
            _caseFrontThumbnail = artwork.Front;
            _insideFrontThumbnail = artwork.InsideFront;
            _backCoverThumbnail = artwork.Back;
            _spineThumbnail = artwork.Spine;
            _rightSpineThumbnail = artwork.RightSpine;
            _inlayThumbnail = artwork.Inlay;
            _discThumbnail = artwork.Disc;
            _secondDiscThumbnail = artwork.SecondDisc;
            _spineCardThumbnail = artwork.SpineCard;
            _caseArtworkLoaded = true;
            _caseArtworkDecodeWidth = decodePixelWidth;
        }

        public void ReleaseHighResolutionCaseArtwork(int decodePixelWidth = 640)
        {
            if (_caseArtworkDecodeWidth <= decodePixelWidth) return;
            if (_mediumCaseFrontThumbnail is not null || _mediumSpineCardThumbnail is not null)
            {
                _caseFrontThumbnail = _mediumCaseFrontThumbnail;
                _insideFrontThumbnail = _mediumInsideFrontThumbnail;
                _backCoverThumbnail = _mediumBackCoverThumbnail;
                _spineThumbnail = _mediumSpineThumbnail;
                _rightSpineThumbnail = _mediumRightSpineThumbnail;
                _inlayThumbnail = _mediumInlayThumbnail;
                _discThumbnail = _mediumDiscThumbnail;
                _secondDiscThumbnail = _mediumSecondDiscThumbnail;
                _spineCardThumbnail = _mediumSpineCardThumbnail;
                _caseArtworkLoaded = true;
                _caseArtworkDecodeWidth = decodePixelWidth;
            }
            else
            {
                _caseFrontThumbnail = _insideFrontThumbnail = _backCoverThumbnail = _spineThumbnail = _rightSpineThumbnail = _inlayThumbnail = _discThumbnail = _secondDiscThumbnail = _spineCardThumbnail = null;
                _caseArtworkLoaded = false;
                _caseArtworkDecodeWidth = 0;
            }
        }

        private (BitmapSource? Image, string Description) LoadCoverThumbnail(string downloadedDirectory)
        {
            var rotations = LoadArtworkRotations(Album.Path);
            var sources = GetCaseArtworkSources(Album, downloadedDirectory);
            var roles = LoadArtworkRoles(Album.Path);
            var frontSpread = sources.FirstOrDefault(source =>
                GetEffectiveArtworkRole(source, roles) is "FrontSpread" or "FrontSpreadReversed" or "FrontSpreadVertical");
            if (frontSpread is not null)
            {
                try
                {
                    return (LoadFrontSpreadThumbnail(Album.Path, frontSpread,
                            GetEffectiveArtworkRole(frontSpread, roles)),
                        $"{frontSpread.DisplayName}\n{frontSpread.Description}");
                }
                catch { }
            }
            foreach (var source in sources)
            {
                try
                {
                    if (source.ZipEntry is not null)
                        return (LoadBitmap(source, 120), $"ZIP内画像\n{source.ZipEntry.FileName}");
                    var path = source.FilePath!;
                    var managed = string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
                        Path.GetFullPath(downloadedDirectory), StringComparison.OrdinalIgnoreCase);
                    return (LoadBitmap(source, 120), managed
                        ? $"{GetManagedArtworkDisplayName(path)}\n{path}"
                        : $"アルバムフォルダ画像\n{path}");
                }
                catch { }
            }
            return (null, "画像はありません");
        }

        private static BitmapSource LoadFrontSpreadThumbnail(string albumPath, AlbumImageSource source, string role)
        {
            var sourcePath = source.FilePath ?? source.ZipEntry?.SourcePath;
            var file = !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath) ? new FileInfo(sourcePath) : null;
            var identity = string.Join('|', source.RoleKey, role, source.RotationDegrees,
                file?.Length ?? 0, file?.LastWriteTimeUtc.Ticks ?? 0,
                source.ZipEntry?.DataOffset ?? 0, source.ZipEntry?.CompressedSize ?? 0);
            static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..20];
            var albumKey = Key(Path.GetFullPath(albumPath).ToUpperInvariant());
            var cacheDirectory = Path.Combine(DataDirectory, "thumbnail-cache");
            var cachePath = Path.Combine(cacheDirectory, $"{albumKey}-{Key(identity)}.png");
            if (File.Exists(cachePath))
            {
                try { return LoadBitmap(cachePath, 120); }
                catch { }
            }

            var vertical = role == "FrontSpreadVertical";
            var reversed = role == "FrontSpreadReversed";
            var front = CropArtwork(LoadBitmap(source, vertical ? 120 : 240),
                vertical ? "TopHalf" : reversed ? "LeftHalf" : "RightHalf");
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                foreach (var stale in Directory.EnumerateFiles(cacheDirectory, $"{albumKey}-*.png", SearchOption.TopDirectoryOnly)
                    .Where(path => !string.Equals(path, cachePath, StringComparison.OrdinalIgnoreCase)))
                    File.Delete(stale);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(front));
                using var output = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                encoder.Save(output);
            }
            catch { }
            return front;
        }

        private (BitmapSource? Front, BitmapSource? InsideFront, BitmapSource? Back,
            BitmapSource? Spine, BitmapSource? RightSpine, BitmapSource? Inlay,
            BitmapSource? Disc, BitmapSource? SecondDisc, BitmapSource? SpineCard, string Description) LoadCaseArtwork(
                string downloadedDirectory, int targetWidth)
        {
            var sources = GetCaseArtworkSources(Album, downloadedDirectory);
            if (sources.Count == 0) return (null, null, null, null, null, null, null, null, null, "画像はありません");

            var roles = LoadArtworkRoles(Album.Path);
            var candidates = new List<(AlbumImageSource Source, string Role, double Aspect, bool IsManual)>();
            foreach (var source in sources)
            {
                try
                {
                    var probe = LoadBitmap(source, 96);
                    var isManual = roles.TryGetValue(source.RoleKey, out var storedRole)
                        && !string.Equals(storedRole, "Auto", StringComparison.OrdinalIgnoreCase);
                    candidates.Add((source, GetEffectiveArtworkRole(source, roles),
                        probe.PixelHeight > 0 ? (double)probe.PixelWidth / probe.PixelHeight : 1, isManual));
                }
                catch { }
            }
            if (candidates.Count == 0) return (null, null, null, null, null, null, null, null, null, "画像を開けません");
            var spineCardCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "SpineCard");
            // Supplementary scans remain in the gallery, but must not become
            // case textures through Front/Back/Disc aspect-ratio fallbacks.
            candidates.RemoveAll(candidate => candidate.Role is "LinerNotes" or "SpineCard" or "Page" or "Flyer" or "Poster");
            if (candidates.Count == 0)
            {
                if (spineCardCandidate.Source is null)
                    return (null, null, null, null, null, null, null, null, null, "3Dケース用の画像はありません");
                try
                {
                    return (null, null, null, null, null, null, null, null,
                        ApplySpineCardFoldOverride(Album.Path, spineCardCandidate.Source,
                            LoadBitmap(spineCardCandidate.Source, targetWidth)), spineCardCandidate.Source.Description);
                }
                catch { return (null, null, null, null, null, null, null, null, null, "画像を開けません"); }
            }

            var insideFrontCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "FrontInside");
            var frontSpreadCandidate = candidates.FirstOrDefault(candidate =>
                candidate.Role is "FrontSpread" or "FrontSpreadReversed" or "FrontSpreadVertical");
            if (frontSpreadCandidate.Source is null)
                frontSpreadCandidate = candidates.FirstOrDefault(candidate => !candidate.IsManual && candidate.Aspect >= 1.62);
            var standaloneFrontCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Front");
            var backWithSpinesCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "BackWithSpines");
            var backCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Back");
            if (backCandidate.Source is null) backCandidate = backWithSpinesCandidate;
            if (backCandidate.Source is null)
                backCandidate = candidates.LastOrDefault(candidate => !Equals(candidate.Source, standaloneFrontCandidate.Source)
                    && !Equals(candidate.Source, frontSpreadCandidate.Source)
                    && !candidate.IsManual && candidate.Role == "Other"
                    && candidate.Aspect is >= 1.12 and <= 1.58);
            var spineCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Spine");
            var leftSpineCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "LeftSpine");
            if (leftSpineCandidate.Source is null) leftSpineCandidate = spineCandidate;
            var rightSpineCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "RightSpine");
            var inlayCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Inlay");
            var discCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Disc");
            var twoDiscCandidate = candidates.FirstOrDefault(candidate => candidate.Role == "Disc2");
            if (discCandidate.Source is null)
                discCandidate = candidates.LastOrDefault(candidate => !Equals(candidate.Source, standaloneFrontCandidate.Source)
                    && !Equals(candidate.Source, frontSpreadCandidate.Source)
                    && candidate.Aspect is >= 0.86 and <= 1.14);

            BitmapSource? LoadRole(
                (AlbumImageSource Source, string Role, double Aspect, bool IsManual) candidate, string role)
            {
                if (candidate.Source is null) return null;
                try
                {
                    // A front scan often contains the full booklet spread. Decode it at twice the
                    // requested output width so that the right-half crop retains full resolution.
                    var isFrontSpread = candidate.Role is "FrontSpread" or "FrontSpreadReversed" or "FrontSpreadVertical"
                        || (!candidate.IsManual && candidate.Aspect >= 1.62);
                    var isVerticalFrontSpread = candidate.Role == "FrontSpreadVertical";
                    var isReversedFrontSpread = candidate.Role == "FrontSpreadReversed";
                    var decodeWidth = !isVerticalFrontSpread && ((role == "Front" && isFrontSpread)
                        || role == "InsideFrontFromSpread")
                        ? checked(targetWidth * 2)
                        : targetWidth;
                    var bitmap = LoadBitmap(candidate.Source, decodeWidth);
                    if (role == "SpineCard")
                        return ApplySpineCardFoldOverride(Album.Path, candidate.Source, bitmap);
                    if (role == "Front" && isFrontSpread)
                        return CropArtwork(RearInsertArtwork.CropWhiteBorder(bitmap), isVerticalFrontSpread ? "TopHalf"
                            : isReversedFrontSpread ? "LeftHalf" : "RightHalf");
                    if (role == "InsideFrontFromSpread")
                        return CropArtwork(RearInsertArtwork.CropWhiteBorder(bitmap), isVerticalFrontSpread ? "BottomHalfRotated"
                            : isReversedFrontSpread ? "RightHalf" : "LeftHalf");
                    if (role == "Back" && (candidate.Role == "BackWithSpines"
                        || (!candidate.IsManual && candidate.Aspect > 1.08)))
                        return CropArtwork(bitmap, "BackPanel");
                    if (role is "Front" or "Back")
                        return RearInsertArtwork.CropWhiteBorder(bitmap);
                    if (role == "LeftSpine") return CropArtwork(bitmap, "LeftSpine");
                    if (role == "RightSpine") return CropArtwork(bitmap, "RightSpine");
                    if (role == "Spine" && candidate.Aspect > 0.35)
                        return CropArtwork(bitmap, "LeftSpine");
                    if (role == "Disc") return DiscArtwork.CropScannerMargin(bitmap);
                    return bitmap;
                }
                catch { return null; }
            }

            var standaloneFront = LoadRole(standaloneFrontCandidate, "Front");
            var spreadFront = LoadRole(frontSpreadCandidate, "Front");
            var useStandaloneFront = standaloneFront is not null && (spreadFront is null
                || standaloneFrontCandidate.IsManual || insideFrontCandidate.Source is not null
                || (long)standaloneFront.PixelWidth * standaloneFront.PixelHeight
                    > (long)spreadFront.PixelWidth * spreadFront.PixelHeight);
            var front = useStandaloneFront ? standaloneFront : spreadFront;
            var selectedFrontCandidate = useStandaloneFront ? standaloneFrontCandidate : frontSpreadCandidate;
            if (front is null)
            {
                selectedFrontCandidate = candidates[0];
                front = LoadRole(selectedFrontCandidate, "Front");
            }
            var insideFront = insideFrontCandidate.Source is not null
                ? LoadRole(insideFrontCandidate, "")
                : frontSpreadCandidate.Source is not null
                    ? LoadRole(frontSpreadCandidate, "InsideFrontFromSpread") : null;
            var back = LoadRole(backCandidate, "Back");
            var splitSpineCandidate = backWithSpinesCandidate.Source is not null
                ? backWithSpinesCandidate : backCandidate;
            var backContainsSpines = splitSpineCandidate.Source is not null
                && (splitSpineCandidate.Role == "BackWithSpines"
                    || (!splitSpineCandidate.IsManual && splitSpineCandidate.Aspect is >= 1.12 and <= 1.58));
            var leftSpine = leftSpineCandidate.Source is not null
                ? LoadRole(leftSpineCandidate, leftSpineCandidate.Role == "Spine" ? "Spine" : "")
                : backContainsSpines ? LoadRole(splitSpineCandidate, "LeftSpine") : null;
            var rightSpine = rightSpineCandidate.Source is not null
                ? LoadRole(rightSpineCandidate, "")
                : spineCandidate.Source is not null
                    ? LoadRole(spineCandidate, "Spine")
                    : backContainsSpines ? LoadRole(splitSpineCandidate, "RightSpine") : null;
            BitmapSource? disc = null;
            BitmapSource? secondDisc = null;
            if (twoDiscCandidate.Source is not null)
            {
                try
                {
                    // Horizontal scans need twice the decode width so each
                    // extracted half retains the requested 3D texture detail.
                    var decodeWidth = twoDiscCandidate.Aspect >= 1
                        ? checked(targetWidth * 2) : targetWidth;
                    (disc, secondDisc) = DiscArtwork.SplitTwoDiscs(
                        LoadBitmap(twoDiscCandidate.Source, decodeWidth));
                }
                catch { }
            }
            disc ??= LoadRole(discCandidate, "Disc");
            return (front, insideFront, back, leftSpine, rightSpine,
                LoadRole(inlayCandidate, "Inlay"), disc, secondDisc,
                LoadRole(spineCardCandidate, "SpineCard"),
                selectedFrontCandidate.Source?.Description ?? "画像はありません");
        }

        private static BitmapSource CreateHorizontalFrontSpread(BitmapSource insideFront, BitmapSource front)
        {
            var side = Math.Max(1, Math.Max(
                Math.Max(insideFront.PixelWidth, insideFront.PixelHeight),
                Math.Max(front.PixelWidth, front.PixelHeight)));
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawImage(insideFront, new Rect(0, 0, side, side));
                drawing.DrawImage(front, new Rect(side, 0, side, side));
            }
            var spread = new RenderTargetBitmap(side * 2, side, 96, 96, PixelFormats.Pbgra32);
            spread.Render(visual);
            spread.Freeze();
            return spread;
        }

        private static BitmapSource CropArtwork(BitmapSource source, string mode)
        {
            if (mode is "BackPanel" or "LeftSpine" or "RightSpine")
            {
                var regions = RearInsertArtwork.GetRegions(source, forceSpines: true);
                var region = mode == "LeftSpine" ? regions.Left : mode == "RightSpine" ? regions.Right : regions.Panel;
                return RearInsertArtwork.Crop(source, region ?? regions.Panel);
            }
            Int32Rect rectangle;
            if (mode == "RightHalf")
            {
                var side = Math.Min(source.PixelWidth / 2, source.PixelHeight);
                rectangle = new Int32Rect(source.PixelWidth - side, Math.Max(0, (source.PixelHeight - side) / 2), side, side);
            }
            else if (mode == "LeftHalf")
            {
                var side = Math.Min(source.PixelWidth / 2, source.PixelHeight);
                rectangle = new Int32Rect(0, Math.Max(0, (source.PixelHeight - side) / 2), side, side);
            }
            else if (mode is "TopHalf" or "BottomHalfRotated")
            {
                var side = Math.Min(source.PixelWidth, source.PixelHeight / 2);
                rectangle = new Int32Rect(Math.Max(0, (source.PixelWidth - side) / 2),
                    mode == "TopHalf" ? 0 : source.PixelHeight - side, side, side);
            }
            else if (mode == "CenterSquare")
            {
                var side = Math.Min(source.PixelWidth, source.PixelHeight);
                rectangle = new Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side);
            }
            else
            {
                var width = Math.Max(1, (int)Math.Round(source.PixelWidth * 0.04));
                rectangle = new Int32Rect(0, 0, width, source.PixelHeight);
            }
            var cropped = new CroppedBitmap(source, rectangle);
            cropped.Freeze();
            if (mode != "BottomHalfRotated") return cropped;
            var rotated = new TransformedBitmap(cropped, new RotateTransform(180));
            rotated.Freeze();
            return rotated;
        }
    }
}
