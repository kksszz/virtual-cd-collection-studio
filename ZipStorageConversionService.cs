using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ZipMp3Player;

internal sealed record ZipStorageConversionResult(string BackupPath, long OriginalSize, long ConvertedSize, int EntryCount);

internal static class ZipStorageConversionService
{
    private const uint EocdSignature = 0x06054b50;
    private const uint CentralSignature = 0x02014b50;

    static ZipStorageConversionService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool HasCompressedEntries(string path)
    {
        if (!File.Exists(path)) return false;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var (entryCount, centralOffset) = ReadDirectoryLocation(file);
        file.Position = centralOffset;
        Span<byte> header = stackalloc byte[46];
        for (var index = 0; index < entryCount; index++)
        {
            ReadExactly(file, header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralSignature)
                throw new InvalidDataException("ZIP中央ディレクトリが壊れています。");
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            file.Position += nameLength + extraLength + commentLength;
            if (method != 0) return true;
        }
        return false;
    }

    public static ZipStorageConversionResult ConvertToStored(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("変換するZIP.MP3が見つかりません。", sourcePath);
        if (!HasCompressedEntries(sourcePath)) throw new InvalidOperationException("このZIP.MP3はすでに無圧縮（Store）です。");
        var originalSize = new FileInfo(sourcePath).Length;
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.storetmp.zip");
        try
        {
            var entryCount = RebuildStored(sourcePath, temporary);
            VerifyEquivalent(sourcePath, temporary, entryCount);
            if (HasCompressedEntries(temporary))
                throw new InvalidDataException("変換後のZIPに圧縮された収録物が残っているため、元ファイルは変更しませんでした。");
            var convertedSize = new FileInfo(temporary).Length;
            var backup = ReplaceWithBackup(temporary, sourcePath);
            return new ZipStorageConversionResult(backup, originalSize, convertedSize, entryCount);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static int RebuildStored(string sourcePath, string destinationPath)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(932));
        using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var sourceEntry in source.Entries)
        {
            var destinationEntry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.NoCompression);
            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
            destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
            using var input = sourceEntry.Open();
            using var output = destinationEntry.Open();
            input.CopyTo(output);
        }
        return source.Entries.Count;
    }

    private static void VerifyEquivalent(string sourcePath, string convertedPath, int expectedCount)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(932));
        using var convertedStream = new FileStream(convertedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var converted = new ZipArchive(convertedStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        if (source.Entries.Count != expectedCount || converted.Entries.Count != expectedCount)
            throw new InvalidDataException("変換後のZIPで収録ファイル数が一致しないため、元ファイルは変更しませんでした。");
        for (var index = 0; index < expectedCount; index++)
        {
            var before = source.Entries[index];
            var after = converted.Entries[index];
            if (!string.Equals(before.FullName, after.FullName, StringComparison.Ordinal) || before.Length != after.Length)
                throw new InvalidDataException($"変換後のZIPで収録物が一致しません: {before.FullName}");
            using var beforeStream = before.Open();
            using var afterStream = after.Open();
            var beforeHash = SHA256.HashData(beforeStream);
            var afterHash = SHA256.HashData(afterStream);
            if (!beforeHash.SequenceEqual(afterHash))
                throw new InvalidDataException($"変換後の内容検証に失敗しました: {before.FullName}");
        }
    }

    private static (int EntryCount, long CentralOffset) ReadDirectoryLocation(Stream stream)
    {
        var searchLength = (int)Math.Min(stream.Length, 65557);
        var tail = new byte[searchLength];
        stream.Position = stream.Length - searchLength;
        ReadExactly(stream, tail);
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) != EocdSignature) continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(index + 10));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index + 16));
            if (count == ushort.MaxValue || offset == uint.MaxValue) throw new NotSupportedException("ZIP64形式の変換には対応していません。");
            return (count, offset);
        }
        throw new InvalidDataException("ZIP終端情報を見つけられません。");
    }

    private static string ReplaceWithBackup(string temporaryPath, string sourcePath)
    {
        var backup = CreateBackupPath(sourcePath);
        try { File.Replace(temporaryPath, sourcePath, backup, ignoreMetadataErrors: true); }
        catch (PlatformNotSupportedException) { ReplaceFallback(temporaryPath, sourcePath, backup); }
        catch (IOException) { ReplaceFallback(temporaryPath, sourcePath, backup); }
        return backup;
    }

    private static void ReplaceFallback(string temporaryPath, string sourcePath, string backup)
    {
        File.Copy(sourcePath, backup, overwrite: false);
        try { File.Move(temporaryPath, sourcePath, overwrite: true); }
        catch { File.Copy(backup, sourcePath, overwrite: true); throw; }
    }

    private static string CreateBackupPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var name = Path.GetFileName(sourcePath);
        for (var index = 0; index < 1000; index++)
        {
            var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + (index == 0 ? "" : $"-{index}");
            var candidate = Path.Combine(directory, $"{name}.before-store-{suffix}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("バックアップファイル名を作成できませんでした。");
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
