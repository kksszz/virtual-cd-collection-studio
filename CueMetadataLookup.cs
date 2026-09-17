using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ZipMp3Player;

internal static class CueMetadataLookup
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
    private static readonly SemaphoreSlim Gate = new(1);
    private static DateTime _lastRequest;
    static CueMetadataLookup() => Client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Virtual-CD-Collection-Studio/0.80 (+https://github.com/kksszz/virtual-cd-collection-studio)");

    internal static async Task<List<CueMetadata>> SearchAsync(CueAlbumReader.Disc disc, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var delay = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            _lastRequest = DateTime.UtcNow;
            var uri = "https://musicbrainz.org/ws/2/discid/" + disc.DiscId
                + "?fmt=json&inc=artists+recordings&toc=" + Uri.EscapeDataString(disc.Toc);
            using var response = await Client.GetAsync(uri, token);
            if (response.StatusCode == HttpStatusCode.NotFound) return [];
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return Parse(json.RootElement, disc);
        }
        finally { Gate.Release(); }
    }
    internal static List<CueMetadata> Parse(JsonElement root, CueAlbumReader.Disc disc)
    {
        static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        static string Artist(JsonElement e)
        {
            if (!e.TryGetProperty("artist-credit", out var credits)) return "";
            return string.Concat(credits.EnumerateArray().Select(c => Text(c, "name") + Text(c, "joinphrase")));
        }
        var results = new List<CueMetadata>();
        if (!root.TryGetProperty("releases", out var releases)) return results;
        foreach (var release in releases.EnumerateArray())
        {
            if (!release.TryGetProperty("media", out var media)) continue;
            foreach (var medium in media.EnumerateArray())
            {
                if (!medium.TryGetProperty("tracks", out var tracks) || tracks.GetArrayLength() != disc.Tracks.Count) continue;
                var exact = medium.TryGetProperty("discs", out var discs) && discs.EnumerateArray().Any(d => Text(d, "id") == disc.DiscId);
                var durationsMatch = true;
                var names = new List<CueTrackMetadata>();
                var n = 0;
                foreach (var track in tracks.EnumerateArray())
                {
                    var recording = track.TryGetProperty("recording", out var r) ? r : track;
                    var title = Text(track, "title"); if (title.Length == 0) title = Text(recording, "title");
                    var artist = Artist(track); if (artist.Length == 0) artist = Artist(recording); if (artist.Length == 0) artist = Artist(release);
                    var duration = ((n + 1 < disc.Tracks.Count ? disc.Tracks[n + 1].Frame : disc.Frames) - disc.Tracks[n].Frame) / 75.0 * 1000;
                    if (!track.TryGetProperty("length", out var length) || !length.TryGetInt64(out var ms) || Math.Abs(ms - duration) > 2000) durationsMatch = false;
                    names.Add(new(title, artist)); n++;
                }
                if ((!exact && !durationsMatch) || names.Any(t => string.IsNullOrWhiteSpace(t.Title))) continue;
                var position = medium.TryGetProperty("position", out var p) ? p.GetInt32() : disc.DiscNumber;
                var description = $"{Artist(release)} — {Text(release, "title")} / {Text(release, "date")} / {Text(release, "country")} / Disc {position} / {(exact ? "Disc ID一致" : "曲数・曲時間の近似候補")}";
                results.Add(new(disc.Fingerprint, Text(release, "id"), Text(release, "title"), Text(release, "date").Split('-')[0],
                    position, media.GetArrayLength(), names, description));
            }
        }
        return results;
    }
}
