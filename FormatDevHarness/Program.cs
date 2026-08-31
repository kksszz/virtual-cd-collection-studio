using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZipMp3Player;

var temporaryFolder = Path.Combine(Path.GetTempPath(), "ZipMp3Player-FormatTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryFolder);
try
{
    VerifyIncompleteMp3Frames(temporaryFolder);
    var wavPath = Path.Combine(temporaryFolder, "01 WAV動作確認.wav");
    var signal = new SignalGenerator(44100, 2) { Frequency = 440, Gain = 0.15, Type = SignalGeneratorType.Sin };
    WaveFileWriter.CreateWaveFile16(wavPath, signal.Take(TimeSpan.FromSeconds(1)));
    var m4aPath = Path.Combine(temporaryFolder, "02 M4A動作確認.m4a");
    using (var wavForEncoding = new WaveFileReader(wavPath))
        MediaFoundationEncoder.EncodeToAac(wavForEncoding, m4aPath, 192000);
    var mp3Path = Path.Combine(temporaryFolder, "03 Deflate動作確認.mp3");
    using (var wavForMp3Encoding = new WaveFileReader(wavPath))
        MediaFoundationEncoder.EncodeToMp3(wavForMp3Encoding, mp3Path, 192000);
    var deflatedZipPath = Path.Combine(temporaryFolder, "DeflateAlbum.zip.mp3");
    using (var archiveFile = new FileStream(deflatedZipPath, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create))
    {
        var entry = archive.CreateEntry("01 Deflate動作確認.mp3", CompressionLevel.SmallestSize);
        using var entryStream = entry.Open();
        using var mp3Stream = new FileStream(mp3Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        mp3Stream.CopyTo(entryStream);
    }

    var wavAlbum = ZipAlbumReader.OpenFolder(temporaryFolder);
    var wavTrack = wavAlbum.Tracks.Single(track => track.AudioFormat == "WAV");
    Require(wavTrack.AudioFormat == "WAV", "WAV format");
    Require(wavTrack.IsSupported, "WAV support state");
    Require(wavTrack.SampleRate == 44100 && wavTrack.Duration.TotalSeconds > 0.9, "WAV properties");
    DecodeSamples(wavTrack, "WAV decode");
    VerifyFaithfulProvider(wavTrack, "WAV faithful normalized PCM");

    var m4aTrack = wavAlbum.Tracks.Single(track => track.AudioFormat == "M4A");
    Require(m4aTrack.IsSupported, "M4A support state");
    Require(m4aTrack.SampleRate == 44100 && m4aTrack.Duration.TotalSeconds > 0.9, "M4A properties");
    DecodeSamples(m4aTrack, "M4A decode");
    VerifyFaithfulProvider(m4aTrack, "M4A faithful normalized PCM");

    var extractionFolder = Path.Combine(Path.GetTempPath(), "ZipMp3Player", "Playback");
    var temporaryEntriesBefore = ExistingTemporaryEntries(extractionFolder);
    var deflatedAlbum = ZipAlbumReader.Open(deflatedZipPath);
    var deflatedTrack = deflatedAlbum.Tracks.Single();
    Require(deflatedTrack.CompressionMethod == 8, "Deflate ZIP compression method");
    Require(deflatedTrack.CompressedSize > 0 && deflatedTrack.Size > 0, "Deflate ZIP sizes");
    Require(deflatedTrack.IsSupported, "Deflate ZIP support state");
    Require(deflatedTrack.SampleRate == 44100 && deflatedTrack.Duration.TotalSeconds > 0.9,
        "Deflate ZIP scanned properties");
    DecodeSamples(deflatedTrack, "Deflate ZIP playback decode");
    VerifyCachedCbr(deflatedTrack);
    Require(ExistingTemporaryEntries(extractionFolder).SetEquals(temporaryEntriesBefore),
        "Deflate ZIP temporary extraction cleanup");

    var unusualM4aPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_M4A") ?? string.Empty;
    if (File.Exists(unusualM4aPath))
    {
        var unusualAlbum = ZipAlbumReader.OpenFolder(Path.GetDirectoryName(unusualM4aPath)!);
        var unusualTrack = unusualAlbum.Tracks.First(track =>
            string.Equals(track.SourcePath, unusualM4aPath, StringComparison.OrdinalIgnoreCase));
        Require(unusualTrack.IsSupported, "M4A decoded-property fallback support state");
        Require(unusualTrack.SampleRate == 48000 && unusualTrack.Duration > TimeSpan.Zero,
            "M4A decoded-property fallback properties");
        DecodeSamples(unusualTrack, "M4A decoded-property fallback decode");
    }

    var flacPath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_FLAC") ?? string.Empty;
    if (File.Exists(flacPath))
    {
        var flacAlbum = ZipAlbumReader.OpenFolder(Path.GetDirectoryName(flacPath)!);
        var flacTrack = flacAlbum.Tracks.First(track => string.Equals(track.SourcePath, flacPath, StringComparison.OrdinalIgnoreCase));
        Require(flacTrack.AudioFormat == "FLAC", "FLAC format");
        Require(flacTrack.IsSupported, "FLAC support state");
        Require(flacTrack.SampleRate == 48000 && flacTrack.BitsPerSample == 24, "FLAC properties");
        Require(!string.IsNullOrWhiteSpace(flacTrack.Title), "FLAC title");
        PrintReaderFormat(flacTrack, "FLAC reader format");
        DecodeSamples(flacTrack, "FLAC decode");
        VerifyFaithfulProvider(flacTrack, "FLAC faithful normalized PCM");
    }

    if (args.Length > 0 && File.Exists(args[0]))
    {
        var suppliedAlbum = ZipAlbumReader.Open(args[0]);
        var cachePath = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_TEST_CACHE");
        if (!string.IsNullOrEmpty(cachePath))
        {
            // Read the user's existing cache without changing or re-scanning it.
            var cachedAlbums = JsonNode.Parse(File.ReadAllText(cachePath))!["Albums"]!.Deserialize<List<ZipAlbum>>()!;
            var cachedAlbum = cachedAlbums.First(album => string.Equals(album.Path, args[0], StringComparison.OrdinalIgnoreCase));
            var cachedCbrTracks = cachedAlbum.Tracks.Where(track => track.IsCbr).ToList();
            Require(cachedCbrTracks.Count > 0, "Saved library CBR fixture");
            foreach (var cachedTrack in cachedCbrTracks)
            {
                Require(cachedTrack.IsSupported, "Saved library CBR support: " + cachedTrack.Title);
                DecodeSamples(cachedTrack, "Saved library CBR decode: " + cachedTrack.Title);
                VerifySeek(cachedTrack, "Saved library CBR seek: " + cachedTrack.Title, [0.5]);
            }
            Console.WriteLine($"Existing cache: {cachedCbrTracks.Count} CBR tracks restored, decoded and sought without re-scan.");
        }
        Require(suppliedAlbum.Tracks.Count > 0, "Supplied ZIP track scan");
        Require(suppliedAlbum.Tracks.All(track => track.IsSupported), "Supplied ZIP support states");
        Require(suppliedAlbum.Tracks.All(track => track.Duration > TimeSpan.Zero && track.BitrateKbps > 0),
            "Supplied ZIP scanned properties");
        if (args.Skip(1).Any(argument => argument.Equals("--expect-vbr", StringComparison.OrdinalIgnoreCase)))
            Require(suppliedAlbum.Tracks.Any(track => !track.IsCbr), "Supplied ZIP VBR detection");
        DecodeSamples(suppliedAlbum.Tracks[0], "Supplied ZIP playback decode");
        VerifySeek(suppliedAlbum.Tracks[0], "Supplied ZIP seek track 1", [0.1, 0.5, 0.9]);
        foreach (var track in suppliedAlbum.Tracks.Skip(1))
            VerifySeek(track, $"Supplied ZIP seek track {track.TrackNumber}", [0.5]);
        Console.WriteLine(string.Join(Environment.NewLine, suppliedAlbum.Tracks.Select(track =>
            $"  {track.TrackNumber:00}: {(track.IsCbr ? "CBR" : "VBR")} {track.BitrateKbps} kbps, {track.Duration.TotalSeconds:0.000} sec")));
        Console.WriteLine($"Supplied ZIP test passed: {suppliedAlbum.Tracks.Count} tracks.");
    }

    Console.WriteLine("WAV, FLAC, M4A and Deflate ZIP format tests passed.");
}
finally
{
    try { Directory.Delete(temporaryFolder, recursive: true); } catch { }
}

static void DecodeSamples(ZipTrack track, string name)
{
    var readerType = typeof(ZipAlbumReader).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
    using var owner = (IDisposable)readerType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [track])!;
    var reader = (WaveStream)readerType.GetProperty("Reader")!.GetValue(owner)!;
    var samples = reader.ToSampleProvider();
    var buffer = new float[4096];
    var read = samples.Read(buffer, 0, buffer.Length);
    Require(read > 0 && buffer.Take(read).All(float.IsFinite), name);
}

static void VerifyCachedCbr(ZipTrack track)
{
    Require(track.IsCbr, "CBR cache fixture");
    var json = JsonSerializer.SerializeToNode(track, new JsonSerializerOptions { IgnoreReadOnlyProperties = true })!.AsObject();
    foreach (var hasFalseFlag in new[] { false, true })
    {
        // Pre-0.46 caches omit the flag; 0.46 can re-save those tracks with false.
        json.Remove("IsMp3Valid");
        if (hasFalseFlag) json["IsMp3Valid"] = false;
        var restored = json.Deserialize<ZipTrack>()!;
        Require(restored.IsSupported, $"Legacy CBR cache support (false flag: {hasFalseFlag})");
        Require(restored.SupportText == track.SupportText, "Legacy CBR status consistency");
        DecodeSamples(restored, "Legacy CBR cache decode");
        VerifySeek(restored, "Legacy CBR cache seek", [0.5]);
        Require(JsonSerializer.Deserialize<ZipTrack>(JsonSerializer.Serialize(restored))!.IsSupported,
            "Legacy CBR re-save/reload support");
    }
    foreach (var (field, value) in new (string, JsonNode?)[]
    {
        ("IsEncrypted", JsonValue.Create(true)), ("ReadError", JsonValue.Create("Invalid ZIP")),
        ("CompressionMethod", JsonValue.Create(99)), ("IsCbr", JsonValue.Create(false)),
        ("BitrateKbps", JsonValue.Create(0)), ("SampleRate", JsonValue.Create(0)),
        ("Duration", JsonValue.Create("00:00:00"))
    })
    {
        var invalid = json.DeepClone().AsObject();
        invalid[field] = value;
        Require(!invalid.Deserialize<ZipTrack>()!.IsSupported, $"Invalid legacy track still blocked: {field}");
    }
    Console.WriteLine("Legacy CBR cache compatibility, decode, seek and invalid-track checks passed.");
}

static void VerifyIncompleteMp3Frames(string parent)
{
    var folder = Path.Combine(parent, "FrameValidation");
    Directory.CreateDirectory(folder);
    // MPEG-1 Layer III, 128 kbps, 44.1 kHz: 417 bytes per unpadded frame.
    foreach (var (name, length) in new[]
    {
        ("one.mp3", 417), ("two.mp3", 834),
        ("truncated-third.mp3", 838), ("three.mp3", 1251)
    })
    {
        var bytes = new byte[length];
        for (var offset = 0; offset + 4 <= length; offset += 417)
            new byte[] { 0xff, 0xfb, 0x90, 0x00 }.CopyTo(bytes, offset);
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
    }
    var tracks = ZipAlbumReader.OpenFolder(folder).Tracks;
    foreach (var track in tracks)
    {
        var expected = track.FileName == "three.mp3";
        Require(track.IsSupported == expected && track.IsMp3Valid == expected && track.IsCbr == expected,
            $"Complete-frame validation: {track.FileName}");
    }
}

static void PrintReaderFormat(ZipTrack track, string name)
{
    var readerType = typeof(ZipAlbumReader).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
    using var owner = (IDisposable)readerType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [track])!;
    var reader = (WaveStream)readerType.GetProperty("Reader")!.GetValue(owner)!;
    Console.WriteLine($"{name}: {reader.WaveFormat}; encoding={reader.WaveFormat.Encoding}; bits={reader.WaveFormat.BitsPerSample}; block={reader.WaveFormat.BlockAlign}");
}

