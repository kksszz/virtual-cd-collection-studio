using System.IO;
using System.IO.Compression;
using System.Text;

namespace ZipMp3Player;

internal sealed record ArtworkRenameRequest(string SourcePath, string? ArchiveEntry, string NewBaseName);
internal sealed record ArtworkRenameResult(IReadOnlyDictionary<string, string> RoleKeys, IReadOnlyList<string> ArchiveBackups);

internal static class ArtworkBatchRenameService
{
    static ArtworkBatchRenameService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private sealed record RenamePlan(string Source, string? Entry, string Target, string OldRoleKey, string NewRoleKey);
    private sealed record EntrySignature(string Name, long Length, uint Crc);

    internal static void ValidateRequests(IReadOnlyList<ArtworkRenameRequest> requests)
    {
        var plans = requests.Select(Plan).Where(plan => plan.Target != (plan.Entry ?? plan.Source)).ToArray();
        Validate(plans);
    }

    internal static ArtworkRenameResult Apply(IReadOnlyList<ArtworkRenameRequest> requests)
    {
        var plans = requests.Select(Plan).Where(plan => plan.Target != (plan.Entry ?? plan.Source)).ToArray();
        if (plans.Length == 0) return new ArtworkRenameResult(new Dictionary<string, string>(), []);
        Validate(plans);

        var archiveGroups = plans.Where(plan => plan.Entry is not null)
            .GroupBy(plan => plan.Source, StringComparer.OrdinalIgnoreCase).ToArray();
        var filePlans = plans.Where(plan => plan.Entry is null).ToArray();
        var archiveTemps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archiveBackups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileTemps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var committedArchives = new List<string>();
        try
        {
            foreach (var group in archiveGroups)
            {
                var temp = Path.Combine(Path.GetDirectoryName(group.Key)!, $".{Path.GetFileName(group.Key)}.{Guid.NewGuid():N}.renametmp");
                archiveTemps.Add(group.Key, temp);
                RebuildArchive(group.Key, temp, group.ToArray());
            }
            // Move every source aside first. This permits swaps and cycles without overwriting data.
            foreach (var plan in filePlans)
            {
                var temp = Path.Combine(Path.GetDirectoryName(plan.Source)!, $".{Guid.NewGuid():N}.rename-pending");
                File.Move(plan.Source, temp);
                fileTemps.Add(plan.Source, temp);
            }
            foreach (var group in archiveGroups)
            {
                var backup = Path.Combine(Path.GetDirectoryName(group.Key)!, $".{Path.GetFileName(group.Key)}.{Guid.NewGuid():N}.rename-backup");
                File.Copy(group.Key, backup);
                File.Move(archiveTemps[group.Key], group.Key, true);
                archiveBackups.Add(group.Key, backup);
                committedArchives.Add(group.Key);
            }
            foreach (var plan in filePlans)
                File.Move(fileTemps[plan.Source], plan.Target);
            return new ArtworkRenameResult(plans.ToDictionary(plan => plan.OldRoleKey, plan => plan.NewRoleKey,
                StringComparer.OrdinalIgnoreCase), archiveBackups.Values.ToArray());
        }
        catch
        {
            var rollbackErrors = new List<Exception>();
            // Re-stage committed targets before restoring source names; swaps remain collision-free.
            foreach (var plan in filePlans)
                if (fileTemps.TryGetValue(plan.Source, out var temp) && File.Exists(plan.Target) && !File.Exists(temp))
                    try { File.Move(plan.Target, temp); } catch (Exception error) { rollbackErrors.Add(error); }
            foreach (var pair in fileTemps)
                if (File.Exists(pair.Value))
                    try { File.Move(pair.Value, pair.Key); } catch (Exception error) { rollbackErrors.Add(error); }
            foreach (var source in committedArchives)
                try
                {
                    File.Copy(archiveBackups[source], source, true);
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("画像名の変更に失敗し、自動復元も完了しませんでした。一時ファイルとZIPバックアップを確認してください。", rollbackErrors);
            throw;
        }
        finally
        {
            foreach (var temp in archiveTemps.Values)
                if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static RenamePlan Plan(ArtworkRenameRequest request)
    {
        var source = Path.GetFullPath(request.SourcePath);
        var original = request.ArchiveEntry ?? source;
        var leaf = Path.GetFileName(original.Replace('/', Path.DirectorySeparatorChar));
        var extension = Path.GetExtension(leaf);
        if (extension is not (".jpg" or ".jpeg" or ".png")
            && !new[] { ".jpg", ".jpeg", ".png" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"画像の拡張子を判定できません: {original}");
        var name = request.NewBaseName;
        ValidateBaseName(name);
        var newLeaf = name + extension;
        var target = request.ArchiveEntry is null
            ? Path.Combine(Path.GetDirectoryName(source)!, newLeaf)
            : request.ArchiveEntry[..^leaf.Length] + newLeaf;
        var oldKey = request.ArchiveEntry is null ? $"file:{source}" : $"zip:{request.ArchiveEntry}";
        var newKey = request.ArchiveEntry is null ? $"file:{target}" : $"zip:{target}";
        return new RenamePlan(source, request.ArchiveEntry, target, oldKey, newKey);
    }

    internal static void ValidateBaseName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Any(character => character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            || name.Split('.')[0].ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9")
            throw new InvalidOperationException($"使用できない画像名です: {name}");
    }

    private static void Validate(IReadOnlyList<RenamePlan> plans)
    {
        if (plans.GroupBy(plan => plan.OldRoleKey, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidOperationException("同じ画像が複数回指定されています。");
        foreach (var fileGroup in plans.Where(plan => plan.Entry is null)
            .GroupBy(plan => Path.GetDirectoryName(plan.Source)!, StringComparer.OrdinalIgnoreCase))
        {
            var sources = fileGroup.Select(plan => plan.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (fileGroup.GroupBy(plan => plan.Target, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                throw new InvalidOperationException("変更後のファイル名が重複しています。");
            foreach (var plan in fileGroup)
            {
                if (!File.Exists(plan.Source)) throw new FileNotFoundException("画像が見つかりません。", plan.Source);
                if (File.Exists(plan.Target) && !sources.Contains(plan.Target))
                    throw new IOException($"同名のファイルが既にあります: {plan.Target}");
            }
        }
        foreach (var group in plans.Where(plan => plan.Entry is not null)
            .GroupBy(plan => plan.Source, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(group.Key)) throw new FileNotFoundException("ZIPが見つかりません。", group.Key);
            using var stream = File.OpenRead(group.Key);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.GetEncoding(932));
            var names = archive.Entries.Select(entry => entry.FullName).ToArray();
            var oldNames = group.Select(plan => plan.Entry!).ToHashSet(StringComparer.Ordinal);
            if (oldNames.Any(name => names.Count(existing => existing == name) != 1))
                throw new InvalidDataException("ZIP内の変更対象を一意に特定できません。");
            var targets = group.Select(plan => plan.Target).ToArray();
            if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length
                || targets.Any(target => names.Any(existing => !oldNames.Contains(existing)
                    && string.Equals(existing, target, StringComparison.OrdinalIgnoreCase))))
                throw new IOException("ZIP内に変更後と同名のファイルがあります。");
        }
    }

    private static void RebuildArchive(string sourcePath, string destinationPath, IReadOnlyList<RenamePlan> plans)
    {
        var mapping = plans.ToDictionary(plan => plan.Entry!, plan => plan.Target, StringComparer.Ordinal);
        var expected = new List<EntrySignature>();
        using (var input = File.OpenRead(sourcePath))
        using (var source = new ZipArchive(input, ZipArchiveMode.Read, false, Encoding.GetEncoding(932)))
        using (var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var rebuilt = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
            foreach (var entry in source.Entries)
            {
                var target = mapping.TryGetValue(entry.FullName, out var replacement) ? replacement : entry.FullName;
                expected.Add(new EntrySignature(target, entry.Length, entry.Crc32));
                var compression = sourcePath.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
                    || entry.Length == entry.CompressedLength ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var result = rebuilt.CreateEntry(target, compression);
                result.LastWriteTime = entry.LastWriteTime;
                result.ExternalAttributes = entry.ExternalAttributes;
                using var from = entry.Open();
                using var to = result.Open();
                from.CopyTo(to);
            }
        using var checkStream = File.OpenRead(destinationPath);
        using var check = new ZipArchive(checkStream, ZipArchiveMode.Read, false, Encoding.UTF8);
        var actual = check.Entries.Select(entry => new EntrySignature(entry.FullName, entry.Length, entry.Crc32)).ToArray();
        if (!actual.SequenceEqual(expected)) throw new InvalidDataException("再構築後のZIPの画像名または内容が一致しません。元ファイルは変更しませんでした。");
    }
}
