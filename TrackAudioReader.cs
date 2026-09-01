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

    public static TrackAudioReader Open(ZipTrack track) => OpenCore(track, false);
    public static TrackAudioReader OpenGapless(ZipTrack track) => OpenCore(track, true);

    private static TrackAudioReader OpenCore(ZipTrack track, bool trimGapless)
    {
        if (track.IsArchiveEntry || track.AudioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase))
        {
            var source = ArchiveEntryExtractor.OpenSeekable(track);
            Mp3FileReader? mp3 = null;
            try
            {
                mp3 = new Mp3FileReader(source);
                return new TrackAudioReader(trimGapless ? Mp3GaplessTrim.Apply(mp3) : mp3, source);
            }
            catch { mp3?.Dispose(); source.Dispose(); throw; }
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
