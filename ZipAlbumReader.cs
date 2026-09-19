using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using NAudio.Wave;

namespace ZipMp3Player;

public sealed class ZipTrack
{
    [JsonIgnore]
    public bool IsFavorite { get; set; }
    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    [JsonIgnore]
    public bool HasLyrics { get; set; }
    [JsonIgnore]
    public bool IsPlaying { get; set; }
    public int TrackNumber { get; init; }
    public string FileName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string Year { get; init; } = "";
    public string Genre { get; init; } = "";
    public int DiscNumber { get; init; }
    public int DiscCount { get; init; }
    public string AudioFormat { get; init; } = "MP3";
    public string SourcePath { get; init; } = "";
    public string CuePath { get; init; } = "";
    public long CueStartFrame { get; init; }
    public bool IsArchiveEntry { get; init; }
    public long DataOffset { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public int CompressionMethod { get; init; }
    public bool IsEncrypted { get; init; }
    public string ReadError { get; init; } = "";
    public bool IsMp3Valid { get; init; }
    public bool IsCbr { get; init; }
    public int BitrateKbps { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public TimeSpan Duration { get; init; }
    public string DurationText => Duration.TotalSeconds > 0 ? $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}" : "—";
    public string YearText => string.IsNullOrWhiteSpace(Year) ? "—" : Year;
    public string GenreText => string.IsNullOrWhiteSpace(Genre) ? "—" : Genre;
    public string DiscText => DiscNumber > 0 ? DiscCount > 0 ? $"{DiscNumber}/{DiscCount}" : DiscNumber.ToString() : "—";
    public string AudioText => AudioFormat is "WAV" or "FLAC" or "CD-DA"
        ? BitsPerSample > 0
            ? $"{AudioFormat} / {SampleRate / 1000.0:0.0} kHz / {BitsPerSample} bit"
            : $"{AudioFormat} / {SampleRate / 1000.0:0.0} kHz"
        : AudioFormat == "M4A"
            ? BitrateKbps > 0
                ? $"M4A / {BitrateKbps} kbps / {SampleRate / 1000.0:0.0} kHz"
                : $"M4A / {SampleRate / 1000.0:0.0} kHz"
        : BitrateKbps > 0
            ? $"{AudioFormat} / {(IsCbr ? "CBR" : "VBR")} {BitrateKbps} kbps / {SampleRate / 1000.0:0.0} kHz"
            : AudioFormat;
    // Older caches contain validated IsCbr metadata but no IsMp3Valid field.
    // Version 0.46 may also have re-saved those caches with IsMp3Valid=false.
    private bool HasValidMp3Audio => (IsMp3Valid || IsCbr)
        && BitrateKbps > 0 && SampleRate > 0 && Duration > TimeSpan.Zero;

    public bool IsSupported => AudioFormat is "WAV" or "FLAC" or "M4A" or "CD-DA"
        ? !IsArchiveEntry && SampleRate > 0 && Duration > TimeSpan.Zero
        : CompressionMethod is 0 or 8 && !IsEncrypted && string.IsNullOrWhiteSpace(ReadError)
            && HasValidMp3Audio;
    public string SupportText => IsEncrypted ? LocalizationService.Select("暗号化", "Encrypted")
        : AudioFormat is "WAV" or "FLAC" or "M4A" or "CD-DA" ? IsSupported
            ? LocalizationService.Select("再生可能", "Playable") : LocalizationService.Select("形式未判定", "Unknown format")
        : !string.IsNullOrWhiteSpace(ReadError) ? LocalizationService.Select("ZIP読込エラー", "ZIP read error")
        : CompressionMethod is not (0 or 8) ? LocalizationService.Select(
            $"ZIP圧縮方式 {CompressionMethod} は未対応", $"ZIP compression method {CompressionMethod} is unsupported")
        : !HasValidMp3Audio ? LocalizationService.Select("MP3解析エラー", "MP3 analysis error")
        : !IsCbr ? LocalizationService.Select("VBR・再生可能", "VBR / Playable")
        : LocalizationService.Select("CBR・再生可能", "CBR / Playable");
    public string UnsupportedMessage
    {
        get
        {
            if (IsEncrypted)
                return LocalizationService.Select(
                    "理由: ZIP内のMP3が暗号化されています。\n\n対処: パスワード保護を解除したZIPを使用してください。",
                    "Reason: The MP3 inside the ZIP is encrypted.\n\nSolution: Use a ZIP without password protection.");
            if (!string.IsNullOrWhiteSpace(ReadError))
                return LocalizationService.Select(
                    $"理由: ZIP内のMP3を展開または解析できませんでした。\n詳細: {ReadError}\n\n対処: ZIPの破損を確認するか、無圧縮（Store／保存）方式で作り直してください。",
                    $"Reason: The MP3 inside the ZIP could not be extracted or analyzed.\nDetails: {ReadError}\n\nSolution: Check the ZIP for damage, or recreate it using Store (no compression).");
            if (CompressionMethod is not (0 or 8))
                return LocalizationService.Select(
                    $"理由: ZIPの圧縮方式 {CompressionMethod} には対応していません。\n\n対処: 無圧縮（Store／保存）またはDeflate方式でZIPを作り直してください。",
                    $"Reason: ZIP compression method {CompressionMethod} is unsupported.\n\nSolution: Recreate the ZIP using Store (no compression) or Deflate.");
            if (AudioFormat is "WAV" or "FLAC" or "M4A" && IsArchiveEntry)
                return LocalizationService.Select(
                    $"理由: ZIP内の{AudioFormat}再生には対応していません。\n\n対処: {AudioFormat}ファイルを通常のフォルダへ展開して登録してください。",
                    $"Reason: {AudioFormat} playback from inside a ZIP is unsupported.\n\nSolution: Extract the {AudioFormat} files to a normal folder and register it.");
            if (!HasValidMp3Audio)
                return LocalizationService.Select(
                    "理由: 有効なMP3音声フレームを解析できませんでした。\n\n対処: 元のMP3が破損していないか確認してください。",
                    "Reason: Valid MP3 audio frames could not be analyzed.\n\nSolution: Check whether the source MP3 is damaged.");
            return LocalizationService.Select("理由: この音声形式を解析できませんでした。", "Reason: This audio format could not be analyzed.");
        }
    }
}

public sealed class ZipAlbum
{
    // Null in older caches; fall back to their per-entry compression metadata.
    public bool? ArchiveHasCompressedEntries { get; init; }
    [JsonIgnore]
    public bool HasCompressedArchiveContent => Tracks.Any(track => track.IsArchiveEntry)
        && (ArchiveHasCompressedEntries ?? (Tracks.Any(track => track.CompressionMethod != 0)
            || Images.Any(image => image.CompressionMethod != 0)
            || TextFiles.Any(text => text.CompressionMethod != 0)));
    public string Path { get; init; } = "";
    private readonly IReadOnlyList<ZipTrack> _tracks = [];
    public IReadOnlyList<ZipTrack> Tracks
    {
        get => _tracks;
        // Keep display and playback in the same order, including deserialized older caches.
        // Unknown disc/track numbers follow known numbers; equal keys retain source order.
        init => _tracks = value.OrderBy(track => track.DiscNumber > 0 ? track.DiscNumber : int.MaxValue)
            .ThenBy(track => track.TrackNumber > 0 ? track.TrackNumber : int.MaxValue).ToList();
    }
    public IReadOnlyList<ZipImage> Images { get; init; } = [];
    public IReadOnlyList<ZipTextFile> TextFiles { get; init; } = [];
    public int ImageCount { get; init; }
}

public sealed class ZipImage
{
    public string FileName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public long DataOffset { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
    public int CompressionMethod { get; init; }
}

public sealed class ZipTextFile
{
    public string FileName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public long DataOffset { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
    public int CompressionMethod { get; init; }
}

public static class ZipAlbumReader
{
    private const uint EocdSignature = 0x06054b50;
    private const uint CentralSignature = 0x02014b50;
    private const uint LocalSignature = 0x04034b50;

