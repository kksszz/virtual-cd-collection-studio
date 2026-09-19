using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

/// <summary>Portable read-only artwork snapshot. Never writes to the album or its settings.</summary>
public static class MobileCaseExporter
{
    public static void Export(JewelCaseCoverFlowItem item, string destination)
    {
        if (string.Equals(Path.GetExtension(destination), ".glb", StringComparison.OrdinalIgnoreCase)) { MobileGlbExporter.Export(item,destination); return; }
        if (!string.Equals(Path.GetExtension(destination), ".vcd3d", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("保存先の拡張子は .vcd3d にしてください。");
        var textures = new Dictionary<string, object>();
        var inlay = item.SplitInlay();
        var obi = item.SpineCard is { } card ? SpineCardArtwork.Split(card) : default;
        var images = new Dictionary<string, BitmapSource?>
        {
            ["front"] = item.FrontCover, ["insideFront"] = item.InsideFrontCover,
            ["back"] = item.BackCover, ["spine"] = item.SpineCover,
            ["rightSpine"] = item.RightSpineCover, ["inlay"] = inlay.Panel,
            ["disc"] = item.DiscImage,
            ["obiBack"] = obi.Back, ["obiSpine"] = obi.Spine, ["obiFront"] = obi.Front
        };
        var fullPath = Path.GetFullPath(destination);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                long totalBytes = 0;
                foreach (var (role, original) in images)
                {
                    if (original is null) continue;
                    BitmapSource image = original;
                    var scale = Math.Min(1, 1024d / Math.Max(image.PixelWidth, image.PixelHeight));
                    if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var bytes = new MemoryStream();
                    encoder.Save(bytes);
                    var content = bytes.ToArray();
                    totalBytes += content.Length;
                    if (content.Length > 5 * 1024 * 1024 || totalBytes > 31 * 1024 * 1024)
                        throw new InvalidDataException("画像データがモバイル用の上限を超えました。");
                    var name = role + ".png";
                    using (var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open()) stream.Write(content);
                    textures[role] = new { file = name, sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() };
                }
                var manifest = new
                {
                    format = "virtual-cd-case", version = 2, model = "jewel-case-v2",
                    title = item.Title, artist = item.Artist,
                    // Do not expose Windows paths. Mobile binding is explicitly selected on import.
                    albumId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Key))).ToLowerInvariant(),
                    tray = item.TrayColorMode is "White" or "Gray" or "Clear" ? item.TrayColorMode : "Black",
                    textures,
                    obi = obi.Front is null ? null : new
                    {
                        frontWidthMm = Math.Clamp(120d * obi.Front.PixelWidth / obi.Front.PixelHeight, 1, 140),
                        backWidthMm = Math.Clamp(120d * obi.Back.PixelWidth / obi.Back.PixelHeight, 1, 140)
                    },
                    wrapped = item.SpineCard is not null,
                    unsupported = new { secondDisc = item.SecondDiscImage is not null }
                };
                using var manifestStream = zip.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(manifestStream, manifest, new JsonSerializerOptions { WriteIndented = true });
            }
            // Replace only after a complete ZIP has been closed. Existing destination survives failures.
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
