using NAudio.Dsp;
using NAudio.Wave;

namespace ZipMp3Player;

public sealed class SpectrumCaptureSampleProvider : ISampleProvider
{
    public const int BandCount = 18;
    private const int FftLength = 2048;
    private const int FftPower = 11;
    private readonly ISampleProvider _source;
    private readonly Complex[] _fftBuffer = new Complex[FftLength];
    private readonly float[] _bands = new float[BandCount];
    private readonly object _gate = new();
    private int _fftPosition;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SpectrumCaptureSampleProvider(ISampleProvider source) => _source = source;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        for (var index = 0; index + channels <= read; index += channels)
        {
            var mono = 0f;
            for (var channel = 0; channel < channels; channel++) mono += buffer[offset + index + channel];
            mono /= channels;
            _fftBuffer[_fftPosition].X = mono * (float)FastFourierTransform.HammingWindow(_fftPosition, FftLength);
            _fftBuffer[_fftPosition].Y = 0;
            if (++_fftPosition < FftLength) continue;
            _fftPosition = 0;
            CalculateBands();
        }
        return read;
    }

    public float[] GetSnapshot()
    {
        lock (_gate) return _bands.ToArray();
    }

    private void CalculateBands()
    {
        FastFourierTransform.FFT(true, FftPower, _fftBuffer);
        var nyquist = WaveFormat.SampleRate / 2.0;
        const double minimumFrequency = 35;
        var maximumFrequency = Math.Min(16_000, nyquist * 0.96);
        var next = new float[BandCount];
        for (var band = 0; band < BandCount; band++)
        {
            var lowFrequency = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, band / (double)BandCount);
            var highFrequency = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, (band + 1) / (double)BandCount);
            var lowBin = Math.Clamp((int)Math.Floor(lowFrequency * FftLength / WaveFormat.SampleRate), 1, FftLength / 2 - 1);
            var highBin = Math.Clamp((int)Math.Ceiling(highFrequency * FftLength / WaveFormat.SampleRate), lowBin + 1, FftLength / 2);
            var peak = 0f;
            for (var bin = lowBin; bin < highBin; bin++)
            {
                var value = _fftBuffer[bin];
                peak = Math.Max(peak, MathF.Sqrt(value.X * value.X + value.Y * value.Y) * 2f);
            }
            var decibels = 20f * MathF.Log10(peak + 1e-9f);
            next[band] = Math.Clamp((decibels + 72f) / 66f, 0f, 1f);
        }

        lock (_gate)
        {
            for (var band = 0; band < BandCount; band++)
            {
                var blend = next[band] > _bands[band] ? 0.68f : 0.16f;
                _bands[band] += (next[band] - _bands[band]) * blend;
            }
        }
    }
}
