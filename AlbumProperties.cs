using System.IO;

namespace ZipMp3Player;

internal sealed record AlbumPropertyRow(string Label, string Value);
internal sealed record AlbumProperties(string SourcePath, bool Exists, IReadOnlyList<AlbumPropertyRow> Rows)
{
    public static AlbumProperties Load(ZipAlbum album)
    {
        var rows = new List<AlbumPropertyRow>();
        void Add(string ja, string en, string value) => rows.Add(new(LocalizationService.Select(ja, en), value));
        string Values(Func<ZipTrack, string> selector) => string.Join(" / ", album.Tracks.Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        var path = album.Path;
        var exists = false;
        var folder = !album.Tracks.Any(track => track.IsArchiveEntry);
        Add("アルバム", "Album", Values(track => track.Album));
        Add("アーティスト", "Artist", Values(track => track.Artist));
        Add("登録パス", "Registered path", path);
        try
        {
            path = Path.GetFullPath(path);
            folder = Directory.Exists(path) || (folder && !File.Exists(path));
            Add("名前", "Name", Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
            Add("種類", "Type", folder ? LocalizationService.Select("音楽フォルダ", "Music folder") : "ZIP / ZIP.MP3");
            Add("場所", "Location", Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? path);
            FileSystemInfo info = folder ? new DirectoryInfo(path) : new FileInfo(path);
            info.Refresh();
            exists = info.Exists;
            Add("状態", "Status", exists ? LocalizationService.Select("利用可能", "Available")
                : LocalizationService.Select("元のファイルまたはフォルダが見つかりません", "Source file or folder not found"));
            if (exists)
            {
                Add("作成日時", "Created", info.CreationTime.ToString("yyyy/MM/dd HH:mm:ss"));
                Add("更新日時", "Modified", info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
                Add("属性", "Attributes", info.Attributes.ToString());
                if (info is FileInfo file) Add("ファイルサイズ", "File size", FormatSize(file.Length));
                else
                {
                    // Only the registered audio files: no recursive scan of unrelated
                    // folders, artwork, junctions or network directory trees.
                    long total = 0;
                    var unavailable = 0;
                    foreach (var source in album.Tracks.Select(track => track.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try { total = checked(total + new FileInfo(source).Length); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                        { unavailable++; }
                    }
                    Add("登録音声の合計サイズ", "Registered audio size", unavailable == 0 ? FormatSize(total)
                        : LocalizationService.Select($"{FormatSize(total)}（取得不可 {unavailable} ファイル）", $"{FormatSize(total)} ({unavailable} files unavailable)"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Add("情報取得エラー", "Information unavailable", ex.Message);
        }
        Add("収録曲数", "Tracks", album.Tracks.Count.ToString("N0"));
        var duration = TimeSpan.FromSeconds(album.Tracks.Sum(track => track.Duration.TotalSeconds));
        Add("合計再生時間", "Total duration", $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}");
        Add("音声形式", "Audio formats", Values(track => track.AudioFormat));
        Add("サンプルレート", "Sample rates", Values(track => track.SampleRate > 0 ? $"{track.SampleRate / 1000.0:0.###} kHz" : ""));
        Add("年", "Year", Values(track => track.Year));
        Add("ジャンル", "Genres", Values(track => track.Genre));
        return new(path, exists, rows.Where(row => !string.IsNullOrWhiteSpace(row.Value)).ToArray());
    }

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]} ({bytes:N0} bytes)";
    }
}
