using System.Text;

namespace ZipMp3Player;

// Pure selection logic: no playback, filesystem access, or collection mutation.
internal sealed class AlbumTypeNavigation
{
    private string _prefix = "";
    private long _lastInput;

    public void Reset() => _prefix = "";

    public int Find(IReadOnlyList<string> labels, string input, int selectedIndex, long now)
    {
        input = Normalize(input);
        if (input.Length == 0 || !input.All(char.IsLetterOrDigit)) return -1;
        var continuing = _prefix.Length > 0 && now - _lastInput <= 1000;
        var cycle = continuing && input.Length == 1 && _prefix.Equals(input, StringComparison.OrdinalIgnoreCase);
        _prefix = continuing && !cycle ? _prefix + input : input;
        _lastInput = now;
        var match = Search(labels, _prefix, cycle ? selectedIndex + 1 : 0);
        if (match < 0 && _prefix != input)
        {
            // A failed continuation starts a new jump, rather than trapping subsequent input.
            _prefix = input;
            match = Search(labels, input, 0);
        }
        return match;
    }

    private static int Search(IReadOnlyList<string> labels, string prefix, int start)
    {
        if (labels.Count == 0) return -1;
        start = Math.Clamp(start, 0, labels.Count) % labels.Count;
        for (var offset = 0; offset < labels.Count; offset++)
        {
            var index = (start + offset) % labels.Count;
            if (Normalize(labels[index]).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).TrimStart();
}
