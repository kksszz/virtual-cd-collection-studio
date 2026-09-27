using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ZipMp3Player;

internal static class ArtworkPerspective
{
    internal static bool Valid(Point[] q,int width,int height)
    {
        if(q.Length!=4||q.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X<0||p.Y<0||p.X>width||p.Y>height))return false;
        for(int i=0;i<4;i++){var a=q[(i+1)%4]-q[i];var b=q[(i+2)%4]-q[(i+1)%4];if(Vector.CrossProduct(a,b)<=1||(q[(i+1)%4]-q[i]).Length<2)return false;}
        return true;
    }
    internal static Size OutputSize(Point[] q)=>new(Math.Max(2,Math.Round(((q[1]-q[0]).Length+(q[2]-q[3]).Length)/2)),Math.Max(2,Math.Round(((q[3]-q[0]).Length+(q[2]-q[1]).Length)/2)));
    internal static BitmapSource Render(BitmapSource source,Point[] q,int maxSize=0)
    {
        if(!Valid(q,source.PixelWidth,source.PixelHeight))throw new ArgumentException("四隅が交差しています。左上→右上→右下→左下の順に調整してください。");
        var size=OutputSize(q);double scale=maxSize>0?Math.Min(1,maxSize/Math.Max(size.Width,size.Height)):1;
        int w=Math.Max(2,(int)(size.Width*scale)),h=Math.Max(2,(int)(size.Height*scale));
        if((long)w*h>100_000_000)throw new ArgumentException("補正後の画像が大きすぎます。");
        var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int sw=source.PixelWidth,sh=source.PixelHeight;
        var input=new byte[checked(sw*sh*4)];converted.CopyPixels(input,sw*4,0);var output=new byte[checked(w*h*4)];
        double dx1=q[1].X-q[2].X,dx2=q[3].X-q[2].X,dx3=q[0].X-q[1].X+q[2].X-q[3].X;
        double dy1=q[1].Y-q[2].Y,dy2=q[3].Y-q[2].Y,dy3=q[0].Y-q[1].Y+q[2].Y-q[3].Y;
        double det=dx1*dy2-dx2*dy1;if(Math.Abs(det)<1e-8)throw new ArgumentException("補正範囲が狭すぎます。");
        double g=(dx3*dy2-dx2*dy3)/det,j=(dx1*dy3-dx3*dy1)/det;
        double a=q[1].X-q[0].X+g*q[1].X,b=q[3].X-q[0].X+j*q[3].X,c=q[0].X;
        double d=q[1].Y-q[0].Y+g*q[1].Y,e=q[3].Y-q[0].Y+j*q[3].Y,f=q[0].Y;
        Parallel.For(0,h,y=>{double v=(y+.5)/h;for(int x=0;x<w;x++){
            double u=(x+.5)/w,den=g*u+j*v+1;
            double px=Math.Clamp((a*u+b*v+c)/den-.5,0,sw-1),py=Math.Clamp((d*u+e*v+f)/den-.5,0,sh-1);
            int ix=(int)px,iy=(int)py,nx=Math.Min(ix+1,sw-1),ny=Math.Min(iy+1,sh-1);double fx=px-ix,fy=py-iy;
            for(int channel=0;channel<4;channel++)output[(y*w+x)*4+channel]=(byte)Math.Clamp(Math.Round(
                input[(iy*sw+ix)*4+channel]*(1-fx)*(1-fy)+input[(iy*sw+nx)*4+channel]*fx*(1-fy)+input[(ny*sw+ix)*4+channel]*(1-fx)*fy+input[(ny*sw+nx)*4+channel]*fx*fy),0,255);
        }});
        var result=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,output,w*4);result.Freeze();return result;
    }
    // Detect the dominant non-background component. A proposal, never an automatic save.
    internal static Point[]? Detect(BitmapSource source)
    {
        var small=ArtworkDeskew.Render(source,0,700);var image=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);
        int w=image.PixelWidth,h=image.PixelHeight;var data=new byte[w*h*4];image.CopyPixels(data,w*4,0);
        var background=new double[3];for(int c=0;c<3;c++)background[c]=new[]{data[c],data[(w-1)*4+c],data[((h-1)*w)*4+c],data[(w*h-1)*4+c]}.OrderBy(v=>v).Skip(1).Take(2).Average(v=>(double)v);
        // Average local ink density instead of requiring individual halftone dots to touch.
        var integral=new double[(w+1)*(h+1)];
        for(int y=0;y<h;y++){double row=0;for(int x=0;x<w;x++){int p=(y*w+x)*4;row+=Math.Max(Math.Abs(data[p]-background[0]),Math.Max(Math.Abs(data[p+1]-background[1]),Math.Abs(data[p+2]-background[2])));integral[(y+1)*(w+1)+x+1]=integral[y*(w+1)+x+1]+row;}}
        var mask=new bool[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            int l=Math.Max(0,x-1),r=Math.Min(w,x+2),t=Math.Max(0,y-1),b=Math.Min(h,y+2);
            double density=(integral[b*(w+1)+r]-integral[t*(w+1)+r]-integral[b*(w+1)+l]+integral[t*(w+1)+l])/((r-l)*(b-t));mask[y*w+x]=density>18;
        }
        // Close small gaps in the dot screen and the central fold without joining distant dust.
        var expanded=new bool[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            bool hit=false;for(int dy=-3;dy<=3&&!hit;dy++)for(int dx=-3;dx<=3;dx++){int xx=x+dx,yy=y+dy;if(xx>=0&&xx<w&&yy>=0&&yy<h&&mask[yy*w+xx]){hit=true;break;}}expanded[y*w+x]=hit;
        }
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            bool full=true;for(int dy=-3;dy<=3&&full;dy++)for(int dx=-3;dx<=3;dx++){int xx=x+dx,yy=y+dy;if(xx>=0&&xx<w&&yy>=0&&yy<h&&!expanded[yy*w+xx]){full=false;break;}}mask[y*w+x]=full;
        }
        var visited=new bool[mask.Length];var queue=new Queue<int>();List<int> largest=[];
        for(int i=0;i<mask.Length;i++)if(mask[i]&&!visited[i]){
            List<int> component=[];queue.Enqueue(i);visited[i]=true;
            while(queue.Count>0){int p=queue.Dequeue();component.Add(p);int x=p%w,y=p/w;
                foreach(var n in new[]{x>0?p-1:-1,x+1<w?p+1:-1,y>0?p-w:-1,y+1<h?p+w:-1})if(n>=0&&mask[n]&&!visited[n]){visited[n]=true;queue.Enqueue(n);}}
            if(component.Count>largest.Count)largest=component;
        }
        if(largest.Count<w*h*.03)return null;
        var points=largest.Select(p=>new Point(p%w,p/w)).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();
        List<Point> hull=[];
        void Add(Point p){while(hull.Count>=2&&Vector.CrossProduct(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        foreach(var p in points)Add(p);int lower=hull.Count;
        for(int i=points.Length-2;i>=0;i--){var p=points[i];while(hull.Count>lower&&Vector.CrossProduct(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}hull.RemoveAt(hull.Count-1);
        var boundary=points.Where(p=>{int x=(int)p.X,y=(int)p.Y;return x==0||y==0||x==w-1||y==h-1||!mask[y*w+x-1]||!mask[y*w+x+1]||!mask[(y-1)*w+x]||!mask[(y+1)*w+x];}).ToArray();
        while(hull.Count>4){int index=Enumerable.Range(0,hull.Count).MinBy(i=>Math.Abs(Vector.CrossProduct(hull[i]-hull[(i+hull.Count-1)%hull.Count],hull[(i+1)%hull.Count]-hull[i])));hull.RemoveAt(index);}
        if(hull.Count!=4)return null;
        // Fit long straight sides before intersecting: raster corner pixels alone can be several pixels inset.
        var lines=new (double A,double B,double C)[4];
        for(int i=0;i<4;i++){
            var start=hull[i];var v=hull[(i+1)%4]-start;
            var samples=boundary.Where(p=>{double t=Vector.Multiply(p-start,v)/v.LengthSquared;return t>.1&&t<.9&&Math.Abs(Vector.CrossProduct(p-start,v))/v.Length<5;}).ToArray();
            if(samples.Length<2)samples=[start,hull[(i+1)%4]];
            double mx=samples.Average(p=>p.X),my=samples.Average(p=>p.Y),xx=samples.Sum(p=>(p.X-mx)*(p.X-mx)),yy=samples.Sum(p=>(p.Y-my)*(p.Y-my)),xy=samples.Sum(p=>(p.X-mx)*(p.Y-my));
            double angle=.5*Math.Atan2(2*xy,xx-yy),a=-Math.Sin(angle),b=Math.Cos(angle);lines[i]=(a,b,a*mx+b*my);
        }
        var fitted=new Point[4];for(int i=0;i<4;i++){var a=lines[(i+3)%4];var b=lines[i];double det=a.A*b.B-b.A*a.B;if(Math.Abs(det)<.01)return null;fitted[i]=new Point(Math.Clamp((a.C*b.B-b.C*a.B)/det,0,w),Math.Clamp((a.A*b.C-b.A*a.C)/det,0,h));}
        hull=fitted.ToList();
        int first=Enumerable.Range(0,4).MinBy(i=>hull[i].X+hull[i].Y);
        var q=Enumerable.Range(0,4).Select(i=>new Point(hull[(first+i)%4].X*source.PixelWidth/w,hull[(first+i)%4].Y*source.PixelHeight/h)).ToArray();
        return Valid(q,source.PixelWidth,source.PixelHeight)?q:null;
    }
}
