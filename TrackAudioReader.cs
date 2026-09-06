using NAudio.Wave;
using System.IO;

namespace ZipMp3Player;

internal sealed class TrackAudioReader : IDisposable
{
    private readonly Stream? _source;
    private readonly string? _temporaryPath;

    private TrackAudioReader(WaveStream reader, Stream? source = null, string? temporaryPath = null)
    {
        Reader = reader;
        _source = source;
        _temporaryPath = temporaryPath;
    }

    public WaveStream Reader { get; }

    public static TrackAudioReader Open(ZipTrack track) => OpenCore(track, false);
    public static TrackAudioReader OpenGapless(ZipTrack track) => OpenCore(track, true);

    private static TrackAudioReader OpenCore(ZipTrack track, bool trimGapless)
    {
        // NAudio's ACM-backed Mp3FileReader recognizes MPEG Layer II headers but
        // returns zero decoded bytes for these files. Windows Media Foundation
        // decodes the same stream correctly, so route MP2 through it explicitly.
        if (track.AudioFormat.Equals("MP2", StringComparison.OrdinalIgnoreCase))
        {
            if (!track.IsArchiveEntry)
                return new TrackAudioReader(new MediaFoundationReader(track.SourcePath));
            return OpenArchivedMpegLayer2(track);
        }

        if (track.IsArchiveEntry || track.AudioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase)
            )
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

    private static TrackAudioReader OpenArchivedMpegLayer2(ZipTrack track)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "ZipMp3Player", "Playback");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.mp2");
        try
        {
            using (var source = ArchiveEntryExtractor.OpenSeekable(track))
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.Read, 128 * 1024, FileOptions.SequentialScan))
                source.CopyTo(output);
            return new TrackAudioReader(new MediaFoundationReader(temporaryPath), temporaryPath: temporaryPath);
        }
        catch
        {
            try { File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    public void Dispose()
    {
        Reader.Dispose();
        _source?.Dispose();
        if (_temporaryPath is not null)
        {
            try { File.Delete(_temporaryPath); } catch { }
        }
    }
}
