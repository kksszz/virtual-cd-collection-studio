using System.Text.Json;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyDiscTrackOrder()
    {
        ZipTrack Track(string name, int disc, int number) => new() { FileName = name, DiscNumber = disc, TrackNumber = number };
        void Check(ZipAlbum album, string expected, string label)
        {
            var actual = string.Join(",", album.Tracks.Select(track => track.FileName));
            if (actual != expected) throw new Exception($"{label}: {actual}");
            Console.WriteLine("PASS " + label);
        }
        var album = new ZipAlbum { Tracks = [Track("2-2", 2, 2), Track("1-2", 1, 2), Track("2-1", 2, 1), Track("1-1", 1, 1)] };
        Check(album, "1-1,1-2,2-1,2-2", "disc then track for display/playback source");
        var legacy = """{"Tracks":[{"FileName":"1-1","DiscNumber":1,"TrackNumber":1},{"FileName":"2-1","DiscNumber":2,"TrackNumber":1},{"FileName":"1-2","DiscNumber":1,"TrackNumber":2},{"FileName":"2-2","DiscNumber":2,"TrackNumber":2}]}""";
        Check(JsonSerializer.Deserialize<ZipAlbum>(legacy)!, "1-1,1-2,2-1,2-2", "old cached interleaved order repaired without rescan");
        Check(JsonSerializer.Deserialize<ZipAlbum>(JsonSerializer.Serialize(album))!, "1-1,1-2,2-1,2-2", "cache round trip");
        Check(new ZipAlbum { Tracks = [Track("ten", 10, 1), Track("two", 2, 1), Track("one", 1, 1)] }, "one,two,ten", "numeric disc order");
        Check(new ZipAlbum { Tracks = [Track("b", 0, 2), Track("a", 0, 1)] }, "a,b", "single disc without disc tags");
        Check(new ZipAlbum { Tracks = [Track("unknown", 0, 1), Track("known", 1, 1)] }, "known,unknown", "unknown disc last");
        Check(new ZipAlbum { Tracks = [Track("unknown", 1, 0), Track("known", 1, 1)] }, "known,unknown", "unknown track last");
        Check(new ZipAlbum { Tracks = [Track("b", 1, 1), Track("a", 1, 1)] }, "b,a", "duplicate keys stable");
        Check(new ZipAlbum(), "", "empty album");
    }
}
