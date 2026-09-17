using System.Net;
using System.Net.Http;
using ZipMp3Player;

internal static class ManualSearchTests
{
    internal static async Task RunAsync(byte[] image)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
        var sample = "Six Degrees Of Inner Turbulence [Bonus Track] [Disc 2]";
        Check(AlbumArtworkLookupService.SimplifyManualSearchTitle(sample) == "Six Degrees Of Inner Turbulence", "sample annotations removed");
        Check(AlbumArtworkLookupService.SimplifyManualSearchTitle("Album【ボーナストラック】【Disc ２】") == "Album", "Japanese brackets/fullwidth number");
        Check(AlbumArtworkLookupService.SimplifyManualSearchTitle("Album (Bonus Tracks) (CD 1/2)") == "Album", "parentheses and disc totals");
        foreach (var title in new[] { "Album (Deluxe)", "Album [Live at Budokan]", "[Disc 2]", "Album [Disc 2", "[Bonus Track] Album", "Album (Part 2)" })
            Check(AlbumArtworkLookupService.SimplifyManualSearchTitle(title) == title, "preserve " + title);
        Check(AlbumArtworkLookupService.SimplifyManualSearchTitle("Album [Live] [Disc 2]") == "Album [Live]", "preserve title while removing trailing disc");
        using var handler = new Handler(image);
        using var client = new HttpClient(handler);
        var service = new AlbumArtworkLookupService(client);
        string? fallback = null;
        var results = await service.SearchAsync(sample, "Dream Theater", default, text => fallback = text);
        Check(results.Count == 1 && handler.Queries.Count == 2 && handler.Queries[0].Contains("Bonus Track")
            && !handler.Queries[1].Contains("Bonus Track") && handler.Queries.All(q => q.Contains("Dream Theater"))
            && fallback == "Six Degrees Of Inner Turbulence", "original query then single simplified retry with same artist");
        handler.Queries.Clear(); handler.AlwaysMatch = true;
        await service.SearchAsync(sample, "Dream Theater", default);
        Check(handler.Queries.Count == 1, "original candidates prevent fallback");
        handler.Queries.Clear(); handler.AlwaysMatch = false;
        await service.SearchAsync("Unknown [Live]", "Dream Theater", default);
        Check(handler.Queries.Count == 1, "unrecognized annotations prevent retry");
        handler.Queries.Clear();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await service.SearchAsync(sample, "Dream Theater", cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(handler.Queries.Count == 0, "cancellation prevents requests"); }
    }

    private sealed class Handler(byte[] image) : HttpMessageHandler
    {
        internal List<string> Queries = [];
        internal bool AlwaysMatch;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri!.Host == "musicbrainz.org")
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                Queries.Add(query);
                var found = AlwaysMatch || (query.Contains("Six Degrees Of Inner Turbulence") && !query.Contains("Bonus Track"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(found
                    ? """{"release-groups":[{"id":"test","title":"Six Degrees Of Inner Turbulence","artist-credit":[{"name":"Dream Theater"}]}]}"""
                    : """{"release-groups":[]}""") });
            }
            var content = new ByteArrayContent(image); content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
