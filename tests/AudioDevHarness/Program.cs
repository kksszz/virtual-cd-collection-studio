using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ZipMp3Player;

const int sampleRate = 44100;
var samples = new float[sampleRate * 2];
for (var frame = 0; frame < sampleRate; frame++)
{
    var amplitude = frame < sampleRate / 2 ? 0.05f : 0.85f;
    var value = amplitude * MathF.Sin(2 * MathF.PI * 1000 * frame / sampleRate);
    samples[frame * 2] = value;
    samples[frame * 2 + 1] = value;
}

var pipeline = new SoftLimiterSampleProvider(
    new LowVolumeClaritySampleProvider(new ArraySampleProvider(samples, sampleRate, 2), enabled: true));
var output = new float[samples.Length];
var read = pipeline.Read(output, 0, output.Length);
Require(read == samples.Length, "sample count");
Require(output.All(float.IsFinite), "finite samples");
Require(output.Max(MathF.Abs) <= 0.981f, "limiter ceiling");

var quietRms = Rms(output, sampleRate / 4, sampleRate / 4, 2);
var loudRms = Rms(output, sampleRate * 3 / 4, sampleRate / 4, 2);
Require(quietRms > 0.045f, "quiet detail gain");
Require(loudRms / quietRms < 10f, "dynamic range reduction");

var boostInput = Enumerable.Repeat(0.35f, 2048).ToArray();
var boostGain = new VolumeSampleProvider(new ArraySampleProvider(boostInput, sampleRate, 1)) { Volume = 2.0f };
var boostLimiter = new SoftLimiterSampleProvider(boostGain);
var boostOutput = new float[boostInput.Length];
boostLimiter.Read(boostOutput, 0, boostOutput.Length);
Require(boostOutput.Average(MathF.Abs) > 0.6f, "200 percent software gain");
Require(boostOutput.Max(MathF.Abs) <= 0.981f, "boost limiter ceiling");
Console.WriteLine($"Low-volume clarity test passed. quiet={quietRms:0.000}, loud={loudRms:0.000}, peak={output.Max(MathF.Abs):0.000}");

foreach (var rate in new[] { 44100, 48000 })
{
    var quiet = NormalizeTone(0.08f, rate, 997);
    var loud = NormalizeTone(0.7f, rate, 4096);
    var quietLevel = Rms(quiet, rate * 18, rate, 2);
    var loudLevel = Rms(loud, rate * 18, rate, 2);
    Require(Math.Abs(20 * Math.Log10(loudLevel / quietLevel)) < 0.2, "normalization level matching");
    var otherChunks = NormalizeTone(0.08f, rate, 4096);
    Require(quiet.SequenceEqual(otherChunks), "read chunk independence");
    for (var i = 0; i < quiet.Length; i += 2)
        Require(quiet[i + 1] == quiet[i] * 0.5f, "linked stereo gain");
    Console.WriteLine($"PASS normalization {rate}Hz: quiet RMS={quietLevel:F4}, loud RMS={loudLevel:F4}");
}
var bypass = new NormalizationSampleProvider(new ArraySampleProvider(samples, sampleRate, 2), false);
var bypassOutput = new float[samples.Length];
bypass.Read(bypassOutput, 0, bypassOutput.Length);
Require(samples.SequenceEqual(bypassOutput), "normalization exact bypass");
var silence = new NormalizationSampleProvider(new ArraySampleProvider(new float[10000], sampleRate, 2), true);
var silenceOutput = new float[10010];
Array.Fill(silenceOutput, 123f);
Require(silence.Read(silenceOutput, 5, 10000) == 10000, "normalization offset read");
Require(silenceOutput.Skip(5).Take(10000).All(x => x == 0), "silence remains silent");
Require(silenceOutput.Take(5).Concat(silenceOutput.Skip(10005)).All(x => x == 123), "offset bounds");
Require(silence.Read(silenceOutput, 0, 10) == 0, "EOF");
Console.WriteLine("PASS normalization bypass, silence, offset and EOF");
var toggled = new NormalizationSampleProvider(new ArraySampleProvider(samples, sampleRate, 2), true);
var toggleOutput = new float[samples.Length];
toggled.Read(toggleOutput, 0, 1000);
toggled.Enabled = false;
toggled.Read(toggleOutput, 1000, samples.Length - 1000);
Require(toggleOutput.Skip(1000).SequenceEqual(samples.Skip(1000)), "live OFF bypass");
var normalizedLimiter = new SoftLimiterSampleProvider(new NormalizationSampleProvider(
    new ArraySampleProvider(samples, sampleRate, 2), true));
normalizedLimiter.Read(toggleOutput, 0, toggleOutput.Length);
Require(toggleOutput.All(float.IsFinite) && toggleOutput.Max(MathF.Abs) <= 0.981f, "normalized limiter ceiling");
Console.WriteLine("PASS normalization live toggle and output limiter");

static float[] NormalizeTone(float amplitude, int rate, int chunk)
{
    var input = new float[rate * 20 * 2];
    for (var f = 0; f < input.Length / 2; f++)
    {
        input[f * 2] = amplitude * MathF.Sin(2 * MathF.PI * 440 * f / rate);
        input[f * 2 + 1] = input[f * 2] * 0.5f;
    }
    var provider = new NormalizationSampleProvider(new ArraySampleProvider(input, rate, 2), true);
    var result = new float[input.Length];
    for (var position = 0; position < result.Length;)
        position += provider.Read(result, position, Math.Min(chunk, result.Length - position));
    return result;
}

static float Rms(float[] values, int startFrame, int frameCount, int channels)
{
    double sum = 0;
    var start = startFrame * channels;
    var count = frameCount * channels;
    for (var i = start; i < start + count; i++) sum += values[i] * values[i];
    return (float)Math.Sqrt(sum / count);
}

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Failed: {name}");
}

sealed class ArraySampleProvider(float[] samples, int sampleRate, int channels) : ISampleProvider
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
