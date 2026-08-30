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
