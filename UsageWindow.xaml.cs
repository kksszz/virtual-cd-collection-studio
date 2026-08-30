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
        SummaryText.Text = $"{entries.Count}曲  •  {totalPlays:N0}回  •  累計 {FormatLongTime(totalSeconds)}";
        if (LocalizationService.IsEnglish)
            SummaryText.Text = $"{entries.Count} tracks  •  {totalPlays:N0} plays  •  Total {FormatLongTime(totalSeconds)}";
        LocalizationService.Apply(this);
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

    private static string FormatLongTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
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
            Entry = entry;
            Artist = entry.Artist;
            Title = entry.Title;
            Album = entry.Album;
            PlayCount = entry.PlayCount;
            PlayedSeconds = entry.TotalPlayedSeconds;
            DurationSeconds = entry.TrackDurationSeconds;
            LastPlayedValue = entry.LastPlayedLocal;
            PlayedTime = FormatLongTime(entry.TotalPlayedSeconds);
            Duration = FormatLongTime(entry.TrackDurationSeconds);
            LastPlayed = entry.LastPlayedLocal?.ToString("yyyy/MM/dd HH:mm") ?? "—";
        }
    }
}
