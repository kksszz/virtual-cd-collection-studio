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

internal sealed record TrackTagUpdate(string FileName, string SourcePath, TrackTagValues Values, string? TargetFileName = null)
{
    public string EffectiveTargetFileName => string.IsNullOrWhiteSpace(TargetFileName) ? FileName : TargetFileName;
}

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
        if (album.Tracks.Any(track => !string.IsNullOrEmpty(track.CuePath)))
            throw new NotSupportedException("CUE音声イメージへのタグ書き込みは行いません。");
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
        var plans = updates.Select(update =>
        {
            var source = Path.GetFullPath(update.SourcePath);
            var targetName = Path.GetFileName(update.EffectiveTargetFileName);
            ValidateTargetName(update.FileName, targetName);
            return new FileWritePlan(update, source, Path.Combine(Path.GetDirectoryName(source)!, targetName));
        }).ToList();
        if (plans.Select(plan => plan.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
            throw new IOException("同じ保存先ファイル名が複数指定されています。");
        var movingSources = plans.Where(plan => plan.RequiresRename)
            .Select(plan => plan.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
            if (File.Exists(plan.Destination)
                && !string.Equals(plan.Source, plan.Destination, StringComparison.OrdinalIgnoreCase)
                && !movingSources.Contains(plan.Destination))
                throw new IOException($"同名のファイルがすでに存在します: {Path.GetFileName(plan.Destination)}");

        var prepared = new List<(FileWritePlan Plan, string Temporary)>();
        var replaced = new List<(string Source, string Recovery)>();
        var renameStaging = new Dictionary<FileWritePlan, string>();
        var placedRenames = new HashSet<FileWritePlan>();
        var persistentBackups = new List<string>();
        var originalAttributes = new Dictionary<FileWritePlan, FileAttributes>();
        var completed = false;
        try
        {
            foreach (var plan in plans)
            {
                if (!File.Exists(plan.Source)) throw new FileNotFoundException("編集する音楽ファイルが見つかりません。", plan.Source);
                originalAttributes[plan] = File.GetAttributes(plan.Source);
                var directory = Path.GetDirectoryName(plan.Source)!;
                var temporary = Path.Combine(directory,
                    $".{Path.GetFileNameWithoutExtension(plan.Source)}.{Guid.NewGuid():N}.tagtmp{Path.GetExtension(plan.Source)}");
                File.Copy(plan.Source, temporary, overwrite: false);
                prepared.Add((plan, temporary));
                MakeWritable(temporary);
                ApplyTags(temporary, plan.Update.Values);
                VerifyTags(temporary, plan.Update.Values);
            }
            if (backupOptions.Enabled)
                foreach (var item in prepared)
                {
                    var backup = CreatePersistentBackupPath(item.Plan.Source, backupOptions.Folder);
                    File.Copy(item.Plan.Source, backup, overwrite: false);
                    persistentBackups.Add(backup);
                }
            foreach (var item in prepared)
            {
                var recovery = CreateRecoveryPath(item.Plan.Source);
                MakeWritable(item.Plan.Source);
                ReplaceWithRecovery(item.Temporary, item.Plan.Source, recovery);
                replaced.Add((item.Plan.Source, recovery));
            }
            // Move every source out of the way before assigning final names. This
            // also supports swaps such as 01.mp3 <-> 02.mp3 without overwriting.
            foreach (var plan in plans.Where(plan => plan.RequiresRename))
            {
                var staging = Path.Combine(Path.GetDirectoryName(plan.Source)!, $".{Path.GetFileName(plan.Source)}.{Guid.NewGuid():N}.renametmp");
                File.Move(plan.Source, staging);
                renameStaging.Add(plan, staging);
            }
            foreach (var (plan, staging) in renameStaging)
            {
                File.Move(staging, plan.Destination);
                placedRenames.Add(plan);
            }
            foreach (var item in replaced) TryDelete(item.Recovery);
            completed = true;
            return new AlbumTagWriteResult(persistentBackups);
        }
        catch
        {
            // Remove edited/renamed copies first, then restore every original from
            // its recovery file so a partial multi-file rename cannot lose data.
            foreach (var (plan, staging) in renameStaging)
            {
                TryDelete(staging);
                if (placedRenames.Contains(plan)) TryDelete(plan.Destination);
            }
            foreach (var item in replaced)
                try { MakeWritable(item.Source); File.Copy(item.Recovery, item.Source, overwrite: true); } catch { }
            foreach (var backup in persistentBackups) TryDelete(backup);
            throw;
        }
        finally
        {
            foreach (var item in prepared) TryDelete(item.Temporary);
            foreach (var staging in renameStaging.Values) TryDelete(staging);
            foreach (var item in replaced) TryDelete(item.Recovery);
            foreach (var plan in plans)
                if (originalAttributes.TryGetValue(plan, out var attributes))
                    TrySetAttributes(completed ? plan.Destination : plan.Source, attributes);
        }
    }

    private static AlbumTagWriteResult WriteArchive(ZipAlbum album, IReadOnlyList<TrackTagUpdate> updates,
        TrackTagBackupOptions backupOptions)
    {
        var sourcePath = album.Path;
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("編集するZIP.MP3が見つかりません。", sourcePath);
        foreach (var update in updates)
        {
            ValidateArchiveTargetName(update.FileName, update.EffectiveTargetFileName);
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
        var resultingNames = source.Entries.Select(entry => updateMap.TryGetValue(entry.FullName, out var update)
            ? update.EffectiveTargetFileName : entry.FullName).ToList();
        if (resultingNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != resultingNames.Count)
            throw new InvalidDataException("ファイル名の変更後にZIP内で同名になる項目があります。");
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
            var destinationName = updateMap.TryGetValue(sourceEntry.FullName, out var renamedUpdate)
                ? renamedUpdate.EffectiveTargetFileName : sourceEntry.FullName;
            var destinationEntry = destination.CreateEntry(destinationName, compression);
            destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
            destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
            if (renamedUpdate is not null)
            {
                using (var input = sourceEntry.Open())
                using (var extracted = new FileStream(temporaryTrack, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(extracted);
                ApplyTags(temporaryTrack, renamedUpdate.Values);
                VerifyTags(temporaryTrack, renamedUpdate.Values);
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
            var edited = rebuilt.Tracks.SingleOrDefault(item => string.Equals(item.FileName, update.EffectiveTargetFileName, StringComparison.Ordinal));
            if (edited is null) throw new InvalidDataException("ZIPの検証で編集した曲を確認できなかったため、元ファイルは変更しませんでした。");
            VerifyTrackValues(edited, update.Values);
        }
    }

    private static void ApplyTags(string path, TrackTagValues values)
    {
        RemoveMalformedPictureFrames(path);
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

    private static int RemoveMalformedPictureFrames(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) != header.Length || !header[..3].SequenceEqual("ID3"u8)) return 0;
        var major = header[3];
        if (major is not (2 or 3 or 4)) return 0;
        var tagSize = SyncSafe(header[6..10]);
        if (tagSize <= 0 || tagSize > 16 * 1024 * 1024 || 10L + tagSize > stream.Length) return 0;
        var body = new byte[tagSize];
        stream.ReadExactly(body);
        var frameStart = GetFrameStart(body, major, header[5]);
        if (frameStart < 0 || frameStart > body.Length) return 0;
        var output = new MemoryStream(body.Length);
        output.Write(body, 0, frameStart);
        var position = frameStart;
        var removed = 0;
        while (position + (major == 2 ? 6 : 10) <= body.Length)
        {
            var idLength = major == 2 ? 3 : 4;
            var id = Encoding.ASCII.GetString(body, position, idLength);
            if (id[0] == '\0') break;
            var size = major switch
            {
                2 => (body[position + 3] << 16) | (body[position + 4] << 8) | body[position + 5],
                4 => SyncSafe(body.AsSpan(position + 4, 4)),
                _ => (body[position + 4] << 24) | (body[position + 5] << 16)
                    | (body[position + 6] << 8) | body[position + 7]
            };
            var headerSize = major == 2 ? 6 : 10;
            if (size < 0 || position + headerSize + size > body.Length) break;
            var malformedPicture = (id == "APIC" || id == "PIC") && size < 5;
            if (malformedPicture) removed++;
            else output.Write(body, position, headerSize + size);
            position += headerSize + size;
        }
        if (removed == 0) return 0;
        while (output.Length < body.Length) output.WriteByte(0);
        stream.Position = 10;
        stream.Write(output.GetBuffer(), 0, body.Length);
        stream.Flush(flushToDisk: true);
        return removed;
    }

    private static int GetFrameStart(ReadOnlySpan<byte> body, byte major, byte flags)
    {
        if ((flags & 0x40) == 0 || body.Length < 4) return 0;
        if (major == 2) return body.Length;
        var size = major == 4 ? SyncSafe(body[..4])
            : ((body[0] << 24) | (body[1] << 16) | (body[2] << 8) | body[3]) + 4;
        return Math.Clamp(size, 0, body.Length);
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes) => bytes.Length < 4 ? 0
        : (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];

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
    private static void MakeWritable(string path)
    {
        if (!File.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
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
            MakeWritable(path);
            File.Delete(path);
        }
        catch { }
    }

    private static void ValidateTargetName(string originalName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName) || targetName is "." or ".."
            || targetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetExtension(originalName), Path.GetExtension(targetName), StringComparison.OrdinalIgnoreCase))
            throw new IOException($"使用できないファイル名、または拡張子の変更が指定されています: {targetName}");
    }

    private static void ValidateArchiveTargetName(string originalName, string targetName)
    {
        var leaf = targetName.Replace('\\', '/').Split('/').LastOrDefault() ?? "";
        ValidateTargetName(Path.GetFileName(originalName), leaf);
        var originalSlash = Math.Max(originalName.LastIndexOf('/'), originalName.LastIndexOf('\\'));
        var targetSlash = Math.Max(targetName.LastIndexOf('/'), targetName.LastIndexOf('\\'));
        var originalDirectory = originalSlash >= 0 ? originalName[..(originalSlash + 1)] : "";
        var targetDirectory = targetSlash >= 0 ? targetName[..(targetSlash + 1)] : "";
        var segments = targetName.Split(['/', '\\']);
        // Legacy ZIP creators often stored backslashes as entry separators.
        // Keep the original directory prefix exactly as-is and only allow the
        // editor to replace the leaf file name.
        if (Path.IsPathRooted(targetName)
            || !string.Equals(originalDirectory, targetDirectory, StringComparison.Ordinal)
            || segments.Any(segment => segment is "" or "." or ".."))
            throw new IOException($"ZIP内で使用できないファイル名が指定されています: {targetName}");
    }

    private sealed record FileWritePlan(TrackTagUpdate Update, string Source, string Destination)
    {
        public bool RequiresRename => !string.Equals(Source, Destination, StringComparison.Ordinal);
    }
}
