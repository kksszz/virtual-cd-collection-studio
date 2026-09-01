using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal static class SpineCardArtwork
{
    internal sealed record Regions(Int32Rect Back, Int32Rect Spine, Int32Rect Front);

    public static Regions GetRegions(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width < 24 || height < 24) return Fallback(width, height);

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

        var center = w / 2;
        var minimumDistance = Math.Max(2, (int)Math.Round(w * 0.055));
        var maximumDistance = Math.Max(minimumDistance, (int)Math.Round(w * 0.18));
        var maximumCenterOffset = Math.Max(2, (int)Math.Round(w * 0.04));
        var preferredWidth = w * 0.16;
        var bestLeft = -1;
        var bestRight = -1;
        var bestScore = double.NegativeInfinity;
        var bestMinimumEdge = 0d;
        // Scans are often a few pixels off-centre or have unequal flap widths.
        // Evaluate fold pairs whose midpoint stays near the image centre rather
        // than requiring both folds to be at exactly the same distance.
        for (var left = center - maximumDistance; left <= center - minimumDistance; left++)
        for (var right = center + minimumDistance; right <= center + maximumDistance; right++)
        {
            var pairCenterOffset = Math.Abs((left + right) / 2.0 - center);
            if (pairCenterOffset > maximumCenterOffset) continue;
            var leftEdge = Difference(left);
            var rightEdge = Difference(right);
            var minimumEdge = Math.Min(leftEdge, rightEdge);
            var balance = Math.Abs(leftEdge - rightEdge) * 0.16;
            var centerPenalty = pairCenterOffset * 0.18;
            // Printed boxes and title bars can produce a stronger vertical
            // edge just inside a flap. A real jewel-case obi spine is normally
            // close to 16% of this flat scan, so strongly penalize implausibly
            // wide centre panels while still letting very clear folds win.
            var widthPenalty = Math.Abs((right - left) - preferredWidth) * 5.0;
            var score = minimumEdge + (leftEdge + rightEdge) * 0.22 - balance - centerPenalty - widthPenalty;
            if (score <= bestScore) continue;
            bestScore = score;
            bestLeft = left;
            bestRight = right;
            bestMinimumEdge = minimumEdge;
        }

        // Below this contrast, text or scanner noise is more likely than two
        // genuine fold boundaries. Preserve a physically plausible centre band.
        if (bestLeft < 0 || bestRight < 0 || bestMinimumEdge < 7.5) return Fallback(width, height);
        var leftFold = (int)Math.Round(bestLeft * (double)width / w);
        var rightFold = (int)Math.Round(bestRight * (double)width / w);
        leftFold = Math.Clamp(leftFold, 1, width - 2);
        rightFold = Math.Clamp(rightFold, leftFold + 1, width - 1);
        return new(new Int32Rect(0, 0, leftFold, height),
            new Int32Rect(leftFold, 0, rightFold - leftFold, height),
            new Int32Rect(rightFold, 0, width - rightFold, height));
    }

    public static (BitmapSource Back, BitmapSource Spine, BitmapSource Front) Split(BitmapSource source)
    {
        var regions = GetRegions(source);
        return (RearInsertArtwork.Crop(source, regions.Back), RearInsertArtwork.Crop(source, regions.Spine),
            RearInsertArtwork.Crop(source, regions.Front));
    }

    private static Regions Fallback(int width, int height)
    {
        var spineWidth = Math.Clamp((int)Math.Round(width * 0.16), 1, Math.Max(1, width - 2));
        var left = Math.Max(1, (width - spineWidth) / 2);
        var right = Math.Min(width - 1, left + spineWidth);
        return new(new Int32Rect(0, 0, left, height), new Int32Rect(left, 0, right - left, height),
            new Int32Rect(right, 0, width - right, height));
    }
}
