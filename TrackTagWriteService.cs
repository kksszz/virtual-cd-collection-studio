using System.IO.Compression;
using System.IO;
using System.Text;

namespace ZipMp3Player;

internal sealed record TrackTagValues(
    string Title,
    string Artist,
    string Album,
    uint Year,
    string Genre,
    uint TrackNumber,
    uint DiscNumber,
    uint DiscCount);

internal sealed record TrackTagUpdate(string FileName, string SourcePath, TrackTagValues Values);

internal sealed record AlbumTagWriteResult(IReadOnlyList<string> BackupPaths);

internal sealed record TrackTagBackupOptions(bool Enabled, string Folder);

internal static class TrackTagWriteService
{
    static TrackTagWriteService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static AlbumTagWriteResult WriteAlbum(ZipAlbum album, IReadOnlyList<TrackTagUpdate> updates,
        TrackTagBackupOptions? backupOptions = null)
    {
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(updates);
        backupOptions ??= new TrackTagBackupOptions(false, "");
        if (backupOptions.Enabled)
        {
            if (string.IsNullOrWhiteSpace(backupOptions.Folder)) throw new InvalidOperationException("タグバックアップの保存フォルダが指定されていません。");
            Directory.CreateDirectory(backupOptions.Folder);
        }
        if (updates.Count == 0) return new AlbumTagWriteResult([]);
        if (album.Tracks.FirstOrDefault()?.IsArchiveEntry == true)
            return WriteArchive(album, updates, backupOptions);

        return WriteFiles(updates, backupOptions);
    }

    private static AlbumTagWriteResult WriteFiles(IReadOnlyList<TrackTagUpdate> updates, TrackTagBackupOptions backupOptions)
    {
        var prepared = new List<(string Source, string Temporary)>();
        var replaced = new List<(string Source, string Recovery)>();
        var persistentBackups = new List<string>();
        try
        {
            foreach (var update in updates)
            {
                if (!File.Exists(update.SourcePath)) throw new FileNotFoundException("編集する音楽ファイルが見つかりません。", update.SourcePath);
                var directory = Path.GetDirectoryName(Path.GetFullPath(update.SourcePath))!;
                var temporary = Path.Combine(directory,
                    $".{Path.GetFileNameWithoutExtension(update.SourcePath)}.{Guid.NewGuid():N}.tagtmp{Path.GetExtension(update.SourcePath)}");
                File.Copy(update.SourcePath, temporary, overwrite: false);
                prepared.Add((update.SourcePath, temporary));
                ApplyTags(temporary, update.Values);
                VerifyTags(temporary, update.Values);
            }
            if (backupOptions.Enabled)
                foreach (var item in prepared)
                {
                    var backup = CreatePersistentBackupPath(item.Source, backupOptions.Folder);
                    File.Copy(item.Source, backup, overwrite: false);
                    persistentBackups.Add(backup);
                }
            foreach (var item in prepared)
            {
                var recovery = CreateRecoveryPath(item.Source);
                ReplaceWithRecovery(item.Temporary, item.Source, recovery);
                replaced.Add((item.Source, recovery));
            }
            foreach (var item in replaced) TryDelete(item.Recovery);
            return new AlbumTagWriteResult(persistentBackups);
        }
        catch
        {
            foreach (var item in replaced.AsEnumerable().Reverse())
                try { File.Copy(item.Recovery, item.Source, overwrite: true); } catch { }
            foreach (var backup in persistentBackups) TryDelete(backup);
            throw;
        }
        finally
        {
            foreach (var item in prepared) TryDelete(item.Temporary);
            foreach (var item in replaced) TryDelete(item.Recovery);
        }
    }

