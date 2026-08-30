using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal sealed class LyricsDocument
{
    private static readonly Regex TimeTag = new(@"\[(?<m>\d{1,3}):(?<s>\d{2})(?:[.:](?<f>\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex MetadataTag = new(@"\[(?:ar|ti|al|by|offset|re|ve|id|au|la|length):[^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static LyricsDocument Empty { get; } = new("", [], []);
    public string DisplayText { get; }
    public IReadOnlyList<TimedLyricLine> TimedLines { get; }
    public IReadOnlyList<int> ContentLineIndices { get; }
    public bool HasTiming => TimedLines.Count > 0;

    private LyricsDocument(string displayText, IReadOnlyList<TimedLyricLine> timedLines, IReadOnlyList<int> contentLineIndices)
    {
        DisplayText = displayText;
        TimedLines = timedLines;
        ContentLineIndices = contentLineIndices;
    }

    public static LyricsDocument Parse(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return Empty;
        var normalized = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\0', '\n', ' ');
        var sourceLines = normalized.Split('\n');
        var displayLines = new List<string>();
        var timedLines = new List<TimedLyricLine>();
        var contentLines = new List<int>();
        var offsetMilliseconds = 0;
        var offsetLine = Regex.Match(normalized, @"\[offset:(?<value>[+-]?\d+)\]", RegexOptions.IgnoreCase);
        if (offsetLine?.Success == true) int.TryParse(offsetLine.Groups["value"].Value, out offsetMilliseconds);

        void AddDisplayLine(string text, IEnumerable<Match> timeMatches)
        {
            var displayLineIndex = displayLines.Count;
            displayLines.Add(text);
            if (!string.IsNullOrWhiteSpace(text)) contentLines.Add(displayLineIndex);
            foreach (var match in timeMatches)
            {
                if (!int.TryParse(match.Groups["m"].Value, out var minutes)
                    || !int.TryParse(match.Groups["s"].Value, out var seconds)) continue;
                var fractionText = match.Groups["f"].Value;
                var fraction = string.IsNullOrEmpty(fractionText) ? 0
                    : int.Parse(fractionText) / Math.Pow(10, fractionText.Length);
                timedLines.Add(new TimedLyricLine(
                    Math.Max(0, minutes * 60 + seconds + fraction + offsetMilliseconds / 1000.0), displayLineIndex));
            }
        }

        foreach (var sourceLine in sourceLines)
        {
            var trimmed = sourceLine.Trim();
            var matches = TimeTag.Matches(sourceLine).Cast<Match>().ToList();
            if (matches.Count == 0)
            {
                var hasMetadata = MetadataTag.IsMatch(trimmed);
                var plainText = MetadataTag.Replace(sourceLine, "").TrimEnd();
                if (!hasMetadata || !string.IsNullOrWhiteSpace(plainText)) AddDisplayLine(plainText, []);
                continue;
            }

            if (matches.Count == 1)
            {
                var text = MetadataTag.Replace(TimeTag.Replace(sourceLine, ""), "").Trim();
                AddDisplayLine(text, matches);
                continue;
            }

            // LRC Makerなどから一行のままコピーされたLRCも、各タイムタグごとの表示行へ戻す。
            var pendingTimes = new List<Match>();
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                pendingTimes.Add(match);
                var segmentStart = match.Index + match.Length;
                var segmentEnd = index + 1 < matches.Count ? matches[index + 1].Index : sourceLine.Length;
                var segment = sourceLine[segmentStart..segmentEnd];
                if (segment.Length == 0) continue; // 連続タグは同じ歌詞の複数時刻

                var text = MetadataTag.Replace(segment, "").Trim();
                AddDisplayLine(text, pendingTimes);
                pendingTimes.Clear();
            }
            if (pendingTimes.Count > 0) AddDisplayLine("", pendingTimes);
        }

        return new LyricsDocument(string.Join(Environment.NewLine, displayLines),
            timedLines.OrderBy(line => line.TimeSeconds).ToList(), contentLines);
    }

    public int GetLineAt(double positionSeconds, double durationSeconds)
    {
        if (HasTiming)
        {
            var line = -1;
            foreach (var candidate in TimedLines)
            {
                if (candidate.TimeSeconds > positionSeconds + 0.08) break;
                line = candidate.DisplayLineIndex;
            }
            return line;
        }
        if (ContentLineIndices.Count == 0 || durationSeconds <= 0) return -1;
        var start = Math.Min(8, durationSeconds * 0.06);
        var end = Math.Max(start + 1, durationSeconds - Math.Min(10, durationSeconds * 0.06));
        var progress = Math.Clamp((positionSeconds - start) / (end - start), 0, 1);
        var index = (int)Math.Round(progress * (ContentLineIndices.Count - 1));
        return ContentLineIndices[index];
    }

    public (int Start, int Length) GetTextSpan(int displayLineIndex)
    {
        if (displayLineIndex < 0) return (-1, 0);
        var lines = DisplayText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        if (displayLineIndex >= lines.Length) return (-1, 0);
        var start = 0;
        for (var index = 0; index < displayLineIndex; index++)
            start += lines[index].Length + Environment.NewLine.Length;
        return (start, lines[displayLineIndex].Length);
    }
}

internal sealed record TimedLyricLine(double TimeSeconds, int DisplayLineIndex);
