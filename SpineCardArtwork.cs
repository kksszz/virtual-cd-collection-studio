using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

namespace ZipMp3Player;

internal sealed class SpineCardFoldSetting
{
    public double Left { get; set; }
    public double Right { get; set; }
}

internal static class SpineCardArtwork
{
    internal sealed record Regions(Int32Rect Back, Int32Rect Spine, Int32Rect Front);
    private sealed record ManualFolds(double Left, double Right);
    private static readonly ConditionalWeakTable<BitmapSource, ManualFolds> ManualOverrides = new();

    public static void SetManualFolds(BitmapSource source, double left, double right)
    {
        left = Math.Clamp(left, 0.01, 0.97);
        right = Math.Clamp(right, left + 0.01, 0.99);
        lock (ManualOverrides)
        {
            ManualOverrides.Remove(source);
            ManualOverrides.Add(source, new ManualFolds(left, right));
        }
    }

    public static Regions GetRegions(BitmapSource source)
    {
        var sourceWidth = source.PixelWidth;
        var sourceHeight = source.PixelHeight;
        var content = RearInsertArtwork.FindContent(source);
        lock (ManualOverrides)
        {
            if (ManualOverrides.TryGetValue(source, out var manual))
            {
                var left = Math.Clamp((int)Math.Round(sourceWidth * manual.Left), content.X + 1,
                    Math.Max(content.X + 1, content.X + content.Width - 2));
                var right = Math.Clamp((int)Math.Round(sourceWidth * manual.Right), left + 1,
                    content.X + content.Width - 1);
                return new(new Int32Rect(content.X, content.Y, left - content.X, content.Height),
                    new Int32Rect(left, content.Y, right - left, content.Height),
                    new Int32Rect(right, content.Y, content.X + content.Width - right, content.Height));
            }
        }
        var width = content.Width;
        var height = content.Height;
        if (width < 24 || height < 24) return Fallback(content);

        BitmapSource image = RearInsertArtwork.Crop(source, content);
        image = image.Format == PixelFormats.Bgra32 ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        if (Math.Max(width, height) > 800)
        {
            var scale = 800.0 / Math.Max(width, height);
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);

        // Average broad vertical strips so lettering and barcodes do not look
        // like folds. Real folds or panel-colour changes persist through most
        // of the card height and remain strong in this profile.
        var profile = new (double B, double G, double R)[w];
        var top = Math.Max(0, (int)Math.Round(h * 0.035));
        var bottom = Math.Min(h, (int)Math.Round(h * 0.965));
        for (var x = 0; x < w; x++)
        {
            double b = 0, g = 0, r = 0;
            for (var y = top; y < bottom; y++)
            {
                var p = (y * w + x) * 4;
                b += pixels[p]; g += pixels[p + 1]; r += pixels[p + 2];
            }
            var count = Math.Max(1, bottom - top);
            profile[x] = (b / count, g / count, r / count);
        }

        double Difference(int boundary)
        {
            var band = Math.Clamp(w / 160, 2, 5);
            if (boundary < band || boundary + band >= w) return 0;
            (double B, double G, double R) Mean(int start)
            {
                double b = 0, g = 0, r = 0;
                for (var x = start; x < start + band; x++)
                {
                    b += profile[x].B; g += profile[x].G; r += profile[x].R;
                }
                return (b / band, g / band, r / band);
            }
            var left = Mean(boundary - band);
            var right = Mean(boundary + 1);
            return Math.Sqrt(Math.Pow(left.B - right.B, 2) + Math.Pow(left.G - right.G, 2)
                + Math.Pow(left.R - right.R, 2));
        }

        var center = w / 2.0;
        var minimumSpineWidth = Math.Max(3, (int)Math.Round(w * 0.085));
        var maximumSpineWidth = Math.Max(minimumSpineWidth, (int)Math.Round(w * 0.235));
        var minimumFlapWidth = Math.Max(2, (int)Math.Round(w * 0.12));
        var preferredWidth = w * 0.16;
        var bestLeft = -1;
        var bestRight = -1;
        var bestScore = double.NegativeInfinity;
        var bestMinimumEdge = 0d;
        // Obi flaps are frequently unequal: Japanese releases can devote most
        // of the reverse to a track list while the front flap is narrow. Do not
        // anchor the spine midpoint to the scan centre. Instead, evaluate every
        // physically plausible fold pair and use centre proximity only as a
        // weak tie-breaker.
        for (var left = minimumFlapWidth; left <= w - minimumFlapWidth - minimumSpineWidth; left++)
        for (var right = left + minimumSpineWidth;
             right <= Math.Min(w - minimumFlapWidth, left + maximumSpineWidth); right++)
        {
            var leftEdge = Difference(left);
            var rightEdge = Difference(right);
            var minimumEdge = Math.Min(leftEdge, rightEdge);
            var balance = Math.Abs(leftEdge - rightEdge) * 0.16;
            var pairCenterOffset = Math.Abs((left + right) / 2.0 - center);
            var centerPenalty = pairCenterOffset * 0.035;
            // Printed boxes and title bars can produce a stronger vertical
            // edge just inside a flap. A real jewel-case obi spine is normally
            // close to 16% of this flat scan, so strongly penalize implausibly
            // wide centre panels while still letting very clear folds win.
            var widthPenalty = Math.Abs((right - left) - preferredWidth) * 5.0;
            var score = minimumEdge + (leftEdge + rightEdge) * 0.22
                - balance - centerPenalty - widthPenalty;
            if (score <= bestScore) continue;
            bestScore = score;
            bestLeft = left;
            bestRight = right;
            bestMinimumEdge = minimumEdge;
        }

        // Below this contrast, text or scanner noise is more likely than two
        // genuine fold boundaries. Preserve a physically plausible centre band.
        if (bestLeft < 0 || bestRight < 0 || bestMinimumEdge < 7.5) return Fallback(content);
        var leftFold = (int)Math.Round(bestLeft * (double)width / w);
        var rightFold = (int)Math.Round(bestRight * (double)width / w);
        leftFold = Math.Clamp(leftFold, 1, width - 2);
        rightFold = Math.Clamp(rightFold, leftFold + 1, width - 1);
        leftFold += content.X;
        rightFold += content.X;
        return new(new Int32Rect(content.X, content.Y, leftFold - content.X, content.Height),
            new Int32Rect(leftFold, content.Y, rightFold - leftFold, content.Height),
            new Int32Rect(rightFold, content.Y, content.X + content.Width - rightFold, content.Height));
    }

    public static (BitmapSource Back, BitmapSource Spine, BitmapSource Front) Split(BitmapSource source)
    {
        var regions = GetRegions(source);
        return (RearInsertArtwork.Crop(source, regions.Back), RearInsertArtwork.Crop(source, regions.Spine),
            RearInsertArtwork.Crop(source, regions.Front));
    }

    private static Regions Fallback(Int32Rect content)
    {
        var width = content.Width;
        var spineWidth = Math.Clamp((int)Math.Round(width * 0.16), 1, Math.Max(1, width - 2));
        var left = content.X + Math.Max(1, (width - spineWidth) / 2);
        var right = Math.Min(content.X + width - 1, left + spineWidth);
        return new(new Int32Rect(content.X, content.Y, left - content.X, content.Height),
            new Int32Rect(left, content.Y, right - left, content.Height),
            new Int32Rect(right, content.Y, content.X + width - right, content.Height));
    }
}
