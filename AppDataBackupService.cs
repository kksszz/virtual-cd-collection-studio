using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ZipMp3Player;

internal static class AppDataBackupService
{
    private static readonly string[] CoreFiles = ["settings.json", "library.json", "usage.json", "favorites.json", "library-events.json"];
    private static readonly string[] CoreDirectories = ["artwork", "lyrics", "cue-metadata"];

    public static void CreateBackup(string dataDirectory, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry("backup-info.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifest.Open()))
            writer.Write(JsonSerializer.Serialize(new { Version = 1, CreatedLocal = DateTimeOffset.Now, App = "Virtual CD Collection Studio" }));

        foreach (var name in CoreFiles)
        {
            var path = Path.Combine(dataDirectory, name);
            if (File.Exists(path)) archive.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
        }
        foreach (var directoryName in CoreDirectories)
        {
            var directory = Path.Combine(dataDirectory, directoryName);
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(dataDirectory, path).Replace('\\', '/');
                archive.CreateEntryFromFile(path, relative, CompressionLevel.Optimal);
            }
        }
    }

    public static string RestoreBackup(string dataDirectory, string backupPath)
    {
        var safetyDirectory = Path.Combine(dataDirectory, "restore-safety");
        Directory.CreateDirectory(safetyDirectory);
        var safetyPath = Path.Combine(safetyDirectory, $"BeforeRestore-{DateTime.Now:yyyyMMdd-HHmmss}.zipmp3backup");
        CreateBackup(dataDirectory, safetyPath);

        var staging = Path.Combine(dataDirectory, "restore-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var input = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            {
                if (archive.GetEntry("backup-info.json") is null)
                    throw new InvalidDataException("Virtual CD Collection Studio のバックアップファイルではありません。");
                long totalSize = 0;
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (string.IsNullOrWhiteSpace(entry.Name) || name == "backup-info.json") continue;
                    if (!IsAllowedEntry(name)) throw new InvalidDataException($"バックアップ内に未対応の項目があります: {name}");
                    totalSize += entry.Length;
                    if (totalSize > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("バックアップの展開サイズが大きすぎます。");
                    var destination = GetSafeDestination(staging, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            foreach (var name in CoreFiles)
            {
                var source = Path.Combine(staging, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(dataDirectory, name), overwrite: true);
            }
            foreach (var directoryName in CoreDirectories)
            {
                var sourceDirectory = Path.Combine(staging, directoryName);
                if (Directory.Exists(sourceDirectory))
                    CopyDirectory(sourceDirectory, Path.Combine(dataDirectory, directoryName), overwrite: true);
            }
            return safetyPath;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite = false)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, source);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (overwrite || !File.Exists(destination)) File.Copy(source, destination, overwrite);
        }
    }

    private static bool IsAllowedEntry(string name) => CoreFiles.Contains(name, StringComparer.OrdinalIgnoreCase)
        || CoreDirectories.Any(directory => name.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase));

    private static string GetSafeDestination(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("バックアップ内の不正なパスを検出しました。");
        return destination;
    }
}

internal sealed record FolderRelocation(string OldPath, string NewPath);
