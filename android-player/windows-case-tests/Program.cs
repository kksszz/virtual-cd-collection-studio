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
        if(args.Length==2&&args[0]=="--sync-table"){SyncStyleChecks.RunTable(args[1]);return;}
        if(args.Length==1&&args[0]=="--qr-status-layout"){
            var app=new Application();
            var status=new System.Windows.Controls.TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=14};
            var box=(System.Windows.Controls.Border)typeof(MobileSyncQrWindow).GetMethod("CreateStatusPanel",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[status])!;
            var panel=new System.Windows.Controls.DockPanel();System.Windows.Controls.DockPanel.SetDock(box,System.Windows.Controls.Dock.Bottom);panel.Children.Add(box);
            var qr=new System.Windows.Controls.Border();panel.Children.Add(qr);
            foreach(double width in new[]{340d,420d})foreach(var message in new[]{"接続待ち","Xperiaからの受信状況\nPCから転送中 · 40% · 128.8 MiB / 316.6 MiB\n受信 128.8 MiB · 再利用 0.0 MiB · 4 / 12ファイル",string.Join("\n",Enumerable.Repeat(new string('長',120),20)),"同期完了"}){
                status.Text=message;panel.Measure(new Size(width,440));panel.Arrange(new Rect(0,0,width,440));panel.UpdateLayout();
                if(Math.Abs(box.ActualHeight-112)>.1||Math.Abs(qr.ActualHeight-328)>.1||Math.Abs(qr.TranslatePoint(new Point(),panel).Y)>.1)throw new Exception("QR moved on status update");
            }
            Console.WriteLine("PASS QR status layout: fixed panel/QR geometry at two widths, waiting/progress/long error/completion");return;
        }
        if(args.Length==1&&args[0]=="--disc-pull"){
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DiscPullGesture")!;var g=Activator.CreateInstance(type)!;
            bool Begin(double a,double b)=>(bool)type.GetMethod("Begin")!.Invoke(g,[7,new Point(100,100),a,9,new Point(200,100),b])!;
            bool Move(Point a,Point b)=>(bool)type.GetMethod("Move")!.Invoke(g,[7,a,9,b])!;
            if(!Begin(0,1)||Move(new Point(102,100),new Point(210,100))||!Move(new Point(102,100),new Point(232,100))||Move(new Point(102,100),new Point(250,100)))throw new Exception("Grab jitter/one-shot");
            if(!Begin(1,0)||!Move(new Point(132,100),new Point(200,100)))throw new Exception("Reverse order");
            Begin(0,1);if(Move(new Point(125,100),new Point(240,100))||Move(new Point(100,100),new Point(240,100)))throw new Exception("Hub drift cancellation");
            if(Begin(.5,1)||Begin(double.NaN,1))throw new Exception("Ordinary pinch captured");
            Begin(0,1);type.GetMethod("Cancel")!.Invoke(g,null);if(Move(new Point(100,100),new Point(240,100)))throw new Exception("Cancel");
            Console.WriteLine("PASS disc pull: hub/edge, either order, jitter, one-shot, cancellation, pinch exclusion");return;
        }
        if(args.Length==2&&args[0]=="--gapless-flac"){
            var album=ZipAlbumReader.OpenFolder(args[1]);var assembly=typeof(MainWindow).Assembly;var entryType=assembly.GetType("ZipMp3Player.PlaybackQueueEntry")!;var gaplessType=assembly.GetType("ZipMp3Player.GaplessPlaybackStream")!;
            foreach(bool faithful in new[]{false,true}){
                var entries=Array.CreateInstance(entryType,album.Tracks.Count);for(int i=0;i<album.Tracks.Count;i++)entries.SetValue(Activator.CreateInstance(entryType,album,i,-1),i);
                using var stream=(NAudio.Wave.WaveStream)Activator.CreateInstance(gaplessType,entries,0,faithful,false,0)!;
                Task.Run(()=>{var data=new byte[32768];long total=0;for(int i=0;i<12000;i++){int n=stream.Read(data,0,data.Length);if(n==0)break;total+=n;}Console.WriteLine($"PASS FLAC cross-thread read faithful={faithful}, bytes={total}");}).GetAwaiter().GetResult();
                stream.CurrentTime=TimeSpan.FromSeconds(15);Task.Run(()=>{var data=new byte[32768];if(stream.Read(data,0,data.Length)==0)throw new Exception("Cross-thread seek failed");}).GetAwaiter().GetResult();
            }return;
        }
        if(args.Length==2&&args[0]=="--cue-flac"){
            var album=ZipAlbumReader.OpenFolder(args[1]);if(album.Tracks.Count!=11||!album.Tracks.Any(t=>t.Title=="To Infinity")||album.Tracks[0].Artist!="Frozen Crown")throw new Exception("CUE metadata: "+string.Join(" | ",album.Tracks.Select(t=>$"{t.TrackNumber}: {t.Title} / {t.Artist}")));
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
            using var baseline=new NAudio.Wave.MediaFoundationReader(album.Tracks[0].SourcePath);
            var scratch=new byte[262144];
            foreach(var track in album.Tracks){var stopwatch=System.Diagnostics.Stopwatch.StartNew();using var owner=(IDisposable)type.GetMethod("Open",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[track])!;Console.WriteLine($"Track {track.TrackNumber} open/seek: {stopwatch.ElapsedMilliseconds} ms");
                var reader=(NAudio.Wave.WaveStream)type.GetProperty("Reader")!.GetValue(owner)!;
                if(Math.Abs(reader.TotalTime.TotalSeconds-track.Duration.TotalSeconds)>0.05)throw new Exception("Clip duration");
                long target=track.CueStartFrame*baseline.WaveFormat.SampleRate/75*baseline.WaveFormat.BlockAlign;
                while(baseline.Position<target){int got=baseline.Read(scratch,0,(int)Math.Min(scratch.Length,target-baseline.Position));if(got==0)throw new Exception("Baseline EOF");}
                byte[] expected=new byte[4096];baseline.ReadExactly(expected);
                byte[] data=new byte[4096];reader.ReadExactly(data);if(!data.SequenceEqual(expected))throw new Exception($"Track {track.TrackNumber} PCM start mismatch at frame {track.CueStartFrame}");
                reader.CurrentTime=TimeSpan.FromSeconds(Math.Max(0,reader.TotalTime.TotalSeconds-1));if(reader.Read(data,0,data.Length)==0)throw new Exception("Seek end");reader.Position=reader.Length;if(reader.Read(data,0,data.Length)!=0)throw new Exception("Clip end");
                reader.Position=0;reader.ReadExactly(data);if(!data.SequenceEqual(expected))throw new Exception("Backward seek PCM mismatch");
                var assembly=typeof(MainWindow).Assembly;var entryType=assembly.GetType("ZipMp3Player.PlaybackQueueEntry")!;var gaplessType=assembly.GetType("ZipMp3Player.GaplessPlaybackStream")!;
                var entries=Array.CreateInstance(entryType,1);entries.SetValue(Activator.CreateInstance(entryType,album,Array.IndexOf(album.Tracks.ToArray(),track),-1),0);
                using var gapless=(NAudio.Wave.WaveStream)Activator.CreateInstance(gaplessType,entries,0,true,false,0)!;
                Task.Run(()=>gapless.ReadExactly(data)).GetAwaiter().GetResult();if(!data.SequenceEqual(expected))throw new Exception("Gapless selected track PCM mismatch");
            }Console.WriteLine("PASS Frozen Crown: 11 CUE tracks, metadata, FLAC decode, seek and bounded EOF for every track");return;
        }
        if(args.Length==2&&args[0]=="--qr-test"){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);File.WriteAllBytes(args[1],MobileSyncQrWindow.Encode("http://192.168.11.63:50703/0123456789abcdef0123456789abcdef0123456789abcdef/"));Console.WriteLine("PASS Windows QR fixture: "+args[1]);return;}
        if(args.Length==2&&args[0]=="--sync-style"){SyncStyleChecks.Run(args[1]);return;}
        if(args.Length>=3&&args[0]=="--sync-test"){SyncChecks.Run(args[1],args[2],args.Contains("--serve"));return;}
        if(args.Length==3&&args[0]=="--convert-glb"){MobileGlbExporter.ConvertSnapshot(args[1],args[2]);Console.WriteLine("PASS GLB conversion: "+args[2]);return;}
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
        MobileCaseExporter.Export(item, Path.ChangeExtension(output,".glb"));
        if(args.Length==1)MobileCaseExporter.Export(item with {FrontCover=null,InsideFrontCover=null,BackCover=null,SpineCover=null,RightSpineCover=null,InlayCover=null,DiscImage=null,SpineCard=null},Path.ChangeExtension(output,".empty.glb"));
        Console.WriteLine("GLB export: " + Path.ChangeExtension(output,".glb"));
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
