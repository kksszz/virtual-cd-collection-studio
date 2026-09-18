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
        foreach (var name in new[] { "../escape.mp3", "/absolute.mp3", "C:/drive.mp3", "a:stream", "CON.txt", "dir/../escape", "trailing. " })
        {
            try { ZipFolderConversion.SafePath(root, name); throw new Exception("unsafe path allowed"); }
            catch (IOException) { Console.WriteLine("PASS unsafe ZIP path rejected: " + name); }
        }
    }
}
