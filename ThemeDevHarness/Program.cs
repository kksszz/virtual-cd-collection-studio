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
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_SEARCH_ONLY") == "1")
        {
            var searchData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-SearchTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", searchData);
            var searchApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyAlbumSearchPerformance(); }
            finally { searchApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BOOKLET_ONLY") == "1")
        {
            var bookletData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-BookletTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", bookletData);
            var bookletApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try { VerifyBookletViewer(bookletData); }
            finally { bookletApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DISC_DRAG_ONLY") == "1")
        {
            var dragApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyDiscDragging(); }
            finally { dragApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_INLAY_TRAY_ONLY") == "1")
        {
            var trayTestData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-InlayTrayTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", trayTestData);
            var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyInlayTraySelection(trayTestData); }
            finally { previewApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_REAR_CROP_ONLY") == "1")
        {
            VerifyRearInsertCrops();
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_PROPERTIES_ONLY") == "1")
        {
            var propertyData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-PropertiesTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", propertyData);
            var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyAlbumProperties(propertyData, pump: true); }
            finally { previewApp.Shutdown(); if (Directory.Exists(propertyData)) Directory.Delete(propertyData, true); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_FAVORITES_ONLY") == "1")
        {
            var favoriteData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-FavoritesTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", favoriteData);
            var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyFavorites(favoriteData); }
            finally { previewApp.Shutdown(); if (Directory.Exists(favoriteData)) Directory.Delete(favoriteData, true); }
            return;
        }
        // Isolated renderer preview: no player startup or single-instance mutex.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZIPMP3PLAYER_INLAY_PREVIEWS")))
        {
            var previewApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            VerifyInlayArtwork();
            previewApp.Shutdown();
            return;
        }
        var data = Path.Combine(Path.GetTempPath(), "ZipMp3Player-ThemeTest-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", data);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var sample = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_ARCHIVE") ?? string.Empty;
        var pixels = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
        var testImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        VerifyBlankCaseArtwork(testImage);
        VerifyRearInsertCrops();
        VerifyInlayArtwork();
        VerifyArtworkRoleSelection(data, testImage);
        VerifySupplementalArtworkRoles(data, testImage);
        VerifyCoverFlowPan(testImage);
        VerifyFavorites(data);
        VerifyPlaybackCaseColor(testImage);
        VerifyAlbumProperties(data, pump: false);

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
            Console.WriteLine("Checking window: " + window.GetType().Name);
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
                    typeof(MainWindow).GetMethod("ApplyAlbumSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mainWindow, null);
                    if (albumList.Items.Count == 0) throw new InvalidOperationException("Album multi-word filter match test failed.");
                    albumSort.SelectedIndex = 2;
                    albumFilter.Text = "__NO_SUCH_ALBUM__";
                    typeof(MainWindow).GetMethod("ApplyAlbumSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mainWindow, null);
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
                var originalFiles = rows.Select(row => row.GetType().GetProperty("FileName")!.GetValue(row)).ToArray();
                var originalPaths = rows.Select(row => row.GetType().GetProperty("SourcePath")!.GetValue(row)).ToArray();
                foreach (var row in rows)
                {
                    row.GetType().GetProperty("Title")!.SetValue(row, "日本語 カタカナ Ａｂ１２３ ①Ⅲ ～　");
                    row.GetType().GetProperty("Artist")!.SetValue(row, "Ｍｒ.Children");
                    row.GetType().GetProperty("Album")!.SetValue(row, "ＢＯＬＥＲＯ");
                    row.GetType().GetProperty("Year")!.SetValue(row, "１９９７");
                    row.GetType().GetProperty("Genre")!.SetValue(row, "Ｊ－ＰＯＰ");
                    row.GetType().GetProperty("TrackNumber")!.SetValue(row, "１２");
                    row.GetType().GetProperty("DiscNumber")!.SetValue(row, "１");
                    row.GetType().GetProperty("DiscCount")!.SetValue(row, "２");
                }
                grid.Items.Refresh();
                var normalize = (Button)tagEditor.FindName("NormalizeAlphaNumericButton");
                normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string Value(object row, string name) => (string)row.GetType().GetProperty(name)!.GetValue(row)!;
                if (rows.Any(row => Value(row, "Title") != "日本語 カタカナ Ab123 ①Ⅲ ～ "
                    || Value(row, "Artist") != "Mr.Children" || Value(row, "Album") != "BOLERO"
                    || Value(row, "Year") != "1997" || Value(row, "Genre") != "J－POP"
                    || Value(row, "TrackNumber") != "12" || Value(row, "DiscNumber") != "1" || Value(row, "DiscCount") != "2"))
                    throw new Exception("All editable tag columns must be converted; other Unicode preserved.");
                if (!rows.Select(row => row.GetType().GetProperty("FileName")!.GetValue(row)).SequenceEqual(originalFiles)
                    || !rows.Select(row => row.GetType().GetProperty("SourcePath")!.GetValue(row)).SequenceEqual(originalPaths)
                    || ((System.Collections.ICollection)typeof(TagEditorWindow).GetProperty("EditedTracks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tagEditor)!).Count != 0)
                    throw new Exception("Conversion must not rename files, change paths or commit a save.");
                if (!((TextBlock)tagEditor.FindName("BatchStatusText")).Text.Contains("3曲・24項目"))
                    throw new Exception("Conversion status count failed.");
                normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!((TextBlock)tagEditor.FindName("BatchStatusText")).Text.Contains("変換対象の全角英数字・全角スペースはありません"))
                    throw new Exception("Repeated normalization should be a no-op.");
                grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[2]);
                grid.BeginEdit(); tagEditor.UpdateLayout();
                if (grid.Columns[2].GetCellContent(rows[0]) is not TextBox editing) throw new Exception("Could not test pending edit.");
                editing.Text = "編集中　Ｚｚ９";
                normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (Value(rows[0], "Title") != "編集中 Zz9") throw new Exception("Conversion must include the active edit.");
                rows[0].GetType().GetProperty("Title")!.SetValue(rows[0], "　日本語　　曲名　");
                normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (Value(rows[0], "Title") != " 日本語  曲名 " || !((TextBlock)tagEditor.FindName("BatchStatusText")).Text.Contains("1曲・1項目"))
                    throw new Exception("Spaces-only fields must convert and count as changed without collapsing whitespace.");
                tagEditor.Width = 900; tagEditor.UpdateLayout();
                if (!normalize.IsVisible || normalize.ActualWidth < 100 || normalize.TranslatePoint(new Point(normalize.ActualWidth, 0), tagEditor).X > tagEditor.ActualWidth)
                    throw new Exception("Normalization button must fit at minimum window width.");
                tagEditor.Width = 1220; tagEditor.UpdateLayout();
                var tagPreview = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TAG_EDITOR_PREVIEW");
                if (!string.IsNullOrEmpty(tagPreview))
                {
                    var bitmap = new RenderTargetBitmap((int)tagEditor.ActualWidth, (int)tagEditor.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(tagEditor);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(tagPreview); encoder.Save(output);
                }
                Console.WriteLine("Tag editor half-width button: all fields, counts, pending edit, unchanged paths, deferred save and repeat/no-op passed.");
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
            // Inspect content directly: this harness deliberately does not pump
            // the App startup dispatcher, so Window templates may still be pending.
            || !VisualDescendants((DependencyObject)englishSettings.Content).OfType<Button>().Any(button => Equals(button.Content, "Save and Close")))
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
        if (!VisualDescendants((DependencyObject)englishMain.Content).OfType<Button>().Any(button => Equals(button.Content, "Open File"))
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

    private static void VerifyPlaybackCaseColor(BitmapSource image)
    {
        var type = typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        using var scene = (IDisposable)Activator.CreateInstance(type)!;
        foreach (var mode in new[] { "Auto", "Clear", "White", "Black", "Gray" })
        {
            var item = new JewelCaseCoverFlowItem("color", "Color", "Artist", "ZIP", mode, image, null, null, null, null, null, null, false);
            List<HelixToolkit.Maths.Color4> Colors(bool playing)
            {
                type.GetMethod("SetItem")!.Invoke(scene, [item with { IsPlaying = playing }, 0.0, 0.0]);
                var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                return root.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Select(m => m.Material).OfType<HelixToolkit.Wpf.SharpDX.PBRMaterial>()
                    .Select(m => m.AlbedoColor).ToList();
            }
            if (!Colors(false).SequenceEqual(Colors(true)))
                throw new InvalidOperationException("Playing an album must not tint the case/tray green.");
        }
        Console.WriteLine("Tray/case colors remain unchanged during playback for all color modes.");
    }

    private static void VerifyAlbumProperties(string data, bool pump)
    {
        var folder = Path.Combine(data, "Properties 音楽");
        Directory.CreateDirectory(folder);
        var pathA = Path.Combine(folder, "a.wav");
        var pathB = Path.Combine(folder, "b.wav");
        File.WriteAllBytes(pathA, new byte[12]);
        File.WriteAllBytes(pathB, new byte[34]);
        File.WriteAllBytes(Path.Combine(folder, "unrelated.jpg"), new byte[100]);
        var album = new ZipAlbum { Path = folder, Tracks = [
            new ZipTrack { SourcePath = pathA, Album = "Property Album", Artist = "Artist", AudioFormat = "WAV", SampleRate = 44100, Duration = TimeSpan.FromSeconds(120) },
            new ZipTrack { SourcePath = pathB, Album = "Property Album", Artist = "Artist", AudioFormat = "WAV", SampleRate = 48000, Duration = TimeSpan.FromSeconds(180) }] };
        var loader = typeof(MainWindow).Assembly.GetType("ZipMp3Player.AlbumProperties")!.GetMethod("Load")!;
        object Load(ZipAlbum source) => loader.Invoke(null, [source])!;
        var trackLoader = typeof(MainWindow).Assembly.GetType("ZipMp3Player.TrackProperties")!.GetMethod("Load")!;
        object LoadTrack(ZipTrack track) => trackLoader.Invoke(null, [track])!;
        Dictionary<string, string> Rows(object result) => ((System.Collections.IEnumerable)result.GetType().GetProperty("Rows")!.GetValue(result)!)
            .Cast<object>().ToDictionary(row => (string)row.GetType().GetProperty("Label")!.GetValue(row)!, row => (string)row.GetType().GetProperty("Value")!.GetValue(row)!);
        var previousLanguage = LocalizationServiceLanguage();
        var localization = typeof(MainWindow).Assembly.GetType("ZipMp3Player.LocalizationService")!;
        void Language(string value) => localization.GetMethod("SetLanguage")!.Invoke(null, [value]);
        try
        {
            Language("ja");
            var result = Load(album);
            var rows = Rows(result);
            if (!rows["登録音声の合計サイズ"].Contains("46 bytes") || rows["収録曲数"] != "2" || rows["合計再生時間"] != "0:05:00"
                || rows["登録パス"] != folder || !rows.ContainsKey("更新日時") || !rows["サンプルレート"].Contains("48 kHz"))
                throw new Exception("Folder properties must reflect live files and exclude unrelated artwork.");
            var zip = Path.Combine(folder, "sample.zip.mp3");
            File.WriteAllBytes(zip, new byte[321]);
            var zipped = new ZipAlbum { Path = zip, Tracks = [new ZipTrack { IsArchiveEntry = true, SourcePath = zip }] };
            if (!Rows(Load(zipped))["ファイルサイズ"].Contains("321 bytes")) throw new Exception("Archive properties must use actual archive size.");
            var zipTrack = new ZipTrack { SourcePath = zip, FileName = "Disc 1/02 日本語.mp3", IsArchiveEntry = true,
                Size = 1000, CompressedSize = 800, CompressionMethod = 8, AudioFormat = "MP3", IsMp3Valid = true,
                BitrateKbps = 192, SampleRate = 44100, Duration = TimeSpan.FromSeconds(180), TrackNumber = 2, DiscNumber = 1, DiscCount = 2 };
            var zipRows = Rows(LoadTrack(zipTrack));
            if (zipRows["ZIP内のファイル名"] != zipTrack.FileName || zipRows["ZIPのパス"] != zip
                || !zipRows["ZIPファイルサイズ"].Contains("321 bytes") || !zipRows["音声サイズ（登録時）"].Contains("1,000 bytes")
                || !zipRows["圧縮後サイズ（登録時）"].Contains("800 bytes") || zipRows["ZIP圧縮方式"] != "Deflate"
                || zipRows["ビットレート方式"] != "VBR" || zipRows["ディスク番号"] != "1/2"
                || !zipRows.ContainsKey("ZIPの更新日時") || zipRows.ContainsKey("更新日時"))
                throw new Exception("Track properties must distinguish archive metadata from the contained track.");
            if (!Rows(LoadTrack(album.Tracks[0]))["ファイルサイズ"].Contains("12 bytes")
                || Rows(LoadTrack(album.Tracks[0])).ContainsKey("ビットレート方式"))
                throw new Exception("Regular audio must use live file size, with no invented CBR/VBR label.");
            var cbr = new ZipTrack { SourcePath = pathA, AudioFormat = "MP3", IsCbr = true, BitrateKbps = 128 };
            if (Rows(LoadTrack(cbr))["ビットレート方式"] != "CBR") throw new Exception("Legacy CBR metadata display failed.");
            File.Delete(zip);
            var missing = Load(zipped);
            if ((bool)missing.GetType().GetProperty("Exists")!.GetValue(missing)! || !Rows(missing).ContainsKey("状態"))
                throw new Exception("Missing source must remain inspectable.");
            var missingTrack = LoadTrack(zipTrack);
            if ((bool)missingTrack.GetType().GetProperty("Exists")!.GetValue(missingTrack)!
                || !Rows(missingTrack).ContainsKey("ZIP内のファイル名") || Rows(missingTrack).ContainsKey("ZIPファイルサイズ"))
                throw new Exception("Missing archive must preserve cached track details without fabricated file metadata.");
            File.Delete(pathB);
            if (!Rows(Load(album))["登録音声の合計サイズ"].Contains("取得不可 1")) throw new Exception("Partial folder sizes must be explicitly labelled.");
            Language("en");
            if (!Rows(Load(album)).ContainsKey("Registered path")) throw new Exception("English properties labels missing.");
            if (!Rows(LoadTrack(zipTrack)).ContainsKey("File inside ZIP")) throw new Exception("English track properties labels missing.");
            Language("ja");
            foreach (var subject in new object[] { album, album.Tracks[0] })
            {
            var windowResult = subject is ZipTrack selected ? LoadTrack(selected) : result;
            var window = subject is ZipTrack selectedTrack ? new AlbumPropertiesWindow(selectedTrack) { ShowInTaskbar = false }
                : new AlbumPropertiesWindow(album) { ShowInTaskbar = false };
            try
            {
                if (pump)
                {
                    window.Show();
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    timer.Tick += (_, _) => { if (((ItemsControl)window.FindName("PropertyRows")).Items.Count > 0 || DateTime.UtcNow > deadline) { timer.Stop(); frame.Continue = false; } };
                    timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                    if (((ItemsControl)window.FindName("PropertyRows")).Items.Count == 0 || !((Button)window.FindName("OpenLocationButton")).IsEnabled)
                        throw new Exception("Async property loading did not finish.");
                }
                else
                {
                    ((ItemsControl)window.FindName("PropertyRows")).ItemsSource = (System.Collections.IEnumerable)windowResult.GetType().GetProperty("Rows")!.GetValue(windowResult)!;
                    window.Show();
                }
                window.UpdateLayout();
                var failures = new List<string>(); Inspect(window, "AlbumPropertiesWindow", failures);
                if (failures.Count != 0) throw new Exception(string.Join(";", failures));
                var values = VisualDescendants(window).OfType<TextBox>().ToArray();
                if (values.Length < 8 || values.Any(box => !box.IsReadOnly)) throw new Exception("Properties must have readable, copyable read-only fields.");
                if (subject is ZipTrack && (window.Title != "曲のプロパティ" || ((TextBlock)window.FindName("HeadingText")).Text != "曲のプロパティ"))
                    throw new Exception("Track property sheet title failed.");
                var screenshot = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_PROPERTIES_PREVIEW");
                if (!string.IsNullOrEmpty(screenshot))
                {
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(subject is ZipTrack ? Path.ChangeExtension(screenshot, "track.png") : screenshot); encoder.Save(output);
                }
            }
            finally { window.Close(); }
            }
            var main = new MainWindow();
            try
            {
                foreach (var name in new[] { "AlbumList", "ArtistTree", "AlbumCoverFlow" })
                    if (!((FrameworkElement)main.FindName(name)).ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Tag, "AlbumProperties")))
                        throw new Exception("Missing properties menu: " + name);
                var grid = (DataGrid)main.FindName("TrackGrid");
                grid.ItemsSource = album.Tracks;
                main.Show(); main.UpdateLayout();
                grid.SelectedIndex = 0;
                var clicked = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
                typeof(MainWindow).GetMethod("TrackGrid_PreviewMouseRightButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, [grid, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = VisualDescendants(clicked).OfType<DataGridCell>().First() }]);
                if (!ReferenceEquals(grid.SelectedItem, album.Tracks[1])) throw new Exception("Track right click must target clicked row.");
                var menuItem = (MenuItem)main.FindName("TrackPropertiesMenuItem");
                void OpenMenu() => typeof(MainWindow).GetMethod("TrackContextMenu_Opened", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, [grid.ContextMenu, new RoutedEventArgs()]);
                OpenMenu();
                if (!menuItem.IsEnabled) throw new Exception("Selected/missing track must allow properties.");
                grid.SelectedIndex = -1; OpenMenu();
                if (menuItem.IsEnabled) throw new Exception("No track selected must disable properties.");
            }
            finally { main.Close(); }
            Console.WriteLine("Album/track properties: sizes, ZIP distinction, missing files, paths, metadata, localization, read-only UI, right-click selection and menus passed.");
        }
        finally { Language(previousLanguage); }
    }

    private static string LocalizationServiceLanguage() => (string)typeof(MainWindow).Assembly.GetType("ZipMp3Player.LocalizationService")!.GetProperty("CurrentLanguage")!.GetValue(null)!;

    private static void VerifyFavorites(string data)
    {
        var folder = Path.Combine(data, "FavoriteAudio");
        Directory.CreateDirectory(folder);
        ZipTrack Track(string title, string album)
        {
            var path = Path.Combine(folder, title + ".wav");
            using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 1)))
                writer.Write(new byte[44100 * 2 * 30], 0, 44100 * 2 * 30);
            return new ZipTrack { Title = title, Artist = "Favorite artist", Album = album, SourcePath = path,
                FileName = Path.GetFileName(path), AudioFormat = "WAV", SampleRate = 44100, Duration = TimeSpan.FromSeconds(30) };
        }
        var first = new ZipAlbum { Path = Path.Combine(folder, "AlbumA"), Tracks = [Track("TrackA", "Album A"), Track("Unmarked", "Album A")] };
        var second = new ZipAlbum { Path = Path.Combine(folder, "AlbumB"), Tracks = [Track("TrackB", "Album B"), Track("TrackC", "Album B")] };
        var main = new MainWindow { ShowInTaskbar = false };
        object? Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, args);
        try
        {
            var albumType = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
            foreach (var album in new[] { first, second })
                ((System.Collections.IList)Field("_albums")!).Add(Activator.CreateInstance(albumType, [album]));
            var store = Field("_favoritesStore")!;
            store.GetType().GetMethod("ToggleTrack")!.Invoke(store, [first.Tracks[0]]);
            store.GetType().GetMethod("ToggleTrack")!.Invoke(store, [second.Tracks[0]]);
            store.GetType().GetMethod("ToggleAlbum")!.Invoke(store, [second]);
            store.GetType().GetMethod("Save")!.Invoke(store, null);
            store.GetType().GetMethod("Load")!.Invoke(store, null);
            var entries = Call("BuildFavoriteEntries")!;
            if (((System.Collections.IList)entries).Count != 3) throw new InvalidOperationException("Favorites must include marked tracks/albums once, not unmarked tracks.");
            var dialog = (FavoritesWindow)Activator.CreateInstance(typeof(FavoritesWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [entries], null)!;
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var grid = (DataGrid)dialog.FindName("FavoritesGrid");
                grid.SelectedIndex = 0;
                var favoriteRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
                typeof(FavoritesWindow).GetMethod("FavoritesGrid_RightButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(dialog, [grid, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = VisualDescendants(favoriteRow).OfType<DataGridCell>().First() }]);
                if (grid.SelectedIndex != 1) throw new Exception("Favorite right click must target clicked row.");
                typeof(FavoritesWindow).GetMethod("TrackContextMenu_Opened", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(dialog, [grid.ContextMenu, new RoutedEventArgs()]);
                if (!((MenuItem)dialog.FindName("TrackPropertiesMenuItem")).IsEnabled) throw new Exception("Favorite properties menu missing.");
                var mode = (ComboBox)dialog.FindName("FavoriteModeCombo");
                var search = (TextBox)dialog.FindName("SearchTextBox");
                if (grid.Items.Count != 2) throw new InvalidOperationException("Favorite track list must combine albums.");
                mode.SelectedIndex = 1;
                if (grid.Items.Count != 2) throw new InvalidOperationException("Favorite album list must include every album track.");
                search.Text = "TrackC";
                if (grid.Items.Count != 1) throw new InvalidOperationException("Favorite search failed.");
                search.Text = "no such favorite";
                if (grid.Items.Count != 0 || ((Button)dialog.FindName("PlayAllButton")).IsEnabled)
                    throw new InvalidOperationException("Empty favorites must disable playback.");
                search.Clear();
                mode.SelectedIndex = 0;
                grid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Title", System.ComponentModel.ListSortDirection.Descending));
                var prepare = typeof(FavoritesWindow).GetMethod("PreparePlayback", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (!(bool)prepare.Invoke(dialog, [false])!) throw new InvalidOperationException("Favorite list cannot be queued.");
                var queue = typeof(FavoritesWindow).GetProperty("PlaybackQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var queueList = (System.Collections.IList)queue;
                if ((string)queueList[0]!.GetType().GetProperty("Title")!.GetValue(queueList[0])! != "TrackB")
                    throw new InvalidOperationException("Playback queue must honor visible sort order.");
                dialog.UpdateLayout();
                if (VisualDescendants(grid).OfType<TextBlock>().Count(text => text.Text is "TrackA" or "TrackB") != 2)
                    throw new InvalidOperationException("Favorite track names must be visible in the list.");
                var failures = new List<string>();
                Inspect(dialog, "FavoritesWindow", failures);
                if (failures.Count != 0) throw new InvalidOperationException("Unreadable favorites text: " + string.Join(",", failures));
                var preview = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_FAVORITES_PREVIEW");
                if (!string.IsNullOrEmpty(preview))
                {
                    if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_FAVORITES_ONLY") == "1")
                    {
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                    }
                    var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(dialog);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preview))!);
                    using var file = File.Create(preview); encoder.Save(file);
                }
                Set("_favoriteQueue", queue);
                Call("PlayFavoriteTrack", 0);
                if (!ReferenceEquals(Field("_playingAlbum"), second)) throw new InvalidOperationException("Favorite playback did not start in sorted order.");
                Call("PlayFollowingTrack", false);
                if (!ReferenceEquals(Field("_playingAlbum"), first) || (int)Field("_favoriteQueueIndex")! != 1)
                    throw new InvalidOperationException("Next favorite must cross albums without playing unmarked tracks.");
                Call("PlayRelative", -1);
                if (!ReferenceEquals(Field("_playingAlbum"), second)) throw new InvalidOperationException("Previous favorite failed.");
                var repeatType = typeof(MainWindow).GetField("_repeat", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType;
                Set("_repeat", Enum.Parse(repeatType, "One"));
                Call("PlayFollowingTrack", true);
                if ((int)Field("_favoriteQueueIndex")! != 0) throw new InvalidOperationException("Favorite repeat-one failed.");
                Set("_repeat", Enum.Parse(repeatType, "All"));
                Call("PlayFollowingTrack", false); Call("PlayFollowingTrack", true);
                if ((int)Field("_favoriteQueueIndex")! != 0) throw new InvalidOperationException("Favorite repeat-all failed.");
                Set("_shuffle", true); Call("PlayFollowingTrack", false);
                if ((int)Field("_favoriteQueueIndex")! != 1) throw new InvalidOperationException("Favorite shuffle must stay in favorites.");
                Set("_shuffle", false); Set("_repeat", Enum.ToObject(repeatType, 0));
                Call("PlayFollowingTrack", true);
                if (Field("_playingAlbum") is not null || ((System.Collections.IList)Field("_favoriteQueue")!).Count != 0)
                    throw new InvalidOperationException("End of favorite queue must stop and clear the queue.");
            }
            finally { dialog.Close(); }
            store.GetType().GetMethod("ToggleTrack")!.Invoke(store, [first.Tracks[0]]);
            if (((System.Collections.IList)Call("BuildFavoriteEntries")!).Count != 2)
                throw new InvalidOperationException("Reopened favorites must reflect removals.");
        }
        finally { Call("StopPlayback", true); main.Close(); }
        Console.WriteLine("Favorites persistence, list/search/sort, cross-album playback, previous/next, repeat and shuffle tests passed.");
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

    private static void VerifySupplementalArtworkRoles(string data, BitmapSource image)
    {
        var folder = Path.Combine(data, "SupplementalArtwork");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "scan.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
        var album = new ZipAlbum { Path = folder, Tracks = [new ZipTrack { Title = "Category test",
            SourcePath = Path.Combine(folder, "test.mp3"), FileName = "test.mp3" }] };
        var window = new MainWindow { ShowInTaskbar = false };
        var setAlbum = typeof(MainWindow).GetMethod("SetCurrentAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var loadRoles = typeof(MainWindow).GetMethod("LoadArtworkRoles", BindingFlags.Static | BindingFlags.NonPublic)!;
        var itemType = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        var item = Activator.CreateInstance(itemType, [album])!;
        var loadCase = itemType.GetMethod("LoadCaseArtwork", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void AssertNoCaseTextures()
        {
            var parts = (System.Runtime.CompilerServices.ITuple)loadCase.Invoke(item, ["", 300])!;
            for (var index = 0; index < 7; index++)
                if (parts[index] is not null) throw new InvalidOperationException("Supplemental artwork must not be assigned to a 3D case panel.");
            itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
            foreach (var width in new[] { 640, 1200 })
            {
                itemType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(item, [width]);
                if (itemType.GetProperty("CaseFrontThumbnail")!.GetValue(item) is not null)
                    throw new InvalidOperationException("Gallery thumbnail fallback must not bypass supplemental roles.");
            }
        }
        try
        {
            setAlbum.Invoke(window, [album]);
            var combo = (ComboBox)window.FindName("ArtworkRoleCombo");
            foreach (var role in new[] { "LinerNotes", "SpineCard", "Page" })
            {
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, role));
                var stored = (Dictionary<string, string>)loadRoles.Invoke(null, [folder])!;
                if (stored["file:" + Path.GetFullPath(path)] != role)
                    throw new InvalidOperationException("New artwork category was not persisted.");
                setAlbum.Invoke(window, [album]);
                if (!Equals(((ComboBoxItem)combo.SelectedItem).Tag, role))
                    throw new InvalidOperationException("New artwork category was not restored in the dropdown.");
                AssertNoCaseTextures();
            }
            combo.SelectedIndex = 0;
            if (((Dictionary<string, string>)loadRoles.Invoke(null, [folder])!).Count != 0)
                throw new InvalidOperationException("Auto must clear the supplemental category override.");
            foreach (var name in new[] { "Spine Card.png", "album_spine-card.png", "obi.png", "帯.png", "Liner Notes.png", "ライナーノーツ.png", "PAGE_01.png", "page02.png" })
            {
                var renamed = Path.Combine(folder, name);
                File.Move(path, renamed);
                try { AssertNoCaseTextures(); }
                finally { File.Move(renamed, path); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("Liner Notes/Spine Card category selection, persistence, reload, filename inference and case exclusion tests passed.");
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

        // A stored ZIP.MP3 image is decoded through its bounded archive entry,
        // then receives the same persistent orientation metadata as loose files.
        var landscape = BitmapSource.Create(80, 40, 96, 96, PixelFormats.Bgr32, null,
            new byte[80 * 40 * 4], 80 * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(landscape));
        using var encoded = new MemoryStream(); png.Save(encoded);
        var zipMp3 = Path.Combine(folder, "rotation-fixture.zip.mp3");
        File.WriteAllBytes(zipMp3, encoded.ToArray());
        var rotatedAlbum = new ZipAlbum
        {
            Path = zipMp3,
            Tracks = [new ZipTrack { Title = "Rotated ZIP artwork" }],
            Images = [new ZipImage { FileName = "Rotated.png", SourcePath = zipMp3,
                DataOffset = 0, CompressedSize = encoded.Length, UncompressedSize = encoded.Length, CompressionMethod = 0 }]
        };
        saveRoles.Invoke(null, [zipMp3, new Dictionary<string, string> { ["zip:Rotated.png"] = "Front" }]);
        var saveRotations = typeof(MainWindow).GetMethod("SaveArtworkRotations", BindingFlags.Static | BindingFlags.NonPublic)!;
        saveRotations.Invoke(null, [zipMp3, new Dictionary<string, int> { ["zip:Rotated.png"] = 90 }]);
        var rotatedItem = Activator.CreateInstance(itemType, [rotatedAlbum])!;
        var rotatedResult = (System.Runtime.CompilerServices.ITuple)load.Invoke(rotatedItem, ["", 300])!;
        if (rotatedResult[0] is not BitmapSource rotatedFront || rotatedFront.PixelHeight <= rotatedFront.PixelWidth)
            throw new InvalidOperationException("Persistent ZIP.MP3 artwork rotation was not applied before 3D case selection.");
        Console.WriteLine("Front/Inlay roles and persistent loose/ZIP.MP3 artwork rotation tests passed.");
    }

    private static void VerifyAlbumSearchPerformance()
    {
        void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var window = new MainWindow { ShowInTaskbar = false };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, type.GetMethod("MainWindow_Loaded", flags)!);
        var albums = (System.Collections.IList)type.GetField("_albums", flags)!.GetValue(window)!;
        var view = (System.ComponentModel.ICollectionView)type.GetField("_albumView", flags)!.GetValue(window)!;
        var itemType = type.GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        const int total = 10000;
        // Populate pre-indexed metadata only: performance fixture does not touch
        // the user's library or spend its timing budget decoding artwork.
        {
            for (var i = 0; i < total; i++)
            {
                var item = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(itemType);
                var path = $@"C:\SearchTest\Album{i:D5}";
                var artist = "Artist " + i % 100;
                var title = "Album " + i;
                var album = new ZipAlbum { Path = path, Tracks = [new ZipTrack { Title = "Song", FileName = "song.mp3", SourcePath = path + "\\song.mp3" }] };
                void Set(string name, object value) => itemType.GetField("<" + name + ">k__BackingField", flags)!.SetValue(item, value);
                Set("Album", album); Set("Title", title); Set("Artist", artist);
                Set("SearchText", (title + " " + artist + " " + path + (i % 10 == 0 ? " SONG MATCH ガンダム" : " OTHER SONG")).ToUpperInvariant());
                albums.Add(item);
            }
        }
        try
        {
            window.Show(); window.UpdateLayout(); Pump(80);
            var text = (TextBox)window.FindName("AlbumFilterTextBox");
            var list = (ListBox)window.FindName("AlbumList");
            var treeRoots = (System.Collections.ICollection)type.GetField("_artistTreeRoots", flags)!.GetValue(window)!;
            var flow = (JewelCaseCoverFlow)window.FindName("AlbumCoverFlow");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var query in new[] { "S", "SO", "SON", "SONG", "SONG M", "ＳＯＮＧ　ＭＡＴＣＨ" }) text.Text = query;
            watch.Stop(); var typingMs = watch.Elapsed.TotalMilliseconds;
            if (list.Items.Count != total) throw new InvalidOperationException("Typing should coalesce filtering instead of refreshing each key.");
            watch.Restart(); Pump(450); window.UpdateLayout(); watch.Stop();
            if (list.Items.Count != 1000) throw new InvalidOperationException("Latest normalized multi-word query did not win.");
            if (treeRoots.Count != 0 || ((System.Collections.ICollection)typeof(JewelCaseCoverFlow).GetField("_items", flags)!.GetValue(flow)!).Count != 0)
                throw new InvalidOperationException("List search must not rebuild hidden tree/3D views.");
            text.Text = "ガンダム"; type.GetMethod("ApplyAlbumSearch", flags)!.Invoke(window, null);
            if (list.Items.Count != 1000) throw new InvalidOperationException("Japanese text filtering failed.");
            text.Text = "__NO_MATCH__"; type.GetMethod("ApplyAlbumSearch", flags)!.Invoke(window, null);
            if (list.Items.Count != 0) throw new InvalidOperationException("No-match query failed.");
            watch.Restart(); text.Clear(); window.UpdateLayout(); watch.Stop(); var clearMs = watch.Elapsed.TotalMilliseconds;
            if (list.Items.Count != total) throw new InvalidOperationException("Clear must restore all results immediately.");
            var realized = VisualDescendants(list).OfType<ListBoxItem>().Count();
            if (realized > 150 || !VirtualizingPanel.GetIsVirtualizing(list)) throw new InvalidOperationException("Album list must virtualize off-screen rows.");
            // IME: text changes during composition do not filter until committed.
            type.GetField("_albumSearchComposing", flags)!.SetValue(window, true);
            text.Text = "NO_MATCH"; Pump(250);
            if (list.Items.Count != total) throw new InvalidOperationException("IME composition triggered premature filtering.");
            type.GetField("_albumSearchComposing", flags)!.SetValue(window, false);
            type.GetMethod("ApplyAlbumSearch", flags)!.Invoke(window, null);
            if (list.Items.Count != 0) throw new InvalidOperationException("IME commit did not update results.");
            text.Text = "MATCH"; type.GetMethod("ApplyAlbumSearch", flags)!.Invoke(window, null);
            var sort = (ComboBox)window.FindName("AlbumSortCombo");
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "ArtistTree"));
            if (treeRoots.Count != 10) throw new InvalidOperationException("Switching to tree must rebuild the latest filtered artists.");
            text.Text = "ALBUM 9990"; type.GetMethod("ApplyAlbumSearch", flags)!.Invoke(window, null);
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "CoverFlow"));
            var selected = albums.Cast<object>().Single(item => ((ZipAlbum)itemType.GetProperty("Album")!.GetValue(item)!).Path.EndsWith("Album09990"));
            if ((int)itemType.GetProperty("CaseArtworkDecodeWidth")!.GetValue(selected)! != 0)
                throw new InvalidOperationException("Cover flow must not decode artwork synchronously during a filter refresh.");
            text.Text = "NO_MATCH"; Pump(500);
            if (list.Items.Count != 0) throw new InvalidOperationException("Cover flow search did not update.");
            var scene = typeof(JewelCaseCoverFlow).GetField("_dxScene", flags)!.GetValue(flow)!;
            var viewport = (FrameworkElement)scene.GetType().GetProperty("Viewport")!.GetValue(scene)!;
            for (var retry = 0; viewport.Visibility != Visibility.Collapsed && retry < 20; retry++) Pump(100);
            if (viewport.Visibility != Visibility.Collapsed) throw new InvalidOperationException("No results must hide stale 3D artwork.");
            text.Text = "ALBUM 9990"; Pump(1000);
            if (viewport.Visibility != Visibility.Visible || list.Items.Count != 1)
                throw new InvalidOperationException("Cover flow did not restore after asynchronous search.");
            Console.WriteLine($"10,000 albums: six text changes {typingMs:0.0} ms; clear + layout {clearMs:0.0} ms; realized rows {realized}.");
            Console.WriteLine("Search debounce/latest-query, width/case/Japanese normalization, clear, IME, lazy tree/3D and virtualization passed.");
        }
        finally { window.Close(); }
    }

    private static void VerifyBookletViewer(string data)
    {
        void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var folder = Path.Combine(data, "album"); Directory.CreateDirectory(folder);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Bisque, null, new Rect(0, 0, 1000, 650));
            dc.DrawRectangle(Brushes.DarkSlateBlue, null, new Rect(500, 0, 500, 650));
            dc.DrawText(new FormattedText("PAGE 2\nBooklet / liner notes\n\nUse arrows to turn pages.\nZoom to read the details.", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Arial"), 28, Brushes.Black, 1), new Point(40, 60));
            dc.DrawText(new FormattedText("MUSIC\nCOLLECTION", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Arial"), 40, Brushes.White, 1), new Point(560, 250));
        }
        var bitmap = new RenderTargetBitmap(1000, 650, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        foreach (var name in new[] { "spread.png", "PAGE_10.png", "PAGE_2.png", "liner notes.png", "back.png", "inlay.png", "disc.png", "obi.png", "other.png" })
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file);
        }
        var album = new ZipAlbum { Path = folder, Tracks = [new ZipTrack { Title = "Booklet", SourcePath = Path.Combine(folder, "song.mp3"), FileName = "song.mp3" }] };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var itemType = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        var item = Activator.CreateInstance(itemType, [album])!;
        if ((bool)itemType.GetProperty("HasFrontSpread")!.GetValue(item)!) throw new InvalidOperationException("Booklet requires an assigned Front Spread.");
        var roles = new Dictionary<string, string> { ["file:" + Path.Combine(folder, "spread.png")] = "FrontSpread" };
        var saveRoles = typeof(MainWindow).GetMethod("SaveArtworkRoles", BindingFlags.Static | BindingFlags.NonPublic)!;
        saveRoles.Invoke(null, [folder, roles]); itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        if (!(bool)itemType.GetProperty("HasFrontSpread")!.GetValue(item)!) throw new InvalidOperationException("Front Spread gate not refreshed.");
        var content = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        if (!content.Pages.Select(page => page.Role).SequenceEqual(new[] { "Front", "Page", "Page", "LinerNotes", "FrontInside" })
            || !content.Pages.Skip(1).Take(3).Select(page => page.Name).SequenceEqual(new[] { "PAGE_2.png", "PAGE_10.png", "liner notes.png" }))
            throw new InvalidOperationException("Viewer must begin with Front, then PAGE/Liner Notes, and end with inside Front.");
        var derivedFront = content.Pages[0].LoadImage(); var derivedInside = content.Pages[^1].LoadImage();
        if (derivedFront.PixelWidth != derivedInside.PixelWidth || derivedFront.PixelHeight != derivedInside.PixelHeight)
            throw new InvalidOperationException("Front spread halves must produce matching cover pages.");
        roles["file:" + Path.Combine(folder, "PAGE_2.png")] = "Other";
        roles["file:" + Path.Combine(folder, "other.png")] = "Page";
        saveRoles.Invoke(null, [folder, roles]);
        var reassigned = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        if (reassigned.Pages.Any(page => page.Name == "PAGE_2.png") || !reassigned.Pages.Any(page => page.Name == "other.png")
            || reassigned.Pages.First().Role != "Front" || reassigned.Pages.Last().Role != "FrontInside")
            throw new InvalidOperationException("Manual page roles must override filenames.");
        roles["file:" + Path.Combine(folder, "spread.png")] = "Other";
        roles["file:" + Path.Combine(folder, "back.png")] = "Front";
        roles["file:" + Path.Combine(folder, "inlay.png")] = "FrontInside";
        saveRoles.Invoke(null, [folder, roles]); itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        if (!(bool)itemType.GetProperty("HasFrontSpread")!.GetValue(item)!)
            throw new InvalidOperationException("Individual Front and inside Front must enable the booklet without a spread.");
        var individual = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        if (individual.Pages.First().Name != "back.png" || individual.Pages.Last().Name != "inlay.png"
            || individual.Pages.Any(page => page.Name == "spread.png"))
            throw new InvalidOperationException("Individual Front sides must suppress redundant Front Spread artwork.");
        var viewerType = typeof(MainWindow).Assembly.GetType("ZipMp3Player.BookletViewerWindow")!;
        var viewer = (Window)Activator.CreateInstance(viewerType, ["Booklet test", content])!;
        viewer.Show(); Pump(1100);
        var image = (Image)viewerType.GetField("_image", flags)!.GetValue(viewer)!;
        if ((int)viewerType.GetField("_index", flags)!.GetValue(viewer)! != 0 || image.Source is null)
            throw new InvalidOperationException("Booklet did not enter PAGE viewer after opening.");
        var viewerPreview = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BOOKLET_VIEWER_PREVIEW");
        if (!string.IsNullOrWhiteSpace(viewerPreview))
        {
            viewer.UpdateLayout();
            var viewerBitmap = new RenderTargetBitmap((int)viewer.ActualWidth, (int)viewer.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            viewerBitmap.Render(viewer);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(viewerBitmap));
            using var file = File.Create(viewerPreview); encoder.Save(file);
        }
        viewerType.GetMethod("Navigate", flags)!.Invoke(viewer, [1]); Pump(400);
        var pageTurn = (UIElement)viewerType.GetField("_pageTurn", flags)!.GetValue(viewer)!;
        if (pageTurn.Visibility != Visibility.Visible || image.Opacity != 1)
            throw new InvalidOperationException("Page turn must use a visible polygon fold without flashing page opacity.");
        if (!string.IsNullOrWhiteSpace(viewerPreview))
        {
            viewer.UpdateLayout();
            var turnBitmap = new RenderTargetBitmap((int)viewer.ActualWidth, (int)viewer.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            turnBitmap.Render(viewer);
            var turnEncoder = new PngBitmapEncoder(); turnEncoder.Frames.Add(BitmapFrame.Create(turnBitmap));
            using var turnFile = File.Create(Path.Combine(Path.GetDirectoryName(viewerPreview)!,
                Path.GetFileNameWithoutExtension(viewerPreview) + "-turn.png")); turnEncoder.Save(turnFile);
        }
        for (var retry = 0; image.Source is null && retry < 50; retry++) Pump(100);
        if ((int)viewerType.GetField("_index", flags)!.GetValue(viewer)! != 1) throw new InvalidOperationException("Page navigation failed.");
        var beforeWidth = image.Width;
        viewerType.GetMethod("SetZoom", flags)!.Invoke(viewer, [2d]);
        if (image.Width <= beforeWidth * 1.5) throw new InvalidOperationException($"Booklet zoom failed: {beforeWidth} -> {image.Width}, {image.Source}, {((TextBlock)viewerType.GetField("_status", flags)!.GetValue(viewer)!).Text}");
        viewerType.GetMethod("SetZoom", flags)!.Invoke(viewer, [1d]); viewer.UpdateLayout();
        var preview = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BOOKLET_PREVIEW");
        if (!string.IsNullOrEmpty(preview))
        {
            var capture = new RenderTargetBitmap((int)viewer.ActualWidth, (int)viewer.ActualHeight, 96, 96, PixelFormats.Pbgra32); capture.Render(viewer);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(capture)); using var file = File.Create(preview); encoder.Save(file);
        }
        viewer.Close();
        var empty = (Window)Activator.CreateInstance(viewerType, ["Empty", new BookletContent(bitmap, [])])!;
        empty.Show(); Pump(650);
        if (((Image)viewerType.GetField("_image", flags)!.GetValue(empty)!).Source is not null) throw new InvalidOperationException("Empty booklet must not browse cover artwork.");
        empty.Close();
        var flow = new JewelCaseCoverFlow();
        var flowItem = new JewelCaseCoverFlowItem("booklet", "Booklet test", "Artist", "DIR", "White", bitmap, bitmap, null, null, null, null, null, false)
            { LoadBooklet = () => content };
        flow.SetItems([flowItem]);
        var owner = new Window { Width = 1100, Height = 760, Content = flow, ShowInTaskbar = false };
        owner.Show(); Pump(200);
        var opened = false;
        var closeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        closeTimer.Tick += (_, _) =>
        {
            var modal = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.GetType() == viewerType);
            if (modal is not null && (int)viewerType.GetField("_index", flags)!.GetValue(modal)! >= 0)
            { opened = true; modal.Close(); closeTimer.Stop(); }
        };
        closeTimer.Start();
        var transition = (Task)typeof(JewelCaseCoverFlow).GetMethod("OpenBookletAsync", flags)!.Invoke(flow, null)!;
        Pump(7200); closeTimer.Stop();
        if (!opened || !transition.IsCompletedSuccessfully || !flow.IsEnabled) throw new InvalidOperationException("3D extraction/viewer/return workflow failed.");
        var scene = typeof(JewelCaseCoverFlow).GetField("_dxScene", flags)!.GetValue(flow)!;
        var offset = (System.Windows.Media.Media3D.TranslateTransform3D)scene.GetType().GetField("_bookletTranslation", flags)!.GetValue(scene)!;
        if (offset.OffsetX != 0 || offset.OffsetY != 0 || offset.OffsetZ != 0) throw new InvalidOperationException("Jacket did not return to case.");
        var sceneType = scene.GetType();
        var progressField = sceneType.GetField("_bookletProgress", flags)!;
        var poseMethod = sceneType.GetMethod("BookletPose", BindingFlags.Static | BindingFlags.NonPublic)!;
        var underTabsPose = (System.Runtime.CompilerServices.ITuple)poseMethod.Invoke(null, [.42])!;
        if ((double)underTabsPose[0]! < 1.5 || (double)underTabsPose[2]! < -.03 || (double)underTabsPose[3]! != 0)
            throw new InvalidOperationException("The booklet must remain flat until it has slid beneath all four retaining tabs.");
        var clearPose = (System.Runtime.CompilerServices.ITuple)poseMethod.Invoke(null, [.72])!;
        if ((double)clearPose[0]! < 2 || (double)clearPose[2]! > -.5 || (double)clearPose[3]! >= 0)
            throw new InvalidOperationException("Withdrawal must clear the outer edge before the booklet is lifted.");
        Task<bool> Animate(bool removed) => (Task<bool>)sceneType.GetMethod("AnimateBookletAsync")!.Invoke(scene, [removed])!;
        var extracting = Animate(true); Pump(400);
        var midway = (double)progressField.GetValue(scene)!;
        if (midway <= 0 || midway >= 1) throw new InvalidOperationException("Extraction must be animated.");
        var reversed = Animate(false);
        if (!extracting.IsCompleted || extracting.Result || (double)progressField.GetValue(scene)! != midway)
            throw new InvalidOperationException("Reversing must cancel prior completion without jumping.");
        Pump(600);
        if (!reversed.IsCompletedSuccessfully || !reversed.Result || (double)progressField.GetValue(scene)! != 0)
            throw new InvalidOperationException("Interrupted extraction must seat the booklet exactly.");
        var removed = Animate(true); Pump(2300);
        if (!removed.IsCompletedSuccessfully || (double)progressField.GetValue(scene)! != 1)
            throw new InvalidOperationException("Full extraction did not complete.");
        var inserting = Animate(false); Pump(1100);
        if (inserting.IsCompleted || (double)progressField.GetValue(scene)! <= 0 || offset.OffsetX == 0)
            throw new InvalidOperationException("Insertion must remain visible instead of snapping into case.");
        Pump(1300);
        if (!inserting.IsCompletedSuccessfully || offset.OffsetX != 0 || offset.OffsetY != 0 || offset.OffsetZ != 0)
            throw new InvalidOperationException("Insertion must finish at the original fitted position.");
        var motionPreviews = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BOOKLET_MOTION_PREVIEWS");
        if (!string.IsNullOrEmpty(motionPreviews))
        {
            Directory.CreateDirectory(motionPreviews);
            sceneType.GetMethod("SetRotation")!.Invoke(scene, [-15d, 22d]);
            var viewport = (HelixToolkit.Wpf.SharpDX.Viewport3DX)sceneType.GetProperty("Viewport")!.GetValue(scene)!;
            foreach (var stage in new[] { 0d, .4d, .6d, .8d, 1d })
            {
                sceneType.GetMethod("SetBookletProgress", flags)!.Invoke(scene, [stage]); Pump(140);
                HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                    Path.Combine(motionPreviews, $"booklet-{stage * 100:000}.png"));
            }
            sceneType.GetMethod("SetBookletRemoved")!.Invoke(scene, [false, false]);
        }
        owner.Close(); ((IDisposable)scene).Dispose();
        VerifySupplementalArtworkRoles(data, bitmap);
        Console.WriteLine("Booklet roles/order, manual overrides, opening/navigation/zoom, empty state and 3D extraction/return passed.");
    }

    private static void VerifyDiscDragging()
    {
        void Pump(int milliseconds = 180)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var item = new JewelCaseCoverFlowItem("drag", "Disc drag", "Test", "DIR", "Clear", null, null, null, null, null, null, null, false);
        foreach (var fullScreen in new[] { false, true })
        {
            var flow = (JewelCaseCoverFlow)Activator.CreateInstance(typeof(JewelCaseCoverFlow), BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: [fullScreen], culture: null)!;
            flow.SetItems([item], item.Key);
            var window = new Window { Width = 1000, Height = 760, Content = flow, ShowInTaskbar = false };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scene = typeof(JewelCaseCoverFlow).GetField("_dxScene", flags)!.GetValue(flow)!;
            var type = scene.GetType();
            var viewport = (HelixToolkit.Wpf.SharpDX.Viewport3DX)type.GetProperty("Viewport")!.GetValue(scene)!;
            var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_discRoot", flags)!.GetValue(scene)!;
            var caseTransform = (System.Windows.Media.Media3D.Transform3DGroup)type.GetField("_caseTransform", flags)!.GetValue(scene)!;
            var translation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField("_discTranslation", flags)!.GetValue(scene)!;
            var constrain = type.GetMethod("ConstrainRemovedDiscOffset", BindingFlags.Static | BindingFlags.NonPublic)!;
            object? Call(string method, params object[] args) => type.GetMethod(method)!.Invoke(scene, args);
            void FlowCall(string method, params object[] args) => typeof(JewelCaseCoverFlow).GetMethod(method, flags)!.Invoke(flow, args);
            Point PickDisc()
            {
                for (var y = 110; y < viewport.ActualHeight - 60; y += 18)
                    for (var x = 120; x < viewport.ActualWidth - 60; x += 18)
                    {
                        var p = new Point(x, y);
                        if ((bool)Call("BeginDiscDrag", p)!) return p;
                    }
                throw new InvalidOperationException("Cannot pick extracted disc in rendered viewport.");
            }
            try
            {
                window.Show(); window.UpdateLayout(); Pump();
                var blocked = (System.Windows.Media.Media3D.Vector3D)constrain.Invoke(null,
                    [new System.Windows.Media.Media3D.Vector3D(.2, -.3, -4)])!;
                if (blocked.X != .2 || blocked.Y != -.3 || blocked.Z < .349)
                    throw new InvalidOperationException("Removed disc collision constraint must preserve free XY movement while blocking case penetration.");
                if ((bool)Call("BeginDiscDrag", new Point(500, 350))!) throw new InvalidOperationException("Inserted disc must not be draggable.");
                FlowCall("SetCaseOpen", true, false);
                foreach (var (yaw, pitch, zoom) in new[] { (-10d, -2d, 1d), (48d, 28d, 1.35d), (-135d, -18d, .8d) })
                {
                    Call("SetRotation", yaw, pitch); Call("SetViewZoom", zoom); Call("SetViewPan", .08d, -.05d);
                    FlowCall("SetDiscRemoved", true, false); Pump();
                    if ((bool)Call("BeginDiscDrag", new Point(5, 5))!) throw new InvalidOperationException("Background must not pick disc.");
                    var start = PickDisc();
                    var world = (System.Windows.Media.Media3D.Point3D)type.GetField("_discDragPoint", flags)!.GetValue(scene)!;
                    var transform = root.Transform.Value;
                    transform.Append(caseTransform.Value);
                    var inverse = transform; inverse.Invert();
                    var localGrab = inverse.Transform(world);
                    var target = start + new Vector(73, -42);
                    Call("DragDiscTo", target); Pump();
                    transform = root.Transform.Value; transform.Append(caseTransform.Value);
                    var projected = HelixToolkit.Wpf.SharpDX.ViewportExtensions.Project(viewport, transform.Transform(localGrab));
                    if ((projected - target).Length > 2)
                        throw new InvalidOperationException($"Disc did not follow pointer: {projected} vs {target}");
                    Call("EndDiscDrag");
                    var offset = new Vector(translation.OffsetX, translation.OffsetY);
                    Call("DragDiscTo", target + new Vector(30, 40));
                    if (offset != new Vector(translation.OffsetX, translation.OffsetY)) throw new InvalidOperationException("Disc moved after release.");
                    FlowCall("SetDiscRemoved", false, false);
                    if (translation.OffsetX != 0 || translation.OffsetY != 0 || translation.OffsetZ != 0)
                        throw new InvalidOperationException("Insert must reset the disc position.");
                }
                Call("SetRotation", -10d, -2d); Call("SetViewZoom", 1d);
                FlowCall("SetDiscRemoved", true, true); Pump(450);
                var animatedStart = PickDisc();
                Call("DragDiscTo", animatedStart + new Vector(65, 25)); Call("EndDiscDrag");
                var stable = new System.Windows.Media.Media3D.Vector3D(translation.OffsetX, translation.OffsetY, translation.OffsetZ);
                Pump(1100);
                if (stable != new System.Windows.Media.Media3D.Vector3D(translation.OffsetX, translation.OffsetY, translation.OffsetZ))
                    throw new InvalidOperationException("Extraction animation snapped back after dragging.");
                var grab = PickDisc();
                var beginFlow = typeof(JewelCaseCoverFlow).GetMethod("TryBeginDiscDrag", flags)!;
                if (!(bool)beginFlow.Invoke(flow, [grab])! || !flow.IsMouseCaptured)
                    throw new InvalidOperationException("Disc drag must capture pointer in the view.");
                var leftEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseUpEvent };
                FlowCall("OnRotationStarted", flow, leftEvent);
                if ((bool)typeof(JewelCaseCoverFlow).GetField("_isRotating", flags)!.GetValue(flow)!)
                    throw new InvalidOperationException("Disc drag must not also rotate case.");
                FlowCall("OnDiscDragEnded", flow, leftEvent);
                if (flow.IsMouseCaptured || (bool)typeof(JewelCaseCoverFlow).GetField("_isDraggingDisc", flags)!.GetValue(flow)!)
                    throw new InvalidOperationException("Mouse up must release disc drag capture.");
                beginFlow.Invoke(flow, [grab]);
                flow.ReleaseMouseCapture();
                if ((bool)typeof(JewelCaseCoverFlow).GetField("_isDraggingDisc", flags)!.GetValue(flow)!)
                    throw new InvalidOperationException("Capture loss must cancel disc drag.");
                FlowCall("SetCaseOpen", false, false);
                if (translation.OffsetX != 0 || translation.OffsetY != 0 || translation.OffsetZ != 0)
                    throw new InvalidOperationException("Closing the case must return the disc.");
            }
            finally { window.Close(); ((IDisposable)scene).Dispose(); }
        }
        Console.WriteLine("Disc dragging: real mesh hits, screen tracking at varied angles/zoom, release, insert, close and animation interruption passed in both views.");
    }

    private static void VerifyInlayTraySelection(string data)
    {
        var folder = Path.Combine(data, "album");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "scan.png");
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var album = new ZipAlbum { Path = folder, Tracks = [new ZipTrack { Title = "Inlay tray test", SourcePath = Path.Combine(folder, "test.mp3"), FileName = "test.mp3" }] };
        var main = typeof(MainWindow);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var saveColor = main.GetMethod("SaveTrayColor", flags)!;
        var loadColor = main.GetMethod("LoadTrayColor", flags)!;
        var setAlbum = main.GetMethod("SetCurrentAlbum", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var itemType = main.GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        var window = new MainWindow();
        try
        {
            foreach (var preference in new[] { "Auto", "White", "Black", "Gray", "Clear" })
            {
                saveColor.Invoke(null, [folder, preference]);
                setAlbum.Invoke(window, [album]);
                var roleCombo = (ComboBox)window.FindName("ArtworkRoleCombo");
                var trayCombo = (ComboBox)window.FindName("TrayColorCombo");
                void Check(bool inlay)
                {
                    var expected = inlay ? "Clear" : preference;
                    var item = Activator.CreateInstance(itemType, [album])!;
                    if ((string)itemType.GetProperty("TrayColorMode")!.GetValue(item)! != expected
                        || !Equals(((ComboBoxItem)trayCombo.SelectedItem).Tag, expected)
                        || trayCombo.IsEnabled == inlay
                        || (string)loadColor.Invoke(null, [folder])! != preference)
                        throw new InvalidOperationException("Inlay tray display/selection or saved preference mismatch: " + preference);
                    foreach (var width in new[] { 300, 1200 })
                    {
                        itemType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(item, [width]);
                        if ((string)itemType.GetProperty("TrayColorMode")!.GetValue(item)! != expected)
                            throw new InvalidOperationException("High resolution artwork changed the tray selection.");
                    }
                }
                roleCombo.SelectedItem = roleCombo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "Other"));
                Check(false);
                roleCombo.SelectedItem = roleCombo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "Inlay"));
                Check(true);
                setAlbum.Invoke(window, [album]);
                Check(true);
                roleCombo.SelectedItem = roleCombo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "Other"));
                Check(false);
            }
            // Filename-inferred Inlay, deletion, stale roles and managed artwork.
            var inferred = Path.Combine(folder, "inlay.png");
            File.Move(path, inferred);
            saveColor.Invoke(null, [folder, "Black"]);
            void ExpectMode(string expected)
            {
                setAlbum.Invoke(window, [album]);
                var item = Activator.CreateInstance(itemType, [album])!;
                if ((string)itemType.GetProperty("TrayColorMode")!.GetValue(item)! != expected
                    || !Equals(((ComboBoxItem)((ComboBox)window.FindName("TrayColorCombo")).SelectedItem).Tag, expected))
                    throw new InvalidOperationException("Inferred/removed/managed Inlay selection failed.");
            }
            ExpectMode("Clear");
            var managed = (string)main.GetMethod("GetDownloadedArtworkDirectory", flags)!.Invoke(null, [folder])!;
            Directory.CreateDirectory(managed);
            var managedPath = Path.Combine(managed, "inlay.png");
            File.Move(inferred, managedPath);
            ExpectMode("Clear");
            // Move outside artwork discovery without deleting the fixture.
            File.Move(managedPath, Path.Combine(data, "removed.png"));
            ExpectMode("Black");
        }
        finally { window.Close(); }
        Console.WriteLine("Inlay auto-clear: all saved colors, role change, reload, high-resolution, inferred/managed artwork and removal passed.");
    }

    private static void VerifyRearInsertCrops()
    {
        var helper = typeof(MainWindow).Assembly.GetType("ZipMp3Player.RearInsertArtwork")!;
        var getRegions = helper.GetMethod("GetRegions")!;
        Int32Rect?[] Regions(BitmapSource image, bool force)
        {
            var result = getRegions.Invoke(null, [image, force])!;
            return new[] { "Panel", "Left", "Right" }.Select(name =>
                (Int32Rect?)result.GetType().GetProperty(name)!.GetValue(result)).ToArray();
        }
        BitmapSource Fixture(int width, int height, Int32Rect content, Color border)
        {
            var bytes = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var color = x >= content.X && x < content.X + content.Width && y >= content.Y && y < content.Y + content.Height
                        ? Colors.DarkGreen : border;
                    var offset = (y * width + x) * 4;
                    bytes[offset] = color.B; bytes[offset + 1] = color.G; bytes[offset + 2] = color.R; bytes[offset + 3] = 255;
                }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        void Expect(BitmapSource image, bool force, params Int32Rect?[] expected)
        {
            var actual = Regions(image, force);
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Rear crop mismatch: " + string.Join(" / ", actual));
        }
        var padded = Fixture(640, 512, new(20, 20, 600, 472), Colors.White);
        Expect(padded, true, new Int32Rect(44, 20, 552, 472), new Int32Rect(20, 20, 24, 472), new Int32Rect(596, 20, 24, 472));
        Expect(padded, false, Regions(padded, true));
        Expect(Fixture(640, 512, new(12, 8, 600, 480), Color.FromRgb(250, 250, 249)), true,
            new Int32Rect(36, 8, 552, 480), new Int32Rect(12, 8, 24, 480), new Int32Rect(588, 8, 24, 480));
        Expect(Fixture(512, 512, new(20, 20, 472, 472), Colors.White), false, new Int32Rect(20, 20, 472, 472), null, null);
        Expect(Fixture(512, 512, new(0, 0, 0, 0), Colors.White), false, new Int32Rect(0, 0, 512, 512), null, null);
        Expect(Fixture(640, 512, new(20, 20, 600, 472), Colors.Gold), true,
            new Int32Rect(26, 0, 588, 512), new Int32Rect(0, 0, 26, 512), new Int32Rect(614, 0, 26, 512));
        var whiteSpineBytes = Enumerable.Repeat((byte)255, 640 * 512 * 4).ToArray();
        for (var y = 0; y < 512; y++)
        {
            // Text strokes start at varying positions on otherwise-white paper.
            // They must not be interpreted as the inner edge of a white margin.
            var x = 610 + y % 20;
            for (var dx = 0; dx < 3 && x + dx < 640; dx++)
            {
                var p = (y * 640 + x + dx) * 4;
                whiteSpineBytes[p] = whiteSpineBytes[p + 1] = whiteSpineBytes[p + 2] = 20;
            }
            whiteSpineBytes[(y * 640) * 4] = 20;
            whiteSpineBytes[(y * 640) * 4 + 1] = 20;
            whiteSpineBytes[(y * 640) * 4 + 2] = 20;
        }
        var whiteSpine = BitmapSource.Create(640, 512, 96, 96, PixelFormats.Bgra32, null, whiteSpineBytes, 640 * 4);
        Expect(whiteSpine, true, new Int32Rect(26, 0, 588, 512), new Int32Rect(0, 0, 26, 512), new Int32Rect(614, 0, 26, 512));
        Expect(Fixture(2, 2, new(0, 0, 2, 2), Colors.White), true, new Int32Rect(0, 0, 2, 2), null, null);
        var crop = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!.GetMethod("CropArtwork", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (mode, index) in new[] { ("BackPanel", 0), ("LeftSpine", 1), ("RightSpine", 2) })
            if (((CroppedBitmap)crop.Invoke(null, [padded, mode])!).SourceRect != Regions(padded, true)[index])
                throw new InvalidOperationException("Back and Inlay must share crop boundaries.");
        var output = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_REAR_CROP_PREVIEWS");
        foreach (var role in new[] { "BACK", "INLAY" })
        {
            var path = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_" + role + "_SCAN");
            if (string.IsNullOrEmpty(path)) continue;
            foreach (var width in new[] { 300, 1200 })
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = width;
                bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
                var regions = Regions(bitmap, role == "BACK");
                Console.WriteLine($"{role} {bitmap.PixelWidth}x{bitmap.PixelHeight}: {string.Join(" / ", regions)}");
                if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_EXPECT_REAR_MARGINS") == "1"
                    && (regions[1] is not { X: > 0, Y: > 0 } || regions[0]!.Value.Height >= bitmap.PixelHeight))
                    throw new InvalidOperationException("Known scanner margins must be removed before spine splitting.");
                if (string.IsNullOrEmpty(output)) continue;
                Directory.CreateDirectory(output);
                for (var i = 0; i < regions.Length; i++)
                {
                    if (regions[i] is not { } rect) continue;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, rect)));
                    using var stream = File.Create(Path.Combine(output, $"{role}-{width}-{i}.png"));
                    encoder.Save(stream);
                }
            }
        }
        Console.WriteLine("Rear insert margin, shared split, square, color-border and tiny-image tests passed.");
    }

    private static void VerifyInlayArtwork()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.LightGreen, null, new Rect(0, 0, 600, 472));
            dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 24, 472));
            dc.DrawRectangle(Brushes.RoyalBlue, null, new Rect(576, 0, 24, 472));
            dc.DrawRectangle(Brushes.Gold, null, new Rect(24, 0, 180, 90));
            dc.DrawText(new FormattedText("INLAY  TOP\nLEFT → RIGHT\nTray underside", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Arial"), 38, Brushes.Black, 1), new Point(45, 105));
        }
        var scan = new RenderTargetBitmap(600, 472, 96, 96, PixelFormats.Pbgra32);
        scan.Render(visual);
        scan.Freeze();
        var item = new JewelCaseCoverFlowItem("inlay", "Inlay test", "Artist", "ZIP", "Clear",
            null, null, null, null, null, scan, null, false);
        var split = typeof(JewelCaseCoverFlowItem).GetMethod("SplitInlay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var panels = (System.Runtime.CompilerServices.ITuple)split.Invoke(item, null)!;
        if (((CroppedBitmap)panels[0]!).SourceRect != new Int32Rect(24, 0, 552, 472)
            || ((CroppedBitmap)panels[1]!).SourceRect != new Int32Rect(0, 0, 24, 472)
            || ((CroppedBitmap)panels[2]!).SourceRect != new Int32Rect(576, 0, 24, 472))
            throw new InvalidOperationException("Inlay must split into a central panel and distinct left/right strips without rotation.");
        var square = new CroppedBitmap(scan, new Int32Rect(0, 0, 472, 472));
        var squarePanels = (System.Runtime.CompilerServices.ITuple)split.Invoke(item with { InlayCover = square }, null)!;
        if (!ReferenceEquals(squarePanels[0], square) || squarePanels[1] is not null || squarePanels[2] is not null)
            throw new InvalidOperationException("A panel-only Inlay must not fabricate spines.");
        var inlayScanPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_INLAY_SCAN");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZIPMP3PLAYER_INLAY_PREVIEWS")) && !string.IsNullOrEmpty(inlayScanPath))
        {
            var actualScan = new BitmapImage(new Uri(inlayScanPath));
            actualScan.Freeze();
            item = item with { InlayCover = actualScan };
        }
        var type = typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        using var scene = (IDisposable)Activator.CreateInstance(type)!;
        var viewport = (HelixToolkit.Wpf.SharpDX.Viewport3DX)type.GetProperty("Viewport")!.GetValue(scene)!;
        var previewDirectory = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_INLAY_PREVIEWS");
        var window = new Window { Width = 1100, Height = 720, Content = viewport, ShowInTaskbar = false };
        try
        {
            if (!string.IsNullOrEmpty(previewDirectory)) window.Show();
            foreach (var mode in new[] { "Clear", "White", "Black", "Gray" })
            {
                var current = item with { TrayColorMode = mode };
                type.GetMethod("SetItem")!.Invoke(scene, [current, -12.0, 15.0]);
                var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var meshes = root.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToList();
                var panel = meshes.Single(m => m.Material?.Name == "Inlay artwork");
                var innerSpines = meshes.Where(m => m.Material?.Name == "Spine paper reverse").ToList();
                if (!((HelixToolkit.Wpf.SharpDX.PhongMaterial)panel.Material!).RenderDiffuseMap
                    || innerSpines.Count != 2 || innerSpines.Any(m => !((HelixToolkit.Wpf.SharpDX.PhongMaterial)m.Material!).RenderDiffuseMap))
                    throw new InvalidOperationException("Interior Inlay and both spine textures must be present.");
                if (meshes.Where(m => m.Material?.Name is "Artwork" or "Spine artwork")
                    .Any(m => ((HelixToolkit.Wpf.SharpDX.PhongMaterial)m.Material!).RenderDiffuseMap))
                    throw new InvalidOperationException("Inlay must not substitute for exterior Back or Spine.");
                if (meshes.Where(m => m.Material?.Name == "Tray").Any(m => m.IsTransparent != (mode == "Clear")))
                    throw new InvalidOperationException("Only clear trays may expose the Inlay.");
                var panelZ = ((HelixToolkit.SharpDX.MeshGeometry3D)panel.Geometry!).Positions![0].Z;
                if (meshes.Where(m => m.Material?.Name == "Tray").All(m =>
                    ((HelixToolkit.SharpDX.MeshGeometry3D)m.Geometry!).Positions!.Min(p => p.Z) <= panelZ))
                    throw new InvalidOperationException("Inlay must sit beneath the tray backing.");
                foreach (var spine in innerSpines)
                {
                    var geometry = (HelixToolkit.SharpDX.MeshGeometry3D)spine.Geometry!;
                    if (geometry.TextureCoordinates![0].X != 1 || geometry.TextureCoordinates[1].X != 0)
                        throw new InvalidOperationException("Inner spine fold UVs must not mirror the print.");
                }
                var fallback = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                    .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current, 1, 0.0, 0.0, 1.0])!;
                var body = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelUIElement3D)fallback.Children[0]).Model;
                for (var i = 10; i < 13; i++)
                {
                    var mesh = (System.Windows.Media.Media3D.GeometryModel3D)body.Children[i];
                    var material = (System.Windows.Media.Media3D.MaterialGroup)mesh.Material;
                    if (mesh.BackMaterial is not null || material.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>().Single().Brush is not ImageBrush)
                        throw new InvalidOperationException("Fallback must also map all three inside panels separately.");
                    var geometry = (System.Windows.Media.Media3D.MeshGeometry3D)mesh.Geometry;
                    var normal = System.Windows.Media.Media3D.Vector3D.CrossProduct(
                        geometry.Positions[geometry.TriangleIndices[1]] - geometry.Positions[geometry.TriangleIndices[0]],
                        geometry.Positions[geometry.TriangleIndices[2]] - geometry.Positions[geometry.TriangleIndices[0]]);
                    if (i == 10 ? normal.Z <= 0 : i == 11 ? normal.X <= 0 : normal.X >= 0)
                        throw new InvalidOperationException("Fallback inlay normals must face into the case.");
                }
                if (!string.IsNullOrEmpty(previewDirectory))
                {
                    type.GetMethod("SetCaseOpen")!.Invoke(scene, [true, false]);
                    type.GetMethod("SetDiscRemoved")!.Invoke(scene, [true, false]);
                    // Remove the disc only in the inspection image to expose all
                    // of the tray floor and both folded strips at once.
                    var disc = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_discRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    disc.Visibility = Visibility.Hidden;
                    window.UpdateLayout();
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    Directory.CreateDirectory(previewDirectory);
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport, Path.Combine(previewDirectory, mode + ".png"));
                }
            }
            if (!string.IsNullOrEmpty(previewDirectory))
            {
                var backScanPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BACK_SCAN");
                var backScan = string.IsNullOrEmpty(backScanPath) ? (BitmapSource)scan : new BitmapImage(new Uri(backScanPath));
                var backParts = (System.Runtime.CompilerServices.ITuple)split.Invoke(item with { InlayCover = backScan }, null)!;
                foreach (var yaw in new[] { -150.0, 150.0 })
                {
                    var backItem = item with { InlayCover = null, BackCover = (BitmapSource?)backParts[0],
                        SpineCover = (BitmapSource?)backParts[1], RightSpineCover = (BitmapSource?)backParts[2] };
                    type.GetMethod("SetItem")!.Invoke(scene, [backItem, yaw, 8.0]);
                    type.GetMethod("SetCaseOpen")!.Invoke(scene, [false, false]);
                    window.UpdateLayout();
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport, Path.Combine(previewDirectory, $"Back-{yaw}.png"));
                }
            }
            type.GetMethod("SetItem")!.Invoke(scene, [item with { InlayCover = null }, 0.0, 0.0]);
            var clearedRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
            if (clearedRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                .Where(m => m.Material?.Name is "Inlay artwork" or "Spine paper reverse")
                .Any(m => ((HelixToolkit.Wpf.SharpDX.PhongMaterial)m.Material!).RenderDiffuseMap))
                throw new InvalidOperationException("Clearing Inlay must clear the interior textures.");
        }
        finally { window.Close(); }
        Console.WriteLine("Inlay panel, both inner spines, fold UVs, clear/opaque trays and removal tests passed.");
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
                || spines[0].RenderDiffuseMap != test.Right || spines[1].RenderDiffuseMap != test.Left
                || (rear.DiffuseMap is not null) != test.Back
                || (spines[0].DiffuseMap is not null) != test.Right || (spines[1].DiffuseMap is not null) != test.Left)
                throw new InvalidOperationException("DirectX must only map artwork assigned to each panel.");

            var model = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [test.Item, 1, -30.0, 0.0, 1.0])!;
            var body = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelUIElement3D)model.Children[0]).Model;
            // The first seven meshes form the shell; the next three are rear/left/right inserts.
            var flags = new[] { test.Back, test.Right, test.Left };
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
