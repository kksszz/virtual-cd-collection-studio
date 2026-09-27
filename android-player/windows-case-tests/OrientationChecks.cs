using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;
internal static class OrientationChecks
{
    internal static void Run()
    {
        var pixels=new byte[]{1,2,3,4,5,6};var image=BitmapSource.Create(2,3,96,96,PixelFormats.Gray8,null,pixels,2);
        byte[][] expected=[[1,2,3,4,5,6],[2,1,4,3,6,5],[6,5,4,3,2,1],[5,6,3,4,1,2],[1,3,5,2,4,6],[5,3,1,6,4,2],[6,4,2,5,3,1],[2,4,6,1,3,5]];
        for(int n=1;n<=8;n++){var result=ArtworkOrientation.Apply(image,n);var bytes=new byte[6];result.CopyPixels(bytes,result.PixelWidth,0);if(!bytes.SequenceEqual(expected[n-1]))throw new Exception("Orientation "+n);}
        var root=Path.Combine(Path.GetTempPath(),"vccs-orientation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"front.jpg");var metadata=new BitmapMetadata("jpg");metadata.SetQuery("/app1/ifd/{ushort=274}",(ushort)8);
        var encoder=new JpegBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image,null,metadata,null));using(var output=File.Create(path))encoder.Save(output);
        var method=typeof(MainWindow).GetMethod("LoadBitmap",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static,null,[typeof(string),typeof(int)],null)!;
        var shown=(BitmapSource)method.Invoke(null,[path,0])!;
        if(shown.PixelWidth!=3||shown.PixelHeight!=2)throw new Exception("Player ignores EXIF");
        ArtworkRotationWriter.Rotate(path,null,90,null);
        using var input=File.OpenRead(path);var saved=BitmapDecoder.Create(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
        if(saved.PixelWidth!=2||saved.PixelHeight!=3||ArtworkOrientation.Read(saved)!=1)throw new Exception("Writer did not normalize orientation");
        var reloaded=(BitmapSource)method.Invoke(null,[path,0])!;
        if(reloaded.PixelWidth!=saved.PixelWidth||reloaded.PixelHeight!=saved.PixelHeight)throw new Exception("Double rotation");
        Console.WriteLine("PASS all 8 EXIF orientations; player display; rotate-save-reload without double rotation");
    }
}
