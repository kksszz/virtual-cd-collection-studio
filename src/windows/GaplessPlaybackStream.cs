using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZipMp3Player;

internal sealed record PlaybackQueueEntry(ZipAlbum Album, int TrackIndex, int FavoriteIndex = -1)
{
    public ZipTrack Track => Album.Tracks[TrackIndex];
}

/// <summary>
/// One output stream for an immutable playback queue. The next decoder and its
/// first PCM block are prepared off the audio thread. Read never waits for I/O
/// to open the next file and never inserts silence or overlaps tracks.
/// </summary>
internal sealed class GaplessPlaybackStream : WaveStream
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<PlaybackQueueEntry> _entries;
    private readonly bool _faithful;
    private readonly Random _random = new();
    private PreparedTrack _current;
    private Task<PreparedTrack>? _next;
    private CancellationTokenSource? _nextCancellation;
    private int _index;
    private int _nextIndex = -1;
    private int _fallbackIndex = -1;
    private int _revision;
    private bool _shuffle;
    private int _repeat;
    private bool _ended;
    private bool _disposed;

    public event Action? TrackChanged;
    public IReadOnlyList<PlaybackQueueEntry> Entries => _entries;
    public override WaveFormat WaveFormat { get; }
    public override long Length { get { lock (_gate) return _current.Length; } }
    public override long Position
    {
        get { lock (_gate) return _current.Position; }
        set { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _current.Seek(value); _ended = false; _fallbackIndex = -1; } }
    }
    public (PlaybackQueueEntry Entry, int Revision, TimeSpan Position, TimeSpan Duration) Snapshot
    {
        get { lock (_gate) return (_entries[_index], _revision,
            TimeSpan.FromSeconds((double)_current.Position / WaveFormat.AverageBytesPerSecond),
            TimeSpan.FromSeconds((double)_current.Length / WaveFormat.AverageBytesPerSecond)); }
    }
    public PlaybackQueueEntry? FallbackEntry { get { lock (_gate) return _fallbackIndex < 0 ? null : _entries[_fallbackIndex]; } }
    internal Task PrefetchCompletion { get { lock (_gate) return _next ?? Task.CompletedTask; } }

    public PlaybackQueueEntry? FollowingEntry(bool naturalEnd)
    {
        lock (_gate)
        {
            if (naturalEnd || _repeat != 2) return _nextIndex < 0 ? null : _entries[_nextIndex];
            var next = _index + 1;
            if (_shuffle && _entries.Count > 1)
                do next = _random.Next(_entries.Count); while (next == _index);
            return next < _entries.Count ? _entries[next] : null;
        }
    }
    public PlaybackQueueEntry RelativeEntry(int delta)
    {
        lock (_gate)
        {
            var index = _index + delta;
            if (_repeat == 1) index = (index % _entries.Count + _entries.Count) % _entries.Count;
            return _entries[Math.Clamp(index, 0, _entries.Count - 1)];
        }
    }

    public GaplessPlaybackStream(IReadOnlyList<PlaybackQueueEntry> entries, int index, bool faithful, bool shuffle, int repeat)
    {
        if (index < 0 || index >= entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _entries = Array.AsReadOnly(entries.ToArray());
        _index = index;
        _faithful = faithful;
        _shuffle = shuffle;
        _repeat = repeat;
        _current = PreparedTrack.Open(_entries[index].Track, null, faithful);
        WaveFormat = _current.WaveFormat;
        PrepareNext();
    }

    public void ConfigureNavigation(bool shuffle, int repeat)
    {
        lock (_gate)
        {
            if (_disposed || (_shuffle == shuffle && _repeat == repeat)) return;
            _shuffle = shuffle; _repeat = repeat;
            AbandonNext();
            PrepareNext();
        }
    }

    private void PrepareNext()
    {
        _nextIndex = _repeat == 2 ? _index : _index + 1;
        if (_repeat != 2 && _shuffle && _entries.Count > 1)
            do _nextIndex = _random.Next(_entries.Count); while (_nextIndex == _index);
        if (_nextIndex >= _entries.Count) _nextIndex = _repeat == 1 ? 0 : -1;
        if (_nextIndex < 0) return;
        var track = _entries[_nextIndex].Track;
        var cancellation = new CancellationTokenSource();
        _nextCancellation = cancellation;
        _next = Task.Run(() =>
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var prepared = PreparedTrack.Open(track, WaveFormat, _faithful);
            if (cancellation.IsCancellationRequested) { prepared.Dispose(); cancellation.Token.ThrowIfCancellationRequested(); }
            return prepared;
        }, cancellation.Token);
    }

    private void AbandonNext()
    {
        var task = _next;
        var cancellation = _nextCancellation;
        _next = null; _nextCancellation = null; _nextIndex = -1;
        cancellation?.Cancel();
        if (task is null) { cancellation?.Dispose(); return; }
        _ = task.ContinueWith(completed =>
        {
            if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
            else _ = completed.Exception; // Observe prefetch failures even after cancellation/stop.
            cancellation?.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("Invalid buffer range.");
        var total = 0;
        var changed = false;
        lock (_gate)
        {
            if (_disposed || _ended) return 0;
            count -= count % WaveFormat.BlockAlign;
            while (total < count)
            {
                var read = _current.Read(buffer, offset + total, count - total);
                total += read;
                if (read > 0) continue;
                // An unavailable next decoder or incompatible faithful format
                // is an explicit fallback, never silent padding on this stream.
                if (_next is null || !_next.IsCompletedSuccessfully || !_next.Result.WaveFormat.Equals(WaveFormat))
                {
                    _fallbackIndex = _nextIndex;
                    _ended = true;
                    break;
                }
                var old = _current;
                _current = _next.Result;
                _index = _nextIndex;
                _revision++;
                changed = true;
                _next = null;
                _nextCancellation?.Dispose(); _nextCancellation = null;
                old.Dispose();
                PrepareNext();
                // Bound pathological empty/repeating files without an endless audio callback.
                if (_current.Length == 0) { _ended = true; break; }
            }
        }
        if (changed) TrackChanged?.Invoke();
        return total;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                AbandonNext();
                _current.Dispose();
                TrackChanged = null;
            }
        }
        base.Dispose(disposing);
    }

    private sealed class PreparedTrack : IDisposable
    {
        private readonly TrackAudioReader _owner;
        private readonly bool _faithful;
        private IWaveProvider _provider;
        private byte[]? _prefix;
        private int _prefixPosition;
        private int _prefixLength;
        public WaveFormat WaveFormat { get; }
        public long Length { get; }
        public long Position { get; private set; }

        private PreparedTrack(TrackAudioReader owner, WaveFormat? target, bool faithful)
        {
            _owner = owner; _faithful = faithful;
            var source = owner.Reader.WaveFormat;
            var channels = Math.Max(2, source.Channels);
            // Keep existing multichannel playback usable. A channel-layout change
            // outside mono/stereo uses an ordinary transition instead of downmixing.
            WaveFormat = faithful ? source
                : target is not null && target.Channels == channels ? target
                : WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, channels);
            Length = (long)Math.Round((double)(owner.Reader.Length / source.BlockAlign)
                * WaveFormat.SampleRate / source.SampleRate) * WaveFormat.BlockAlign;
            _provider = CreateProvider();
            // At most ~1 second of prepared PCM; never decode an entire album into RAM.
            _prefix = new byte[Math.Min(WaveFormat.AverageBytesPerSecond, 256 * 1024) / WaveFormat.BlockAlign * WaveFormat.BlockAlign];
            _prefixLength = _provider.Read(_prefix, 0, _prefix.Length);
        }

        public static PreparedTrack Open(ZipTrack track, WaveFormat? target, bool faithful)
        {
            var owner = TrackAudioReader.OpenGapless(track);
            try { return new PreparedTrack(owner, target, faithful); }
            catch { owner.Dispose(); throw; }
        }

        private IWaveProvider CreateProvider()
        {
            if (_faithful) return _owner.Reader;
            ISampleProvider samples = _owner.Reader.ToSampleProvider();
            if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
            if (samples.WaveFormat.SampleRate != WaveFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, WaveFormat.SampleRate);
            return samples.ToWaveProvider();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            // Limit to the exact track duration, excluding codec padding when known.
            count = (int)Math.Min(count, Math.Max(0, Length - Position));
            var copied = Math.Min(count, _prefixLength - _prefixPosition);
            if (copied > 0)
            {
                Buffer.BlockCopy(_prefix!, _prefixPosition, buffer, offset, copied);
                _prefixPosition += copied;
            }
            var read = copied;
            if (read < count) read += _provider.Read(buffer, offset + read, count - read);
            Position += read;
            return read;
        }

        public void Seek(long position)
        {
            position = Math.Clamp(position, 0, Length);
            position -= position % WaveFormat.BlockAlign;
            var source = _owner.Reader.WaveFormat;
            var sourceFrames = (long)Math.Round((double)(position / WaveFormat.BlockAlign) * source.SampleRate / WaveFormat.SampleRate);
            _owner.Reader.Position = sourceFrames * source.BlockAlign;
            _provider = CreateProvider();
            _prefix = null; _prefixPosition = _prefixLength = 0;
            Position = position;
        }
        public void Dispose() => _owner.Dispose();
    }
}