    static ZipAlbumReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static ZipAlbum Open(string path)
    {
        if (CueAlbumReader.IsCue(path) || CueAlbumReader.IsImage(path)) return CueAlbumReader.Open(path);
        // Library refreshes may overlap a verified atomic archive replacement.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var (entryCount, centralOffset) = ReadEocd(file);
        var tracks = new List<ZipTrack>();
        var images = new List<ZipImage>();
        var textFiles = new List<ZipTextFile>();
        var hasCompressedEntries = false;
        file.Position = centralOffset;
        var header = new byte[46];

        for (var index = 0; index < entryCount; index++)
        {
            ReadExactly(file, header);
            if (U32(header, 0) != CentralSignature) throw new InvalidDataException("ZIP中央ディレクトリが壊れています。");

            var flags = U16(header, 8);
            var method = U16(header, 10);
            hasCompressedEntries |= method != 0;
            var compressedSize = U32(header, 20);
            var uncompressedSize = U32(header, 24);
            var nameLength = U16(header, 28);
            var extraLength = U16(header, 30);
            var commentLength = U16(header, 32);
            var localOffset = U32(header, 42);
            var nameBytes = new byte[nameLength];
            ReadExactly(file, nameBytes);
            var encoding = (flags & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(932);
            var name = encoding.GetString(nameBytes);
            file.Position += extraLength + commentLength;

            var isMp3 = name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
            var isImage = name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            var isText = name.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            if (!isMp3 && !isImage && !isText) continue;
            if (compressedSize == uint.MaxValue || uncompressedSize == uint.MaxValue || localOffset == uint.MaxValue)
                throw new NotSupportedException("ZIP64形式は初版では未対応です。");

            var returnPosition = file.Position;
            var dataOffset = ReadDataOffset(file, localOffset);
            file.Position = returnPosition;
            if (isImage)
            {
                if ((flags & 1) == 0 && method is 0 or 8)
                    images.Add(new ZipImage
                    {
                        FileName = name, SourcePath = path, DataOffset = dataOffset,
                        CompressedSize = compressedSize, UncompressedSize = uncompressedSize,
                        CompressionMethod = method
                    });
                continue;
            }
            if (isText)
            {
                if ((flags & 1) == 0 && method is 0 or 8 && uncompressedSize <= 10 * 1024 * 1024)
                    textFiles.Add(new ZipTextFile
                    {
                        FileName = name, SourcePath = path, DataOffset = dataOffset,
                        CompressedSize = compressedSize, UncompressedSize = uncompressedSize,
                        CompressionMethod = method
                    });
                continue;
            }

            var tags = new TagInfo("", "", "", "", "", 0, 0, 0, 0);
            var audio = new AudioInfo(false, false, 0, 0, TimeSpan.Zero, 0);
            var readError = "";
            if ((flags & 1) == 0 && method is 0 or 8)
            {
                try
                {
                    using var trackStream = ArchiveEntryExtractor.OpenSeekable(path, dataOffset,
                        compressedSize, uncompressedSize, method);
                    tags = ReadTags(trackStream);
                    audio = AnalyzeMp3(trackStream, tags.AudioStart);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException)
                {
                    readError = exception.Message;
                }
            }

            var title = string.IsNullOrWhiteSpace(tags.Title) ? System.IO.Path.GetFileNameWithoutExtension(name) : tags.Title;
            tracks.Add(new ZipTrack
            {
                TrackNumber = tags.Track > 0 ? tags.Track : tracks.Count + 1,
                FileName = name,
                Title = title,
                Artist = tags.Artist,
                Album = tags.Album,
                Year = tags.Year,
                Genre = tags.Genre,
                DiscNumber = tags.Disc,
                DiscCount = tags.DiscCount,
                AudioFormat = audio.Layer == 2 ? "MP2" : "MP3",
                SourcePath = path,
                IsArchiveEntry = true,
                DataOffset = dataOffset,
                Size = uncompressedSize,
                CompressedSize = compressedSize,
                CompressionMethod = method,
                IsEncrypted = (flags & 1) != 0,
                ReadError = readError,
                IsMp3Valid = audio.IsValid,
                IsCbr = audio.IsCbr,
                BitrateKbps = audio.Bitrate,
                SampleRate = audio.SampleRate,
                Duration = audio.Duration
            });
        }

        if (tracks.Count == 0) throw new InvalidDataException("ZIP内にMP3が見つかりませんでした。");
        return new ZipAlbum
        {
            Path = path,
            Tracks = tracks,
            Images = images.OrderBy(image => image.FileName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            ArchiveHasCompressedEntries = hasCompressedEntries,
            TextFiles = textFiles.OrderBy(text => text.FileName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            ImageCount = images.Count + CountExternalImages(path, isFolderAlbum: false)
        };
    }

    public static ZipAlbum OpenFolder(string folderPath)
    {
        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(IsStandardAudioPath)
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (files.Count == 0) throw new InvalidDataException("フォルダ内に対応音楽ファイルが見つかりませんでした。");

        var tracks = new List<ZipTrack>();
        foreach(var cue in Directory.EnumerateFiles(folderPath,"*.cue")){
            var text=File.ReadAllText(cue);
            if(!System.Text.RegularExpressions.Regex.IsMatch(text,"(?im)^\\s*FILE\\s+\"[^\"]+\\.flac\"\\s+WAVE\\s*$"))continue;
            var expanded=CueAlbumReader.Open(cue);tracks.AddRange(expanded.Tracks);
            foreach(var source in expanded.Tracks.Select(t=>t.SourcePath).Distinct())files.RemoveAll(f=>Path.GetFullPath(f).Equals(source,StringComparison.OrdinalIgnoreCase));
        }
        foreach (var path in files)
        {
            var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            if (format is "WAV" or "FLAC" or "M4A")
            {
                tracks.Add(ReadTaggedTrack(path, format, tracks.Count + 1));
                continue;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var tags = ReadTags(stream);
            var audio = AnalyzeMp3(stream, tags.AudioStart);
            tracks.Add(new ZipTrack
            {
                TrackNumber = tags.Track > 0 ? tags.Track : tracks.Count + 1,
                FileName = Path.GetFileName(path),
                Title = string.IsNullOrWhiteSpace(tags.Title) ? Path.GetFileNameWithoutExtension(path) : tags.Title,
                Artist = tags.Artist,
                Album = tags.Album,
                Year = tags.Year,
                Genre = tags.Genre,
                DiscNumber = tags.Disc,
                DiscCount = tags.DiscCount,
                AudioFormat = audio.Layer == 2 ? "MP2" : "MP3",
                SourcePath = path,
                IsArchiveEntry = false,
                DataOffset = 0,
                Size = stream.Length,
                CompressedSize = stream.Length,
                CompressionMethod = 0,
                IsEncrypted = false,
                IsMp3Valid = audio.IsValid,
                IsCbr = audio.IsCbr,
                BitrateKbps = audio.Bitrate,
                SampleRate = audio.SampleRate,
                Duration = audio.Duration
            });
        }
        return new ZipAlbum
        {
            Path = folderPath,
            Tracks = tracks,
            ImageCount = CountExternalImages(folderPath, isFolderAlbum: true)
        };
    }

    internal static bool IsSupportedArchivePath(string path) =>
        path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    internal static bool IsStandardAudioPath(string path) =>
        !IsTagEditingArtifact(path)
        && !IsSupportedArchivePath(path)
        && (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase));

    internal static bool IsTagEditingArtifact(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(".", StringComparison.Ordinal)) return false;
        var marker = name.LastIndexOf(".tagtmp.", StringComparison.OrdinalIgnoreCase);
        if (marker <= 1) return false;
        var guidStart = name.LastIndexOf('.', marker - 1);
        if (guidStart < 0 || marker - guidStart - 1 != 32) return false;
        return name.AsSpan(guidStart + 1, 32).ToString().All(Uri.IsHexDigit);
    }

    private static ZipTrack ReadTaggedTrack(string path, string format, int fallbackTrack)
    {
        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        var properties = file.Properties;
        var duration = properties.Duration;
        var sampleRate = properties.AudioSampleRate;
        var bitsPerSample = properties.BitsPerSample;
        if ((duration <= TimeSpan.Zero || sampleRate <= 0)
            && TryReadDecodedProperties(path, format, out var decodedDuration, out var decodedSampleRate, out var decodedBitsPerSample))
        {
            if (duration <= TimeSpan.Zero) duration = decodedDuration;
            if (sampleRate <= 0) sampleRate = decodedSampleRate;
            if (bitsPerSample <= 0 && format is "WAV" or "FLAC") bitsPerSample = decodedBitsPerSample;
        }
        var bitrate = properties.AudioBitrate;
        if (bitrate <= 0 && duration.TotalSeconds > 0)
            bitrate = (int)Math.Round(new FileInfo(path).Length * 8.0 / duration.TotalSeconds / 1000.0);
        return new ZipTrack
        {
            TrackNumber = tag.Track > 0 && tag.Track <= int.MaxValue ? (int)tag.Track : fallbackTrack,
            FileName = Path.GetFileName(path),
            Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(path) : tag.Title,
            Artist = tag.Performers.FirstOrDefault() ?? "",
            Album = tag.Album ?? "",
            Year = tag.Year > 0 ? tag.Year.ToString() : "",
            Genre = tag.Genres.FirstOrDefault() ?? "",
            DiscNumber = tag.Disc > 0 && tag.Disc <= int.MaxValue ? (int)tag.Disc : 0,
            DiscCount = tag.DiscCount > 0 && tag.DiscCount <= int.MaxValue ? (int)tag.DiscCount : 0,
            AudioFormat = format,
            SourcePath = path,
            IsArchiveEntry = false,
            DataOffset = 0,
            Size = new FileInfo(path).Length,
            CompressedSize = new FileInfo(path).Length,
            CompressionMethod = 0,
            IsEncrypted = false,
            IsCbr = true,
            BitrateKbps = bitrate,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            Duration = duration
        };
    }

    private static bool TryReadDecodedProperties(string path, string format, out TimeSpan duration,
        out int sampleRate, out int bitsPerSample)
    {
        duration = TimeSpan.Zero;
        sampleRate = 0;
        bitsPerSample = 0;
        try
        {
            using WaveStream reader = format == "WAV"
                ? new WaveFileReader(path)
                : new MediaFoundationReader(path);
            duration = reader.TotalTime;
            sampleRate = reader.WaveFormat.SampleRate;
            bitsPerSample = reader.WaveFormat.BitsPerSample;
            return duration > TimeSpan.Zero && sampleRate > 0;
        }
        catch
        {
            return false;
        }
    }

    private static int CountExternalImages(string albumPath, bool isFolderAlbum)
    {
        try
        {
            if (isFolderAlbum)
                return Directory.EnumerateFiles(albumPath, "*", SearchOption.AllDirectories).Count(IsImagePath);

            var sidecar = albumPath.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase) ? albumPath[..^4] : "";
            if (Directory.Exists(sidecar))
                return Directory.EnumerateFiles(sidecar, "*", SearchOption.AllDirectories).Count(IsImagePath);

            var directory = Path.GetDirectoryName(albumPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
            var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Where(IsImagePath);
            var archiveCount = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Count(IsSupportedArchivePath);
            if (archiveCount <= 1) return candidates.Count();
            var fileName = Path.GetFileName(albumPath);
            var baseName = fileName.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^8] : Path.GetFileNameWithoutExtension(fileName);
            return candidates.Count(path => Path.GetFileNameWithoutExtension(path).StartsWith(baseName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return 0; }
    }

    private static bool IsImagePath(string path) => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static (ushort Count, uint Offset) ReadEocd(FileStream file)
    {
        var length = (int)Math.Min(file.Length, 65_557);
        var tail = new byte[length];
        file.Position = file.Length - length;
        ReadExactly(file, tail);
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) != EocdSignature) continue;
            if (U16(tail, i + 4) != 0 || U16(tail, i + 6) != 0)
                throw new NotSupportedException("分割ZIPは未対応です。");
            return (U16(tail, i + 10), U32(tail, i + 16));
        }
        throw new InvalidDataException("ZIPとして認識できません。");
    }

