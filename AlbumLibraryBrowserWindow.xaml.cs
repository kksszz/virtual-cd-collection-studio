using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

public sealed record AlbumLibraryBrowserItem(JewelCaseCoverFlowItem CaseItem, BitmapSource? TileCover,
    Func<int, CancellationToken, Task<JewelCaseCoverFlowItem>>? LoadCaseItem = null, bool IsFavorite = false,
    int TrackCount = 0)
{
    public string Key => CaseItem.Key;
    public string Title => CaseItem.Title;
    public string Artist => CaseItem.Artist;
    public bool IsPlaying => CaseItem.IsPlaying;
}

public sealed record AlbumBrowserPlaybackState(string TrackTitle, bool IsPlaying, bool HasTrack, double Volume);
public enum AlbumBrowserSortMode { Artist, Album }
public sealed record AlbumBrowserInitialFilter(string Key, string Label);
public sealed class AlbumBrowserVolumeChangedEventArgs(double volume) : EventArgs
{
    public double Volume { get; } = volume;
}
public sealed class AlbumBrowserTrackRequestedEventArgs(string albumKey, int trackIndex) : EventArgs
{
    public string AlbumKey { get; } = albumKey;
    public int TrackIndex { get; } = trackIndex;
}

public partial class AlbumLibraryBrowserWindow : Window
{
    private readonly ObservableCollection<AlbumLibraryBrowserItem> _items;
    private readonly ICollectionView _view;
    private bool _synchronizingSelection;
    private CancellationTokenSource? _artworkLoadCancellation;
    private readonly Func<AlbumBrowserPlaybackState>? _playbackStateProvider;
    private readonly DispatcherTimer _playbackStateTimer;
    private readonly DispatcherTimer _tileScrollTimer;
    private bool _updatingPlaybackControls;
    private ScrollViewer? _tileScrollViewer;
    private double _tileScrollTarget;
    private bool _animatingTileScroll;
    private string _initialFilterKey = "All";
    private double _tileSize = 246;
    private readonly Random _attractRandom = new();
    private CancellationTokenSource? _attractCancellation;
    private readonly Queue<string> _recentAttractKeys = new();
    private bool _attractMode;
    private bool _attractTransitionPending;

    public string? SelectedKey { get; private set; }
    public AlbumBrowserSortMode SortMode { get; private set; }
    public bool IsAttractMode => _attractMode;
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? ItemActivated;
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? DiscActivated;
    public event EventHandler? PreviousTrackRequested;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? NextTrackRequested;
    public event EventHandler<AlbumBrowserVolumeChangedEventArgs>? VolumeChangedRequested;
    public event EventHandler<AlbumBrowserTrackRequestedEventArgs>? AttractTrackRequested;

