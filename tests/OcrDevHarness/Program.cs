using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

if (!LyricsOcrService.IsJapaneseAvailable())
    throw new InvalidOperationException("Japanese Windows OCR language is unavailable.");

var visual = new DrawingVisual();
using (var drawing = visual.RenderOpen())
{
    drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1000, 360));
    var typeface = new Typeface(new FontFamily("Yu Gothic UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    drawing.DrawText(new FormattedText("東京戦心", CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
        typeface, 92, Brushes.Black, 1), new Point(80, 45));
    drawing.DrawText(new FormattedText("音楽を聴こう", CultureInfo.GetCultureInfo("ja-JP"), FlowDirection.LeftToRight,
        typeface, 72, Brushes.Black, 1), new Point(80, 190));
}
var bitmap = new RenderTargetBitmap(1000, 360, 96, 96, PixelFormats.Pbgra32);
bitmap.Render(visual);
bitmap.Freeze();

var result = await LyricsOcrService.RecognizeJapaneseAsync(bitmap, verticalLayout: false);
if (string.IsNullOrWhiteSpace(result.Text) || result.LineCount < 1 || !result.Text.Contains("東京戦心", StringComparison.Ordinal))
    throw new InvalidOperationException("Japanese OCR returned no text.");
Console.WriteLine($"Japanese OCR test passed: {result.Text.Replace(Environment.NewLine, " / ")}");
