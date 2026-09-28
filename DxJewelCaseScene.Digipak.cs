using System.Numerics;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
namespace ZipMp3Player;

internal sealed partial class DxJewelCaseScene
{
    private bool _isDigipak;
    private GroupModel3D? _digipakLeft, _digipakRight, _digipakFolds;
    private readonly AxisAngleRotation3D _digipakLeftAngle=new(new Vector3D(0,1,0),180);
    private readonly AxisAngleRotation3D _digipakRightAngle=new(new Vector3D(0,1,0),-180);
    private readonly DispatcherTimer _digipakTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
    private EventHandler? _digipakTick;
    private double _digipakProgress;
    private bool _caseIsOpen;
    private readonly List<BitmapSource> _digipakDerivedImages=[];
    private PBRMaterial? _digipakPaper;
    private PhongMaterial? _digipakLeftFoldMaterial, _digipakRightFoldMaterial;
    internal bool IsDigipak=>_isDigipak;
    private static float D(float mm)=>mm*DigipakDimensions.Unit;
    private void CancelDigipakAnimation(){_digipakTimer.Stop();if(_digipakTick is not null)_digipakTimer.Tick-=_digipakTick;_digipakTick=null;}
    private void ResetCaseTransforms()
    {
        CancelDigipakAnimation();
        // Shared groups must leave their previous panel before a scene rebuild.
        _digipakLeft?.Children.Remove(_bookletRoot);
        _digipakRight?.Children.Remove(_secondDiscRoot);
        _isDigipak=false;_digipakLeft=_digipakRight=_digipakFolds=null;
        Viewport.IsShadowMappingEnabled=!_interactiveMotion;
        Viewport.EnableSSAO=!_interactiveMotion;
        bool wasOpen=_caseIsOpen;SetCaseOpen(false,false);_caseIsOpen=wasOpen;
        foreach(var image in _digipakDerivedImages)_textureCache.Remove(image);_digipakDerivedImages.Clear();
        _lidRoot.Transform=new RotateTransform3D(_lidHingeRotation,new Point3D(AssembledHingeX,0,0));
        var first=new Transform3DGroup();first.Children.Add(new RotateTransform3D(_discSpinRotation,new Point3D(.044,.004,0)));first.Children.Add(new RotateTransform3D(_discTiltRotation));first.Children.Add(_discTranslation);_discRoot.Transform=first;
        _secondDiscRoot.Transform=new RotateTransform3D(_secondDiscSpinRotation,new Point3D(.044,.004,0));
        _frontPanelSeatTranslation.OffsetZ=0;
    }
    private void BuildDigipak(JewelCaseCoverFlowItem item)
    {
        _isDigipak=true;_digipakProgress=0;
        // Sub-millimetre layered paper is below the shadow/AO sampling scale.
        // Preserve direct lighting and tray reflections without paper self-shadow acne.
        Viewport.IsShadowMappingEnabled=false;Viewport.EnableSSAO=false;
        _baseRoot.Children.Clear();_lidRoot.Children.Clear();_frontPanelRoot.Children.Clear();
        _lidRoot.Transform=Transform3D.Identity;
        _digipakLeft=new GroupModel3D{Transform=new RotateTransform3D(_digipakLeftAngle,D(DigipakDimensions.LeftHinge),0,D(5))};
        _digipakRight=new GroupModel3D{Transform=new RotateTransform3D(_digipakRightAngle,D(DigipakDimensions.RightHinge),0,D(4.5f))};
        _digipakFolds=new GroupModel3D();
        _baseRoot.Children.Add(_digipakRight);_baseRoot.Children.Add(_digipakFolds);_lidRoot.Children.Add(_digipakLeft);
        _digipakPaper=new PBRMaterial{Name="Digipak paper edges",AlbedoColor=new Color4(.09f,.085f,.075f,1),RoughnessFactor=.92,MetallicFactor=0};
        PhongMaterial FoldMaterial(BitmapSource? image,string name)=>new(){Name=name,DiffuseColor=new Color4(1,1,1,1),DiffuseMap=CreateTexture(image),RenderDiffuseMap=image is not null,SpecularColor=new Color4(.035f,.035f,.035f,1),SpecularShininess=10};
        _digipakLeftFoldMaterial=item.Digipak!.LeftFold is {} leftFold?FoldMaterial(leftFold,"Digipak left fold artwork"):null;
        _digipakRightFoldMaterial=item.Digipak.RightFold is {} rightFold?FoldMaterial(rightFold,"Digipak right fold artwork"):null;
        var white=new PBRMaterial{Name="Digipak unprinted paper",AlbedoColor=new Color4(.82f,.8f,.75f,1),RoughnessFactor=.9};
        void Paper(float left,GroupModel3D target,BitmapSource? inner,BitmapSource? outer,string name){
            // Printed faces ARE the paper surface. Do not place them a few
            // microns over another full face: the depth buffer cannot reliably
            // separate those layers when zoomed or tilted (z-fighting).
            var edge=new MeshBuilder(true,false,false);
            Vector3 P(float x,float y,float z)=>new(D(x),D(y),D(z));
            float right=left+138;
            edge.AddQuad(P(left,62,-.5f),P(left,62,.5f),P(left,-62,.5f),P(left,-62,-.5f));
            edge.AddQuad(P(right,62,.5f),P(right,62,-.5f),P(right,-62,-.5f),P(right,-62,.5f));
            edge.AddQuad(P(left,62,-.5f),P(right,62,-.5f),P(right,62,.5f),P(left,62,.5f));
            edge.AddQuad(P(left,-62,.5f),P(right,-62,.5f),P(right,-62,-.5f),P(left,-62,-.5f));
            if(inner is null)edge.AddQuad(P(left,62,.5f),P(left,-62,.5f),P(right,-62,.5f),P(right,62,.5f));
            if(outer is null)edge.AddQuad(P(left,62,-.5f),P(right,62,-.5f),P(right,-62,-.5f),P(left,-62,-.5f));
            AddMesh(edge.ToMeshGeometry3D(),_digipakPaper,false,false,target);
            if(inner is not null)AddArtwork(inner,D(left),D(right),D(-62),D(62),D(.5f),false,target,name+" inner",twoSided:false);
            if(outer is not null)AddArtwork(outer,D(left),D(right),D(-62),D(62),D(-.5f),true,target,name+" outer",twoSided:false);
        }
        Paper(-69,_baseRoot,null,item.BackCover,"Digipak center");
        Paper(DigipakDimensions.Left,_digipakLeft,item.Digipak!.InnerLeft,item.FrontCover,"Digipak left");
        Paper(DigipakDimensions.Right,_digipakRight,null,item.Digipak.OuterRight,"Digipak right");
        // Only the bottom of the booklet is inserted behind the slit lip.
        float slitY=D(-62+DigipakDimensions.PocketFromBottom), slitLeft=D(DigipakDimensions.Left+6.5f);
        AddBox(new Vector3(slitLeft+D(62.5f),slitY,D(.57f)),D(125),D(.15f),D(.08f),_digipakPaper,false,_digipakLeft);
        var slotEnds=new MeshBuilder(true,false,false);
        foreach(float x in new[]{slitLeft,slitLeft+D(125)})slotEnds.AddCylinder(new Vector3(x,slitY,D(.5f)),new Vector3(x,slitY,D(.65f)),D(.5f),16);
        AddMesh(slotEnds.ToMeshGeometry3D(),_digipakPaper,false,false,_digipakLeft);
        BitmapSource? Half(bool right){if(item.Digipak.Trays is not {} scan)return null;
            int start=right?(int)Math.Round(scan.PixelWidth*146d/282):0,end=right?scan.PixelWidth:(int)Math.Round(scan.PixelWidth*136d/282);
            var crop=new CroppedBitmap(scan,new Int32Rect(start,0,Math.Max(1,end-start),scan.PixelHeight));crop.Freeze();_digipakDerivedImages.Add(crop);return crop;}
        BuildDigipakTray(-69,_baseRoot,Half(false),white,item.TrayColorMode);
        BuildDigipakTray(DigipakDimensions.Right,_digipakRight,Half(true),white,item.TrayColorMode);
        float cx=D(-69+1+67),cy=D(62-61.5f);
        _baseRoot.Children.Add(_discRoot);_digipakRight.Children.Add(_secondDiscRoot);
        void DiscTransform(GroupModel3D root,AxisAngleRotation3D spin,float x){var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(spin,new Point3D(x,cy,0)));transform.Children.Add(new RotateTransform3D(_discTiltRotation,new Point3D(x,cy,0)));transform.Children.Add(_discTranslation);root.Transform=transform;}
        DiscTransform(_discRoot,_discSpinRotation,cx);
        float secondX=D(DigipakDimensions.Right+1+67);DiscTransform(_secondDiscRoot,_secondDiscSpinRotation,secondX);
        AddDisc(item.DiscImage,new Vector3(cx,cy,D(2.8f)),D(60),D(7.5f),D(1.2f),_discRoot,"Digipak disc 1");
        AddDisc(item.SecondDiscImage,new Vector3(secondX,cy,D(2.8f)),D(60),D(7.5f),D(1.2f),_secondDiscRoot,"Digipak disc 2");
        // Keep the booklet independent of the rigid cover. It slides out of the
        // slit before the existing reader-opening transition.
        _digipakLeft.Children.Add(_bookletRoot);
        float bookletLeft=D(DigipakDimensions.Left+9),bookletRight=bookletLeft+D(120),bookletBottom=D(-60),bookletTop=D(60),bookletZ=D(.64f);
        AddArtwork(item.FrontCover,bookletLeft,bookletRight,bookletBottom,bookletTop,bookletZ,false,_bookletRoot,"Digipak booklet front",twoSided:false);
        AddArtwork(item.BackCover,bookletLeft,bookletRight,bookletBottom,bookletTop,bookletZ-D(.05f),true,_bookletRoot,"Digipak booklet reverse",twoSided:false);
        _bookletOuterImage=item.BackCover;_bookletRearArtwork=_bookletRoot.Children.LastOrDefault();
        _bookletOpeningBounds=(bookletLeft,bookletRight,bookletBottom,bookletTop,bookletZ-D(.05f));
        if(item.Digipak.InnerLeft is {} pocketImage){int pixels=Math.Max(1,(int)Math.Round(pocketImage.PixelHeight*DigipakDimensions.PocketFromBottom/124d));var lip=new CroppedBitmap(pocketImage,new Int32Rect(0,pocketImage.PixelHeight-pixels,pocketImage.PixelWidth,pixels));lip.Freeze();_digipakDerivedImages.Add(lip);
            AddArtwork(lip,D(DigipakDimensions.Left),D(DigipakDimensions.Left+138),D(-62),slitY,D(.81f),false,_digipakLeft,"Digipak pocket lip",twoSided:false);}
        SetDigipakProgress(_caseIsOpen?1:0);
    }
    private void BuildDigipakTray(float panelLeft,GroupModel3D parent,BitmapSource? artwork,PBRMaterial backing,string colorMode)
    {
        float left=D(panelLeft+1),right=left+D(136),bottom=D(-62),top=D(62),z=D(.65f);
        AddBox(new Vector3((left+right)/2,0,D(.55f)),D(136),D(124),D(.1f),backing,false,parent);
        if(artwork is not null)AddArtwork(artwork,left,right,bottom,top,z,false,parent,"Digipak tray printed background",twoSided:false);
        var clear=new PBRMaterial{Name="Digipak transparent tray",AlbedoColor=colorMode switch{
            "Black"=>new Color4(.035f,.035f,.04f,1),"White"=>new Color4(.8f,.78f,.72f,1),"Gray"=>new Color4(.3f,.32f,.34f,1),_=>new Color4(.72f,.75f,.76f,.22f)},RoughnessFactor=.14,ReflectanceFactor=.4,ClearCoatStrength=.3,RenderEnvironmentMap=true};
        bool transparent=clear.AlbedoColor.Alpha<1;
        foreach(float x in new[]{left+D(.6f),right-D(.6f)})AddBox(new Vector3(x,0,D(2.5f)),D(1.2f),D(124),D(4),clear,transparent,parent);
        foreach(float y in new[]{bottom+D(.6f),top-D(.6f)})AddBox(new Vector3((left+right)/2,y,D(2.5f)),D(133.6f),D(1.2f),D(4),clear,transparent,parent);
        float cx=left+D(67),cy=D(.5f);var ring=new MeshBuilder(true,false,false);
        const int segments=128;
        void Ring(float inner,float outer,float low,float high){for(int i=0;i<segments;i++){
            float a=i*2*MathF.PI/segments,b=(i+1)*2*MathF.PI/segments;
            Vector3 P(float angle,float radius,float height)=>new(cx+MathF.Cos(angle)*D(radius),cy+MathF.Sin(angle)*D(radius),D(height));
            ring.AddQuad(P(a,inner,high),P(a,outer,high),P(b,outer,high),P(b,inner,high));
            ring.AddQuad(P(a,outer,low),P(b,outer,low),P(b,outer,high),P(a,outer,high));
            ring.AddQuad(P(b,inner,low),P(a,inner,low),P(a,inner,high),P(b,inner,high));
        }}
        Ring(60.5f,61.3f,.8f,1.7f);Ring(9,15,.8f,1.5f);
        AddMesh(ring.ToMeshGeometry3D(),clear,transparent,true,parent);
        var hub=new MeshBuilder(true,false,false);
        for(int i=0;i<12;i++){float a=i*2*MathF.PI/12,b=(i+.7f)*2*MathF.PI/12;
            Vector3 P(float angle,float radius,float height)=>new(cx+MathF.Cos(angle)*D(radius),cy+MathF.Sin(angle)*D(radius),D(height));
            hub.AddQuad(P(a,5,.7f),P(a,7.5f,3.5f),P(b,7.5f,3.5f),P(b,5,.7f));
            hub.AddQuad(P(a,7.5f,3.5f),P(a,6.5f,3.7f),P(b,6.5f,3.7f),P(b,7.5f,3.5f));
        }
        AddMesh(hub.ToMeshGeometry3D(),clear,transparent,true,parent);
        // Four finger recess edges around the well, not a solid square tray.
        var recess=new MeshBuilder(true,false,false);
        foreach(int side in new[]{-1,1})foreach(int vertical in new[]{-1,1}){
            float x=cx+D(side*50),y=cy+D(vertical*47);for(int i=0;i<24;i++){
                float a=i*2*MathF.PI/24,b=(i+1)*2*MathF.PI/24;
                Vector3 P(float angle,float r,float h)=>new(x+MathF.Cos(angle)*D(r),y+MathF.Sin(angle)*D(r),D(h));
                recess.AddQuad(P(a,9,1.4f),P(a,9.7f,1.4f),P(b,9.7f,1.4f),P(b,9,1.4f));
            }
        }
        AddMesh(recess.ToMeshGeometry3D(),clear,transparent,true,parent);
    }
    private void SetDigipakProgress(double value)
    {
        _digipakProgress=Math.Clamp(value,0,1);var angles=DigipakDimensions.Angles(_digipakProgress);
        _digipakLeftAngle.Angle=angles.Left;_digipakRightAngle.Angle=angles.Right;
        double scale=1-.58*_digipakProgress;_openScale.ScaleX=_openScale.ScaleY=_openScale.ScaleZ=scale;
        _openCenterTranslation.OffsetX=D(1)*(float)_digipakProgress;
        if(_digipakFolds is not null&&_digipakPaper is not null){_digipakFolds.Children.Clear();
            Fold(-69,DigipakDimensions.Left+138,_digipakLeft!.Transform, -1,_digipakLeftFoldMaterial);
            Fold(69,DigipakDimensions.Right,_digipakRight!.Transform,1,_digipakRightFoldMaterial);
        }
        RequestRender();
        void Fold(float fixedX,float movingX,Transform3D transform,int direction,PhongMaterial? artwork){
            var start=new Point3D(D(fixedX),0,0);var end=transform.Transform(new Point3D(D(movingX),0,0));
            var control=new Point3D((start.X+end.X)/2+direction*D(1.5f)*Math.Abs(end.Z)/D(10),(start.Y+end.Y)/2,(start.Z+end.Z)/2);
            var mesh=new MeshBuilder(true,false,false);
            Vector3 P(float t,float y,float offset){var u=1-t;
                var tangent=new Vector3((float)(2*u*(control.X-start.X)+2*t*(end.X-control.X)),0,(float)(2*u*(control.Z-start.Z)+2*t*(end.Z-control.Z)));
                var normal=Vector3.Normalize(new Vector3(direction*tangent.Z,0,-direction*tangent.X));
                return new Vector3((float)(u*u*start.X+2*u*t*control.X+t*t*end.X),D(y),(float)(u*u*start.Z+2*u*t*control.Z+t*t*end.Z))+normal*D(offset);
            }
            for(int i=0;i<16;i++){float a=i/16f,b=(i+1)/16f;
                if(direction<0){if(artwork is null)mesh.AddQuad(P(a,62,.5f),P(a,-62,.5f),P(b,-62,.5f),P(b,62,.5f));mesh.AddQuad(P(b,62,-.5f),P(b,-62,-.5f),P(a,-62,-.5f),P(a,62,-.5f));}
                else{if(artwork is null)mesh.AddQuad(P(b,62,.5f),P(b,-62,.5f),P(a,-62,.5f),P(a,62,.5f));mesh.AddQuad(P(a,62,-.5f),P(a,-62,-.5f),P(b,-62,-.5f),P(b,62,-.5f));}
            }
            AddMesh(mesh.ToMeshGeometry3D(),_digipakPaper!,false,true,_digipakFolds);
            if(artwork is not null){var printed=new MeshBuilder(true,true,true);
                for(int i=0;i<16;i++){float a=i/16f,b=(i+1)/16f;
                    if(direction<0)printed.AddQuad(P(a,62,.51f),P(a,-62,.51f),P(b,-62,.51f),P(b,62,.51f),new Vector2(a,0),new Vector2(a,1),new Vector2(b,1),new Vector2(b,0));
                    else printed.AddQuad(P(b,62,.51f),P(b,-62,.51f),P(a,-62,.51f),P(a,62,.51f),new Vector2(1-b,0),new Vector2(1-b,1),new Vector2(1-a,1),new Vector2(1-a,0));
                }
                AddMesh(printed.ToMeshGeometry3D(),artwork,false,true,_digipakFolds);
            }
        }
    }
    private void SetDigipakOpen(bool open,bool animate)
    {
        CancelDigipakAnimation();double from=_digipakProgress,target=open?1:0;
        if(!animate||Math.Abs(target-from)<.001){SetDigipakProgress(target);InvalidateFinalFrame();return;}
        var clock=System.Diagnostics.Stopwatch.StartNew();_digipakTick=(_,_)=>{
            double t=Math.Clamp(clock.Elapsed.TotalMilliseconds/1180,0,1);double eased=t*t*(3-2*t);SetDigipakProgress(from+(target-from)*eased);
            if(t>=1){CancelDigipakAnimation();SetDigipakProgress(target);InvalidateFinalFrame();}
        };_digipakTimer.Tick+=_digipakTick;_digipakTimer.Start();
    }
}