    private static long ReadDataOffset(FileStream file, uint localOffset)
    {
        file.Position = localOffset;
        Span<byte> local = stackalloc byte[30];
        ReadExactly(file, local);
        if (U32(local, 0) != LocalSignature) throw new InvalidDataException("ZIPローカルヘッダーが壊れています。");
        return localOffset + 30L + U16(local, 26) + U16(local, 28);
    }

    private sealed record TagInfo(string Title, string Artist, string Album, string Year, string Genre,
        int Track, int Disc, int DiscCount, long AudioStart);

    private static TagInfo ReadTags(Stream stream)
    {
        var title = ""; var artist = ""; var album = ""; var year = ""; var genre = "";
        var track = 0; var disc = 0; var discCount = 0; long audioStart = 0;
        Span<byte> header = stackalloc byte[10];
        stream.Position = 0;
        if (stream.Read(header) == 10 && header[..3].SequenceEqual("ID3"u8))
        {
            var major = header[3];
            var tagUnsynchronized = (header[5] & 0x80) != 0;
            var tagSize = SyncSafe(header[6..10]);
            audioStart = 10L + tagSize + ((header[5] & 0x10) != 0 ? 10 : 0);
            var body = new byte[Math.Min(tagSize, 16 * 1024 * 1024)];
            ReadExactly(stream, body);
            // ID3v2.2/v2.3 frame sizes describe the data before whole-tag
            // unsynchronization bytes are inserted. Remove those bytes from the
            // entire body before using the frame sizes; removing them after a
            // raw frame slice truncates the final UTF-16 byte (for example 明).
            if (tagUnsynchronized && major is 2 or 3)
                body = RemoveId3Unsynchronization(body);
            var pos = GetId3FrameStart(body, major, header[5]);
            var frameHeaderSize = major == 2 ? 6 : 10;
            while (pos + frameHeaderSize <= body.Length && major is 2 or 3 or 4)
            {
                var idLength = major == 2 ? 3 : 4;
                var id = Encoding.ASCII.GetString(body, pos, idLength);
                if (id[0] == '\0') break;
                var size = major switch
                {
                    2 => U24Big(body, pos + 3),
                    4 => SyncSafe(body.AsSpan(pos + 4, 4)),
                    _ => (int)U32Big(body, pos + 4)
                };
                if (size <= 0 || pos + frameHeaderSize + size > body.Length) break;
                var frameData = body.AsSpan(pos + frameHeaderSize, size);
                var formatFlags = major == 2 ? (byte)0 : body[pos + 9];
                var value = id.StartsWith('T')
                    ? DecodeId3Text(PrepareId3TextFrame(frameData, major, formatFlags,
                        tagUnsynchronized && major == 4))
                    : "";
                switch (id)
                {
                    case "TT2":
                    case "TIT2": title = value; break;
                    case "TP1":
                    case "TPE1": artist = value; break;
                    case "TAL":
                    case "TALB": album = value; break;
                    case "TYE":
                    case "TYER":
                    case "TDRC": year = value; break;
                    case "TCO":
                    case "TCON": genre = NormalizeId3Genre(value); break;
                    case "TRK":
                    case "TRCK": int.TryParse(value.Split('/')[0], out track); break;
                    case "TPA":
                    case "TPOS":
                        var discParts = value.Split('/');
                        int.TryParse(discParts[0], out disc);
                        if (discParts.Length > 1) int.TryParse(discParts[1], out discCount);
                        break;
                }
                pos += frameHeaderSize + size;
            }
        }

        if (stream.Length >= 128 && (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)))
        {
            var v1 = new byte[128]; stream.Position = stream.Length - 128; ReadExactly(stream, v1);
            if (v1.AsSpan(0, 3).SequenceEqual("TAG"u8))
            {
                title = string.IsNullOrWhiteSpace(title) ? DecodeV1(v1.AsSpan(3, 30)) : title;
                artist = string.IsNullOrWhiteSpace(artist) ? DecodeV1(v1.AsSpan(33, 30)) : artist;
                album = string.IsNullOrWhiteSpace(album) ? DecodeV1(v1.AsSpan(63, 30)) : album;
                year = string.IsNullOrWhiteSpace(year) ? DecodeV1(v1.AsSpan(93, 4)) : year;
                if (track == 0 && v1[125] == 0) track = v1[126];
            }
        }
        return new TagInfo(title, artist, album, year, genre, track, disc, discCount, audioStart);
    }

