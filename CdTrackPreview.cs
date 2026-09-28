using System.IO;
using NAudio.Wave;

namespace ZipMp3Player;

// Read only the selected track, keeping the final partial output buffer inside its bounds.
internal sealed class CdTrackPreviewSource : IWaveProvider, IDisposable
{
    private readonly object gate = new();
    private readonly Func<long, int, byte[]> read;
    private readonly Action release;
    private readonly long end;
    private readonly long start;
    private long sector;
    private byte[] block = [];
    private int blockOffset;
    private int seekOffset;
    private bool disposed;
    public WaveFormat WaveFormat { get; } = new(44100, 16, 2);
    internal TimeSpan Duration { get; }
    internal TimeSpan StartPosition { get; private set; }

    internal static CdTrackPreviewSource Open(string drive, CueAlbumReader.Disc expected, int number, TimeSpan position = default)
    {
        var cd = new CdAudioSource(drive);
        try
        {
            if (cd.Disc.Toc != expected.Toc) throw new IOException("CDが変更されています。「CDを読み込む」で再読込してください。");
            var source = new CdTrackPreviewSource(cd.Disc, number, cd.Read, cd.Dispose);
            source.Seek(position);
            return source;
        }
        catch { cd.Dispose(); throw; }
    }

    internal CdTrackPreviewSource(CueAlbumReader.Disc disc, int number, Func<long, int, byte[]> read, Action release)
    {
        int index = disc.Tracks.FindIndex(t => t.Number == number);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(number));
        sector = disc.Tracks[index].Frame;
        start = sector;
        end = index + 1 < disc.Tracks.Count ? disc.Tracks[index + 1].Frame : disc.Frames;
        if (end <= sector) throw new InvalidDataException("CDの曲位置が不正です。");
        Duration = TimeSpan.FromSeconds((end - sector) / 75d);
        this.read = read;
        this.release = release;
    }

    internal void Seek(TimeSpan position)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Seek at a stereo PCM sample boundary, retaining any offset inside the CD sector.
            long bytes = position >= Duration ? (end - start) * 2352 : (long)(Math.Max(position.TotalSeconds, 0) * WaveFormat.AverageBytesPerSecond);
            bytes -= bytes % WaveFormat.BlockAlign;
            sector = start + bytes / 2352;
            seekOffset = (int)(bytes % 2352);
            block = [];blockOffset = 0;
            StartPosition = TimeSpan.FromSeconds(bytes / (double)WaveFormat.AverageBytesPerSecond);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
        lock (gate)
        {
            if (disposed) return 0;
            int copied = 0;
            while (copied < count)
            {
                if (blockOffset == block.Length)
                {
                    if (sector >= end) break;
                    int sectors = (int)Math.Min(16, end - sector);
                    block = read(sector, sectors);
                    if (block.Length != sectors * 2352) throw new IOException("CDの試聴データが不足しています。");
                    sector += sectors;
                    blockOffset = seekOffset;
                    seekOffset = 0;
                }
                int bytes = Math.Min(count - copied, block.Length - blockOffset);
                Buffer.BlockCopy(block, blockOffset, buffer, offset + copied, bytes);
                blockOffset += bytes;
                copied += bytes;
            }
            return copied;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            block = [];
            release();
        }
    }
}

internal sealed class CdTrackPreviewPlayer : IDisposable
{
    private readonly CdTrackPreviewSource source;
    private readonly WaveOutEvent output;
    internal event EventHandler<StoppedEventArgs>? Stopped;
    internal TimeSpan Duration => source.Duration;
    internal TimeSpan Position => TimeSpan.FromSeconds(Math.Min(Duration.TotalSeconds, source.StartPosition.TotalSeconds + output.GetPosition() / (double)source.WaveFormat.AverageBytesPerSecond));
    internal static CdTrackPreviewPlayer Open(string drive, CueAlbumReader.Disc disc, int number, TimeSpan position = default)
        => new(CdTrackPreviewSource.Open(drive, disc, number, position));
    private CdTrackPreviewPlayer(CdTrackPreviewSource source)
    {
        this.source = source;
        output = new WaveOutEvent { DesiredLatency = 300, NumberOfBuffers = 3 };
        try { output.Init(source); output.PlaybackStopped += PlaybackStopped; }
        catch { output.Dispose(); source.Dispose(); throw; }
    }
    private void PlaybackStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(this, e);
    internal void Play() => output.Play();
    public void Dispose()
    {
        output.PlaybackStopped -= PlaybackStopped;
        try { output.Stop(); }
        finally { try { output.Dispose(); } finally { source.Dispose(); } }
    }
}
