using NAudio.Wave;
using ZipMp3Player;

if (args.Length == 2 && args[0] == "--prepare")
{
    var service = new AiGuitarPreviewService(args[1]);
    await service.PrepareAsync(Console.WriteLine, CancellationToken.None);
    Console.WriteLine($"READY={service.IsRuntimePresent}");
    return;
}

if (args.Length == 2 && args[0] == "--pipeline-test")
{
    using var preview = new AudioFileReader(args[1]);
    var remaster = new RemasterSampleProvider(preview.ToSampleProvider(), RemasterMode.AudioHdr);
    var equalizer = new EqualizerSampleProvider(remaster, [3, 5, 4, 1, -2, 0, 3, 5, 4, 2])
    {
        Enabled = true,
        ClampOutput = false
    };
    var bassBoost = new BassBoostSampleProvider(equalizer, true, 60);
    var limiter = new SoftLimiterSampleProvider(bassBoost);
    var pipelineBuffer = new float[8192];
    long pipelineSamples = 0;
    var peak = 0f;
    int read;
    while ((read = limiter.Read(pipelineBuffer, 0, pipelineBuffer.Length)) > 0)
    {
        pipelineSamples += read;
        for (var index = 0; index < read; index++) peak = Math.Max(peak, Math.Abs(pipelineBuffer[index]));
    }
    Console.WriteLine($"SOURCE_DURATION={preview.TotalTime.TotalSeconds:0.000}");
    Console.WriteLine($"PROCESSED_DURATION={pipelineSamples / (double)(equalizer.WaveFormat.SampleRate * equalizer.WaveFormat.Channels):0.000}");
    Console.WriteLine($"PEAK={peak:0.000000}");
    return;
}

if (args.Length == 1 && args[0] == "--bass-test")
{
    const int sampleRate = 44100;
    const int channels = 2;
    var input = new float[sampleRate * channels * 2];
    for (var frame = 0; frame < sampleRate * 2; frame++)
    {
        var amplitude = frame < sampleRate ? 0.001f : 0.1f;
        var sample = amplitude * MathF.Sin(2f * MathF.PI * 65f * frame / sampleRate);
        input[frame * 2] = sample;
        input[frame * 2 + 1] = sample;
    }
    var bass = new BassBoostSampleProvider(new TestBufferSampleProvider(input, sampleRate, channels), true, 100);
    var limiter = new SoftLimiterSampleProvider(bass);
    var output = new float[input.Length];
    var outputCount = 0;
    while (outputCount < output.Length)
    {
        var count = limiter.Read(output, outputCount, output.Length - outputCount);
        if (count == 0) break;
        outputCount += count;
    }
    static double Rms(float[] values, int start, int count)
        => Math.Sqrt(values.Skip(start).Take(count).Select(value => value * value).Average());
    var segmentSamples = sampleRate * channels;
    Console.WriteLine($"NOISE_GAIN={Rms(output, sampleRate / 4 * channels, segmentSamples / 2) / Rms(input, sampleRate / 4 * channels, segmentSamples / 2):0.000}");
    Console.WriteLine($"MUSIC_GAIN={Rms(output, segmentSamples + sampleRate / 4 * channels, segmentSamples / 2) / Rms(input, segmentSamples + sampleRate / 4 * channels, segmentSamples / 2):0.000}");
    Console.WriteLine($"PEAK={output.Max(value => Math.Abs(value)):0.000000}");
    return;
}

if (args.Length == 1 && args[0] == "--spectrum-test")
{
    const int sampleRate = 44100;
    const int channels = 2;
    var input = new float[sampleRate * channels * 2];
    for (var frame = 0; frame < sampleRate * 2; frame++)
    {
        var sample = 0.32f * MathF.Sin(2f * MathF.PI * 65f * frame / sampleRate)
            + 0.14f * MathF.Sin(2f * MathF.PI * 4000f * frame / sampleRate);
        input[frame * 2] = sample;
        input[frame * 2 + 1] = sample;
    }
    var spectrum = new SpectrumCaptureSampleProvider(new TestBufferSampleProvider(input, sampleRate, channels));
    var output = new float[input.Length];
    var read = spectrum.Read(output, 0, output.Length);
    var bands = spectrum.GetSnapshot();
    Console.WriteLine($"SAMPLES={read}");
    Console.WriteLine($"BANDS={bands.Length}");
    Console.WriteLine($"STRONGEST_BAND={Array.IndexOf(bands, bands.Max())}");
    Console.WriteLine($"PEAK_LEVEL={bands.Max():0.000}");
    Console.WriteLine($"LEVELS={string.Join(',', bands.Select(value => value.ToString("0.00")))}");
    return;
}

