using System.Net;
using System.Net.Http;
using ZipMp3Player;

internal static class ProviderTests
{
    public static async Task RunAsync(byte[] high, byte[] low)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
        Check(AlbumArtworkLookupService.SearchAlbumTitle("Album [Disc 2]") == "Album", "disc suffix search correction");
        Check(AlbumArtworkLookupService.SearchAlbumTitle("Album Disc 1") == "Album", "plain disc suffix");
        Check(AlbumArtworkLookupService.SearchAlbumTitle("Album (Deluxe)") == "Album (Deluxe)", "edition preserved");
        Check(!AlbumArtworkLookupService.TracksCorroborate(["A", "B", "C", "D"], ["A", "B", "X"]), "weak tracks rejected");
        Check(AlbumArtworkLookupService.TracksCorroborate(["A", "B", "C", "D"], ["A", "B", "C"]), "70 percent tracks corroborate");
        Check(!AlbumArtworkLookupService.IsAudioDbImageUrl("https://theaudiodb.com.evil.example/image.jpg"), "foreign image host rejected");
        Check(!AlbumArtworkLookupService.IsAudioDbImageUrl("http://www.theaudiodb.com/image.jpg"), "insecure image rejected");
        var handler = new Handler(high);
        using var client = new HttpClient(handler);
        var service = new AlbumArtworkLookupService(client);
        var result = await service.FindAutomaticCoverAsync("Album", "Artist", [], default);
        Check(result.Candidate?.SourceUrl == "https://www.theaudiodb.com/images/cover.jpg", "AudioDB fallback and provenance");
        handler.Image = low;
        result = await service.FindAutomaticCoverAsync("Album", "Artist", [], default);
        Check(result.Candidate is null && result.Detail.Contains("画質条件未達"), "fallback low quality reason");
        handler.Image = high;
        handler.WrongArtist = true;
        result = await service.FindAutomaticCoverAsync("Album", "Artist", [], default);
        Check(result.Candidate is null && result.Detail.Contains("名前不一致"), "fallback wrong artist rejected");
        handler.WrongArtist = false;
        handler.MusicBrainzError = true;
        result = await service.FindAutomaticCoverAsync("Album", "Artist", [], default);
        Check(result.Candidate is not null && result.Detail.Contains("HTTP 502"), "provider failure does not block fallback");
        handler.MusicBrainzError = false;
        handler.ReleaseMode = true;
        result = await service.FindAutomaticCoverAsync("Album", "Artist", [], default);
        Check(result.Candidate?.SourceUrl.Contains("/release/") == true, "individual release fallback after missing representative art");
        result = await service.FindAutomaticCoverAsync("Album Disc 1", "Artist", ["A", "B", "C"], default);
        Check(result.Candidate?.SourceUrl.Contains("/release/") == true, "corrected title uses track-verified release art");
        result = await service.FindAutomaticCoverAsync("Album Disc 1", "Artist", ["X", "Y", "Z"], default);
        Check(result.Candidate is null && result.Detail.Contains("要確認"), "unverified corrected title not saved");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await service.FindAutomaticCoverAsync("Album", "Artist", [], canceled.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { Console.WriteLine("PASS multi-provider cancellation"); }
    }

    private sealed class Handler(byte[] image) : HttpMessageHandler
    {
        public byte[] Image = image;
        public bool WrongArtist, MusicBrainzError, ReleaseMode;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.Host == "coverartarchive.org" && uri.AbsolutePath.Contains("/release-group/"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (uri.Host == "musicbrainz.org")
            {
                if (MusicBrainzError) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
                if (!ReleaseMode) return Json("{\"count\":0,\"release-groups\":[]}");
                if (uri.AbsolutePath.Contains("release-group")) return Json("""
                    {"count":1,"release-groups":[{"id":"00000000-0000-0000-0000-000000000001","title":"Album","artist-credit":[{"name":"Artist"}]}]}
                    """);
                if (uri.AbsolutePath.EndsWith("/release")) return Json("""
                    {"releases":[{"id":"00000000-0000-0000-0000-000000000002","title":"Album"}]}
                    """);
                return Json("""
                    {"artist-credit":[{"name":"Artist"}],"media":[{"tracks":[{"title":"A"},{"title":"B"},{"title":"C"}]}]}
                    """);
            }
            if (uri.AbsolutePath.Contains("searchalbum")) return Json("{\"album\":[{\"idAlbum\":\"12345\",\"strAlbum\":\"Album\",\"strArtist\":\""
                + (WrongArtist ? "Other" : "Artist") + "\",\"strAlbumThumb\":\"https://www.theaudiodb.com/images/cover.jpg\"}]}");
            var content = new ByteArrayContent(Image); content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
        private static Task<HttpResponseMessage> Json(string json) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json) });
    }
}
