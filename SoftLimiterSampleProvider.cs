using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>Transparent below the knee, then smoothly limits combined AI/remaster/EQ peaks.</summary>
public sealed class SoftLimiterSampleProvider : ISampleProvider
{
    private const float Knee = 0.86f;
    private const float Ceiling = 0.98f;
    private readonly ISampleProvider _source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SoftLimiterSampleProvider(ISampleProvider source) => _source = source;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        for (var index = offset; index < offset + read; index++)
        {
            var sample = buffer[index];
            var magnitude = MathF.Abs(sample);
            if (magnitude <= Knee) continue;
            var normalized = (magnitude - Knee) / (Ceiling - Knee);
            var limited = Knee + (Ceiling - Knee) * MathF.Tanh(normalized);
            buffer[index] = MathF.CopySign(limited, sample);
        }
        return read;
    }
}