    private static AlbumTagWriteResult WriteArchive(ZipAlbum album, IReadOnlyList<TrackTagUpdate> updates,
        TrackTagBackupOptions backupOptions)
    {
        var sourcePath = album.Path;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("編集するZIP.MP3が見つかりません。", sourcePath);
        foreach (var update in updates)
        {
            var track = album.Tracks.SingleOrDefault(item => string.Equals(item.FileName, update.FileName, StringComparison.Ordinal));
            if (track is null) throw new InvalidOperationException($"編集対象がアルバム内に見つかりません: {update.FileName}");
            if (track.IsEncrypted) throw new InvalidOperationException($"暗号化されたZIP内の曲は編集できません: {update.FileName}");
            if (track.CompressionMethod is not (0 or 8))
                throw new InvalidOperationException($"ZIP圧縮方式 {track.CompressionMethod} の曲は編集できません: {update.FileName}\nStoreまたはDeflate方式へ変更してください。");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var temporaryArchive = Path.Combine(directory, $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.tagtmp.zip");
        var temporaryTrack = Path.Combine(Path.GetTempPath(), $"ZipMp3Player-{Guid.NewGuid():N}.mp3");
        string? persistentBackup = null;
        var recovery = CreateRecoveryPath(sourcePath);
        try
        {
            RebuildArchive(album, updates, sourcePath, temporaryArchive, temporaryTrack);
            VerifyArchive(album, updates, temporaryArchive);
            if (backupOptions.Enabled)
            {
                persistentBackup = CreatePersistentBackupPath(sourcePath, backupOptions.Folder);
                File.Copy(sourcePath, persistentBackup, overwrite: false);
            }
            ReplaceWithRecovery(temporaryArchive, sourcePath, recovery);
            TryDelete(recovery);
            return new AlbumTagWriteResult(persistentBackup is null ? [] : [persistentBackup]);
        }
        catch
        {
            if (persistentBackup is not null) TryDelete(persistentBackup);
            throw;
        }
        finally
        {
            TryDelete(temporaryTrack);
            TryDelete(temporaryArchive);
            TryDelete(recovery);
        }
    }

    private static void RebuildArchive(ZipAlbum album, IReadOnlyList<TrackTagUpdate> updates,
        string sourcePath, string destinationPath, string temporaryTrack)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false, Encoding.GetEncoding(932));
        var updateMap = updates.ToDictionary(update => update.FileName, StringComparer.Ordinal);
        foreach (var update in updates)
        {
            var count = source.Entries.Count(entry => string.Equals(entry.FullName, update.FileName, StringComparison.Ordinal));
            if (count != 1)
                throw new InvalidDataException(count == 0
                    ? $"ZIP内で編集対象の曲を見つけられませんでした: {update.FileName}"
                    : $"ZIP内に同名の曲が複数あるため、安全に編集できません: {update.FileName}");
        }

        using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        // Read legacy non-UTF8 names as CP932, then write every name as UTF-8 so Greek numerals and other Unicode are retained.
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var sourceEntry in source.Entries)
        {
            var knownMethod = FindCompressionMethod(album, sourceEntry.FullName);
            var compression = knownMethod == 0 || (knownMethod is null && sourceEntry.Length == sourceEntry.CompressedLength)
                ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var destinationEntry = destination.CreateEntry(sourceEntry.FullName, compression);
            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
            destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
            if (updateMap.TryGetValue(sourceEntry.FullName, out var update))
            {
                using (var input = sourceEntry.Open())
                using (var extracted = new FileStream(temporaryTrack, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(extracted);
                ApplyTags(temporaryTrack, update.Values);
                VerifyTags(temporaryTrack, update.Values);
                using var edited = new FileStream(temporaryTrack, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = destinationEntry.Open();
                edited.CopyTo(output);
            }
            else
            {
                using var input = sourceEntry.Open();
                using var output = destinationEntry.Open();
                input.CopyTo(output);
            }
        }
    }

    private static int? FindCompressionMethod(ZipAlbum album, string fileName)
    {
        var track = album.Tracks.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
        if (track is not null) return track.CompressionMethod;
        var image = album.Images.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
        if (image is not null) return image.CompressionMethod;
        var text = album.TextFiles.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.Ordinal));
        return text?.CompressionMethod;
    }

