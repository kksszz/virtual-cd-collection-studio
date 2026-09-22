using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

// An explicit marker scopes recursive album loading to converted albums only.
internal static class FolderAlbumLayout
{
    internal const string MarkerName = ".vccs-folder-album.json";
    internal sealed record Marker(int Version, string SourceName, long SourceLength, long SourceWriteTicks, bool ReplacementReady = false);
    internal static bool IsTemporary(string path) => path.Replace('\\', '/').Split('/')
        .Any(part => Regex.IsMatch(part, @"^\.zip-folder-[0-9a-f]{32}$", RegexOptions.IgnoreCase));
    internal static Marker? Read(string root)
    {
        try
        {
            var path = Path.Combine(root, MarkerName);
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(path));
            return marker?.Version == 1 ? marker : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    internal static bool IsRoot(string root) => Read(root) is not null;
    internal static string RootFor(string directory)
    {
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (IsRoot(parent.FullName)) return parent.FullName;
        return directory;
    }
    internal static bool IsSupersededArchive(string path)
    {
        if (!path.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)) return false;
        var marker = Read(path[..^8]);
        if (marker is null || !marker.ReplacementReady || !string.Equals(marker.SourceName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var source = new FileInfo(path);
            return source.Exists && source.Length == marker.SourceLength && source.LastWriteTimeUtc.Ticks == marker.SourceWriteTicks;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    internal static void Write(string directory, string source)
    {
        var info = new FileInfo(source);
        var path = Path.Combine(directory, MarkerName);
        // Preserve a marker from an earlier round trip, but never overwrite an unrelated file.
        if (File.Exists(path) && Read(directory) is null) throw new IOException("アルバム管理ファイルと同名の収録物があります。元ZIPは保持しています。");
        File.WriteAllText(path, JsonSerializer.Serialize(new Marker(1, info.Name, info.Length, info.LastWriteTimeUtc.Ticks)));
    }
    internal static int DiscFromPath(string relativePath)
    {
        var match = Regex.Match(relativePath.Replace('\\', '/'), @"(?:^|/)(?:Disc|CD)[ _-]*0*(\d+)(?:/|$)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var disc) ? disc : 0;
    }
}
