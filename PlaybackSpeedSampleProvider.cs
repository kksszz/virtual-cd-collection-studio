using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZipMp3Player;

public static class PlaybackSpeedSampleProvider
{
    public static ISampleProvider Create(ISampleProvider source, double speed, bool preservePitch)
    {
        speed = Math.Clamp(speed, 0.5, 2.0);
        if (Math.Abs(speed - 1.0) < 0.001) return source;

        var originalRate = source.WaveFormat.SampleRate;
        var rateTagged = new RateTaggedProvider(source, (int)Math.Round(originalRate * speed));
        ISampleProvider result = new WdlResamplingSampleProvider(rateTagged, originalRate);
        if (preservePitch)
        {
            var pitch = new SmbPitchShiftingSampleProvider(result) { PitchFactor = (float)(1.0 / speed) };
            result = pitch;
        }
        return result;
    }

    private sealed class RateTaggedProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public WaveFormat WaveFormat { get; }
        public RateTaggedProvider(ISampleProvider source, int reportedSampleRate)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reportedSampleRate, source.WaveFormat.Channels);
        }
        public int Read(float[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    }
}
