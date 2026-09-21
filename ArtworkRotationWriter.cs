using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal static class ArtworkRotationWriter
{
    // Work on a sibling copy; only the validated result replaces the original.
    // Keep the backup even when later UI/cache refresh fails.
    public static string Rotate(string path, string? entryName, int degrees, string? backupFolder, IProgress<string>? progress = null)
        => Save(path, entryName, degrees, null, backupFolder, progress,0);

    public static string Crop(string path, string? entryName, int degrees, System.Windows.Int32Rect crop,
        string? backupFolder, IProgress<string>? progress = null)
        => Save(path, entryName, degrees, crop, backupFolder, progress,0);

    public static string CropWithAngle(string path,string? entryName,int degrees,System.Windows.Int32Rect crop,
        double fineAngle,string? backupFolder,IProgress<string>? progress=null)
        => Save(path,entryName,degrees,crop,backupFolder,progress,fineAngle);

    private static string Save(string path, string? entryName, int degrees, System.Windows.Int32Rect? crop,
        string? backupFolder, IProgress<string>? progress,double fineAngle)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".rotationtmp";
        var folder = string.IsNullOrWhiteSpace(backupFolder) ? Path.GetDirectoryName(path)! : backupFolder;
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, Path.GetFileName(path) + (crop is null ? ".rotation-backup-" : ".crop-backup-") + Guid.NewGuid().ToString("N"));
        try
        {
            if (entryName is null)
            {
                progress?.Report(crop is null ? "画像を回転し、保存用ファイルを作成しています…" : "選択範囲を切り抜き、保存用ファイルを作成しています…");
                using var input = File.OpenRead(path);
                var bytes = RotateBytes(input, Path.GetExtension(path), degrees, crop,fineAngle);
                File.WriteAllBytes(temporary, bytes);
            }
            else
            {
                Dictionary<string, string> before;
                progress?.Report("1 / 4　元のZIPの内容を確認しています…");
                using (var original = File.OpenRead(path))
                using (var archive = new ZipArchive(original, ZipArchiveMode.Read, false, Encoding.GetEncoding(932)))
                    before = Signatures(archive, entryName);
                using (var original = File.OpenRead(path))
                using (var archive = new ZipArchive(original, ZipArchiveMode.Read, false, Encoding.GetEncoding(932)))
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var rebuilt = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    var completed = 0;
                    foreach (var entry in archive.Entries)
                    {
                        progress?.Report($"2 / 4　ZIPを再構築しています…（{++completed} / {archive.Entries.Count} ファイル）");
                        var target = entry.FullName == entryName;
                        // ZIP.MP3 must remain directly playable, including after artwork edits.
                        var replacement = rebuilt.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                        replacement.LastWriteTime = entry.LastWriteTime;
                        replacement.ExternalAttributes = entry.ExternalAttributes;
                        using var input = entry.Open();
                        using var output = replacement.Open();
                        if (target) output.Write(RotateBytes(input, Path.GetExtension(entryName), degrees, crop,fineAngle));
                        else input.CopyTo(output);
                    }
                }
                progress?.Report("3 / 4　保存内容を検証しています…\n音楽など、他のファイルが変わっていないか確認中");
                using var checkStream = File.OpenRead(temporary);
                using var check = new ZipArchive(checkStream, ZipArchiveMode.Read, false, Encoding.GetEncoding(932));
                var after = Signatures(check, entryName);
                if (check.Entries.Any(entry => entry.Length != entry.CompressedLength))
                    throw new InvalidDataException("無圧縮ZIPの検証に失敗しました。元ファイルは変更しません。");
                if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out var hash) || hash != p.Value))
                    throw new InvalidDataException("ZIP内の他のファイルの検証に失敗しました。元ファイルは変更しません。");
                using var image = check.Entries.Single(e => e.FullName == entryName).Open();
                using var checkedImage = new MemoryStream();
                image.CopyTo(checkedImage);
                checkedImage.Position = 0;
                _ = BitmapDecoder.Create(checkedImage, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
            progress?.Report(entryName is null ? "元の画像をバックアップしています…" : "4 / 4　元のZIPをバックアップしています…");
            File.Copy(path, backup, false);
            // No unsafe overwrite fallback: an unsupported atomic replacement leaves the original intact.
            progress?.Report("検証済みのファイルに置き換えています…");
            File.Replace(temporary, path, null);
            return backup;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, string> Signatures(ZipArchive archive, string target)
    {
        if (archive.Entries.Count(e => e.FullName == target) != 1)
            throw new InvalidDataException("ZIP内の対象画像を一意に特定できません。");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName == target) continue;
            using var stream = entry.Open();
            result.Add(entry.FullName, Convert.ToHexString(SHA256.HashData(stream)));
        }
        return result;
    }

    private static byte[] RotateBytes(Stream input, string extension, int degrees, System.Windows.Int32Rect? crop,double fineAngle)
    {
        // WIC exposes a delayed 1x1 placeholder for non-seekable ZIP streams.
        using var seekable = new MemoryStream();
        input.CopyTo(seekable);
        seekable.Position = 0;
        var decoder = BitmapDecoder.Create(seekable, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) throw new NotSupportedException("複数ページ・アニメーション画像の編集には対応していません。");
        var normalized = ((degrees % 360) + 360) % 360;
        BitmapSource rotated = new TransformedBitmap(decoder.Frames[0], new RotateTransform(normalized));
        rotated=ArtworkDeskew.Render(rotated,fineAngle);
        if (crop is { } area)
        {
            if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1
                || (long)area.X + area.Width > rotated.PixelWidth || (long)area.Y + area.Height > rotated.PixelHeight)
                throw new ArgumentOutOfRangeException(nameof(crop), "切り抜き範囲が画像の外にあります。");
            rotated = new CroppedBitmap(rotated, area);
        }
        BitmapEncoder encoder = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => throw new NotSupportedException("この画像形式の編集保存には対応していません。")
        };
        encoder.Frames.Add(BitmapFrame.Create(rotated));
        using var output = new MemoryStream();
        encoder.Save(output);
        output.Position = 0;
        var verify = BitmapDecoder.Create(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (verify.PixelWidth != rotated.PixelWidth || verify.PixelHeight != rotated.PixelHeight)
            throw new InvalidDataException("編集後の画像検証に失敗しました。");
        return output.ToArray();
    }
}