    private sealed record AudioInfo(bool IsValid, bool IsCbr, int Bitrate, int SampleRate, TimeSpan Duration, int Layer);

    private static AudioInfo AnalyzeMp3(Stream stream, long audioStart)
    {
        stream.Position = Math.Min(audioStart, stream.Length);
        var first = FindFirstFrame(stream);
        if (first is null) return new(false, false, 0, 0, TimeSpan.Zero, 0);
        var initialBitrate = first.Value.Bitrate;
        var sampleRate = first.Value.SampleRate;
        var layer = first.Value.Layer;
        if (TryReadXingAudioInfo(stream, first.Value.Offset, first.Value.FrameLength,
                first.Value.SamplesPerFrame, sampleRate, layer, out var xingInfo))
            return xingInfo;
        var frameCount = 0L;
        var sampleCount = 0L;
        var audioBytes = 0L;
        var isCbr = true;
        stream.Position = first.Value.Offset;
        Span<byte> four = stackalloc byte[4];
        while (stream.Position + 4 <= stream.Length)
        {
            var frameStart = stream.Position;
            if (stream.Read(four) != 4) break;
            var parsed = ParseMpegHeader(four);
            if (parsed is null) break;
            if (parsed.Value.SampleRate != sampleRate || parsed.Value.Layer != layer) break;
            var next = frameStart + parsed.Value.FrameLength;
            if (next <= frameStart || next > stream.Length) break;
            if (parsed.Value.Bitrate != initialBitrate) isCbr = false;
            frameCount++;
            sampleCount += parsed.Value.SamplesPerFrame;
            audioBytes += parsed.Value.FrameLength;
            stream.Position = next;
        }
        var duration = sampleRate > 0 ? TimeSpan.FromSeconds(sampleCount / (double)sampleRate) : TimeSpan.Zero;
        var averageBitrate = duration.TotalSeconds > 0
            ? (int)Math.Round(audioBytes * 8.0 / duration.TotalSeconds / 1000.0)
            : 0;
        var isValid = frameCount >= 3;
        return new(isValid, isValid && isCbr, averageBitrate, sampleRate, duration, layer);
    }

