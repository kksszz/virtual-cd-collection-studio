using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
namespace ZipMp3Player;

public sealed partial class JewelCaseCoverFlow
{
    // Lightweight closed exterior for the collection rack and WPF fallback.
    private static ContainerUIElement3D CreateMultiCaseExterior(JewelCaseCoverFlowItem item,double x,double y,double z,double scale,double yaw,double pitch)
    {
        double u=DigipakDimensions.Unit,w=142*u,h=124*u,d=24*u,ph=117.5*u;
        var art=item.MultiCase!;var group=new Model3DGroup();
        var trayColor=item.TrayColorMode switch {
            "White"=>Color.FromRgb(220,218,207),
            "Black"=>Color.FromRgb(51,50,56),
            "Gray"=>Color.FromRgb(90,94,98),
            "Clear"=>Color.FromArgb(44,224,232,236),
            _=>Color.FromRgb(29,32,37)
        };
        group.Children.Add(CreateBox(w,h,d,new Point3D(),CreateMaterial(trayColor,5)));
        group.Children.Add(CreateQuad(new(-69*u,ph/2,d/2+.0001),new(69*u,ph/2,d/2+.0001),new(69*u,-ph/2,d/2+.0001),new(-69*u,-ph/2,d/2+.0001),CreateImageMaterial(art.Front,item.Title,1)));
        group.Children.Add(CreateQuad(new(69*u,ph/2,-d/2-.0001),new(-69*u,ph/2,-d/2-.0001),new(-69*u,-ph/2,-d/2-.0001),new(69*u,-ph/2,-d/2-.0001),CreateOptionalArtworkMaterial(art.Back,item.Title,1)));
        void Fold(BitmapSource? image,bool left,bool front) {
            if(image is null)return;
            double side=(left?-1:1)*(w/2+.0001),z0=(front?5:-11)*u,z1=z0+6*u;
            var quad=left
                ?CreateQuad(new(side,ph/2,z0),new(side,ph/2,z1),new(side,-ph/2,z1),new(side,-ph/2,z0),CreateImageMaterial(image,item.Title,1))
                :CreateQuad(new(side,ph/2,z1),new(side,ph/2,z0),new(side,-ph/2,z0),new(side,-ph/2,z1),CreateImageMaterial(image,item.Title,1));
            group.Children.Add(quad);
        }
        Fold(art.FrontLeft,true,true);Fold(art.FrontRight,false,true);
        Fold(art.BackRight,true,false);Fold(art.BackLeft,false,false);
        if(item.SpineCard is {} obi) {
            var (back,spine,front)=SpineCardArtwork.Split(obi);
            double ch=120*u,ps=ch/back.PixelHeight,ox=-w/2-.014,fz=d/2+.006,bz=-fz;
            group.Children.Add(CreateQuad(new(ox,ch/2,fz),new(ox+front.PixelWidth*ps,ch/2,fz),
                new(ox+front.PixelWidth*ps,-ch/2,fz),new(ox,-ch/2,fz),CreateImageMaterial(front,item.Title,1)));
            group.Children.Add(CreateQuad(new(ox,ch/2,bz),new(ox+back.PixelWidth*ps,ch/2,bz),
                new(ox+back.PixelWidth*ps,-ch/2,bz),new(ox,-ch/2,bz),CreateImageMaterial(back,item.Title,1),reflected:true));
            var side=CreateQuad(new(ox,ch/2,fz),new(ox,ch/2,bz),
                new(ox,-ch/2,bz),new(ox,-ch/2,fz),CreateImageMaterial(spine,item.Title,1));
            // The DirectX viewer maps the scan's spine from back to front.
            // The rack uses this separate WPF exterior, so match that U axis
            // instead of showing the printed spine mirrored in the rack.
            ((MeshGeometry3D)side.Geometry).TextureCoordinates =
                [new Point(1,0),new Point(0,0),new Point(0,1),new Point(1,1)];
            group.Children.Add(side);
        }
        var t=new Transform3DGroup();t.Children.Add(new ScaleTransform3D(scale,scale,scale));
        t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),pitch)));
        t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),yaw)));t.Children.Add(new TranslateTransform3D(x,y,z));
        var container=new ContainerUIElement3D{Transform=t};container.Children.Add(new ModelUIElement3D{Model=group});return container;
    }
}
