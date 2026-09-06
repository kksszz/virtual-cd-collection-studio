using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO.Compression;
using System.Security.Cryptography;
using ZipMp3Player;

var diagnosticArchive = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TAG_DIAG_ARCHIVE");
if (!string.IsNullOrWhiteSpace(diagnosticArchive))
{
    DiagnoseJapaneseTag(diagnosticArchive);
    return;
}
var albumDiagnosticArchive = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_ALBUM_DIAG_ARCHIVE");
if (!string.IsNullOrWhiteSpace(albumDiagnosticArchive))
{
    DiagnoseAlbumTags(albumDiagnosticArchive);
    return;
}
var diagnosticMp3Directory = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_MP3_DIAG_DIR");
if (!string.IsNullOrWhiteSpace(diagnosticMp3Directory))
{
    DiagnoseMp3Files(diagnosticMp3Directory);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "ZipMp3Player-TagEditTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TestHalfWidthNormalization();
    TestTitleCaseNormalization();
    TestTagAnomalyDetection();
    Require(!ZipAlbumReader.IsStandardAudioPath(@"C:\Album\.01. Newborn Me.cb17b4dfb80246d1a48b297b0d267062.tagtmp.mp3"),
        "tag-edit temporary MP3 excluded from folder albums");
    Require(ZipAlbumReader.IsStandardAudioPath(@"C:\Album\01. Newborn Me.mp3"),
        "ordinary MP3 remains included in folder albums");
    var wav = Path.Combine(root, "source.wav");
    WaveFileWriter.CreateWaveFile16(wav, new SignalGenerator(44100, 2)
        { Frequency = 440, Gain = 0.12, Type = SignalGeneratorType.Sin }.Take(TimeSpan.FromSeconds(1)));
    var first = Path.Combine(root, "01 first.mp3");
    var second = Path.Combine(root, "02 second.mp3");
    using (var reader = new WaveFileReader(wav)) MediaFoundationEncoder.EncodeToMp3(reader, first, 192000);
    File.Copy(first, second);
    SetInitial(first, "First", 2); SetInitial(second, "Second", 3);

    TestUnsynchronizedJapaneseId3(root, first);
    TestMalformedPictureFrame(root, first);
    TestNormal(root, first);
    foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        TestArchive(root, first, second, compression);
    TestArchiveRename(root, first);
    Console.WriteLine("Normal/ZIP filename editing, album-wide tag editing, and Deflate-to-Store conversion tests passed.");
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

    var renameAlbum = ZipAlbumReader.OpenFolder(folder);
    var renameTrack = renameAlbum.Tracks.Single();
    var renamedPath = Path.Combine(folder, "renamed track.mp3");
    var renamedValues = Values("ファイル名変更", 5);
    TrackTagWriteService.WriteAlbum(renameAlbum,
        [new(renameTrack.FileName, renameTrack.SourcePath, renamedValues, "renamed track.mp3")]);
    Require(!File.Exists(path) && File.Exists(renamedPath), "normal file renamed");
    VerifyTag(renamedPath, renamedValues);
    Require(AudioPayloadHash(renamedPath) == originalAudio, "renamed normal audio payload unchanged");

    var readOnlyFolder = Path.Combine(root, "read-only"); Directory.CreateDirectory(readOnlyFolder);
    var readOnlyPath = Path.Combine(readOnlyFolder, "read-only.mp3"); File.Copy(source, readOnlyPath);
    File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) | FileAttributes.ReadOnly);
    var readOnlyTrack = ZipAlbumReader.OpenFolder(readOnlyFolder).Tracks.Single();
    var readOnlyValues = Values("読み取り専用編集", 6);
    TrackTagWriteService.WriteAlbum(ZipAlbumReader.OpenFolder(readOnlyFolder),
        [new(readOnlyTrack.FileName, readOnlyTrack.SourcePath, readOnlyValues)]);
    VerifyTag(readOnlyPath, readOnlyValues);
    Require((File.GetAttributes(readOnlyPath) & FileAttributes.ReadOnly) != 0, "read-only attribute restored after tag edit");
    Require(!Directory.EnumerateFiles(readOnlyFolder, "*.tagtmp.mp3").Any(), "read-only tag temporary file removed");
    File.SetAttributes(readOnlyPath, FileAttributes.Normal);
}

