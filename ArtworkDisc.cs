using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ZipMp3Player;

internal sealed record DiscCrop(Rect Bounds, double Angle = 0)
{
    internal bool Valid(BitmapSource source) => Bounds.Width >= 8 && Bounds.Height >= 8 &&
        Bounds.Left >= 0 && Bounds.Top >= 0 && Bounds.Right <= source.PixelWidth && Bounds.Bottom <= source.PixelHeight && double.IsFinite(Angle);
}

internal static class ArtworkDisc
{
    internal static DiscCrop? Detect(BitmapSource source)
    {
        var small=ArtworkDeskew.Render(source,0,700);
        var bitmap=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);
        int w=bitmap.PixelWidth,h=bitmap.PixelHeight;var pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);
        var bg=Enumerable.Range(0,3).Select(c=>new[]{pixels[c],pixels[(w-1)*4+c],pixels[(h-1)*w*4+c],pixels[(w*h-1)*4+c]}.Order().Skip(1).Take(2).Average(x=>(double)x)).ToArray();
        var ink=new bool[w*h];
        for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
            double sum=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                int p=((y+dy)*w+x+dx)*4;sum+=Enumerable.Range(0,3).Max(c=>Math.Abs(pixels[p+c]-bg[c]));
            }ink[y*w+x]=sum/9>24;
        }
        var seen=new bool[w*h];var queue=new Queue<int>();List<int> best=[];
        for(int i=0;i<ink.Length;i++)if(ink[i]&&!seen[i]){
            List<int> points=[];queue.Enqueue(i);seen[i]=true;
            while(queue.TryDequeue(out int p)){points.Add(p);int x=p%w,y=p/w;
                foreach(int n in new[]{x>0?p-1:-1,x<w-1?p+1:-1,y>0?p-w:-1,y<h-1?p+w:-1})if(n>=0&&ink[n]&&!seen[n]){seen[n]=true;queue.Enqueue(n);}
            }if(points.Count>best.Count)best=points;
        }
        if(best.Count<w*h*.02)return null;
        int left=best.Min(p=>p%w),right=best.Max(p=>p%w)+1,top=best.Min(p=>p/w),bottom=best.Max(p=>p/w)+1;
        double width=right-left,height=bottom-top,ratio=width/height,fill=best.Count/(width*height);
        if(ratio<.65||ratio>1.55||fill<.40||fill>.88)return null;
        // Reject rectangular artwork and isolated text rather than proposing a destructive circular crop.
        double cx=(left+right)/2d,cy=(top+bottom)/2d;
        if(best.Count(p=>Math.Pow((p%w+.5-cx)/(width/2),2)+Math.Pow((p/w+.5-cy)/(height/2),2)>1.10)>best.Count*.02)return null;
        return new DiscCrop(new Rect(left*(double)source.PixelWidth/w,top*(double)source.PixelHeight/h,width*source.PixelWidth/w,height*source.PixelHeight/h));
    }

    internal static BitmapSource Render(BitmapSource source,DiscCrop crop,int maxSize=0)
    {
        if(!crop.Valid(source))throw new ArgumentException("DISCの外周を画像内に合わせてください。");
        int size=(int)Math.Round(Math.Max(crop.Bounds.Width,crop.Bounds.Height));if(maxSize>0)size=Math.Min(size,maxSize);
        if((long)size*size>100_000_000)throw new ArgumentException("補正後の画像が大きすぎます。");
        var visual=new DrawingVisual();using(var draw=visual.RenderOpen()){
            draw.DrawRectangle(Brushes.White,null,new Rect(0,0,size,size));
            draw.PushClip(new EllipseGeometry(new Point(size/2d,size/2d),size/2d,size/2d));
            draw.PushTransform(new RotateTransform(crop.Angle,size/2d,size/2d));
            double sx=size/crop.Bounds.Width,sy=size/crop.Bounds.Height;
            draw.DrawImage(source,new Rect(-crop.Bounds.X*sx,-crop.Bounds.Y*sy,source.PixelWidth*sx,source.PixelHeight*sy));
            draw.Pop();draw.Pop();
        }
        var output=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);output.Render(visual);output.Freeze();return output;
    }
}
