using System.IO;
using System.IO.Compression;
using System.Text;

namespace ZipMp3Player;

internal static class ArchiveEntryExtractor
{
    private const int BufferSize = 128 * 1024;

    static ArchiveEntryExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Stream OpenSeekable(ZipTrack track)
    {
        if (!track.IsArchiveEntry)
            return new FileStream(track.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        return track.CompressionMethod switch
        {
            0 => new BoundedFileStream(track.SourcePath, track.DataOffset, track.Size),
            8 => InflateToTemporaryStream(track.SourcePath, track.DataOffset, track.CompressedSize, track.Size),
            _ => throw new NotSupportedException($"ZIP圧縮方式 {track.CompressionMethod} には対応していません。")
        };
    }

    public static Stream OpenSeekable(string sourcePath, long dataOffset, long compressedSize,
        long uncompressedSize, int compressionMethod)
    {
        return compressionMethod switch
        {
            0 => new BoundedFileStream(sourcePath, dataOffset, uncompressedSize),
            8 => InflateToTemporaryStream(sourcePath, dataOffset, compressedSize, uncompressedSize),
            _ => throw new NotSupportedException($"ZIP圧縮方式 {compressionMethod} には対応していません。")
        };
    }

    public static Stream OpenSeekable(string sourcePath, string entryName)
    {
        using var file = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(932));
        var matches = archive.Entries.Where(entry =>
            string.Equals(entry.FullName, entryName, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(matches.Length == 0
                ? $"ZIP内に画像が見つかりません: {entryName}"
                : $"ZIP内に同名の画像が複数あります: {entryName}");

        var entry = matches[0];
        var output = new MemoryStream(entry.Length > 0 && entry.Length <= int.MaxValue ? (int)entry.Length : 0);
        using (var input = entry.Open()) input.CopyTo(output);
        output.Position = 0;
        return output;
    }

    private static FileStream InflateToTemporaryStream(string sourcePath, long dataOffset,
        long compressedSize, long expectedSize)
    {
        if (compressedSize < 0 || expectedSize <= 0)
            throw new InvalidDataException("ZIPに記録された曲のサイズが正しくありません。");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "ZipMp3Player", "Playback");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.mp3");
        var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete, BufferSize,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        try
        {
            using var bounded = new BoundedFileStream(sourcePath, dataOffset, compressedSize);
            using var deflate = new DeflateStream(bounded, CompressionMode.Decompress);
            var buffer = new byte[BufferSize];
            long total = 0;
            while (true)
            {
                var read = deflate.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > expectedSize)
                    throw new InvalidDataException("ZIPの展開サイズが記録値を超えたため、安全のため中止しました。");
                output.Write(buffer, 0, read);
            }

            if (total != expectedSize)
                throw new InvalidDataException("ZIPの展開サイズが記録値と一致しません。ファイルが壊れている可能性があります。");
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            try { File.Delete(temporaryPath); } catch { }
            throw;
        }
    }
}