static void TestArchiveRename(string root, string source)
{
    var path = Path.Combine(root, "rename.zip.mp3");
    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        Add(archive, "【ALBUM】日本語\\01 original.mp3", source, CompressionLevel.NoCompression);
    var album = ZipAlbumReader.Open(path);
    var track = album.Tracks.Single();
    var audioHash = ArchivedAudioHash(track);
    var values = Values("ZIPファイル名変更", 1);
    TrackTagWriteService.WriteAlbum(album,
        [new(track.FileName, track.SourcePath, values, "【ALBUM】日本語\\01 renamed.mp3")]);
    var rebuilt = ZipAlbumReader.Open(path);
    var renamed = rebuilt.Tracks.Single();
    Require(renamed.FileName == "【ALBUM】日本語\\01 renamed.mp3", "legacy backslash ZIP entry renamed");
    VerifyTrack(renamed, values);
    Require(ArchivedAudioHash(renamed) == audioHash, "renamed ZIP audio payload unchanged");
    using var zip = ZipFile.OpenRead(path);
    Require(zip.GetEntry("【ALBUM】日本語\\01 original.mp3") is null && zip.GetEntry("【ALBUM】日本語\\01 renamed.mp3") is not null,
        "ZIP contains only renamed entry");
}

static void TestUnsynchronizedJapaneseId3(string root, string source)
{
    var folder = Path.Combine(root, "unsynchronized-japanese");
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "日本語.mp3");
    var sourceBytes = File.ReadAllBytes(source);
    var audioStart = sourceBytes.Length >= 10 && sourceBytes.AsSpan(0, 3).SequenceEqual("ID3"u8)
        ? 10 + ((sourceBytes[6] & 0x7f) << 21) + ((sourceBytes[7] & 0x7f) << 14)
            + ((sourceBytes[8] & 0x7f) << 7) + (sourceBytes[9] & 0x7f)
        : 0;
    // TPE1 payload before unsynchronization is 11 bytes:
    // encoding=1, UTF-16LE BOM, 徳永英明. The stored FF 00 FE sequence
    // contains one inserted protection byte while the frame size remains 11.
    byte[] storedFrameData = [0x01, 0xff, 0x00, 0xfe, 0xb3, 0x5f, 0x38, 0x6c, 0xf1, 0x82, 0x0e, 0x66];
    var bodySize = 10 + storedFrameData.Length;
    using (var output = File.Create(path))
    {
        output.Write([0x49, 0x44, 0x33, 0x03, 0x00, 0x80,
            (byte)((bodySize >> 21) & 0x7f), (byte)((bodySize >> 14) & 0x7f),
            (byte)((bodySize >> 7) & 0x7f), (byte)(bodySize & 0x7f)]);
        output.Write("TPE1"u8);
        output.Write([0x00, 0x00, 0x00, 0x0b, 0x00, 0x00]);
        output.Write(storedFrameData);
        output.Write(sourceBytes, audioStart, sourceBytes.Length - audioStart);
    }
    var track = ZipAlbumReader.OpenFolder(folder).Tracks.Single();
    Require(track.Artist == "徳永英明" && !track.Artist.Contains('\uFFFD'),
        "ID3v2.3 whole-tag unsynchronization preserves final Japanese UTF-16 character");

    var archivePath = Path.Combine(root, "unsynchronized-japanese.zip.mp3");
    using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        Add(archive, "Album/02.いい日旅立ち.mp3", path, CompressionLevel.NoCompression);
    var archiveAlbum = ZipAlbumReader.Open(archivePath);
    var archiveTrack = archiveAlbum.Tracks.Single();
    var values = new TrackTagValues("02.いい日旅立ち", "徳永英明", "VOCALIST 2", 2006, "", 2, 0, 0);
    TrackTagWriteService.WriteAlbum(archiveAlbum,
        [new(archiveTrack.FileName, archiveTrack.SourcePath, values)]);
    VerifyTrack(ZipAlbumReader.Open(archivePath).Tracks.Single(), values);
}

