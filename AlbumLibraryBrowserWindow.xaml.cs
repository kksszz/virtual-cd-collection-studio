using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

public sealed record AlbumLibraryBrowserItem(JewelCaseCoverFlowItem CaseItem, BitmapSource? TileCover,
    Func<int, CancellationToken, Task<JewelCaseCoverFlowItem>>? LoadCaseItem = null)
{
    public string Key => CaseItem.Key;
    public string Title => CaseItem.Title;
    public string Artist => CaseItem.Artist;
    public bool IsPlaying => CaseItem.IsPlaying;
}

public sealed record AlbumBrowserPlaybackState(string TrackTitle, bool IsPlaying, bool HasTrack, double Volume);
public sealed class AlbumBrowserVolumeChangedEventArgs(double volume) : EventArgs
{
    public double Volume { get; } = volume;
}

public partial class AlbumLibraryBrowserWindow : Window
{
    private readonly ObservableCollection<AlbumLibraryBrowserItem> _items;
    private readonly ICollectionView _view;
    private bool _synchronizingSelection;
    private CancellationTokenSource? _artworkLoadCancellation;
    private readonly Func<AlbumBrowserPlaybackState>? _playbackStateProvider;
    private readonly DispatcherTimer _playbackStateTimer;
    private bool _updatingPlaybackControls;

    public string? SelectedKey { get; private set; }
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? ItemActivated;
    public event EventHandler? PreviousTrackRequested;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? NextTrackRequested;
    public event EventHandler<AlbumBrowserVolumeChangedEventArgs>? VolumeChangedRequested;

    public AlbumLibraryBrowserWindow(IReadOnlyList<AlbumLibraryBrowserItem> items, string? selectedKey,
        Func<AlbumBrowserPlaybackState>? playbackStateProvider = null)
    {
        InitializeComponent();
        LocalizationService.Apply(this);
        _playbackStateProvider = playbackStateProvider;
        _playbackStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _playbackStateTimer.Tick += (_, _) => UpdatePlaybackControls();
        _items = new ObservableCollection<AlbumLibraryBrowserItem>(items);
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = MatchesFilter;
        TileList.ItemsSource = _view;
        SelectedKey = selectedKey;
        RefreshCoverFlow();
        SelectTile(selectedKey);
        ShowTiles();
        Loaded += (_, _) =>
        {
            if (TileList.SelectedItem is not null) TileList.ScrollIntoView(TileList.SelectedItem);
            TileList.Focus();
            QueueNearbyArtwork();
            UpdatePlaybackControls();
            _playbackStateTimer.Start();
        };
        Closed += (_, _) =>
        {
            _playbackStateTimer.Stop();
            _artworkLoadCancellation?.Cancel();
        };
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
        return string.IsNullOrEmpty(query)
            || album.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || album.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_view is null) return;
        _view.Refresh();
        RefreshCoverFlow();
        UpdateResultText();
    }

    private void RefreshCoverFlow()
    {
        var visible = _view.Cast<AlbumLibraryBrowserItem>().ToList();
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
        CoverFlow.Visibility = Visibility.Visible;
        CoverFlow.SelectByKey(SelectedKey ?? string.Empty);
        CoverFlow.Focus();
    }

    private void ShowTiles()
    {
        CoverFlow.Visibility = Visibility.Collapsed;
        TileList.Visibility = Visibility.Visible;
        if (TileList.SelectedItem is not null) TileList.ScrollIntoView(TileList.SelectedItem);
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
