using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed partial class AlbumArtworkLookupService
{
    private readonly HttpClient _client;
    internal AlbumArtworkLookupService(HttpClient? client = null) => _client = client ?? Client;
    internal static bool ExactTextMatch(string left, string right)
    {
        static string Normalize(string text) => string.Join(" ", text.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
            && string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    // Never use fuzzy search ranking alone to choose an unattended download.
    public async Task<AlbumArtworkCandidate?> FindExactCoverAsync(string album, string artist, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist)
            || artist is "アーティスト不明" or "Unknown Artist") return null;
        var query = $"releasegroup:\"{EscapeQuery(album)}\" AND artist:\"{EscapeQuery(artist)}\"";
        using var response = await GetFromMusicBrainzAsync(
            "https://musicbrainz.org/ws/2/release-group/?fmt=json&limit=25&query=" + Uri.EscapeDataString(query), token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (document.RootElement.TryGetProperty("count", out var count) && count.GetInt32() > 25) return null;
        if (!document.RootElement.TryGetProperty("release-groups", out var groups)) return null;
        var matches = groups.EnumerateArray().Where(group =>
            ExactTextMatch(album, ReadString(group, "title") ?? "")
            && group.TryGetProperty("artist-credit", out var credits)
            && credits.GetArrayLength() == 1
            && ExactTextMatch(artist, ReadString(credits[0], "name") ?? "")).ToArray();
        if (matches.Length != 1) return null;
        var id = ReadString(matches[0], "id");
        if (!Guid.TryParse(id, out _)) return null;
        var cover = await TryDownloadCoverAsync(id!, token, 1200);
        if (cover is null || !IsSuitableAutomaticCover(cover.Value.Bytes)) return null;
        return new AlbumArtworkCandidate(id!, album, artist, ReadString(matches[0], "first-release-date") ?? "",
            cover.Value.Bytes, cover.Value.Extension);
    }

    internal static bool IsSuitableAutomaticCover(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024) return false;
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            var shortest = Math.Min(frame.PixelWidth, frame.PixelHeight);
            if (longest < 600 || longest > 4096 || shortest < 500 || (double)longest / shortest > 1.5) return false;
            // Validate decoding only after bounding the expanded image dimensions.
            var pixels = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            pixels.CopyPixels(new byte[frame.PixelWidth * frame.PixelHeight * 4], frame.PixelWidth * 4, 0);
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException
            or System.Runtime.InteropServices.COMException or IOException)
        {
            return false;
        }
    }
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

    private async Task<HttpResponseMessage> GetFromMusicBrainzAsync(string uri, CancellationToken cancellationToken)
    {
        await MusicBrainzGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var remaining = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - _lastMusicBrainzRequestUtc);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
                _lastMusicBrainzRequestUtc = DateTime.UtcNow;
                var response = await _client.GetAsync(uri, cancellationToken);
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

    private async Task<(byte[] Bytes, string Extension)?> TryDownloadCoverAsync(
        string releaseGroupId, CancellationToken cancellationToken, int size = 500)
    {
        try
        {
            var uri = $"https://coverartarchive.org/release-group/{releaseGroupId}/front-{size}";
            using var response = await _client.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var extension = mediaType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            return (await response.Content.ReadAsByteArrayAsync(cancellationToken), extension);
        }
        catch (HttpRequestException) when (size == 500)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(25),
            MaxResponseContentBufferSize = 20 * 1024 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Virtual-CD-Collection-Studio/0.80 (+https://github.com/kksszz/virtual-cd-collection-studio)");
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
    public string SourceUrl { get; init; } = "";
    public string Summary => string.IsNullOrWhiteSpace(Date)
        ? $"{Album}\n{Artist}"
        : $"{Album}\n{Artist}  •  {Date}";
}