static void TestMalformedPictureFrame(string root, string source)
{
    var folder = Path.Combine(root, "malformed-picture");
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "bad-apic.mp3");
    var sourceBytes = File.ReadAllBytes(source);
    var audioStart = sourceBytes.Length >= 10 && sourceBytes.AsSpan(0, 3).SequenceEqual("ID3"u8)
        ? 10 + ((sourceBytes[6] & 0x7f) << 21) + ((sourceBytes[7] & 0x7f) << 14)
            + ((sourceBytes[8] & 0x7f) << 7) + (sourceBytes[9] & 0x7f)
        : 0;
    byte[] apic = [0x41, 0x50, 0x49, 0x43, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0];
    byte[] artist = [0x54, 0x50, 0x45, 0x31, 0, 0, 0, 4, 0, 0, 0, 0x4d, 0x32, 0x4d];
    var bodySize = apic.Length + artist.Length;
    using (var output = File.Create(path))
    {
        output.Write([0x49, 0x44, 0x33, 0x03, 0x00, 0x00,
            (byte)((bodySize >> 21) & 0x7f), (byte)((bodySize >> 14) & 0x7f),
            (byte)((bodySize >> 7) & 0x7f), (byte)(bodySize & 0x7f)]);
        output.Write(apic);
        output.Write(artist);
        output.Write(sourceBytes, audioStart, sourceBytes.Length - audioStart);
    }
    var beforeAudio = AudioPayloadHash(path);
    var archivePath = Path.Combine(root, "malformed-picture.zip.mp3");
    using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        Add(archive, "Album/Don't Say You Love Me.mp3", path, CompressionLevel.NoCompression);
    var album = ZipAlbumReader.OpenFolder(folder);
    var track = album.Tracks.Single();
    var values = new TrackTagValues("Don't Say You Love Me", "M2M", "Acoustics", 2000, "Pop", 2, 0, 0);
    TrackTagWriteService.WriteAlbum(album, [new(track.FileName, track.SourcePath, values)]);
    VerifyTag(path, values);
    Require(AudioPayloadHash(path) == beforeAudio, "malformed APIC repair leaves audio payload unchanged");

    var archiveAlbum = ZipAlbumReader.Open(archivePath);
    var archiveTrack = archiveAlbum.Tracks.Single();
    var archivedAudio = ArchivedAudioHash(archiveTrack);
    TrackTagWriteService.WriteAlbum(archiveAlbum,
        [new(archiveTrack.FileName, archiveTrack.SourcePath, values)]);
    var rebuilt = ZipAlbumReader.Open(archivePath).Tracks.Single();
    VerifyTrack(rebuilt, values);
    Require(ArchivedAudioHash(rebuilt) == archivedAudio,
        "malformed APIC repair inside ZIP leaves audio payload unchanged");
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

static void TestTitleCaseNormalization()
{
    Require(TagTextNormalization.UpperCaseWordsToTitleCase("SOUL DOCTOR") == "Soul Doctor", "all-caps artist title case");
    Require(TagTextNormalization.UpperCaseWordsToTitleCase("HARD ROCK") == "Hard Rock", "all-caps genre title case");
    Require(TagTextNormalization.UpperCaseWordsToTitleCase("For a fistful of Dollars") == "For a fistful of Dollars", "mixed-case album preserved");
    Require(TagTextNormalization.UpperCaseWordsToTitleCase("Track01") == "Track01", "mixed-case title preserved");
    Require(TagTextNormalization.UpperCaseWordsToTitleCase("日本語") == "日本語", "uncased Japanese preserved");
    Console.WriteLine("Selected all-caps title-case conversion tests passed.");
}

