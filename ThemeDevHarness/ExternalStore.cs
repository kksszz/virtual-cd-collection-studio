using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using NAudio.Wave;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyExternalStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExternalStoreTest-" + Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "library"); var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(library);
        var wav = Path.Combine(root, "sample.wav"); var mp3 = Path.Combine(root, "sample.mp3");
        using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2))) writer.Write(new byte[176400], 0, 176400);
        using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(reader, mp3, 192000);
        string Sample(string name)
        {
            var path = Path.Combine(library, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(mp3, "01 音声.mp3", CompressionLevel.Optimal);
            using var text = new StreamWriter(zip.CreateEntry("booklet/歌詞.txt", CompressionLevel.Optimal).Open());
            text.Write("sample lyrics"); return path;
        }
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
        ZipStorageConversionService.ValidateBackupRoot(backups, [library]);
        try { ZipStorageConversionService.ValidateBackupRoot(Path.Combine(library, "backup"), [library]); throw new Exception("In-library backup allowed"); }
        catch (IOException) { Console.WriteLine("PASS reject library backup root"); }
        foreach (var name in new[] { "rename.zip", "in-place.zip.mp3" })
        {
            var path = Sample(name); var before = ZipAlbumReader.Open(path);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var result = ZipStorageConversionService.ConvertWithExternalBackup(path, backups);
            Check(hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Backup))), "exact original backup " + name);
            Check(!ZipStorageConversionService.HasCompressedEntries(result.Destination), "all entries stored " + name);
            Check(File.Exists(path), "source exists before finalization " + name);
            var after = ZipAlbumReader.Open(result.Destination);
            var favorites = new FavoritesStore(Path.Combine(root, name + ".favorites"));
            favorites.ToggleAlbum(before); favorites.ToggleTrack(before.Tracks[0]);
            favorites.RelocateAlbum(before, after.Path); favorites.RelocateTrack(before.Tracks[0], after.Path); favorites.Save();
            Check(favorites.IsAlbumFavorite(after) && favorites.IsTrackFavorite(after.Tracks[0]), "favorite identity migration " + name);
            var usage = new PlaybackUsageStore(Path.Combine(root, name + ".usage"));
            usage.CommitPlay(usage.GetOrCreate(before.Tracks[0])); usage.RelocateTrack(before.Tracks[0], after.Path); usage.Save();
            Check(usage.GetOrCreate(after.Tracks[0]).PlayCount == 1 && usage.Snapshot().Count == 1, "history identity migration " + name);
            ZipStorageConversionService.FinishExternalConversion(result);
            Check(File.Exists(result.Backup) && File.Exists(result.Destination), "backup and converted file retained " + name);
            if (name.EndsWith(".zip")) Check(!File.Exists(path), "old zip removed after verification");
        }
        var collision = Sample("collision.zip"); File.WriteAllText(collision + ".mp3", "keep");
        try { ZipStorageConversionService.ConvertWithExternalBackup(collision, backups); throw new Exception("Overwrite allowed"); }
        catch (IOException) { Check(File.ReadAllText(collision + ".mp3") == "keep", "collision protected"); }
        var changed = Sample("changed.zip"); var pending = ZipStorageConversionService.ConvertWithExternalBackup(changed, backups);
        File.AppendAllText(changed, "changed");
        try { ZipStorageConversionService.FinishExternalConversion(pending); throw new Exception("Changed source removed"); }
        catch (IOException) { Check(File.Exists(changed), "changed source retained"); }
        var dataDirectory = Path.Combine(root, "app-data"); Directory.CreateDirectory(dataDirectory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", dataDirectory);
        var sourceAlbum = ZipAlbumReader.Open(Sample("metadata.zip"));
        var renamed = ZipStorageConversionService.ConvertWithExternalBackup(sourceAlbum.Path, backups);
        var destinationAlbum = ZipAlbumReader.Open(renamed.Destination);
        var favoriteData = new FavoritesStore(Path.Combine(dataDirectory, "favorites.json"));
        favoriteData.ToggleAlbum(sourceAlbum); favoriteData.ToggleTrack(sourceAlbum.Tracks[0]); favoriteData.Save();
        var historyData = new PlaybackUsageStore(Path.Combine(dataDirectory, "usage.json"));
        historyData.CommitPlay(historyData.GetOrCreate(sourceAlbum.Tracks[0])); historyData.Save();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var artworkMethod = typeof(MainWindow).GetMethod("GetDownloadedArtworkDirectory", flags)!;
        var from = (string)artworkMethod.Invoke(null, [sourceAlbum.Path])!;
        var to = (string)artworkMethod.Invoke(null, [destinationAlbum.Path])!;
        Directory.CreateDirectory(from);
        File.WriteAllText(Path.Combine(from, "cover.jpg"), "test artwork");
        File.WriteAllText(Path.Combine(from, "artwork-roles.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string> {
            ["file:" + Path.Combine(from, "cover.jpg")] = "Front", ["zip:front.jpg"] = "FrontInside" }));
        var lyricsMethod = typeof(MainWindow).GetMethod("GetSavedLyricsPathForIdentity", flags)!;
        var oldLyrics = (string)lyricsMethod.Invoke(null, [sourceAlbum.Path, sourceAlbum.Path, sourceAlbum.Tracks[0].FileName, true])!;
        var newLyrics = (string)lyricsMethod.Invoke(null, [destinationAlbum.Path, destinationAlbum.Path, destinationAlbum.Tracks[0].FileName, true])!;
        Directory.CreateDirectory(Path.GetDirectoryName(oldLyrics)!); File.WriteAllText(oldLyrics, "saved lyrics");
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        try
        {
            typeof(MainWindow).GetMethod("MigrateStoredZipIdentity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [sourceAlbum, destinationAlbum]);
            Check(File.ReadAllText(Path.Combine(to, "cover.jpg")) == "test artwork", "managed artwork copied");
            var roles = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(to, "artwork-roles.json")))!;
            Check(roles["file:" + Path.Combine(to, "cover.jpg")] == "Front" && roles["zip:front.jpg"] == "FrontInside", "artwork role keys remapped");
            Check(File.ReadAllText(newLyrics) == "saved lyrics", "saved lyrics preserved");
            favoriteData = new FavoritesStore(Path.Combine(dataDirectory, "favorites.json")); favoriteData.Load();
            historyData = new PlaybackUsageStore(Path.Combine(dataDirectory, "usage.json")); historyData.Load();
            Check(favoriteData.IsAlbumFavorite(destinationAlbum) && favoriteData.IsTrackFavorite(destinationAlbum.Tracks[0]), "window migration saved favorites");
            Check(historyData.GetOrCreate(destinationAlbum.Tracks[0]).PlayCount == 1, "window migration saved history");
        }
        finally { window.Close(); application.Shutdown(); }
    }
}
