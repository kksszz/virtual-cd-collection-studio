using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal sealed record ArtworkLookupOutcome(AlbumArtworkCandidate? Candidate, string Detail, bool TransientFailure);

internal sealed partial class AlbumArtworkLookupService
{
    private static readonly SemaphoreSlim AudioDbGate = new(1, 1);
    private static DateTime _nextAudioDbRequestUtc;

    internal static string SearchAlbumTitle(string album) => Regex.Replace(album,
        @"\s*(?:[\[(]\s*(?:disc|cd)\s*\d+\s*[\])]|(?:disc|cd)\s+\d+)\s*$", "",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

    internal static bool TracksCorroborate(IReadOnlyList<string> local, IReadOnlyList<string> remote)
    {
        var titles = local.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = titles.Count(title => remote.Any(other => ExactTextMatch(title, other)));
        return titles.Length >= 3 && matches >= 3 && matches >= Math.Ceiling(titles.Length * 0.7);
    }

    public async Task<ArtworkLookupOutcome> FindAutomaticCoverAsync(string album, string artist,
        IReadOnlyList<string> tracks, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(album) || album == "タイトルなし" || string.IsNullOrWhiteSpace(artist)
            || artist is "Unknown Artist" or "アーティスト不明")
            return new(null, "メタデータ不足", false);
        var notes = new List<string>();
        var failed = false;
        async Task<AlbumArtworkCandidate?> TryProvider(string name, Func<Task<AlbumArtworkCandidate?>> lookup)
        {
            try { return await lookup(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                failed = true;
                notes.Add(name + ": " + (ex is HttpRequestException http
                    ? http.StatusCode is { } status ? $"HTTP {(int)status}" : "通信失敗"
                    : ex is OperationCanceledException ? "タイムアウト" : "応答形式不正"));
                return null;
            }
        }
        var result = await TryProvider("MusicBrainz/CAA", () => FindMusicBrainzExpandedAsync(album, artist, tracks, notes, token));
        if (result is not null) return new(result, string.Join(" / ", notes), failed);
        result = await TryProvider("TheAudioDB", () => FindAudioDbAsync(album, artist, notes, token));
        return new(result, string.Join(" / ", notes), failed);
    }

    private async Task<AlbumArtworkCandidate?> FindMusicBrainzExpandedAsync(string album, string artist,
        IReadOnlyList<string> tracks, List<string> notes, CancellationToken token)
    {
        var title = SearchAlbumTitle(album);
        var corrected = !ExactTextMatch(title, album);
        var query = $"releasegroup:\"{EscapeQuery(title)}\" AND artist:\"{EscapeQuery(artist)}\"";
        using var response = await GetFromMusicBrainzAsync("https://musicbrainz.org/ws/2/release-group/?fmt=json&limit=25&query="
            + Uri.EscapeDataString(query), token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("release-groups", out var groups))
            throw new JsonException();
        var matches = groups.EnumerateArray().Where(g => ExactTextMatch(title, ReadString(g, "title") ?? "")
            && MatchesArtist(g, artist)).ToArray();
        if (matches.Length != 1 || (json.RootElement.TryGetProperty("count", out var count) && count.GetInt32() > 25))
        {
            notes.Add(matches.Length == 0 ? "MusicBrainz: 名前不一致・未登録" : "MusicBrainz: 複数候補（要確認）");
            return null;
        }
        var groupId = ReadString(matches[0], "id")!;
        if (!Guid.TryParse(groupId, out _)) throw new JsonException();
        if (!corrected)
        {
            var groupCover = await DownloadAutomaticImageAsync($"https://coverartarchive.org/release-group/{groupId}/front-1200",
                "CAA代表画像", groupId, album, artist, notes, token);
            if (groupCover is not null) return groupCover;
        }
        using var releasesResponse = await GetFromMusicBrainzAsync(
            $"https://musicbrainz.org/ws/2/release?release-group={groupId}&fmt=json&limit=5", token);
        releasesResponse.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await releasesResponse.Content.ReadAsStringAsync(token));
        if (!releases.RootElement.TryGetProperty("releases", out var releaseList)) throw new JsonException();
        foreach (var release in releaseList.EnumerateArray().Take(5))
        {
            if (!ExactTextMatch(title, ReadString(release, "title") ?? "")) continue;
            var id = ReadString(release, "id");
            if (!Guid.TryParse(id, out _)) continue;
            using var detailResponse = await GetFromMusicBrainzAsync(
                $"https://musicbrainz.org/ws/2/release/{id}?fmt=json&inc=recordings+artist-credits", token);
            detailResponse.EnsureSuccessStatusCode();
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync(token));
            if (!MatchesArtist(detail.RootElement, artist)) continue;
            if (corrected)
            {
                var remote = new List<string>();
                if (detail.RootElement.TryGetProperty("media", out var media))
                    foreach (var medium in media.EnumerateArray())
                        if (medium.TryGetProperty("tracks", out var songs))
                            remote.AddRange(songs.EnumerateArray().Select(song => ReadString(song, "title") ?? ""));
                if (!TracksCorroborate(tracks, remote)) continue;
            }
            var cover = await DownloadAutomaticImageAsync($"https://coverartarchive.org/release/{id}/front-1200",
                "CAA個別発売盤", id!, album, artist, notes, token);
            if (cover is not null) return cover;
        }
        notes.Add(corrected ? "MusicBrainz: 曲目照合済み画像なし（要確認）" : "CAA: 個別発売盤にも適合画像なし");
        return null;
    }

    private static bool MatchesArtist(JsonElement entity, string artist) =>
        entity.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array
        && credits.GetArrayLength() == 1 && ExactTextMatch(artist, ReadString(credits[0], "name") ?? "");

    private async Task<AlbumArtworkCandidate?> FindAudioDbAsync(string album, string artist, List<string> notes, CancellationToken token)
    {
        // The documented free development API: do not scrape HTML or invent image URLs.
        await AudioDbGate.WaitAsync(token);
        HttpResponseMessage response;
        try
        {
            var delay = _nextAudioDbRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            _nextAudioDbRequestUtc = DateTime.UtcNow.AddSeconds(2.1);
            response = await _client.GetAsync("https://www.theaudiodb.com/api/v1/json/123/searchalbum.php?s="
                + Uri.EscapeDataString(artist) + "&a=" + Uri.EscapeDataString(album), token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                _nextAudioDbRequestUtc = DateTime.UtcNow.AddMinutes(1);
        }
        finally { AudioDbGate.Release(); }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("album", out var albums)) throw new JsonException();
            var matches = albums.ValueKind == JsonValueKind.Array ? albums.EnumerateArray().Where(a =>
                ExactTextMatch(album, ReadString(a, "strAlbum") ?? "")
                && ExactTextMatch(artist, ReadString(a, "strArtist") ?? "")).ToArray() : [];
            if (matches.Length != 1)
            {
                notes.Add(matches.Length == 0 ? "TheAudioDB: 名前不一致・未登録" : "TheAudioDB: 複数候補（要確認）");
                return null;
            }
            var id = ReadString(matches[0], "idAlbum");
            if (id is null || !Regex.IsMatch(id, @"^\d{1,20}$")) throw new JsonException();
            foreach (var url in new[] { ReadString(matches[0], "strAlbumThumbHQ"), ReadString(matches[0], "strAlbumThumb") }
                .Where(IsAudioDbImageUrl).Distinct())
            {
                var cover = await DownloadAutomaticImageAsync(url!, "TheAudioDB", "audiodb-" + id, album, artist, notes, token);
                if (cover is not null) return cover;
            }
            notes.Add("TheAudioDB: 適合画像なし");
            return null;
        }
    }

    internal static bool IsAudioDbImageUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host == "theaudiodb.com" || uri.Host.EndsWith(".theaudiodb.com", StringComparison.OrdinalIgnoreCase));

    private async Task<AlbumArtworkCandidate?> DownloadAutomaticImageAsync(string url, string provider, string id,
        string album, string artist, List<string> notes, CancellationToken token)
    {
        using var response = await _client.GetAsync(url, token);
        if (response.StatusCode == HttpStatusCode.NotFound) { notes.Add(provider + ": 画像なし"); return null; }
        response.EnsureSuccessStatusCode();
        var mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is not ("image/jpeg" or "image/png")) { notes.Add(provider + ": 未対応画像形式"); return null; }
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (!IsSuitableAutomaticCover(bytes)) { notes.Add(provider + ": 画質条件未達／画像破損"); return null; }
        return new(id, album, artist, "", bytes, mime == "image/png" ? ".png" : ".jpg") { SourceUrl = url };
    }
}
