using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO.Compression;
using System.Security.Cryptography;
using ZipMp3Player;

var root = Path.Combine(Path.GetTempPath(), "ZipMp3Player-TagEditTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestHalfWidthNormalization();
    var wav = Path.Combine(root, "source.wav");
    WaveFileWriter.CreateWaveFile16(wav, new SignalGenerator(44100, 2)
        { Frequency = 440, Gain = 0.12, Type = SignalGeneratorType.Sin }.Take(TimeSpan.FromSeconds(1)));
    var first = Path.Combine(root, "01 first.mp3");
    var second = Path.Combine(root, "02 second.mp3");
    using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(reader, first, 192000);
    File.Copy(first, second);
    SetInitial(first, "First", 2); SetInitial(second, "Second", 3);

    TestNormal(root, first);
    foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        TestArchive(root, first, second, compression);
    Console.WriteLine("Normal MP3, album-wide ZIP tag editing, and Deflate-to-Store conversion tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void TestNormal(string root, string source)
{
    var folder = Path.Combine(root, "normal"); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "track.mp3"); File.Copy(source, path);
    var originalAudio = AudioPayloadHash(path);
    var album = ZipAlbumReader.OpenFolder(folder); var track = album.Tracks.Single();
    var values = Values("通常編集", 3);
    var result = TrackTagWriteService.WriteAlbum(album, [new(track.FileName, track.SourcePath, values)]);
    Require(result.BackupPaths.Count == 0 && !Directory.EnumerateFiles(folder).Any(file => file.Contains("tag-backup", StringComparison.OrdinalIgnoreCase)), "normal backup disabled by default");
    VerifyTag(path, values); Require(AudioPayloadHash(path) == originalAudio, "normal audio payload unchanged");
    var backupFolder = Path.Combine(root, "tag-backups");
    var backedUpValues = Values("バックアップあり", 4);
    var backedUp = TrackTagWriteService.WriteAlbum(ZipAlbumReader.OpenFolder(folder),
        [new(track.FileName, track.SourcePath, backedUpValues)], new TrackTagBackupOptions(true, backupFolder));
    Require(backedUp.BackupPaths.Count == 1 && File.Exists(backedUp.BackupPaths[0])
        && string.Equals(Path.GetDirectoryName(backedUp.BackupPaths[0]), backupFolder, StringComparison.OrdinalIgnoreCase), "normal dedicated backup folder");
    VerifyTag(path, backedUpValues); Require(AudioPayloadHash(path) == originalAudio, "backed-up normal audio payload unchanged");
}

static void TestArchive(string root, string first, string second, CompressionLevel compression)
{
    var kind = compression == CompressionLevel.NoCompression ? "store" : "deflate";
    var path = Path.Combine(root, kind + ".zip.mp3");
    var marker = new byte[] { 1, 3, 5, 7, 9 };
    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
    {
        Add(archive, "Album/01 日本語 Ⅲ α.mp3", first, compression);
        Add(archive, "Album/02 second.mp3", second, compression);
        var image = archive.CreateEntry("Album/cover.jpg", compression); using (var output = image.Open()) output.Write(marker);
        var lyrics = archive.CreateEntry("Album/01 日本語 Ⅲ α.lrc", compression); using var writer = new StreamWriter(lyrics.Open()); writer.Write("[00:01]test");
    }
    var originalArchiveHash = Hash(path);
    var album = ZipAlbumReader.Open(path);
    Require(album.Tracks.Any(track => track.Title == "First" && track.Artist == "Original" && track.Album == "Original Album"), kind + " ID3v2.2 tag read");
    var beforeAudio = album.Tracks.ToDictionary(track => track.FileName, track => ArchivedAudioHash(track));
    var updates = album.Tracks.Select((track, index) => new TrackTagUpdate(track.FileName, track.SourcePath,
        Values(index == 0 ? "ZIP一曲目" : "ZIP二曲目", (uint)(index + 1)))).ToList();
    var tagBackupFolder = Path.Combine(root, "zip-tag-backups");
    var backupOptions = compression == CompressionLevel.NoCompression ? null : new TrackTagBackupOptions(true, tagBackupFolder);
    var result = TrackTagWriteService.WriteAlbum(album, updates, backupOptions);
    if (backupOptions is null)
        Require(result.BackupPaths.Count == 0 && !Directory.EnumerateFiles(root).Any(file => Path.GetFileName(file).StartsWith(Path.GetFileName(path) + ".tag-backup", StringComparison.OrdinalIgnoreCase)), kind + " no album-folder tag backup");
    else
    {
        Require(result.BackupPaths.Count == 1 && File.Exists(result.BackupPaths[0])
            && string.Equals(Path.GetDirectoryName(result.BackupPaths[0]), tagBackupFolder, StringComparison.OrdinalIgnoreCase), kind + " dedicated backup folder");
        Require(Hash(result.BackupPaths[0]) == originalArchiveHash, kind + " backup exact original");
    }
    var rebuilt = ZipAlbumReader.Open(path);
    Require(rebuilt.Tracks.Count == 2 && rebuilt.Images.Count == 1 && rebuilt.TextFiles.Count == 1, kind + " entry counts");
    for (var index = 0; index < rebuilt.Tracks.Count; index++)
    {
        var expected = updates.Single(update => update.FileName == rebuilt.Tracks[index].FileName).Values;
        VerifyTrack(rebuilt.Tracks[index], expected);
        Require(ArchivedAudioHash(rebuilt.Tracks[index]) == beforeAudio[rebuilt.Tracks[index].FileName], kind + " audio payload unchanged");
    }
    using (var zip = ZipFile.OpenRead(path))
    {
        using var imageInput = zip.GetEntry("Album/cover.jpg")!.Open(); using var memory = new MemoryStream(); imageInput.CopyTo(memory);
        Require(memory.ToArray().SequenceEqual(marker), kind + " non-audio entry unchanged");
    }
    if (compression == CompressionLevel.NoCompression)
        Require(!ZipStorageConversionService.HasCompressedEntries(path), "store detection");
    else
    {
        Require(ZipStorageConversionService.HasCompressedEntries(path), "deflate detection");
        var beforeConversion = Hash(path);
        var conversion = ZipStorageConversionService.ConvertToStored(path);
        Require(File.Exists(conversion.BackupPath) && Hash(conversion.BackupPath) == beforeConversion, "conversion exact backup");
        Require(!ZipStorageConversionService.HasCompressedEntries(path), "converted archive is Store");
        var stored = ZipAlbumReader.Open(path);
        Require(stored.Tracks.Count == 2 && stored.Images.Count == 1 && stored.TextFiles.Count == 1, "conversion entry counts");
        Require(stored.Tracks.Any(track => track.FileName.Contains("Ⅲ α", StringComparison.Ordinal)), "conversion Unicode names");
        for (var index = 0; index < stored.Tracks.Count; index++)
            VerifyTrack(stored.Tracks[index], updates.Single(update => update.FileName == stored.Tracks[index].FileName).Values);
    }
}

static void TestHalfWidthNormalization()
{
    var full = "０１２３４５６７８９ＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ";
    var ascii = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric(full) == ascii, "every full-width Latin letter and digit");
    const string untouched = "日本語 カタカナ ｶﾀｶﾅ ①Ⅳ㍑ ﬁ é α ～－．［］😀\t\n\u00a0\u2003";
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric(untouched) == untouched, "Japanese, symbols, compatibility characters, spaces and emoji preserved");
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric("Ｍｒ.Children ＢＯＬＥＲＯ １９９７") == "Mr.Children BOLERO 1997", "mixed width text");
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric("　日本語　　曲名　") == " 日本語  曲名 ", "spaces-only conversion preserves leading, trailing and repeated spaces");
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric("Ａ　 B　　１２\t\n") == "A  B  12\t\n", "mixed letters/digits/spaces and line breaks");
    Require(TagTextNormalization.ToHalfWidthAlphaNumeric(ascii) == ascii && TagTextNormalization.ToHalfWidthAlphaNumeric("") == "", "unchanged and empty inputs");
    Console.WriteLine("Full-width alphanumeric conversion and preservation tests passed.");
}

