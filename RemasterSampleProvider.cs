using NAudio.Dsp;
using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>
/// Non-destructive, real-time listening enhancement. This deliberately uses
/// conservative DSP rather than claiming to reconstruct information that was
/// removed by MP3 encoding.
/// </summary>
public sealed class RemasterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly object _gate = new();
    private BiQuadFilter[][] _toneFilters;
    private BiQuadFilter[] _exciterHighPass;
    private float[] _envelopes;
    private float[] _slowEnvelopes;
    private RemasterProfile _profile;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public RemasterMode Mode { get; private set; }

    public RemasterSampleProvider(ISampleProvider source, RemasterMode mode)
    {
        _source = source;
        _profile = RemasterProfile.For(mode);
        Mode = mode;
        (_toneFilters, _exciterHighPass, _envelopes, _slowEnvelopes) = BuildState(_profile);
    }

    public void SetMode(RemasterMode mode)
    {
        lock (_gate)
        {
            if (Mode == mode) return;
            Mode = mode;
            _profile = RemasterProfile.For(mode);
            (_toneFilters, _exciterHighPass, _envelopes, _slowEnvelopes) = BuildState(_profile);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0 || Mode == RemasterMode.Off) return read;

        lock (_gate)
        {
            var channels = WaveFormat.Channels;
            var attack = MathF.Exp(-1f / (0.008f * WaveFormat.SampleRate));
            var release = MathF.Exp(-1f / (0.180f * WaveFormat.SampleRate));
            var slow = MathF.Exp(-1f / (0.550f * WaveFormat.SampleRate));

            for (var i = 0; i < read; i++)
            {
                var channel = i % channels;
                var sample = buffer[offset + i];

                foreach (var filter in _toneFilters[channel]) sample = filter.Transform(sample);

                // Add a very small, band-limited harmonic component. This restores
                // perceived air without pretending to recover the original waveform.
                var highBand = _exciterHighPass[channel].Transform(sample);
                var excited = MathF.Tanh(highBand * 3.2f) / 3.2f;
                sample += excited * _profile.ExciterMix;

                var level = MathF.Abs(sample);
                var coefficient = level > _envelopes[channel] ? attack : release;
                _envelopes[channel] = coefficient * _envelopes[channel] + (1f - coefficient) * level;
                _slowEnvelopes[channel] = slow * _slowEnvelopes[channel] + (1f - slow) * level;

                // HDR-style modes reveal low-level detail and emphasize short attacks.
                // The gain is envelope-driven to avoid changing every sample independently.
                if (_profile.DetailBoost > 0 && _slowEnvelopes[channel] < 0.28f)
                    sample *= 1f + _profile.DetailBoost * (1f - _slowEnvelopes[channel] / 0.28f);
                if (_profile.TransientBoost > 0 && _envelopes[channel] > _slowEnvelopes[channel] * 1.18f)
                {
                    var transient = Math.Clamp(_envelopes[channel] / Math.Max(_slowEnvelopes[channel], 0.001f) - 1.18f, 0, 1);
                    sample *= 1f + transient * _profile.TransientBoost;
                }
                if (_envelopes[channel] > _profile.CompressorThreshold)
                {
                    var compressed = _profile.CompressorThreshold
                        + (_envelopes[channel] - _profile.CompressorThreshold) / _profile.CompressorRatio;
                    sample *= compressed / Math.Max(_envelopes[channel], 0.000001f);
                }

                buffer[offset + i] = Math.Clamp(sample * _profile.OutputGain, -1f, 1f);
            }

            if (channels == 2 && _profile.StereoWidth > 1f)
            {
                for (var i = 0; i + 1 < read; i += 2)
                {
                    var left = buffer[offset + i];
                    var right = buffer[offset + i + 1];
                    var mid = (left + right) * 0.5f;
                    var side = (left - right) * 0.5f * _profile.StereoWidth;
                    buffer[offset + i] = Math.Clamp(mid + side, -1f, 1f);
                    buffer[offset + i + 1] = Math.Clamp(mid - side, -1f, 1f);
                }
            }
        }
        return read;
    }

    private (BiQuadFilter[][] Tone, BiQuadFilter[] Exciter, float[] Envelopes, float[] SlowEnvelopes) BuildState(RemasterProfile profile)
    {
        var tone = new BiQuadFilter[WaveFormat.Channels][];
        var exciter = new BiQuadFilter[WaveFormat.Channels];
        for (var channel = 0; channel < WaveFormat.Channels; channel++)
        {
            tone[channel] =
            [
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 90, 0.75f, profile.LowGainDb),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 360, 0.85f, profile.MudGainDb),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 2600, 0.9f, profile.PresenceGainDb),
                BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, 9000, 0.7f, profile.AirGainDb)
            ];
            exciter[channel] = BiQuadFilter.HighPassFilter(WaveFormat.SampleRate, 4800, 0.707f);
        }
        return (tone, exciter, new float[WaveFormat.Channels], new float[WaveFormat.Channels]);
    }

    private sealed record RemasterProfile(
        float LowGainDb, float MudGainDb, float PresenceGainDb, float AirGainDb,
        float ExciterMix, float CompressorThreshold, float CompressorRatio, float OutputGain,
        float DetailBoost, float TransientBoost, float StereoWidth)
    {
        public static RemasterProfile For(RemasterMode mode) => mode switch
        {
            RemasterMode.Light => new(0.5f, -0.4f, 0.3f, 0.7f, 0.012f, 0.82f, 1.3f, 0.98f, 0, 0, 1),
            RemasterMode.Standard => new(0.9f, -0.8f, 0.6f, 1.3f, 0.022f, 0.74f, 1.6f, 0.97f, 0, 0, 1),
            RemasterMode.Strong => new(1.4f, -1.3f, 1.0f, 2.1f, 0.036f, 0.66f, 2.0f, 0.95f, 0, 0, 1),
            RemasterMode.Dramatic => new(3.2f, -2.4f, 2.7f, 4.6f, 0.095f, 0.56f, 2.8f, 0.88f, 0.06f, 0.08f, 1.08f),
            RemasterMode.AudioHdr => new(2.0f, -1.7f, 2.2f, 3.8f, 0.070f, 0.62f, 2.2f, 0.86f, 0.22f, 0.20f, 1.24f),
            _ => new(0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 1)
        };
    }
}

public enum RemasterMode { Off, Light, Standard, Strong, Dramatic, AudioHdr }
