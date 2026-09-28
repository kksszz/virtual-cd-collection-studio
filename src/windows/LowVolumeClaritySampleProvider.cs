using NAudio.Dsp;
using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>
/// Narrows the dynamic range and applies a restrained loudness contour so that
/// bass, vocals and attacks remain intelligible at low listening levels.
/// </summary>
public sealed class LowVolumeClaritySampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[][] _toneFilters;
    private readonly float[] _envelopes;
    private volatile bool _enabled;
    private float _mix;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public LowVolumeClaritySampleProvider(ISampleProvider source, bool enabled)
    {
        _source = source;
        _enabled = enabled;
        _mix = enabled ? 1f : 0f;
        _envelopes = new float[WaveFormat.Channels];
        _toneFilters = new BiQuadFilter[WaveFormat.Channels][];
        for (var channel = 0; channel < WaveFormat.Channels; channel++)
        {
            _toneFilters[channel] =
            [
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 90, 0.75f, 2.4f),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 360, 0.85f, -1.1f),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 2600, 0.9f, 2.0f),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 8200, 0.75f, 1.2f)
            ];
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        var attack = MathF.Exp(-1f / (0.010f * WaveFormat.SampleRate));
        var release = MathF.Exp(-1f / (0.260f * WaveFormat.SampleRate));
        var mixStep = 1f / Math.Max(1, WaveFormat.SampleRate / 50f); // 約20msで滑らかに切替

        for (var index = 0; index < read; index++)
        {
            var channel = index % channels;
            var original = buffer[offset + index];
            var processed = original;
            foreach (var filter in _toneFilters[channel]) processed = filter.Transform(processed);

            var level = MathF.Abs(processed);
            var coefficient = level > _envelopes[channel] ? attack : release;
            _envelopes[channel] = coefficient * _envelopes[channel] + (1f - coefficient) * level;

            // 小さい環境音やMP3ノイズだけを過度に持ち上げないよう、極小レベルは補正しない。
            var envelope = _envelopes[channel];
            var gain = 1f;
            if (envelope > 0.003f)
            {
                const float threshold = 0.20f;
                const float ratio = 4.0f;
                var compressedLevel = envelope <= threshold
                    ? envelope
                    : threshold + (envelope - threshold) / ratio;
                gain = 1.62f * compressedLevel / Math.Max(envelope, 0.000001f);
            }
            processed *= gain;

            var targetMix = _enabled ? 1f : 0f;
            _mix = targetMix > _mix ? Math.Min(targetMix, _mix + mixStep) : Math.Max(targetMix, _mix - mixStep);
            buffer[offset + index] = original + (processed - original) * _mix;
        }
        return read;
    }
}
