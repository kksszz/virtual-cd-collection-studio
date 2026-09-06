using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NAudio.Wave;
using ZipMp3Player;

internal static class GaplessUiTests
{
    public static void Run(string directory)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", Path.Combine(directory, "UiData"));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { Verify(directory); }
            catch (Exception ex) { error = ex; }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Gapless UI integration failed", error);
    }

    private static void Verify(string directory)
    {
        ZipAlbum Album(string name)
        {
            var path = Path.Combine(directory, name + ".wav");
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
                writer.Write(new byte[44100 * 4 * 2], 0, 44100 * 4 * 2);
            return new ZipAlbum { Path = name, Tracks = [new ZipTrack { Title = name, Album = name, SourcePath = path,
                FileName = Path.GetFileName(path), AudioFormat = "WAV", SampleRate = 44100, Duration = TimeSpan.FromSeconds(2) }] };
        }
        var first = Album("UiAlbumA");
        var second = Album("UiAlbumB");
        var main = new MainWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(main);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(main, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(main, args);
        var play = typeof(MainWindow).GetMethods(flags).Single(m => m.Name == "PlayTrack" && m.GetParameters().Length == 8);
        try
        {
            var itemType = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
            foreach (var album in new[] { first, second })
                ((System.Collections.IList)Field("_albums")!).Add(Activator.CreateInstance(itemType, [album]));
            Call("SetCurrentAlbum", first);
            play.Invoke(main, [first, 0, null, false, true, false, null, false]);
            var stream = (GaplessPlaybackStream)Field("_gapless")!;
            var output = Field("_output");
            var equalizer = Field("_equalizer");
            if (stream is null || output is null || stream.Entries.Count != 2) throw new Exception("Default gapless queue not created");
            stream.PrefetchCompletion.GetAwaiter().GetResult();
            stream.Position = stream.Length - stream.WaveFormat.BlockAlign;
            if (stream.Read(new byte[16], 0, 16) != 16) throw new Exception("Boundary read incomplete");
            Call("SynchronizeGaplessTrack");
            if (!ReferenceEquals(output, Field("_output")) || !ReferenceEquals(equalizer, Field("_equalizer")))
                throw new Exception("Automatic boundary must keep output and DSP instances alive");
            if (!ReferenceEquals(second, Field("_playingAlbum")) || !ReferenceEquals(second, Field("_album"))
                || !main.Title.StartsWith("UiAlbumB") || ((TextBlock)main.FindName("NowPlayingTitleText")).Text != "UiAlbumB")
                throw new Exception("Track boundary did not update album/now-playing metadata");
            if (Field("_activeUsageEntry") is null || ((DataGrid)main.FindName("TrackGrid")).SelectedIndex != 0)
                throw new Exception("Track boundary did not update usage or track selection");
            ((TextBox)main.FindName("AlbumFilterTextBox")).Text = "no matching album";
            Call("RebuildAudioEffects");
            if (((GaplessPlaybackStream)Field("_gapless")!).Entries.Count != 2)
                throw new Exception("Changing effects during browsing must preserve original queue");
            ((CheckBox)main.FindName("GaplessCheck")).IsChecked = false;
            if (Field("_gapless") is not null || Field("_trackReader") is null)
                throw new Exception("Gapless OFF must restore legacy playback");
            ((TextBox)main.FindName("AlbumFilterTextBox")).Clear();
            ((CheckBox)main.FindName("GaplessCheck")).IsChecked = true;
            if (Field("_gapless") is null) throw new Exception("Gapless ON must rebuild queue");
            Call("StopPlayback", true);

            // A favorite boundary must not clear the queue or play unmarked tracks.
            Set("_favoriteQueue", new FavoriteTrackEntry[] { new(first, first.Tracks[0], false, true), new(second, second.Tracks[0], false, true) });
            Set("_favoriteQueueIndex", 0);
            Call("SetCurrentAlbum", first);
            play.Invoke(main, [first, 0, null, false, true, true, null, false]);
            stream = (GaplessPlaybackStream)Field("_gapless")!;
            stream.PrefetchCompletion.GetAwaiter().GetResult();
            // Browsing another album must not be interrupted by an automatic boundary.
            var browsing = Album("BrowsingOnly");
            Call("SetCurrentAlbum", browsing);
            output = Field("_output");
            stream.Position = stream.Length - stream.WaveFormat.BlockAlign;
            if (stream.Read(new byte[16], 0, 16) != 16) throw new Exception("Favorite boundary read incomplete");
            Call("SynchronizeGaplessTrack");
            if ((int)Field("_favoriteQueueIndex")! != 1 || !ReferenceEquals(output, Field("_output"))
                || !ReferenceEquals(browsing, Field("_album")) || !ReferenceEquals(second, Field("_playingAlbum")))
                throw new Exception("Favorite boundary or independent browsing failed");
            Console.WriteLine("PASS: UI gapless output/DSP reuse, metadata, usage, favorites, browsing, queue persistence and ON/OFF");
        }
        finally { Call("StopPlayback", true); main.Close(); }
    }
}
