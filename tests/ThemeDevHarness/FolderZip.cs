using System.IO;
using System.IO.Compression;
using NAudio.Wave;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyFolderZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "ZipMp3FolderTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool good, string name) { if (!good) throw new Exception(name); Console.WriteLine("PASS " + name); }
        string Sample(string name)
        {
            var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            var wav = Path.Combine(root, Guid.NewGuid().ToString("N") + ".wav");
            using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2))) writer.Write(new byte[176400], 0, 176400);
            using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(reader, Path.Combine(folder, "01 音声.mp3"), 192000);
            Directory.CreateDirectory(Path.Combine(folder, "booklet", "empty"));
            File.WriteAllText(Path.Combine(folder, "booklet", "歌詞.txt"), "歌詞のサンプル");
            return folder;
        }
        var source = Sample("Album");
        var before = ZipAlbumReader.OpenFolder(source);
        var result = FolderZipConversion.Convert(source);
        Check(Directory.Exists(source) && File.Exists(result.Destination), "conversion retains source until confirmation");
        using (var zip = ZipFile.OpenRead(result.Destination))
            Check(zip.GetEntry("booklet/empty/") is not null && zip.Entries.All(e => e.Length == e.CompressedLength), "stored archive including empty directories");
        Check(result.Album.Tracks.Count == 1, "readable music track");
        var usage = new PlaybackUsageStore(Path.Combine(root, "usage.json"));
        usage.CommitPlay(usage.GetOrCreate(before.Tracks[0])); usage.CopyTrackHistory(before.Tracks[0], result.Album.Tracks[0]); usage.Save();
        Check(usage.GetOrCreate(result.Album.Tracks[0]).PlayCount == 1, "history copied across DIR/ZIP identity");
        try { FolderZipConversion.Convert(source); throw new Exception("overwrite allowed"); } catch (IOException) { Console.WriteLine("PASS no overwrite"); }
        File.WriteAllText(Path.Combine(source, "new.txt"), "added later");
        try { FolderZipConversion.DeleteVerifiedSource(result); throw new Exception("changed folder deleted"); } catch (IOException) { Check(File.Exists(Path.Combine(source, "01 音声.mp3")), "added file prevents deletion"); }
        var deleteSource = Sample("Delete-test"); var valid = FolderZipConversion.Convert(deleteSource);
        using (var held = new FileStream(Path.Combine(deleteSource, "01 音声.mp3"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { FolderZipConversion.DeleteVerifiedSource(valid); throw new Exception("locked source deleted"); }
            catch (IOException) { Check(File.Exists(Path.Combine(deleteSource, "booklet", "歌詞.txt")), "locked file prevents all deletion"); }
        }
        FolderZipConversion.DeleteVerifiedSource(valid);
        Check(!Directory.Exists(deleteSource) && File.Exists(valid.Destination), "verified source removal keeps archive");
        var changed = Sample("Changed-test"); var changedResult = FolderZipConversion.Convert(changed);
        File.AppendAllText(Path.Combine(changed, "booklet", "歌詞.txt"), "changed");
        try { FolderZipConversion.DeleteVerifiedSource(changedResult); throw new Exception("changed file deleted"); } catch (IOException) { Check(Directory.Exists(changed), "changed bytes prevent deletion"); }
        var corrupt = Sample("Archive-changed"); var corruptResult = FolderZipConversion.Convert(corrupt);
        using (var stream = new FileStream(corruptResult.Destination, FileMode.Append)) stream.WriteByte(0);
        try { FolderZipConversion.DeleteVerifiedSource(corruptResult); throw new Exception("changed archive allowed deletion"); }
        catch (IOException) { Check(File.Exists(Path.Combine(corrupt, "01 音声.mp3")), "archive change prevents source deletion"); }
        Console.WriteLine("Isolated test directory: " + root);
        var probe = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ZIP_FOLDER_PROBE");
        if (!string.IsNullOrWhiteSpace(probe))
        {
            var copy = Path.Combine(root, "Read-only original copy.zip.mp3");
            File.Copy(probe, copy);
            var expected = ZipAlbumReader.Open(copy);
            var actual = ZipFolderConversion.Convert(copy);
            Check(actual.Albums.Sum(a => a.Tracks.Count) == expected.Tracks.Count, "real archive copy: all tracks extracted and decoded");
            foreach (var original in expected.Tracks)
                Check(File.Exists(actual.FindTrack(original.FileName).Track.SourcePath), "real track mapped: " + original.FileName);
            foreach (var disc in actual.Albums)
                Console.WriteLine($"Verified disc: {Path.GetFileName(disc.Path)} / {disc.Tracks.Count} tracks");
        }
        var renamed = Path.Combine(root, "New album name.zip.mp3");
        using (var zip = ZipFile.Open(renamed, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Old album name/");
            zip.CreateEntryFromFile(Path.Combine(source, "01 音声.mp3"), "Old album name/01 音声.mp3", CompressionLevel.Optimal);
            zip.CreateEntryFromFile(Path.Combine(source, "booklet", "歌詞.txt"), "Old album name/booklet/歌詞.txt");
            zip.CreateEntry("Old album name/booklet/empty/");
        }
        var extracted = ZipFolderConversion.Convert(renamed);
        Check(Path.GetFileName(extracted.Destination) == "New album name" && File.Exists(Path.Combine(extracted.Destination, "01 音声.mp3")), "current ZIP name replaces old wrapper");
        Check(Directory.Exists(Path.Combine(extracted.Destination, "booklet", "empty")), "nested and empty directories retained");
        Check(extracted.EntryPaths["Old album name/01 音声.mp3"] == "01 音声.mp3", "old/new entry identity mapping");
        try { ZipFolderConversion.Convert(renamed); throw new Exception("existing directory overwritten"); }
        catch (IOException) { Check(File.Exists(renamed), "existing destination blocks extraction"); }
        ZipFolderConversion.DeleteVerifiedSource(extracted);
        Check(!File.Exists(renamed) && Directory.Exists(extracted.Destination), "confirmed ZIP removal preserves extracted files");
        var roundtrip = FolderZipConversion.Convert(extracted.Destination);
        Check(roundtrip.Album.Tracks.Count == 1, "DIR can convert back to ZIP.MP3");
        foreach (var wrapped in new[] { false, true })
        {
            var multi = Path.Combine(root, wrapped ? "Wrapped discs.zip.mp3" : "Discs.zip.mp3");
            var prefix = wrapped ? "Old title/" : "";
            using (var zip = ZipFile.Open(multi, ZipArchiveMode.Create))
            {
                if (wrapped) zip.CreateEntry(prefix);
                foreach (var disc in new[] { "Disc1", "Disc2" })
                {
                    zip.CreateEntryFromFile(Path.Combine(source, "01 音声.mp3"), prefix + disc + "/01 音声.mp3", CompressionLevel.Optimal);
                    zip.CreateEntryFromFile(Path.Combine(source, "booklet", "歌詞.txt"), prefix + disc + "/01 音声.txt");
                }
                zip.CreateEntry(prefix + "Booklet/empty/");
            }
            var originalHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(multi));
            var discs = ZipFolderConversion.Convert(multi);
            Check(discs.Albums.Count == 1 && discs.Album.Tracks.Count == 2, "multiple disc folders form one album");
            Check(discs.Album.Tracks.Select(t => t.DiscNumber).SequenceEqual(new[] { 1, 2 }), "disc folder names preserve playback order");
            Check(!FolderAlbumLayout.IsSupersededArchive(multi), "source remains visible until metadata migration completes");
            ZipFolderConversion.CompleteReplacement(discs);
            Check(FolderAlbumLayout.IsSupersededArchive(multi), "completed conversion suppresses original ZIP without deleting it");
            foreach (var disc in new[] { "Disc1", "Disc2" })
            {
                var mapped = discs.FindTrack(prefix + disc + "/01 音声.mp3");
                Check(mapped.Album.Path == discs.Destination && mapped.Track.FileName == disc + "/01 音声.mp3"
                    && mapped.Track.SourcePath == Path.Combine(discs.Destination, disc, "01 音声.mp3"),
                    "identical filenames map to correct disc: " + disc);
                Check(File.ReadAllBytes(mapped.Track.SourcePath).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "01 音声.mp3"))),
                    "extracted audio bytes unchanged");
                Check(ZipAlbumReader.OpenFolder(mapped.Album.Path).Tracks.Count == 2, "group reload retains both discs");
                Check(FolderAlbumLayout.RootFor(Path.Combine(discs.Destination, disc)) == discs.Destination, "child scan resolves to grouped album");
            }
            Check(Directory.Exists(Path.Combine(discs.Destination, "Booklet", "empty")), "shared booklet hierarchy retained");
            Check(originalHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(multi))), "original multidisc ZIP unchanged");
            VerifyGroupedFolderDiscovery(discs, root);
            File.AppendAllText(discs.FindTrack(prefix + "Disc2/01 音声.mp3").Track.SourcePath, "changed");
            try { ZipFolderConversion.DeleteVerifiedSource(discs); throw new Exception("changed disc allowed deletion"); }
            catch (IOException) { Check(File.Exists(multi), "changed second disc prevents ZIP deletion"); }
        }
        foreach (var name in new[] { "../escape.mp3", "/absolute.mp3", "C:/drive.mp3", "a:stream", "CON.txt", "dir/../escape", "trailing. " })
        {
            try { ZipFolderConversion.SafePath(root, name); throw new Exception("unsafe path allowed"); }
            catch (IOException) { Console.WriteLine("PASS unsafe ZIP path rejected: " + name); }
        }
    }

    private static void VerifyGroupedFolderDiscovery(ZipFolderConversion.Result result, string testRoot)
    {
        var stage = Path.Combine(testRoot, ".zip-folder-" + Guid.NewGuid().ToString("N"), "Disc1");
        Directory.CreateDirectory(stage);
        File.Copy(result.Album.Tracks[0].SourcePath, Path.Combine(stage, "01.mp3"));
        if (!FolderAlbumLayout.IsTemporary(stage) || FolderAlbumLayout.IsTemporary(Path.Combine(testRoot, ".zip-folder-my-album")))
            throw new Exception("temporary folder classification");
        var mainType = typeof(MainWindow);
        var scan = mainType.GetMethod("ScanWorker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var updateType = mainType.GetNestedType("ScanUpdate", System.Reflection.BindingFlags.NonPublic)!;
        var progress = Activator.CreateInstance(typeof(Progress<>).MakeGenericType(updateType))!;
        var scanned = (System.Collections.IEnumerable)scan.Invoke(null,
            new object[] { new[] { result.Destination, Path.GetDirectoryName(stage)! }, progress, CancellationToken.None })!;
        var paths = scanned.Cast<object>().Select(item => ((ZipAlbum)item.GetType().GetProperty("Album")!.GetValue(item)!).Path).ToArray();
        if (paths.Length != 1 || paths[0] != result.Destination) throw new Exception("scan must yield one album and exclude partial extraction");
        var discover = mainType.GetMethod("DiscoverUntrackedAlbums", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var missing = (IReadOnlyList<string>)discover.Invoke(null, new object[] {
            new[] { result.Destination, Path.GetDirectoryName(stage)! }, new[] { result.Destination }, CancellationToken.None })!;
        if (missing.Count != 0) throw new Exception("discovery resurrected discs or temporary files");
        Console.WriteLine("PASS full scan and rediscovery: single album, no temporary-folder duplicates");
    }
}
