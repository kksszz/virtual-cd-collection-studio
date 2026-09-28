using ZipMp3Player;

var lrc = LyricsDocument.Parse("[ar:Artist]\n[00:05.00]最初の行\n[00:10.50][00:20.00]次の行\n[offset:500]");
Require(lrc.HasTiming, "LRC timing detection");
Require(lrc.DisplayText == $"最初の行{Environment.NewLine}次の行", "timing tag removal");
Require(lrc.GetLineAt(5.0, 30) == -1, "no highlight before first timestamp");
Require(lrc.GetLineAt(5.6, 30) == 0, "first timed line with offset");
Require(lrc.GetLineAt(11.1, 30) == 1, "second timed line with offset");
Require(lrc.TimedLines.Count == 3, "multiple timestamps");
var secondSpan = lrc.GetTextSpan(1);
Require(lrc.DisplayText.Substring(secondSpan.Start, secondSpan.Length) == "次の行", "timed display line text span");

var flattened = LyricsDocument.Parse("[id:test] [ti:閃光] [00:01.16]Intro [00:09.67]Lyrics [00:19.28] [00:34.71]First line [00:38.49]Second line");
Require(flattened.HasTiming && !flattened.DisplayText.Contains("[00:") && !flattened.DisplayText.Contains("[id:"),
    "flattened LRC metadata/timestamp removal");
Require(flattened.DisplayText == string.Join(Environment.NewLine, ["Intro", "Lyrics", "", "First line", "Second line"]),
    "flattened LRC line reconstruction");
Require(flattened.GetLineAt(20, 60) == 2 && flattened.GetLineAt(35, 60) == 3,
    "flattened LRC synchronized line mapping");

var plain = LyricsDocument.Parse("A\n\nB\nC");
Require(!plain.HasTiming, "plain lyrics detection");
Require(plain.GetLineAt(0, 100) == 0, "estimated beginning");
Require(plain.GetLineAt(100, 100) == 3, "estimated ending");

Console.WriteLine("LRC synchronization and estimated lyric scrolling tests passed.");

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Failed: {name}");
}
