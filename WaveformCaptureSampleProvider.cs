using NAudio.Wave;

namespace ZipMp3Player;

public sealed class WaveformCaptureSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _ring = new float[160];
    private readonly object _gate = new();
    private readonly int _framesPerPoint;
    private int _writeIndex;
    private int _count;
    private int _pendingFrames;
    private float _pendingPeak;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public WaveformCaptureSampleProvider(ISampleProvider source)
    {
        _source = source;
        // Around 200 points per second keeps the display closely coupled to attacks
        // while the shorter ring shows a quicker-moving recent window.
        _framesPerPoint = Math.Max(24, source.WaveFormat.SampleRate / 200);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;
        for (var i = 0; i + channels <= read; i += channels)
        {
            var peak = 0f;
            for (var channel = 0; channel < channels; channel++)
                peak = Math.Max(peak, Math.Abs(buffer[offset + i + channel]));
            _pendingPeak = Math.Max(_pendingPeak, peak);
            if (++_pendingFrames < _framesPerPoint) continue;
            lock (_gate)
            {
                _ring[_writeIndex] = _pendingPeak;
                _writeIndex = (_writeIndex + 1) % _ring.Length;
                _count = Math.Min(_count + 1, _ring.Length);
            }
            _pendingFrames = 0;
            _pendingPeak = 0;
        }
        return read;
    }

    public float[] GetSnapshot()
    {
        lock (_gate)
        {
            var result = new float[_ring.Length];
            var padding = _ring.Length - _count;
            var start = (_writeIndex - _count + _ring.Length) % _ring.Length;
            for (var i = 0; i < _count; i++) result[padding + i] = _ring[(start + i) % _ring.Length];
            return result;
        }
    }
}