static void TestTagAnomalyDetection()
{
    var rows = new[]
    {
        new TagAnomalyInput("Band - Album - 01 - First.mp3", "First", "Band", "Album", "2000", "Rock", "2", ""),
        new TagAnomalyInput("Band - Album - 02 - Second.mp3", "Second", "Band", "Album", "", "", "2", ""),
        new TagAnomalyInput("Band - Album - 03 - Third.mp3", "Third", "", "", "", "", "32", "")
    };
    var codes = TagAnomalyDetector.Analyze(rows).Select(item => item.Code).ToHashSet();
    Require(codes.Contains("TRACK_DUPLICATE") && codes.Contains("TRACK_OUTLIER") && codes.Contains("TRACK_GAPS")
        && codes.Contains("FILENAME_TRACK_MISMATCH") && codes.Contains("ARTIST_MISSING")
        && codes.Contains("ALBUM_MISSING") && codes.Contains("YEAR_MISSING") && codes.Contains("GENRE_MISSING"),
        "tag anomaly detection covers numbering, filename mismatch, and missing metadata");
    var clean = TagAnomalyDetector.Analyze(new[]
    {
        new TagAnomalyInput("01 - First.mp3", "First", "Band", "Album", "2000", "Rock", "1", ""),
        new TagAnomalyInput("02 - Second.mp3", "Second", "Band", "Album", "2000", "Rock", "2", "")
    });
    Require(clean.Count == 0, "consistent tags do not produce anomaly warnings");
    var continuedNumbering = TagAnomalyDetector.Analyze(new[]
    {
        new TagAnomalyInput("203-killswitch-engage-first.mp3", "First", "Killswitch Engage", "The End Of Heartache", "2005", "Metal", "15", ""),
        new TagAnomalyInput("204-killswitch-engage-second.mp3", "Second", "Killswitch Engage", "The End Of Heartache", "2005", "Metal", "16", ""),
        new TagAnomalyInput("205-killswitch-engage-third.mp3", "Third", "Killswitch Engage", "The End Of Heartache", "2005", "Metal", "17", ""),
        new TagAnomalyInput("206-killswitch-engage-fourth.mp3", "Fourth", "Killswitch Engage", "The End Of Heartache", "2005", "Metal", "18", "")
    });
    Require(continuedNumbering.Count == 0,
        "continued track numbering and a consistent file-name offset do not produce false warnings");
    Console.WriteLine("Tag anomaly detection tests passed.");
}

