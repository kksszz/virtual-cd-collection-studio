using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZipMp3Player;

internal sealed record CollectionBackupSource(string Id, string OriginalPath);
internal sealed record CollectionBackupFile(string Path, long Length, string Sha256);
internal sealed record CollectionBackupManifest(int Version, DateTimeOffset Created, List<CollectionBackupSource> Sources,
    List<CollectionBackupFile> Files, List<string> Directories);
internal sealed record CollectionBackupProgress(int Completed, int Total, string Path);
internal sealed record CollectionBackupResult(string Path, int Files, long Bytes);

/// <summary>A portable, verified snapshot. Extraction never overwrites the active installation or music.</summary>
internal static class CollectionBackupService
{
    private const string ManifestName = "collection-backup.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static IReadOnlyList<CollectionBackupSource> Sources(string dataDirectory, string applicationDirectory,
        bool includeData, bool includeApplication, IReadOnlyList<string> libraryFolders)
    {
        var sources = new List<CollectionBackupSource>();
        if (includeData) sources.Add(new("Data", Path.GetFullPath(dataDirectory)));
        if (includeApplication) sources.Add(new("Application", Path.GetFullPath(applicationDirectory)));
        for (var i = 0; i < libraryFolders.Count; i++)
            sources.Add(new($"Libraries/{i + 1:D4}", Path.GetFullPath(libraryFolders[i])));
        if (sources.Count == 0) throw new InvalidOperationException("バックアップ対象を選択してください。");
        if (sources.Select(source => source.OriginalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new InvalidOperationException("同じフォルダーが複数の対象に選択されています。");
        return sources;
    }

    private static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.Equals(basePath, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureOrdinary(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("リンク／ジャンクションはバックアップできません: " + path);
    }

    private static bool IncludeFile(string id, string path)
    {
        if (id.StartsWith("Libraries/", StringComparison.Ordinal)) return true;
        var name = Path.GetFileName(path);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zipmp3backup", StringComparison.OrdinalIgnoreCase)) return false;
        if (id == "Application" && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IncludeDirectory(string id, string path)
    {
        var name = Path.GetFileName(path);
        return id != "Data" || !name.Equals("restore-safety", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("thumbnail-cache", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("restore-staging-", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record PendingFile(string Source, string Entry, long Length, long Modified);
    private sealed record InventoryResult(List<PendingFile> Files, List<string> Directories);
    private static InventoryResult Inventory(IReadOnlyList<CollectionBackupSource> sources, string destination)
    {
        var result = new List<PendingFile>();
        var directories = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var root = source.OriginalPath;
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("対象フォルダーが見つかりません: " + root);
            EnsureOrdinary(root);
            if (IsUnder(destination, root)) throw new InvalidOperationException("バックアップ先は対象フォルダーの外を選択してください: " + root);
            var pending = new Stack<string>(); pending.Push(root);
            directories.Add(source.Id + "/");
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                EnsureOrdinary(directory);
                foreach (var sub in Directory.EnumerateDirectories(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    EnsureOrdinary(sub);
                    if (IncludeDirectory(source.Id, sub))
                    {
                        directories.Add(source.Id + "/" + Path.GetRelativePath(root, sub).Replace('\\', '/') + "/");
                        pending.Push(sub);
                    }
                }
                foreach (var file in Directory.EnumerateFiles(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    EnsureOrdinary(file);
                    if (!IncludeFile(source.Id, file)) continue;
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (relative is "." or ".." || relative.StartsWith("../", StringComparison.Ordinal)
                        || relative.Split('/').Any(part => part is "." or ".." or ""))
                        throw new InvalidDataException("対象内の不正なパスです: " + file);
                    var entry = source.Id + "/" + relative;
                    if (!names.Add(entry)) throw new InvalidDataException("重複するバックアップ項目です: " + entry);
                    var info = new FileInfo(file);
                    result.Add(new(file, entry, info.Length, info.LastWriteTimeUtc.Ticks));
                }
            }
        }
        return new(result, directories);
    }

    internal static (int Files, long Bytes) Estimate(IReadOnlyList<CollectionBackupSource> sources, string destination)
    {
        var files = Inventory(sources, destination);
        return (files.Files.Count, checked(files.Files.Sum(file => file.Length)));
    }

    internal static CollectionBackupResult Create(IReadOnlyList<CollectionBackupSource> sources, string destination,
        CompressionLevel compression, IProgress<CollectionBackupProgress>? progress = null, CancellationToken cancellation = default)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("バックアップ先は既存ファイルを上書きしません: " + destination);
        var files = Inventory(sources, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            var recorded = new List<CollectionBackupFile>(files.Files.Count);
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var directory in files.Directories) archive.CreateEntry(directory);
                for (var i = 0; i < files.Files.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var file = files.Files[i];
                    progress?.Report(new(i, files.Files.Count, file.Entry));
                    var entry = archive.CreateEntry(file.Entry, compression);
                    using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length != file.Length || File.GetLastWriteTimeUtc(file.Source).Ticks != file.Modified)
                        throw new IOException("バックアップ中に元ファイルが変更されました: " + file.Source);
                    using var entryOutput = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1024 * 1024];
                    int read;
                    long copied = 0;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        entryOutput.Write(buffer, 0, read); hash.AppendData(buffer, 0, read); copied += read;
                    }
                    if (copied != file.Length || File.GetLastWriteTimeUtc(file.Source).Ticks != file.Modified)
                        throw new IOException("バックアップ中に元ファイルが変更されました: " + file.Source);
                    recorded.Add(new(file.Entry, copied, Convert.ToHexString(hash.GetHashAndReset())));
                }
                var manifest = new CollectionBackupManifest(2, DateTimeOffset.Now, sources.ToList(), recorded, files.Directories);
                using var writer = new StreamWriter(archive.CreateEntry(ManifestName, CompressionLevel.Optimal).Open());
                writer.Write(JsonSerializer.Serialize(manifest, Json));
            }
            Validate(partial, cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(partial, destination);
            progress?.Report(new(files.Files.Count, files.Files.Count, "完了"));
            return new(destination, files.Files.Count, recorded.Sum(file => file.Length));
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static bool SafeEntry(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':')) return false;
        return path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    }

    internal static CollectionBackupManifest Validate(string archivePath, CancellationToken cancellation = default)
    {
        using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var manifestEntries = archive.Entries.Where(entry => entry.FullName == ManifestName).ToArray();
        if (manifestEntries.Length != 1 || manifestEntries[0].Length > 64L * 1024 * 1024)
            throw new InvalidDataException("完全バックアップの目録が見つからないか、大きすぎます。");
        var manifestEntry = manifestEntries[0];
        CollectionBackupManifest manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<CollectionBackupManifest>(reader.ReadToEnd())
                ?? throw new InvalidDataException("バックアップの目録を読み取れません。");
        if (manifest.Version != 2 || manifest.Sources is null || manifest.Files is null || manifest.Directories is null)
            throw new InvalidDataException("対応していないバックアップ形式です。");
        if (manifest.Sources.Select(source => source.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Sources.Count
            || manifest.Sources.Any(source => !SafeEntry(source.Id) || source.Id == ManifestName))
            throw new InvalidDataException("目録に不正な対象名があります。");
        if (manifest.Directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Directories.Count
            || manifest.Directories.Any(directory => !directory.EndsWith('/') || !SafeEntry(directory.TrimEnd('/'))
                || !manifest.Sources.Any(source => directory.Equals(source.Id + "/", StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(source.Id + "/", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("目録に不正なフォルダーがあります。");
        var expected = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        if (expected.Count != manifest.Files.Count || archive.Entries.Count != expected.Count + manifest.Directories.Count + 1)
            throw new InvalidDataException("バックアップの項目数が一致しません。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.FullName == ManifestName) continue;
            if (!seen.Add(entry.FullName)) throw new InvalidDataException("バックアップ内に重複した項目があります。");
            if (manifest.Directories.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.FullName.EndsWith('/') || entry.Length != 0)
                    throw new InvalidDataException("フォルダー項目が不正です: " + entry.FullName);
                continue;
            }
            if (!SafeEntry(entry.FullName) || !expected.TryGetValue(entry.FullName, out var file)
                || file.Length < 0 || entry.Length != file.Length
                || !manifest.Sources.Any(source => entry.FullName.StartsWith(source.Id + "/", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("目録にない／不正なファイルです: " + entry.FullName);
            using var contents = entry.Open();
            var digest = Convert.ToHexString(SHA256.HashData(contents));
            if (!digest.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("バックアップの内容が破損しています: " + entry.FullName);
        }
        return manifest;
    }

    internal static CollectionBackupResult RestoreToNewFolder(string archivePath, string destination,
        IProgress<CollectionBackupProgress>? progress = null, CancellationToken cancellation = default)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("復元先には新しい空のフォルダー名を指定してください: " + destination);
        var manifest = Validate(archivePath, cancellation);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.CreateDirectory(staging);
        try
        {
            using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            foreach (var directory in manifest.Directories)
            {
                var folder = Path.GetFullPath(Path.Combine(staging, directory.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnder(folder, staging)) throw new InvalidDataException("不正な復元先フォルダーです。");
                Directory.CreateDirectory(folder);
            }
            for (var i = 0; i < manifest.Files.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var file = manifest.Files[i];
                progress?.Report(new(i, manifest.Files.Count, file.Path));
                var target = Path.GetFullPath(Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnder(target, staging) || target.Equals(staging, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("バックアップのパスが復元先の外を指します。");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var entry = archive.GetEntry(file.Path)!.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long copied = 0;
                int read;
                while ((read = entry.Read(buffer)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read); hash.AppendData(buffer, 0, read); copied += read;
                }
                if (copied != file.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("復元中にバックアップが変化しました: " + file.Path);
            }
            cancellation.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(staging, "RESTORE-README.txt"),
                "Virtual CD Collection Studio complete backup\n" +
                "Data: application settings and managed data. Application: portable app files. Libraries: registered music folder snapshots.\n" +
                "Nothing was overwritten in your current installation. To use on this PC, review the restored files and register the new library paths in Settings.\n");
            Directory.Move(staging, destination);
            progress?.Report(new(manifest.Files.Count, manifest.Files.Count, "完了"));
            return new(destination, manifest.Files.Count, manifest.Files.Sum(file => file.Length));
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
}