static TrackTagValues Values(string title, uint track) => new(TagTextNormalization.ToHalfWidthAlphaNumeric(title + "　ＡＢＣ１２３"),
    TagTextNormalization.ToHalfWidthAlphaNumeric("編集　Ｍｒ.Children"), TagTextNormalization.ToHalfWidthAlphaNumeric("編集　　ＢＯＬＥＲＯ"), 2026, "Rock", track, 1, 2);
static void SetInitial(string path, string title, byte id3Version)
{
    var previousVersion = TagLib.Id3v2.Tag.DefaultVersion;
    var previousForce = TagLib.Id3v2.Tag.ForceDefaultVersion;
    try
    {
        TagLib.Id3v2.Tag.DefaultVersion = id3Version;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
        using var file = TagLib.File.Create(path);
        file.Tag.Title = title; file.Tag.Performers = ["Original"]; file.Tag.Album = "Original Album"; file.Save();
    }
    finally
    {
        TagLib.Id3v2.Tag.DefaultVersion = previousVersion;
        TagLib.Id3v2.Tag.ForceDefaultVersion = previousForce;
    }
}
static void VerifyTag(string path, TrackTagValues value) { using var file = TagLib.File.Create(path); Require(file.Tag.Title == value.Title && file.Tag.FirstPerformer == value.Artist && file.Tag.Album == value.Album && file.Tag.Year == value.Year && file.Tag.FirstGenre == value.Genre && file.Tag.Track == value.TrackNumber && file.Tag.Disc == value.DiscNumber && file.Tag.DiscCount == value.DiscCount, "tag values"); }
static void VerifyTrack(ZipTrack track, TrackTagValues value) => Require(track.Title == value.Title && track.Artist == value.Artist && track.Album == value.Album && track.Year == value.Year.ToString() && track.Genre == value.Genre && track.TrackNumber == value.TrackNumber && track.DiscNumber == value.DiscNumber && track.DiscCount == value.DiscCount, "ZIP reader tag values");
static void Add(ZipArchive archive, string name, string source, CompressionLevel compression) { var entry = archive.CreateEntry(name, compression); using var input = File.OpenRead(source); using var output = entry.Open(); input.CopyTo(output); }
static string ArchivedAudioHash(ZipTrack track) { using var input = ArchiveEntryExtractor.OpenSeekable(track); return AudioPayloadStreamHash(input); }
static string AudioPayloadHash(string path) { using var input = File.OpenRead(path); return AudioPayloadStreamHash(input); }
static string AudioPayloadStreamHash(Stream input)
{
    using var memory = new MemoryStream(); input.CopyTo(memory); var bytes = memory.ToArray();
    var start = bytes.Length >= 10 && bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3'
        ? 10 + ((bytes[6] & 0x7F) << 21) + ((bytes[7] & 0x7F) << 14) + ((bytes[8] & 0x7F) << 7) + (bytes[9] & 0x7F)
        : 0;
    var end = bytes.Length; if (end >= 128 && bytes[end - 128] == (byte)'T' && bytes[end - 127] == (byte)'A' && bytes[end - 126] == (byte)'G') end -= 128;
    return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(start, end - start)));
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException("Failed: " + label); }