    public AlbumLibraryBrowserWindow(IReadOnlyList<AlbumLibraryBrowserItem> items, string? selectedKey,
        Func<AlbumBrowserPlaybackState>? playbackStateProvider = null,
        AlbumBrowserSortMode sortMode = AlbumBrowserSortMode.Artist,
        string? initialFilterText = null)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _playbackStateProvider = playbackStateProvider;
        _playbackStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _playbackStateTimer.Tick += (_, _) => UpdatePlaybackControls();
        _tileScrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _tileScrollTimer.Tick += TileScrollTimer_Tick;
        _items = new ObservableCollection<AlbumLibraryBrowserItem>(items);
        CoverFlow.PlaybackActiveProvider = key =>
            (_playbackStateProvider?.Invoke().IsPlaying ?? false)
            && _items.Any(item => item.IsPlaying
                && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = MatchesFilter;
        TileList.ItemsSource = _view;
        TileList.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (TileList.ItemContainerGenerator.Status ==
                System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) ApplyTileSize();
        };
        InitialFilterList.ItemsSource = BuildInitialFilters();
        SortMode = sortMode;
        BrowserSortCombo.SelectedIndex = sortMode == AlbumBrowserSortMode.Album ? 1 : 0;
        ApplySort(false);
        SelectedKey = selectedKey;
        if (!string.IsNullOrWhiteSpace(initialFilterText))
        {
            FilterBox.Text = initialFilterText;
            FilterBox.CaretIndex = FilterBox.Text.Length;
        }
        RefreshCoverFlow();
        SelectTile(selectedKey);
        ShowTiles();
        Loaded += (_, _) =>
        {
            if (TileList.SelectedItem is not null) TileList.ScrollIntoView(TileList.SelectedItem);
            _tileScrollViewer = FindVisualChild<ScrollViewer>(TileList);
            _tileScrollTarget = _tileScrollViewer?.VerticalOffset ?? 0;
            UpdateInitialFilterButtons();
            ApplyTileSize();
            TileList.Focus();
            QueueNearbyArtwork();
            UpdatePlaybackControls();
            _playbackStateTimer.Start();
        };
        Closed += (_, _) =>
        {
            StopAttractMode();
            _playbackStateTimer.Stop();
            _tileScrollTimer.Stop();
            _artworkLoadCancellation?.Cancel();
        };
    }

    private void AttractMode_Click(object sender, RoutedEventArgs e)
    {
        if (_attractMode) StopAttractMode();
        else StartAttractMode();
    }

    private void StartAttractMode()
    {
        if (!_view.Cast<AlbumLibraryBrowserItem>().Any(item => item.TrackCount > 0)) return;
        _attractMode = true;
        AttractModeButton.Content = LocalizationService.Select("■ Attract停止", "■ Stop Attract");
        AttractModeButton.Background = new SolidColorBrush(Color.FromRgb(36, 104, 75));
        AttractStatusText.Visibility = Visibility.Visible;
        AttractStatusText.Text = LocalizationService.Select("Attractモード：ルーレット開始…", "Attract: roulette starting…");
        _ = SelectAndPlayAttractAlbumAsync();
    }

    private void StopAttractMode()
    {
        _attractMode = false;
        _attractTransitionPending = false;
        _attractCancellation?.Cancel();
        _attractCancellation?.Dispose();
        _attractCancellation = null;
        if (AttractModeButton is not null)
        {
            AttractModeButton.Content = LocalizationService.Select("✦ Attract開始", "✦ Start Attract");
            AttractModeButton.ClearValue(BackgroundProperty);
        }
        if (AttractStatusText is not null) AttractStatusText.Visibility = Visibility.Collapsed;
    }

    public bool AdvanceAttractMode()
    {
        if (!_attractMode) return false;
        if (!_attractTransitionPending) _ = SelectAndPlayAttractAlbumAsync();
        return true;
    }

    private async Task SelectAndPlayAttractAlbumAsync()
    {
        if (!_attractMode || _attractTransitionPending) return;
        _attractTransitionPending = true;
        _attractCancellation?.Cancel();
        _attractCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _attractCancellation = cancellation;
        try
        {
            var candidates = _view.Cast<AlbumLibraryBrowserItem>().Where(item => item.TrackCount > 0).ToList();
            if (candidates.Count == 0) { StopAttractMode(); return; }
            var recent = _recentAttractKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var preferred = candidates.Where(item => !recent.Contains(item.Key)
                && !string.Equals(item.Key, SelectedKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var pool = preferred.Count > 0 ? preferred : candidates.Where(item =>
                !string.Equals(item.Key, SelectedKey, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 0) pool = candidates;
            var item = pool[_attractRandom.Next(pool.Count)];
            var trackIndex = _attractRandom.Next(item.TrackCount);
            if (CoverFlow.IsVisible)
            {
                AttractStatusText.Text = LocalizationService.Select(
                    "Attractモード：選択候補の画像を準備中…",
                    "Attract: preparing the selected artwork…");
                item = await EnsureAttractArtworkAsync(item, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            await RunAttractRouletteAsync(candidates, item, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_attractMode) return;

            SelectionChanged?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(item.CaseItem));
            QueueNearbyArtwork();
            AttractStatusText.Text = LocalizationService.Select(
                $"Attractモード：{item.Artist} — {item.Title}",
                $"Attract: {item.Artist} — {item.Title}");

            await DelayAttractAsync(CoverFlow.IsVisible ? 720 : 480, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_attractMode) return;
            RememberAttractAlbum(item.Key, Math.Min(8, Math.Max(2, candidates.Count / 3)));
            AttractTrackRequested?.Invoke(this, new AlbumBrowserTrackRequestedEventArgs(item.Key, trackIndex));
            MarkPlaying(item.Key);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Attract mode failed: {ex}");
            AttractStatusText.Text = LocalizationService.Select(
                $"Attractモードを続行できませんでした: {ex.Message}",
                $"Attract mode could not continue: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_attractCancellation, cancellation))
            {
                _attractCancellation.Dispose();
                _attractCancellation = null;
            }
            _attractTransitionPending = false;
        }
    }

    private async Task<AlbumLibraryBrowserItem> EnsureAttractArtworkAsync(
        AlbumLibraryBrowserItem item, CancellationToken cancellationToken)
    {
        if (item.LoadCaseItem is null) return item;
        _artworkLoadCancellation?.Cancel();
        _artworkLoadCancellation?.Dispose();
        _artworkLoadCancellation = null;
        var caseItem = await item.LoadCaseItem(1200, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var index = _items.ToList().FindIndex(candidate =>
            string.Equals(candidate.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return item with { CaseItem = caseItem };
        var current = _items[index];
        var loaded = current with
        {
            CaseItem = caseItem with { IsPlaying = current.IsPlaying }
        };
        _items[index] = loaded;
        // Replace the cached 3D model now, while the roulette has not started,
        // so its final destination always owns a ready texture.
        RefreshCoverFlow();
        return loaded;
    }

    private async Task RunAttractRouletteAsync(IReadOnlyList<AlbumLibraryBrowserItem> candidates,
        AlbumLibraryBrowserItem target, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return;
        var start = candidates.ToList().FindIndex(item =>
            string.Equals(item.Key, SelectedKey, StringComparison.OrdinalIgnoreCase));
        if (start < 0) start = _attractRandom.Next(candidates.Count);
        var targetIndex = candidates.ToList().FindIndex(item =>
            string.Equals(item.Key, target.Key, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) targetIndex = start;
        var direction = _attractRandom.Next(2) == 0 ? -1 : 1;
        var frames = Math.Clamp(18 + candidates.Count / 4, 20, 32);
        var index = start;

        for (var frame = 1; frame <= frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = frame / (double)frames;
            // Keep every hop inside the already-realized CoverFlow radius.
            // The old compressed full-library revolution jumped hundreds of
            // entries per frame, destroying each model before its ImageBrush
            // could render. Fast early double-steps retain the roulette feel
            // while reusing adjacent cached models.
            var step = progress < .42 && candidates.Count > 5 ? 2 : 1;
            index = (index + direction * step) % candidates.Count;
            if (index < 0) index += candidates.Count;
            PreviewAttractSelection(candidates[index]);
            AttractStatusText.Text = LocalizationService.Select(
                $"Attractモード：ルーレット {frame}/{frames}",
                $"Attract: roulette {frame}/{frames}");
            var delay = 42 + (int)(172 * progress * progress);
            await DelayAttractAsync(delay, cancellationToken);
        }

        // The roulette is deliberately local for texture reuse. Settle on the
        // preloaded random target only after the wheel has slowed.
        PreviewAttractSelection(target);
    }

    private Task DelayAttractAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var completion = new TaskCompletionSource(TaskCreationOptions.None);
        var timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        CancellationTokenRegistration registration = default;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            registration.Dispose();
            completion.TrySetResult();
        };
        registration = cancellationToken.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            timer.Stop();
            registration.Dispose();
            completion.TrySetCanceled(cancellationToken);
        })));
        timer.Start();
        return completion.Task;
    }

    private void PreviewAttractSelection(AlbumLibraryBrowserItem item)
    {
        SelectedKey = item.Key;
        SelectTile(item.Key);
        CoverFlow.SelectByKey(item.Key);
        if (!TileList.IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (TileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container
                || container.RenderTransform is not ScaleTransform scale) return;
            if (scale.IsFrozen)
            {
                scale = scale.Clone();
                container.RenderTransform = scale;
            }
            var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1.09, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.09, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        }));
    }

    private void RememberAttractAlbum(string key, int capacity)
    {
        _recentAttractKeys.Enqueue(key);
        while (_recentAttractKeys.Count > capacity) _recentAttractKeys.Dequeue();
    }

    private void TileSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tileSize = e.NewValue;
        if (TileSizeText is not null) TileSizeText.Text = $"{e.NewValue:0}";
        if (TileList is not null) ApplyTileSize();
    }

    private void ApplyTileSize()
    {
        if (TileList is null) return;
        foreach (var item in TileList.Items)
        {
            if (TileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) continue;
            container.Width = _tileSize;
            container.Height = _tileSize + 46;
        }
    }

    private static IReadOnlyList<AlbumBrowserInitialFilter> BuildInitialFilters()
    {
        var filters = new List<AlbumBrowserInitialFilter>
        {
            new("All", LocalizationService.Select("すべて", "All")),
            new("Favorite", "★"),
            new("Number", "#")
        };
        filters.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(letter =>
            new AlbumBrowserInitialFilter("Latin:" + letter, letter.ToString())));
        foreach (var kana in new[] { "あ", "か", "さ", "た", "な", "は", "ま", "や", "ら", "わ" })
            filters.Add(new AlbumBrowserInitialFilter("Kana:" + kana, kana));
        filters.Add(new AlbumBrowserInitialFilter("Japanese", LocalizationService.Select("漢字", "Kanji")));
        filters.Add(new AlbumBrowserInitialFilter("Other", LocalizationService.Select("他", "Other")));
        return filters;
    }

    private void InitialFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }
            || string.Equals(key, _initialFilterKey, StringComparison.Ordinal)) return;
        _initialFilterKey = key;
        _view.Refresh();
        RefreshCoverFlow();
        AnimateTileRefresh();
        UpdateInitialFilterButtons();
    }

    private void UpdateInitialFilterButtons()
    {
        foreach (var button in FindVisualChildren<Button>(InitialFilterList))
        {
            var selected = string.Equals(button.Tag?.ToString(), _initialFilterKey, StringComparison.Ordinal);
            button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(194, 203, 214));
            button.Background = selected
                ? new SolidColorBrush(Color.FromRgb(42, 108, 151))
                : new SolidColorBrush(Color.FromRgb(35, 44, 55));
            button.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(113, 187, 235))
                : new SolidColorBrush(Color.FromRgb(64, 76, 90));
        }
    }

    private void BrowserSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is null || BrowserSortCombo.SelectedItem is not ComboBoxItem selected) return;
        SortMode = string.Equals(selected.Tag?.ToString(), "Album", StringComparison.Ordinal)
            ? AlbumBrowserSortMode.Album : AlbumBrowserSortMode.Artist;
        ApplySort(true);
    }

    private void ApplySort(bool animate)
    {
        var selectedKey = SelectedKey;
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();
            if (SortMode == AlbumBrowserSortMode.Artist)
            {
                _view.SortDescriptions.Add(new SortDescription(nameof(AlbumLibraryBrowserItem.Artist), ListSortDirection.Ascending));
                _view.SortDescriptions.Add(new SortDescription(nameof(AlbumLibraryBrowserItem.Title), ListSortDirection.Ascending));
            }
            else
            {
                _view.SortDescriptions.Add(new SortDescription(nameof(AlbumLibraryBrowserItem.Title), ListSortDirection.Ascending));
                _view.SortDescriptions.Add(new SortDescription(nameof(AlbumLibraryBrowserItem.Artist), ListSortDirection.Ascending));
            }
        }
        if (!IsInitialized) return;
        RefreshCoverFlow();
        SelectTile(selectedKey);
        UpdateInitialFilterButtons();
        if (animate) AnimateTileRefresh();
    }

    private void AnimateTileRefresh()
    {
        if (!TileList.IsVisible) return;
        TileList.BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void TileList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _tileScrollViewer ??= FindVisualChild<ScrollViewer>(TileList);
        if (_tileScrollViewer is null || _tileScrollViewer.ScrollableHeight <= 0) return;
        if (!_tileScrollTimer.IsEnabled) _tileScrollTarget = _tileScrollViewer.VerticalOffset;
        _tileScrollTarget = Math.Clamp(_tileScrollTarget - (e.Delta * .9), 0, _tileScrollViewer.ScrollableHeight);
        if (!_tileScrollTimer.IsEnabled) _tileScrollTimer.Start();
        e.Handled = true;
    }

    private void TileScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_tileScrollViewer is null || !TileList.IsVisible)
        {
            _tileScrollTimer.Stop();
            return;
        }
        _tileScrollTarget = Math.Clamp(_tileScrollTarget, 0, _tileScrollViewer.ScrollableHeight);
        var difference = _tileScrollTarget - _tileScrollViewer.VerticalOffset;
        if (Math.Abs(difference) < .35)
        {
            _animatingTileScroll = true;
            _tileScrollViewer.ScrollToVerticalOffset(_tileScrollTarget);
            _animatingTileScroll = false;
            _tileScrollTimer.Stop();
            return;
        }
        _animatingTileScroll = true;
        _tileScrollViewer.ScrollToVerticalOffset(_tileScrollViewer.VerticalOffset + difference * .22);
        _animatingTileScroll = false;
    }

    private void TileList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_animatingTileScroll && !_tileScrollTimer.IsEnabled)
            _tileScrollTarget = e.VerticalOffset;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private void UpdatePlaybackControls()
    {
        var state = _playbackStateProvider?.Invoke()
            ?? new AlbumBrowserPlaybackState(LocalizationService.Select("停止中", "Stopped"), false, false, .8);
        _updatingPlaybackControls = true;
        try
        {
            NowPlayingTitleText.Text = string.IsNullOrWhiteSpace(state.TrackTitle)
                ? LocalizationService.Select("停止中", "Stopped") : state.TrackTitle;
            BrowserPlayPauseButton.Content = state.IsPlaying
                ? LocalizationService.Select("⏸ 一時停止", "⏸ Pause")
                : LocalizationService.Select("▶ 再生", "▶ Play");
            PreviousTrackButton.IsEnabled = state.HasTrack;
            NextTrackButton.IsEnabled = state.HasTrack;
            BrowserVolumeSlider.Value = Math.Clamp(state.Volume, BrowserVolumeSlider.Minimum, BrowserVolumeSlider.Maximum);
            BrowserVolumeText.Text = $"{state.Volume * 100:0}%";
        }
        finally { _updatingPlaybackControls = false; }
    }

    private void PreviousTrack_Click(object sender, RoutedEventArgs e) => PreviousTrackRequested?.Invoke(this, EventArgs.Empty);
    private void PlayPause_Click(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    private void NextTrack_Click(object sender, RoutedEventArgs e) => NextTrackRequested?.Invoke(this, EventArgs.Empty);

    private void BrowserVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrowserVolumeText is not null) BrowserVolumeText.Text = $"{e.NewValue * 100:0}%";
        if (!_updatingPlaybackControls)
            VolumeChangedRequested?.Invoke(this, new AlbumBrowserVolumeChangedEventArgs(e.NewValue));
    }

    private bool MatchesFilter(object item)
    {
        if (item is not AlbumLibraryBrowserItem album) return false;
        var query = FilterBox?.Text?.Trim();
        return MatchesInitialFilter(album) && (string.IsNullOrEmpty(query)
            || album.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || album.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool MatchesInitialFilter(AlbumLibraryBrowserItem album)
    {
        if (_initialFilterKey == "All") return true;
        if (_initialFilterKey == "Favorite") return album.IsFavorite;
        var value = SortMode == AlbumBrowserSortMode.Artist ? album.Artist : album.Title;
        var initial = FirstSearchCharacter(value);
        if (initial is null) return _initialFilterKey == "Other";
        if (_initialFilterKey == "Number") return char.IsDigit(initial.Value);
        if (_initialFilterKey.StartsWith("Latin:", StringComparison.Ordinal))
            return GetLatinInitial(initial.Value) is { } latin && latin == _initialFilterKey[^1];
        if (_initialFilterKey.StartsWith("Kana:", StringComparison.Ordinal))
            return GetKanaRow(initial.Value) is { } row && row == _initialFilterKey[^1];
        if (_initialFilterKey == "Japanese") return IsCjkIdeograph(initial.Value);
        return _initialFilterKey == "Other"
            && !char.IsDigit(initial.Value)
            && GetLatinInitial(initial.Value) is null
            && GetKanaRow(initial.Value) is null
            && !IsCjkIdeograph(initial.Value);
    }

    private static char? FirstSearchCharacter(string value)
    {
        foreach (var character in value.Normalize(NormalizationForm.FormKC).Trim())
            if (char.IsLetterOrDigit(character)) return character;
        return null;
    }

    private static char? GetLatinInitial(char character)
    {
        var decomposed = character.ToString().Normalize(NormalizationForm.FormD);
        var first = char.ToUpperInvariant(decomposed[0]);
        return first is >= 'A' and <= 'Z' ? first : null;
    }

    private static char? GetKanaRow(char character)
    {
        if (character is >= '\u30A1' and <= '\u30F6') character = (char)(character - 0x60);
        if ("ぁあぃいぅうぇえぉおゔ".Contains(character)) return 'あ';
        if ("かがきぎくぐけげこご".Contains(character)) return 'か';
        if ("さざしじすずせぜそぞ".Contains(character)) return 'さ';
        if ("ただちぢっつづてでとど".Contains(character)) return 'た';
        if ("なにぬねの".Contains(character)) return 'な';
        if ("はばぱひびぴふぶぷへべぺほぼぽ".Contains(character)) return 'は';
        if ("まみむめも".Contains(character)) return 'ま';
        if ("ゃやゅゆょよ".Contains(character)) return 'や';
        if ("らりるれろ".Contains(character)) return 'ら';
        if ("ゎわゐゑをん".Contains(character)) return 'わ';
        return null;
    }

    private static bool IsCjkIdeograph(char character) =>
        character is >= '\u3400' and <= '\u4DBF'
        || character is >= '\u4E00' and <= '\u9FFF'
        || character is >= '\uF900' and <= '\uFAFF';

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_view is null) return;
        if (FilterClearButton is not null)
            FilterClearButton.Visibility = string.IsNullOrEmpty(FilterBox.Text)
                ? Visibility.Collapsed : Visibility.Visible;
        _view.Refresh();
        RefreshCoverFlow();
        AnimateTileRefresh();
        UpdateResultText();
    }

    private void FilterClear_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        FilterBox.Focus();
    }

    private void RefreshCoverFlow()
    {
        var visible = _view.Cast<AlbumLibraryBrowserItem>()
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        CoverFlow.SetItems(visible.Select(item => item.CaseItem with { CollectionPresentation = true }).ToList(), SelectedKey);
        if (visible.Count > 0 && !visible.Any(item => string.Equals(item.Key, SelectedKey, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedKey = visible[0].Key;
        }
        SelectTile(SelectedKey);
        UpdateResultText();
    }

    private void UpdateResultText()
    {
        if (ResultText is null) return;
        ResultText.Text = LocalizationService.Select($"{_view.Cast<object>().Count()} / {_items.Count} アルバム",
            $"{_view.Cast<object>().Count()} / {_items.Count} albums");
    }

    private void TileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection || TileList.SelectedItem is not AlbumLibraryBrowserItem item) return;
        SelectedKey = item.Key;
        CoverFlow.SelectByKey(item.Key);
        SelectionChanged?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(item.CaseItem));
        QueueNearbyArtwork();
    }

    private void CoverFlow_SelectionChanged(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        SelectedKey = e.Item.Key;
        SelectTile(e.Item.Key);
        SelectionChanged?.Invoke(this, e);
        QueueNearbyArtwork();
    }

    private void QueueNearbyArtwork()
    {
        _artworkLoadCancellation?.Cancel();
        _artworkLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _artworkLoadCancellation = cancellation;
        _ = LoadNearbyArtworkAsync(cancellation.Token);
    }

    private async Task LoadNearbyArtworkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(100, cancellationToken);
            var visible = _view.Cast<AlbumLibraryBrowserItem>().ToList();
            var selectedIndex = visible.FindIndex(item =>
                string.Equals(item.Key, SelectedKey, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0) return;
            var requests = Enumerable.Range(Math.Max(0, selectedIndex - 12),
                    Math.Min(visible.Count - 1, selectedIndex + 12) - Math.Max(0, selectedIndex - 12) + 1)
                .OrderBy(index => Math.Abs(index - selectedIndex))
                .Select(index => (Item: visible[index], Width: index == selectedIndex ? 1600 : 960))
                .Where(request => request.Item.LoadCaseItem is not null)
                .ToList();
            var loadedSinceRefresh = 0;
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var caseItem = await request.Item.LoadCaseItem!(request.Width, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var currentIndex = _items.IndexOf(request.Item);
                if (currentIndex < 0) continue;
                var current = _items[currentIndex];
                _items[currentIndex] = current with
                {
                    CaseItem = caseItem with { IsPlaying = current.IsPlaying }
                };
                loadedSinceRefresh++;
                // Show the selected high-resolution image immediately, then
                // update side cases in small batches to avoid rebuilding the
                // 3D scene after every individual file decode.
                if (request.Width == 1600 || loadedSinceRefresh >= 4)
                {
                    RefreshCoverFlow();
                    loadedSinceRefresh = 0;
                }
            }
            if (loadedSinceRefresh > 0) RefreshCoverFlow();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Album browser artwork load failed: {ex.Message}"); }
    }

    private void SelectTile(string? key)
    {
        if (key is null) return;
        var item = _items.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        _synchronizingSelection = true;
        TileList.SelectedItem = item;
        _synchronizingSelection = false;
        if (TileList.IsVisible) TileList.ScrollIntoView(item);
    }

    private void TileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(TileList, e.OriginalSource as DependencyObject)
            is not System.Windows.Controls.ListBoxItem) return;
        ActivateSelected();
        e.Handled = true;
    }

    private void CoverFlow_ItemActivated(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        SelectedKey = e.Item.Key;
        MarkPlaying(e.Item.Key);
        ItemActivated?.Invoke(this, e);
    }

    private void CoverFlow_DiscActivated(object? sender, JewelCaseCoverFlowSelectionChangedEventArgs e)
    {
        SelectedKey = e.Item.Key;
        var stopping = (_playbackStateProvider?.Invoke().IsPlaying ?? false)
            && _items.Any(item => item.IsPlaying
                && string.Equals(item.Key, e.Item.Key, StringComparison.OrdinalIgnoreCase));
        MarkPlaying(stopping ? string.Empty : e.Item.Key);
        DiscActivated?.Invoke(this, e);
    }

    private void ActivateSelected()
    {
        var item = _items.FirstOrDefault(candidate => string.Equals(candidate.Key, SelectedKey, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        MarkPlaying(item.Key);
        ItemActivated?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(item.CaseItem));
    }

    private void MarkPlaying(string key)
    {
        for (var index = 0; index < _items.Count; index++)
        {
            var current = _items[index];
            var playing = string.Equals(current.Key, key, StringComparison.OrdinalIgnoreCase);
            if (current.IsPlaying == playing) continue;
            _items[index] = current with { CaseItem = current.CaseItem with { IsPlaying = playing } };
        }
        RefreshCoverFlow();
        SelectTile(key);
    }

    private void TileMode_Click(object sender, RoutedEventArgs e) => ShowTiles();
    private void CoverFlowMode_Click(object sender, RoutedEventArgs e)
    {
        TileList.Visibility = Visibility.Collapsed;
        TileSizePanel.Visibility = Visibility.Collapsed;
        CoverFlow.RackPresentation = false;
        CoverFlow.Visibility = Visibility.Visible;
        CoverFlow.SelectByKey(SelectedKey ?? string.Empty);
        CoverFlow.Focus();
    }

    private void RackMode_Click(object sender, RoutedEventArgs e)
    {
        TileList.Visibility = Visibility.Collapsed;
        TileSizePanel.Visibility = Visibility.Collapsed;
        CoverFlow.RackPresentation = true;
        CoverFlow.Visibility = Visibility.Visible;
        CoverFlow.SelectByKey(SelectedKey ?? string.Empty);
        CoverFlow.Focus();
    }

    private void ShowTiles()
    {
        CoverFlow.Visibility = Visibility.Collapsed;
        TileList.Visibility = Visibility.Visible;
        TileSizePanel.Visibility = Visibility.Visible;
        if (TileList.SelectedItem is not null) TileList.ScrollIntoView(TileList.SelectedItem);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _tileScrollViewer ??= FindVisualChild<ScrollViewer>(TileList);
            _tileScrollTarget = _tileScrollViewer?.VerticalOffset ?? 0;
        }));
        TileList.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && !FilterBox.IsKeyboardFocusWithin) { ActivateSelected(); e.Handled = true; }
        else if (e.Key == Key.Space && !FilterBox.IsKeyboardFocusWithin) { PlayPauseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { PreviousTrackRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { NextTrackRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { FilterBox.Focus(); FilterBox.SelectAll(); e.Handled = true; }
    }
}
