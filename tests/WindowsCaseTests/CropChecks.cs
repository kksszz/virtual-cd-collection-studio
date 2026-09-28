using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class CropChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "vcd-crop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pixels = Enumerable.Range(0,16).SelectMany(i => new byte[]{(byte)i,0,255,255}).ToArray();
        var bitmap = BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,pixels,16);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var originalStream = new MemoryStream(); encoder.Save(originalStream);
        var original = originalStream.ToArray();
        var type = typeof(MainWindow).Assembly.GetType("ZipMp3Player.ArtworkRotationWriter")!;
        string Crop(string path, string? entry, int rotation, Int32Rect rect) =>
            (string)type.GetMethod("Crop")!.Invoke(null,[path,entry,rotation,rect,null,null])!;
        void Check(Stream input, byte expected)
        {
            using var memory = new MemoryStream(); input.CopyTo(memory); memory.Position=0;
            var image = BitmapDecoder.Create(memory,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            if(image.PixelWidth!=2 || image.PixelHeight!=2)throw new Exception("Wrong crop dimensions");
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            var bytes=new byte[16]; converted.CopyPixels(bytes,8,0);
            if(bytes[0]!=expected)throw new Exception("Wrong crop origin");
        }
        var path=Path.Combine(root,"scan.png"); File.WriteAllBytes(path,original);
        var backup=Crop(path,null,0,new Int32Rect(1,1,2,2));
        if(!File.ReadAllBytes(backup).SequenceEqual(original))throw new Exception("Original backup changed");
        using(var input=File.OpenRead(path))Check(input,5);
        var before=File.ReadAllBytes(path);
        try { Crop(path,null,0,new Int32Rect(1,1,9,9)); throw new Exception("Invalid crop accepted"); }
        catch(TargetInvocationException ex) when(ex.InnerException is ArgumentOutOfRangeException){}
        if(!File.ReadAllBytes(path).SequenceEqual(before))throw new Exception("Invalid crop modified source");
        var zipPath=Path.Combine(root,"album.zip.mp3");
        using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create)){
            using(var stream=zip.CreateEntry("画像/scan.png").Open())stream.Write(original);
            using(var stream=zip.CreateEntry("song.mp3").Open())stream.Write(new byte[]{8,9,10});
        }
        Crop(zipPath,"画像/scan.png",90,new Int32Rect(0,0,2,2));
        using(var zip=ZipFile.OpenRead(zipPath)){
            using(var stream=zip.GetEntry("画像/scan.png")!.Open())Check(stream,12);
            if(zip.Entries.Any(e=>e.Length!=e.CompressedLength))throw new Exception("ZIP not stored");
            using var audio=zip.GetEntry("song.mp3")!.Open(); using var memory=new MemoryStream();audio.CopyTo(memory);
            if(!memory.ToArray().SequenceEqual(new byte[]{8,9,10}))throw new Exception("Audio changed");
        }
        var app=new Application();
        var deskew=typeof(MainWindow).Assembly.GetType("ZipMp3Player.ArtworkDeskew")!;
        var renderAngle=deskew.GetMethod("Render",BindingFlags.Static|BindingFlags.NonPublic)!;
        bitmap.Freeze();
        foreach(double angle in new[]{-15d,-.1,.1,15}){
            var expected=(BitmapSource)renderAngle.Invoke(null,[bitmap,angle,0])!;
            var finePath=Path.Combine(root,"fine-"+angle.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png");File.WriteAllBytes(finePath,original);
            var fineCrop=new Int32Rect(0,0,expected.PixelWidth,expected.PixelHeight);
            // Saving uses Task.Run in the real window: validate that rendering works on that worker.
            var fineBackup=Task.Run(()=>(string)type.GetMethod("CropWithAngle")!.Invoke(null,[finePath,null,0,fineCrop,angle,null,null])!).GetAwaiter().GetResult();
            using var input=File.OpenRead(finePath);var actual=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            if(actual.PixelWidth!=expected.PixelWidth||actual.PixelHeight!=expected.PixelHeight)throw new Exception("Fine-angle bounds mismatch");
            byte[] Pixels(BitmapSource source){var converted=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);return pixels;}
            if(!Pixels(actual).SequenceEqual(Pixels(expected)))throw new Exception("Preview/save pixels differ");
            if(!File.ReadAllBytes(fineBackup).SequenceEqual(original))throw new Exception("Fine-angle backup changed");
        }
        var fineZip=Path.Combine(root,"fine.zip.mp3");
        using(var archive=ZipFile.Open(fineZip,ZipArchiveMode.Create)){
            using(var stream=archive.CreateEntry("scan.png").Open())stream.Write(original);
            using(var stream=archive.CreateEntry("song.mp3").Open())stream.Write(new byte[]{8,9,10});
        }
        Task.Run(()=>type.GetMethod("CropWithAngle")!.Invoke(null,[fineZip,"scan.png",90,new Int32Rect(1,1,2,2),1.2d,null,null])).GetAwaiter().GetResult();
        using(var archive=ZipFile.OpenRead(fineZip)){
            if(archive.Entries.Any(e=>e.Length!=e.CompressedLength))throw new Exception("Fine-angle ZIP not stored");
            using var stream=archive.GetEntry("song.mp3")!.Open();using var data=new MemoryStream();stream.CopyTo(data);
            if(!data.ToArray().SequenceEqual(new byte[]{8,9,10}))throw new Exception("Fine-angle audio changed");
        }
        var highDpi=BitmapSource.Create(4,4,300,300,PixelFormats.Bgra32,null,pixels,16);
        var highDpiResult=(BitmapSource)renderAngle.Invoke(null,[highDpi,1d,0])!;
        if(highDpiResult.PixelWidth!=5||highDpiResult.PixelHeight!=5)throw new Exception("DPI changed pixel coordinates");
        var editorType=typeof(MainWindow).Assembly.GetType("ZipMp3Player.ArtworkCropWindow")!;
        var selection=editorType.GetMethod("SelectionRect",BindingFlags.NonPublic|BindingFlags.Static)!;
        var rect=(Int32Rect)selection.Invoke(null,[new Point(8,9),new Point(-1,-2),4,4])!;
        if(rect!=new Int32Rect(0,0,4,4))throw new Exception("Selection clamping failed");
        var editor=(Window)Activator.CreateInstance(editorType,["切り抜きテスト",bitmap])!;
        var select=editorType.GetMethod("SetSelection",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var save=(System.Windows.Controls.Button)editorType.GetField("_save",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;
        if(save.IsEnabled)throw new Exception("Full image should not save");
        var slider=(System.Windows.Controls.Slider)editorType.GetField("_angle",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(editor)!;
        slider.Value=1.2;
        if(!save.IsEnabled)throw new Exception("Angle-only save disabled");
        slider.Value=0;
        if(save.IsEnabled)throw new Exception("Angle reset did not restore unchanged selection");
        select.Invoke(editor,[new Point(3,3),new Point(1,1),true]);
        if(!save.IsEnabled)throw new Exception("Valid crop save disabled");
        var content=(FrameworkElement)editor.Content;
        content.Measure(new Size(1060,680));content.Arrange(new Rect(0,0,1060,680));content.UpdateLayout();
        var render=new RenderTargetBitmap(1060,680,96,96,PixelFormats.Pbgra32);render.Render(content);
        var screenshot=new PngBitmapEncoder();screenshot.Frames.Add(BitmapFrame.Create(render));
        var screenshotPath=Path.Combine(root,"crop-editor.png");
        using(var stream=File.Create(screenshotPath))screenshot.Save(stream);
        editor.Close();
        if(!File.ReadAllBytes(path).SequenceEqual(before))throw new Exception("Editor cancel modified image");
        Console.WriteLine("PASS crop: pixels, quarter/fine rotation, preview/save match, worker thread, DPI, backup, invalid crop, stored ZIP, audio, selection, reset, cancel");
        Console.WriteLine(screenshotPath);
    }
}