    private static void VerifyArchive(ZipAlbum original, IReadOnlyList<TrackTagUpdate> updates, string archivePath)
    {
        var rebuilt = ZipAlbumReader.Open(archivePath);
        if (rebuilt.Tracks.Count != original.Tracks.Count || rebuilt.Images.Count != original.Images.Count || rebuilt.TextFiles.Count != original.TextFiles.Count)
            throw new InvalidDataException("ZIPの検証で収録ファイル数の不一致を検出したため、元ファイルは変更しませんでした。");
        foreach (var update in updates)
        {
            var edited = rebuilt.Tracks.SingleOrDefault(item => string.Equals(item.FileName, update.FileName, StringComparison.Ordinal));
            if (edited is null) throw new InvalidDataException("ZIPの検証で編集した曲を確認できなかったため、元ファイルは変更しませんでした。");
            VerifyTrackValues(edited, update.Values);
        }
    }

    private static void ApplyTags(string path, TrackTagValues values)
    {
        using var file = TagLib.File.Create(path);
        file.Tag.Title = NullIfBlank(values.Title);
        file.Tag.Performers = string.IsNullOrWhiteSpace(values.Artist) ? [] : [values.Artist.Trim()];
        file.Tag.Album = NullIfBlank(values.Album);
        file.Tag.Year = values.Year;
        file.Tag.Genres = string.IsNullOrWhiteSpace(values.Genre) ? [] : [values.Genre.Trim()];
        file.Tag.Track = values.TrackNumber;
        file.Tag.Disc = values.DiscNumber;
        file.Tag.DiscCount = values.DiscCount;
        file.Save();
    }

    private static void VerifyTags(string path, TrackTagValues values)
    {
        using var file = TagLib.File.Create(path);
        if (!Same(file.Tag.Title, values.Title)
            || !Same(file.Tag.FirstPerformer, values.Artist)
            || !Same(file.Tag.Album, values.Album)
            || file.Tag.Year != values.Year
            || !Same(file.Tag.FirstGenre, values.Genre)
            || file.Tag.Track != values.TrackNumber
            || file.Tag.Disc != values.DiscNumber
            || file.Tag.DiscCount != values.DiscCount)
            throw new InvalidDataException("書き込んだタグを再読込して確認できませんでした。元ファイルは変更していません。");
    }

    private static void VerifyTrackValues(ZipTrack track, TrackTagValues values)
    {
        if (!Same(track.Title, values.Title) || !Same(track.Artist, values.Artist) || !Same(track.Album, values.Album)
            || !Same(track.Year, values.Year == 0 ? "" : values.Year.ToString()) || !Same(track.Genre, values.Genre)
            || track.TrackNumber != (int)values.TrackNumber || track.DiscNumber != (int)values.DiscNumber
            || track.DiscCount != (int)values.DiscCount)
            throw new InvalidDataException($"再構築したZIPから編集内容を確認できませんでした。元ファイルは変更していません。\n"
                + $"確認値: タイトル={track.Title}, アーティスト={track.Artist}, アルバム={track.Album}, 年={track.Year}, ジャンル={track.Genre}, 曲={track.TrackNumber}, Disc={track.DiscNumber}/{track.DiscCount}");
    }

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

    private static void ReplaceFallback(string temporaryPath, string sourcePath, string backup)
    {
        File.Copy(sourcePath, backup, overwrite: false);
        try
        {
            File.Move(temporaryPath, sourcePath, overwrite: true);
        }
        catch
        {
            File.Copy(backup, sourcePath, overwrite: true);
            throw;
        }
    }

    private static string CreateRecoveryPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        return Path.Combine(directory, $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.tagrollback");
    }

    private static string CreatePersistentBackupPath(string sourcePath, string backupFolder)
    {
        var name = Path.GetFileName(sourcePath);
        for (var index = 0; index < 1000; index++)
        {
            var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + (index == 0 ? "" : $"-{index}");
            var candidate = Path.Combine(backupFolder, $"{name}.tag-backup-{suffix}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("バックアップファイル名を作成できませんでした。");
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim() ?? "", right?.Trim() ?? "", StringComparison.Ordinal);
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
