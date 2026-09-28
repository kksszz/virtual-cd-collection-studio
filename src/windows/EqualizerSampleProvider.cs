using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZipMp3Player;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    public static readonly float[] Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private readonly ISampleProvider _source;
    private readonly float[] _gains;
    private BiQuadFilter[][] _filters;
    private readonly object _gate = new();

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool Enabled { get; set; } = true;
    public bool ClampOutput { get; set; } = true;

    public EqualizerSampleProvider(ISampleProvider source, IReadOnlyList<double> gains)
    {
        _source = source;
        _gains = gains.Select(value => (float)value).ToArray();
        _filters = BuildFilters();
    }

    public void SetGain(int band, double gain)
    {
        if (band < 0 || band >= _gains.Length) return;
        lock (_gate)
        {
            _gains[band] = (float)Math.Clamp(gain, -12, 12);
            _filters = BuildFilters();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (!Enabled) return read;
        lock (_gate)
        {
            var channels = WaveFormat.Channels;
            for (var i = 0; i < read; i++)
            {
                var channel = i % channels;
                var sample = buffer[offset + i];
                for (var band = 0; band < Frequencies.Length; band++)
                    sample = _filters[channel][band].Transform(sample);
                buffer[offset + i] = ClampOutput ? Math.Clamp(sample, -1f, 1f) : sample;
            }
        }
        return read;
    }

    private BiQuadFilter[][] BuildFilters()
    {
        var result = new BiQuadFilter[WaveFormat.Channels][];
        for (var channel = 0; channel < result.Length; channel++)
        {
            result[channel] = new BiQuadFilter[Frequencies.Length];
            for (var band = 0; band < Frequencies.Length; band++)
                result[channel][band] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, Frequencies[band], 0.9f, _gains[band]);
        }
        return result;
    }
}
