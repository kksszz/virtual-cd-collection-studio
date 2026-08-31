using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "ZipMp3Player-ThemeTest-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", data);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var sample = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_ARCHIVE") ?? string.Empty;
        var pixels = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
        var testImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        VerifyBlankCaseArtwork(testImage);
        VerifyArtworkRoleSelection(data, testImage);
        VerifyCoverFlowPan(testImage);

        var entryType = typeof(MainWindow).Assembly.GetType("ZipMp3Player.PlaybackUsageEntry")!;
        var entryList = Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
        for (var index = 0; index < 5; index++)
        {
            var entry = Activator.CreateInstance(entryType)!;
            entryType.GetProperty("Title")!.SetValue(entry, $"テスト曲 {index + 1}");
            entryType.GetProperty("Artist")!.SetValue(entry, $"アーティスト {index + 1}");
            entryType.GetProperty("Album")!.SetValue(entry, $"アルバム {index + 1}");
            entryType.GetProperty("TotalPlayedSeconds")!.SetValue(entry, 3600.0 - index * 400);
            entryType.GetProperty("PlayCount")!.SetValue(entry, 20 - index);
            entryType.GetProperty("LastPlayedLocal")!.SetValue(entry, DateTimeOffset.Now.AddMinutes(-index));
            ((System.Collections.IList)entryList).Add(entry);
        }
        var usageWindow = (Window)Activator.CreateInstance(typeof(UsageWindow), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [entryList], culture: null)!;
        (Window Window, bool Show)[] windows =
        [
            (new MainWindow { ShowInTaskbar = false, WindowState = WindowState.Normal }, true),
            (new SettingsWindow([@"C:\Music", @"C:\HiddenMusic"], [@"C:\HiddenMusic"], true, data) { ShowInTaskbar = false }, true),
            (usageWindow, true),
            (new TagEditorWindow(new ZipAlbum
            {
                Path = @"C:\Music\Batch.zip.mp3",
                Tracks =
                [
                    new ZipTrack { FileName = "01.mp3", SourcePath = @"C:\Music\Batch.zip.mp3", IsArchiveEntry = true, Title = "One", Artist = "Old A", Album = "Old Album", TrackNumber = 1 },
                    new ZipTrack { FileName = "10.mp3", SourcePath = @"C:\Music\Batch.zip.mp3", IsArchiveEntry = true, Title = "Ten", Artist = "Old B", Album = "Old Album", TrackNumber = 10 },
                    new ZipTrack { FileName = "02.mp3", SourcePath = @"C:\Music\Batch.zip.mp3", IsArchiveEntry = true, Title = "Two", Artist = "Old C", Album = "Old Album", TrackNumber = 2 }
                ]
            }) { ShowInTaskbar = false }, true),
            (new ArtworkLookupWindow("", "") { ShowInTaskbar = false }, false)
        ];
        var failures = new List<string>();
        foreach (var item in windows)
        {
            var window = item.Window;
            if (item.Show) window.Show();
            else
            {
                window.Measure(new Size(window.Width, window.Height));
                window.Arrange(new Rect(0, 0, window.Width, window.Height));
            }
            window.UpdateLayout();
            if (window is MainWindow mainWindow)
            {
                var extensionPanel = (ScrollViewer)mainWindow.FindName("ExtensionPanel");
                var extensionToggle = (Button)mainWindow.FindName("ExtensionPanelToggleButton");
                if (extensionPanel.Visibility != Visibility.Visible || !Equals(extensionToggle.Content, "▶"))
                    throw new InvalidOperationException("Extension panel default-open test failed.");
                var visualizerMode = (ComboBox)mainWindow.FindName("VisualizerModeCombo");
                if ((visualizerMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "Spectrum"
                    || ((FrameworkElement)mainWindow.FindName("SpectrumDisplay")).Visibility != Visibility.Visible
                    || ((FrameworkElement)mainWindow.FindName("WaveformDisplay")).Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Spectrum visualizer default test failed.");
                var artworkColumn = (ColumnDefinition)mainWindow.FindName("ArtworkColumn");
                var lyricsColumn = (ColumnDefinition)mainWindow.FindName("LyricsColumn");
                var imageLyricsTotal = artworkColumn.ActualWidth + lyricsColumn.ActualWidth;
                if (imageLyricsTotal <= 0 || Math.Abs(artworkColumn.ActualWidth / imageLyricsTotal - 0.6) > 0.03)
                    throw new InvalidOperationException("Default image/lyrics 60:40 ratio test failed.");
                var trackContentRow = (RowDefinition)mainWindow.FindName("TrackContentRow");
                var imageBottomRow = (RowDefinition)mainWindow.FindName("ImageBottomRow");
                var verticalPanelTotal = trackContentRow.ActualHeight + imageBottomRow.ActualHeight;
                if (verticalPanelTotal <= 0 || Math.Abs(imageBottomRow.ActualHeight / verticalPanelTotal - 0.56) > 0.03)
                    throw new InvalidOperationException("Default bottom image panel 56 percent ratio test failed.");
                var artworkToolbarRow = (RowDefinition)mainWindow.FindName("ArtworkToolbarRow");
                var lyricsToolbarRow = (RowDefinition)mainWindow.FindName("LyricsToolbarRow");
                if (Math.Abs(artworkToolbarRow.ActualHeight - lyricsToolbarRow.ActualHeight) > 0.5)
                    throw new InvalidOperationException("Image and lyrics toolbar height alignment test failed.");
                if (mainWindow.FindName("ImageLayoutCombo") is not null)
                    throw new InvalidOperationException("Removed image layout selector is still present.");
                var stableAlbumList = (ListBox)mainWindow.FindName("AlbumList");
                var stableArtistTree = (TreeView)mainWindow.FindName("ArtistTree");
                if (ScrollViewer.GetHorizontalScrollBarVisibility(stableAlbumList) != ScrollBarVisibility.Disabled
                    || ScrollViewer.GetVerticalScrollBarVisibility(stableAlbumList) != ScrollBarVisibility.Visible
                    || ScrollViewer.GetHorizontalScrollBarVisibility(stableArtistTree) != ScrollBarVisibility.Disabled
                    || ScrollViewer.GetVerticalScrollBarVisibility(stableArtistTree) != ScrollBarVisibility.Visible)
                    throw new InvalidOperationException("Album scrollbar stability settings test failed.");
                var albumToolbar = (Grid)mainWindow.FindName("AlbumToolbar");
                var compactAlbumSort = (ComboBox)mainWindow.FindName("AlbumSortCombo");
                var compactAlbumFilter = (TextBox)mainWindow.FindName("AlbumFilterTextBox");
                if (compactAlbumSort.Width > 110
                    || Math.Abs(compactAlbumSort.TranslatePoint(new Point(0, compactAlbumSort.ActualHeight / 2), albumToolbar).Y
                                - compactAlbumFilter.TranslatePoint(new Point(0, compactAlbumFilter.ActualHeight / 2), albumToolbar).Y) > 1)
                    throw new InvalidOperationException("Single-row compact album toolbar test failed.");
                var remasterCombo = (ComboBox)mainWindow.FindName("RemasterModeCombo");
                var eqPresetCombo = (ComboBox)mainWindow.FindName("EqPresetCombo");
                if ((eqPresetCombo.Items[0] as ComboBoxItem)?.Tag?.ToString() != "Flat"
                    || !Equals((eqPresetCombo.Items[0] as ComboBoxItem)?.Content, "デフォルト")
                    || eqPresetCombo.SelectedIndex != 0)
                    throw new InvalidOperationException("Default equalizer preset ordering test failed.");
                remasterCombo.SelectedIndex = remasterCombo.Items.Count - 1;
                mainWindow.UpdateLayout();
                var selectedText = VisualDescendants(remasterCombo).OfType<TextBlock>()
                    .FirstOrDefault(text => text.Text.Contains("HDR", StringComparison.OrdinalIgnoreCase));
                if (selectedText is null || !IsDark(selectedText.Foreground))
                {
                    foreach (var text in VisualDescendants(remasterCombo).OfType<TextBlock>())
                        Console.WriteLine($"Combo text '{text.Text}' foreground={text.Foreground}");
                    throw new InvalidOperationException("Selected ComboBox text contrast test failed.");
                }
                var settingsButton = VisualDescendants(mainWindow).OfType<Button>().First(button => Equals(button.Content, "⚙"));
                if (settingsButton.TranslatePoint(new Point(), mainWindow).X > 300)
                    throw new InvalidOperationException("Top toolbar layout test failed.");
                var compactHeader = (Grid)mainWindow.FindName("CompactHeader");
                var unifiedInfoCard = (Border)mainWindow.FindName("UnifiedInfoCard");
                var appVersion = typeof(MainWindow).Assembly.GetName().Version!;
                if (!mainWindow.Title.Contains($"zip.mp3 Player and Manager Plus v{appVersion.Major}.{appVersion.Minor}") || mainWindow.FindName("VersionText") is not null)
                    throw new InvalidOperationException("Title-bar version display test failed.");
                var albumHeader = (TextBlock)mainWindow.FindName("AlbumTitleText");
                var nowPlayingHeader = (TextBlock)mainWindow.FindName("NowPlayingTitleText");
                var settingsCenterY = settingsButton.TranslatePoint(new Point(0, settingsButton.ActualHeight / 2), mainWindow).Y;
                var nowPlayingCenterY = nowPlayingHeader.TranslatePoint(new Point(0, nowPlayingHeader.ActualHeight / 2), mainWindow).Y;
                if (Math.Abs(compactHeader.ActualHeight - 46) > 0.5 || Math.Abs(settingsCenterY - nowPlayingCenterY) > 4
                    || albumHeader.IsVisible || nowPlayingHeader.FontSize < 15
                    || ((TextBlock)mainWindow.FindName("NowPlayingTagText")).FontSize < 13
                    || !nowPlayingHeader.IsDescendantOf(unifiedInfoCard))
                    throw new InvalidOperationException("Unified single-card compact header test failed.");
                var playbackRateButton = (Button)mainWindow.FindName("PlaybackRateButton");
                var transportButtons = new[]
                {
                    (Button)mainWindow.FindName("SelectNowPlayingButton"),
                    (Button)mainWindow.FindName("ShuffleButton"),
                    (Button)mainWindow.FindName("PreviousButton"),
                    (Button)mainWindow.FindName("PlayButton"),
                    (Button)mainWindow.FindName("StopButton"),
                    (Button)mainWindow.FindName("NextButton"),
                    (Button)mainWindow.FindName("RepeatButton")
                };
                var transportWidths = transportButtons.Select(button => button.ActualWidth).ToArray();
                ((Button)mainWindow.FindName("PlayButton")).Content = "⏸ 一時停止";
                ((Button)mainWindow.FindName("ShuffleButton")).Content = "⤨ ON";
                ((Button)mainWindow.FindName("RepeatButton")).Content = "↻ 全曲";
                mainWindow.UpdateLayout();
                if (transportButtons.Select((button, index) => Math.Abs(button.ActualWidth - transportWidths[index]) > 0.1).Any(changed => changed))
                    throw new InvalidOperationException("Transport button fixed-width state test failed.");
                ((Button)mainWindow.FindName("PlayButton")).Content = "▶ 再生";
                ((Button)mainWindow.FindName("ShuffleButton")).Content = "⤨ OFF";
                ((Button)mainWindow.FindName("RepeatButton")).Content = "↻ OFF";
                var shuffleButton = (Button)mainWindow.FindName("ShuffleButton");
                var repeatButton = (Button)mainWindow.FindName("RepeatButton");
                var previousTransportButton = (Button)mainWindow.FindName("PreviousButton");
                if (!shuffleButton.Content.ToString()!.StartsWith("⤨", StringComparison.Ordinal)
                    || !repeatButton.Content.ToString()!.StartsWith("↻", StringComparison.Ordinal)
                    || shuffleButton.ActualWidth >= ((Button)mainWindow.FindName("PlayButton")).ActualWidth
                    || repeatButton.ActualWidth >= ((Button)mainWindow.FindName("PlayButton")).ActualWidth
                    || repeatButton.TranslatePoint(new Point(), mainWindow).X <= shuffleButton.TranslatePoint(new Point(), mainWindow).X
                    || previousTransportButton.TranslatePoint(new Point(), mainWindow).X <= repeatButton.TranslatePoint(new Point(), mainWindow).X)
                    throw new InvalidOperationException("Adjacent shuffle/repeat mode button layout test failed.");
                var speedTestItem = new MenuItem { Tag = "1.25", IsCheckable = true, IsChecked = true };
                typeof(MainWindow).GetMethod("PlaybackSpeedMenu_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(mainWindow, [speedTestItem, new RoutedEventArgs(MenuItem.ClickEvent)]);
                var pitchTestItem = playbackRateButton.ContextMenu!.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "音程を維持"));
                pitchTestItem.IsChecked = false;
                typeof(MainWindow).GetMethod("PreservePitchMenu_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(mainWindow, [pitchTestItem, new RoutedEventArgs(MenuItem.ClickEvent)]);
                if (!playbackRateButton.Content.ToString()!.Contains("1.25×")
                    || !playbackRateButton.Content.ToString()!.Contains("音程 可変")
                    || IsDark(playbackRateButton.Foreground))
                    throw new InvalidOperationException("Compact playback-rate control test failed.");
                var volumeSlider = (Slider)mainWindow.FindName("VolumeSlider");
                var volumeText = (TextBlock)mainWindow.FindName("VolumeValueText");
                volumeSlider.Value = 1.5;
                if (Math.Abs(volumeSlider.Maximum - 2) > 0.001 || volumeText.Text != "150%" || IsDark(volumeText.Foreground))
                    throw new InvalidOperationException("200 percent amplified volume UI test failed.");
                var faithfulMode = (CheckBox)mainWindow.FindName("FaithfulModeCheck");
                var enhancementControls = (StackPanel)mainWindow.FindName("AudioEnhancementControls");
                var equalizerControls = (StackPanel)mainWindow.FindName("EqualizerControls");
                var equalizerExpander = (Expander)mainWindow.FindName("EqualizerExpander");
                var volumeControls = (StackPanel)mainWindow.FindName("VolumeControls");
                var faithfulStatus = (TextBlock)mainWindow.FindName("FaithfulModeStatusText");
                faithfulMode.IsChecked = true;
                if (enhancementControls.IsEnabled || equalizerControls.IsEnabled || playbackRateButton.IsEnabled
                    || equalizerExpander.IsEnabled || volumeControls.IsEnabled || volumeSlider.IsEnabled
                    || enhancementControls.Opacity > 0.5 || equalizerExpander.Opacity > 0.5
                    || playbackRateButton.Opacity > 0.5 || volumeControls.Opacity > 0.5 || volumeText.Text != "固定"
                    || !playbackRateButton.Content.ToString()!.Contains("1.0×")
                    || !faithfulStatus.Text.Contains("DSPなし"))
                    throw new InvalidOperationException("Faithful playback mode UI isolation test failed.");
                faithfulMode.IsChecked = false;
                if (!enhancementControls.IsEnabled || !equalizerControls.IsEnabled || !playbackRateButton.IsEnabled
                    || !equalizerExpander.IsEnabled || !volumeControls.IsEnabled || !volumeSlider.IsEnabled
                    || enhancementControls.Opacity < 0.99 || equalizerExpander.Opacity < 0.99
                    || playbackRateButton.Opacity < 0.99 || volumeControls.Opacity < 0.99 || volumeText.Text != "150%"
                    || !playbackRateButton.Content.ToString()!.Contains("1.25×"))
                    throw new InvalidOperationException("Faithful playback mode restore test failed.");
                if (VisualDescendants(mainWindow).OfType<Button>().All(button => !Equals(button.Content, "Google歌詞")))
                    throw new InvalidOperationException("Google lyrics button is missing.");
                if (File.Exists(sample))
                {
                    typeof(MainWindow).GetMethod("OpenAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mainWindow, [sample]);
                    var openedForUsage = (ZipAlbum)typeof(MainWindow).GetField("_album", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mainWindow)!;
                    var usageEntry = Activator.CreateInstance(entryType)!;
                    var usageKey = (string)typeof(MainWindow).Assembly.GetType("ZipMp3Player.PlaybackUsageStore")!
                        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                        .Single(method => method.Name == "CreateTrackKey" && method.GetParameters().Length == 1
                            && method.GetParameters()[0].ParameterType == typeof(ZipTrack))
                        .Invoke(null, [openedForUsage.Tracks[2]])!;
                    entryType.GetProperty("Key")!.SetValue(usageEntry, usageKey);
                    entryType.GetProperty("Title")!.SetValue(usageEntry, openedForUsage.Tracks[2].Title);
                    var usageMatch = typeof(MainWindow).GetMethod("FindUsageHistoryTrack", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [usageEntry]);
                    if (usageMatch is null) throw new InvalidOperationException("Usage-history track resolution test failed.");
                    var queryBuilder = typeof(MainWindow).GetMethod("BuildGoogleLyricsQuery", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var lyricsQuery = (string)queryBuilder.Invoke(null,
                        [new ZipTrack { Artist = "Artist Test", Title = "Song Test", Album = "Album Test" }, null])!;
                    if (!lyricsQuery.Contains("Artist Test") || !lyricsQuery.Contains("Song Test")
                        || !lyricsQuery.Contains("Album Test") || !lyricsQuery.EndsWith("歌詞"))
                        throw new InvalidOperationException("Google lyrics album-aware query test failed.");
                    mainWindow.UpdateLayout();
                    var fixedAlbumList = (ListBox)mainWindow.FindName("AlbumList");
                    if (fixedAlbumList.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem fixedAlbumRow
                        || Math.Abs(fixedAlbumRow.ActualHeight - 62) > 0.5)
                        throw new InvalidOperationException("Fixed album row height test failed.");
                    var albumFavorite = VisualDescendants(mainWindow).OfType<Button>()
                        .First(button => Equals(button.ToolTip, "アルバムのお気に入りを登録／解除"));
                    albumFavorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var trackFavorite = VisualDescendants(mainWindow).OfType<Button>()
                        .First(button => Equals(button.ToolTip, "曲のお気に入りを登録／解除"));
                    trackFavorite.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    mainWindow.UpdateLayout();
                    var favoritesPath = Path.Combine(data, "favorites.json");
                    if (!File.Exists(favoritesPath) || !File.ReadAllText(favoritesPath).Contains("AlbumKeys")
                        || VisualDescendants(mainWindow).OfType<Button>().Count(button =>
                            (Equals(button.ToolTip, "アルバムのお気に入りを登録／解除")
                             || Equals(button.ToolTip, "曲のお気に入りを登録／解除"))
                            && Equals(button.Content, "★")) < 2)
                        throw new InvalidOperationException("Album/track favorite test failed.");
                    Clipboard.SetImage(testImage);
                    if (VisualDescendants(mainWindow).OfType<Button>().Any(button => Equals(button.Content, "貼り付け")))
                        throw new InvalidOperationException("Removed artwork paste button is still present.");
                    var pasted = (bool)typeof(MainWindow).GetMethod("PasteAlbumArtworkFromClipboard", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null)!;
                    var pastedPath = Directory.EnumerateFiles(Path.Combine(data, "artwork"), "clipboard-*.png", SearchOption.AllDirectories).FirstOrDefault();
                    if (!pasted || pastedPath is null)
                        throw new InvalidOperationException("Clipboard-to-album test failed.");
                    var openedForArtwork = (ZipAlbum)typeof(MainWindow).GetField("_album", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mainWindow)!;
                    var managedPathCheck = typeof(MainWindow).GetMethod("IsManagedArtworkPath", BindingFlags.Static | BindingFlags.NonPublic)!;
                    if (!(bool)managedPathCheck.Invoke(null, [openedForArtwork.Path, pastedPath])!
                        || (bool)managedPathCheck.Invoke(null, [openedForArtwork.Path, Path.Combine(data, "outside.png")])!
                        || !((Button)mainWindow.FindName("DeleteAlbumImageButton")).IsEnabled)
                        throw new InvalidOperationException("Managed artwork deletion protection test failed.");
                    var lyricsBox = (TextBox)mainWindow.FindName("LyricsTextBox");
                    lyricsBox.Clear();
                    typeof(MainWindow).GetMethod("SaveLyrics_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [mainWindow, new RoutedEventArgs()]);
                    var blankTrack = openedForArtwork.Tracks[0];
                    var savedLyricsPath = (string)typeof(MainWindow).GetMethod("GetSavedLyricsPath", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [openedForArtwork, blankTrack])!;
                    typeof(MainWindow).GetMethod("LoadLyrics", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [openedForArtwork, blankTrack, true]);
                    if (!File.Exists(savedLyricsPath) || new FileInfo(savedLyricsPath).Length != 0 || !string.IsNullOrEmpty(lyricsBox.Text)
                        || !((TextBlock)mainWindow.FindName("LyricsStatusText")).Text.StartsWith("空欄保存")
                        || blankTrack.HasLyrics)
                        throw new InvalidOperationException("Blank lyrics persistence test failed.");
                    lyricsBox.Text = "登録済み歌詞の表示テスト";
                    typeof(MainWindow).GetMethod("SaveLyrics_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [mainWindow, new RoutedEventArgs()]);
                    var trackGridForLyrics = (DataGrid)mainWindow.FindName("TrackGrid");
                    if (!blankTrack.HasLyrics || trackGridForLyrics.Columns.All(column => !Equals(column.Header, "歌詞")))
                        throw new InvalidOperationException("Track lyrics indicator test failed.");
                    if (trackGridForLyrics.Columns.Any(column => Equals(column.Header, "状態"))
                        || trackGridForLyrics.Columns.Single(column => Equals(column.Header, "音質")) is not DataGridTemplateColumn
                        || new[] { "年", "ジャンル", "Disc" }.Any(header =>
                            trackGridForLyrics.Columns.All(column => !Equals(column.Header, header))))
                        throw new InvalidOperationException("Track support warning column test failed.");
                    var metadataTrack = new ZipTrack
                    {
                        AudioFormat = "MP3", BitrateKbps = 192, SampleRate = 44100,
                        Year = "1999", Genre = "Metal", DiscNumber = 2, DiscCount = 3
                    };
                    if (metadataTrack.YearText != "1999" || metadataTrack.GenreText != "Metal"
                        || metadataTrack.DiscText != "2/3" || !metadataTrack.AudioText.StartsWith("MP3 / "))
                        throw new InvalidOperationException("Track metadata display test failed.");
                    typeof(MainWindow).GetField("_playingAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, openedForArtwork);
                    typeof(MainWindow).GetField("_currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, 0);
                    typeof(MainWindow).GetField("_lyricsAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, openedForArtwork);
                    typeof(MainWindow).GetField("_lyricsTrack", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, blankTrack);
                    lyricsBox.Text = "[id:paste-test] [ar:同期テスト] [00:05.00]最初の歌詞 [00:10.00]現在の歌詞";
                    if (lyricsBox.Text.Contains("[00:") || lyricsBox.Text != $"最初の歌詞{Environment.NewLine}現在の歌詞")
                        throw new InvalidOperationException("Pasted flattened LRC display conversion test failed.");
                    typeof(MainWindow).GetMethod("SaveLyrics_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [mainWindow, new RoutedEventArgs()]);
                    if (!File.ReadAllText(savedLyricsPath).Contains("[00:10.00]現在の歌詞"))
                        throw new InvalidOperationException("Hidden LRC timestamps must survive save.");
                    typeof(MainWindow).GetMethod("LoadLyrics", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [openedForArtwork, blankTrack, true]);
                    mainWindow.UpdateLayout();
                    var lyricsUnderline = (Border)mainWindow.FindName("LyricsCurrentLineUnderline");
                    typeof(MainWindow).GetMethod("UpdateLyricsAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [5.2, 30.0]);
                    var firstUnderlineTop = Canvas.GetTop(lyricsUnderline);
                    if (lyricsUnderline.Visibility != Visibility.Visible || double.IsNaN(firstUnderlineTop))
                        throw new InvalidOperationException("LRC playback time first-line underline test failed.");
                    typeof(MainWindow).GetMethod("UpdateLyricsAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [10.2, 30.0]);
                    var secondUnderlineTop = Canvas.GetTop(lyricsUnderline);
                    if (lyricsBox.Text.Contains("[00:") || lyricsUnderline.Visibility != Visibility.Visible
                        || secondUnderlineTop <= firstUnderlineTop)
                        throw new InvalidOperationException("Saved/reloaded LRC current-line underline test failed.");
                    lyricsBox.Text = string.Join(Environment.NewLine, ["通常歌詞の一行目", "通常歌詞の二行目", "通常歌詞の三行目"]);
                    typeof(MainWindow).GetMethod("UpdateLyricsAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [15.0, 30.0]);
                    if (lyricsUnderline.Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Plain lyrics must not show the LRC current-line underline.");
                    var lyricsOcrButton = (Button)mainWindow.FindName("LyricsOcrButton");
                    var lyricsContent = (Grid)mainWindow.FindName("LyricsContent");
                    if (mainWindow.FindName("LyricsScrollModeText") is not null
                        || lyricsOcrButton.TranslatePoint(new Point(), lyricsContent).Y
                           >= lyricsBox.TranslatePoint(new Point(), lyricsContent).Y)
                        throw new InvalidOperationException("Compact lyrics toolbar layout test failed.");
                    var wrappedLrc = string.Join(Environment.NewLine, Enumerable.Range(1, 25).Select(index =>
                        $"[00:{index:00}.00]折り返しを含む長い歌詞の自動スクロール確認行 {index:00} 追加の文字列 追加の文字列 追加の文字列"));
                    lyricsBox.Text = wrappedLrc;
                    mainWindow.UpdateLayout();
                    var lyricsScroller = VisualDescendants(lyricsBox).OfType<ScrollViewer>().First();
                    lyricsScroller.ScrollToHome();
                    typeof(MainWindow).GetMethod("UpdateLyricsAutoScroll", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [24.2, 30.0]);
                    mainWindow.UpdateLayout();
                    if (lyricsScroller.VerticalOffset <= 0)
                        throw new InvalidOperationException("Wrapped LRC visual-line auto-scroll test failed.");
                    var albumFilter = (TextBox)mainWindow.FindName("AlbumFilterTextBox");
                    var albumList = (ListBox)mainWindow.FindName("AlbumList");
                    var albumSort = (ComboBox)mainWindow.FindName("AlbumSortCombo");
                    var artistTree = (TreeView)mainWindow.FindName("ArtistTree");
                    albumFilter.Text = string.Join(' ', Path.GetFileName(sample)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(2));
                    if (albumList.Items.Count == 0) throw new InvalidOperationException("Album multi-word filter match test failed.");
                    albumSort.SelectedIndex = 2;
                    albumFilter.Text = "__NO_SUCH_ALBUM__";
                    if (artistTree.Items.Count != 0) throw new InvalidOperationException("Artist-tree filter test failed.");
                    albumFilter.Clear();
                    albumSort.SelectedIndex = 0;
                    var equivalent = typeof(MainWindow).GetMethod("AreDisplayValuesEquivalent", BindingFlags.Static | BindingFlags.NonPublic)!;
                    if (!(bool)equivalent.Invoke(null, ["Gundam Ending Selection", "GUNDAM ENDING SELECTION"])!)
                        throw new InvalidOperationException("Header duplicate normalization test failed.");
                    var compactHeaderTrack = new ZipTrack
                    {
                        Title = "Winners Forever", Artist = "GUNDAM ENDING SELECTION",
                        Album = "Gundam Ending Selection", TrackNumber = 2,
                        AudioFormat = "M4A", BitrateKbps = 192, SampleRate = 44100,
                        Duration = TimeSpan.FromMinutes(3.5)
                    };
                    typeof(MainWindow).GetMethod("UpdateNowPlayingHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, [compactHeaderTrack]);
                    var compactDetails = ((TextBlock)mainWindow.FindName("NowPlayingTagText")).Text;
                    if (compactDetails.Contains("アーティスト:") || compactDetails.Contains("アルバム:")
                        || compactDetails.Split("GUNDAM", StringSplitOptions.None).Length > 2)
                        throw new InvalidOperationException("Compact now-playing header test failed.");
                    var openedAlbum = (ZipAlbum)typeof(MainWindow).GetField("_album", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mainWindow)!;
                    typeof(MainWindow).GetField("_playingAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, openedAlbum);
                    typeof(MainWindow).GetField("_currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mainWindow, 2);
                    typeof(MainWindow).GetMethod("UpdatePlayingAlbumIndicator", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mainWindow, [openedAlbum]);
                    if (!openedAlbum.Tracks[2].IsPlaying || openedAlbum.Tracks.Where((_, index) => index != 2).Any(track => track.IsPlaying))
                        throw new InvalidOperationException("Playing track highlight state test failed.");
                    var nowPlayingButton = (Button)mainWindow.FindName("SelectNowPlayingButton");
                    if (nowPlayingButton.Content?.ToString()?.StartsWith("↪", StringComparison.Ordinal) != true
                        || nowPlayingButton.Content?.ToString()?.Contains('▶') == true)
                        throw new InvalidOperationException("Go-to-playing navigation icon test failed.");
                    nowPlayingButton.IsEnabled = true;
                    albumFilter.Text = "__HIDE_PLAYING_ALBUM__";
                    nowPlayingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var trackGrid = (DataGrid)mainWindow.FindName("TrackGrid");
                    if (!string.IsNullOrEmpty(albumFilter.Text) || albumList.SelectedItem is null || trackGrid.SelectedIndex != 2
                        || VisualDescendants(mainWindow).OfType<TextBlock>().All(text => Equals(text.Text, "▶ 再生中") == false))
                        throw new InvalidOperationException("Select-now-playing button/list indicator test failed.");
                    albumSort.SelectedIndex = 2;
                    nowPlayingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (artistTree.SelectedItem is null) throw new InvalidOperationException("Select-now-playing artist-tree test failed.");
                    albumSort.SelectedIndex = 0;
                    var disabledFolders = (HashSet<string>)typeof(MainWindow)
                        .GetField("_disabledFolders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mainWindow)!;
                    var sampleRoot = Path.GetDirectoryName(openedForArtwork.Path)!;
                    disabledFolders.Add(sampleRoot);
                    typeof(MainWindow).GetMethod("ApplyFolderVisibility", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null);
                    if (albumList.Items.Cast<object>().OfType<object>().Any(item => ReferenceEquals(item, albumList.SelectedItem))
                        || albumList.Items.Count != 0)
                        throw new InvalidOperationException("Disabled music-folder album filtering test failed.");
                    disabledFolders.Clear();
                    typeof(MainWindow).GetMethod("ApplyFolderVisibility", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null);
                    if (albumList.Items.Count == 0)
                        throw new InvalidOperationException("Re-enabled music-folder album restore test failed.");
                }
                extensionToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                mainWindow.UpdateLayout();
                if (extensionPanel.Visibility != Visibility.Collapsed || !Equals(extensionToggle.Content, "◀"))
                    throw new InvalidOperationException("Extension panel collapse test failed.");
                var playerSettings = typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mainWindow)!;
                var minimizeProperty = playerSettings.GetType().GetProperty("MinimizeOnClose")!;
                minimizeProperty.SetValue(playerSettings, true);
                var closeArgs = new System.ComponentModel.CancelEventArgs();
                typeof(MainWindow).GetMethod("Window_Closing", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(mainWindow, [mainWindow, closeArgs]);
                if (!closeArgs.Cancel || mainWindow.WindowState != WindowState.Minimized)
                    throw new InvalidOperationException("Minimize-on-close behavior test failed.");
                minimizeProperty.SetValue(playerSettings, false);
                mainWindow.WindowState = WindowState.Normal;
            }
            if (window is TagEditorWindow tagEditor)
            {
                var grid = (DataGrid)tagEditor.FindName("TagsGrid");
                var sourceType = (TextBox)tagEditor.FindName("SourceTypeText");
                var sourcePath = (TextBox)tagEditor.FindName("SourcePathText");
                var tagEditNotice = (TextBox)tagEditor.FindName("TagEditNoticeText");
                sourcePath.SelectAll();
                if (!sourceType.IsReadOnly || !sourcePath.IsReadOnly || !tagEditNotice.IsReadOnly
                    || sourcePath.SelectedText != sourcePath.Text)
                    throw new InvalidOperationException("Tag editor selectable source information test failed.");
                var trackNumberColumn = grid.Columns[1];
                var sortedView = CollectionViewSource.GetDefaultView(grid.ItemsSource);
                sortedView.SortDescriptions.Clear();
                sortedView.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                    trackNumberColumn.SortMemberPath, System.ComponentModel.ListSortDirection.Ascending));
                var sortedTrackNumbers = sortedView.Cast<object>()
                    .Select(row => (string)row.GetType().GetProperty("TrackNumber")!.GetValue(row)!).ToArray();
                if (trackNumberColumn.SortMemberPath != "TrackNumberSort"
                    || !sortedTrackNumbers.SequenceEqual(["1", "2", "10"]))
                    throw new InvalidOperationException("Tag editor numeric track sorting test failed.");
                var rows = grid.Items.Cast<object>().ToList();
                var artist = rows[0].GetType().GetProperty("Artist")!;
                artist.SetValue(rows[0], "Batch Artist");
                grid.CurrentItem = rows[0];
                grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[3]);
                var apply = VisualDescendants(tagEditor).OfType<Button>()
                    .Single(button => Equals(button.Content, "選択セルの値を全曲へ適用"));
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                tagEditor.UpdateLayout();
                var headers = VisualDescendants(tagEditor).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().ToList();
                if (grid.SelectionMode != DataGridSelectionMode.Extended
                    || rows.Any(row => !Equals(artist.GetValue(row), "Batch Artist"))
                    || headers.Count == 0 || headers.Any(header => !IsDark(header.Background) || IsDark(header.Foreground)))
                    throw new InvalidOperationException("Album tag batch-apply UI test failed.");
            }
            else if (window is SettingsWindow)
            {
                var folderItems = ((ListBox)window.FindName("FolderList")).Items.Cast<MusicFolderOption>().ToList();
                var countText = (TextBlock)window.FindName("FolderCountText");
                var minimizeCheck = (CheckBox)window.FindName("MinimizeOnCloseCheck");
                var tagBackupCheck = (CheckBox)window.FindName("TagBackupCheck");
                var tagBackupPanel = (DockPanel)window.FindName("TagBackupFolderPanel");
                if (folderItems.Count != 2 || folderItems.Count(folder => folder.IsEnabled) != 1
                    || !countText.Text.Contains("表示 1 / 登録 2") || minimizeCheck.IsChecked != true
                    || tagBackupCheck.IsChecked != false || tagBackupPanel.IsEnabled
                    || !Equals(((Button)window.FindName("ExitApplicationButton")).Content, "アプリを終了"))
                    throw new InvalidOperationException($"Music-folder checkbox settings test failed. items={folderItems.Count}, enabled={folderItems.Count(folder => folder.IsEnabled)}, count='{countText.Text}', minimize={minimizeCheck.IsChecked}, language={((ComboBox)window.FindName("LanguageCombo")).SelectedIndex}");
            }
            if (window is UsageWindow)
            {
                var tabs = VisualDescendants(window).OfType<TabControl>().Single();
                foreach (var index in new[] { 0, 1, 0 })
                {
                    tabs.SelectedIndex = index;
                    window.UpdateLayout();
                    if (index == 0)
                    {
                        foreach (var name in new[] { "TopArtistList", "TopAlbumList", "TopTrackList", "RecentTrackList" })
                        {
                            var list = (ItemsControl)window.FindName(name);
                            var boundNames = VisualDescendants(list).OfType<TextBlock>()
                                .Where(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == "Name").ToList();
                            if (boundNames.Count != 5 || boundNames.Any(text => string.IsNullOrEmpty(text.Text) || IsDark(text.Foreground)))
                                failures.Add("UsageWindow/" + name + ": unreadable bound names");
                        }
                    }
                    Inspect(window, "UsageWindow/tab" + index, failures);
                }
                var preview = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_USAGE_PREVIEW");
                if (!string.IsNullOrEmpty(preview))
                {
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preview))!);
                    using var imageFile = File.Create(preview);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(imageFile);
                }
            }
            Inspect(window, window.GetType().Name, failures);
            window.Close();
        }
        if (File.Exists(sample))
        {
            var restored = new MainWindow { ShowInTaskbar = false, WindowState = WindowState.Normal };
            restored.Show();
            typeof(MainWindow).GetMethod("OpenAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restored, [sample]);
            restored.UpdateLayout();
            var restoredAlbumList = (ListBox)restored.FindName("AlbumList");
            var restoredAlbumItem = restoredAlbumList.Items.Cast<object>().First();
            var restoredAlbumType = restoredAlbumItem.GetType();
            var artworkRoleCombo = (ComboBox)restored.FindName("ArtworkRoleCombo");
            artworkRoleCombo.SelectedItem = artworkRoleCombo.Items.OfType<ComboBoxItem>()
                .First(item => Equals(item.Tag, "Front"));
            restored.UpdateLayout();
            if (!Directory.EnumerateFiles(data, "artwork-roles.json", SearchOption.AllDirectories)
                .Any(path => File.ReadAllText(path).Contains("\"Front\"", StringComparison.Ordinal)))
                throw new InvalidOperationException("Artwork role persistence test failed.");
            restoredAlbumType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(restoredAlbumItem, null);
            if (restoredAlbumType.GetProperty("CoverThumbnail")!.GetValue(restoredAlbumItem) is null
                || restoredAlbumType.GetProperty("CaseFrontThumbnail")!.GetValue(restoredAlbumItem) is null
                || restoredAlbumType.GetProperty("BackCoverThumbnail")!.GetValue(restoredAlbumItem) is null
                || restoredAlbumType.GetProperty("SpineThumbnail")!.GetValue(restoredAlbumItem) is null
                || restoredAlbumType.GetProperty("DiscThumbnail")!.GetValue(restoredAlbumItem) is null)
                throw new InvalidOperationException("Composite CD artwork role inference test failed.");
            ((ComboBox)restored.FindName("AlbumSortCombo")).SelectedIndex = 3;
            restored.UpdateLayout();
            var coverFlow = (JewelCaseCoverFlow)restored.FindName("AlbumCoverFlow");
            if (coverFlow.Visibility != Visibility.Visible || coverFlow.ItemCount != 1
                || coverFlow.SelectedFrontCover is null
                || !string.Equals(coverFlow.SelectedKey, sample, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Jewel-case cover-flow mode test failed.");
            var snapshotPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_COVERFLOW_SNAPSHOT");
            if (!string.IsNullOrWhiteSpace(snapshotPath) && coverFlow.ActualWidth > 0 && coverFlow.ActualHeight > 0)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(coverFlow.ActualWidth),
                    (int)Math.Ceiling(coverFlow.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(coverFlow);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(snapshotPath);
                encoder.Save(output);
            }
            if (((ScrollViewer)restored.FindName("ExtensionPanel")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Extension panel state persistence test failed.");
            if (VisualDescendants(restored).OfType<Button>().Count(button =>
                (Equals(button.ToolTip, "アルバムのお気に入りを登録／解除")
                 || Equals(button.ToolTip, "曲のお気に入りを登録／解除"))
                && Equals(button.Content, "★")) < 2)
                throw new InvalidOperationException("Favorite persistence test failed.");
            restored.Close();
        }
        var storedImage = ClipboardArtworkStorage.SavePng(testImage, Path.Combine(data, "artwork-test"));
        using (var input = File.OpenRead(storedImage))
        {
            var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (decoded.PixelWidth != 2 || decoded.PixelHeight != 2) throw new InvalidOperationException("Clipboard PNG test failed.");
        }
        var libraryPath = Path.Combine(data, "library.json");
        var partialLibraryPath = Path.Combine(data, "library.partial.json");
        File.WriteAllText(libraryPath, "{\"Version\":13,\"IsComplete\":true,\"Albums\":[]}");
        var cacheWindow = new MainWindow { ShowInTaskbar = false };
        var loadCache = typeof(MainWindow).GetMethod("LoadLibraryCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var needsRefresh = typeof(MainWindow).GetField("_cacheNeedsRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        loadCache.Invoke(cacheWindow, null);
        if (!(bool)needsRefresh.GetValue(cacheWindow)!)
            throw new InvalidOperationException("Pre-VBR cache must request a metadata refresh.");
        typeof(MainWindow).GetField("_hasCompleteLibraryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, true);
        typeof(MainWindow).GetField("_libraryCacheComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, false);
        typeof(MainWindow).GetField("_scanGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, 1);
        typeof(MainWindow).GetField("_completedScanGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, 0);
        typeof(MainWindow).GetMethod("SaveLibraryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cacheWindow, null);
        if (!File.ReadAllText(libraryPath).Contains("\"IsComplete\":true")
            || !File.Exists(partialLibraryPath) || !File.ReadAllText(partialLibraryPath).Contains("\"IsComplete\":false"))
            throw new InvalidOperationException("Incomplete scan must not replace complete library cache.");
        if (!File.ReadAllText(partialLibraryPath).Contains("\"Version\":14"))
            throw new InvalidOperationException("Updated cache must persist the VBR schema version.");
        typeof(MainWindow).GetField("_libraryCacheComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, true);
        typeof(MainWindow).GetField("_completedScanGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cacheWindow, 1);
        typeof(MainWindow).GetMethod("SaveLibraryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cacheWindow, null);
        loadCache.Invoke(cacheWindow, null);
        if ((bool)needsRefresh.GetValue(cacheWindow)!)
            throw new InvalidOperationException("A complete updated cache must not trigger another migration scan.");
        cacheWindow.Close();
        var englishSettings = new SettingsWindow([@"C:\Music"], [], false, data, "en") { ShowInTaskbar = false };
        englishSettings.Show();
        englishSettings.UpdateLayout();
        var languageCombo = (ComboBox)englishSettings.FindName("LanguageCombo");
        if (englishSettings.Title != "Settings" || englishSettings.DisplayLanguage != "en"
            || languageCombo.SelectedIndex != 1
            || VisualDescendants(englishSettings).OfType<Button>().All(button => Equals(button.Content, "Save and Close")))
            throw new InvalidOperationException("English localization settings test failed.");
        languageCombo.SelectedIndex = 0;
        englishSettings.UpdateLayout();
        if (englishSettings.Title != "設定" || englishSettings.DisplayLanguage != "ja")
            throw new InvalidOperationException("Japanese localization restore test failed.");
        englishSettings.Close();
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"DisplayLanguage\":\"en\"}");
        var englishMain = new MainWindow { ShowInTaskbar = false };
        englishMain.Show();
        englishMain.UpdateLayout();
        if (VisualDescendants(englishMain).OfType<Button>().All(button => Equals(button.Content, "Open File"))
            || ((DataGrid)englishMain.FindName("TrackGrid")).Columns.All(column => !Equals(column.Header, "Artist")))
            throw new InvalidOperationException("English main-window localization test failed.");
        ((TextBlock)englishMain.FindName("NowPlayingTitleText")).Text = "アルバム";
        var localizationType = typeof(MainWindow).Assembly.GetType("ZipMp3Player.LocalizationService")!;
        localizationType.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [englishMain]);
        if (((TextBlock)englishMain.FindName("NowPlayingTitleText")).Text != "アルバム")
            throw new InvalidOperationException("Media metadata must not be translated.");
        englishMain.Close();
        app.Shutdown();
        if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        if (failures.Count > 0) throw new InvalidOperationException("Dark text detected: " + string.Join(", ", failures));
        Console.WriteLine("Theme and clipboard-to-album tests passed.");
    }

    private static void VerifyCoverFlowPan(BitmapSource image)
    {
        var item = new JewelCaseCoverFlowItem("one", "Pan test", "Artist", "ZIP", "White",
            image, null, null, null, null, null, null, false);
        foreach (var fullScreen in new[] { false, true })
        {
            var flow = (JewelCaseCoverFlow)Activator.CreateInstance(typeof(JewelCaseCoverFlow),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [fullScreen], null)!;
            var window = new Window { Content = flow, Width = 800, Height = 600, ShowInTaskbar = false };
            object? Field(string name) => typeof(JewelCaseCoverFlow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow);
            void Call(string name, params object[] args) => typeof(JewelCaseCoverFlow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(flow, args);
            MouseButtonEventArgs ButtonEvent(RoutedEvent route, MouseButton button)
            {
                var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = route };
                flow.RaiseEvent(e);
                return e;
            }
            try
            {
                flow.SetItems([item, item with { Key = "two" }]);
                window.Show();
                window.UpdateLayout();
                var yaw = (double)Field("_caseYaw")!;
                var down = ButtonEvent(UIElement.PreviewMouseDownEvent, MouseButton.Middle);
                if (!down.Handled || !(bool)Field("_isPanning")! || !flow.IsMouseCaptured)
                    throw new InvalidOperationException("Middle button must capture and begin a pan.");
                Call("PanBy", new Vector(80, -40));
                var pan = (Vector)Field("_casePan")!;
                if (pan.X <= 0 || pan.Y <= 0 || yaw != (double)Field("_caseYaw")! || flow.SelectedKey != "one")
                    throw new InvalidOperationException("Pan must move right/up without rotating or changing albums.");
                ButtonEvent(UIElement.PreviewMouseUpEvent, MouseButton.Left);
                if (!(bool)Field("_isPanning")!) throw new InvalidOperationException("Left release must not end a middle drag.");
                var up = ButtonEvent(UIElement.PreviewMouseUpEvent, MouseButton.Middle);
                if (!up.Handled || (bool)Field("_isPanning")! || flow.IsMouseCaptured)
                    throw new InvalidOperationException("Middle release must end pan and release capture.");
                var wpfPan = (System.Windows.Media.Media3D.TranslateTransform3D)Field("_wpfPan")!;
                if (wpfPan.OffsetX != pan.X || wpfPan.OffsetY != pan.Y)
                    throw new InvalidOperationException("WPF pan transform not synchronized.");
                if (Field("_dxScene") is object scene)
                {
                    var dxPan = (System.Windows.Media.Media3D.TranslateTransform3D)scene.GetType()
                        .GetField("_viewPan", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    if (dxPan.OffsetX != pan.X || dxPan.OffsetY != pan.Y)
                        throw new InvalidOperationException("DirectX pan transform not synchronized.");
                }
                Call("AdjustZoom", 120);
                Call("SetCaseOpen", true, false);
                Call("SetDiscRemoved", true, false);
                flow.SetItems([item, item with { Key = "two" }], "one");
                if ((Vector)Field("_casePan")! != pan) throw new InvalidOperationException("Refresh/open/zoom must preserve pan.");
                flow.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(flow)!, Environment.TickCount, Key.R)
                    { RoutedEvent = UIElement.PreviewKeyDownEvent });
                if ((Vector)Field("_casePan")! != new Vector()) throw new InvalidOperationException("R must reset position.");
                Call("PanBy", new Vector(50, 30));
                flow.SelectByKey("two");
                if ((Vector)Field("_casePan")! != new Vector()) throw new InvalidOperationException("Album selection must reset position.");
                ButtonEvent(UIElement.PreviewMouseDownEvent, MouseButton.Middle);
                flow.ReleaseMouseCapture();
                if ((bool)Field("_isPanning")!) throw new InvalidOperationException("Capture loss must cancel pan.");
                flow.SetItems([]);
                if (ButtonEvent(UIElement.PreviewMouseDownEvent, MouseButton.Middle).Handled || flow.IsMouseCaptured)
                    throw new InvalidOperationException("Empty cover flow must not start a pan.");
            }
            finally
            {
                window.Close();
                (Field("_dxScene") as IDisposable)?.Dispose();
            }
        }
        Console.WriteLine("Middle-button pan, release, reset, capture-loss and fullscreen tests passed.");
    }

    private static void VerifyArtworkRoleSelection(string data, BitmapSource front)
    {
        var folder = Path.Combine(data, "ArtworkRoleTest");
        Directory.CreateDirectory(folder);
        void SaveImage(string name, BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(folder, name));
            encoder.Save(file);
        }
        SaveImage("Front.png", front);
        var album = new ZipAlbum { Path = folder, Tracks = [new ZipTrack { Title = "Artwork test" }] };
        var itemType = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        var item = Activator.CreateInstance(itemType, [album])!;
        var load = itemType.GetMethod("LoadCaseArtwork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Check(bool hasBack, bool hasSpines)
        {
            var result = load.Invoke(item, ["", 300])!;
            var tuple = (System.Runtime.CompilerServices.ITuple)result;
            if (tuple[0] is null || (tuple[2] is not null) != hasBack
                || (tuple[3] is not null) != hasSpines || (tuple[4] is not null) != hasSpines)
                throw new InvalidOperationException("Artwork roles must keep missing panels blank and preserve explicit spine crops.");
        }
        Check(false, false);
        var wide = BitmapSource.Create(300, 236, 96, 96, PixelFormats.Bgr32, null, new byte[300 * 236 * 4], 300 * 4);
        SaveImage("Inlay.png", wide);
        Check(false, false);
        var roles = new Dictionary<string, string> { ["file:" + Path.GetFullPath(Path.Combine(folder, "Inlay.png"))] = "Back" };
        var saveRoles = typeof(MainWindow).GetMethod("SaveArtworkRoles", BindingFlags.Static | BindingFlags.NonPublic)!;
        saveRoles.Invoke(null, [folder, roles]);
        Check(true, false);
        roles[roles.Keys.Single()] = "BackWithSpines";
        saveRoles.Invoke(null, [folder, roles]);
        Check(true, true);
        Console.WriteLine("Front-only, Inlay-only, manual Back and BackWithSpines role tests passed.");
    }

    private static void VerifyBlankCaseArtwork(BitmapSource image)
    {
        var blank = new JewelCaseCoverFlowItem("test", "Artwork test", "Artist", "ZIP", "White",
            image, null, null, null, null, image, image, false);
        var cases = new[]
        {
            (Item: blank, Back: false, Left: false, Right: false),
            (Item: blank with { BackCover = image }, Back: true, Left: false, Right: false),
            (Item: blank with { SpineCover = image }, Back: false, Left: true, Right: false),
            (Item: blank with { RightSpineCover = image }, Back: false, Left: false, Right: true),
            (Item: blank with { BackCover = image, SpineCover = image, RightSpineCover = image }, Back: true, Left: true, Right: true),
            (Item: blank, Back: false, Left: false, Right: false)
        };
        var sceneType = typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        using var scene = (IDisposable)Activator.CreateInstance(sceneType)!;
        foreach (var test in cases)
        {
            sceneType.GetMethod("SetItem")!.Invoke(scene, [test.Item, -30.0, 0.0]);
            var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)sceneType.GetField("_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
            var materials = root.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                .Select(mesh => mesh.Material).OfType<HelixToolkit.Wpf.SharpDX.PhongMaterial>().ToList();
            var rear = materials.Single(material => material.Name == "Artwork");
            var spines = materials.Where(material => material.Name == "Spine artwork").ToList();
            if (spines.Count != 2 || rear.RenderDiffuseMap != test.Back
                || spines[0].RenderDiffuseMap != test.Left || spines[1].RenderDiffuseMap != test.Right
                || (rear.DiffuseMap is not null) != test.Back
                || (spines[0].DiffuseMap is not null) != test.Left || (spines[1].DiffuseMap is not null) != test.Right)
                throw new InvalidOperationException("DirectX must only map artwork assigned to each panel.");

            var model = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [test.Item, 1, -30.0, 0.0, 1.0])!;
            var body = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelUIElement3D)model.Children[0]).Model;
            // The first seven meshes form the shell; the next three are rear/left/right inserts.
            var flags = new[] { test.Back, test.Left, test.Right };
            for (var index = 0; index < flags.Length; index++)
            {
                var mesh = (System.Windows.Media.Media3D.GeometryModel3D)body.Children[7 + index];
                var material = (System.Windows.Media.Media3D.MaterialGroup)mesh.Material;
                var brush = material.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>().Single().Brush;
                if (flags[index] ? brush is not ImageBrush : brush is not SolidColorBrush)
                    throw new InvalidOperationException("WPF missing inserts must be solid blank, never replacement artwork or gradients.");
            }
        }
        Console.WriteLine("Blank Back/Spine and explicit per-side artwork tests passed in both renderers.");
    }

    private static void Inspect(DependencyObject parent, string path, List<string> failures)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var childPath = path + "/" + child.GetType().Name;
            if (child is TextBlock text && !HasComboBoxParent(text) && IsDark(text.Foreground))
                failures.Add(childPath + ":" + text.Text);
            if (child is CheckBox checkBox && IsDark(checkBox.Foreground))
                failures.Add(childPath + ":" + checkBox.Content);
            Inspect(child, childPath, failures);
        }
    }

    private static bool HasComboBoxParent(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ComboBox) return true;
        return false;
    }

    private static bool IsDark(Brush brush)
    {
        if (brush is not SolidColorBrush solid) return false;
        var color = solid.Color;
        return color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722 < 90;
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
}
