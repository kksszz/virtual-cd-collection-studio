using System.IO;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

public static class ClipboardArtworkStorage
{
    public static string SavePng(BitmapSource image, string directory)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
            throw new InvalidDataException("クリップボードの画像サイズを取得できません。");
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var destination = Path.Combine(directory, $"clipboard-{stamp}.png");
        for (var suffix = 2; File.Exists(destination); suffix++)
            destination = Path.Combine(directory, $"clipboard-{stamp}-{suffix}.png");

        var temporary = destination + ".tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                encoder.Save(output);
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
