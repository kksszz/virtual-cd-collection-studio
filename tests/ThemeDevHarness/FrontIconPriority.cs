using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyFrontIconPriority()
    {
        var data = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")!;
        if (string.IsNullOrEmpty(data) || Directory.Exists(data)) throw new Exception("Fresh isolated test directory required");
        Directory.CreateDirectory(data);
        var folder = Path.Combine(data, "album");
        Directory.CreateDirectory(folder);
        string Save(string name)
        {
            var path = Path.Combine(folder, name);
            var bitmap = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Repeat((byte)255, 20 * 20 * 4).ToArray(), 20 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path);
            encoder.Save(output);
            return path;
        }
        var back = Save("Dream Theater (Back).png");
        var front = Save("Dream Theater (Front).png");
        var inside = Save("Dream Theater (Inside).png");
        var album = new ZipAlbum { Path = folder, Tracks = [new ZipTrack { SourcePath = Path.Combine(folder, "song.mp3") }] };
        var type = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
        var item = Activator.CreateInstance(type, [album, false])!;
        var load = type.GetMethod("LoadCoverThumbnail", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var saveRoles = typeof(MainWindow).GetMethod("SaveArtworkRoles", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Check(string expected, string label)
        {
            foreach (var pixels in new[] { 120, 384 })
            {
                var result = (ITuple)load.Invoke(item, [Path.Combine(data, "managed-empty"), pixels])!;
                if (result[0] is not BitmapSource || result[1] is not string description || !description.Contains(expected))
                    throw new Exception($"{label}, {pixels}px: {result[1]}");
                Console.WriteLine($"PASS {label}, {pixels}px");
            }
        }
        Check(Path.GetFileName(front), "long filename Front beats Back");
        var roles = new Dictionary<string, string> { ["file:" + back] = "BackWithSpines", ["file:" + front] = "Front", ["file:" + inside] = "FrontInside" };
        saveRoles.Invoke(null, [folder, roles]);
        Check(Path.GetFileName(front), "saved Front assignment beats Back and FrontInside");
        roles["file:" + front] = "FrontInside";
        roles["file:" + inside] = "Front";
        saveRoles.Invoke(null, [folder, roles]);
        Check(Path.GetFileName(inside), "explicit role overrides filename");
        roles["file:" + front] = "FrontSpread";
        saveRoles.Invoke(null, [folder, roles]);
        Check(Path.GetFileName(front), "spread priority and crop preserved");
        roles["file:" + front] = "FrontSpreadVertical";
        saveRoles.Invoke(null, [folder, roles]);
        Check(Path.GetFileName(inside), "Front precedes vertical spread like image panel");
        Console.WriteLine("PASS isolated icon selection; no real albums modified. " + data);
    }
}
