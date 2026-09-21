using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal static class ArtworkDeskew
{
    internal static Size Bounds(int width, int height, double degrees)
    {
        if(!double.IsFinite(degrees)||Math.Abs(degrees)>15)throw new ArgumentOutOfRangeException(nameof(degrees));
        double angle=degrees*Math.PI/180,cos=Math.Abs(Math.Cos(angle)),sin=Math.Abs(Math.Sin(angle));
        return new Size(Math.Ceiling(width*cos+height*sin),Math.Ceiling(height*cos+width*sin));
    }

    // Always render from the untouched source. Repeated slider changes never resample a previous result.
    internal static BitmapSource Render(BitmapSource source,double degrees,int maxPixels=0)
    {
        if(degrees==0&&maxPixels==0)return source;
        var size=Bounds(source.PixelWidth,source.PixelHeight,degrees);
        double scale=maxPixels>0?Math.Min(1,maxPixels/Math.Max(size.Width,size.Height)):1;
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var drawing=visual.RenderOpen()){
            drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,Math.Ceiling(size.Width*scale),Math.Ceiling(size.Height*scale)));
            drawing.PushTransform(new ScaleTransform(scale,scale));
            drawing.PushTransform(new TranslateTransform(size.Width/2,size.Height/2));
            drawing.PushTransform(new RotateTransform(degrees));
            drawing.DrawImage(source,new Rect(-source.PixelWidth/2d,-source.PixelHeight/2d,source.PixelWidth,source.PixelHeight));
            drawing.Pop();drawing.Pop();drawing.Pop();
        }
        var result=new RenderTargetBitmap((int)Math.Ceiling(size.Width*scale),(int)Math.Ceiling(size.Height*scale),96,96,PixelFormats.Pbgra32);
        result.Render(visual);result.Freeze();return result;
    }
}
