using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZipMp3Player;

internal sealed record FavoriteTrackEntry(ZipAlbum Album, ZipTrack Track, bool AlbumFavorite, bool TrackFavorite)
{
    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string AlbumTitle => Track.Album;
    public string DurationText => Track.DurationText;
}

public partial class FavoritesWindow : Window
{
    private readonly IReadOnlyList<FavoriteTrackEntry> _entries;
    internal IReadOnlyList<FavoriteTrackEntry> PlaybackQueue { get; private set; } = [];
    internal int PlaybackIndex { get; private set; }

    internal FavoritesWindow(IReadOnlyList<FavoriteTrackEntry> entries)
    {
        _entries = entries;
        InitializeComponent();
        RefreshRows();
        LocalizationService.Apply(this);
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshRows();
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshRows();
    private void RefreshRows()
    {
        if (FavoritesGrid is null || SearchTextBox is null) return;
        var query = SearchTextBox.Text.Trim();
        var rows = _entries.Where(entry => FavoriteModeCombo.SelectedIndex == 1 ? entry.AlbumFavorite : entry.TrackFavorite)
            .Where(entry => query.Length == 0 || new[] { entry.Title, entry.Artist, entry.AlbumTitle }
                .Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
        FavoritesGrid.ItemsSource = rows;
        FavoritesGrid.SelectedIndex = rows.Count > 0 ? 0 : -1;
        SummaryText.Text = LocalizationService.Select($"{rows.Count} 曲（ライブラリ内のお気に入り）", $"{rows.Count} tracks (favorites in your library)");
        EmptyText.Text = LocalizationService.Select(
            query.Length > 0 ? "検索に一致するお気に入りはありません" : "お気に入りはまだありません。\n曲やアルバムの ☆ を押して登録してください。",
            query.Length > 0 ? "No matching favorites." : "No favorites yet.\nClick ☆ beside a track or album to add one.");
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayAllButton.IsEnabled = rows.Any(entry => entry.Track.IsSupported);
        UpdateSelection();
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        if (PlaySelectedButton is not null)
            PlaySelectedButton.IsEnabled = FavoritesGrid.SelectedItem is FavoriteTrackEntry { Track.IsSupported: true };
    }
    private void PlayAll_Click(object sender, RoutedEventArgs e) => ChoosePlayback(false);
    private void PlaySelected_Click(object sender, RoutedEventArgs e) => ChoosePlayback(true);
    private void FavoritesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FavoritesGrid, e.OriginalSource as DependencyObject) is DataGridRow)
            ChoosePlayback(true);
    }
    private void FavoritesGrid_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(FavoritesGrid, e.OriginalSource as DependencyObject) is DataGridRow row)
            FavoritesGrid.SelectedItem = row.Item;
    }
    private void TrackContextMenu_Opened(object sender, RoutedEventArgs e)
        => TrackPropertiesMenuItem.IsEnabled = FavoritesGrid.SelectedItem is FavoriteTrackEntry;
    private void TrackProperties_Click(object sender, RoutedEventArgs e)
    {
        if (FavoritesGrid.SelectedItem is FavoriteTrackEntry entry)
            new AlbumPropertiesWindow(entry.Track) { Owner = this }.ShowDialog();
    }
    private void FavoritesGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ChoosePlayback(true);
        e.Handled = true;
    }
    private void ChoosePlayback(bool fromSelection)
    {
        if (PreparePlayback(fromSelection)) DialogResult = true;
    }
    internal bool PreparePlayback(bool fromSelection)
    {
        // Read the view, not the source list: honor search and column sorting.
        var queue = FavoritesGrid.Items.Cast<FavoriteTrackEntry>().Where(entry => entry.Track.IsSupported).ToList();
        var index = fromSelection
            ? FavoritesGrid.SelectedItem is FavoriteTrackEntry selected ? queue.IndexOf(selected) : -1
            : 0;
        if (queue.Count == 0 || index < 0) return false;
        PlaybackQueue = queue;
        PlaybackIndex = index;
        return true;
    }
}
