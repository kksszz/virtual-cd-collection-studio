using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ZipMp3Player;
internal static class ArtworkOrientation
{
    internal static int Read(BitmapFrame frame)
    {
        if(frame.Metadata is not BitmapMetadata metadata)return 1;
        foreach(var query in new[]{"/app1/ifd/{ushort=274}","/ifd/{ushort=274}"})
            try{var value=metadata.GetQuery(query);if(value is not null&&Convert.ToInt32(value) is int n&&n is >=1 and <=8)return n;}catch(NotSupportedException){}catch(System.Runtime.InteropServices.COMException){}
        return 1;
    }
    internal static BitmapSource Apply(BitmapSource source,int orientation)
    {
        var matrix=orientation switch{
            2=>new Matrix(-1,0,0,1,0,0),3=>new Matrix(-1,0,0,-1,0,0),4=>new Matrix(1,0,0,-1,0,0),
            5=>new Matrix(0,1,1,0,0,0),6=>new Matrix(0,1,-1,0,0,0),7=>new Matrix(0,-1,-1,0,0,0),8=>new Matrix(0,-1,1,0,0,0),_=>Matrix.Identity};
        if(matrix.IsIdentity)return source;
        var transformed=new TransformedBitmap(source,new MatrixTransform(matrix));transformed.Freeze();return transformed;
    }
}
