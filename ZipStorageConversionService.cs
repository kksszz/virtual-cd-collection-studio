using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ZipMp3Player;

internal sealed record ZipStorageConversionResult(string BackupPath, long OriginalSize, long ConvertedSize, int EntryCount);

internal static class ZipStorageConversionService
{
    internal sealed record ExternalResult(string Source, string Destination, string Backup, string SourceHash, string OutputHash);

    internal static string StoredDestination(string source) => source.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
        ? source : source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? source + ".mp3"
        : throw new InvalidOperationException("ZIPではありません。");

    internal static void ValidateBackupRoot(string backup, IEnumerable<string> libraryRoots)
    {
        var full = Path.GetFullPath(backup).TrimEnd('\\');
        foreach (var root in libraryRoots)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\');
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase) || full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("バックアップ先がライブラリ登録フォルダー内です。処理を中止します。");
        }
        for (var parent = new DirectoryInfo(full); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンク経由のバックアップ先は使用できません。");
    }

    internal static ExternalResult ConvertWithExternalBackup(string source, string backupDirectory)
    {
        source = Path.GetFullPath(source);
        var destination = StoredDestination(source);
        if (destination != source && (File.Exists(destination) || Directory.Exists(destination)))
            throw new IOException("出力先がすでに存在します: " + destination);
        for (var parent = new FileInfo(source) as FileSystemInfo; parent is not null;
            parent = parent is FileInfo file ? file.Directory : ((DirectoryInfo)parent).Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクは変換できません。");
        if (!HasCompressedEntries(source)) throw new InvalidOperationException("すでに無圧縮です。");
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, Guid.NewGuid().ToString("N") + ".original");
        File.WriteAllText(backup + ".json", System.Text.Json.JsonSerializer.Serialize(new { Source = source, Destination = destination, Backup = backup }));
        var temporary = Path.Combine(Path.GetDirectoryName(source)!, "." + Guid.NewGuid().ToString("N") + ".storetmp");
        string sourceHash;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                input.Position = 0;
                sourceHash = Convert.ToHexString(SHA256.HashData(input));
                using (var saved = File.OpenRead(backup))
                    if (sourceHash != Convert.ToHexString(SHA256.HashData(saved))) throw new IOException("元ファイルのバックアップ検証に失敗しました。");
                var count = RebuildStored(backup, temporary);
                VerifyEquivalent(backup, temporary, count);
                if (HasCompressedEntries(temporary)) throw new IOException("圧縮された収録物が残っています。");
                // Parse all track metadata before publishing the replacement.
                _ = ZipAlbumReader.Open(temporary);
            }
            string outputHash;
            using (var output = File.OpenRead(temporary)) outputHash = Convert.ToHexString(SHA256.HashData(output));
            using (var current = File.OpenRead(source))
                if (sourceHash != Convert.ToHexString(SHA256.HashData(current))) throw new IOException("変換中に元ファイルが変更されました。");
            // The verified original remains outside the music library in all cases.
            if (destination == source) File.Replace(temporary, source, null, true);
            else File.Move(temporary, destination, false);
            return new(source, destination, backup, sourceHash, outputHash);
        }
        finally { TryDelete(temporary); }
    }

    internal static void FinishExternalConversion(ExternalResult result)
    {
        using var backup = File.OpenRead(result.Backup);
        if (Convert.ToHexString(SHA256.HashData(backup)) != result.SourceHash) throw new IOException("バックアップが変更されました。");
        using var output = File.OpenRead(result.Destination);
        if (Convert.ToHexString(SHA256.HashData(output)) != result.OutputHash) throw new IOException("出力ファイルが変更されました。");
        if (!result.Source.Equals(result.Destination, StringComparison.OrdinalIgnoreCase))
            FolderZipConversion.DeleteVerifiedFile(result.Source, result.SourceHash);
    }
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
