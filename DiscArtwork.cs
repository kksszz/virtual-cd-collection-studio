using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal static class DiscArtwork
{
    public static (BitmapSource First, BitmapSource Second) SplitTwoDiscs(BitmapSource source)
    {
        if (source.PixelWidth < 2 || source.PixelHeight < 2)
            return (source, source);

        // Preserve the source scan. Split along its longer axis, then trim
        // scanner margins independently for both virtual disc images.
        var horizontal = source.PixelWidth >= source.PixelHeight;
        var firstRectangle = horizontal
            ? new Int32Rect(0, 0, source.PixelWidth / 2, source.PixelHeight)
            : new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight / 2);
        var secondRectangle = horizontal
            ? new Int32Rect(firstRectangle.Width, 0, source.PixelWidth - firstRectangle.Width, source.PixelHeight)
            : new Int32Rect(0, firstRectangle.Height, source.PixelWidth, source.PixelHeight - firstRectangle.Height);
        return (CropScannerMargin(RearInsertArtwork.Crop(source, firstRectangle)),
            CropScannerMargin(RearInsertArtwork.Crop(source, secondRectangle)));
    }

    public static BitmapSource CropScannerMargin(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width < 32 || height < 32) return source;

        BitmapSource image = source.Format == PixelFormats.Bgra32 ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (Math.Max(width, height) > 800)
        {
            var scale = 800.0 / Math.Max(width, height);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }

        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);

        (double B, double G, double R, double A) Corner(int startX, int startY)
        {
            var size = Math.Clamp(Math.Min(w, h) / 32, 2, 16);
            double b = 0, g = 0, r = 0, a = 0;
            var count = 0;
            for (var y = startY; y < startY + size; y++)
            for (var x = startX; x < startX + size; x++)
            {
                var p = (y * w + x) * 4;
                b += pixels[p]; g += pixels[p + 1]; r += pixels[p + 2]; a += pixels[p + 3];
                count++;
            }
            return (b / count, g / count, r / count, a / count);
        }

        var sampleSize = Math.Clamp(Math.Min(w, h) / 32, 2, 16);
        var corners = new[]
        {
            Corner(0, 0), Corner(w - sampleSize, 0),
            Corner(0, h - sampleSize), Corner(w - sampleSize, h - sampleSize)
        };
        static double Distance((double B, double G, double R, double A) left,
            (double B, double G, double R, double A) right) =>
            Math.Sqrt(Math.Pow(left.B - right.B, 2) + Math.Pow(left.G - right.G, 2)
                + Math.Pow(left.R - right.R, 2) + Math.Pow(left.A - right.A, 2) * 0.25);
        if (corners.SelectMany((left, index) => corners.Skip(index + 1).Select(right => Distance(left, right)))
            .DefaultIfEmpty().Max() > 34)
            return CenterSquare(source);

        var background = (B: corners.Average(c => c.B), G: corners.Average(c => c.G),
            R: corners.Average(c => c.R), A: corners.Average(c => c.A));
        bool Foreground(int x, int y)
        {
            var p = (y * w + x) * 4;
            var colorDistance = Math.Sqrt(Math.Pow(pixels[p] - background.B, 2)
                + Math.Pow(pixels[p + 1] - background.G, 2) + Math.Pow(pixels[p + 2] - background.R, 2));
            return Math.Abs(pixels[p + 3] - background.A) > 24 || colorDistance > 38;
        }

        var minimumColumnPixels = Math.Max(2, (int)Math.Ceiling(h * 0.025));
        var minimumRowPixels = Math.Max(2, (int)Math.Ceiling(w * 0.025));
        var left = Enumerable.Range(0, w).FirstOrDefault(x => Enumerable.Range(0, h).Count(y => Foreground(x, y)) >= minimumColumnPixels, -1);
        var right = Enumerable.Range(0, w).Reverse().FirstOrDefault(x => Enumerable.Range(0, h).Count(y => Foreground(x, y)) >= minimumColumnPixels, -1);
        var top = Enumerable.Range(0, h).FirstOrDefault(y => Enumerable.Range(0, w).Count(x => Foreground(x, y)) >= minimumRowPixels, -1);
        var bottom = Enumerable.Range(0, h).Reverse().FirstOrDefault(y => Enumerable.Range(0, w).Count(x => Foreground(x, y)) >= minimumRowPixels, -1);
        if (left < 0 || top < 0 || right <= left || bottom <= top) return CenterSquare(source);

        var contentWidth = right - left + 1;
        var contentHeight = bottom - top + 1;
        var aspect = (double)contentWidth / contentHeight;
        var diameter = Math.Max(contentWidth, contentHeight);
        var centerX = (left + right) / 2.0;
        var centerY = (top + bottom) / 2.0;
        if (aspect is < 0.86 or > 1.16 || diameter < Math.Min(w, h) * 0.55
            || Math.Abs(centerX - (w - 1) / 2.0) > w * 0.09
            || Math.Abs(centerY - (h - 1) / 2.0) > h * 0.09)
            return CenterSquare(source);

        // Retain a narrow physical edge while excluding the broad scanner mat.
        diameter = Math.Min(Math.Min(w, h), (int)Math.Ceiling(diameter * 1.018));
        left = Math.Clamp((int)Math.Round(centerX - diameter / 2.0), 0, w - diameter);
        top = Math.Clamp((int)Math.Round(centerY - diameter / 2.0), 0, h - diameter);
        var scaleX = (double)width / w;
        var scaleY = (double)height / h;
        var rectangle = new Int32Rect(
            Math.Clamp((int)Math.Round(left * scaleX), 0, width - 1),
            Math.Clamp((int)Math.Round(top * scaleY), 0, height - 1),
            Math.Max(1, Math.Min(width, (int)Math.Round(diameter * scaleX))),
            Math.Max(1, Math.Min(height, (int)Math.Round(diameter * scaleY))));
        rectangle.Width = Math.Min(rectangle.Width, width - rectangle.X);
        rectangle.Height = Math.Min(rectangle.Height, height - rectangle.Y);
        var side = Math.Min(rectangle.Width, rectangle.Height);
        rectangle = new Int32Rect(rectangle.X + (rectangle.Width - side) / 2,
            rectangle.Y + (rectangle.Height - side) / 2, side, side);
        return RearInsertArtwork.Crop(source, rectangle);
    }

    private static BitmapSource CenterSquare(BitmapSource source)
    {
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        return RearInsertArtwork.Crop(source, new Int32Rect(
            (source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
    }
}
