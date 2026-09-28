using NAudio.Dsp;
using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>Noise-aware bass enhancement with subsonic filtering and adaptive boost gating.</summary>
public sealed class BassBoostSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[] _subsonicFilters;
    private readonly BiQuadFilter[] _bassBandFilters;
    private readonly float[] _envelopes;
    private volatile bool _enabled;
    private volatile float _amount;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public BassBoostSampleProvider(ISampleProvider source, bool enabled, double amount)
    {
        _source = source;
        _enabled = enabled;
        _amount = (float)Math.Clamp(amount, 0, 100);
        _subsonicFilters = new BiQuadFilter[WaveFormat.Channels];
        _bassBandFilters = new BiQuadFilter[WaveFormat.Channels];
        _envelopes = new float[WaveFormat.Channels];
        for (var channel = 0; channel < WaveFormat.Channels; channel++)
        {
            _subsonicFilters[channel] = BiQuadFilter.HighPassFilter(WaveFormat.SampleRate, 29f, 0.707f);
            _bassBandFilters[channel] = BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, 145f, 0.78f);
        }
    }

    public void SetAmount(double amount) => _amount = (float)Math.Clamp(amount, 0, 100);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0 || !_enabled || _amount <= 0) return read;

        var channels = WaveFormat.Channels;
        var attack = MathF.Exp(-1f / (0.008f * WaveFormat.SampleRate));
        var release = MathF.Exp(-1f / (0.180f * WaveFormat.SampleRate));
        var boostDb = 10f * _amount / 100f;
        var addedGain = MathF.Pow(10f, boostDb / 20f) - 1f;

        for (var sampleIndex = 0; sampleIndex < read; sampleIndex++)
        {
            var channel = sampleIndex % channels;
            var clean = _subsonicFilters[channel].Transform(buffer[offset + sampleIndex]);
            var bassBand = _bassBandFilters[channel].Transform(clean);
            var level = MathF.Abs(bassBand);
            var coefficient = level > _envelopes[channel] ? attack : release;
            _envelopes[channel] = coefficient * _envelopes[channel] + (1f - coefficient) * level;
            var gate = Math.Clamp((_envelopes[channel] - 0.0015f) / 0.0045f, 0f, 1f);
            gate = gate * gate * (3f - 2f * gate);
            buffer[offset + sampleIndex] = clean + bassBand * addedGain * gate;
        }
        return read;
    }
}
