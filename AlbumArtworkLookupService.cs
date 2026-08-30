using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ZipMp3Player;

internal sealed class AlbumArtworkLookupService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim MusicBrainzGate = new(1, 1);
    private static DateTime _lastMusicBrainzRequestUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<AlbumArtworkCandidate>> SearchAsync(
        string album, string artist, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(album)) return [];

        var query = $"releasegroup:\"{EscapeQuery(album)}\"";
        if (!string.IsNullOrWhiteSpace(artist) && artist != "アーティスト不明")
            query += $" AND artist:\"{EscapeQuery(artist)}\"";

        var uri = "https://musicbrainz.org/ws/2/release-group/?fmt=json&limit=12&query="
            + Uri.EscapeDataString(query);
        using var response = await GetFromMusicBrainzAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("release-groups", out var groups)) return [];
        var results = new List<AlbumArtworkCandidate>();
        foreach (var group in groups.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= 6) break;
            var id = ReadString(group, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var image = await TryDownloadCoverAsync(id, cancellationToken);
            if (image is null) continue;

            var foundArtist = "アーティスト不明";
            if (group.TryGetProperty("artist-credit", out var credits))
            {
                var names = credits.EnumerateArray()
                    .Select(credit => ReadString(credit, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name));
                var joined = string.Join(" / ", names);
                if (!string.IsNullOrWhiteSpace(joined)) foundArtist = joined;
            }

            results.Add(new AlbumArtworkCandidate(
                id,
                ReadString(group, "title") ?? album,
                foundArtist,
                ReadString(group, "first-release-date") ?? "",
                image.Value.Bytes,
                image.Value.Extension));
        }
        return results;
    }

    private static async Task<HttpResponseMessage> GetFromMusicBrainzAsync(string uri, CancellationToken cancellationToken)
    {
        await MusicBrainzGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var remaining = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastMusicBrainzRequestUtc);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
                _lastMusicBrainzRequestUtc = DateTime.UtcNow;
                var response = await Client.GetAsync(uri, cancellationToken);
                if (response.StatusCode is not (HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests) || attempt >= 2)
                    return response;
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(1.5 * (attempt + 1)), cancellationToken);
            }
        }
        finally
        {
            MusicBrainzGate.Release();
        }
    }

    private static async Task<(byte[] Bytes, string Extension)?> TryDownloadCoverAsync(
        string releaseGroupId, CancellationToken cancellationToken)
    {
        try
        {
            var uri = $"https://coverartarchive.org/release-group/{releaseGroupId}/front-500";
            using var response = await Client.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var extension = mediaType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            return (await response.Content.ReadAsByteArrayAsync(cancellationToken), extension);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("zip.mp3-Player-and-Manager-Plus/0.45 (Windows album artwork lookup)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/*;q=0.9");
        return client;
    }

    private static string EscapeQuery(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Trim();

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}

public sealed record AlbumArtworkCandidate(
    string ReleaseGroupId,
    string Album,
    string Artist,
    string Date,
    byte[] ImageBytes,
    string Extension)
{
    public string Summary => string.IsNullOrWhiteSpace(Date)
        ? $"{Album}\n{Artist}"
        : $"{Album}\n{Artist}  •  {Date}";
}
