using System.Windows;
using System.Windows.Controls;

namespace ZipMp3Player;

internal sealed class CueMetadataWindow : Window
{
    internal CueMetadata? SelectedMetadata { get; private set; }
    internal CueMetadataWindow(CueAlbumReader.Disc disc)
    {
        Title = "CUEの曲情報 — MusicBrainz"; Width = 900; Height = 650; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new CancellationTokenSource();
        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(130) });
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var status = new TextBlock { Text = "CD構成をMusicBrainzに照合しています…（音声・ローカルパスは送信しません）", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        grid.Children.Add(status);
        var candidates = new ListBox { DisplayMemberPath = nameof(CueMetadata.Description) };
        Grid.SetRow(candidates, 1); grid.Children.Add(candidates);
        var preview = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetRow(preview, 2); grid.Children.Add(preview);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "確認して反映", IsEnabled = false, Padding = new Thickness(15, 6, 15, 6), Margin = new Thickness(4) };
        var close = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(15, 6, 15, 6), Margin = new Thickness(4) };
        buttons.Children.Add(apply); buttons.Children.Add(close); Grid.SetRow(buttons, 3); grid.Children.Add(buttons);
        Content = grid;
        candidates.SelectionChanged += (_, _) =>
        {
            var metadata = candidates.SelectedItem as CueMetadata;
            apply.IsEnabled = metadata is not null;
            preview.ItemsSource = metadata?.Tracks.Select((track, n) => new
            {
                Disc = metadata.DiscNumber, Track = n + 1, track.Title, track.Artist,
                Time = TimeSpan.FromSeconds(((n + 1 < disc.Tracks.Count ? disc.Tracks[n + 1].Frame : disc.Frames) - disc.Tracks[n].Frame) / 75.0).ToString(@"mm\:ss")
            }).ToList();
        };
        apply.Click += (_, _) =>
        {
            if (candidates.SelectedItem is not CueMetadata metadata) return;
            if (MessageBox.Show(this, $"{metadata.Description}\n\nこの{metadata.Tracks.Count}曲の情報を反映しますか？\nISO・BIN・CUEは変更せず、アプリ内に保存します。",
                "曲情報の確認", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            SelectedMetadata = metadata; DialogResult = true;
        };
        Closed += (_, _) => cancel.Cancel();
        Loaded += async (_, _) =>
        {
            try
            {
                var results = await CueMetadataLookup.SearchAsync(disc, cancel.Token);
                if (cancel.IsCancellationRequested) return;
                candidates.ItemsSource = results;
                status.Text = results.Count == 0 ? "一致する候補がありません。情報は変更していません。"
                    : $"候補 {results.Count}件。版・Disc番号・曲名を確認して選択してください。（提供: MusicBrainz）";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cancel.IsCancellationRequested) status.Text = "取得できませんでした（変更なし）: " + ex.Message; }
        };
    }
}
