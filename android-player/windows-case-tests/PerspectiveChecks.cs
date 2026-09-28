using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;
internal static class PerspectiveChecks
{
    internal static void Run(string? example)
    {
        var rect=new Int32Rect(20,30,100,80);
        if(ArtworkCropWindow.DragSelection(rect,2,new Point(120,70),new Point(160,70),300,200)!=new Int32Rect(20,30,140,80))throw new Exception("Right edge resize");
        if(ArtworkCropWindow.DragSelection(rect,4,new Point(70,30),new Point(70,10),300,200)!=new Int32Rect(20,10,100,100))throw new Exception("Top edge resize");
        if(ArtworkCropWindow.DragSelection(rect,16,new Point(50,60),new Point(-100,-100),300,200)!=new Int32Rect(0,0,100,80))throw new Exception("Move bounds");
        Point[] corners=[new(70,65),new(510,85),new(490,310),new(60,290)];
        var halftone=Enumerable.Repeat((byte)255,600*400).ToArray();
        for(int y=66;y<310;y+=2)for(int x=70;x<510;x+=2)if(x<287||x>291)halftone[y*600+x]=150;
        var pale=BitmapSource.Create(600,400,96,96,PixelFormats.Gray8,null,halftone,600);pale.Freeze();
        var paleCorners=ArtworkPerspective.Detect(pale)??throw new Exception("Halftone/fold detection failed");
        if((paleCorners[0]-new Point(70,66)).Length>6||(paleCorners[2]-new Point(509,309)).Length>6)throw new Exception("Halftone outer boundary mismatch");
        var blank=BitmapSource.Create(600,400,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)255,600*400).ToArray(),600);
        if(ArtworkPerspective.Detect(blank)!=null)throw new Exception("Blank page false positive");
        Console.WriteLine("PASS pale halftone, central gap, blank rejection");
        var straightPixels=Enumerable.Repeat((byte)255,1000*700).ToArray();
        for(int y=120;y<580;y++)for(int x=140;x<860;x++)straightPixels[y*1000+x]=110;
        var straight=BitmapSource.Create(1000,700,96,96,PixelFormats.Gray8,null,straightPixels,1000);straight.Freeze();
        var straightCorners=ArtworkPerspective.Detect(straight)??throw new Exception("Straight boundary not detected");
        Point[] straightExpected=[new(140,120),new(860,120),new(860,580),new(140,580)];
        for(int i=0;i<4;i++)if((straightCorners[i]-straightExpected[i]).Length>1.6)throw new Exception("Straight crop leaves fringe: "+straightCorners[i]);
        Console.WriteLine("PASS straight crop boundary within 1.6 pixels");
        _=new Application();var dragWindow=new ArtworkPerspectiveWindow("drag test",straight);
        var dragContent=(FrameworkElement)dragWindow.Content;dragContent.Measure(new Size(1100,720));dragContent.Arrange(new Rect(0,0,1100,720));dragContent.UpdateLayout();
        var dragFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        typeof(ArtworkPerspectiveWindow).GetMethod("Draw",dragFlags)!.Invoke(dragWindow,null);
        var targets=(List<System.Windows.Shapes.Ellipse>)typeof(ArtworkPerspectiveWindow).GetField("handleTargets",dragFlags)!.GetValue(dragWindow)!;
        var scale=(double)typeof(ArtworkPerspectiveWindow).GetMethod("DisplayScale",dragFlags)!.Invoke(dragWindow,null)!;
        if(targets.Any(target=>Math.Abs(target.Width*scale-44)>.01||!target.IsHitTestVisible))throw new Exception("Corner drag target too small");
        Console.WriteLine("PASS corner drag target 44 display units, independent of image scale");
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen()){
            draw.DrawRectangle(Brushes.White,null,new Rect(0,0,600,400));
            var geometry=new StreamGeometry();using(var g=geometry.Open()){g.BeginFigure(corners[0],true,true);g.PolyLineTo(corners.Skip(1).ToArray(),true,false);}draw.DrawGeometry(Brushes.DarkBlue,null,geometry);
        }
        var source=new RenderTargetBitmap(600,400,96,96,PixelFormats.Pbgra32);source.Render(visual);source.Freeze();
        var detected=ArtworkPerspective.Detect(source)??throw new Exception("No contour");
        Console.WriteLine("Synthetic corners: "+string.Join("; ",detected.Select(p=>p.ToString())));
        for(int i=0;i<4;i++)if((detected[i]-corners[i]).Length>5)throw new Exception("Corner mismatch "+i);
        var result=ArtworkPerspective.Render(source,corners);if(result.PixelWidth<430||result.PixelHeight<220)throw new Exception("Output dimensions");
        if(ArtworkPerspective.Valid([corners[0],corners[2],corners[1],corners[3]],600,400))throw new Exception("Crossed corners accepted");
        var root=Path.Combine(Path.GetTempPath(),"vccs-perspective-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        static void Save(BitmapSource bitmap,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);}
        var path=Path.Combine(root,"scan.png");Save(source,path);var original=File.ReadAllBytes(path);
        var backup=ArtworkRotationWriter.Perspective(path,null,0,corners,null);if(!File.ReadAllBytes(backup).SequenceEqual(original))throw new Exception("Backup changed");
        if(ArtworkRotationWriter.FindUndoBackup(path,null,null)!=backup)throw new Exception("Undo backup not found");
        ArtworkRotationWriter.Restore(path,null,backup,null);if(!File.ReadAllBytes(path).SequenceEqual(original))throw new Exception("Undo image mismatch");
        var zipPath=Path.Combine(root,"test.zip");using(var archive=ZipFile.Open(zipPath,ZipArchiveMode.Create)){
            using(var image=archive.CreateEntry("scan.png").Open())image.Write(original);
            using(var music=archive.CreateEntry("music.mp3").Open())music.Write([1,2,3,4]);
        }
        var zipBackup=ArtworkRotationWriter.Perspective(zipPath,"scan.png",0,corners,null);
        using(var archive=ZipFile.OpenRead(zipPath)){using var music=archive.GetEntry("music.mp3")!.Open();using var bytes=new MemoryStream();music.CopyTo(bytes);if(!bytes.ToArray().SequenceEqual(new byte[]{1,2,3,4}))throw new Exception("Music changed");}
        using(var archive=ZipFile.Open(zipPath,ZipArchiveMode.Update)){archive.GetEntry("music.mp3")!.Delete();using var changed=archive.CreateEntry("music.mp3").Open();changed.Write([9,8,7]);}
        ArtworkRotationWriter.Restore(zipPath,"scan.png",zipBackup,null);
        if(!ArtworkRotationWriter.ReadImage(zipPath,"scan.png").SequenceEqual(original)||!ArtworkRotationWriter.ReadImage(zipPath,"music.mp3").SequenceEqual(new byte[]{9,8,7}))throw new Exception("Undo changed unrelated ZIP entry");
        Console.WriteLine("PASS undo image bytes and preserve later unrelated ZIP changes");
        Console.WriteLine("PASS contour detection, perspective dimensions, invalid quadrilateral, original backup, ZIP preservation");
        if(example is not null){
            var load=typeof(MainWindow).GetMethod("LoadBitmap",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static,null,[typeof(string),typeof(int)],null)!;
            var actual=(BitmapSource)load.Invoke(null,[example,1400])!;
            var quad=ArtworkPerspective.Detect(actual)??throw new Exception("Real scan requires manual corners");
            Save(ArtworkPerspective.Render(actual,quad,1200),Path.Combine(root,"corrected.png"));
            Console.WriteLine("Detected corners: "+string.Join("; ",quad.Select(p=>p.ToString())));Console.WriteLine("Preview: "+Path.Combine(root,"corrected.png"));
            var window=new ArtworkPerspectiveWindow("Booklet1.jpg",actual);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(ArtworkPerspectiveWindow).GetProperty("Corners",flags)!.SetValue(window,quad);
            typeof(ArtworkPerspectiveWindow).GetMethod("Refresh",flags)!.Invoke(window,null);
            var content=(FrameworkElement)window.Content;content.Measure(new Size(1100,720));content.Arrange(new Rect(0,0,1100,720));content.UpdateLayout();
            var screenshot=new RenderTargetBitmap(1100,720,96,96,PixelFormats.Pbgra32);screenshot.Render(content);Save(screenshot,Path.Combine(root,"editor.png"));Console.WriteLine("Editor: "+Path.Combine(root,"editor.png"));
        }
    }
}
