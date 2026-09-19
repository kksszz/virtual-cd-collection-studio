using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyLyricsSplitter()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZipMp3Player-LyricsSplitter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            MusicFolders = Array.Empty<string>(), AutomaticArtworkEnabled = false,
            ImageLyricsRatio = 0.75, LyricsPanelExpanded = true
        }));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow { Width = 1600, Height = 950, ShowActivated = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            Pump();
            var artwork = (ColumnDefinition)window.FindName("ArtworkColumn");
            var lyrics = (ColumnDefinition)window.FindName("LyricsColumn");
            var splitter = (GridSplitter)window.FindName("ImageLyricsSplitter");
            var complete = typeof(MainWindow).GetMethod("ImageLyricsSplitter_DragCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var ratio in new[] { 0.55, 0.70, 0.45 })
            {
                // Mimic the preview commit: Width is new but ActualWidth is still old.
                artwork.Width = new GridLength(ratio, GridUnitType.Star);
                lyrics.Width = new GridLength(1 - ratio, GridUnitType.Star);
                complete.Invoke(window, [splitter, new DragCompletedEventArgs(0, 0, false)]);
                Pump();
                var actual = artwork.ActualWidth / (artwork.ActualWidth + lyrics.ActualWidth);
                if (Math.Abs(actual - ratio) > 0.015) throw new Exception($"Drag reverted: {ratio} -> {actual}");
                using var saved = JsonDocument.Parse(File.ReadAllText(path));
                if (Math.Abs(saved.RootElement.GetProperty("ImageLyricsRatio").GetDouble() - ratio) > 0.015)
                    throw new Exception("Split ratio not saved");
                Console.WriteLine($"PASS lyrics splitter ratio {ratio:P0}, saved and retained after layout");
            }
        }
        finally { window.Close(); app.Shutdown(); }

        static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
