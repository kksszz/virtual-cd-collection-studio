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
        var cornersAreUniform = corners.SelectMany((left, index) =>
                corners.Skip(index + 1).Select(right => Distance(left, right)))
            .DefaultIfEmpty().Max() <= 34;

        var background = (B: corners.Average(c => c.B), G: corners.Average(c => c.G),
            R: corners.Average(c => c.R), A: corners.Average(c => c.A));
        double BackgroundDistance(int x, int y)
        {
            var p = (y * w + x) * 4;
            return Math.Sqrt(Math.Pow(pixels[p] - background.B, 2)
                + Math.Pow(pixels[p + 1] - background.G, 2)
                + Math.Pow(pixels[p + 2] - background.R, 2));
        }
        bool Foreground(int x, int y)
        {
            var p = (y * w + x) * 4;
            var colorDistance = BackgroundDistance(x, y);
            // Ignore the pale scanner shadow and edge haze. A higher colour
            // threshold produces a tighter, more stable physical-disc bound.
            return Math.Abs(pixels[p + 3] - background.A) > 32 || colorDistance > 56;
        }

        bool TryFindHubCenter(out double hubX, out double hubY)
        {
            hubX = (w - 1) / 2.0;
            hubY = (h - 1) / 2.0;
            var imageCenterX = (w - 1) / 2.0;
            var imageCenterY = (h - 1) / 2.0;
            var searchRadius = Math.Max(8, (int)Math.Round(Math.Min(w, h) * 0.14));
            var minX = Math.Max(0, (int)Math.Floor(imageCenterX - searchRadius));
            var maxX = Math.Min(w - 1, (int)Math.Ceiling(imageCenterX + searchRadius));
            var minY = Math.Max(0, (int)Math.Floor(imageCenterY - searchRadius));
            var maxY = Math.Min(h - 1, (int)Math.Ceiling(imageCenterY + searchRadius));

            // The scanner background is normally visible through the centre
            // hole. Start with its closest matching pixel to the image centre,
            // then follow only that connected region so white label lettering
            // cannot pull the measured centre away from the physical spindle.
            var seedX = -1;
            var seedY = -1;
            var closest = double.MaxValue;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var p = (y * w + x) * 4;
                if (BackgroundDistance(x, y) > 30
                    || Math.Abs(pixels[p + 3] - background.A) > 28) continue;
                var distance = Math.Pow(x - imageCenterX, 2) + Math.Pow(y - imageCenterY, 2);
                if (distance >= closest) continue;
                closest = distance;
                seedX = x;
                seedY = y;
            }
            if (seedX < 0) return false;

            var visited = new bool[w * h];
            var queue = new Queue<int>();
            var seed = seedY * w + seedX;
            visited[seed] = true;
            queue.Enqueue(seed);
            long sumX = 0, sumY = 0;
            var count = 0;
            var boundLeft = seedX;
            var boundRight = seedX;
            var boundTop = seedY;
            var boundBottom = seedY;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var x = current % w;
                var y = current / w;
                sumX += x;
                sumY += y;
                count++;
                boundLeft = Math.Min(boundLeft, x);
                boundRight = Math.Max(boundRight, x);
                boundTop = Math.Min(boundTop, y);
                boundBottom = Math.Max(boundBottom, y);

                void Visit(int nextX, int nextY)
                {
                    if (nextX < minX || nextX > maxX || nextY < minY || nextY > maxY) return;
                    var next = nextY * w + nextX;
                    if (visited[next]) return;
                    visited[next] = true;
                    var p = next * 4;
                    if (BackgroundDistance(nextX, nextY) > 30
                        || Math.Abs(pixels[p + 3] - background.A) > 28) return;
                    queue.Enqueue(next);
                }
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            var minimum = w * h * 0.001;
            var maximum = w * h * 0.04;
            var componentWidth = boundRight - boundLeft + 1;
            var componentHeight = boundBottom - boundTop + 1;
            var componentAspect = (double)componentWidth / componentHeight;
            if (count < minimum || count > maximum || componentAspect is < 0.68 or > 1.47)
                return false;
            hubX = (double)sumX / count;
            hubY = (double)sumY / count;
            return true;
        }

        bool TryFindPrintedDiscRadius(double centerX, double centerY, out double radius)
        {
            radius = 0;
            var minimumDimension = Math.Min(w, h);
            var firstRadius = Math.Max(8, (int)Math.Round(minimumDimension * 0.28));
            // Stop inside the image perimeter. This deliberately favours the
            // printed label-to-rim transition over the later rim-to-scanner
            // transition that previously left a white ring on the 3D disc.
            var lastRadius = (int)Math.Floor(minimumDimension * 0.485);
            if (lastRadius <= firstRadius + 8) return false;

            var bestScore = 0.0;
            var bestRadius = 0;
            const int rayCount = 96;
            var rayScores = new double[rayCount];
            for (var candidate = firstRadius; candidate <= lastRadius; candidate++)
            {
                for (var ray = 0; ray < rayCount; ray++)
                {
                    var angle = Math.PI * 2 * ray / rayCount;
                    var cosine = Math.Cos(angle);
                    var sine = Math.Sin(angle);
                    int X(double sampleRadius) => Math.Clamp(
                        (int)Math.Round(centerX + cosine * sampleRadius), 0, w - 1);
                    int Y(double sampleRadius) => Math.Clamp(
                        (int)Math.Round(centerY + sine * sampleRadius), 0, h - 1);
                    var innerX = X(candidate - 3);
                    var innerY = Y(candidate - 3);
                    var outerX = X(candidate + 3);
                    var outerY = Y(candidate + 3);
                    var inner = (innerY * w + innerX) * 4;
                    var outer = (outerY * w + outerX) * 4;
                    rayScores[ray] = Math.Sqrt(
                        Math.Pow(pixels[inner] - pixels[outer], 2)
                        + Math.Pow(pixels[inner + 1] - pixels[outer + 1], 2)
                        + Math.Pow(pixels[inner + 2] - pixels[outer + 2], 2));
                }
                Array.Sort(rayScores);
                // Median aggregation ignores lettering and isolated shadows;
                // a real circular boundary produces a change on most rays.
                var score = (rayScores[rayCount / 2 - 1] + rayScores[rayCount / 2]) / 2;
                if (score <= bestScore) continue;
                bestScore = score;
                bestRadius = candidate;
            }
            if (bestScore < 12 || bestRadius <= 0) return false;
            radius = bestRadius;
            return true;
        }

        BitmapSource CropAround(double centerX, double centerY, double diameter)
        {
            var scaleX = (double)width / w;
            var scaleY = (double)height / h;
            centerX *= scaleX;
            centerY *= scaleY;
            var requestedSide = Math.Clamp((int)Math.Floor(diameter * (scaleX + scaleY) / 2),
                1, Math.Min(width, height));
            var maximumCenteredSide = Math.Max(1, (int)Math.Floor(2 * Math.Min(
                Math.Min(centerX, width - centerX),
                Math.Min(centerY, height - centerY))));
            var side = Math.Min(requestedSide, maximumCenteredSide);
            return RearInsertArtwork.Crop(source, new Int32Rect(
                Math.Clamp((int)Math.Round(centerX - side / 2.0), 0, width - side),
                Math.Clamp((int)Math.Round(centerY - side / 2.0), 0, height - side),
                side, side));
        }

        var physicalCenterX = (w - 1) / 2.0;
        var physicalCenterY = (h - 1) / 2.0;
        if (cornersAreUniform
            && TryFindHubCenter(out var hubCenterX, out var hubCenterY))
        {
            physicalCenterX = hubCenterX;
            physicalCenterY = hubCenterY;
        }
        if (TryFindPrintedDiscRadius(physicalCenterX, physicalCenterY, out var printedRadius))
            return CropAround(physicalCenterX, physicalCenterY, printedRadius * 2 * 0.995);

        // Non-uniform corners used to disable cropping before the radial disc
        // detector was attempted. Keep the safe square fallback only when
        // both circle detection and a uniform scanner mat are unavailable.
        if (!cornersAreUniform) return CenterSquare(source);

        var minimumColumnPixels = Math.Max(2, (int)Math.Ceiling(h * 0.04));
        var minimumRowPixels = Math.Max(2, (int)Math.Ceiling(w * 0.04));
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
        // The detected bounds determine the diameter. Prefer the physical hub
        // centre when the scanner background is visible through it; otherwise
        // fall back to the exact image centre. This removes the residual wobble
        // present when a disc was placed a few pixels off-centre on the scanner.
        // The photographed/scanned metal rim can contain a one-sided pale
        // fringe even after the scanner mat has been rejected. Crop two per
        // cent inside the measured diameter so that fringe stays beyond the
        // circular 3D mesh while preserving the hub-centred registration.
        diameter = Math.Min(Math.Min(w, h),
            Math.Max(1, (int)Math.Floor(diameter * 0.98)));
        return CropAround(physicalCenterX, physicalCenterY, diameter);
    }

    private static BitmapSource CenterSquare(BitmapSource source)
    {
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        return RearInsertArtwork.Crop(source, new Int32Rect(
            (source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
    }
}
