using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal static class CueAlbumReader
{
    internal static bool IsCue(string path) => path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase);
    internal static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".bin";
    internal static string ResolveCue(string path)
    {
        if (IsCue(path)) return Path.GetFullPath(path);
        var cue = Path.ChangeExtension(path, ".cue");
        if (!File.Exists(cue)) throw new NotSupportedException("同名のCUEが必要です。単独ISO・SACD・DVD-Audioには対応していません。");
        return Path.GetFullPath(cue);
    }

    internal sealed record CueTrack(int Number, long Frame, string Title, string Artist);
    internal sealed record Disc(string CuePath, string ImagePath, string Album, string Artist,
        int DiscNumber, long Frames, List<CueTrack> Tracks, string Fingerprint)
    {
        internal string DiscId
        {
            get
            {
                var text = new StringBuilder("01" + Tracks.Count.ToString("X2") + (Frames + 150).ToString("X8"));
                for (var n = 0; n < 99; n++) text.Append((n < Tracks.Count ? Tracks[n].Frame + 150 : 0).ToString("X8"));
                return Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(text.ToString())))
                    .Replace('+', '.').Replace('/', '_').Replace('=', '-');
            }
        }
        internal string Toc => $"1 {Tracks.Count} {Frames + 150} " + string.Join(" ", Tracks.Select(t => t.Frame + 150));
    }

    internal static Disc Read(string path)
    {
        path = ResolveCue(path);
        var info = new FileInfo(path);
        if (info.Length > 1024 * 1024) throw new InvalidDataException("CUEが大きすぎます。");
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(932).GetString(bytes);
        }
        string? image = null;
        var album = Path.GetFileNameWithoutExtension(path);
        var artist = "アーティスト不明";
        var discNumber = 1;
        var suffix = Regex.Match(album, @"(?:disc|cd)[ _-]*(\d+)$", RegexOptions.IgnoreCase);
        if (suffix.Success) discNumber = int.Parse(suffix.Groups[1].Value, CultureInfo.InvariantCulture);
        var entries = new List<CueTrack>();
        var current = -1;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (Regex.IsMatch(line, @"^(PREGAP|POSTGAP)\s|^FLAGS\s+.*\bPRE\b", RegexOptions.IgnoreCase))
                throw new NotSupportedException("追加ギャップまたはプリエンファシスを指定したCUEには対応していません。");
            var file = Regex.Match(line, "^FILE\\s+\"([^\"]+)\"\\s+(\\S+)$", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(line, @"^FILE\s", RegexOptions.IgnoreCase))
            {
                if (!file.Success || image is not null || !(file.Groups[2].Value.Equals("BINARY", StringComparison.OrdinalIgnoreCase)||file.Groups[2].Value.Equals("WAVE",StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("1つのBINARY音声ファイルを参照するCUEのみ対応しています。");
                var relative = file.Groups[1].Value;
                // Do not follow arbitrary absolute/traversal paths embedded in a CUE.
                if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("CUEの参照先は同じフォルダー内にしてください。");
                var root = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
                image = Path.GetFullPath(Path.Combine(root, relative));
                if (!image.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !(IsImage(image)&&file.Groups[2].Value.Equals("BINARY",StringComparison.OrdinalIgnoreCase)||image.EndsWith(".flac",StringComparison.OrdinalIgnoreCase)&&file.Groups[2].Value.Equals("WAVE",StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("CUEの参照先が対応するISO/BINではありません。");
                continue;
            }
            var track = Regex.Match(line, @"^TRACK\s+(\d+)\s+(\S+)$", RegexOptions.IgnoreCase);
            if (track.Success)
            {
                if (image is null || !track.Groups[2].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("音声トラックのみのCUEに対応しています。");
                var number = int.Parse(track.Groups[1].Value, CultureInfo.InvariantCulture);
                if (number != entries.Count + 1 || number > 99) throw new InvalidDataException("CUEの曲番号が連続していません。");
                entries.Add(new(number, -1, $"Track {number:00}", artist)); current++;
                continue;
            }
            var index = Regex.Match(line, @"^INDEX\s+(\d+)\s+(\d+):(\d+):(\d+)$", RegexOptions.IgnoreCase);
            if (index.Success)
            {
                if (current < 0) throw new InvalidDataException("TRACKより前にINDEXがあります。");
                var seconds = int.Parse(index.Groups[3].Value); var frames = int.Parse(index.Groups[4].Value);
                if (seconds >= 60 || frames >= 75) throw new InvalidDataException("CUEの時刻が不正です。");
                if (index.Groups[1].Value is "01" or "1")
                {
                    if (entries[current].Frame >= 0) throw new InvalidDataException("INDEX 01が重複しています。");
                    entries[current] = entries[current] with { Frame = long.Parse(index.Groups[2].Value) * 4500 + seconds * 75 + frames };
                }
                continue;
            }
            var tag = Regex.Match(line, "^(TITLE|PERFORMER)\\s+\"(.*)\"$", RegexOptions.IgnoreCase);
            if (tag.Success)
            {
                var title = tag.Groups[1].Value.Equals("TITLE", StringComparison.OrdinalIgnoreCase);
                if (current < 0) { if (title) album = tag.Groups[2].Value; else artist = tag.Groups[2].Value; }
                else entries[current] = title ? entries[current] with { Title = tag.Groups[2].Value } : entries[current] with { Artist = tag.Groups[2].Value };
            }
        }
        if (image is null || entries.Count == 0) throw new InvalidDataException("CUEに音声トラックがありません。");
        var imageInfo = new FileInfo(image);
        bool flac=image.EndsWith(".flac",StringComparison.OrdinalIgnoreCase);
        if (!imageInfo.Exists || imageInfo.Length == 0 || (!flac&&imageInfo.Length % 2352 != 0))
            throw new InvalidDataException("音声イメージが見つからないか、2352バイト単位のCD音声ではありません。");
        var total = imageInfo.Length / 2352;
        if(flac){using var audio=new NAudio.Wave.MediaFoundationReader(image);total=(long)Math.Ceiling(audio.TotalTime.TotalSeconds*75);}
        long previous = -1;
        foreach (var entry in entries)
        {
            if (entry.Frame <= previous || entry.Frame >= total) throw new InvalidDataException("CUEの曲境界が不正です。");
            previous = entry.Frame;
        }
        var identity = text + "|" + imageInfo.Length + "|" + imageInfo.LastWriteTimeUtc.Ticks;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new(path, image, album, artist, discNumber, total, entries, fingerprint);
    }

    internal static ZipAlbum Open(string path)
    {
        var disc = Read(path);
        var metadata = CueMetadataStore.Load(disc);
        bool flac=disc.ImagePath.EndsWith(".flac",StringComparison.OrdinalIgnoreCase);
        using var decoded=flac?new NAudio.Wave.MediaFoundationReader(disc.ImagePath):null;
        return new ZipAlbum
        {
            Path = disc.CuePath,
            Tracks = disc.Tracks.Select((entry, n) => new ZipTrack
            {
                CuePath = disc.CuePath, FileName = $"{Path.GetFileName(disc.ImagePath)}#track{entry.Number:00}",
                SourcePath = disc.ImagePath, TrackNumber = entry.Number,
                DiscNumber = metadata?.DiscNumber ?? disc.DiscNumber,
                DiscCount = metadata?.DiscCount ?? 0,
                Title = metadata?.Tracks[n].Title ?? entry.Title,
                Artist = metadata?.Tracks[n].Artist ?? entry.Artist, Album = metadata?.Album ?? disc.Album,
                Year = metadata?.Year ?? "", AudioFormat = flac?"FLAC":"CD-DA", SampleRate = decoded?.WaveFormat.SampleRate??44100, BitsPerSample = decoded?.WaveFormat.BitsPerSample??16,
                CueStartFrame=entry.Frame,
                DataOffset = entry.Frame * 2352,
                Size = ((n + 1 < disc.Tracks.Count ? disc.Tracks[n + 1].Frame : disc.Frames) - entry.Frame) * 2352,
                Duration = TimeSpan.FromSeconds(((n + 1 < disc.Tracks.Count ? disc.Tracks[n + 1].Frame : disc.Frames) - entry.Frame) / 75.0)
            }).ToList()
        };
    }
}
