using System.IO;

namespace ZipMp3Player;

internal sealed record LibrarySizeSummary(int Albums, int Tracks, long StoredBytes, long AudioBytes, int Unreadable)
{
    internal static string Format(long bytes) => $"{bytes / 1_000_000_000.0:N2} GB ({bytes / 1073741824.0:N2} GiB)";

    internal static LibrarySizeSummary Calculate(IReadOnlyList<ZipAlbum> albums, CancellationToken token)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var albumPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = new HashSet<(string, string)>();
        long stored = 0, audio = 0;
        var unreadable = 0;
        foreach (var album in albums)
        {
            token.ThrowIfCancellationRequested();
            if (!albumPaths.Add(Path.GetFullPath(album.Path))) continue;
            Visit(album.Path);
            foreach (var track in album.Tracks)
            {
                token.ThrowIfCancellationRequested();
                var source = string.IsNullOrEmpty(track.SourcePath) ? album.Path : track.SourcePath;
                var key = (Path.GetFullPath(source).ToUpperInvariant(), track.IsArchiveEntry || !string.IsNullOrEmpty(track.CuePath)
                    ? track.FileName : "");
                if (tracks.Add(key)) audio += Math.Max(0, track.Size);
                Visit(source); // CUE tracks share one ISO/BIN: count that file only once.
            }
        }
        return new(albumPaths.Count, tracks.Count, stored, audio, unreadable);

        void Visit(string path)
        {
            token.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (files.Contains(path) || directories.Contains(path)) return;
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { files.Add(path); unreadable++; return; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(path);
                    foreach (var child in Directory.EnumerateFileSystemEntries(path)) Visit(child);
                }
                else
                {
                    files.Add(path);
                    stored += new FileInfo(path).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { files.Add(path); unreadable++; }
        }
    }
}
