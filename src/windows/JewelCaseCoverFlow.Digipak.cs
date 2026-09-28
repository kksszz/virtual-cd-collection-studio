using System.Windows.Media;
using System.Windows.Media.Media3D;
namespace ZipMp3Player;
public sealed partial class JewelCaseCoverFlow
{
    private static ContainerUIElement3D CreateDigipakExterior(JewelCaseCoverFlowItem item,double x,double y,double z,double scale,double yaw,double pitch)
    {
        double u=DigipakDimensions.Unit,width=139*u,height=124*u,depth=11*u;
        var paper=CreateMaterial(Color.FromRgb(38,35,32),5);var group=new Model3DGroup();
        group.Children.Add(CreateBox(width,height,depth,new Point3D(0,0,0),paper));
        group.Children.Add(CreateQuad(new(-width/2,height/2,depth/2+.0001),new(width/2,height/2,depth/2+.0001),new(width/2,-height/2,depth/2+.0001),new(-width/2,-height/2,depth/2+.0001),CreateImageMaterial(item.FrontCover,item.Title,1)));
        var rear=CreateQuad(new(width/2,height/2,-depth/2-.0001),new(-width/2,height/2,-depth/2-.0001),new(-width/2,-height/2,-depth/2-.0001),new(width/2,-height/2,-depth/2-.0001),CreateOptionalArtworkMaterial(item.BackCover,item.Title,1));group.Children.Add(rear);
        if(item.Digipak?.LeftFold is {} left)group.Children.Add(CreateQuad(
            new(-width/2-.0001,height/2,-depth/2),new(-width/2-.0001,height/2,depth/2),new(-width/2-.0001,-height/2,depth/2),new(-width/2-.0001,-height/2,-depth/2),CreateImageMaterial(left,item.Title,1)));
        if(item.Digipak?.RightFold is {} right){var side=CreateQuad(
            new(width/2+.0001,height/2,depth/2),new(width/2+.0001,height/2,-depth/2),new(width/2+.0001,-height/2,-depth/2),new(width/2+.0001,-height/2,depth/2),CreateImageMaterial(right,item.Title,1));
            var geometry=(MeshGeometry3D)side.Geometry;
            geometry.TextureCoordinates=new PointCollection(geometry.TextureCoordinates.Select(p=>new System.Windows.Point(1-p.X,p.Y)));
            group.Children.Add(side);
        }
        var transforms=new Transform3DGroup();transforms.Children.Add(new ScaleTransform3D(scale,scale,scale));transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),pitch)));transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),yaw)));transforms.Children.Add(new TranslateTransform3D(x,y,z));
        var container=new ContainerUIElement3D{Transform=transforms};container.Children.Add(new ModelUIElement3D{Model=group});return container;
    }
}