static void VerifySeek(ZipTrack track, string name, IReadOnlyList<double> ratios)
{
    var readerType = typeof(ZipAlbumReader).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
    using var owner = (IDisposable)readerType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [track])!;
    var reader = (WaveStream)readerType.GetProperty("Reader")!.GetValue(owner)!;
    var samples = reader.ToSampleProvider();
    var buffer = new float[4096];
    foreach (var ratio in ratios)
    {
        var requested = TimeSpan.FromTicks((long)(reader.TotalTime.Ticks * ratio));
        reader.CurrentTime = requested;
        Require(Math.Abs((reader.CurrentTime - requested).TotalMilliseconds) <= 100, $"{name} {ratio:P0} position");
        var beforeRead = reader.CurrentTime;
        var read = samples.Read(buffer, 0, buffer.Length);
        Require(read > 0 && buffer.Take(read).All(float.IsFinite), $"{name} {ratio:P0} decode");
        Require(reader.CurrentTime > beforeRead, $"{name} {ratio:P0} advances");
    }
}

static void VerifyFaithfulProvider(ZipTrack track, string name)
{
    var readerType = typeof(ZipAlbumReader).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
    using var owner = (IDisposable)readerType.GetMethod("Open", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [track])!;
    var reader = (WaveStream)readerType.GetProperty("Reader")!.GetValue(owner)!;
    var provider = (IWaveProvider)typeof(MainWindow).GetMethod("CreateFaithfulWaveProvider", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [reader])!;
    Require(provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && provider.WaveFormat.BitsPerSample == 32,
        name + " format");
    var bytes = new byte[4096];
    var read = provider.Read(bytes, 0, bytes.Length);
    var samples = new float[read / sizeof(float)];
    Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
    Require(samples.Length > 0 && samples.All(float.IsFinite)
        && samples.All(sample => Math.Abs(sample) <= 1.001f), name + " samples");
}

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Failed: {name}");
}

static HashSet<string> ExistingTemporaryEntries(string folder) => Directory.Exists(folder)
    ? Directory.EnumerateFiles(folder).ToHashSet(StringComparer.OrdinalIgnoreCase)
    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
