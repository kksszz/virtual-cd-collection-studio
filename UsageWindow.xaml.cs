using System.Windows;

namespace ZipMp3Player;

public partial class UsageWindow : Window
{
    internal PlaybackUsageEntry? SelectedForPlayback { get; private set; }

    internal UsageWindow(IReadOnlyList<PlaybackUsageEntry> entries)
    {
        InitializeComponent();
        var rows = entries.Select(entry => new UsageRow(entry)).ToList();
        UsageGrid.ItemsSource = rows;

        var totalSeconds = entries.Sum(entry => entry.TotalPlayedSeconds);
        var totalPlays = entries.Sum(entry => entry.PlayCount);
        var artistCount = entries.Select(entry => entry.Artist).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        var albumCount = entries.Select(entry => entry.Album).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        SummaryText.Text = LocalizationService.Select(
            $"{entries.Count}曲  •  {totalPlays:N0}回  •  累計 {FormatLongTime(totalSeconds)}",
            $"{entries.Count} tracks  •  {totalPlays:N0} plays  •  Total {FormatLongTime(totalSeconds)}");
        TotalTimeText.Text = FormatCompactTime(totalSeconds);
        TotalPlaysText.Text = LocalizationService.Select($"{totalPlays:N0} 回", $"{totalPlays:N0} plays");
        TrackCountText.Text = LocalizationService.Select($"{entries.Count:N0} 曲", $"{entries.Count:N0} tracks");
        ArtistCountText.Text = LocalizationService.Select($"{artistCount:N0} 組", $"{artistCount:N0}");
        AlbumCountText.Text = LocalizationService.Select($"{albumCount:N0} 枚", $"{albumCount:N0}");

        var favorite = entries.OrderByDescending(entry => entry.TotalPlayedSeconds).FirstOrDefault();
        FavoriteTrackText.Text = favorite?.Title ?? "—";
        FavoriteTrackDetailText.Text = favorite is null ? "" : $"{favorite.Artist}  •  {FormatCompactTime(favorite.TotalPlayedSeconds)}";

        TopArtistList.ItemsSource = CreateAggregateRows(entries
            .GroupBy(entry => entry.Artist, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new AggregateValue(group.Key, "", group.Sum(entry => entry.TotalPlayedSeconds), group.Sum(entry => entry.PlayCount)))
            .OrderByDescending(item => item.Seconds).Take(5).ToList());
        TopAlbumList.ItemsSource = CreateAggregateRows(entries
            .GroupBy(entry => new { entry.Album, entry.Artist })
            .Select(group => new AggregateValue(group.Key.Album, group.Key.Artist, group.Sum(entry => entry.TotalPlayedSeconds), group.Sum(entry => entry.PlayCount)))
            .OrderByDescending(item => item.Seconds).Take(5).ToList());
        TopTrackList.ItemsSource = entries.OrderByDescending(entry => entry.TotalPlayedSeconds).Take(5)
            .Select((entry, index) => new DashboardRow { Rank = $"{index + 1}", Name = entry.Title, Secondary = entry.Artist, Detail = FormatCompactTime(entry.TotalPlayedSeconds) }).ToList();
        RecentTrackList.ItemsSource = entries.Where(entry => entry.LastPlayedLocal is not null)
            .OrderByDescending(entry => entry.LastPlayedLocal).Take(5)
            .Select(entry => new DashboardRow { Name = entry.Title, Secondary = entry.Artist, Detail = entry.LastPlayedLocal?.ToString("MM/dd HH:mm") ?? "—" }).ToList();

        LocalizationService.Apply(this);
    }

    private static IReadOnlyList<DashboardRow> CreateAggregateRows(IReadOnlyList<AggregateValue> values)
    {
        var maximum = values.Count == 0 ? 0 : values.Max(value => value.Seconds);
        return values.Select(value => new DashboardRow
        {
            Name = value.Name, Secondary = value.Secondary,
            Detail = $"{FormatCompactTime(value.Seconds)}  •  {value.Plays:N0}{LocalizationService.Select("回", " plays")}",
            BarWidth = maximum <= 0 ? 0 : Math.Max(5, value.Seconds / maximum * 250)
        }).ToList();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UsageGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(UsageGrid, e.OriginalSource as DependencyObject)
            is not System.Windows.Controls.DataGridRow) return;
        if (UsageGrid.SelectedItem is not UsageRow row) return;
        SelectedForPlayback = row.Entry;
        DialogResult = true;
        e.Handled = true;
    }

    private static string FormatCompactTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalHours >= 1)
            return LocalizationService.Select($"{(int)span.TotalHours}時間 {span.Minutes}分", $"{(int)span.TotalHours}h {span.Minutes}m");
        return LocalizationService.Select($"{span.Minutes}分", $"{span.Minutes}m");
    }

    private static string FormatLongTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }

    private sealed record AggregateValue(string Name, string Secondary, double Seconds, int Plays);
    private sealed class DashboardRow
    {
        public string Rank { get; init; } = "";
        public string Name { get; init; } = "";
        public string Secondary { get; init; } = "";
        public string Detail { get; init; } = "";
        public double BarWidth { get; init; }
    }

    private sealed class UsageRow
    {
        public PlaybackUsageEntry Entry { get; }
        public string Artist { get; }
        public string Title { get; }
        public string Album { get; }
        public int PlayCount { get; }
        public string PlayedTime { get; }
        public string Duration { get; }
        public string LastPlayed { get; }
        public double PlayedSeconds { get; }
        public double DurationSeconds { get; }
        public DateTimeOffset? LastPlayedValue { get; }

        public UsageRow(PlaybackUsageEntry entry)
        {
            Entry = entry; Artist = entry.Artist; Title = entry.Title; Album = entry.Album; PlayCount = entry.PlayCount;
            PlayedSeconds = entry.TotalPlayedSeconds; DurationSeconds = entry.TrackDurationSeconds; LastPlayedValue = entry.LastPlayedLocal;
            PlayedTime = FormatLongTime(entry.TotalPlayedSeconds); Duration = FormatLongTime(entry.TrackDurationSeconds);
            LastPlayed = entry.LastPlayedLocal?.ToString("yyyy/MM/dd HH:mm") ?? "—";
        }
    }
}
