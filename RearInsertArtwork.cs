using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal static class RearInsertArtwork
{
    internal sealed record Regions(Int32Rect Panel, Int32Rect? Left, Int32Rect? Right);

    public static Regions GetRegions(BitmapSource source, bool forceSpines)
    {
        var content = FindContent(source);
        var aspect = (double)content.Width / content.Height;
        if (content.Width < 3 || (!forceSpines && (aspect < 1.12 || aspect > 1.58)))
            return new(content, null, null);
        // Measure the 6 + 138 + 6 mm insert AFTER removing scanner margins.
        // The three rectangles share their boundaries, with no duplicated strip.
        var strip = Math.Clamp((int)Math.Round(content.Width * 0.04), 1, (content.Width - 1) / 2);
        return new(new(content.X + strip, content.Y, content.Width - 2 * strip, content.Height),
            new(content.X, content.Y, strip, content.Height),
            new(content.X + content.Width - strip, content.Y, strip, content.Height));
    }

    public static BitmapSource Crop(BitmapSource image, Int32Rect rectangle)
    {
        if (rectangle == new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight)) return image;
        var crop = new CroppedBitmap(image, rectangle);
        crop.Freeze();
        return crop;
    }

    /// <summary>
    /// Removes only a confidently detected, straight near-white scanner frame.
    /// The source bitmap (and therefore the user's original image) is unchanged.
    /// </summary>
    public static BitmapSource CropWhiteBorder(BitmapSource image) => Crop(image, FindContent(image));

    internal static Int32Rect FindContent(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width < 32 || height < 32) return new(0, 0, width, height);
        var image = source.Format == PixelFormats.Bgra32 ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        // Bound analysis memory for large artwork; crop coordinates stay in the
        // original decoded image, so texture quality is not reduced.
        if (Math.Max(width, height) > 800)
        {
            var scale = 800.0 / Math.Max(width, height);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        bool White(int x, int y)
        {
            var p = (y * w + x) * 4;
            var min = Math.Min(pixels[p], Math.Min(pixels[p + 1], pixels[p + 2]));
            var max = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2]));
            return pixels[p + 3] >= 240 && min >= 236 && max - min <= 18;
        }
        int Inset(bool horizontal, bool reverse)
        {
            var axis = horizontal ? w : h;
            var cross = horizontal ? h : w;
            // Flatbed scans commonly leave an 8-10% strip on one edge. The
            // straight-edge/coverage checks below distinguish it from pale
            // cover artwork, so allow the probe to reach those wider strips.
            var limit = Math.Max(1, (int)Math.Ceiling(axis * 0.14));
            var margins = new List<(double Position, int Margin)>();
            var eligible = 0;
            const int samples = 51;
            for (var i = 0; i < samples; i++)
            {
                var other = (int)Math.Round((cross - 1) * (0.02 + 0.96 * i / (samples - 1)));
                var n = 0;
                while (n < limit)
                {
                    var along = reverse ? axis - 1 - n : n;
                    if (!White(horizontal ? along : other, horizontal ? other : along)) break;
                    n++;
                }
                // Uniform white artwork / broad white design areas are not a
                // confident scanner border. Never search deep into the artwork.
                if (n >= limit) continue;
                eligible++;
                if (n > 0) margins.Add(((double)other / Math.Max(1, cross - 1), n));
            }
            // Ignore sample lines that are white for the full safety depth.
            // They can be a deliberately white spine or flap. Still require
            // most of the image to contain measurable artwork, and nearly all
            // of those measurable lines to share the same white border.
            if (eligible < samples * 0.55 || margins.Count < eligible * 0.9) return 0;
            // A real scanner edge is straight (possibly tilted), so its white
            // run follows one line. Text printed on white Spine paper produces
            // irregular run lengths from glyph to glyph; do not crop those as
            // if they were empty scanner margins.
            var meanX = margins.Average(value => value.Position);
            var meanY = margins.Average(value => value.Margin);
            var denominator = margins.Sum(value => Math.Pow(value.Position - meanX, 2));
            var slope = denominator <= double.Epsilon ? 0
                : margins.Sum(value => (value.Position - meanX) * (value.Margin - meanY)) / denominator;
            var intercept = meanY - slope * meanX;
            var residual = Math.Sqrt(margins.Average(value =>
                Math.Pow(value.Margin - (intercept + slope * value.Position), 2)));
            if (residual > Math.Max(2.25, axis * 0.004)) return 0;
            var ordered = margins.Select(value => value.Margin).Order().ToList();
            // Small scan skew produces varying margins. Use the inner edge of
            // the white border, but ignore isolated outliers from artwork details.
            return ordered[(int)Math.Floor((ordered.Count - 1) * 0.95)];
        }
        var left = (int)Math.Round(Inset(true, false) * (double)width / w);
        var right = (int)Math.Round(Inset(true, true) * (double)width / w);
        var top = (int)Math.Round(Inset(false, false) * (double)height / h);
        var bottom = (int)Math.Round(Inset(false, true) * (double)height / h);
        var contentWidth = width - left - right;
        var contentHeight = height - top - bottom;
        return contentWidth > 0 && contentHeight > 0
            ? new(left, top, contentWidth, contentHeight)
            : new(0, 0, width, height);
    }
}
