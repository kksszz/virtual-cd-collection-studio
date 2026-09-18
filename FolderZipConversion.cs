using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZipMp3Player;

internal static class FolderZipConversion
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);
    internal sealed record Result(string Source, string Destination, string ArchiveHash,
        Dictionary<string, string> Files, string[] Directories, ZipAlbum Album);

    private static string Root(string path)
    {
        var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.GetParent(root) is null || !Directory.Exists(root)
            || root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
            throw new IOException("アルバム専用のフォルダーを選択してください。");
        // Reject links anywhere in the ancestry as well as inside the album.
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("リンク先フォルダーは変換できません。");
        return root;
    }

    private static (string[] Files, string[] Directories) Inventory(string root)
    {
        var files = new List<string>(); var directories = new List<string>();
        var queue = new Queue<string>(); queue.Enqueue(root);
        while (queue.TryDequeue(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Encrypted)) != 0)
                    throw new IOException("リンクまたは暗号化ファイルを含むフォルダーは変換できません。");
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if ((attributes & FileAttributes.Directory) != 0) { directories.Add(relative); queue.Enqueue(path); }
                else files.Add(relative);
            }
        }
        return (files.Order(StringComparer.Ordinal).ToArray(), directories.Order(StringComparer.Ordinal).ToArray());
    }

    private static string Hash(Stream stream) => System.Convert.ToHexString(SHA256.HashData(stream));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Hash(stream); }
    private static string Child(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("不正な参照先です。");
        return path;
    }

    internal static Result Convert(string source)
    {
        var root = Root(source);
        var original = ZipAlbumReader.OpenFolder(root);
        if (original.Tracks.Count == 0) throw new IOException("音楽アルバムではありません。");
        if (original.Tracks.Any(t => !t.SourcePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("現在のZIP再生はMP3専用です。MP3以外の音源を含むアルバムは変換できません。");
        var destination = root + ".zip.mp3";
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("同名のZIP.MP3が存在します。上書きしません。");
        var inventory = Inventory(root);
        var temporary = Path.Combine(Directory.GetParent(root)!.FullName, "." + Guid.NewGuid().ToString("N") + ".conversiontmp");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (var directory in inventory.Directories) zip.CreateEntry(directory + "/", CompressionLevel.NoCompression);
                foreach (var relative in inventory.Files)
                {
                    using var input = new FileStream(Child(root, relative), FileMode.Open, FileAccess.Read, FileShare.Read);
                    hashes.Add(relative, Hash(input)); input.Position = 0;
                    var entry = zip.CreateEntry(relative, CompressionLevel.NoCompression);
                    using var entryStream = entry.Open(); input.CopyTo(entryStream);
                }
            }
            using (var zip = ZipFile.OpenRead(temporary))
            {
                if (zip.Entries.Count != hashes.Count + inventory.Directories.Length) throw new IOException("収録数の検証に失敗しました。");
                foreach (var pair in hashes)
                {
                    var entry = zip.GetEntry(pair.Key) ?? throw new IOException("収録物が不足しています。");
                    using var stream = entry.Open();
                    if (Hash(stream) != pair.Value || entry.CompressedLength != entry.Length) throw new IOException("内容検証に失敗しました。");
                }
            }
            var readBack = ZipAlbumReader.Open(temporary);
            if (readBack.Tracks.Count != original.Tracks.Count || readBack.Tracks.Any(t => !t.IsSupported))
                throw new IOException("曲の読み込み検証に失敗しました。入れ子の別アルバムや未対応音源がないか確認してください。");
            foreach (var track in readBack.Tracks)
            { using var reader = TrackAudioReader.Open(track); if (reader.Reader.Read(new byte[4096], 0, 4096) == 0) throw new IOException("曲を読み取れません。"); }
            var result = new Result(root, destination, HashFile(temporary), hashes, inventory.Directories, readBack);
            VerifySource(result);
            File.Move(temporary, destination); // Never overwrite.
            return result with { Album = ZipAlbumReader.Open(destination) };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void VerifySource(Result result)
    {
        Root(result.Source);
        var now = Inventory(result.Source);
        if (!now.Files.SequenceEqual(result.Files.Keys.Order(StringComparer.Ordinal)) || !now.Directories.SequenceEqual(result.Directories))
            throw new IOException("元フォルダーの内容が変わりました。削除しません。");
        foreach (var pair in result.Files)
            if (HashFile(Child(result.Source, pair.Key)) != pair.Value) throw new IOException("元ファイルが変更されました。削除しません。");
    }

    internal static void DeleteVerifiedSource(Result result)
    {
        // Caller must obtain explicit confirmation. Keep archive and source locked against writes.
        using var archive = new FileStream(result.Destination, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Hash(archive) != result.ArchiveHash) throw new IOException("ZIPが変更されました。元データは削除しません。");
        VerifySource(result);
        var locks = new List<FileStream>();
        try
        {
            foreach (var pair in result.Files)
            {
                // READ + DELETE access, share only READ: prevent writes/replacements until validation is done.
                var handle = CreateFile(Child(result.Source, pair.Key), 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("元ファイルを安全にロックできません。削除を中止します。"); }
                var stream = new FileStream(handle, FileAccess.Read);
                locks.Add(stream);
                if (Hash(stream) != pair.Value) throw new IOException("元ファイルが変更されました。削除を中止します。");
            }
            // No recursive deletion: new/unexpected files prevent directory removal.
            foreach (var stream in locks)
            {
                byte delete = 1;
                if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref delete, 1))
                    throw new IOException("元ファイルの削除に失敗しました。ZIPは保持しています。");
            }
            foreach (var stream in locks) stream.Dispose();
            locks.Clear();
            foreach (var directory in result.Directories.OrderByDescending(x => x.Length)) Directory.Delete(Child(result.Source, directory), false);
            Directory.Delete(result.Source, false);
        }
        finally { foreach (var stream in locks) stream.Dispose(); }
    }
    internal static void DeleteVerifiedFile(string path, string hash)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("リンクされたファイルは削除しません。");
        var handle = CreateFile(path, 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("元ZIPをロックできません。"); }
        using var stream = new FileStream(handle, FileAccess.Read);
        if (Hash(stream) != hash) throw new IOException("元ZIPが変更されています。削除しません。");
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1)) throw new IOException("元ZIPを削除できませんでした。");
    }
}
