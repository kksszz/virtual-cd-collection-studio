using System.IO;
using System.Text.Json;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyLibrarySize()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZipMp3Player-Size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var music = Path.Combine(root, "track.mp3");
        var archive = Path.Combine(root, "album.zip.mp3");
        var image = Path.Combine(root, "image.iso");
        var cue = Path.Combine(root, "image.cue");
        File.WriteAllBytes(music, new byte[100]);
        File.WriteAllBytes(archive, new byte[50]);
        File.WriteAllBytes(image, new byte[200]);
        File.WriteAllBytes(cue, new byte[10]);
        File.WriteAllBytes(Path.Combine(root, "cover.jpg"), new byte[20]);
        var albums = new ZipAlbum[] {
            new() { Path = root, Tracks = [new() { SourcePath = music, Size = 100 }] },
            new() { Path = archive, Tracks = [new() { SourcePath = archive, FileName = "one.mp3", IsArchiveEntry = true, Size = 150 }] },
            new() { Path = cue, Tracks = [new() { SourcePath = image, CuePath = cue, FileName = "1", Size = 100 },
                new() { SourcePath = image, CuePath = cue, FileName = "2", Size = 100 }] }
        };
        var result = LibrarySizeSummary.Calculate(albums.Concat([albums[0]]).ToArray(), CancellationToken.None);
        if (result.StoredBytes != 380 || result.AudioBytes != 450 || result.Tracks != 4 || result.Albums != 3 || result.Unreadable != 0)
            throw new Exception("Size/dedup regression: " + result);
        var missing = LibrarySizeSummary.Calculate([new() { Path = Path.Combine(root, "missing.zip") }], CancellationToken.None);
        if (missing.Unreadable != 1) throw new Exception("Missing file not reported");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { LibrarySizeSummary.Calculate(albums, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS stored/audio sizes, folder/archive overlap, CUE deduplication, missing files, cancellation");
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        try
        {
            foreach (var language in new[] { "ja", "en" })
            {
                var window = new SettingsWindow([], [], false, root, language) { LibraryAlbums = albums,
                    ShowActivated = false, ShowInTaskbar = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                try
                {
                    window.Show();
                    var refresh = (System.Windows.Controls.Button)window.FindName("LibrarySizeRefreshButton");
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (!refresh.IsEnabled && DateTime.UtcNow < deadline)
                    {
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                    }
                    var label = (System.Windows.Controls.TextBlock)window.FindName("LibrarySizeText");
                    if (!label.Text.Contains("GiB") || !refresh.IsEnabled) throw new Exception("Size UI failed: " + label.Text);
                    Console.WriteLine("PASS settings size display " + language);
                }
                finally { window.Close(); }
            }
        }
        finally { app.Shutdown(); }

        var probe = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_LIBRARY_SIZE_PROBE");
        if (!string.IsNullOrWhiteSpace(probe))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(probe));
            var library = document.RootElement.GetProperty("Albums").Deserialize<List<ZipAlbum>>()!;
            var summary = LibrarySizeSummary.Calculate(library, CancellationToken.None);
            Console.WriteLine("READ-ONLY LIBRARY: " + JsonSerializer.Serialize(summary));
            Console.WriteLine("Stored: " + LibrarySizeSummary.Format(summary.StoredBytes));
            Console.WriteLine("Unpacked audio: " + LibrarySizeSummary.Format(summary.AudioBytes));
        }
    }
}
