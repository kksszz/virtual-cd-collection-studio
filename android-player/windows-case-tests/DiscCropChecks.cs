using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;
internal static class DiscCropChecks
{
    internal static void Run(string? example)
    {
        var visual=new DrawingVisual();using(var d=visual.RenderOpen()){
            d.DrawRectangle(Brushes.White,null,new Rect(0,0,600,500));d.DrawEllipse(Brushes.Teal,null,new Point(350,240),160,140);d.DrawEllipse(Brushes.White,null,new Point(350,240),20,18);
            d.DrawRectangle(Brushes.Red,null,new Rect(340,130,20,20));
        }
        var source=new RenderTargetBitmap(600,500,96,96,PixelFormats.Pbgra32);source.Render(visual);source.Freeze();
        var crop=ArtworkDisc.Detect(source)??throw new Exception("Disc not detected");
        if(Math.Abs(crop.Bounds.X-190)>3||Math.Abs(crop.Bounds.Y-100)>3||Math.Abs(crop.Bounds.Width-320)>5||Math.Abs(crop.Bounds.Height-280)>5)throw new Exception("Disc bounds "+crop);
        var output=ArtworkDisc.Render(source,crop with{Angle=90});if(output.PixelWidth!=output.PixelHeight)throw new Exception("Not square");
        var pixels=new byte[output.PixelWidth*output.PixelHeight*4];output.CopyPixels(pixels,output.PixelWidth*4,0);
        if(pixels[0]!=255||pixels[(output.PixelHeight/2*output.PixelWidth+output.PixelWidth/2)*4]!=255)throw new Exception("White exterior/hole lost");
        var blank=BitmapSource.Create(600,500,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)255,300000).ToArray(),600);
        if(ArtworkDisc.Detect(blank)!=null)throw new Exception("Blank detected");
        var rect=ArtworkDiscWindow.Adjust(new Rect(10,20,100,80),4,new Vector(-100,-100),600,500);if(rect.X!=0||rect.Y!=0||rect.Width!=100)throw new Exception("Move clamp");
        if(ArtworkDiscWindow.Adjust(rect,1,new Vector(20,0),600,500).Width!=120)throw new Exception("Handle resize");
        var root=Path.Combine(Path.GetTempPath(),"vccs-disc-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        static void Save(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);encoder.Save(stream);}
        var file=Path.Combine(root,"disc.png");Save(source,file);var original=File.ReadAllBytes(file);
        var backup=Task.Run(()=>ArtworkRotationWriter.Disc(file,null,0,crop,null)).GetAwaiter().GetResult();
        if(!File.ReadAllBytes(backup).SequenceEqual(original))throw new Exception("Backup mismatch");
        ArtworkRotationWriter.Restore(file,null,backup,null);if(!File.ReadAllBytes(file).SequenceEqual(original))throw new Exception("Restore mismatch");
        var zip=Path.Combine(root,"disc.zip");using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)){using(var s=a.CreateEntry("disc.png").Open())s.Write(original);using(var s=a.CreateEntry("track.mp3").Open())s.Write([1,2,3]);}
        ArtworkRotationWriter.Disc(zip,"disc.png",0,crop,null);if(!ArtworkRotationWriter.ReadImage(zip,"track.mp3").SequenceEqual(new byte[]{1,2,3}))throw new Exception("ZIP music changed");
        if(example is not null){var load=typeof(MainWindow).GetMethod("LoadBitmap",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic,null,[typeof(string),typeof(int)],null)!;source=null!;var actual=(BitmapSource)load.Invoke(null,[example,1400])!;crop=ArtworkDisc.Detect(actual)??throw new Exception("Actual disc detection failed");Save(ArtworkDisc.Render(actual,crop,1000),Path.Combine(root,"actual.png"));Console.WriteLine("Actual bounds: "+crop);}
        _=new Application();var window=new ArtworkDiscWindow("disc.png",BitmapDecoder.Create(new Uri(file),BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0]);
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;typeof(ArtworkDiscWindow).GetProperty("Crop",flags)!.SetValue(window,ArtworkDisc.Detect(BitmapDecoder.Create(new Uri(file),BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0]));typeof(ArtworkDiscWindow).GetMethod("Refresh",flags)!.Invoke(window,null);
        var content=(FrameworkElement)window.Content;content.Measure(new Size(1100,740));content.Arrange(new Rect(0,0,1100,740));content.UpdateLayout();var screen=new RenderTargetBitmap(1100,740,96,96,PixelFormats.Pbgra32);screen.Render(content);Save(screen,Path.Combine(root,"editor.png"));
        Console.WriteLine("PASS disc detection, square render, white exterior, hole, handles, background save, backup, restore and ZIP preservation. Output: "+root);
    }
}