    private static bool TryReadXingAudioInfo(Stream stream, long frameOffset, int frameLength,
        int samplesPerFrame, int sampleRate, int layer, out AudioInfo audio)
    {
        audio = new(false, false, 0, 0, TimeSpan.Zero, 0);
        if (frameLength < 16 || sampleRate <= 0) return false;
        var header = new byte[Math.Min(frameLength, 256)];
        stream.Position = frameOffset;
        var read = stream.Read(header, 0, header.Length);
        for (var offset = 4; offset + 12 <= read; offset++)
        {
            var isXing = header.AsSpan(offset, 4).SequenceEqual("Xing"u8);
            var isInfo = header.AsSpan(offset, 4).SequenceEqual("Info"u8);
            if (!isXing && !isInfo) continue;
            var flags = U32Big(header, offset + 4);
            if ((flags & 1) == 0) return false;
            var frames = U32Big(header, offset + 8);
            if (frames < 3) return false;
            var duration = TimeSpan.FromSeconds(frames * (double)samplesPerFrame / sampleRate);
            var audioBytes = Math.Max(0, stream.Length - frameOffset);
            var cursor = offset + 12;
            if ((flags & 2) != 0 && cursor + 4 <= read)
                audioBytes = U32Big(header, cursor);
            var averageBitrate = duration.TotalSeconds > 0
                ? (int)Math.Round(audioBytes * 8.0 / duration.TotalSeconds / 1000.0)
                : 0;
            audio = new(true, isInfo, averageBitrate, sampleRate, duration, layer);
            return true;
        }
        return false;
    }