static void DiagnoseJapaneseTag(string archivePath)
{
    var album = ZipAlbumReader.Open(archivePath);
    var track = album.Tracks.First(item => item.FileName.StartsWith("02.", StringComparison.Ordinal));
    var diagnosticFolder = Path.Combine(Path.GetTempPath(), "ZipMp3Player-TagDiag-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(diagnosticFolder);
    var temporary = Path.Combine(diagnosticFolder, "02 test.mp3");
    try
    {
        using (var input = ArchiveEntryExtractor.OpenSeekable(track))
        using (var output = File.Create(temporary)) input.CopyTo(output);
        using (var file = TagLib.File.Create(temporary))
        {
            Console.WriteLine($"Entry={track.FileName}");
            Console.WriteLine($"Before Artist=[{file.Tag.FirstPerformer}] Types={file.TagTypes}");
            foreach (var tagType in new[] { TagLib.TagTypes.Id3v1, TagLib.TagTypes.Id3v2 })
            {
                var tag = file.GetTag(tagType, false);
                if (tag is not null) Console.WriteLine($"{tagType}: Artist=[{tag.FirstPerformer}] Title=[{tag.Title}] Album=[{tag.Album}]");
            }
            file.Tag.Performers = ["徳永英明"];
            file.Tag.Album = "VOCALIST 2";
            file.Save();
        }
        using (var reopened = TagLib.File.Create(temporary))
        {
            Console.WriteLine($"After Artist=[{reopened.Tag.FirstPerformer}] Types={reopened.TagTypes}");
            foreach (var tagType in new[] { TagLib.TagTypes.Id3v1, TagLib.TagTypes.Id3v2 })
            {
                var tag = reopened.GetTag(tagType, false);
                if (tag is not null) Console.WriteLine($"{tagType}: Artist=[{tag.FirstPerformer}] Title=[{tag.Title}] Album=[{tag.Album}]");
            }
        }
        DumpId3TextFrames(temporary);
        var custom = ZipAlbumReader.OpenFolder(diagnosticFolder).Tracks.Single();
        Console.WriteLine($"Custom reader after save: Artist=[{custom.Artist}] Album=[{custom.Album}]");
    }
    finally { try { Directory.Delete(diagnosticFolder, true); } catch { } }
}

static void DiagnoseAlbumTags(string archivePath)
{
    var album = ZipAlbumReader.Open(archivePath);
    var diagnosticFolder = Path.Combine(Path.GetTempPath(), "ZipMp3Player-AlbumDiag-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(diagnosticFolder);
    try
    {
        Console.WriteLine($"Album={archivePath}");
        Console.WriteLine($"Tracks={album.Tracks.Count}");
        Console.WriteLine("Index\tFile\tAppTrack\tAppTitle\tAppArtist\tAppAlbum\tAppYear\tAppGenre\tTagTrack\tTagTitle\tTagArtist\tTagAlbum\tTagYear\tTagGenre");
        for (var index = 0; index < album.Tracks.Count; index++)
        {
            var track = album.Tracks[index];
            var temporary = Path.Combine(diagnosticFolder, $"{index:D2}.mp3");
            using (var input = ArchiveEntryExtractor.OpenSeekable(track))
            using (var output = File.Create(temporary)) input.CopyTo(output);
            using var file = TagLib.File.Create(temporary);
            static string Clean(string? value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            Console.WriteLine(string.Join('\t', new object[]
            {
                index + 1, Clean(track.FileName), track.TrackNumber, Clean(track.Title), Clean(track.Artist), Clean(track.Album),
                Clean(track.Year), Clean(track.Genre), file.Tag.Track, Clean(file.Tag.Title), Clean(file.Tag.FirstPerformer),
                Clean(file.Tag.Album), file.Tag.Year, Clean(file.Tag.FirstGenre)
            }));
        }
    }
    finally { try { Directory.Delete(diagnosticFolder, true); } catch { } }
}

static void DiagnoseMp3Files(string directory)
{
    foreach (var path in Directory.EnumerateFiles(directory, "*.mp3").OrderBy(path => path))
    {
        Console.WriteLine($"File={Path.GetFileName(path)} bytes={new FileInfo(path).Length}");
        DumpId3TextFrames(path);
        try
        {
            using var file = TagLib.File.Create(path);
            Console.WriteLine($"TagLib: duration={file.Properties.Duration} bitrate={file.Properties.AudioBitrate} rate={file.Properties.AudioSampleRate} codecs={string.Join(',', file.Properties.Codecs.Select(codec => codec.Description))}");
            if (file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
            {
                var frames = id3.GetFrames<TagLib.Id3v2.AttachmentFrame>().ToList();
                Console.WriteLine($"APIC frames={frames.Count}");
                foreach (var frame in frames)
                    try { Console.WriteLine($"  size={frame.Size} loaded={frame.IsLoaded} data={frame.Data.Count} mime={frame.MimeType}"); }
                    catch (Exception ex) { Console.WriteLine($"  APIC error {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"TagLib error: {ex.Message}"); }
        try
        {
            using var reader = new Mp3FileReader(path);
            Console.WriteLine($"NAudio: duration={reader.TotalTime} rate={reader.WaveFormat.SampleRate} channels={reader.WaveFormat.Channels}");
        }
        catch (Exception ex) { Console.WriteLine($"NAudio error: {ex.Message}"); }
    }
    try
    {
        var album = ZipAlbumReader.OpenFolder(directory);
        foreach (var track in album.Tracks)
            Console.WriteLine($"Custom: {track.FileName} valid={track.IsMp3Valid} cbr={track.IsCbr} bitrate={track.BitrateKbps} rate={track.SampleRate} duration={track.Duration} error=[{track.ReadError}]");
    }
    catch (Exception ex) { Console.WriteLine($"Custom error: {ex}"); }
}

static void DumpId3TextFrames(string path)
{
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length < 10 || !bytes.AsSpan(0, 3).SequenceEqual("ID3"u8)) return;
    var major = bytes[3];
    var tagSize = (bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9];
    var position = 10;
    while (position + (major == 2 ? 6 : 10) <= 10 + tagSize)
    {
        var idLength = major == 2 ? 3 : 4;
        var id = System.Text.Encoding.ASCII.GetString(bytes, position, idLength);
        if (id[0] == '\0') break;
        var size = major == 2
            ? (bytes[position + 3] << 16) | (bytes[position + 4] << 8) | bytes[position + 5]
            : major == 4
                ? (bytes[position + 4] << 21) | (bytes[position + 5] << 14) | (bytes[position + 6] << 7) | bytes[position + 7]
                : (bytes[position + 4] << 24) | (bytes[position + 5] << 16) | (bytes[position + 6] << 8) | bytes[position + 7];
        var headerSize = major == 2 ? 6 : 10;
        if (size <= 0 || position + headerSize + size > bytes.Length) break;
        if (id is "TP1" or "TPE1" or "PIC" or "APIC")
            Console.WriteLine($"Raw {id} v2.{major} size={size}: {Convert.ToHexString(bytes.AsSpan(position + headerSize, size))}");
        position += headerSize + size;
    }
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
