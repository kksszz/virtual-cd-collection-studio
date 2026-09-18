using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        JewelCaseCoverFlowItem item;
        if (args.Length > 1)
        {
            // Exercise exactly the desktop's prepared artwork path, without a window or settings writes.
            var album = Directory.Exists(args[1]) ? ZipAlbumReader.OpenFolder(args[1]) : ZipAlbumReader.Open(args[1]);
            var type = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
            var model = Activator.CreateInstance(type, album)!;
            type.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(model, [2048]);
            object? Get(string name) => type.GetProperty(name)!.GetValue(model);
            item = new(album.Path, (string)Get("Title")!, (string)Get("Artist")!, "", (string)Get("TrayColorMode")!,
                (BitmapSource?)Get("CaseFrontThumbnail"), (BitmapSource?)Get("InsideFrontThumbnail"), (BitmapSource?)Get("BackCoverThumbnail"),
                (BitmapSource?)Get("SpineThumbnail"), (BitmapSource?)Get("RightSpineThumbnail"), (BitmapSource?)Get("InlayThumbnail"), (BitmapSource?)Get("DiscThumbnail"), false) { SpineCard = (BitmapSource?)Get("SpineCardThumbnail") };
        }
        else
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DarkSlateBlue, null, new Rect(0, 0, 1800, 1800));
                dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 900, 900));
                dc.DrawRectangle(Brushes.Turquoise, null, new Rect(900, 0, 900, 900));
                dc.DrawText(new FormattedText("FRONT\n3D TEST", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 210, Brushes.White, 1), new Point(180, 1100));
            }
            var image = new RenderTargetBitmap(1800,1800,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();
            var obiVisual = new DrawingVisual();
            using(var dc=obiVisual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DarkBlue,null,new Rect(0,0,800,1200));
                dc.DrawRectangle(Brushes.Crimson,null,new Rect(300,0,100,1200));
                dc.DrawRectangle(Brushes.Goldenrod,null,new Rect(400,0,400,1200));
                dc.DrawText(new FormattedText("BACK     S   FRONT",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),45,Brushes.White,1),new Point(20,130));
            }
            var obiImage=new RenderTargetBitmap(800,1200,96,96,PixelFormats.Pbgra32);obiImage.Render(obiVisual);obiImage.Freeze();
            var folds=typeof(MainWindow).Assembly.GetType("ZipMp3Player.SpineCardArtwork")!;
            folds.GetMethod("SetManualFolds")!.Invoke(null,[obiImage,300d/800,400d/800]);
            item = new("private-windows-path", "3D TEST", "Orientation / hinge", "DIR", "White", image, image, image, null, null, null, image, false) { SpineCard = obiImage };
        }
        MobileCaseExporter.Export(item, output);
        using var zip = ZipFile.OpenRead(output);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        var root = manifest.RootElement;
        if(root.GetProperty("version").GetInt32()!=2 || root.GetProperty("model").GetString()!="jewel-case-v2")throw new Exception("Schema");
        if(args.Length==1 && (Math.Abs(root.GetProperty("obi").GetProperty("frontWidthMm").GetDouble()-40)>0.1 || Math.Abs(root.GetProperty("obi").GetProperty("backWidthMm").GetDouble()-30)>0.1))throw new Exception("Manual folds / physical scale lost");
        foreach(var property in root.GetProperty("textures").EnumerateObject())
        {
            using var stream=zip.GetEntry(property.Value.GetProperty("file").GetString()!)!.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);
            if(Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant()!=property.Value.GetProperty("sha256").GetString())throw new Exception("Hash");
            bytes.Position=0;var image=BitmapFrame.Create(bytes,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            if(image.PixelWidth>1024 || image.PixelHeight>1024)throw new Exception("Size");
            Console.WriteLine($"PASS {property.Name}: {image.PixelWidth}x{image.PixelHeight}");
        }
        if(root.ToString().Contains(item.Key))throw new Exception("Windows path leaked");
        Console.WriteLine("PASS export: schema, hashes, bounded textures, no source path/audio; " + output);
    }
}
