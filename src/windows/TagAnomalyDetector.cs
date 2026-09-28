using System.IO;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal sealed record TagAnomalyInput(
    string FileName, string Title, string Artist, string Album, string Year,
    string Genre, string TrackNumber, string DiscNumber);

internal sealed record TagAnomaly(string Code, string Message);

internal static class TagAnomalyDetector
{
    public static IReadOnlyList<TagAnomaly> Analyze(IReadOnlyList<TagAnomalyInput> rows)
    {
        var result = new List<TagAnomaly>();
        if (rows.Count == 0) return result;

        AddMissing(result, rows, "TITLE_MISSING", "タイトル", "title", row => row.Title);
        AddMissing(result, rows, "ARTIST_MISSING", "アーティスト", "artist", row => row.Artist);
        AddMissing(result, rows, "ALBUM_MISSING", "アルバム名", "album", row => row.Album);
        AddMissing(result, rows, "YEAR_MISSING", "年", "year", row => row.Year);
        AddMissing(result, rows, "GENRE_MISSING", "ジャンル", "genre", row => row.Genre);

        var numbered = rows.Select((row, index) => new ParsedRow(row, index + 1,
            ParsePositive(row.TrackNumber), ParsePositive(row.DiscNumber))).ToList();
        var invalidTrackRows = numbered.Where(item => !string.IsNullOrWhiteSpace(item.Row.TrackNumber) && item.Track is null)
            .Select(item => item.DisplayIndex).ToList();
        if (invalidTrackRows.Count > 0)
            result.Add(new TagAnomaly("TRACK_INVALID", LocalizationService.Select(
                $"曲番号が正の整数ではありません: 行 {JoinNumbers(invalidTrackRows)}",
                $"Track numbers are not positive integers: rows {JoinNumbers(invalidTrackRows)}")));

        var missingTrackRows = numbered.Where(item => item.Track is null && string.IsNullOrWhiteSpace(item.Row.TrackNumber))
            .Select(item => item.DisplayIndex).ToList();
        if (missingTrackRows.Count > 0)
            result.Add(new TagAnomaly("TRACK_MISSING", LocalizationService.Select(
                $"曲番号が未設定です: {missingTrackRows.Count}曲（行 {JoinNumbers(missingTrackRows)}）",
                $"Track number is missing on {missingTrackRows.Count} tracks (rows {JoinNumbers(missingTrackRows)})")));

        foreach (var discGroup in numbered.GroupBy(item => item.Disc ?? 0).OrderBy(group => group.Key))
        {
            var discLabel = discGroup.Key > 0 ? LocalizationService.Select($"Disc {discGroup.Key}", $"Disc {discGroup.Key}")
                : LocalizationService.Select("Disc未設定", "disc not set");
            var positive = discGroup.Where(item => item.Track is not null).ToList();
            var duplicates = positive.GroupBy(item => item.Track!.Value).Where(group => group.Count() > 1)
                .Select(group => group.Key).OrderBy(value => value).ToList();
            if (duplicates.Count > 0)
                result.Add(new TagAnomaly("TRACK_DUPLICATE", LocalizationService.Select(
                    $"{discLabel}の曲番号が重複しています: {JoinNumbers(duplicates)}",
                    $"Duplicate track numbers in {discLabel}: {JoinNumbers(duplicates)}")));

            var groupCount = discGroup.Count();
            var distinctTracks = positive.Select(item => item.Track!.Value).Distinct().OrderBy(value => value).ToList();
            var outliers = new List<uint>();
            var largeGap = (uint)Math.Max(3, groupCount);
            for (var index = 1; index < distinctTracks.Count; index++)
            {
                if (distinctTracks[index] - distinctTracks[index - 1] <= largeGap) continue;
                outliers.AddRange(distinctTracks.Skip(index));
                break;
            }
            if (outliers.Count > 0)
                result.Add(new TagAnomaly("TRACK_OUTLIER", LocalizationService.Select(
                    $"{discLabel}の連番から大きく離れた曲番号があります: {JoinNumbers(outliers)}",
                    $"Track numbers far outside the sequence in {discLabel}: {JoinNumbers(outliers)}")));

            var present = distinctTracks.Where(value => !outliers.Contains(value)).ToHashSet();
            var firstExpected = present.Count > 0 && present.Min() <= 3 ? 1u : present.DefaultIfEmpty(1u).Min();
            var lastExpected = present.DefaultIfEmpty(firstExpected).Max();
            var missing = FindMissingNumbers(present, firstExpected, lastExpected);
            if (missing.Count > 0)
                result.Add(new TagAnomaly("TRACK_GAPS", LocalizationService.Select(
                    $"{discLabel}の曲番号に欠番があります: {JoinNumbers(missing)}",
                    $"Missing track numbers in {discLabel}: {JoinNumbers(missing)}")));
        }

        var fileTrackPairs = numbered.Where(item => item.Track is not null)
            .Select(item => (item, fileTrack: InferTrackNumber(item.Row.FileName)))
            .Where(pair => pair.fileTrack is not null).ToList();
        var hasUniformOffset = fileTrackPairs.Count > 1
            && fileTrackPairs.Select(pair => (long)pair.fileTrack!.Value - pair.item.Track!.Value).Distinct().Count() == 1;
        var mismatches = fileTrackPairs.Where(_ => !hasUniformOffset)
            .Where(pair => pair.fileTrack != pair.item.Track)
            .Select(pair => $"{Path.GetFileName(pair.item.Row.FileName)} ({pair.fileTrack}→{pair.item.Track})")
            .Take(8).ToList();
        if (mismatches.Count > 0)
            result.Add(new TagAnomaly("FILENAME_TRACK_MISMATCH", LocalizationService.Select(
                $"ファイル名の番号とタグの曲番号が一致しません: {string.Join("、", mismatches)}",
                $"File-name and tag track numbers differ: {string.Join(", ", mismatches)}")));

        var albumNames = rows.Select(row => row.Album.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Take(6).ToList();
        if (albumNames.Count > 1)
            result.Add(new TagAnomaly("ALBUM_MIXED", LocalizationService.Select(
                $"アルバム名の表記が複数あります: {string.Join(" / ", albumNames)}",
                $"Multiple album names are present: {string.Join(" / ", albumNames)}")));
        return result;
    }

    private static void AddMissing(List<TagAnomaly> result, IReadOnlyList<TagAnomalyInput> rows,
        string code, string japaneseLabel, string englishLabel, Func<TagAnomalyInput, string> selector)
    {
        var indices = rows.Select((row, index) => (row, index: index + 1))
            .Where(item => string.IsNullOrWhiteSpace(selector(item.row))).Select(item => item.index).ToList();
        if (indices.Count == 0) return;
        result.Add(new TagAnomaly(code, LocalizationService.Select(
            $"{japaneseLabel}が未設定です: {indices.Count}曲（行 {JoinNumbers(indices)}）",
            $"Missing {englishLabel} on {indices.Count} tracks (rows {JoinNumbers(indices)})")));
    }

    private static uint? ParsePositive(string value)
        => uint.TryParse(value.Trim(), out var parsed) && parsed > 0 ? parsed : null;

    private static uint? InferTrackNumber(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var matches = Regex.Matches(stem, @"(?<!\d)(\d{1,3})(?!\d)");
        return matches.Count > 0 && uint.TryParse(matches[matches.Count - 1].Groups[1].Value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static List<uint> FindMissingNumbers(HashSet<uint> present, uint first, uint last)
    {
        var missing = new List<uint>();
        for (var value = first; value <= last && missing.Count < 256; value++)
        {
            if (!present.Contains(value)) missing.Add(value);
            if (value == uint.MaxValue) break;
        }
        return missing;
    }

    private static string JoinNumbers<T>(IReadOnlyCollection<T> values)
        => string.Join(", ", values.Take(16)) + (values.Count > 16 ? " …" : "");

    private sealed record ParsedRow(TagAnomalyInput Row, int DisplayIndex, uint? Track, uint? Disc);
}