    private static (long Offset, int Bitrate, int SampleRate, int FrameLength, int SamplesPerFrame, int Layer)? FindFirstFrame(Stream stream)
    {
        var start = stream.Position;
        Span<byte> header = stackalloc byte[4];
        for (var i = 0; i < 64 * 1024 && stream.Position + 4 <= stream.Length; i++)
        {
            var pos = stream.Position;
            if (stream.Read(header) != 4) return null;
            var parsed = ParseMpegHeader(header);
            if (parsed is not null)
            {
                var next = pos + parsed.Value.FrameLength;
                if (next + 4 <= stream.Length)
                {
                    stream.Position = next;
                    if (stream.Read(header) == 4 && ParseMpegHeader(header) is not null)
                        return (pos, parsed.Value.Bitrate, parsed.Value.SampleRate, parsed.Value.FrameLength,
                            parsed.Value.SamplesPerFrame, parsed.Value.Layer);
                }
            }
            stream.Position = pos + 1;
        }
        stream.Position = start;
        return null;
    }

    private static (int Bitrate, int SampleRate, int FrameLength, int SamplesPerFrame, int Layer)? ParseMpegHeader(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return null;
        var h = BinaryPrimitives.ReadUInt32BigEndian(b);
        if ((h & 0xFFE00000) != 0xFFE00000) return null;
        var version = (h >> 19) & 3; var layerBits = (h >> 17) & 3;
        var bitrateIndex = (int)((h >> 12) & 15); var sampleIndex = (int)((h >> 10) & 3);
        if (version == 1 || layerBits is not (1 or 2) || bitrateIndex is 0 or 15 || sampleIndex == 3) return null;
        var layer = layerBits == 2 ? 2 : 3;
        int[] mpeg1Layer3Rates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        int[] mpeg1Layer2Rates = [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384];
        int[] mpeg2Rates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        int[] mpeg1Samples = [44100, 48000, 32000];
        var divisor = version == 3 ? 1 : version == 2 ? 2 : 4;
        var bitrate = (version == 3 ? layer == 2 ? mpeg1Layer2Rates : mpeg1Layer3Rates : mpeg2Rates)[bitrateIndex];
        var sampleRate = mpeg1Samples[sampleIndex] / divisor;
        var padding = (int)((h >> 9) & 1);
        var samplesPerFrame = layer == 2 || version == 3 ? 1152 : 576;
        var frameCoefficient = layer == 2 || version == 3 ? 144 : 72;
        var frameLength = frameCoefficient * bitrate * 1000 / sampleRate + padding;
        return (bitrate, sampleRate, frameLength, samplesPerFrame, layer);
    }

