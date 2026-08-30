using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace ZipMp3Player;

public sealed record LyricsOcrResult(string Text, string Language, int LineCount);

public static class LyricsOcrService
{
    private static readonly Regex JapaneseCharacterGap = new(
        @"(?<=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}々〆〇])[ \t]+(?=[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}々〆〇])",
        RegexOptions.Compiled);

    public static bool IsJapaneseAvailable()
    {
        try { return OcrEngine.IsLanguageSupported(new Language("ja-JP")); }
        catch { return false; }
    }

    public static async Task<LyricsOcrResult> RecognizeJapaneseAsync(BitmapSource source, bool verticalLayout)
    {
        var language = new Language("ja-JP");
        var engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new NotSupportedException("Windowsの日本語OCRが利用できません。Windowsの言語設定で日本語の言語機能を追加してください。");
        var prepared = PrepareForOcr(source, OcrEngine.MaxImageDimension);
        var temporary = Path.Combine(Path.GetTempPath(), "ZipMp3Player-Ocr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(prepared));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                encoder.Save(output);

            var file = await StorageFile.GetFileFromPathAsync(temporary);
            using var stream = await file.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var text = verticalLayout ? FormatVertical(result) : FormatHorizontal(result);
            return new LyricsOcrResult(text.Trim(), engine.RecognizerLanguage.DisplayName, result.Lines.Count);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static BitmapSource PrepareForOcr(BitmapSource source, uint maximumDimension)
    {
        var max = Math.Max(source.PixelWidth, source.PixelHeight);
        if (max <= maximumDimension) return source;
        var scale = maximumDimension / (double)max;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private static string FormatHorizontal(OcrResult result) => string.Join(Environment.NewLine,
        result.Lines.Select(line => NormalizeJapaneseSpacing(line.Text.Trim())).Where(line => line.Length > 0));

    private static string FormatVertical(OcrResult result)
    {
        var words = result.Lines.SelectMany(line => line.Words).ToList();
        if (words.Count == 0) return "";
        var widths = words.Select(word => Math.Max(1, word.BoundingRect.Width)).OrderBy(width => width).ToList();
        var tolerance = widths[widths.Count / 2] * 1.25;
        var columns = new List<List<OcrWord>>();
        foreach (var word in words.OrderByDescending(CenterX))
        {
            var column = columns.FirstOrDefault(candidate => Math.Abs(candidate.Average(CenterX) - CenterX(word)) <= tolerance);
            if (column is null) columns.Add([word]);
            else column.Add(word);
        }
        return string.Join(Environment.NewLine, columns
            .OrderByDescending(column => column.Average(CenterX))
            .Select(column => NormalizeJapaneseSpacing(string.Concat(column.OrderBy(word => word.BoundingRect.Y)
                .ThenBy(word => word.BoundingRect.X).Select(word => word.Text.Trim()))))
            .Where(column => column.Length > 0));
    }

    private static double CenterX(OcrWord word) => word.BoundingRect.X + word.BoundingRect.Width / 2;
    private static string NormalizeJapaneseSpacing(string value) => JapaneseCharacterGap.Replace(value, "");
}
