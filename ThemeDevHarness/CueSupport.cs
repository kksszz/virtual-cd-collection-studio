using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyCueSupport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZipMp3Player-CueTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", Path.Combine(directory, "data"));
        void Check(bool good, string name) { if (!good) throw new Exception(name); Console.WriteLine("PASS " + name); }
        var image = Path.Combine(directory, "test.bin");
        var cue = Path.Combine(directory, "test.cue");
        // Two seconds of stereo signed 16-bit PCM, two distinguishable tracks.
        var bytes = Enumerable.Repeat((byte)1, 2352 * 75).Concat(Enumerable.Repeat((byte)2, 2352 * 75)).ToArray();
        File.WriteAllBytes(image, bytes);
        File.WriteAllText(cue, "FILE \"test.bin\" BINARY\nPERFORMER \"Artist\"\nTITLE \"Album\"\nTRACK 01 AUDIO\nTITLE \"One\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Two\"\nINDEX 01 00:01:00\n");
        var original = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image)));
        var album = ZipAlbumReader.Open(image);
        Check(album.Path == cue && album.Tracks.Count == 2 && album.Tracks[1].DataOffset == 176400, "image resolves CUE, boundaries");
        Check(album.Tracks.All(t => t.IsSupported && t.Duration.TotalSeconds == 1 && t.SampleRate == 44100), "CDDA metadata");
        var favorites = new FavoritesStore(Path.Combine(directory, "favorites.json"));
        favorites.ToggleTrack(album.Tracks[0]);
        Check(favorites.IsTrackFavorite(album.Tracks[0]) && !favorites.IsTrackFavorite(album.Tracks[1]), "CUE favorites have separate track identity");
        Check(PlaybackUsageStore.CreateTrackKey(album.Tracks[0]) != PlaybackUsageStore.CreateTrackKey(album.Tracks[1]), "CUE playback usage has separate track identity");
        foreach (var track in album.Tracks)
        {
            using var reader = TrackAudioReader.Open(track);
            var buffer = new byte[track.Size + 128];
            var read = reader.Reader.Read(buffer, 0, buffer.Length);
            Check(read == track.Size && buffer.Take(read).All(b => b == track.TrackNumber), "bounded PCM track " + track.TrackNumber);
            reader.Reader.CurrentTime = TimeSpan.FromSeconds(0.5);
            Check(Math.Abs(reader.Reader.CurrentTime.TotalSeconds - 0.5) < 0.001, "seek track " + track.TrackNumber);
            using var gapless = TrackAudioReader.OpenGapless(track);
            Check(gapless.Reader.Length == track.Size, "gapless exact PCM length");
        }
        var disc = CueAlbumReader.Read(cue);
        var scan = typeof(MainWindow).GetMethod("ScanWorker", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var updateType = scan.GetParameters()[1].ParameterType.GenericTypeArguments[0];
        var progress = Activator.CreateInstance(typeof(Progress<>).MakeGenericType(updateType));
        var scanned = (System.Collections.IList)scan.Invoke(null, new object[] { new[] { directory }, progress!, CancellationToken.None })!;
        Check(scanned.Count == 1, "library scan registers CUE once and skips raw image duplicate");
        var metadata = new CueMetadata(disc.Fingerprint, "test-release", "Imported", "2026", 1, 2,
            [new("New One", "Artist"), new("New Two", "Artist")], "Test");
        CueMetadataStore.Save(disc, metadata);
        Check(CueAlbumReader.Open(cue).Tracks[1].Title == "New Two", "metadata reload");
        CueMetadataStore.Save(disc, metadata with { Album = "Updated" });
        Check(CueAlbumReader.Open(cue).Tracks[0].Album == "Updated", "metadata replacement with backup");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image))) == original, "original binary unchanged");
        var response = JsonSerializer.Serialize(new { releases = new[] { new { id = "id", title = "Imported", date = "2026", media = new[] { new {
            position = 1, discs = new[] { new { id = disc.DiscId } }, tracks = new[] { new { title = "One", length = 1000 }, new { title = "Two", length = 1000 } }
        } } } } });
        using (var json = JsonDocument.Parse(response)) Check(CueMetadataLookup.Parse(json.RootElement, disc).Count == 1, "external Disc ID/track mapping");
        using (var json = JsonDocument.Parse(response.Replace(disc.DiscId, "different")))
            Check(CueMetadataLookup.Parse(json.RootElement, disc).Count == 1, "fuzzy TOC duration mapping");
        using (var json = JsonDocument.Parse(response.Replace(disc.DiscId, "different").Replace("1000", "99000")))
            Check(CueMetadataLookup.Parse(json.RootElement, disc).Count == 0, "wrong disc and durations rejected");
        var originalCue = File.ReadAllText(cue);
        foreach (var bad in new[] { originalCue.Replace("test.bin", "../test.bin"), originalCue.Replace("AUDIO", "MODE1/2352"),
            originalCue.Replace("00:01:00", "00:00:00"), originalCue.Replace("00:01:00", "00:99:00") })
        {
            File.WriteAllText(cue, bad);
            try { CueAlbumReader.Read(cue); throw new Exception("Invalid CUE accepted"); }
            catch (InvalidDataException) { Console.WriteLine("PASS malformed CUE rejected"); }
            catch (NotSupportedException) { Console.WriteLine("PASS unsupported CUE rejected"); }
        }
        File.WriteAllText(cue, originalCue + "REM changed\n");
        Check(CueMetadataStore.Load(CueAlbumReader.Read(cue)) is null, "stale metadata fingerprint rejected");
        var real = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_CUE_SAMPLE_DIRECTORY");
        var probeData = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DISCOVERY_PROBE_DATA");
        if (!string.IsNullOrEmpty(probeData))
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(probeData, "settings.json")));
            using var library = JsonDocument.Parse(File.ReadAllText(Path.Combine(probeData, "library.json")));
            var roots = settings.RootElement.GetProperty("MusicFolders").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var disabled = settings.RootElement.GetProperty("DisabledMusicFolders").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            roots = roots.OrderBy(disabled.Contains).ToArray();
            var known = library.RootElement.GetProperty("Albums").EnumerateArray().Select(x => x.GetProperty("Path").GetString()!).ToArray();
            var discover = typeof(MainWindow).GetMethod("DiscoverUntrackedAlbums", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var found = (IReadOnlyList<string>)discover.Invoke(null, new object[] { roots, known, CancellationToken.None })!;
            foreach (var path in found) Console.WriteLine("Discovery probe: " + path);
        }
        if (!string.IsNullOrEmpty(real))
        {
            var refresh = typeof(MainWindow).GetMethod("RefreshChangedAlbums", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var existingType = refresh.GetParameters()[1].ParameterType.GenericTypeArguments[0];
            var refreshResult = refresh.Invoke(null, new object[] { new[] { real }, Array.CreateInstance(existingType, 0), CancellationToken.None })!;
            var refreshed = (System.Collections.IList)refreshResult.GetType().GetProperty("Refreshed")!.GetValue(refreshResult)!;
            Check(refreshed.Count == 4, "targeted folder refresh discovers all four CUE albums");
            var count = 0;
            foreach (var sample in Directory.EnumerateFiles(real, "*.cue"))
            {
                var before = File.GetLastWriteTimeUtc(sample);
                var parsed = CueAlbumReader.Open(sample);
                foreach (var track in parsed.Tracks)
                {
                    using var reader = TrackAudioReader.Open(track);
                    var buffer = new byte[4096];
                    Check(reader.Reader.Read(buffer, 0, buffer.Length) == buffer.Length, Path.GetFileName(sample) + " track " + track.TrackNumber);
                    reader.Reader.Position = reader.Reader.Length - 4;
                    Check(reader.Reader.Read(buffer, 0, buffer.Length) == 4, "bounded end");
                }
                Check(File.GetLastWriteTimeUtc(sample) == before, "sample CUE unchanged");
                count += parsed.Tracks.Count;
                Console.WriteLine($"Disc ID: {CueAlbumReader.Read(sample).DiscId}, tracks={parsed.Tracks.Count}");
            }
            Check(count == 57, "all 57 sample tracks readable");
        }
        Console.WriteLine("Isolated test data: " + directory);
    }
}
