using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal static class ZipFolderConversion
{
    internal sealed record Result(string Source, string Destination, string ArchiveHash,
        Dictionary<string, string> EntryPaths, Dictionary<string, string> Hashes, ZipAlbum Album);
    internal static string DestinationFor(string source) => source.EndsWith(".zip.mp3", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFullPath(source)[..^8] : throw new IOException("ZIP.MP3を選択してください。");
    internal static string SafePath(string root, string name)
    {
        var parts = name.Replace('\\', '/').TrimEnd('/').Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')
            || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
            throw new IOException("ZIP内に安全に展開できない名前があります: " + name);
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("展開先の範囲外です。");
        return path;
    }
    private static void RejectLinks(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("リンク経由の展開・削除はできません。");
    }
    private static string Hash(Stream stream) => System.Convert.ToHexString(SHA256.HashData(stream));
    internal static Result Convert(string source)
    {
        source = Path.GetFullPath(source);
        var destination = DestinationFor(source);
        RejectLinks(Path.GetDirectoryName(source)!);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクされたZIPは対象外です。");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("同名のフォルダーまたはファイルがあります。上書きしません。");
        var original = ZipAlbumReader.Open(source);
        if (original.Tracks.Any(t => t.IsEncrypted || t.CompressionMethod is not (0 or 8))) throw new IOException("暗号化・未対応の圧縮形式です。");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archiveHash = Hash(input); input.Position = 0;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, true, Encoding.GetEncoding(932));
        var entries = zip.Entries.ToArray();
        if (entries.Length > 100000) throw new IOException("収録物が多すぎます。");
        var normalized = entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        foreach (var name in normalized) SafePath(destination, name);
        if (normalized.Select(n => n.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new IOException("同名・大小文字違いの収録物があるため展開できません。");
        if (entries.Any(e => ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (e.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0))
            throw new IOException("ZIP内のリンクは展開できません。");
        var prefix = normalized[0].Split('/')[0] + "/";
        if (!normalized.All(n => n.StartsWith(prefix, StringComparison.Ordinal))) prefix = "";
        var mapping = entries.ToDictionary(e => e.FullName, e => e.FullName.Replace('\\', '/')[prefix.Length..], StringComparer.Ordinal);
        var stage = Path.Combine(Path.GetDirectoryName(destination)!, ".zip-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in entries)
            {
                var relative = mapping[entry.FullName]; if (relative.Length == 0) continue;
                var target = SafePath(stage, relative);
                if (relative.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var content = entry.Open();
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920]; long count = 0; int read;
                    while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                    { count += read; if (count > entry.Length) throw new IOException("展開サイズが記録値を超えました。"); output.Write(buffer, 0, read); }
                    if (count != entry.Length) throw new IOException("展開サイズが一致しません。");
                }
                using var verify = entry.Open(); var expected = Hash(verify);
                using var disk = File.OpenRead(target);
                if (Hash(disk) != expected) throw new IOException("展開内容の照合に失敗しました。");
                hashes.Add(relative, expected);
            }
            var album = ZipAlbumReader.OpenFolder(stage);
            if (album.Tracks.Count != original.Tracks.Count) throw new IOException("複数階層に分かれた音源は対応していません。元ZIPは保持しています。");
            foreach (var track in album.Tracks)
            { using var reader = TrackAudioReader.Open(track); if (reader.Reader.Read(new byte[4096], 0, 4096) == 0) throw new IOException("音源を読み込めません。"); }
            Directory.Move(stage, destination); // Atomic same-parent publish, no overwrite.
            return new(source, destination, archiveHash, mapping, hashes, ZipAlbumReader.OpenFolder(destination));
        }
        catch (Exception ex) { throw new IOException($"{ex.Message}\n元ZIPは変更していません。部分的な展開データが残る場合の場所: {stage}", ex); }
    }
    internal static void DeleteVerifiedSource(Result result)
    {
        RejectLinks(result.Destination);
        RejectLinks(Path.GetDirectoryName(result.Source)!);
        foreach (var directory in result.EntryPaths.Values.Where(n => n.EndsWith('/')))
        {
            var path = SafePath(result.Destination, directory);
            if (!Directory.Exists(path)) throw new IOException("展開先のフォルダーが不足しています。元ZIPは削除しません。");
            RejectLinks(path);
        }
        var locks = new List<FileStream>();
        try
        {
            foreach (var pair in result.Hashes)
            {
                var path = SafePath(result.Destination, pair.Key);
                RejectLinks(Path.GetDirectoryName(path)!);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("展開先がリンクに変更されています。");
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); locks.Add(stream);
                if (Hash(stream) != pair.Value) throw new IOException("展開先が変更されています。元ZIPは削除しません。");
            }
            FolderZipConversion.DeleteVerifiedFile(result.Source, result.ArchiveHash);
        }
        finally { foreach (var stream in locks) stream.Dispose(); }
    }
}
