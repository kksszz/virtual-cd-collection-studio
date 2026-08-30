using NAudio.Wave;
using System.IO;

namespace ZipMp3Player;

internal sealed class TrackAudioReader : IDisposable
{
    private readonly Stream? _source;

    private TrackAudioReader(WaveStream reader, Stream? source = null)
    {
        Reader = reader;
        _source = source;
    }

    public WaveStream Reader { get; }

    public static TrackAudioReader Open(ZipTrack track)
    {
        if (track.IsArchiveEntry || track.AudioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase))
        {
            var source = ArchiveEntryExtractor.OpenSeekable(track);
            try { return new TrackAudioReader(new Mp3FileReader(source), source); }
            catch { source.Dispose(); throw; }
        }

        if (track.AudioFormat.Equals("WAV", StringComparison.OrdinalIgnoreCase))
            return new TrackAudioReader(new WaveFileReader(track.SourcePath));

        if (track.AudioFormat.Equals("FLAC", StringComparison.OrdinalIgnoreCase)
            || track.AudioFormat.Equals("M4A", StringComparison.OrdinalIgnoreCase))
            return new TrackAudioReader(new MediaFoundationReader(track.SourcePath));

        throw new NotSupportedException($"{track.AudioFormat}形式の再生には対応していません。");
    }

    public void Dispose()
    {
        Reader.Dispose();
        _source?.Dispose();
    }
}
