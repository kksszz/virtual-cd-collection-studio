using System.IO;
using System.IO.Compression;
using NAudio.Wave;
using NAudio.Lame;
using ZipMp3Player;

var folder = Path.Combine(Path.GetTempPath(), "ZipMp3Player-GaplessTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var a = MakeWav("AlbumA", 44100, 2, 4417, i => i < 40 ? (short)0 : (short)(1000 + i % 127));
    var b = MakeWav("AlbumB", 44100, 2, 6671, i => (short)(-1000 - i % 191));
    var queue = new[] { new PlaybackQueueEntry(a, 0), new PlaybackQueueEntry(b, 0) };
    foreach (var faithful in new[] { false, true })
    {
        using var stream = new GaplessPlaybackStream(queue, 0, faithful, false, 0);
        await stream.PrefetchCompletion;
        var changed = 0;
        stream.TrackChanged += () => changed++;
        var expected = Decode(queue[0].Track, faithful).Concat(Decode(queue[1].Track, faithful)).ToArray();
        var actual = Drain(stream, 8192);
        Require(actual.SequenceEqual(expected), $"Exact cross-album PCM concatenation (faithful={faithful}); intentional silence retained");
        Require(changed == 1 && stream.Snapshot.Entry.Album == b && stream.Snapshot.Revision == 1, "Boundary event/identity");
        Require(stream.FallbackEntry is null, "End of queue stops without fallback");
    }
    using (var seek = new GaplessPlaybackStream(queue, 0, false, false, 0))
    {
        await seek.PrefetchCompletion;
        seek.Position = 1000 * seek.WaveFormat.BlockAlign;
        var expected = Decode(queue[0].Track, false).Skip(1000 * 8).Concat(Decode(queue[1].Track, false)).ToArray();
        Require(Drain(seek, 2048).SequenceEqual(expected), "Seek discards old prefix and preserves boundary");
    }
    var mono = MakeWav("Mono48k", 48000, 1, 48000, i => (short)(10000 * Math.Sin(i * 0.06)));
    var different = new[] { queue[0], new PlaybackQueueEntry(mono, 0) };
    using (var normal = new GaplessPlaybackStream(different, 0, false, false, 0))
    {
        await normal.PrefetchCompletion;
        var data = Drain(normal, 32768);
        Require(data.Length == (4417 + 44100) * 8, $"Resampling length: {data.Length}");
        Require(normal.Snapshot.Entry.Album == mono && normal.FallbackEntry is null, "Normal rate/channel conversion stays on stream");
        var values = new float[data.Length / 4];
        Buffer.BlockCopy(data, 0, values, 0, data.Length);
        Require(values.All(float.IsFinite), "Resampled values finite");
        Require(Enumerable.Range(4417, 44100).All(i => values[2 * i] == values[2 * i + 1]), "Mono mapped to both stereo channels");
    }
    using (var faithful = new GaplessPlaybackStream(different, 0, true, false, 0))
    {
        await faithful.PrefetchCompletion;
        Require(Drain(faithful, 8192).SequenceEqual(Decode(queue[0].Track, true)), "Faithful format mismatch preserves first track exactly");
        Require(faithful.FallbackEntry?.Album == mono, "Faithful format mismatch requests ordinary next-track playback");
    }
    var highRate = MakeWav("Stereo48k", 48000, 2, 4800, _ => 4000);
    var lowRate = MakeWav("Mono44k", 44100, 1, 44100, _ => 4000);
    using (var upsample = new GaplessPlaybackStream([new(highRate, 0), new(lowRate, 0)], 0, false, false, 0))
    {
        await upsample.PrefetchCompletion;
        Require(Drain(upsample, 4096).Length == (4800 + 48000) * 8 && upsample.FallbackEntry is null, "Upsampling 44.1k to 48k duration");
    }
    var surround = MakeWav("Surround", 48000, 6, 1000, _ => 1000);
    using (var multi = new GaplessPlaybackStream([new(surround, 0), new(surround, 0), new(highRate, 0)], 0, false, false, 0))
    {
        await multi.PrefetchCompletion;
        var data = new byte[1001 * 6 * 4];
        Require(multi.Read(data, 0, data.Length) == data.Length, "Matching multichannel boundary remains supported");
        await multi.PrefetchCompletion;
        Require(Drain(multi, 6 * 4 * 128).Length == 999 * 6 * 4 && multi.FallbackEntry?.Album == highRate,
            "Multichannel layout mismatch falls back without silently downmixing");
    }
    using (var repeat = new GaplessPlaybackStream(queue, 0, true, false, 2))
    {
        await repeat.PrefetchCompletion;
        var first = Decode(queue[0].Track, true);
        var data = new byte[first.Length + 8];
        Require(repeat.Read(data, 0, data.Length) == data.Length && data.SequenceEqual(first.Concat(first.Take(8))), "Repeat one exact boundary");
        Require(repeat.FollowingEntry(false)?.Album == b, "Manual next skips repeat-one");
        repeat.ConfigureNavigation(false, 1);
        await repeat.PrefetchCompletion;
        Require(repeat.RelativeEntry(-1).Album == b, "Repeat all previous wraps");
        repeat.ConfigureNavigation(true, 0);
        await repeat.PrefetchCompletion;
        Require(repeat.FollowingEntry(true)?.Album == b, "Shuffle prefetch/navigation agree");
    }
    using (var repeatAll = new GaplessPlaybackStream(queue, 1, true, false, 1))
    {
        await repeatAll.PrefetchCompletion;
        var last = Decode(queue[1].Track, true);
        var data = new byte[last.Length + 8];
        Require(repeatAll.Read(data, 0, data.Length) == data.Length && data.SequenceEqual(last.Concat(Decode(queue[0].Track, true).Take(8))), "Repeat all wraps sample-exactly");
    }
    var missing = new ZipAlbum { Path = Path.Combine(folder, "missing"), Tracks = [new ZipTrack { SourcePath = Path.Combine(folder, "missing.wav"), AudioFormat = "WAV" }] };
    using (var failure = new GaplessPlaybackStream([queue[0], new(missing, 0)], 0, true, false, 0))
    {
        try { await failure.PrefetchCompletion; } catch (FileNotFoundException) { }
        Require(Drain(failure, 4096).SequenceEqual(Decode(queue[0].Track, true)) && failure.FallbackEntry?.Album == missing, "Failed prefetch retains first track and requests fallback");
    }
    for (var i = 0; i < 20; i++)
    {
        using var cancel = new GaplessPlaybackStream(queue, 0, false, false, 0);
        cancel.ConfigureNavigation(true, 2);
        cancel.ConfigureNavigation(false, 1);
        await cancel.PrefetchCompletion;
    }
    Console.WriteLine("PASS: cancellation/navigation/disposal stress");

    // Generate actual LAME CBR/VBR files to verify metadata AND Windows ACM decoding alignment.
    foreach (var (rate, channels) in new[] { (44100, 2), (48000, 2), (32000, 1), (22050, 1) })
    {
    var peaks = new[] { rate / 4, rate * 3 / 4 };
    var impulse = MakeWav($"Impulse{rate}", rate, channels, rate, i => (short)(peaks.Contains(i) ? 28000 : 0));
    foreach (var vbr in new[] { false, true })
    {
        var mp3Path = Path.Combine(folder, $"{rate}-{vbr}.mp3");
        using (var input = new WaveFileReader(impulse.Tracks[0].SourcePath))
        using (var encoder = new LameMP3FileWriter(mp3Path, input.WaveFormat, vbr
            ? new LameConfig { Preset = LAMEPreset.V2, WriteVBRTag = true }
            : new LameConfig { BitRate = rate < 32000 ? 96 : 192, WriteVBRTag = true })) input.CopyTo(encoder);
        var track = new ZipTrack { SourcePath = mp3Path, AudioFormat = "MP3" };
        using (var raw = new Mp3FileReader(mp3Path))
        {
            Console.WriteLine($"MP3 VBR={vbr}: Xing={raw.XingHeader is not null}, decodedFrames={raw.Length / raw.WaveFormat.BlockAlign}");
            Require(raw.XingHeader is not null && Mp3GaplessTrim.TryRead(raw.XingHeader.Mp3Frame.RawData, out _, out _), $"Valid LAME tag (VBR={vbr})");
            var corrupt = raw.XingHeader!.Mp3Frame.RawData.ToArray();
            corrupt[50] ^= 1;
            Require(!Mp3GaplessTrim.TryRead(corrupt, out _, out _), "Damaged LAME CRC cannot trim audio");
        }
        var decoded = Decode(track, true);
        if (rate < 32000)
        {
            using var raw = TrackAudioReader.Open(track);
            Require(decoded.SequenceEqual(Drain(raw.Reader, 4096)), "MPEG-2 ACM priming is unverified; retain all decoded audio safely");
            var album = new ZipAlbum { Path = mp3Path, Tracks = [track, track] };
            using var stream = new GaplessPlaybackStream([new(album, 0), new(album, 1)], 0, true, false, 0);
            await stream.PrefetchCompletion;
            Require(Drain(stream, 8192).SequenceEqual(decoded.Concat(decoded)), "MPEG-2 untrimmed boundary retains decoder output");
            continue;
        }
        Require(decoded.Length == rate * channels * 2, $"Trimmed MP3 sample count (VBR={vbr}, rate={rate}, channels={channels}): {decoded.Length / (channels * 2)}");
        var pcm = new short[decoded.Length / 2];
        Buffer.BlockCopy(decoded, 0, pcm, 0, decoded.Length);
        foreach (var expectedPeak in peaks)
        {
            var peak = Enumerable.Range(expectedPeak - 1500, 3000).MaxBy(i => Math.Abs((int)pcm[i * channels]));
            Require(peak == expectedPeak, $"Windows ACM trimmed impulse alignment: {peak} == {expectedPeak}");
        }
        using (var seeking = TrackAudioReader.OpenGapless(track))
        {
            var start = rate / 2;
            seeking.Reader.Position = start * channels * 2;
        var tail = Drain(seeking.Reader, 4096);
            Require(tail.Length == (rate - start) * channels * 2, "Trimmed MP3 seek duration");
            var tailPcm = new short[tail.Length / 2];
            Buffer.BlockCopy(tail, 0, tailPcm, 0, tail.Length);
            var peak = Enumerable.Range(0, tail.Length / (channels * 2)).MaxBy(i => Math.Abs((int)tailPcm[i * channels]));
            Require(peak + start == peaks[1], "Trimmed MP3 seek alignment");
        }
        foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
        {
            var zipPath = Path.Combine(folder, $"{rate}-{vbr}-{compression}.zip.mp3");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(mp3Path, "01.mp3", compression);
                archive.CreateEntryFromFile(mp3Path, "02.mp3", compression);
            }
            var album = ZipAlbumReader.Open(zipPath);
            using var zipped = new GaplessPlaybackStream([new(album, 0), new(album, 1)], 0, true, false, 0);
            await zipped.PrefetchCompletion;
            Require(Drain(zipped, 16384).SequenceEqual(decoded.Concat(decoded)), $"ZIP {compression} (VBR={vbr}) exact concatenation");
        }
    }
    var noTagPath = Path.Combine(folder, $"no-lame{rate}.mp3");
    using (var input = new WaveFileReader(impulse.Tracks[0].SourcePath)) MediaFoundationEncoder.EncodeToMp3(input, noTagPath, rate < 32000 ? 96000 : 192000);
    using (var raw = new Mp3FileReader(noTagPath))
    using (var owner = TrackAudioReader.OpenGapless(new ZipTrack { SourcePath = noTagPath, AudioFormat = "MP3" }))
        Require(owner.Reader.Length == raw.Length, "MP3 without LAME metadata retains full duration");
    }
    GaplessUiTests.Run(folder);
    Console.WriteLine("All gapless tests passed.");
}
finally
{
    // All paths are generated below this isolated test directory, never user media.
    Directory.Delete(folder, true);
}

ZipAlbum MakeWav(string name, int rate, int channels, int frames, Func<int, short> value)
{
    var path = Path.Combine(folder, name + ".wav");
    using (var output = new WaveFileWriter(path, new WaveFormat(rate, 16, channels)))
        for (var i = 0; i < frames; i++) for (var ch = 0; ch < channels; ch++) output.WriteSample(value(i) / 32768f);
    return new ZipAlbum { Path = name, Tracks = [new ZipTrack { SourcePath = path, AudioFormat = "WAV", Title = name, SampleRate = rate, Duration = TimeSpan.FromSeconds((double)frames / rate) }] };
}
static byte[] Decode(ZipTrack track, bool faithful)
{
    using var owner = TrackAudioReader.OpenGapless(track);
    return Drain(faithful ? owner.Reader : owner.Reader.ToSampleProvider().ToWaveProvider(), 4096);
}
static byte[] Drain(IWaveProvider provider, int size)
{
    using var result = new MemoryStream();
    var buffer = new byte[size];
    int read;
    while ((read = provider.Read(buffer, 0, buffer.Length)) > 0) result.Write(buffer, 0, read);
    return result.ToArray();
}
static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS: " + name);
}
