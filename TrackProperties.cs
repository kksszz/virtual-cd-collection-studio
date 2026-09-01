using System.IO;

namespace ZipMp3Player;

internal static class TrackProperties
{
    // Reuse the read-only property sheet snapshot without opening a decoder or
    // extracting archive entries. File metadata is live; audio metadata is cached.
    public static AlbumProperties Load(ZipTrack track)
    {
        var rows = new List<AlbumPropertyRow>();
        void Add(string ja, string en, string value) => rows.Add(new(LocalizationService.Select(ja, en), value));
        var path = track.SourcePath;
        var exists = false;
        Add("曲名", "Title", track.Title);
        Add("アーティスト", "Artist", track.Artist);
        Add("アルバム", "Album", track.Album);
        Add(track.IsArchiveEntry ? "ZIPのパス" : "ファイルのパス", track.IsArchiveEntry ? "ZIP path" : "File path", path);
        if (track.IsArchiveEntry)
        {
            Add("ZIP内のファイル名", "File inside ZIP", track.FileName);
            Add("音声サイズ（登録時）", "Audio size (indexed)", AlbumProperties.FormatSize(track.Size));
            Add("圧縮後サイズ（登録時）", "Compressed size (indexed)", AlbumProperties.FormatSize(track.CompressedSize));
            Add("ZIP圧縮方式", "ZIP compression", track.CompressionMethod switch
            {
                0 => LocalizationService.Select("Store（無圧縮）", "Store (uncompressed)"),
                8 => "Deflate",
                _ => track.CompressionMethod.ToString()
            });
        }
        try
        {
            path = Path.GetFullPath(path);
            Add(track.IsArchiveEntry ? "ZIPの名前" : "ファイル名", track.IsArchiveEntry ? "ZIP name" : "File name", Path.GetFileName(path));
            Add("場所", "Location", Path.GetDirectoryName(path) ?? path);
            var info = new FileInfo(path);
            info.Refresh();
            exists = info.Exists;
            Add("元ファイルの状態", "Source file status", exists ? LocalizationService.Select("利用可能", "Available")
                : LocalizationService.Select("元のファイルが見つかりません", "Source file not found"));
            if (exists)
            {
                Add(track.IsArchiveEntry ? "ZIPファイルサイズ" : "ファイルサイズ", track.IsArchiveEntry ? "ZIP file size" : "File size", AlbumProperties.FormatSize(info.Length));
                Add(track.IsArchiveEntry ? "ZIPの作成日時" : "作成日時", track.IsArchiveEntry ? "ZIP created" : "Created", info.CreationTime.ToString("yyyy/MM/dd HH:mm:ss"));
                Add(track.IsArchiveEntry ? "ZIPの更新日時" : "更新日時", track.IsArchiveEntry ? "ZIP modified" : "Modified", info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
                Add(track.IsArchiveEntry ? "ZIPの属性" : "属性", track.IsArchiveEntry ? "ZIP attributes" : "Attributes", info.Attributes.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Add("情報取得エラー", "Information unavailable", ex.Message);
        }
        Add("再生時間", "Duration", track.Duration > TimeSpan.Zero
            ? $"{(int)track.Duration.TotalHours}:{track.Duration.Minutes:00}:{track.Duration.Seconds:00}" : "—");
        Add("音声形式", "Audio format", track.AudioFormat);
        if (track.BitrateKbps > 0) Add("ビットレート", "Bitrate", $"{track.BitrateKbps} kbps");
        if (track.AudioFormat.Equals("MP3", StringComparison.OrdinalIgnoreCase) && (track.IsMp3Valid || track.IsCbr))
            Add("ビットレート方式", "Bitrate mode", track.IsCbr ? "CBR" : "VBR");
        if (track.SampleRate > 0) Add("サンプルレート", "Sample rate", $"{track.SampleRate / 1000.0:0.###} kHz");
        if (track.BitsPerSample > 0) Add("ビット深度", "Bit depth", $"{track.BitsPerSample} bit");
        if (track.TrackNumber > 0) Add("トラック番号", "Track number", track.TrackNumber.ToString());
        if (track.DiscNumber > 0) Add("ディスク番号", "Disc number", track.DiscText);
        Add("年", "Year", track.Year);
        Add("ジャンル", "Genre", track.Genre);
        Add("対応状況（登録情報）", "Support (indexed)", track.SupportText);
        return new(path, exists, rows.Where(row => !string.IsNullOrWhiteSpace(row.Value)).ToArray());
    }
}
