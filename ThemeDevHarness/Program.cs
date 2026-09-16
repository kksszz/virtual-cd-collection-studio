using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static partial class Program
{
    [STAThread]
    private static void Main()
    {
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_AUDIO_SETTINGS_TEST") == "1")
        {
            VerifyAudioSettingsRestart();
            return;
        }
        var archiveCheckPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ARCHIVE_CHECK");
        if (!string.IsNullOrWhiteSpace(archiveCheckPath))
        {
            var album = ZipAlbumReader.Open(archiveCheckPath);
            Console.WriteLine($"Archive parsed: {album.Path} | tracks={album.Tracks.Count} | images={album.Images.Count}");
            foreach (var track in album.Tracks)
                Console.WriteLine($"#{track.TrackNumber} {track.Title} | {track.Artist} | {track.DurationText} | {track.SampleRate} Hz");
            var secondTrack = album.Tracks[1];
            using var audio = TrackAudioReader.Open(secondTrack);
            var buffer = new byte[Math.Max(4096, audio.Reader.WaveFormat.AverageBytesPerSecond)];
            var decoded = audio.Reader.Read(buffer, 0, buffer.Length);
            if (!string.Equals(secondTrack.Title, "End Of The Line", StringComparison.Ordinal)
                || secondTrack.Duration < TimeSpan.FromMinutes(4) || decoded <= 0)
                throw new InvalidOperationException("Malformed UTF-16 title, Xing duration, or MP3 fallback playback was not repaired.");
            Console.WriteLine($"Track 2 fallback playback decoded {decoded} bytes as {audio.Reader.WaveFormat}.");
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_LIBRARY_WATCH_ONLY") == "1")
        {
            VerifyAutomaticLibraryUpdates();
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TAG_EDITOR_ONLY") == "1")
        {
            var tagApp = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            tagApp.InitializeComponent();
            try { VerifyTagEditorFilenameEditing(); }
            finally { tagApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_LIBRARY_LOADING_ONLY") == "1")
        {
            var loadingData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-LibraryLoadingTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", loadingData);
            var loadingApp = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            loadingApp.InitializeComponent();
            try { VerifyLibraryLoadingIndicator(); }
            finally { loadingApp.Shutdown(); if (Directory.Exists(loadingData)) Directory.Delete(loadingData, true); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ARTWORK_POPUP_ONLY") == "1")
        {
            var popupApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyArtworkPopupFit(); }
            finally { popupApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_SEARCH_ONLY") == "1")
        {
            var searchData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-SearchTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", searchData);
            var searchApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { VerifyAlbumSearchPerformance(); }
            finally { searchApp.Shutdown(); }
            return;
        }
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_LYRICS_LAYOUT_ONLY") == "1")
        {
            var layoutData = Path.Combine(Path.GetTempPath(), "ZipMp3Player-LyricsLayoutTest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", layoutData);
            var layoutApp = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            layoutApp.InitializeComponent();
            var window = new MainWindow { ShowInTaskbar = false, Width = 1500, Height = 900 };
            try
            {
                window.Show(); window.UpdateLayout();
                var artwork = (ColumnDefinition)window.FindName("ArtworkColumn");
                var lyrics = (ColumnDefinition)window.FindName("LyricsColumn");
                var lyricsPanel = (Grid)window.FindName("LyricsContent");
                var splitter = (GridSplitter)window.FindName("ImageLyricsSplitter");
                var splitterColumn = (ColumnDefinition)window.FindName("ImageLyricsSplitterColumn");
                var toggle = (Button)window.FindName("LyricsPanelToggleButton");
                if (Grid.GetColumn(toggle) != 1 || toggle.Width != 24 || toggle.Height != 46
                    || !Equals(toggle.Content, "▶"))
                    throw new InvalidOperationException("Lyrics collapse control must be a narrow divider claw, not a toolbar button.");
                var initialWidth = artwork.ActualWidth;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                if (lyricsPanel.Visibility != Visibility.Collapsed || lyrics.ActualWidth > .5
                    || splitter.Visibility != Visibility.Collapsed || splitterColumn.Width.Value != 24
                    || !Equals(toggle.Content, "◀") || artwork.ActualWidth <= initialWidth + 100)
                    throw new InvalidOperationException("Lyrics collapse did not expand album artwork into the released space.");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                var total = artwork.ActualWidth + lyrics.ActualWidth;
                if (lyricsPanel.Visibility != Visibility.Visible || splitter.Visibility != Visibility.Visible
                    || total <= 0 || Math.Abs(artwork.ActualWidth / total - .6) > .03)
                    throw new InvalidOperationException("Lyrics expansion did not restore the saved split ratio.");
                Console.WriteLine("Lyrics collapse expands album artwork and restores the saved split ratio.");
            }
            finally { window.Close(); layoutApp.Shutdown(); }
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
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ALBUM_BROWSER_ONLY") == "1")
        {
            var browserApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var browserPixels = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
            var browserImage = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, browserPixels, 8);
            try { VerifyAlbumLibraryBrowser(browserImage); }
            finally { browserApp.Shutdown(); }
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
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DISC_CROP_ONLY") == "1")
        {
            VerifyDiscArtworkCrop();
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
        VerifyDiscArtworkCrop();
        VerifyRearInsertCrops();
        VerifyInlayArtwork();
        VerifyArtworkRoleSelection(data, testImage);
        VerifySupplementalArtworkRoles(data, testImage);
        VerifyCoverFlowPan(testImage);
        VerifyContinuousCoverFlowKeyboard(testImage);
        VerifyAlbumLibraryBrowser(testImage);
        VerifyLibraryLoadingIndicator();
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
                var lyricsToggle = (Button)mainWindow.FindName("LyricsPanelToggleButton");
                var lyricsPanel = (Grid)mainWindow.FindName("LyricsContent");
                var lyricsSplitter = (GridSplitter)mainWindow.FindName("ImageLyricsSplitter");
                var initialArtworkWidth = artworkColumn.ActualWidth;
                lyricsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                mainWindow.UpdateLayout();
                if (lyricsPanel.Visibility != Visibility.Collapsed
                    || lyricsColumn.ActualWidth > .5
                    || lyricsSplitter.Visibility != Visibility.Collapsed
                    || artworkColumn.ActualWidth <= initialArtworkWidth + 100)
                    throw new InvalidOperationException("Collapsing lyrics must give its complete width to album artwork.");
                lyricsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                mainWindow.UpdateLayout();
                imageLyricsTotal = artworkColumn.ActualWidth + lyricsColumn.ActualWidth;
                if (lyricsPanel.Visibility != Visibility.Visible
                    || lyricsSplitter.Visibility != Visibility.Visible
                    || imageLyricsTotal <= 0
                    || Math.Abs(artworkColumn.ActualWidth / imageLyricsTotal - 0.6) > 0.03)
                    throw new InvalidOperationException("Restoring lyrics must recover the previous image/lyrics split.");
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
                var unifiedInfoCard = (Border)mainWindow.FindName("UnifiedInfoCard");
                if (settingsButton.TranslatePoint(new Point(settingsButton.ActualWidth, 0), mainWindow).X
                    > unifiedInfoCard.TranslatePoint(new Point(), mainWindow).X + 1)
                    throw new InvalidOperationException("Top toolbar layout test failed.");
                var compactHeader = (Grid)mainWindow.FindName("CompactHeader");
                var appVersion = typeof(MainWindow).Assembly.GetName().Version!;
                if (!mainWindow.Title.Contains($"Virtual CD Collection Studio v{appVersion.Major}.{appVersion.Minor}") || mainWindow.FindName("VersionText") is not null)
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
                    var browserAlbums = ((System.Collections.IEnumerable)typeof(MainWindow)
                        .GetMethod("GetAlbumBrowserSourceAlbums", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null)!).Cast<object>().ToList();
                    if (albumList.Items.Cast<object>().OfType<object>().Any(item => ReferenceEquals(item, albumList.SelectedItem))
                        || albumList.Items.Count != 0 || browserAlbums.Count != 0)
                        throw new InvalidOperationException("Disabled music-folder album filtering test failed.");
                    disabledFolders.Clear();
                    typeof(MainWindow).GetMethod("ApplyFolderVisibility", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null);
                    browserAlbums = ((System.Collections.IEnumerable)typeof(MainWindow)
                        .GetMethod("GetAlbumBrowserSourceAlbums", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(mainWindow, null)!).Cast<object>().ToList();
                    if (albumList.Items.Count == 0 || browserAlbums.Count == 0)
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
                var initialRows = grid.Items.Cast<object>().ToList();
                grid.CurrentItem = initialRows[0];
                grid.CurrentCell = new DataGridCellInfo(initialRows[0], grid.Columns[0]);
                grid.BeginEdit(); tagEditor.UpdateLayout();
                if (grid.Columns[0].IsReadOnly || grid.Columns[0].GetCellContent(initialRows[0]) is not TextBox fileNameEditor)
                    throw new InvalidOperationException("Tag editor file name column must be editable text.");
                fileNameEditor.SelectAll();
                if (fileNameEditor.SelectedText != fileNameEditor.Text)
                    throw new InvalidOperationException("Tag editor file name text must support selection and copying.");
                grid.CancelEdit(DataGridEditingUnit.Cell);
                grid.CancelEdit(DataGridEditingUnit.Row);
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
                rows[0].GetType().GetProperty("DisplayFileName")!.SetValue(rows[0], "０１　ＥＤＩＴ＆ＬＩＶＥ？.mp3");
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
                if (rows.Any(row => Value(row, "Title") != "日本語 カタカナ Ab123 ①Ⅲ ~ "
                    || Value(row, "Artist") != "Mr.Children" || Value(row, "Album") != "BOLERO"
                    || Value(row, "Year") != "1997" || Value(row, "Genre") != "J-POP"
                    || Value(row, "TrackNumber") != "12" || Value(row, "DiscNumber") != "1" || Value(row, "DiscCount") != "2"))
                    throw new Exception("All editable tag columns must be converted; other Unicode preserved.");
                if (Value(rows[0], "DisplayFileName") != "01 EDIT&LIVE？.mp3")
                    throw new Exception("Tag normalization must convert file names while preserving invalid half-width symbols.");
                if (!rows.Select(row => row.GetType().GetProperty("FileName")!.GetValue(row)).SequenceEqual(originalFiles)
                    || !rows.Select(row => row.GetType().GetProperty("SourcePath")!.GetValue(row)).SequenceEqual(originalPaths)
                    || ((System.Collections.ICollection)typeof(TagEditorWindow).GetProperty("EditedTracks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tagEditor)!).Count != 0)
                    throw new Exception("Conversion must not rename files, change paths or commit a save.");
                if (!((TextBlock)tagEditor.FindName("BatchStatusText")).Text.Contains("3曲・25項目"))
                    throw new Exception("Conversion status count failed.");
                normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!((TextBlock)tagEditor.FindName("BatchStatusText")).Text.Contains("変換対象の全角英数字・記号・全角スペースはありません"))
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
        var currentCacheVersion = (int)typeof(MainWindow).GetField("CurrentLibraryCacheVersion",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        if (!File.ReadAllText(partialLibraryPath).Contains($"\"Version\":{currentCacheVersion}"))
            throw new InvalidOperationException("Updated cache must persist the VBR schema version.");
        // Completing the migration clears the refresh request before the
        // partial snapshot is promoted to the complete cache.
        needsRefresh.SetValue(cacheWindow, false);
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

    private static void VerifyArtworkPopupFit()
    {
        const int pixelWidth = 2400;
        const int pixelHeight = 1200;
        var bitmap = BitmapSource.Create(pixelWidth, pixelHeight, 300, 300, PixelFormats.Gray8, null,
            new byte[pixelWidth * pixelHeight], pixelWidth);
        var image = new Image { Stretch = Stretch.Fill };
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var mainWindowType = typeof(MainWindow);
        mainWindowType.GetMethod("SetArtworkPopupImage", flags)!.Invoke(null, [image, bitmap]);
        if (Math.Abs(image.Width - pixelWidth) > .01 || Math.Abs(image.Height - pixelHeight) > .01
            || Math.Abs(bitmap.Width - image.Width) < 100)
            throw new InvalidOperationException(
                $"Artwork popup must ignore scan DPI for its pixel-sized layout: source={bitmap.Width:0.0}x{bitmap.Height:0.0}, layout={image.Width:0.0}x{image.Height:0.0}.");

        var fitMethod = mainWindowType.GetMethod("CalculateArtworkPopupFitScale", flags)!;
        var fit = (double)fitMethod.Invoke(null, [pixelWidth, pixelHeight, 1000d, 700d, 0, 24d])!;
        if (pixelWidth * fit > 976.01 || pixelHeight * fit > 676.01
            || Math.Abs(fit - 976d / pixelWidth) > .0001)
            throw new InvalidOperationException($"Landscape scan did not fit the popup viewport: {fit:0.0000}.");

        var rotatedFit = (double)fitMethod.Invoke(null, [pixelWidth, pixelHeight, 1000d, 700d, 90, 24d])!;
        if (pixelHeight * rotatedFit > 976.01 || pixelWidth * rotatedFit > 676.01
            || Math.Abs(rotatedFit - 676d / pixelWidth) > .0001)
            throw new InvalidOperationException($"Rotated scan did not fit the popup viewport: {rotatedFit:0.0000}.");
        Console.WriteLine("Artwork popup DPI-independent sizing and fit-to-window tests passed.");
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
        void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var spineCard = new TransformedBitmap(image, new ScaleTransform(30, 60));
        spineCard.Freeze();
        var item = new JewelCaseCoverFlowItem("one", "Pan test", "Artist", "ZIP", "White",
            image, null, null, null, null, null, null, false) { SpineCard = spineCard };
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
                var spineCardButton = (Button)Field("_spineCardButton")!;
                if (spineCardButton.Visibility != Visibility.Visible
                    || !spineCardButton.Content.ToString()!.Contains("Spine", StringComparison.Ordinal))
                    throw new InvalidOperationException("The selected Spine Card must expose its remove/insert button.");
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
                    if (!fullScreen)
                    {
                        // Spine Card albums are wrapped until the film is removed.
                        // This block specifically verifies the following obi/lid
                        // sequence, so begin from the already-unwrapped state.
                        Call("ApplyWrappingOpened", true, false);
                        Task<bool>? opening = null;
                        flow.Dispatcher.BeginInvoke(() => opening = (Task<bool>)typeof(JewelCaseCoverFlow)
                            .GetMethod("SetCaseOpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(flow, [true, true])!);
                        Pump(50);
                        if (opening is null) throw new InvalidOperationException("The case-open sequence did not start on the UI thread.");
                        var obiOffset = (System.Windows.Media.Media3D.TranslateTransform3D)scene.GetType()
                            .GetField("_spineCardTranslation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                        var lidAngle = (System.Windows.Media.Media3D.AxisAngleRotation3D)scene.GetType()
                            .GetField("_lidHingeRotation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                        var obiProgressField = scene.GetType().GetField("_spineCardProgress",
                            BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var sequenceClock = System.Diagnostics.Stopwatch.StartNew();
                        while ((double)obiProgressField.GetValue(scene)! <= 0
                            && sequenceClock.ElapsedMilliseconds < 3000) Pump(20);
                        var movingProgress = (double)obiProgressField.GetValue(scene)!;
                        if (movingProgress is > 0 and < 1 && Math.Abs(lidAngle.Angle) > .01)
                            throw new InvalidOperationException("The obi must slide first while the case lid stays closed.");
                        var removedOffset = (double)scene.GetType().GetField("_spineCardRemovedOffsetX",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                        while (lidAngle.Angle >= -1 && sequenceClock.ElapsedMilliseconds < 4500) Pump(30);
                        if (Math.Abs(obiOffset.OffsetX - removedOffset) > .01
                            || Math.Abs(obiOffset.OffsetY - -.08) > .01
                            || obiOffset.OffsetZ < .018 || lidAngle.Angle >= -1
                            || (double)obiProgressField.GetValue(scene)! != 1)
                            throw new InvalidOperationException("The case may open only after the obi is fully separated.");
                        Pump(1250);
                        if (!opening.IsCompletedSuccessfully || !opening.Result || !(bool)Field("_isSpineCardRemoved")!)
                            throw new InvalidOperationException("Automatic obi removal and case-open sequence did not complete.");
                        Call("SetCaseOpen", false, false);
                        Call("ApplySpineCardRemoved", false, false);
                    }
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
        void AssertSupplementalCaseTextures(bool expectSpineCard)
        {
            var parts = (System.Runtime.CompilerServices.ITuple)loadCase.Invoke(item, ["", 300])!;
            for (var index = 0; index < 8; index++)
                if (parts[index] is not null) throw new InvalidOperationException("Supplemental artwork must not be assigned to a 3D case panel.");
            if ((parts[8] is not null) != expectSpineCard)
                throw new InvalidOperationException("Only Spine Card supplementary artwork may wrap around the 3D case.");
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
            var spineAdjustment = (Button)window.FindName("AdjustSpineCardFoldsButton");
            foreach (var role in new[] { "LinerNotes", "SpineCard", "Page", "Flyer", "Poster" })
            {
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, role));
                var stored = (Dictionary<string, string>)loadRoles.Invoke(null, [folder])!;
                if (stored["file:" + Path.GetFullPath(path)] != role)
                    throw new InvalidOperationException("New artwork category was not persisted.");
                setAlbum.Invoke(window, [album]);
                if (!Equals(((ComboBoxItem)combo.SelectedItem).Tag, role))
                    throw new InvalidOperationException("New artwork category was not restored in the dropdown.");
                if ((spineAdjustment.Visibility == Visibility.Visible) != (role == "SpineCard"))
                    throw new InvalidOperationException("The Spine Card boundary button must be visible only while Spine Card artwork is selected.");
                AssertSupplementalCaseTextures(role == "SpineCard");
            }
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "Disc2"));
            var twoDiscParts = (System.Runtime.CompilerServices.ITuple)loadCase.Invoke(item, ["", 300])!;
            if (twoDiscParts[6] is not BitmapSource || twoDiscParts[7] is not BitmapSource)
                throw new InvalidOperationException("Disc (2 Disc) must non-destructively provide separate Disc 1 and Disc 2 textures.");
            combo.SelectedIndex = 0;
            if (((Dictionary<string, string>)loadRoles.Invoke(null, [folder])!).Count != 0)
                throw new InvalidOperationException("Auto must clear the supplemental category override.");
            foreach (var name in new[] { "Spine Card.png", "album_spine-card.png", "obi.png", "帯.png",
                         "Liner Notes.png", "ライナーノーツ.png", "PAGE_01.png", "page02.png",
                         "Flyer.png", "album_flyer-02.png", "フライヤー.png", "チラシ.png",
                         "Poster.png", "tour_poster-02.png", "ポスター.png" })
            {
                var renamed = Path.Combine(folder, name);
                File.Move(path, renamed);
                try { AssertSupplementalCaseTextures(name.Contains("Spine", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("obi", StringComparison.OrdinalIgnoreCase) || name.Contains("帯", StringComparison.Ordinal)); }
                finally { File.Move(renamed, path); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("Liner Notes/Page/Flyer/Poster exclusion and Spine Card 3D assignment, persistence, reload and filename inference tests passed.");
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
        var defaultImageMethod = typeof(MainWindow).GetMethod("GetDefaultAlbumImageIndex",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        static int PreferredIndex(MethodInfo method, params string[] roles) =>
            (int)method.Invoke(null, [roles])!;
        if (PreferredIndex(defaultImageMethod, "Back", "FrontSpreadVertical", "Front", "FrontSpread") != 3
            || PreferredIndex(defaultImageMethod, "Back", "FrontSpreadVertical", "Front") != 2
            || PreferredIndex(defaultImageMethod, "Back", "FrontSpreadVertical", "Disc") != 1
            || PreferredIndex(defaultImageMethod, "Back", "Disc") != 0)
            throw new InvalidOperationException("Album image default selection must prefer Front Spread, then Front, then Vertical Front Spread, and otherwise preserve detection order.");

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
        foreach (var name in new[] { "spread.png", "PAGE_10.png", "PAGE_2.png", "liner notes.png",
                     "flyer.png", "poster.png", "back.png", "inlay.png", "disc.png", "obi.png", "other.png" })
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name)); encoder.Save(file);
        }
        var verticalVisual = new DrawingVisual();
        using (var dc = verticalVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 400, 400));
            dc.DrawRectangle(Brushes.RoyalBlue, null, new Rect(0, 400, 400, 400));
            // This marker begins at the upper-left of the lower panel and must
            // end at the lower-right after the inside cover is rotated 180°.
            dc.DrawRectangle(Brushes.Lime, null, new Rect(0, 400, 48, 48));
        }
        var verticalBitmap = new RenderTargetBitmap(400, 800, 96, 96, PixelFormats.Pbgra32);
        verticalBitmap.Render(verticalVisual); verticalBitmap.Freeze();
        var verticalEncoder = new PngBitmapEncoder(); verticalEncoder.Frames.Add(BitmapFrame.Create(verticalBitmap));
        using (var file = File.Create(Path.Combine(folder, "vertical-spread.png"))) verticalEncoder.Save(file);
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
        if (!content.Pages.Select(page => page.Role).SequenceEqual(new[] { "Front", "Page", "Page", "LinerNotes", "Flyer", "FrontInside" })
            || !content.Pages.Skip(1).Take(4).Select(page => page.Name).SequenceEqual(
                new[] { "PAGE_2.png", "PAGE_10.png", "liner notes.png", "flyer.png" })
            || content.Pages.Any(page => page.Name == "poster.png"))
            throw new InvalidOperationException("Viewer must show Front, PAGE, Liner Notes and Flyer in order, exclude Poster, and end with inside Front.");
        var derivedFront = content.Pages[0].LoadImage(); var derivedInside = content.Pages[^1].LoadImage();
        if (derivedFront.PixelWidth != derivedInside.PixelWidth || derivedFront.PixelHeight != derivedInside.PixelHeight)
            throw new InvalidOperationException("Front spread halves must produce matching cover pages.");

        roles["file:" + Path.Combine(folder, "spread.png")] = "FrontSpreadReversed";
        saveRoles.Invoke(null, [folder, roles]); itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        var reversedContent = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        var reversedFront = reversedContent.Pages[0].LoadImage();
        var reversedInside = reversedContent.Pages[^1].LoadImage();
        var reversedFrontPixel = new byte[4];
        var reversedInsidePixel = new byte[4];
        reversedFront.CopyPixels(new Int32Rect(20, 20, 1, 1),
            reversedFrontPixel, 4, 0);
        reversedInside.CopyPixels(new Int32Rect(20, 20, 1, 1),
            reversedInsidePixel, 4, 0);
        var reversedCase = (System.Runtime.CompilerServices.ITuple)itemType.GetMethod("LoadCaseArtwork",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, ["", 600])!;
        if (reversedFrontPixel[2] <= reversedFrontPixel[0] || reversedInsidePixel[0] <= reversedInsidePixel[2]
            || reversedCase[0] is not BitmapSource || reversedCase[1] is not BitmapSource)
            throw new InvalidOperationException($"Reversed Front Spread must use the left panel as Front and the right panel as inside Front in both booklet and 3D case: front={string.Join(',', reversedFrontPixel)}, inside={string.Join(',', reversedInsidePixel)}, case={reversedCase[0] is not null}/{reversedCase[1] is not null}.");
        roles["file:" + Path.Combine(folder, "spread.png")] = "FrontSpread";
        saveRoles.Invoke(null, [folder, roles]); itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);

        var getArtworkDirectory = typeof(MainWindow).GetMethod("GetDownloadedArtworkDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var artworkDirectory = (string)getArtworkDirectory.Invoke(null, [folder])!;
        Directory.CreateDirectory(artworkDirectory);
        var lowResolutionFront = BitmapSource.Create(120, 120, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat(new byte[] { 20, 40, 180, 255 }, 120 * 120).SelectMany(pixel => pixel).ToArray(), 120 * 4);
        var onlineFrontPath = Path.Combine(artworkDirectory, "online-cover.png");
        var onlineFrontEncoder = new PngBitmapEncoder(); onlineFrontEncoder.Frames.Add(BitmapFrame.Create(lowResolutionFront));
        using (var onlineFrontFile = File.Create(onlineFrontPath)) onlineFrontEncoder.Save(onlineFrontFile);
        itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        var albumIcon = (BitmapSource)itemType.GetProperty("CoverThumbnail")!.GetValue(item)!;
        var iconPixel = new byte[4]; albumIcon.CopyPixels(new Int32Rect(8, 8, 1, 1), iconPixel, 4, 0);
        if (iconPixel[0] <= iconPixel[2]
            || !Directory.EnumerateFiles(Path.Combine(data, "thumbnail-cache"), "*.png").Any())
            throw new InvalidOperationException("Album icons must prefer and cache the Front side of a Front Spread over online Front artwork.");
        var caseArtwork = (System.Runtime.CompilerServices.ITuple)itemType.GetMethod("LoadCaseArtwork", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(item, [artworkDirectory, 600])!;
        if (caseArtwork[0] is not BitmapSource caseFront || caseFront.PixelWidth <= lowResolutionFront.PixelWidth
            || caseArtwork[1] is not BitmapSource caseInside || caseInside.PixelWidth != caseFront.PixelWidth)
            throw new InvalidOperationException("A low-resolution downloaded Front must not replace a better Front Spread or remove its inside cover in 3D.");
        File.Delete(onlineFrontPath);
        itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        roles["file:" + Path.Combine(folder, "spread.png")] = "Other";
        roles["file:" + Path.Combine(folder, "vertical-spread.png")] = "FrontSpreadVertical";
        saveRoles.Invoke(null, [folder, roles]); itemType.GetMethod("RefreshImageCount")!.Invoke(item, null);
        var verticalContent = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        var verticalFront = verticalContent.Pages[0].LoadImage();
        var verticalInside = verticalContent.Pages[^1].LoadImage();
        var rotatedMarker = new byte[4];
        verticalInside.CopyPixels(new Int32Rect(verticalInside.PixelWidth - 20,
            verticalInside.PixelHeight - 20, 1, 1), rotatedMarker, 4, 0);
        if (verticalFront.PixelWidth != verticalFront.PixelHeight
            || verticalInside.PixelWidth != verticalInside.PixelHeight
            || verticalContent.FrontSpread.PixelWidth != verticalContent.FrontSpread.PixelHeight * 2
            || rotatedMarker[1] < 180 || rotatedMarker[0] > 100 || rotatedMarker[2] > 100)
            throw new InvalidOperationException("Vertical Front Spread must use the top as Front, rotate the lower inside cover 180°, and normalize the booklet spread horizontally.");
        var verticalCaseArtwork = (System.Runtime.CompilerServices.ITuple)itemType.GetMethod("LoadCaseArtwork",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, [artworkDirectory, 600])!;
        if (verticalCaseArtwork[0] is not BitmapSource verticalCaseFront
            || verticalCaseArtwork[1] is not BitmapSource verticalCaseInside
            || verticalCaseFront.PixelWidth != verticalCaseInside.PixelWidth
            || verticalCaseFront.PixelHeight != verticalCaseInside.PixelHeight)
            throw new InvalidOperationException("Vertical Front Spread must provide matching Front and inside Front textures to the 3D case.");
        if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_BOOKLET_VERTICAL_ONLY") == "1")
        {
            Console.WriteLine("Vertical Front Spread top/front, rotated lower/inside, thumbnail and 3D case tests passed.");
            return;
        }
        roles["file:" + Path.Combine(folder, "PAGE_2.png")] = "Other";
        roles["file:" + Path.Combine(folder, "other.png")] = "Page";
        saveRoles.Invoke(null, [folder, roles]);
        var reassigned = (BookletContent)itemType.GetMethod("LoadBooklet")!.Invoke(item, null)!;
        if (reassigned.Pages.Any(page => page.Name == "PAGE_2.png") || !reassigned.Pages.Any(page => page.Name == "other.png")
            || reassigned.Pages.First().Role != "Front" || reassigned.Pages.Last().Role != "FrontInside")
            throw new InvalidOperationException("Manual page roles must override filenames.");
        roles["file:" + Path.Combine(folder, "vertical-spread.png")] = "Other";
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
        var pageFrame = (Border)viewerType.GetField("_pageFrame", flags)!.GetValue(viewer)!;
        if (pageTurn.Visibility != Visibility.Collapsed || image.Opacity != 1
            || Math.Abs(pageFrame.Width - image.Width) > .01 || Math.Abs(pageFrame.Height - image.Height) > .01)
            throw new InvalidOperationException("Page navigation must keep the transition inside the destination page dimensions.");
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

    private static void VerifyContinuousCoverFlowKeyboard(BitmapSource image)
    {
        void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var items = Enumerable.Range(0, 8).Select(index => new JewelCaseCoverFlowItem(
            $"key-{index}", $"Album {index}", "Artist", "DIR", "Clear",
            image, null, null, null, null, null, null, false)).ToArray();
        var flow = new JewelCaseCoverFlow { CollectionPresentation = true };
        var window = new Window { Content = flow, Width = 900, Height = 650, ShowInTaskbar = false };
        var notifications = 0;
        flow.SelectionChanged += (_, _) => notifications++;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var navigate = typeof(JewelCaseCoverFlow).GetMethod("NavigateByKeyboard", flags)!;
        var finish = typeof(JewelCaseCoverFlow).GetMethod("FinishKeyboardNavigation", flags)!;
        try
        {
            flow.SetItems(items, "key-0");
            window.Show();
            window.UpdateLayout();
            navigate.Invoke(flow, [Key.Right, false]);
            Thread.Sleep(95);
            navigate.Invoke(flow, [Key.Right, true]);
            Thread.Sleep(95);
            navigate.Invoke(flow, [Key.Right, true]);
            if (flow.SelectedKey != "key-3" || notifications != 1)
                throw new InvalidOperationException("Held Right must advance continuously while deferring expensive selection updates.");
            var motions = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow)
                .GetField("_collectionMotions", flags)!.GetValue(flow)!;
            if (motions.Count == 0)
                throw new InvalidOperationException("Held arrow navigation must keep the shared CoverFlow motion active.");
            var firstMotion = motions.Values.Cast<object>().First();
            var duration = (double)firstMotion.GetType().GetProperty("DurationSeconds")!.GetValue(firstMotion)!;
            var easeOut = (bool)firstMotion.GetType().GetProperty("EaseOut")!.GetValue(firstMotion)!;
            if (Math.Abs(duration - .14) > .001 || easeOut)
                throw new InvalidOperationException("Held arrow navigation must use short linear motion for an uninterrupted flow.");
            finish.Invoke(flow, null);
            if (notifications != 2)
                throw new InvalidOperationException("Releasing a held arrow must publish the final album exactly once.");

            flow.RackPresentation = true;
            flow.SetItems(items, "key-0");
            notifications = 0;
            navigate.Invoke(flow, [Key.Right, false]);
            Thread.Sleep(95);
            navigate.Invoke(flow, [Key.Right, true]);
            var models = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow)
                .GetField("_collectionModels", flags)!.GetValue(flow)!;
            var oldSelectedModel = models["key-1"]!;
            var rackMotion = motions[oldSelectedModel]!;
            var rackMotionType = rackMotion.GetType();
            var rackFrom = rackMotionType.GetProperty("From")!.GetValue(rackMotion)!;
            var rackTo = rackMotionType.GetProperty("To")!.GetValue(rackMotion)!;
            var poseType = rackFrom.GetType();
            foreach (var component in new[] { "Y", "Z", "Scale", "Yaw", "Pitch" })
            {
                var property = poseType.GetProperty(component)!;
                var fromValue = (double)property.GetValue(rackFrom)!;
                var toValue = (double)property.GetValue(rackTo)!;
                if (Math.Abs(fromValue - toValue) > .001)
                    throw new InvalidOperationException($"A former rack selection retained a pickup {component} transition during continuous navigation.");
            }
            finish.Invoke(flow, null);
            if (notifications != 2)
                throw new InvalidOperationException("Rack navigation must publish its initial and final selections exactly once each.");
            Pump(350);
            if (motions.Count != 0)
                throw new InvalidOperationException("Held arrow navigation must settle and release its render motion after key-up.");
        }
        finally { window.Close(); }
        Console.WriteLine("Continuous held-arrow CoverFlow motion and deferred selection synchronization passed.");
    }

    private static void VerifyAlbumLibraryBrowser(BitmapSource image)
    {
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        void Pump(int milliseconds = 160)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 12000)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && started.ElapsedMilliseconds < timeoutMilliseconds) Pump(250);
        }

        BitmapSource Cover(byte red, byte green, byte blue)
        {
            return Solid(64, 64, red, green, blue);
        }
        BitmapSource Spine(byte red, byte green, byte blue)
        {
            return Solid(6, 64, red, green, blue);
        }
        BitmapSource Solid(int width, int height, byte red, byte green, byte blue)
        {
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = blue; pixels[index + 1] = green; pixels[index + 2] = red; pixels[index + 3] = 255;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        AlbumLibraryBrowserItem Item(string key, string title, string artist, BitmapSource cover, bool favorite = false) => new(
            new JewelCaseCoverFlowItem(key, title, artist, "DIR", "Clear", cover, null, null,
                Spine(244, 198, 28), Spine(220, 35, 176), null, null, false), cover,
            IsFavorite: favorite, TrackCount: 3);
        var items = new[]
        {
            Item("first", "First Album", "Alpha", Cover(180, 62, 54)),
            Item("second", "Second Album", "Beta", Cover(42, 135, 92)),
            Item("third", "Third Album", "Gamma", Cover(48, 92, 178)),
            Item("fourth", "Fourth Album", "Delta", Cover(173, 108, 38), favorite: true),
            Item("fifth", "Fifth Album", "Epsilon", Cover(109, 62, 160)),
            Item("sixth", "Sixth Album", "Zeta", Cover(32, 139, 154)),
            Item("seventh", "Seventh Album", "Eta", Cover(178, 63, 119)),
            Item("number", "1984", "Number Artist", Cover(94, 112, 146)),
            Item("japanese", "音の世界", "音楽家", Cover(126, 82, 148))
        };
        var requestedArtworkWidth = 0;
        var initialSecond = items[1];
        items[1] = new AlbumLibraryBrowserItem(initialSecond.CaseItem, initialSecond.TileCover,
            (width, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                requestedArtworkWidth = width;
                return Task.FromResult(initialSecond.CaseItem with { FrontCover = Cover(24, 116, 74) });
            });
        var previousRequests = 0;
        var pauseRequests = 0;
        var nextRequests = 0;
        var requestedVolume = -1d;
        var attractRequests = new List<AlbumBrowserTrackRequestedEventArgs>();
        var browser = new AlbumLibraryBrowserWindow(items, "second",
            () => new AlbumBrowserPlaybackState("Test Song  •  Test Artist", true, true, 1.25))
        {
            WindowState = WindowState.Normal,
            Width = 1180,
            Height = 760,
            ShowInTaskbar = false
        };
        browser.PreviousTrackRequested += (_, _) => previousRequests++;
        browser.PlayPauseRequested += (_, _) => pauseRequests++;
        browser.NextTrackRequested += (_, _) => nextRequests++;
        browser.VolumeChangedRequested += (_, args) => requestedVolume = args.Volume;
        browser.AttractTrackRequested += (_, args) => attractRequests.Add(args);
        try
        {
            browser.Show();
            browser.UpdateLayout();
            Pump(350);
            var tiles = (ListBox)browser.FindName("TileList");
            var filter = (TextBox)browser.FindName("FilterBox");
            var filterClear = (Button)browser.FindName("FilterClearButton");
            var sort = (ComboBox)browser.FindName("BrowserSortCombo");
            var tileSize = (Slider)browser.FindName("TileSizeSlider");
            var flow = (JewelCaseCoverFlow)browser.FindName("CoverFlow");
            var tileMode = (Button)browser.FindName("TileModeButton");
            var coverFlowMode = (Button)browser.FindName("CoverFlowModeButton");
            var rackMode = (Button)browser.FindName("RackModeButton");
            var previousTrack = (Button)browser.FindName("PreviousTrackButton");
            var playPause = (Button)browser.FindName("BrowserPlayPauseButton");
            var nextTrack = (Button)browser.FindName("NextTrackButton");
            var volume = (Slider)browser.FindName("BrowserVolumeSlider");
            var nowPlaying = (TextBlock)browser.FindName("NowPlayingTitleText");
            var attract = (Button)browser.FindName("AttractModeButton");
            var attractStatus = (TextBlock)browser.FindName("AttractStatusText");
            if (tiles.Items.Count != 9 || browser.SelectedKey != "second" || requestedArtworkWidth != 1600)
                throw new InvalidOperationException("Full-screen album tiles must load the complete library and preserve selection.");
            var attractPreload = (Task<AlbumLibraryBrowserItem>)typeof(AlbumLibraryBrowserWindow)
                .GetMethod("EnsureAttractArtworkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(browser, [items[1], CancellationToken.None])!;
            PumpUntil(() => attractPreload.IsCompleted);
            if (!attractPreload.IsCompletedSuccessfully || requestedArtworkWidth != 1200
                || attractPreload.Result.CaseItem.FrontCover is null)
                throw new InvalidOperationException("Attract mode must preload and cache its random destination artwork before roulette movement starts.");
            if (((AlbumLibraryBrowserItem)tiles.Items[0]).Artist != "Alpha"
                || ScrollViewer.GetCanContentScroll(tiles)
                || tiles.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem firstTile
                || firstTile.RenderTransform is not ScaleTransform)
                throw new InvalidOperationException("Album browser tiles must start in artist order and use pixel-smooth animated containers.");
            var initialFirstY = firstTile.TranslatePoint(new Point(), tiles).Y;
            var initialFifth = (ListBoxItem)tiles.ItemContainerGenerator.ContainerFromIndex(4)!;
            if (initialFifth.TranslatePoint(new Point(), tiles).Y <= initialFirstY + 20)
                throw new InvalidOperationException("Default tile size must wrap the fifth album to a new row at the test width.");
            tileSize.Value = 180;
            browser.UpdateLayout();
            Pump(180);
            var resizedFirst = (ListBoxItem)tiles.ItemContainerGenerator.ContainerFromIndex(0)!;
            var resizedFifth = (ListBoxItem)tiles.ItemContainerGenerator.ContainerFromIndex(4)!;
            if (Math.Abs(resizedFirst.ActualWidth - 180) > .1 || Math.Abs(resizedFirst.ActualHeight - 226) > .1
                || Math.Abs(resizedFifth.TranslatePoint(new Point(), tiles).Y
                    - resizedFirst.TranslatePoint(new Point(), tiles).Y) > 2)
                throw new InvalidOperationException("Tile size slider must resize and reflow album placement in real time.");
            tileSize.Value = 246;
            browser.UpdateLayout();
            Pump(120);
            sort.SelectedIndex = 1;
            Pump(260);
            if (browser.SortMode != AlbumBrowserSortMode.Album
                || ((AlbumLibraryBrowserItem)tiles.Items[0]).Title != "1984"
                || browser.SelectedKey != "second" || flow.ItemCount != 9)
                throw new InvalidOperationException("Album-name sorting must update tiles and CoverFlow without losing selection.");
            sort.SelectedIndex = 0;
            Pump(260);
            if (browser.SortMode != AlbumBrowserSortMode.Artist
                || ((AlbumLibraryBrowserItem)tiles.Items[0]).Artist != "Alpha")
                throw new InvalidOperationException("Artist sorting must be restorable in the full-screen album browser.");
            var initialButtons = VisualDescendants(browser).OfType<Button>()
                .Where(button => button.Tag is string).ToDictionary(button => (string)button.Tag, StringComparer.Ordinal);
            if (!initialButtons.ContainsKey("Favorite") || !initialButtons.ContainsKey("Number")
                || !initialButtons.ContainsKey("Latin:A") || !initialButtons.ContainsKey("Kana:あ")
                || !initialButtons.ContainsKey("Japanese") || !initialButtons.ContainsKey("Other"))
                throw new InvalidOperationException("Album browser initial search must expose favorites, numbers, A-Z, kana, kanji and other groups.");
            initialButtons["Favorite"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            if (tiles.Items.Count != 1 || flow.ItemCount != 1
                || ((AlbumLibraryBrowserItem)tiles.Items[0]).Key != "fourth")
                throw new InvalidOperationException("Favorite initial search must filter tiles and 3D CoverFlow together.");
            initialButtons["All"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sort.SelectedIndex = 1;
            Pump();
            initialButtons["Number"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            if (tiles.Items.Count != 1 || ((AlbumLibraryBrowserItem)tiles.Items[0]).Key != "number")
                throw new InvalidOperationException("Numeric album initial search failed.");
            initialButtons["Japanese"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            if (tiles.Items.Count != 1 || flow.ItemCount != 1
                || ((AlbumLibraryBrowserItem)tiles.Items[0]).Key != "japanese")
                throw new InvalidOperationException("Kanji album initial search failed.");
            initialButtons["All"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sort.SelectedIndex = 0;
            Pump();
            previousTrack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            playPause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            nextTrack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            volume.Value = 1.1;
            if (nowPlaying.Text != "Test Song  •  Test Artist" || !Equals(playPause.Content, "⏸ 一時停止")
                || previousRequests != 1 || pauseRequests != 1 || nextRequests != 1
                || Math.Abs(requestedVolume - 1.1) > .001)
                throw new InvalidOperationException("Album browser tile and 3D screens must share track title, transport and volume controls.");
            filter.Text = "Gamma";
            Pump();
            if (tiles.Items.Count != 1 || flow.ItemCount != 1 || filterClear.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Album browser search must filter tiles and 3D CoverFlow together.");
            filterClear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            if (filter.Text.Length != 0 || filterClear.Visibility != Visibility.Collapsed
                || tiles.Items.Count != 9 || !filter.IsKeyboardFocusWithin)
                throw new InvalidOperationException("Album browser search must provide a one-click clear button and retain search focus.");
            coverFlowMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            flow.SelectByKey("first", true);
            var synchronizedMotions = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow)
                .GetField("_collectionMotions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            if (synchronizedMotions.Count is < 1 or > 25)
                throw new InvalidOperationException("CoverFlow must animate all visible cases through one bounded render-synchronized motion set.");
            foreach (System.Windows.Media.Media3D.ContainerUIElement3D movingModel in synchronizedMotions.Keys)
            {
                var transforms = (System.Windows.Media.Media3D.Transform3DGroup)movingModel.Transform;
                var scale = (System.Windows.Media.Media3D.ScaleTransform3D)transforms.Children[0];
                var translation = (System.Windows.Media.Media3D.TranslateTransform3D)transforms.Children[3];
                if (DependencyPropertyHelper.GetValueSource(scale,
                        System.Windows.Media.Media3D.ScaleTransform3D.ScaleXProperty).IsAnimated
                    || DependencyPropertyHelper.GetValueSource(translation,
                        System.Windows.Media.Media3D.TranslateTransform3D.OffsetXProperty).IsAnimated)
                    throw new InvalidOperationException("CoverFlow must not create independent WPF animation clocks per transform property.");
            }
            Pump(650);
            PumpUntil(() => synchronizedMotions.Count == 0, 3000);
            if (synchronizedMotions.Count != 0
                || (bool)typeof(JewelCaseCoverFlow).GetField("_collectionRenderingSubscribed",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!)
                throw new InvalidOperationException("CoverFlow must release its shared render callback after motion settles.");
            var circularModels = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow).GetField("_collectionModels",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            var wrappedJapanese = (System.Windows.Media.Media3D.ContainerUIElement3D)circularModels["japanese"]!;
            var nextAlphabetic = (System.Windows.Media.Media3D.ContainerUIElement3D)circularModels["second"]!;
            var coverFlowModelBeforeRack = circularModels["first"];
            static double ModelX(System.Windows.Media.Media3D.ContainerUIElement3D model) =>
                ((System.Windows.Media.Media3D.TranslateTransform3D)
                    ((System.Windows.Media.Media3D.Transform3DGroup)model.Transform).Children[3]).OffsetX;
            if (ModelX(wrappedJapanese) >= 0 || ModelX(nextAlphabetic) <= 0)
                throw new InvalidOperationException("CoverFlow must wrap Japanese/kanji albums onto the left side when the A group is selected.");
            rackMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(650);
            var rebuiltRackModels = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow).GetField("_collectionModels",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            if (ModelX((System.Windows.Media.Media3D.ContainerUIElement3D)rebuiltRackModels["japanese"]!) >= 0
                || ReferenceEquals(coverFlowModelBeforeRack, rebuiltRackModels["first"]))
                throw new InvalidOperationException("CD rack mode must retain circular order but rebuild cases without CoverFlow floor reflections.");
            typeof(JewelCaseCoverFlow).GetMethod("MoveSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(flow, [-1]);
            Pump(650);
            if (flow.SelectedKey != "japanese")
                throw new InvalidOperationException("CoverFlow navigation must loop from the first album to the Japanese/kanji end of the library.");
            coverFlowMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            attract.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => attractRequests.Count >= 1);
            if (!browser.IsAttractMode || attractRequests.Count != 1
                || attractRequests[0].TrackIndex is < 0 or >= 3
                || flow.Visibility != Visibility.Visible)
                throw new InvalidOperationException($"Attract mode must run its roulette inside the currently selected CoverFlow mode. active={browser.IsAttractMode}, requests={attractRequests.Count}, flow={flow.Visibility}, status={attractStatus.Text}");
            if (!browser.AdvanceAttractMode())
                throw new InvalidOperationException("A natural track ending must be captured while Attract mode is active.");
            PumpUntil(() => attractRequests.Count >= 2);
            if (attractRequests.Count != 2 || attractRequests[1].AlbumKey == attractRequests[0].AlbumKey)
                throw new InvalidOperationException("Attract mode must advance to a different recent album after each song.");
            tileMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!browser.AdvanceAttractMode())
                throw new InvalidOperationException("Attract mode must remain active after switching to tile mode.");
            PumpUntil(() => attractRequests.Count >= 3);
            if (attractRequests.Count != 3 || tiles.Visibility != Visibility.Visible || flow.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Attract roulette must move and settle without forcing tile mode back to 3D.");
            rackMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!browser.AdvanceAttractMode())
                throw new InvalidOperationException("Attract mode must remain active after switching to CD rack mode.");
            PumpUntil(() => attractRequests.Count >= 4);
            if (attractRequests.Count != 4 || flow.Visibility != Visibility.Visible || !flow.RackPresentation)
                throw new InvalidOperationException("Attract roulette must run inside CD rack mode without changing presentation mode.");
            attract.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (browser.IsAttractMode || browser.AdvanceAttractMode())
                throw new InvalidOperationException("Attract mode must stop immediately and return natural-end handling to the normal player.");
            rackMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            flow.SelectByKey("third", true);
            Pump(650);
            var rackModels = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow).GetField("_collectionModels",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            var rackSelected = (System.Windows.Media.Media3D.ContainerUIElement3D)rackModels["third"]!;
            var rackSide = (System.Windows.Media.Media3D.ContainerUIElement3D)rackModels["second"]!;
            var rackSelectedTransforms = (System.Windows.Media.Media3D.Transform3DGroup)rackSelected.Transform;
            var rackSideTransforms = (System.Windows.Media.Media3D.Transform3DGroup)rackSide.Transform;
            var rackSelectedYaw = (System.Windows.Media.Media3D.AxisAngleRotation3D)
                ((System.Windows.Media.Media3D.RotateTransform3D)rackSelectedTransforms.Children[2]).Rotation;
            var rackSideYaw = (System.Windows.Media.Media3D.AxisAngleRotation3D)
                ((System.Windows.Media.Media3D.RotateTransform3D)rackSideTransforms.Children[2]).Rotation;
            var rackSelectedTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)rackSelectedTransforms.Children[3];
            if (!flow.RackPresentation || Math.Abs(Math.Abs(rackSelectedYaw.Angle) - 18) > .1
                || Math.Abs(rackSideYaw.Angle - 90) > .1 || Math.Abs(rackSelectedTranslation.OffsetZ - .65) > .02
                || typeof(JewelCaseCoverFlow).GetField("_rackFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow) is null)
                throw new InvalidOperationException("CD rack mode must pack side cases Spine-forward and pull the selected Front case out of the rack.");
            coverFlowMode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(650);
            if (flow.RackPresentation)
                throw new InvalidOperationException("Switching back to CoverFlow must remove the rack layout.");
            flow.SelectByKey("second", true);
            Pump(500);
            var modelMap = (System.Collections.IDictionary)typeof(JewelCaseCoverFlow).GetField("_collectionModels",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            var retainedModel = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["second"]!;
            var collectionItem = items[1].CaseItem with
            {
                BackCover = Cover(80, 70, 60),
                SpineCover = Spine(40, 50, 60),
                RightSpineCover = Spine(60, 50, 40)
            };
            typeof(JewelCaseCoverFlowItem).GetProperty("CollectionPresentation",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(collectionItem, true);
            var exterior = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [collectionItem, 0, 0d, 0d, .36d, false])!;
            var exteriorBody = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelUIElement3D)exterior.Children[0]).Model;
            var shell = typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!
                .GetMethod("GetCoverFlowShellGeometry", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            var shellGeometry = shell.GetType().GetProperty("BottomTray")!.GetValue(shell);
            var topMouldedEdges = (System.Windows.Media.Media3D.MeshGeometry3D)
                shell.GetType().GetProperty("TopMouldedEdges")!.GetValue(shell)!;
            var topEdgePositions = topMouldedEdges.Positions;
            var topEdgeIndices = topMouldedEdges.TriangleIndices;
            const double shellWidth = 2.42;
            const double shellHeight = 2.12;
            for (var triangle = 0; triangle + 2 < topEdgeIndices.Count; triangle += 3)
            {
                var points = new[]
                {
                    topEdgePositions[topEdgeIndices[triangle]],
                    topEdgePositions[topEdgeIndices[triangle + 1]],
                    topEdgePositions[topEdgeIndices[triangle + 2]]
                };
                var whollyInsideRail = points.All(point => Math.Abs(point.Y) > shellHeight * .445)
                    || points.All(point => point.X < -shellWidth * .402)
                    || points.All(point => point.X > shellWidth * .485);
                if (!whollyInsideRail)
                    throw new InvalidOperationException("A front-lid triangle straddles clear and moulded acrylic and can create a diagonal side highlight.");
            }
            var exteriorImageBrushes = exteriorBody.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>()
                .Select(model => model.Material).OfType<System.Windows.Media.Media3D.MaterialGroup>()
                .SelectMany(material => material.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>())
                .Select(material => material.Brush).OfType<ImageBrush>().ToList();
            if (exteriorBody.Children.Count != 11
                || !ReferenceEquals(((System.Windows.Media.Media3D.GeometryModel3D)exteriorBody.Children[0]).Geometry, shellGeometry)
                || exteriorImageBrushes.Count != 5
                || exteriorImageBrushes.Any(brush => brush.Stretch != Stretch.Fill)
                || exteriorImageBrushes.Any(brush => RenderOptions.GetBitmapScalingMode(brush) != BitmapScalingMode.HighQuality))
                throw new InvalidOperationException("Collection cases must share the correctly proportioned 3D View STL exterior and map complete, uncropped Front, Back and both Spine textures.");
            var collectionTrayMould = (System.Windows.Media.Media3D.MeshGeometry3D)
                ((System.Windows.Media.Media3D.GeometryModel3D)exteriorBody.Children[9]).Geometry;
            if (collectionTrayMould.Positions.Count < 8
                || collectionTrayMould.Positions.Max(point => point.X)
                    - collectionTrayMould.Positions.Min(point => point.X) < .20)
                throw new InvalidOperationException("CoverFlow and rack cases must retain the tray spine moulding omitted by the exterior STL.");
            var blackCollectionItem = collectionItem with { TrayColorMode = "Black" };
            typeof(JewelCaseCoverFlowItem).GetProperty("CollectionPresentation",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(blackCollectionItem, true);
            var blackExterior = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [blackCollectionItem, 0, 0d, 0d, .36d, false])!;
            var blackExteriorBody = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelUIElement3D)blackExterior.Children[0]).Model;
            var blackTrayMould = (System.Windows.Media.Media3D.MeshGeometry3D)
                ((System.Windows.Media.Media3D.GeometryModel3D)blackExteriorBody.Children[9]).Geometry;
            if (blackTrayMould.Positions.Count < 140)
                throw new InvalidOperationException("Opaque CoverFlow tray spines must retain their dense vertical moulded ribs.");
            var rackExterior = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [collectionItem, 0, 0d, 0d, .36d, true])!;
            var rackExteriorBody = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelUIElement3D)rackExterior.Children[0]).Model;
            if (rackExteriorBody.Children.Count != exteriorBody.Children.Count - 1)
                throw new InvalidOperationException("CD rack cases must omit the overlapping floor-reflection quad that appears as a rectangular shadow.");
            var artworkGlow = exteriorBody.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>()
                .Select(model => model.Material).OfType<System.Windows.Media.Media3D.MaterialGroup>()
                .SelectMany(material => material.Children.OfType<System.Windows.Media.Media3D.EmissiveMaterial>())
                .Select(material => material.Brush.Opacity).DefaultIfEmpty().Max();
            var viewport = (System.Windows.Controls.Viewport3D)typeof(JewelCaseCoverFlow)
                .GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow)!;
            var collectionLights = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelVisual3D)viewport.Children[0]).Content;
            var brightestLight = collectionLights.Children.OfType<System.Windows.Media.Media3D.DirectionalLight>()
                .Select(light => Math.Max(light.Color.R, Math.Max(light.Color.G, light.Color.B))).DefaultIfEmpty().Max();
            if (artworkGlow > .23 || brightestLight > 190)
                throw new InvalidOperationException("Collection artwork lighting must preserve pale-cover detail instead of clipping to white.");
            var exteriorSpines = exteriorBody.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>()
                .Skip(7).Take(2).Select(model => (System.Windows.Media.Media3D.MeshGeometry3D)model.Geometry).ToList();
            var expectedSpineX = new[]
            {
                -1.2125,
                1.2125
            };
            if (exteriorSpines.Count != 2
                || exteriorSpines.Select((mesh, index) => (mesh, index)).Any(entry =>
                    entry.mesh.Positions.Any(point =>
                        Math.Abs(point.X - expectedSpineX[entry.index]) > .0001
                        || Math.Abs(point.Z) > DxJewelCaseScene.StandardVisibleSpineDepth / 2 + .0001))
                || exteriorSpines.Any(mesh => Math.Abs(
                    mesh.Positions.Max(point => point.Z) - mesh.Positions.Min(point => point.Z)
                    - DxJewelCaseScene.StandardVisibleSpineDepth) > .0002))
                throw new InvalidOperationException("Collection Spine artwork must remain inside both case lips but sit on the visible side surface so WPF transparent-shell depth writes cannot hide it.");
            var exteriorBack = (System.Windows.Media.Media3D.MeshGeometry3D)
                ((System.Windows.Media.Media3D.GeometryModel3D)exteriorBody.Children[6]).Geometry;
            if (exteriorBack.Positions.Any(point => point.Z >= -DxJewelCaseScene.StandardCaseDepth / 2))
                throw new InvalidOperationException("Collection Back artwork must sit on the visible rear surface so the transparent shell cannot hide it.");
            var inlayPixels = Enumerable.Repeat(new byte[] { 74, 92, 138, 255 }, 150 * 118)
                .SelectMany(pixel => pixel).ToArray();
            var inlayOnlyArtwork = BitmapSource.Create(150, 118, 96, 96, PixelFormats.Bgra32,
                null, inlayPixels, 150 * 4);
            inlayOnlyArtwork.Freeze();
            var inlayOnlyItem = collectionItem with
            {
                BackCover = null,
                SpineCover = null,
                RightSpineCover = null,
                InlayCover = inlayOnlyArtwork
            };
            var inlayExterior = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [inlayOnlyItem, 1, 0d, 0d, .36d, false])!;
            var inlayExteriorBody = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelUIElement3D)inlayExterior.Children[0]).Model;
            if (inlayExteriorBody.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>()
                    .Select(model => model.Material).OfType<System.Windows.Media.Media3D.MaterialGroup>()
                    .SelectMany(material => material.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>())
                    .Count(material => material.Brush is ImageBrush) != 5)
                throw new InvalidOperationException("Collection exterior must use Inlay panel and folds when explicit Back/Spine images are absent.");
            var invalidSpineItem = collectionItem with
            {
                SpineCover = Cover(210, 210, 210),
                RightSpineCover = Cover(210, 210, 210),
                InlayCover = null
            };
            var invalidSpineExterior = (System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [invalidSpineItem, 1, 0d, 0d, .36d, false])!;
            var invalidSpineBody = (System.Windows.Media.Media3D.Model3DGroup)
                ((System.Windows.Media.Media3D.ModelUIElement3D)invalidSpineExterior.Children[0]).Model;
            if (invalidSpineBody.Children.Count != 9)
                throw new InvalidOperationException("Square/non-spine artwork and missing Spine placeholders must not create squeezed white side faces.");
            flow.SelectByKey("third", true);
            var retainedAgain = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["second"]!;
            var retainedTransforms = (System.Windows.Media.Media3D.Transform3DGroup)retainedAgain.Transform;
            var retainedTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)retainedTransforms.Children[3];
            if (!ReferenceEquals(retainedModel, retainedAgain) || !synchronizedMotions.Contains(retainedAgain))
                throw new InvalidOperationException("3D CoverFlow must retain and animate case models instead of replacing the scene.");
            Pump(650);
            PumpUntil(() => synchronizedMotions.Count == 0, 3000);
            if (browser.SelectedKey != "third" || (tiles.SelectedItem as AlbumLibraryBrowserItem)?.Key != "third")
                throw new InvalidOperationException("Tile and 3D CoverFlow selections must remain synchronized.");
            var settledLeftModel = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["second"]!;
            var leftYaw = (System.Windows.Media.Media3D.AxisAngleRotation3D)
                ((System.Windows.Media.Media3D.RotateTransform3D)
                    ((System.Windows.Media.Media3D.Transform3DGroup)settledLeftModel.Transform).Children[2]).Rotation;
            var rightModel = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["sixth"]!;
            var rightYaw = (System.Windows.Media.Media3D.AxisAngleRotation3D)
                ((System.Windows.Media.Media3D.RotateTransform3D)
                    ((System.Windows.Media.Media3D.Transform3DGroup)rightModel.Transform).Children[2]).Rotation;
            if (Math.Abs(leftYaw.Angle + 55) > .01 || Math.Abs(rightYaw.Angle - 55) > .01)
                throw new InvalidOperationException($"CoverFlow side cases must turn inward so their inner Spine faces the selected album. left={leftYaw.Angle:0.0}, right={rightYaw.Angle:0.0}");
            flow.SelectByKey("second", true);
            var incomingFromLeft = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["second"]!;
            if (!synchronizedMotions.Contains(incomingFromLeft))
                throw new InvalidOperationException("A case selected from the left must turn toward the viewer without flipping across centre.");
            flow.SelectByKey("fourth", true);
            flow.SelectByKey("fifth", true);
            var rapidTarget = (System.Windows.Media.Media3D.ContainerUIElement3D)modelMap["fifth"]!;
            if (!synchronizedMotions.Contains(rapidTarget))
                throw new InvalidOperationException("Repeated CoverFlow selection must continue from an animated pose without snapping.");
            flow.SelectByKey("third", true);
            var activated = 0;
            browser.ItemActivated += (_, _) => activated++;
            typeof(AlbumLibraryBrowserWindow).GetMethod("ActivateSelected",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(browser, null);
            Pump();
            if (!browser.IsVisible || activated != 1 || (tiles.SelectedItem as AlbumLibraryBrowserItem)?.IsPlaying != true)
                throw new InvalidOperationException("Starting playback must keep the full-screen browser open and update its playing state.");
            if (Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ALBUM_BROWSER_PREVIEW") is { Length: > 0 } previewDirectory)
            {
                Directory.CreateDirectory(previewDirectory);
                flow.SelectByKey("fourth", true);
                void Save(string name)
                {
                    browser.UpdateLayout(); Pump(220);
                    var bitmap = new RenderTargetBitmap((int)browser.ActualWidth, (int)browser.ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(browser);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(previewDirectory, name));
                    encoder.Save(output);
                }
                tiles.Visibility = Visibility.Visible; flow.Visibility = Visibility.Collapsed;
                Save("album-browser-tiles.png");
                tiles.Visibility = Visibility.Collapsed; flow.Visibility = Visibility.Visible; flow.RackPresentation = false;
                Save("album-browser-coverflow.png");
                flow.RackPresentation = true; Pump(650);
                Save("album-browser-rack.png");
                flow.RackPresentation = false;
                typeof(JewelCaseCoverFlow).GetField("_caseYaw", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(flow, 180d);
                typeof(JewelCaseCoverFlow).GetMethod("RebuildScene", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(flow, null);
                Pump(650);
                Save("album-browser-back.png");
            }
        }
        finally { browser.Close(); }
        var inheritedSearchBrowser = new AlbumLibraryBrowserWindow(items, "third", initialFilterText: "Gamma")
        { ShowInTaskbar = false, WindowState = WindowState.Normal, Width = 900, Height = 620 };
        try
        {
            inheritedSearchBrowser.Show(); inheritedSearchBrowser.UpdateLayout(); Pump(120);
            var inheritedFilter = (TextBox)inheritedSearchBrowser.FindName("FilterBox");
            var inheritedTiles = (ListBox)inheritedSearchBrowser.FindName("TileList");
            if (inheritedFilter.Text != "Gamma" || inheritedTiles.Items.Count != 1
                || ((AlbumLibraryBrowserItem)inheritedTiles.Items[0]).Key != "third")
                throw new InvalidOperationException("Album browser must inherit and apply the main-window search text.");
            inheritedFilter.Clear(); Pump();
            if (inheritedTiles.Items.Count != items.Length)
                throw new InvalidOperationException("Clearing an inherited browser search must restore the complete library.");
        }
        finally { inheritedSearchBrowser.Close(); }
        Console.WriteLine("Full-screen album browser smooth tile/sort/search/3D CoverFlow/CD rack synchronization passed.");
    }

    private static void VerifyLibraryLoadingIndicator()
    {
        var window = new MainWindow { ShowInTaskbar = false, WindowState = WindowState.Normal, Width = 960, Height = 620 };
        var type = typeof(MainWindow);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window,
            type.GetMethod("MainWindow_Loaded", flags)!);
        try
        {
            window.Show();
            var task = (Task)type.GetMethod("ShowLibraryLoadingAsync", flags)!.Invoke(window,
                ["音楽ファイルを読み込んでいます…", "保存済みライブラリを復元中 12/120", false])!;
            var frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            var overlay = (Border)window.FindName("LibraryLoadingOverlay");
            var title = (TextBlock)window.FindName("LibraryLoadingTitle");
            var detail = (TextBlock)window.FindName("LibraryLoadingDetail");
            var progress = (ProgressBar)window.FindName("LibraryLoadingProgress");
            if (overlay.Visibility != Visibility.Visible || title.Text.Length == 0
                || !detail.Text.Contains("12/120") || !progress.IsIndeterminate || !overlay.IsHitTestVisible)
                throw new InvalidOperationException("Music library loading must show a rendered progress overlay before long-running work starts.");
            type.GetMethod("HideLibraryLoading", flags)!.Invoke(window, null);
            if (overlay.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Music library loading overlay must close after work completes.");

            task = (Task)type.GetMethod("ShowLibraryLoadingAsync", flags)!.Invoke(window,
                ["音楽ファイルを読み込んでいます…", "登録フォルダを確認しています", true])!;
            frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            var card = (Border)window.FindName("LibraryLoadingCard");
            type.GetMethod("UpdateLibraryLoading", flags)!.Invoke(window, ["解析中 37/100", 37, 100]);
            if (overlay.Visibility != Visibility.Visible || overlay.IsHitTestVisible
                || card.HorizontalAlignment != HorizontalAlignment.Right
                || card.VerticalAlignment != VerticalAlignment.Bottom
                || progress.IsIndeterminate || progress.Maximum != 100 || progress.Value != 37
                || Mouse.OverrideCursor is not null)
                throw new InvalidOperationException("Background library scan must remain click-through and show coalesced determinate progress.");
            type.GetMethod("HideLibraryLoading", flags)!.Invoke(window, null);

            var bulk = new BulkObservableCollection<int> { 1, 2 };
            var collectionChanges = 0;
            System.Collections.Specialized.NotifyCollectionChangedAction? lastAction = null;
            bulk.CollectionChanged += (_, e) => { collectionChanges++; lastAction = e.Action; };
            bulk.ReplaceAll(Enumerable.Range(0, 2000));
            if (collectionChanges != 1 || lastAction != System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                || bulk.Count != 2000 || bulk[1999] != 1999)
                throw new InvalidOperationException("Bulk library replacement must notify the UI exactly once.");

            var cachedFolder = Path.Combine(Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")!, "cached-album");
            Directory.CreateDirectory(cachedFolder);
            File.WriteAllBytes(Path.Combine(cachedFolder, "Front.jpg"), [0, 0, 0, 0]);
            var cachedAlbum = new ZipAlbum
            {
                Path = cachedFolder,
                Tracks = [new ZipTrack
                {
                    TrackNumber = 1, FileName = "01.mp3", SourcePath = Path.Combine(cachedFolder, "01.mp3"),
                    Title = "Cached", Artist = "Artist", Album = "Cached", IsArchiveEntry = false
                }],
                ImageCount = 1
            };
            type.GetMethod("RestoreCachedAlbums", flags)!.Invoke(window, [new[] { cachedAlbum }]);
            var restoredAlbums = (System.Collections.IList)type.GetField("_albums", flags)!.GetValue(window)!;
            var restoredItem = restoredAlbums[0]!;
            if (restoredAlbums.Count != 1
                || (bool)restoredItem.GetType().GetProperty("ArtworkSummaryLoaded")!.GetValue(restoredItem)!
                || restoredItem.GetType().GetProperty("CoverThumbnail")!.GetValue(restoredItem) is not null)
                throw new InvalidOperationException("Startup cache restore must defer filesystem and artwork work until background maintenance.");
        }
        finally { window.Close(); }
        Console.WriteLine("Blocking startup overlay, click-through rescan progress, and single-reset bulk replacement passed.");
    }

    private static void VerifyTagEditorFilenameEditing()
    {
        var album = new ZipAlbum
        {
            Path = @"C:\Music\Batch.zip.mp3",
            Tracks =
            [
                new ZipTrack { FileName = "Album/01 original.mp3", SourcePath = @"C:\Music\Batch.zip.mp3",
                    IsArchiveEntry = true, Title = "One", Artist = "Artist", Album = "Album", TrackNumber = 1 }
            ]
        };
        var window = new TagEditorWindow(album) { ShowInTaskbar = false };
        var selectedAll = false;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            var grid = (DataGrid)window.FindName("TagsGrid");
            var row = grid.Items[0];
            grid.CurrentItem = row;
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
            grid.BeginEdit(); window.UpdateLayout();
            if (grid.Columns[0].GetCellContent(row) is not TextBox editor)
                throw new InvalidOperationException("File name cell did not enter text editing mode.");
            editor.SelectAll();
            selectedAll = editor.SelectedText == editor.Text;
            editor.Text = "01 renamed.mp3";
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            var save = VisualDescendants(window).OfType<Button>().Single(button => Equals(button.Content, "まとめて保存"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        if (window.ShowDialog() != true || !selectedAll || window.EditedTracks.Count != 1)
            throw new InvalidOperationException("Editable/selectable file name did not produce a saved update.");
        var update = window.EditedTracks[0];
        if (update.FileName != "Album/01 original.mp3" || update.EffectiveTargetFileName != "Album/01 renamed.mp3")
            throw new InvalidOperationException("Editing an archive file name must preserve its internal folder.");
        Console.WriteLine("Tag editor selectable/editable file name and saved rename mapping passed.");
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
            var playbackState = new JewelCasePlaybackState("Test Track  •  Test Artist", true, true, .42);
            var selectedAlbumIsPlaying = true;
            var previousRequests = 0;
            var pauseRequests = 0;
            var nextRequests = 0;
            var requestedVolume = -1d;
            if (fullScreen)
            {
                flow.PlaybackStateProvider = () => playbackState;
                flow.PlaybackActiveProvider = _ => selectedAlbumIsPlaying;
                flow.PreviousTrackRequested += (_, _) => previousRequests++;
                flow.PlayPauseRequested += (_, _) => pauseRequests++;
                flow.NextTrackRequested += (_, _) => nextRequests++;
                flow.VolumeChangedRequested += (_, e) => requestedVolume = e.Volume;
            }
            flow.SetItems([item], item.Key);
            var window = new Window { Width = 1000, Height = 760, Content = flow, ShowInTaskbar = false };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var scene = typeof(JewelCaseCoverFlow).GetField("_dxScene", flags)!.GetValue(flow)!;
            var type = scene.GetType();
            var viewport = (HelixToolkit.Wpf.SharpDX.Viewport3DX)type.GetProperty("Viewport")!.GetValue(scene)!;
            var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_discRoot", flags)!.GetValue(scene)!;
            var caseTransform = (System.Windows.Media.Media3D.Transform3DGroup)type.GetField("_caseTransform", flags)!.GetValue(scene)!;
            var translation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField("_discTranslation", flags)!.GetValue(scene)!;
            var spin = (System.Windows.Media.Media3D.AxisAngleRotation3D)type.GetField("_discSpinRotation", flags)!.GetValue(scene)!;
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
                var playbackBar = (Border)typeof(JewelCaseCoverFlow).GetField("_playbackBar", flags)!.GetValue(flow)!;
                if (fullScreen)
                {
                    var title = (TextBlock)typeof(JewelCaseCoverFlow).GetField("_playbackTitleText", flags)!.GetValue(flow)!;
                    var previousButton = (Button)typeof(JewelCaseCoverFlow).GetField("_previousTrackButton", flags)!.GetValue(flow)!;
                    var pauseButton = (Button)typeof(JewelCaseCoverFlow).GetField("_playPauseButton", flags)!.GetValue(flow)!;
                    var nextButton = (Button)typeof(JewelCaseCoverFlow).GetField("_nextTrackButton", flags)!.GetValue(flow)!;
                    var volume = (Slider)typeof(JewelCaseCoverFlow).GetField("_playbackVolumeSlider", flags)!.GetValue(flow)!;
                    if (playbackBar.Visibility != Visibility.Visible || title.Text != playbackState.TrackDisplay
                        || Math.Abs(volume.Value - playbackState.Volume) > .001)
                        throw new InvalidOperationException("Full-screen 3D playback controls did not reflect the active track.");
                    selectedAlbumIsPlaying = false;
                    FlowCall("UpdateDiscPlayback");
                    if ((bool)type.GetField("_discPlaying", flags)!.GetValue(scene)!)
                        throw new InvalidOperationException("A global playing track must not rotate the selected case's disc when the active album key differs.");
                    selectedAlbumIsPlaying = true;
                    FlowCall("UpdateDiscPlayback");
                    previousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    volume.Value = 1.24;
                    if (previousRequests != 1 || pauseRequests != 1 || nextRequests != 1 || Math.Abs(requestedVolume - 1.24) > .001)
                        throw new InvalidOperationException("Full-screen 3D playback controls did not forward commands.");
                    playbackState = playbackState with { IsPlaying = false };
                    Pump();
                    if (!(pauseButton.Content?.ToString()?.Contains(LocalizationService.Select("再生", "Play")) ?? false))
                        throw new InvalidOperationException("Paused 3D playback must expose a resume button.");
                    playbackState = playbackState with { HasTrack = false };
                    Pump();
                    if (playbackBar.Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("3D playback controls must hide after playback stops.");
                    playbackState = playbackState with { HasTrack = true, IsPlaying = true };
                    Pump();
                }
                else if (playbackBar.Visibility != Visibility.Collapsed)
                {
                    throw new InvalidOperationException("Embedded 3D case must not show the full-screen playback bar.");
                }
                var stationaryAngle = spin.Angle;
                Call("SetDiscPlaying", true); Pump(140);
                if (fullScreen)
                {
                    playbackState = playbackState with { IsPlaying = false };
                    selectedAlbumIsPlaying = false;
                }
                Call("SetDiscPlaying", false);
                if (Math.Abs(spin.Angle - stationaryAngle) < 20)
                    throw new InvalidOperationException("A playing album must rotate the disc at audio-CD speed.");
                var pausedAngle = spin.Angle; Pump(140);
                if (Math.Abs(spin.Angle - pausedAngle) > .01)
                    throw new InvalidOperationException("Pausing playback must stop the disc at its current angle.");
                Call("SetDiscPlaying", true); Pump(80);
                Call("SetItem", item with { Key = "next-album" }, -10d, -2d);
                if ((bool)type.GetField("_discPlaying", flags)!.GetValue(scene)!
                    || Math.Abs(spin.Angle) > .001)
                    throw new InvalidOperationException("Selecting a different album must stop the previous disc clock and reset the new disc to its initial angle.");
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
                Call("EndDiscDrag");
                var activations = 0;
                flow.DiscActivated += (_, _) => activations++;
                var activateDisc = typeof(JewelCaseCoverFlow).GetMethod("TryActivateDisc", flags)!;
                if (!(bool)activateDisc.Invoke(flow, [grab])! || activations != 1)
                    throw new InvalidOperationException("Double-clicking the visible disc must activate its album exactly once.");
                var beginFlow = typeof(JewelCaseCoverFlow).GetMethod("TryBeginDiscDrag", flags)!;
                if (!(bool)beginFlow.Invoke(flow, [grab])! || !flow.IsMouseCaptured)
                    throw new InvalidOperationException("Disc drag must capture pointer in the view.");
                if (viewport.EnableSSAO || viewport.IsShadowMappingEnabled
                    || viewport.FXAALevel != HelixToolkit.SharpDX.FXAALevel.None
                    || viewport.MSAA != HelixToolkit.SharpDX.MSAALevel.Two)
                    throw new InvalidOperationException("Interactive motion must pause only SSAO, shadows and FXAA while retaining MSAA geometry quality.");
                var leftEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseUpEvent };
                FlowCall("OnRotationStarted", flow, leftEvent);
                if ((bool)typeof(JewelCaseCoverFlow).GetField("_isRotating", flags)!.GetValue(flow)!)
                    throw new InvalidOperationException("Disc drag must not also rotate case.");
                FlowCall("OnDiscDragEnded", flow, leftEvent);
                if (flow.IsMouseCaptured || (bool)typeof(JewelCaseCoverFlow).GetField("_isDraggingDisc", flags)!.GetValue(flow)!)
                    throw new InvalidOperationException("Mouse up must release disc drag capture.");
                if (!viewport.EnableSSAO || !viewport.IsShadowMappingEnabled
                    || viewport.FXAALevel != HelixToolkit.SharpDX.FXAALevel.Medium)
                    throw new InvalidOperationException("Pointer release must immediately restore the full-quality still frame.");
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
        Console.WriteLine("Disc interaction: full-screen playback controls, hit-only activation, rotation/pause, dragging, collision and animation interruption passed in both views.");
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

    private static void VerifyDiscArtworkCrop()
    {
        var helper = typeof(MainWindow).Assembly.GetType("ZipMp3Player.DiscArtwork")!;
        var crop = helper.GetMethod("CropScannerMargin", BindingFlags.Static | BindingFlags.Public)!;
        var split = helper.GetMethod("SplitTwoDiscs", BindingFlags.Static | BindingFlags.Public)!;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 320, 320));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(18, 62, 29)), null,
                new Point(160, 160), 124, 124);
        }
        var scan = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32);
        scan.Render(visual);
        var cropped = (BitmapSource)crop.Invoke(null, [scan])!;
        if (cropped.PixelWidth is < 240 or > 250 || cropped.PixelWidth != cropped.PixelHeight)
            throw new InvalidOperationException($"Disc scanner margin crop was too loose: {cropped.PixelWidth}x{cropped.PixelHeight}.");

        var tightPixels = new byte[180 * 180 * 4];
        for (var y = 0; y < 180; y++)
        for (var x = 0; x < 180; x++)
        {
            var p = (y * 180 + x) * 4;
            tightPixels[p] = (byte)(20 + x);
            tightPixels[p + 1] = (byte)(30 + y);
            tightPixels[p + 2] = (byte)(40 + (x + y) / 2);
            tightPixels[p + 3] = 255;
        }
        var tight = BitmapSource.Create(180, 180, 96, 96, PixelFormats.Bgra32, null, tightPixels, 180 * 4);
        var unchanged = (BitmapSource)crop.Invoke(null, [tight])!;
        if (unchanged.PixelWidth != 180 || unchanged.PixelHeight != 180)
            throw new InvalidOperationException("Edge-to-edge Disc artwork must not be cropped.");

        BitmapSource TwoDiscFixture(bool horizontal)
        {
            var width = horizontal ? 640 : 320;
            var height = horizontal ? 320 : 640;
            var fixture = new DrawingVisual();
            using (var drawing = fixture.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                var firstCenter = horizontal ? new Point(160, 160) : new Point(160, 160);
                var secondCenter = horizontal ? new Point(480, 160) : new Point(160, 480);
                drawing.DrawEllipse(Brushes.DarkRed, null, firstCenter, 124, 124);
                drawing.DrawEllipse(Brushes.DarkBlue, null, secondCenter, 124, 124);
            }
            var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            result.Render(fixture);
            result.Freeze();
            return result;
        }

        foreach (var horizontal in new[] { true, false })
        {
            var source = TwoDiscFixture(horizontal);
            var sourceSize = (source.PixelWidth, source.PixelHeight);
            var pair = split.Invoke(null, [source])!;
            var pairType = pair.GetType();
            var first = (BitmapSource)pairType.GetField("Item1")!.GetValue(pair)!;
            var second = (BitmapSource)pairType.GetField("Item2")!.GetValue(pair)!;
            if (first.PixelWidth is < 240 or > 250 || second.PixelWidth is < 240 or > 250
                || first.PixelWidth != first.PixelHeight || second.PixelWidth != second.PixelHeight
                || (source.PixelWidth, source.PixelHeight) != sourceSize)
                throw new InvalidOperationException($"Two-disc {(horizontal ? "horizontal" : "vertical")} extraction failed: {first.PixelWidth}x{first.PixelHeight}, {second.PixelWidth}x{second.PixelHeight}.");
        }
        Console.WriteLine("Disc scanner-margin crop and non-destructive horizontal/vertical two-disc extraction tests passed.");
    }

    private static void VerifyRearInsertCrops()
    {
        var helper = typeof(MainWindow).Assembly.GetType("ZipMp3Player.RearInsertArtwork")!;
        var getRegions = helper.GetMethod("GetRegions")!;
        var cropWhiteBorder = helper.GetMethod("CropWhiteBorder")!;
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
        var deepBottomBorder = Fixture(512, 512, new(0, 0, 512, 456), Colors.White);
        var croppedFrontOrBack = (BitmapSource)cropWhiteBorder.Invoke(null, [deepBottomBorder])!;
        if (croppedFrontOrBack.PixelWidth != 512 || croppedFrontOrBack.PixelHeight != 456)
            throw new InvalidOperationException($"Standalone Front/Back white-border crop failed: {croppedFrontOrBack.PixelWidth}x{croppedFrontOrBack.PixelHeight}.");
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
        var obiVisual = new DrawingVisual();
        using (var dc = obiVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 240, 1200));
            dc.DrawRectangle(Brushes.DimGray, null, new Rect(240, 0, 120, 1200));
            dc.DrawRectangle(Brushes.DarkOrange, null, new Rect(360, 0, 240, 1200));
            dc.DrawRectangle(Brushes.White, null, new Rect(238, 0, 3, 1200));
            dc.DrawRectangle(Brushes.White, null, new Rect(359, 0, 3, 1200));
        }
        var obi = new RenderTargetBitmap(600, 1200, 96, 96, PixelFormats.Pbgra32);
        obi.Render(obiVisual); obi.Freeze();
        var obiHelper = typeof(MainWindow).Assembly.GetType("ZipMp3Player.SpineCardArtwork")!;
        var obiRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [obi])!;
        var detectedBack = (Int32Rect)obiRegions.GetType().GetProperty("Back")!.GetValue(obiRegions)!;
        var detectedSpine = (Int32Rect)obiRegions.GetType().GetProperty("Spine")!.GetValue(obiRegions)!;
        var detectedFront = (Int32Rect)obiRegions.GetType().GetProperty("Front")!.GetValue(obiRegions)!;
        if (Math.Abs(detectedBack.Width - 240) > 12 || Math.Abs(detectedSpine.Width - 120) > 24
            || Math.Abs(detectedFront.Width - 240) > 12)
            throw new InvalidOperationException($"Spine Card folds were not detected symmetrically: {detectedBack} / {detectedSpine} / {detectedFront}");
        var offsetVisual = new DrawingVisual();
        using (var dc = offsetVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 652, 1118));
            dc.DrawRectangle(Brushes.White, null, new Rect(285, 0, 100, 1118));
            // Strong symmetric artwork boundaries must not beat the slightly
            // off-centre pair of real fold boundaries.
            dc.DrawRectangle(Brushes.DarkRed, null, new Rect(208, 0, 8, 1118));
            dc.DrawRectangle(Brushes.DarkRed, null, new Rect(436, 0, 8, 1118));
        }
        var offsetObi = new RenderTargetBitmap(652, 1118, 96, 96, PixelFormats.Pbgra32);
        offsetObi.Render(offsetVisual); offsetObi.Freeze();
        var offsetRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [offsetObi])!;
        var offsetBack = (Int32Rect)offsetRegions.GetType().GetProperty("Back")!.GetValue(offsetRegions)!;
        var offsetSpine = (Int32Rect)offsetRegions.GetType().GetProperty("Spine")!.GetValue(offsetRegions)!;
        var offsetFront = (Int32Rect)offsetRegions.GetType().GetProperty("Front")!.GetValue(offsetRegions)!;
        if (Math.Abs(offsetBack.Width - 285) > 10 || Math.Abs(offsetSpine.Width - 100) > 14
            || Math.Abs(offsetFront.Width - 267) > 10)
            throw new InvalidOperationException($"Slightly off-centre Spine Card folds were not preserved: {offsetBack} / {offsetSpine} / {offsetFront}");
        var asymmetricVisual = new DrawingVisual();
        using (var dc = asymmetricVisual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 18)), null, new Rect(0, 0, 620, 1000));
            dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(310, 0, 100, 1000));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 24, 25)), null, new Rect(410, 0, 210, 1000));
            // Track-list and price-box edges are strong printed features, not folds.
            dc.DrawRectangle(Brushes.Gray, null, new Rect(82, 0, 5, 1000));
            dc.DrawRectangle(Brushes.DarkRed, null, new Rect(472, 0, 7, 1000));
        }
        var asymmetricObi = new RenderTargetBitmap(620, 1000, 96, 96, PixelFormats.Pbgra32);
        asymmetricObi.Render(asymmetricVisual); asymmetricObi.Freeze();
        var asymmetricRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [asymmetricObi])!;
        var asymmetricBack = (Int32Rect)asymmetricRegions.GetType().GetProperty("Back")!.GetValue(asymmetricRegions)!;
        var asymmetricSpine = (Int32Rect)asymmetricRegions.GetType().GetProperty("Spine")!.GetValue(asymmetricRegions)!;
        var asymmetricFront = (Int32Rect)asymmetricRegions.GetType().GetProperty("Front")!.GetValue(asymmetricRegions)!;
        if (Math.Abs(asymmetricBack.Width - 310) > 10 || Math.Abs(asymmetricSpine.Width - 100) > 14
            || Math.Abs(asymmetricFront.Width - 210) > 10)
            throw new InvalidOperationException($"Strongly off-centre Spine Card folds were not preserved: {asymmetricBack} / {asymmetricSpine} / {asymmetricFront}");
        var borderedVisual = new DrawingVisual();
        using (var dc = borderedVisual.RenderOpen())
        {
            // Typical scanner export: a straight white frame surrounds the
            // complete obi. It must be removed before folds are measured,
            // without treating the intentionally pale spine as empty margin.
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 660, 1060));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 18)), null, new Rect(20, 30, 310, 1000));
            dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(330, 30, 100, 1000));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 24, 25)), null, new Rect(430, 30, 210, 1000));
        }
        var borderedObi = new RenderTargetBitmap(660, 1060, 96, 96, PixelFormats.Pbgra32);
        borderedObi.Render(borderedVisual); borderedObi.Freeze();
        var borderedRegions = SpineCardArtwork.GetRegions(borderedObi);
        if (Math.Abs(borderedRegions.Back.X - 20) > 2 || Math.Abs(borderedRegions.Back.Y - 30) > 2
            || Math.Abs(borderedRegions.Back.Width - 310) > 10
            || Math.Abs(borderedRegions.Spine.Width - 100) > 14
            || Math.Abs(borderedRegions.Front.Width - 210) > 10
            || Math.Abs(borderedRegions.Back.Height - 1000) > 4
            || borderedRegions.Back.Y != borderedRegions.Spine.Y
            || borderedRegions.Spine.Y != borderedRegions.Front.Y)
            throw new InvalidOperationException($"White scanner margin was not removed before Spine Card folding: "
                + $"{borderedRegions.Back} / {borderedRegions.Spine} / {borderedRegions.Front}");
        SpineCardArtwork.SetManualFolds(asymmetricObi, .44, .64);
        var manuallyAdjusted = SpineCardArtwork.GetRegions(asymmetricObi);
        if (Math.Abs(manuallyAdjusted.Spine.X - 273) > 1 || Math.Abs(manuallyAdjusted.Spine.Width - 124) > 1)
            throw new InvalidOperationException("Manual Spine Card fold guides must override automatic detection non-destructively.");
        var foldEditor = new SpineCardFoldEditorWindow(asymmetricObi, .44, .64, manual: true)
        { ShowInTaskbar = false, WindowState = WindowState.Normal, Width = 700, Height = 560 };
        try
        {
            foldEditor.Show(); foldEditor.UpdateLayout();
            var guideField = typeof(SpineCardFoldEditorWindow).GetField("_leftGuide",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (((System.Windows.Controls.Primitives.Thumb)guideField.GetValue(foldEditor)!).Opacity > .05)
                throw new InvalidOperationException("The wide Spine Card fold-guide hit target must remain visually transparent.");
            typeof(SpineCardFoldEditorWindow).GetMethod("MoveGuide", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(foldEditor, [true, 18d]);
            if (foldEditor.LeftFold <= .44 || foldEditor.RightFold != .64 || foldEditor.UseAutomatic)
                throw new InvalidOperationException("Spine Card fold editor must move guides independently and retain manual mode.");
        }
        finally { foldEditor.Close(); }
        var printedEdgeVisual = new DrawingVisual();
        using (var dc = printedEdgeVisual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 12, 12)), null, new Rect(0, 0, 652, 1113));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(72, 68, 78)), null, new Rect(278, 0, 106, 1113));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 38, 44)), null, new Rect(384, 0, 268, 1113));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(120, 115, 125)), null, new Rect(405, 0, 7, 1113));
        }
        var printedEdgeObi = new RenderTargetBitmap(652, 1113, 96, 96, PixelFormats.Pbgra32);
        printedEdgeObi.Render(printedEdgeVisual); printedEdgeObi.Freeze();
        var printedEdgeRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [printedEdgeObi])!;
        var printedEdgeBack = (Int32Rect)printedEdgeRegions.GetType().GetProperty("Back")!.GetValue(printedEdgeRegions)!;
        var printedEdgeSpine = (Int32Rect)printedEdgeRegions.GetType().GetProperty("Spine")!.GetValue(printedEdgeRegions)!;
        if (Math.Abs(printedEdgeBack.Width - 278) > 10 || Math.Abs(printedEdgeSpine.Width - 106) > 14)
            throw new InvalidOperationException($"Printed flap artwork was mistaken for a Spine Card fold: {printedEdgeBack} / {printedEdgeSpine}");
        var opaqueObi = (BitmapSource)obiHelper.GetMethod("MakeOpaque",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [obi])!;
        var separatorPixel = new byte[4];
        opaqueObi.CopyPixels(new Int32Rect(239, 100, 1, 1), separatorPixel, 4, 0);
        var printedPixel = new byte[4];
        opaqueObi.CopyPixels(new Int32Rect(100, 100, 1, 1), printedPixel, 4, 0);
        if (separatorPixel[3] != 255 || printedPixel[3] != 255)
            throw new InvalidOperationException("Every Spine Card pixel, including white separator bands, must remain opaque paper.");
        var item = new JewelCaseCoverFlowItem("inlay", "Inlay test", "Artist", "ZIP", "Clear",
            null, null, null, null, null, scan, scan, false) { SpineCard = obi, SecondDiscImage = scan };
        var frontSpreadPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_FRONT_SPREAD");
        if (!string.IsNullOrWhiteSpace(frontSpreadPath))
        {
            var spread = new BitmapImage(new Uri(frontSpreadPath)); spread.Freeze();
            var side = Math.Min(spread.PixelWidth / 2, spread.PixelHeight);
            var y = Math.Max(0, (spread.PixelHeight - side) / 2);
            var inside = new CroppedBitmap(spread, new Int32Rect(0, y, side, side)); inside.Freeze();
            var front = new CroppedBitmap(spread,
                new Int32Rect(spread.PixelWidth - side, y, side, side)); front.Freeze();
            item = item with { FrontCover = front, InsideFrontCover = inside };
        }
        var sameExterior = typeof(JewelCaseCoverFlow).GetMethod("SameExteriorArtwork",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        if ((bool)sameExterior.Invoke(null, [item, item with { SpineCard = printedEdgeObi }])!)
            throw new InvalidOperationException("A changed Spine Card must invalidate the cached 3D exterior model.");
        var spineCardScanPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_SPINE_CARD_SCAN");
        if (!string.IsNullOrWhiteSpace(spineCardScanPath))
        {
            var actualObi = new BitmapImage(new Uri(spineCardScanPath)); actualObi.Freeze();
            item = item with { SpineCard = actualObi };
            var actualRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [actualObi])!;
            Console.WriteLine($"SPINE CARD {actualObi.PixelWidth}x{actualObi.PixelHeight}: "
                + $"{actualRegions.GetType().GetProperty("Back")!.GetValue(actualRegions)} / "
                + $"{actualRegions.GetType().GetProperty("Spine")!.GetValue(actualRegions)} / "
                + $"{actualRegions.GetType().GetProperty("Front")!.GetValue(actualRegions)}");
        }
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
        if (!string.IsNullOrEmpty(previewDirectory)) Directory.CreateDirectory(previewDirectory);
        var window = new Window { Width = 1100, Height = 720, Content = viewport, ShowInTaskbar = false };
        try
        {
            if (!string.IsNullOrEmpty(previewDirectory)) window.Show();
            foreach (var mode in new[] { "Clear", "White", "Black", "Gray" })
            {
                var current = item with { TrayColorMode = mode };
                if (mode == "Clear")
                {
                    type.GetMethod("SetItem")!.Invoke(scene, [current with { FrontCover = scan }, -12.0, 15.0]);
                    var frontBookletRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                        "_bookletRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    if (frontBookletRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Any(mesh => mesh.Material?.Name is "Booklet front fold artwork" or "Booklet rear fold artwork"))
                        throw new InvalidOperationException("A thick image-wrapped booklet side must not project as a vertical band beside the Spine Card.");
                    var frontArtwork = frontBookletRoot.Children
                        .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Where(mesh => mesh.Material is HelixToolkit.Wpf.SharpDX.PhongMaterial
                            { Name: "Artwork", RenderDiffuseMap: true })
                        .OrderByDescending(mesh => ((HelixToolkit.SharpDX.MeshGeometry3D)mesh.Geometry!)
                            .Positions!.Average(point => point.Z))
                        .First();
                    var shell = type.GetMethod("GetCoverFlowShellGeometry",
                        BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                    var expectedLeft = (float)shell.GetType().GetProperty("FrontLeft")!.GetValue(shell)!;
                    var actualLeft = ((HelixToolkit.SharpDX.MeshGeometry3D)frontArtwork.Geometry!)
                        .Positions!.Min(point => point.X);
                    if (Math.Abs(actualLeft - expectedLeft) > .0001f)
                        throw new InvalidOperationException("Front artwork must extend beneath the hinge-side retaining moulding without a black vertical gap.");
                    var artworkGeometry = (HelixToolkit.SharpDX.MeshGeometry3D)frontArtwork.Geometry!;
                    var artworkPositions = artworkGeometry.Positions!;
                    var actualRight = artworkPositions.Max(point => point.X);
                    var frontPanel = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                        "_frontPanelRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var retainers = frontPanel.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Single(mesh => mesh.Material?.Name == "Booklet retaining clips");
                    var retainerMaterial = (HelixToolkit.Wpf.SharpDX.PBRMaterial)retainers.Material!;
                    var retainerPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)retainers.Geometry!).Positions!;
                    const float unitX = 2.42f / 142f;
                    const float unitY = 2.12f / 125f;
                    var leftEdgeOffsetMm = (actualLeft - retainerPositions.Min(point => point.X)) / unitX;
                    var rightEdgeOffsetMm = (retainerPositions.Max(point => point.X) - actualRight) / unitX;
                    var leftGuidePoints = retainerPositions.Where(point => point.X < actualLeft).ToList();
                    var leftGuideHeightMm = (leftGuidePoints.Max(point => point.Y)
                        - leftGuidePoints.Min(point => point.Y)) / unitY;
                    var rightClipPoints = retainerPositions.Where(point => point.X > actualRight - 1.3f * unitX).ToList();
                    var artworkZ = artworkPositions.Average(point => point.Z);
                    var rightClipFrontZ = rightClipPoints.Max(point => point.Z);
                    if (Math.Abs(leftEdgeOffsetMm - 1.175f) > .08f
                        || Math.Abs(rightEdgeOffsetMm - .65f) > .08f
                        || Math.Abs(leftGuideHeightMm - 122f) > .08f
                        || rightClipPoints.Any(point => Math.Abs(point.Y) < 15f * unitY)
                        || Math.Abs(rightClipFrontZ - artworkZ) > .2f * unitX
                        || retainerMaterial.AlbedoColor.Alpha > .18f
                        || retainerMaterial.RenderEnvironmentMap)
                        throw new InvalidOperationException("Booklet retainers must match img284: a slim left guide and two separated clear right clips.");
                }
                type.GetMethod("SetItem")!.Invoke(scene, [current, -12.0, 15.0]);
                var frontPanelRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                    "_frontPanelRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var restoredLidRails = frontPanelRoot.Children
                    .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Front lid moulded rails").ToList();
                if (restoredLidRails.Count != 2)
                    throw new InvalidOperationException("The front lid must retain separate upper and lower moulded-rail meshes.");
                var clearFrontPanels = frontPanelRoot.Children
                    .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Clear front panel").ToList();
                if (clearFrontPanels.Count != 1
                    || ((HelixToolkit.SharpDX.MeshGeometry3D)clearFrontPanels[0].Geometry!).Positions!.Count < 4)
                    throw new InvalidOperationException("The lid must retain one independent clear outer front panel.");
                var clearFrontPanelPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)
                    clearFrontPanels[0].Geometry!).Positions!;
                if (clearFrontPanelPositions.Max(point => point.Z)
                        - clearFrontPanelPositions.Min(point => point.Z) < .75f * .0177f)
                    throw new InvalidOperationException("The clear front panel must retain visible physical thickness in edge views.");
                var restoredRailPositions = restoredLidRails.Select(rail =>
                    ((HelixToolkit.SharpDX.MeshGeometry3D)rail.Geometry!).Positions!).ToList();
                if (restoredRailPositions.Any(positions =>
                        positions.Any(point => Math.Abs(point.Y) <= 2.12f * .445f))
                    || restoredRailPositions.Any(positions =>
                        positions.Any(point => Math.Sign(point.Y) != Math.Sign(positions[0].Y))))
                    throw new InvalidOperationException("Each restored moulded rail must stay on one horizontal edge and contain no vertical artwork-side wall.");
                var upperRail = restoredRailPositions.Single(positions => positions[0].Y > 0);
                var lowerRail = restoredRailPositions.Single(positions => positions[0].Y < 0);
                var expectedClawReach = 5.0f * .445f;
                if (upperRail.Min(point => point.Y) > 125f * .445f / 2 - expectedClawReach
                    || lowerRail.Max(point => point.Y) < -125f * .445f / 2 + expectedClawReach
                    || upperRail.Count < 100 || lowerRail.Count < 100)
                    throw new InvalidOperationException("Each lid rail must include two rounded catches projecting into the booklet area.");
                var minimumRailDepth = 8.7f * .0177f;
                if (restoredRailPositions.Any(positions =>
                        positions.Max(point => point.Z) - positions.Min(point => point.Z)
                            < minimumRailDepth))
                    throw new InvalidOperationException("Each lid rail must extend from the front plate to the case mating plane so it remains visible edge-on.");
                // The 168 relief prisms add thousands of positions to each
                // combined rail mesh. This distinguishes the ribbed rails
                // from the former plain boxes without depending on scene
                // scale, which varies in the harness.
                if (upperRail.Count < 4000 || lowerRail.Count < 4000)
                    throw new InvalidOperationException("Both front-lid rails must retain the scan-matched fine moulded rib field.");
                if (frontPanelRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Any(mesh => mesh.Material?.Name == "Clear front-lid side walls"))
                    throw new InvalidOperationException("The duplicate moulded-edge shell must not be rendered over Front artwork.");
                if (frontPanelRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Any(mesh => mesh.Material?.Name == "Clear opening-side Spine frame"))
                    throw new InvalidOperationException("The front lid must not contain a detached full-depth Spine plate.");
                var baseRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                    "_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var openingSideWalls = baseRoot.Children
                    .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Clear opening-side Spine frame").ToList();
                if (openingSideWalls.Count != 1)
                    throw new InvalidOperationException("The fixed shell must contain one opening-side Spine frame.");
                var openingWallGeometry = (HelixToolkit.SharpDX.MeshGeometry3D)
                    openingSideWalls[0].Geometry!;
                var openingWallPositions = openingWallGeometry.Positions!;
                if (openingWallPositions.Min(point => point.Z) > -DxJewelCaseScene.StandardCaseDepth / 2 + .001f
                    || openingWallPositions.Max(point => point.Z) < DxJewelCaseScene.StandardCaseDepth / 2 - .001f)
                    throw new InvalidOperationException("The opening-side frame must reach both ends of the Spine.");
                var openingWallIndices = openingWallGeometry.TriangleIndices!;
                for (var index = 0; index < openingWallIndices.Count; index += 3)
                {
                    var centroid = (openingWallPositions[openingWallIndices[index]]
                        + openingWallPositions[openingWallIndices[index + 1]]
                        + openingWallPositions[openingWallIndices[index + 2]]) / 3f;
                    if (Math.Abs(centroid.Y) < 2.12f * .35f
                        && Math.Abs(centroid.Z) < DxJewelCaseScene.StandardCaseDepth * .25f)
                        throw new InvalidOperationException("The opening-side frame must not contain a transparent face across the printed Spine centre.");
                }
                var spineRibModels = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Tray spine ribs").ToList();
                var spineGrooveModels = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Tray spine groove floor").ToList();
                var spineTransitionModels = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                    .Where(mesh => mesh.Material?.Name == "Tray spine transition").ToList();
                if (mode == "Black")
                {
                    var scanDark = new HelixToolkit.Maths.Color4(0.0331f, 0.0319f, 0.0395f, 1);
                    var trayMaterials = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Where(mesh => mesh.Material?.Name is "Tray" or "Tray spine ribs" or "Tray spine transition")
                        .Select(mesh => (HelixToolkit.Wpf.SharpDX.PBRMaterial)mesh.Material!)
                        .ToList();
                    if (trayMaterials.Count < 3 || trayMaterials.Any(material =>
                            Math.Abs(material.AlbedoColor.Red - scanDark.Red) > .0001f
                            || Math.Abs(material.AlbedoColor.Green - scanDark.Green) > .0001f
                            || Math.Abs(material.AlbedoColor.Blue - scanDark.Blue) > .0001f))
                        throw new InvalidOperationException("Dark tray, scan ribs and stepped shoulder must share the img129 scan-matched resin colour.");
                }
                if (mode == "Clear")
                {
                    var flatSpineSkins = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Where(mesh => mesh.Material?.Name == "Tray")
                        .Select(mesh => (HelixToolkit.SharpDX.MeshGeometry3D)mesh.Geometry!)
                        .Where(geometry => geometry.Positions is { Count: 4 }
                            && geometry.Positions.Max(point => point.Z) - geometry.Positions.Min(point => point.Z) < .0001f)
                        .ToList();
                    if (spineRibModels.Count != 0 || spineGrooveModels.Count != 0
                        || spineTransitionModels.Count != 0 || flatSpineSkins.Count != 1)
                        throw new InvalidOperationException("A clear tray must use one flat spine skin without ribs, boxed side walls or a transition shoulder.");
                }
                else
                {
                    var spineTransitionModel = spineTransitionModels.Single();
                    var spineTransitionPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)spineTransitionModel.Geometry!).Positions!;
                    var transitionXSpanMm = (spineTransitionPositions.Max(point => point.X)
                        - spineTransitionPositions.Min(point => point.X)) / (2.42f / 142f);
                    var transitionZSpanMm = (spineTransitionPositions.Max(point => point.Z)
                        - spineTransitionPositions.Min(point => point.Z))
                        / (DxJewelCaseScene.StandardCaseDepth / DxJewelCaseScene.StandardCaseDepthMm);
                    if (spineTransitionPositions.Count < 12
                        || Math.Abs(transitionXSpanMm - 1.2f) > .02f
                        || transitionZSpanMm < 3.2f)
                        throw new InvalidOperationException($"Tray spine must join the main tray through a continuous 1.2 mm stepped shoulder: vertices={spineTransitionPositions.Count}, X={transitionXSpanMm:0.00} mm, Z={transitionZSpanMm:0.00} mm.");
                    var spineRibModel = spineRibModels.Single();
                    var spineGrooveModel = spineGrooveModels.Single();
                    var spineRibPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)spineRibModel.Geometry!).Positions!;
                    var spineGroovePositions = ((HelixToolkit.SharpDX.MeshGeometry3D)spineGrooveModel.Geometry!).Positions!;
                    var millimetreX = 2.42f / 142f;
                    var millimetreZ = DxJewelCaseScene.StandardCaseDepth / DxJewelCaseScene.StandardCaseDepthMm;
                    var ribXSpan = spineRibPositions.Max(point => point.X) - spineRibPositions.Min(point => point.X);
                    var ribZSpan = spineRibPositions.Max(point => point.Z) - spineRibPositions.Min(point => point.Z);
                    var ribFrontZ = spineRibPositions.Max(point => point.Z);
                    var ribMinX = spineRibPositions.Min(point => point.X);
                    var ribMaxX = spineRibPositions.Max(point => point.X);
                    var competingTrayZ = baseRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Where(mesh => mesh != spineRibModel && mesh.Material?.Name == "Tray")
                        .SelectMany(mesh => ((HelixToolkit.SharpDX.MeshGeometry3D)mesh.Geometry!).Positions!)
                        .Where(point => point.X >= ribMinX && point.X <= ribMaxX)
                        .Select(point => point.Z).DefaultIfEmpty(float.MinValue).Max();
                    var ribEdges = spineRibPositions.Select(point => Math.Round(point.X, 5)).Distinct().Count();
                    if (spineRibPositions.Count < 17 * 8 || ribEdges != 34
                        || Math.Abs(ribXSpan / millimetreX - 12.70f) > .12f
                        || Math.Abs(ribZSpan / millimetreZ - .12f) > .01f
                        || competingTrayZ >= ribFrontZ - .0001f
                        || spineGroovePositions.Count < 4)
                        throw new InvalidOperationException($"Tray spine scan ribs must retain 17 unobstructed raised strips across the 13 mm band with 0.30 mm grooves and 0.12 mm relief: vertices={spineRibPositions.Count}, edges={ribEdges}, X={ribXSpan / millimetreX:0.00} mm, relief={ribZSpan / millimetreZ:0.00} mm, rib/front={ribFrontZ:0.00000}, competing={competingTrayZ:0.00000}.");
                }
                if (mode == "Clear")
                {
                    var secondDiscRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                        "_secondDiscRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    if (secondDiscRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .All(mesh => mesh.Material?.Name != "Disc 2 artwork"))
                        throw new InvalidOperationException("A two-disc scan must create an independently textured second disc beneath Disc 1.");
                    var wrappingUpper = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                        "_wrappingUpperRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var wrappingLower = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                        "_wrappingLowerRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var wrappingWidth = (float)type.GetField("_wrappingCaseWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var wrappingDepth = (float)type.GetField("_wrappingCaseDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var tapeGroups = new[] { "_tearTapeFrontRoot", "_tearTapeBackRoot", "_tearTapeSideRoot", "_tearTapeRibbonRoot" }
                        .Select(name => (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(
                            name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!).ToList();
                    var foldedFacets = wrappingUpper.Children.Concat(wrappingLower.Children)
                        .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Where(mesh => mesh.Material?.Name == "Caramel wrapping folded facet").ToList();
                    if (wrappingUpper.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                            .Count(mesh => mesh.Material?.Name is "Caramel wrapping film" or "Caramel wrapping fold") < 3
                        || wrappingLower.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                            .All(mesh => mesh.Material?.Name != "Caramel wrapping film")
                        || foldedFacets.Count != 4
                        || wrappingUpper.Children.Concat(wrappingLower.Children)
                            .OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                            .Any(mesh => mesh.Material?.Name == "Caramel wrapping seal ribs")
                        || tapeGroups.SelectMany(group => group.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>())
                            .Count(mesh => mesh.Material?.Name?.StartsWith("Caramel tear tape", StringComparison.Ordinal) == true) != 6)
                        throw new InvalidOperationException("Caramel wrapping must keep smooth film sides without vertical ribs, plus folded corner facets, a four-sided tear tape, bent pull tab and deformable ribbon.");
                    if (foldedFacets.Select(mesh => (HelixToolkit.SharpDX.MeshGeometry3D)mesh.Geometry!)
                        .Any(geometry =>
                        {
                            var positions = geometry.Positions
                                ?? throw new InvalidOperationException("Wrapping fold facet has no vertices.");
                            return positions.Max(point => point.Y) - positions.Min(point => point.Y) > .0001f
                                || positions.Min(point => point.Y) <= 1.06f;
                        }))
                        throw new InvalidOperationException("Caramel wrapping end folds must lie on the narrow top side instead of covering Front or Back artwork.");
                    if (Math.Abs(wrappingWidth - (2.42f + DxJewelCaseScene.WrappingSideClearance * 2)) > .0001
                        || Math.Abs(wrappingDepth - (DxJewelCaseScene.StandardCaseDepth
                            + DxJewelCaseScene.WrappingFaceClearance * 2)) > .0001
                        || DxJewelCaseScene.WrappingFaceClearance
                            - DxJewelCaseScene.SpineCardFlapClearance < .003f)
                        throw new InvalidOperationException("Caramel film must hug the case and obi while retaining a non-coplanar anti-flicker gap.");
                    var tapeY = (float)type.GetField("_wrappingTapeY", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var pullTab = tapeGroups[2].Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Single(mesh => mesh.Material?.Name == "Caramel tear tape ribbon");
                    var glossyFilm = wrappingUpper.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
                        .Select(mesh => mesh.Material).OfType<HelixToolkit.Wpf.SharpDX.PBRMaterial>()
                        .Single(material => material.Name == "Caramel wrapping film");
                    var tabPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)pullTab.Geometry!).Positions!;
                    var tabXSpan = tabPositions.Max(point => point.X) - tabPositions.Min(point => point.X);
                    var tabZSpan = tabPositions.Max(point => point.Z) - tabPositions.Min(point => point.Z);
                    if (tapeY > -.78f || tabXSpan > .045f || tabZSpan is < .012f or > .028f
                        || pullTab.Material is not HelixToolkit.Wpf.SharpDX.PBRMaterial { ReflectanceFactor: > .6f }
                        || glossyFilm.AlbedoColor.Alpha < .16f
                        || glossyFilm.ReflectanceFactor < .80f || glossyFilm.RoughnessFactor > .03f
                        || glossyFilm.ClearCoatStrength < .95f || glossyFilm.ClearCoatRoughness > .01f)
                        throw new InvalidOperationException($"The tear tape must sit near the lower edge and its short tab must bend forward instead of protruding as a flat rectangle: Y={tapeY:0.000}, X={tabXSpan:0.000}, Z={tabZSpan:0.000}.");
                    var upperTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField(
                        "_wrappingUpperTranslation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var lowerTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField(
                        "_wrappingLowerTranslation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var upperPeel = (System.Windows.Media.Media3D.AxisAngleRotation3D)type.GetField(
                        "_wrappingUpperPeel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var lowerPeel = (System.Windows.Media.Media3D.AxisAngleRotation3D)type.GetField(
                        "_wrappingLowerPeel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var tearScale = (System.Windows.Media.Media3D.ScaleTransform3D)type.GetField(
                        "_tearTapeScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.20]);
                    var ribbonModel = tapeGroups[^1].Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().Single();
                    var ribbonPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)ribbonModel.Geometry!).Positions!;
                    if (ribbonPositions.Count == 0)
                        throw new InvalidOperationException($"Pulled ribbon geometry was not rebuilt: progress={type.GetProperty("WrappingProgress")!.GetValue(scene)}, visible={ribbonModel.Visibility}.");
                    var ribbonYSpan = ribbonPositions.Max(point => point.Y) - ribbonPositions.Min(point => point.Y);
                    var ribbonZSpan = ribbonPositions.Max(point => point.Z) - ribbonPositions.Min(point => point.Z);
                    if (ribbonModel.Visibility != Visibility.Visible || ribbonPositions.Count < 80
                        || ribbonYSpan < .15f || ribbonZSpan < .04f)
                        throw new InvalidOperationException($"A pulled tear tape must become a curved, sagging and twisting ribbon instead of translating rigidly: visible={ribbonModel.Visibility}, points={ribbonPositions.Count}, Y={ribbonYSpan:0.000}, Z={ribbonZSpan:0.000}.");
                    if (pullTab.Visibility != Visibility.Hidden)
                        throw new InvalidOperationException("The stationary pull tab must become the moving ribbon end immediately after pulling begins.");
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.40]);
                    ribbonPositions = ((HelixToolkit.SharpDX.MeshGeometry3D)ribbonModel.Geometry!).Positions!;
                    if (ribbonModel.Visibility != Visibility.Visible
                        || ribbonPositions.Any(point => point.Z >= 0))
                        throw new InvalidOperationException("Once the tear tape reaches the rear face, its entire loose ribbon must remain behind the case instead of crossing onto the Front artwork.");
                    type.GetMethod("SetWrappingCut")!.Invoke(scene, [true, false]);
                    if (Math.Abs((double)type.GetProperty("WrappingProgress")!.GetValue(scene)!
                            - DxJewelCaseScene.TearCompleteProgress) > .001
                        || tearScale.ScaleX > .02 || upperTranslation.OffsetX != 0 || lowerTranslation.OffsetX != 0
                        || pullTab.Visibility != Visibility.Hidden)
                        throw new InvalidOperationException("Pulling the tear tape must leave the broad wrapping film tight around the case.");
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.82]);
                    var upperClearance = (float)type.GetField("_wrappingUpperClearanceY", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    var lowerClearance = (float)type.GetField("_wrappingLowerClearanceY", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                    if (upperTranslation.OffsetX != 0 || lowerTranslation.OffsetX != 0
                        || upperTranslation.OffsetZ != 0 || lowerTranslation.OffsetZ != 0
                        || upperPeel.Angle != 0 || lowerPeel.Angle != 0
                        || upperTranslation.OffsetY < upperClearance * .93
                        || lowerTranslation.OffsetY > -lowerClearance * .93)
                        throw new InvalidOperationException("Close-fitting film faces must slide straight along the case until both halves clear its edges; rotation, forward lift and rightward removal may not begin while they remain in contact.");
                    type.GetMethod("SetWrappingOpened")!.Invoke(scene, [true, false]);
                    if ((double)type.GetProperty("WrappingProgress")!.GetValue(scene)! < .999
                        || upperTranslation.OffsetX < 2.9 || lowerTranslation.OffsetX < 2.9
                        || upperTranslation.OffsetZ < .45 || lowerTranslation.OffsetZ < .37
                        || upperPeel.Angle > -5.5 || lowerPeel.Angle < 5.5)
                        throw new InvalidOperationException("Only the second opening step may lift both loosened film sections away in one hand-pull direction.");
                    type.GetMethod("SetWrappingOpened")!.Invoke(scene, [false, false]);
                    if (pullTab.Visibility != Visibility.Visible)
                        throw new InvalidOperationException("Rewrapping must restore the bent reflective pull tab.");
                    type.GetMethod("SetItem")!.Invoke(scene, [current with { SpineCard = null }, -12.0, 15.0]);
                    if (wrappingUpper.Children.Count != 0 || wrappingLower.Children.Count != 0
                        || tapeGroups.Any(group => group.Children.Count != 0))
                        throw new InvalidOperationException("Caramel wrapping must exist only when the album has a Spine Card.");
                    type.GetMethod("SetItem")!.Invoke(scene, [current, -12.0, 15.0]);
                }
                var root = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_baseRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var meshes = root.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToList();
                var panel = meshes.Single(m => m.Material?.Name == "Inlay artwork");
                var spineCardRoot = (HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_spineCardRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var spineCardMeshes = spineCardRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToList();
                if (spineCardMeshes.Count(m => m.Material?.Name is "Spine Card back flap" or "Spine Card spine" or "Spine Card front flap") != 3)
                    throw new InvalidOperationException("All three Spine Card faces must belong to one independently removable group.");
                if (spineCardMeshes.Where(m => m.Material?.Name is "Spine Card back flap" or "Spine Card spine" or "Spine Card front flap")
                    .Any(m => m.IsTransparent || m.CullMode != SharpDX.Direct3D11.CullMode.Back))
                    throw new InvalidOperationException("Spine Card print must be opaque and outward-facing so it cannot replace its plain reverse.");
                var paperReverse = spineCardMeshes.SingleOrDefault(m => m.Material?.Name == "Spine Card paper reverse");
                if (paperReverse is null || paperReverse.IsTransparent
                    || paperReverse.Material is not HelixToolkit.Wpf.SharpDX.PhongMaterial reverseMaterial
                    || reverseMaterial.DiffuseMap is not null || reverseMaterial.DiffuseColor.Alpha < .999f
                    || reverseMaterial.DiffuseColor.Red < .95f || reverseMaterial.DiffuseColor.Green < .95f
                    || reverseMaterial.DiffuseColor.Blue < .95f
                    || ((HelixToolkit.SharpDX.MeshGeometry3D)paperReverse.Geometry!).Positions?.Count != 12)
                    throw new InvalidOperationException("All three unregistered Spine Card reverse panels must be opaque plain white paper.");
                var activeRegions = obiHelper.GetMethod("GetRegions")!.Invoke(null, [item.SpineCard!])!;
                var activeBack = (Int32Rect)activeRegions.GetType().GetProperty("Back")!.GetValue(activeRegions)!;
                var activeSpine = (Int32Rect)activeRegions.GetType().GetProperty("Spine")!.GetValue(activeRegions)!;
                var activeFront = (Int32Rect)activeRegions.GetType().GetProperty("Front")!.GetValue(activeRegions)!;
                var backGeometry = (HelixToolkit.SharpDX.MeshGeometry3D)spineCardMeshes.Single(
                    m => m.Material?.Name == "Spine Card back flap").Geometry!;
                var spineGeometry = (HelixToolkit.SharpDX.MeshGeometry3D)spineCardMeshes.Single(
                    m => m.Material?.Name == "Spine Card spine").Geometry!;
                var backPositions = backGeometry.Positions ?? throw new InvalidOperationException("Spine Card Back geometry has no vertices.");
                var spinePositions = spineGeometry.Positions ?? throw new InvalidOperationException("Spine Card spine geometry has no vertices.");
                var frontGeometry = (HelixToolkit.SharpDX.MeshGeometry3D)spineCardMeshes.Single(
                    m => m.Material?.Name == "Spine Card front flap").Geometry!;
                var frontPositions = frontGeometry.Positions ?? throw new InvalidOperationException("Spine Card Front geometry has no vertices.");
                var mappedBackWidth = backPositions.Max(p => p.X) - backPositions.Min(p => p.X);
                var mappedFrontWidth = frontPositions.Max(p => p.X) - frontPositions.Min(p => p.X);
                var mappedHeight = backPositions.Max(p => p.Y) - backPositions.Min(p => p.Y);
                var mappedSpineWidth = spinePositions.Max(p => p.Z) - spinePositions.Min(p => p.Z);
                if (Math.Abs(mappedBackWidth / mappedHeight - (double)activeBack.Width / activeBack.Height) > 0.015
                    || Math.Abs(mappedFrontWidth / mappedHeight - (double)activeFront.Width / activeFront.Height) > 0.015
                    || Math.Abs(mappedSpineWidth - DxJewelCaseScene.StandardCaseDepth) > .001
                    || Math.Abs(mappedHeight - 2.12 * 120 / 125) > 0.015
                    || mappedHeight >= 2.12)
                    throw new InvalidOperationException("Spine Card flaps must preserve the scan aspect ratio while its side remains fitted to the 10 mm case and its full height stays inside the case.");
                var foldX = spinePositions[0].X;
                if (Math.Abs(backPositions.Min(p => p.X) - foldX) > .0001
                    || Math.Abs(frontPositions.Min(p => p.X) - foldX) > .0001)
                    throw new InvalidOperationException("All Spine Card faces must meet at one continuous physical fold line.");
                type.GetMethod("SetSpineCardRemoved")!.Invoke(scene, [true, false]);
                var spineCardTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField(
                    "_spineCardTranslation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                if (spineCardTranslation.OffsetX >= -.45
                    || Math.Abs(spineCardTranslation.OffsetY - -.08) > .001
                    || spineCardTranslation.OffsetZ < .018)
                    throw new InvalidOperationException("Spine Card must slide clear of the lid while its printed front remains ahead of the acrylic.");
                var spineCardDragTranslation = (System.Windows.Media.Media3D.TranslateTransform3D)type.GetField(
                    "_spineCardDragTranslation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                spineCardDragTranslation.OffsetX = .25;
                spineCardDragTranslation.OffsetY = -.18;
                spineCardDragTranslation.OffsetZ = .09;
                type.GetMethod("SetSpineCardRemoved")!.Invoke(scene, [false, false]);
                if (spineCardDragTranslation.OffsetX != 0 || spineCardDragTranslation.OffsetY != 0 || spineCardDragTranslation.OffsetZ != 0)
                    throw new InvalidOperationException("Returning the Spine Card must reset its user drag offset.");
                var innerSpines = meshes.Where(m => m.Material?.Name == "Spine paper reverse").ToList();
                if (!((HelixToolkit.Wpf.SharpDX.PhongMaterial)panel.Material!).RenderDiffuseMap
                    || innerSpines.Count != 2 || innerSpines.Any(m => !((HelixToolkit.Wpf.SharpDX.PhongMaterial)m.Material!).RenderDiffuseMap))
                    throw new InvalidOperationException("Interior Inlay and both spine textures must be present.");
                var outerSpines = meshes.Where(m => m.Material?.Name == "Spine artwork").ToList();
                var outerSpineX = outerSpines.Select(m =>
                    ((HelixToolkit.SharpDX.MeshGeometry3D)m.Geometry!).Positions![0].X).Order().ToArray();
                var innerSpineX = innerSpines.Select(m =>
                    ((HelixToolkit.SharpDX.MeshGeometry3D)m.Geometry!).Positions![0].X).Order().ToArray();
                if (outerSpines.Count != 2 || outerSpines.Any(m => m.IsTransparent)
                    || Math.Abs(outerSpineX[0] - (-1.21f - DxJewelCaseScene.SpineArtworkSurfaceOffset)) > .0001f
                    || Math.Abs(outerSpineX[1] - (1.21f + DxJewelCaseScene.SpineArtworkSurfaceOffset)) > .0001f
                    || Math.Abs(innerSpineX[0] - (-1.21f + DxJewelCaseScene.SpineArtworkSurfaceOffset)) > .0001f
                    || Math.Abs(innerSpineX[1] - (1.21f - DxJewelCaseScene.SpineArtworkSurfaceOffset)) > .0001f)
                    throw new InvalidOperationException("Opaque Spine paper faces must bracket the transparent case wall so tray triangles cannot show through them.");
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
                    .GetMethod("CreateCaseModel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [current, 1, 0.0, 0.0, 1.0, false])!;
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
                foreach (var mesh in body.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>().TakeLast(3))
                {
                    if (mesh.BackMaterial is not System.Windows.Media.Media3D.MaterialGroup reverse
                        || reverse.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>().Single().Brush
                            is not SolidColorBrush reverseBrush
                        || reverseBrush.Color.A != 255 || reverseBrush.Color.R < 245
                        || reverseBrush.Color.G < 245 || reverseBrush.Color.B < 245)
                        throw new InvalidOperationException("Fallback Spine Card reverse panels must use opaque plain white paper.");
                }
                if (!string.IsNullOrEmpty(previewDirectory))
                {
                    type.GetMethod("SetCaseOpen")!.Invoke(scene, [false, false]);
                    type.GetMethod("SetSpineCardRemoved")!.Invoke(scene, [false, false]);
                    window.UpdateLayout();
                    var closedFrame = new System.Windows.Threading.DispatcherFrame();
                    var closedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                    closedTimer.Tick += (_, _) => { closedTimer.Stop(); closedFrame.Continue = false; };
                    closedTimer.Start(); System.Windows.Threading.Dispatcher.PushFrame(closedFrame);
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"SpineCard-Closed-{mode}.png"));
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.20]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"Wrapping-TapePull-{mode}.png"));
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.40]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"Wrapping-TapeBack-{mode}.png"));
                    type.GetMethod("SetWrappingCut")!.Invoke(scene, [true, false]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"Wrapping-TapeRemoved-{mode}.png"));
                    type.GetMethod("SetWrappingProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(scene, [.76]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"Wrapping-FilmLift-{mode}.png"));
                    type.GetMethod("SetWrappingOpened")!.Invoke(scene, [false, false]);
                    type.GetMethod("SetRotation")!.Invoke(scene, [78.0, 0.0]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"SpineCard-Edge-{mode}.png"));
                    type.GetMethod("SetRotation")!.Invoke(scene, [-12.0, 15.0]);
                    type.GetMethod("SetSpineCardRemoved")!.Invoke(scene, [true, false]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"SpineCard-Removed-{mode}.png"));
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

                    // Closed front inspection without an obi/wrapping layer:
                    // this is the view in which the tray's hinge-side ribs must
                    // remain legible through the clear lid beside the booklet.
                    var frontScanPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_FRONT_SCAN");
                    var frontScan = string.IsNullOrWhiteSpace(frontScanPath)
                        ? (BitmapSource)scan : new BitmapImage(new Uri(frontScanPath));
                    if (frontScan.CanFreeze) frontScan.Freeze();
                    var traySpinePreview = current with { SpineCard = null, FrontCover = frontScan };
                    type.GetMethod("SetItem")!.Invoke(scene, [traySpinePreview, -12.0, 15.0]);
                    type.GetMethod("SetCaseOpen")!.Invoke(scene, [false, false]);
                    window.UpdateLayout();
                    HelixToolkit.Wpf.SharpDX.ViewportExtensions.SaveScreen(viewport,
                        Path.Combine(previewDirectory, $"TraySpine-Closed-{mode}.png"));
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
        var wrappingFlow = new JewelCaseCoverFlow();
        try
        {
            wrappingFlow.SetItems([item], item.Key);
            var flowType = typeof(JewelCaseCoverFlow);
            var caseButton = (Button)flowType.GetField("_caseOpenButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!;
            var wrappingButton = (Button)flowType.GetField("_wrappingButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!;
            var spineButton = (Button)flowType.GetField("_spineCardButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!;
            if (caseButton.IsEnabled || spineButton.IsEnabled
                || wrappingButton.Visibility != Visibility.Visible
                || (string)wrappingButton.Content != "◆ テープを引く")
                throw new InvalidOperationException("A Spine Card case must start wrapped and lock the case and Spine Card until the film is removed.");
            flowType.GetMethod("ApplyWrappingOpened", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(wrappingFlow, [true, false]);
            var open = (Task<bool>)flowType.GetMethod("SetCaseOpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(wrappingFlow, [true, false])!;
            if (!open.GetAwaiter().GetResult()
                || !(bool)flowType.GetField("_isCaseOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!
                || !(bool)flowType.GetField("_isSpineCardRemoved", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!)
                throw new InvalidOperationException("Opening a Spine Card case must restore the obi removal motion and open state.");
            var plainCase = item with { Key = "without-spine-card", SpineCard = null };
            wrappingFlow.SetItems([plainCase], plainCase.Key);
            if (!caseButton.IsEnabled || wrappingButton.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("A case without a Spine Card must have no wrapping control and must remain directly openable.");
            var plainOpen = (Task<bool>)flowType.GetMethod("SetCaseOpenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(wrappingFlow, [true, false])!;
            if (!plainOpen.GetAwaiter().GetResult()
                || !(bool)flowType.GetField("_isCaseOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrappingFlow)!)
                throw new InvalidOperationException("A case without a Spine Card failed to open directly.");
        }
        finally
        {
            ((IDisposable?)typeof(JewelCaseCoverFlow).GetField("_dxScene", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(wrappingFlow))?.Dispose();
        }
        Console.WriteLine("Inlay, opaque Spine Card, removal motion, and optional packaging tests passed.");
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
        var shell = sceneType.GetMethod("GetCoverFlowShellGeometry", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;
        foreach (var property in new[] { "TopLid", "TopMouldedEdges" })
        {
            var mesh = (System.Windows.Media.Media3D.MeshGeometry3D)shell.GetType().GetProperty(property)!.GetValue(shell)!;
            for (var index = 0; index + 2 < mesh.TriangleIndices.Count; index += 3)
            {
                var a = mesh.Positions[mesh.TriangleIndices[index]];
                var b = mesh.Positions[mesh.TriangleIndices[index + 1]];
                var c = mesh.Positions[mesh.TriangleIndices[index + 2]];
                var normal = System.Windows.Media.Media3D.Vector3D.CrossProduct(b - a, c - a);
                if (normal.LengthSquared > 0) normal.Normalize();
                var spanX = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
                var spanY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
                if (Math.Abs(normal.Z) > .88 && spanX > 2.42 * .52 && spanY > 2.12 * .52)
                    throw new InvalidOperationException("The clear lid must not place a full-face reflective triangle over Front artwork.");
                var maximumX = Math.Max(a.X, Math.Max(b.X, c.X));
                var centroidY = (a.Y + b.Y + c.Y) / 3;
                if (maximumX < -2.42 * .402 && Math.Abs(centroidY) < 2.12 * .43)
                    throw new InvalidOperationException("The clear lid must not retain the central printable side wall that projects a vertical band over Front artwork.");
            }
        }
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
                .Invoke(null, [test.Item, 1, -30.0, 0.0, 1.0, false])!;
            var body = (System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelUIElement3D)model.Children[0]).Model;
            var directFrontBrushes = body.Children.OfType<System.Windows.Media.Media3D.GeometryModel3D>()
                .Select(mesh => mesh.Material).OfType<System.Windows.Media.Media3D.MaterialGroup>()
                .SelectMany(material => material.Children.OfType<System.Windows.Media.Media3D.DiffuseMaterial>())
                .Select(material => material.Brush)
                .OfType<ImageBrush>()
                .Where(brush => ReferenceEquals(brush.ImageSource, test.Item.FrontCover)
                    && Math.Abs(brush.Opacity - 1.0) < .001)
                .ToList();
            if (model.Children.Count != 1 || directFrontBrushes.Count != 1)
                throw new InvalidOperationException("WPF CoverFlow artwork must be a directly textured 3D face, not a visual-host plane.");
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

    private static void VerifyAutomaticLibraryUpdates()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "ZipMp3Player-LibraryWatchTest-" + Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(testRoot, "data");
        var musicRoot = Path.Combine(testRoot, "music");
        var albumRoot = Path.Combine(musicRoot, "Album A");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(albumRoot);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "settings.json"),
            System.Text.Json.JsonSerializer.Serialize(new { MusicFolders = new[] { musicRoot } }));
        WriteTestWave(Path.Combine(albumRoot, "01.wav"));
        var archivePath = Path.Combine(musicRoot, "Plain ZIP Album.zip");
        WriteTestMp3Archive(archivePath, testRoot);

        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow { ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Hide();
            WaitFor(() => GetAlbums(window).Count == 2
                && GetAlbums(window).Any(album => string.Equals(album.Path, archivePath, StringComparison.OrdinalIgnoreCase)
                    && album.Tracks.Count == 1 && album.Tracks[0].IsArchiveEntry),
                "initial folder and ordinary ZIP library scan");

            // Simulate a network share dropping its watcher notification. The
            // lightweight reconciliation must find and queue only the new album,
            // without reopening every cached album in a full scan.
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
            typeof(MainWindow).GetMethod("DisposeLibraryWatchers", flags)!.Invoke(window, null);
            var missedAlbumRoot = Path.Combine(musicRoot, "Missed Network Album");
            Directory.CreateDirectory(missedAlbumRoot);
            WriteTestWave(Path.Combine(missedAlbumRoot, "01.wav"));
            var discovered = (IReadOnlyList<string>)typeof(MainWindow)
                .GetMethod("DiscoverUntrackedAlbums", flags)!.Invoke(null,
                    [new[] { musicRoot }, GetAlbums(window).Select(album => album.Path).ToArray(), CancellationToken.None])!;
            if (discovered.Count != 1 || !string.Equals(discovered[0], missedAlbumRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Missed network-folder notification was not recovered as one untracked album.");
            typeof(MainWindow).GetMethod("QueueLibraryChange", flags)!.Invoke(window, [discovered[0]]);
            WaitFor(() => GetAlbums(window).Any(album => string.Equals(album.Path, missedAlbumRoot, StringComparison.OrdinalIgnoreCase)),
                "missed network folder reconciliation");
            var changeLogStore = typeof(MainWindow).GetField("_libraryChangeLogStore", flags)!.GetValue(window)!;
            var changeLogSnapshot = (System.Collections.IEnumerable)changeLogStore.GetType().GetMethod("Snapshot")!.Invoke(changeLogStore, null)!;
            var addedLog = changeLogSnapshot.Cast<object>().FirstOrDefault(entry =>
                Equals(entry.GetType().GetProperty("Action")!.GetValue(entry), "Added")
                && string.Equals((string)entry.GetType().GetProperty("Path")!.GetValue(entry)!, missedAlbumRoot,
                    StringComparison.OrdinalIgnoreCase));
            if (addedLog is null || (int)addedLog.GetType().GetProperty("TrackCount")!.GetValue(addedLog)! != 1)
                throw new InvalidOperationException("Incremental album addition was not persisted in library history.");
            Directory.Delete(missedAlbumRoot, true);
            typeof(MainWindow).GetMethod("QueueLibraryChange", flags)!.Invoke(window, [missedAlbumRoot]);
            WaitFor(() => GetAlbums(window).Count == 2, "reconciled folder cleanup");
            changeLogSnapshot = (System.Collections.IEnumerable)changeLogStore.GetType().GetMethod("Snapshot")!.Invoke(changeLogStore, null)!;
            if (!changeLogSnapshot.Cast<object>().Any(entry =>
                    Equals(entry.GetType().GetProperty("Action")!.GetValue(entry), "Removed")
                    && string.Equals((string)entry.GetType().GetProperty("Path")!.GetValue(entry)!, missedAlbumRoot,
                        StringComparison.OrdinalIgnoreCase))
                || !File.Exists(Path.Combine(dataRoot, "library-events.json")))
                throw new InvalidOperationException("Incremental album removal was not persisted in library history.");
            typeof(MainWindow).GetMethod("ConfigureLibraryWatchers", flags)!.Invoke(window, null);

            WriteTestWave(Path.Combine(albumRoot, "02.wav"));
            WaitFor(() => GetAlbums(window).Single(album => Directory.Exists(album.Path)).Tracks.Count == 2,
                "incremental file addition");

            File.Delete(Path.Combine(albumRoot, "01.wav"));
            WaitFor(() => GetAlbums(window).Single(album => Directory.Exists(album.Path)).Tracks.Count == 1,
                "incremental file deletion");

            var renamedRoot = Path.Combine(musicRoot, "Album Renamed");
            Directory.Move(albumRoot, renamedRoot);
            WaitFor(() => GetAlbums(window).Count == 2
                && GetAlbums(window).Any(album => string.Equals(album.Path, renamedRoot, StringComparison.OrdinalIgnoreCase)),
                "incremental folder rename");

            Directory.Delete(renamedRoot, true);
            WaitFor(() => GetAlbums(window).Count == 1, "incremental folder album deletion");

            var renamedArchivePath = Path.Combine(musicRoot, "Plain ZIP Album Renamed.zip");
            File.Move(archivePath, renamedArchivePath);
            WaitFor(() => GetAlbums(window).Count == 1
                && string.Equals(GetAlbums(window).Single().Path, renamedArchivePath, StringComparison.OrdinalIgnoreCase),
                "incremental ordinary ZIP rename");
            File.Delete(renamedArchivePath);
            WaitFor(() => GetAlbums(window).Count == 0, "incremental ordinary ZIP deletion");
            Console.WriteLine("Automatic folder and ordinary ZIP add/delete/rename tests passed without a full rescan.");
        }
        finally
        {
            window.Close();
            app.Shutdown();
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }

    private static IReadOnlyList<ZipAlbum> GetAlbums(MainWindow window)
    {
        var collection = (System.Collections.IEnumerable)typeof(MainWindow)
            .GetField("_albums", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        return collection.Cast<object>()
            .Select(item => (ZipAlbum)item.GetType().GetProperty("Album")!.GetValue(item)!)
            .ToList();
    }

    private static void WriteTestWave(string path)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 1));
        writer.Write(new byte[44100 / 5 * 2]);
    }

    private static void WriteTestMp3Archive(string archivePath, string temporaryRoot)
    {
        var wavePath = Path.Combine(temporaryRoot, "archive-source.wav");
        var mp3Path = Path.Combine(temporaryRoot, "archive-source.mp3");
        WriteTestWave(wavePath);
        using (var reader = new NAudio.Wave.WaveFileReader(wavePath))
            NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 128000);
        using var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create);
        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(
            archive, mp3Path, "01 Plain ZIP Track.mp3", System.IO.Compression.CompressionLevel.NoCompression);
    }

    private static void WaitFor(Func<bool> condition, string operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out waiting for {operation}.");
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