if (args.Length == 2 && args[0] == "--inspect")
{
    var inspected = ZipAlbumReader.Open(args[1]);
    Console.WriteLine($"ALBUM={inspected.Tracks[0].Album}");
    Console.WriteLine($"ARTIST={inspected.Tracks[0].Artist}");
    Console.WriteLine($"TITLE={inspected.Tracks[0].Title}");
    return;
}

if (args.Length == 2 && args[0] == "--inspect-folder")
{
    var inspected = ZipAlbumReader.OpenFolder(args[1]);
    Console.WriteLine($"ALBUM={inspected.Tracks[0].Album}");
    Console.WriteLine($"ARTIST={inspected.Tracks[0].Artist}");
    Console.WriteLine($"TITLE={inspected.Tracks[0].Title}");
    return;
}

if (args.Length == 3 && args[0] == "--service")
{
    var serviceAlbum = ZipAlbumReader.Open(args[2]);
    var supportedTracks = serviceAlbum.Tracks.Where(candidate => candidate.IsSupported).ToList();
    var serviceTrack = supportedTracks.Count > 1 ? supportedTracks[1] : supportedTracks[0];
    Console.WriteLine($"TRACK={serviceTrack.Title}");
    var service = new AiGuitarPreviewService(args[1]);
    await service.PrepareAsync(Console.WriteLine, CancellationToken.None);
    var result = await service.CreatePreviewAsync(serviceTrack, 0, "dramatic_remake", 100,
        (message, percent) => Console.WriteLine($"{percent}% {message}"), CancellationToken.None);
    Console.WriteLine($"RESULT={result}");
    return;
}

if (args.Length == 3 && args[0] == "--karaoke-service")
{
    var karaokeAlbum = ZipAlbumReader.Open(args[2]);
    var karaokeTracks = karaokeAlbum.Tracks.Where(candidate => candidate.IsSupported).ToList();
    var karaokeTrack = karaokeTracks.Count > 1 ? karaokeTracks[1] : karaokeTracks[0];
    Console.WriteLine($"TRACK={karaokeTrack.Title}");
    var service = new AiGuitarPreviewService(args[1]);
    await service.PrepareAsync(Console.WriteLine, CancellationToken.None);
    var result = await service.CreateKaraokePreviewAsync(karaokeTrack, 0, 100,
        (message, percent) => Console.WriteLine($"{percent}% {message}"), CancellationToken.None);
    Console.WriteLine($"RESULT={result}");
    return;
}

if (args.Length != 2) throw new ArgumentException("Usage: AiDevHarness archive.zip.mp3 output.wav");
var album = ZipAlbumReader.Open(args[0]);
var tracks = album.Tracks.Where(candidate => candidate.IsSupported).ToList();
var track = tracks.Count > 1 ? tracks[1] : tracks[0];
using var source = new BoundedFileStream(track.SourcePath, track.DataOffset, track.Size);
using var reader = new Mp3FileReader(source);
var samples = reader.ToSampleProvider();
using var writer = new WaveFileWriter(args[1], WaveFormat.CreateIeeeFloatWaveFormat(samples.WaveFormat.SampleRate, samples.WaveFormat.Channels));
var buffer = new float[8192];
var remaining = samples.WaveFormat.SampleRate * samples.WaveFormat.Channels * AiGuitarPreviewService.PreviewDurationSeconds;
while (remaining > 0)
{
    var read = samples.Read(buffer, 0, Math.Min(buffer.Length, remaining));
    if (read == 0) break;
    writer.WriteSamples(buffer, 0, read);
    remaining -= read;
}
Console.WriteLine($"{track.Title}|{track.Artist}|{writer.Length}");

sealed class TestBufferSampleProvider(float[] samples, int sampleRate, int channels) : ISampleProvider
{
    private int _position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    public int Read(float[] buffer, int offset, int count)
    {
        var available = Math.Min(count, samples.Length - _position);
        Array.Copy(samples, _position, buffer, offset, available);
        _position += available;
        return available;
    }
}
