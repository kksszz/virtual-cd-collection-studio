using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class Program
{
    [STAThread]
    private static void Main() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        Check(AlbumArtworkLookupService.ExactTextMatch(" Ａｕｒａ  ", "aura"), "width/case/whitespace normalization");
        Check(!AlbumArtworkLookupService.ExactTextMatch("Aura (Deluxe)", "Aura"), "edition suffix not discarded");
        Check(!AlbumArtworkLookupService.ExactTextMatch("", ""), "empty metadata rejected");
        var high = MakePng(1200, 1200);
        var low = MakePng(500, 500);
        Check(AlbumArtworkLookupService.IsSuitableAutomaticCover(high), "1200px accepted");
        Check(!AlbumArtworkLookupService.IsSuitableAutomaticCover(low), "500px rejected");
        Check(AlbumArtworkLookupService.IsSuitableAutomaticCover(MakePng(600, 600)), "600px boundary accepted");
        Check(!AlbumArtworkLookupService.IsSuitableAutomaticCover([1, 2, 3]), "invalid image rejected");
        Check(!AlbumArtworkLookupService.IsSuitableAutomaticCover(MakePng(1200, 400)), "banner rejected");
        var handler = new FakeHandler { Image = high };
        using var client = new HttpClient(handler);
        var lookup = new AlbumArtworkLookupService(client);
        Check(await lookup.FindExactCoverAsync("Aura", "Unknown Artist", default) is null && handler.Requests == 0,
            "unknown artist sends no request");
        handler.Json = Results("Aura", "Fair Warning", 1);
        var cover = await lookup.FindExactCoverAsync("Aura", "Fair Warning", default);
        Check(cover is not null && cover.ImageBytes.SequenceEqual(high), "exact candidate retains original bytes");
        Check(handler.LastImageUri?.EndsWith("/front-1200") == true, "1200 endpoint requested");
        handler.Json = Results("Aura", "Other Artist", 1);
        var before = handler.ImageRequests;
        Check(await lookup.FindExactCoverAsync("Aura", "Fair Warning", default) is null
            && handler.ImageRequests == before, "wrong artist rejected before download");
        handler.Json = Results("Aura", "Fair Warning", 2);
        Check(await lookup.FindExactCoverAsync("Aura", "Fair Warning", default) is null, "ambiguous groups rejected");
        handler.Json = Results("Aura", "Fair Warning", 1);
        handler.Image = low;
        Check(await lookup.FindExactCoverAsync("Aura", "Fair Warning", default) is null, "low resolution API result rejected");
        handler.Image = high;
        handler.ImageStatus = HttpStatusCode.NotFound;
        Check(await lookup.FindExactCoverAsync("Aura", "Fair Warning", default) is null, "missing cover skipped");
        handler.ImageStatus = HttpStatusCode.BadGateway;
        try { await lookup.FindExactCoverAsync("Aura", "Fair Warning", default); throw new Exception("HTTP failure swallowed"); }
        catch (HttpRequestException) { Console.WriteLine("PASS HTTP error remains distinguishable from no match"); }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        before = handler.Requests;
        try { await lookup.FindExactCoverAsync("Aura", "Fair Warning", cancellation.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(handler.Requests == before, "cancellation prevents requests"); }
        Console.WriteLine("All artwork checks passed (fake HTTP only; no album/settings writes).");
        await ProviderTests.RunAsync(high, low);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    private static byte[] MakePng(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[width * height * 4], width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string Results(string title, string artist, int count) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["count"] = count,
        ["release-groups"] = Enumerable.Range(0, count).Select(_ => new Dictionary<string, object>
        {
            ["id"] = Guid.NewGuid().ToString(), ["title"] = title,
            ["artist-credit"] = new[] { new { name = artist } }
        }).ToArray()
    });

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string Json = "{}";
        public byte[] Image = [];
        public int Requests, ImageRequests;
        public string? LastImageUri;
        public HttpStatusCode ImageStatus = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests++;
            if (request.RequestUri!.Host == "musicbrainz.org")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) });
            ImageRequests++;
            LastImageUri = request.RequestUri.ToString();
            var content = new ByteArrayContent(Image);
            content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(ImageStatus) { Content = content });
        }
    }
}
