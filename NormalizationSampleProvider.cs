using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>Streaming RMS level matching, not integrated LUFS/ReplayGain analysis.
/// Linked channels preserve stereo balance; gated windows avoid boosting silence.</summary>
public sealed class NormalizationSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _windowSamples;
    private readonly double _reduceStep, _increaseStep;
    private double _energy, _smoothedEnergy, _gainDb, _targetDb;
    private int _samples, _channel;
    private double _gain = 1;
    private bool _hasLevel;
    private volatile bool _enabled;
    public bool Enabled { get => _enabled; set => _enabled = value; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public NormalizationSampleProvider(ISampleProvider source, bool enabled)
    {
        _source = source;
        _enabled = enabled;
        var samplesPerSecond = WaveFormat.SampleRate * WaveFormat.Channels;
        _windowSamples = Math.Max(WaveFormat.Channels, samplesPerSecond / 10);
        _reduceStep = 12.0 / samplesPerSecond;
        _increaseStep = 1.5 / samplesPerSecond;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var enabled = _enabled;
        if (!enabled)
        {
            _energy = _smoothedEnergy = _gainDb = _targetDb = 0;
            _samples = 0;
            _channel = 0;
            _gain = 1;
            _hasLevel = false;
            return read; // Bit-exact bypass.
        }
        for (var i = offset; i < offset + read; i++)
        {
            var sample = buffer[i];
            _energy += (double)sample * sample;
            if (++_samples == _windowSamples)
            {
                var energy = _energy / _samples;
                if (energy > 0.00001) // -50 dBFS gate: retain gain during pauses.
                {
                    _smoothedEnergy = _hasLevel ? _smoothedEnergy * 0.95 + energy * 0.05 : energy;
                    _hasLevel = true;
                    _targetDb = Math.Clamp(-18.0 - 10.0 * Math.Log10(_smoothedEnergy), -18.0, 9.0);
                }
                _samples = 0;
                _energy = 0;
            }
            // One common gain per frame, independent of Read buffer boundaries.
            if (_channel == 0)
            {
                _gainDb += Math.Clamp(_targetDb - _gainDb,
                    -_reduceStep * WaveFormat.Channels, _increaseStep * WaveFormat.Channels);
                _gain = Math.Pow(10.0, _gainDb / 20.0);
            }
            buffer[i] = (float)(sample * _gain);
            _channel = (_channel + 1) % WaveFormat.Channels;
        }
        return read;
    }
}
