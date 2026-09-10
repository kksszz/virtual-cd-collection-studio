using System.IO;
using System.IO.Compression;
using System.Text;

namespace ZipMp3Player;

internal sealed record ArchiveImageDeletionResult(int RemainingEntryCount, long OriginalSize, long RebuiltSize);

internal static class ArchiveImageDeletionService
{
    static ArchiveImageDeletionService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static ArchiveImageDeletionResult Delete(string archivePath, string imageEntryName)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("画像を削除するZIPが見つかりません。", archivePath);
        if (string.IsNullOrWhiteSpace(imageEntryName)) throw new ArgumentException("削除する画像名が空です。", nameof(imageEntryName));
        if (!IsImageEntry(imageEntryName)) throw new InvalidOperationException("選択項目はZIP内画像ではありません。");

        var fullArchivePath = Path.GetFullPath(archivePath);
        var directory = Path.GetDirectoryName(fullArchivePath)!;
        // Do not use a .zip suffix: the library watcher must not discover a partially rebuilt archive.
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullArchivePath)}.{Guid.NewGuid():N}.imagetmp");
        var recoveryPath = Path.Combine(directory, $".{Path.GetFileName(fullArchivePath)}.{Guid.NewGuid():N}.imagerollback");
        var originalAttributes = File.GetAttributes(fullArchivePath);
        var originalSize = new FileInfo(fullArchivePath).Length;
        try
        {
            var expected = RebuildWithoutEntry(fullArchivePath, temporaryPath, imageEntryName);
            VerifyRebuiltArchive(temporaryPath, imageEntryName, expected);
            ReplaceWithRecovery(temporaryPath, fullArchivePath, recoveryPath);
            TryDelete(recoveryPath);
            TrySetAttributes(fullArchivePath, originalAttributes);
            return new ArchiveImageDeletionResult(expected.Count,
                originalSize,
                new FileInfo(fullArchivePath).Length);
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(recoveryPath);
            TrySetAttributes(fullArchivePath, originalAttributes);
        }
    }

    private static List<EntrySignature> RebuildWithoutEntry(string sourcePath, string destinationPath, string targetName)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(932));
        var matchingEntries = source.Entries.Count(entry => string.Equals(entry.FullName, targetName, StringComparison.Ordinal));
        if (matchingEntries != 1)
            throw new InvalidDataException(matchingEntries == 0
                ? $"ZIP内で削除対象の画像を見つけられませんでした: {targetName}"
                : $"ZIP内に同名の画像が複数あるため、安全に削除できません: {targetName}");

        var expected = new List<EntrySignature>(Math.Max(0, source.Entries.Count - 1));
        using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var sourceEntry in source.Entries)
        {
            if (string.Equals(sourceEntry.FullName, targetName, StringComparison.Ordinal)) continue;
            expected.Add(new EntrySignature(sourceEntry.FullName, sourceEntry.Length, sourceEntry.Crc32));
            var compression = sourceEntry.Length == sourceEntry.CompressedLength
                ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var destinationEntry = destination.CreateEntry(sourceEntry.FullName, compression);
            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
            destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
            using var input = sourceEntry.Open();
            using var output = destinationEntry.Open();
            input.CopyTo(output);
        }
        destinationStream.Flush(flushToDisk: true);
        return expected;
    }

    private static void VerifyRebuiltArchive(string archivePath, string deletedName, IReadOnlyList<EntrySignature> expected)
    {
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        if (archive.Entries.Any(entry => string.Equals(entry.FullName, deletedName, StringComparison.Ordinal)))
            throw new InvalidDataException("再構築したZIPに削除対象の画像が残っています。元ファイルは変更しませんでした。");
        var actual = archive.Entries.Select(entry => new EntrySignature(entry.FullName, entry.Length, entry.Crc32)).ToArray();
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException("再構築したZIPの収録物または内容検査値が一致しません。元ファイルは変更しませんでした。");
    }

    private static bool IsImageEntry(string name) =>
        name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static void ReplaceWithRecovery(string temporaryPath, string sourcePath, string recoveryPath)
    {
        try
        {
            File.Replace(temporaryPath, sourcePath, recoveryPath, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceFallback(temporaryPath, sourcePath, recoveryPath);
        }
        catch (IOException)
        {
            ReplaceFallback(temporaryPath, sourcePath, recoveryPath);
        }
    }

    private static void ReplaceFallback(string temporaryPath, string sourcePath, string recoveryPath)
    {
        File.Copy(sourcePath, recoveryPath, overwrite: false);
        try { File.Move(temporaryPath, sourcePath, overwrite: true); }
        catch
        {
            File.Copy(recoveryPath, sourcePath, overwrite: true);
            throw;
        }
    }

    private static void TrySetAttributes(string path, FileAttributes attributes)
    {
        try { if (File.Exists(path)) File.SetAttributes(path, attributes); } catch { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch { }
    }

    private sealed record EntrySignature(string Name, long Length, uint Crc32);
}