    private static string DecodeId3Text(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return "";
        if (data[0] == 0) return DecodeLegacyText(data[1..]);
        var encodingByte = data[0];
        var payload = data[1..];
        if (encodingByte is 1 or 2)
        {
            // Some older ID3 writers (and TagLibSharp when preserving those
            // frames) leave a single NUL padding byte after UTF-16 text. Feeding
            // the resulting odd byte count to Encoding produces a trailing U+FFFD.
            if ((payload.Length & 1) != 0 && payload[^1] == 0) payload = payload[..^1];
            while (payload.Length >= 2 && payload[^1] == 0 && payload[^2] == 0)
                payload = payload[..^2];
        }
        if (encodingByte == 1) return DecodeUtf16WithBomRepair(payload);
        var encoding = encodingByte switch { 2 => Encoding.BigEndianUnicode, 3 => Encoding.UTF8, _ => Encoding.Latin1 };
        return encoding.GetString(payload).Trim('\0', ' ', '\ufeff');
    }
    private static string DecodeUtf16WithBomRepair(ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 6 && payload[0] == 0xff && payload[1] == 0xfe)
        {
            var pairs = Math.Min((payload.Length - 2) / 2, 32);
            var bigEndianZeros = 0;
            var littleEndianZeros = 0;
            for (var index = 0; index < pairs; index++)
            {
                if (payload[2 + index * 2] == 0) bigEndianZeros++;
                if (payload[3 + index * 2] == 0) littleEndianZeros++;
            }
            // Some older taggers emitted an LE BOM followed by BE text.
            if ((bigEndianZeros >= 3 && bigEndianZeros > littleEndianZeros * 2)
                || (pairs > 0 && bigEndianZeros == pairs && littleEndianZeros == 0))
                return Encoding.BigEndianUnicode.GetString(payload[2..]).Trim('\0', ' ', '\ufeff');
        }
        else if (payload.Length >= 6 && payload[0] == 0xfe && payload[1] == 0xff)
        {
            var pairs = Math.Min((payload.Length - 2) / 2, 32);
            var bigEndianZeros = 0;
            var littleEndianZeros = 0;
            for (var index = 0; index < pairs; index++)
            {
                if (payload[2 + index * 2] == 0) bigEndianZeros++;
                if (payload[3 + index * 2] == 0) littleEndianZeros++;
            }
            if ((littleEndianZeros >= 3 && littleEndianZeros > bigEndianZeros * 2)
                || (pairs > 0 && littleEndianZeros == pairs && bigEndianZeros == 0))
                return Encoding.Unicode.GetString(payload[2..]).Trim('\0', ' ', '\ufeff');
        }
        return Encoding.Unicode.GetString(payload).Trim('\0', ' ', '\ufeff');
    }
    private static int GetId3FrameStart(ReadOnlySpan<byte> body, byte major, byte flags)
    {
        if ((flags & 0x40) == 0 || body.Length < 4) return 0;
        if (major == 2) return body.Length; // ID3v2.2 uses this bit for whole-tag compression.
        var size = major == 4 ? SyncSafe(body[..4]) : (int)U32Big(body, 0) + 4;
        return Math.Clamp(size, 0, body.Length);
    }
    private static string NormalizeId3Genre(string value)
    {
        var parts = value.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            var explicitName = parts.LastOrDefault(part => TagLib.Genres.IndexToAudio(part) is null);
            if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        }
        var close = value.IndexOf(')');
        if (value.StartsWith('(') && close > 0 && close + 1 < value.Length)
        {
            var explicitName = value[(close + 1)..].Trim('\0', ' ');
            if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        }
        return TagLib.Genres.IndexToAudio(value) ?? value;
    }
    private static byte[] PrepareId3TextFrame(ReadOnlySpan<byte> data, byte major, byte formatFlags,
        bool tagUnsynchronized)
    {
        if (major != 4)
        {
            var legacy = data.ToArray();
            return tagUnsynchronized ? RemoveId3Unsynchronization(legacy) : legacy;
        }
        if ((formatFlags & 0x0c) != 0) return []; // Compressed or encrypted text is not directly decodable.
        var offset = 0;
        if ((formatFlags & 0x40) != 0) offset++; // Group identifier.
        if ((formatFlags & 0x01) != 0) offset += 4; // Data length indicator.
        if (offset > data.Length) return [];
        var result = data[offset..].ToArray();
        return tagUnsynchronized || (formatFlags & 0x02) != 0 ? RemoveId3Unsynchronization(result) : result;
    }
    private static byte[] RemoveId3Unsynchronization(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length];
        var count = 0;
        for (var index = 0; index < data.Length; index++)
        {
            result[count++] = data[index];
            if (data[index] == 0xff && index + 1 < data.Length && data[index + 1] == 0x00) index++;
        }
        return result[..count];
    }
    private static string DecodeV1(ReadOnlySpan<byte> data) => DecodeLegacyText(data);
    private static string DecodeLegacyText(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var latin = Encoding.Latin1.GetString(bytes).TrimEnd('\0', ' ');
        try
        {
            var strict932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var japanese = strict932.GetString(bytes).TrimEnd('\0', ' ');
            if (japanese.Any(IsJapaneseCodePageCharacter)) return japanese;
        }
        catch (DecoderFallbackException) { }
        // Encoding byte 0 is nominally Latin-1. Windows-created tags often contain CP1252 punctuation.
        // Prefer CP1252 when no Japanese Shift-JIS sequence was detected.
        try { return Encoding.GetEncoding(1252).GetString(bytes).TrimEnd('\0', ' '); }
        catch { return latin; }
    }
    private static bool IsJapaneseCodePageCharacter(char ch) => ch is
        >= '\u3040' and <= '\u30ff' // Hiragana and Katakana
        or >= '\u3400' and <= '\u9fff' // CJK ideographs
        or >= '\uff01' and <= '\uffef' // Full-width and half-width forms
        or >= '\u2160' and <= '\u217f' // Roman numerals (Ⅰ, Ⅱ, Ⅲ...)
        or >= '\u2460' and <= '\u24ff' // Enclosed numbers and letters
        or '\u2116' or '\u2121' or '\u3231'; // №, TEL and Japanese corporate mark
    private static int SyncSafe(ReadOnlySpan<byte> b) => (b[0] << 21) | (b[1] << 14) | (b[2] << 7) | b[3];
    private static ushort U16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b[p..]);
    private static uint U32(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
    private static uint U32Big(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b[p..]);
    private static int U24Big(ReadOnlySpan<byte> b, int p) => (b[p] << 16) | (b[p + 1] << 8) | b[p + 2];
    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer[read..]);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }
}
