using System.Numerics;
using System.Windows.Media.Media3D;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;

namespace ZipMp3Player;

// Measured specimen, not a generic stretched jewel case. All dimensions in mm.
internal static class MultiCaseDimensions
{
    internal const float Width = 141.5f, Height = 124, Plate = 1;
    internal const float RailDepth = 20, TabDepth = 24, AxisSpacing = 14;
    internal const float AxisFromEnd = 5, ClosedDepth = 24;
    // Side scans img334/335 suggest about 7 mm for the black centre strip.
    // This is a scan-based estimate, not another caliper measurement.
    internal const float SideWallDepth = 7, SidePaperWidth = 6;
    // Keep the 120 mm booklet size; align its right edge to the inner frame.
    internal const float BookletWidth = 120, BookletRight = 69;
    internal const float BookletLeft = BookletRight - BookletWidth;
    internal const float HingeX = -Width / 2 + AxisFromEnd;
    internal static (double Front, double Center) Angles(double progress)
    {
        progress = Math.Clamp(progress, 0, 2);
        double center = Math.Max(0, progress - 1);
        // The front stays facing up while the central assembly turns over.
        return (-180 * Math.Min(progress, 1) + 180 * center, -180 * center);
    }
}

internal sealed partial class DxJewelCaseScene
{
    private GroupModel3D? _multiCenter, _multiFront;
    private readonly List<GroupModel3D> _multiDiscs = [];
    private readonly List<GroupModel3D> _multiOuterHalves = [];
    private readonly AxisAngleRotation3D _multiFrontAngle = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _multiCenterAngle = new(new Vector3D(0, 1, 0), 0);
    private bool _isMultiCase;
    private double _multiProgress;
    internal bool IsMultiCase=>_isMultiCase;
    internal bool MultiCaseTurned { get; private set; }

    internal void TurnMultiCase(bool turned,bool animate=true)
    {
        if(!_isMultiCase||!_caseIsOpen)return;
        MultiCaseTurned=turned;AnimateMultiCase(turned?2:1,animate);
    }
    private void AnimateMultiCase(double target,bool animate)
    {
        CancelDigipakAnimation();
        double from=_multiProgress;
        if(!animate){SetMultiCaseProgress(target);InvalidateFinalFrame();return;}
        var clock=System.Diagnostics.Stopwatch.StartNew();
        _digipakTick=(_,_)=>{
            double t=Math.Clamp(clock.Elapsed.TotalMilliseconds/1100,0,1);
            SetMultiCaseProgress(from+(target-from)*t*t*(3-2*t));
            if(t>=1){CancelDigipakAnimation();InvalidateFinalFrame();}
        };
        _digipakTimer.Tick+=_digipakTick;_digipakTimer.Start();
    }

    internal void BuildMultiCasePrototype(MultiCaseArtwork? artwork=null,string trayColor="Black")
    {
        // Repeated preview changes create rotated/cropped bitmaps. Do not retain
        // textures for every previous selection for the lifetime of the window.
        _textureCache.Clear();
        // Start from the normal lifecycle to clear textures, animations and groups.
        SetItem(new JewelCaseCoverFlowItem("multi-case-prototype", "24 mm", "", "", trayColor,
            null, null, null, null, null, null, null, false), -22, -18);
        // Capture the complete standard tray, including procedural spine ribs
        // and raised shoulder that are not part of the source STL BottomTray.
        var standardTray = _baseRoot.Children.OfType<MeshGeometryModel3D>()
            .Where(m => m.Material?.Name == "Tray" || (m.Material?.Name?.StartsWith("Tray spine ", StringComparison.Ordinal) ?? false))
            .ToArray();
        _baseRoot.Children.Clear(); _lidRoot.Children.Clear();
        _spineCardRoot.Children.Clear(); _wrappingUpperRoot.Children.Clear();
        _wrappingLowerRoot.Children.Clear(); _tearTapeRoot.Children.Clear();
        _multiDiscs.Clear();
        _multiDiscStates.Clear();_multiSelectedDisc=null;_multiPlayingDisc=null;
        _multiBookletPresent=artwork?.BookletFront is not null;
        _multiOuterHalves.Clear();
        Viewport.EnableSSAO = false; Viewport.IsShadowMappingEnabled = false;
        // Share the standard tray material, including colour, gloss and transparency.
        var trayModel = standardTray.First(m => m.Material?.Name == "Tray");
        var resin = trayModel.Material!;
        bool resinTransparent = trayModel.IsTransparent;
        var clear = new PBRMaterial { Name = "Multi case clear shell", AlbedoColor = new Color4(.65f,.7f,.73f,.25f),
            RoughnessFactor = .15, ReflectanceFactor = .4 };
        float hx = D(MultiCaseDimensions.HingeX);
        _multiCenter = new GroupModel3D { Transform = new RotateTransform3D(_multiCenterAngle, hx, 0, D(-7)) };
        _multiFront = new GroupModel3D { Transform = new RotateTransform3D(_multiFrontAngle, hx, 0, D(7)) };
        _baseRoot.Children.Add(_multiCenter); _multiCenter.Children.Add(_multiFront);
        var shell = CaseGeometry.Value;
        // Reuse the standard disc-side shell/tray, not the booklet lid.
        // Preserve native X/Z dimensions: fitting to an arbitrary 5 mm depth
        // flattened the well, retaining hub and raised spine shoulder.
        var all = new[] { shell.BottomTray, shell.BottomPerimeter, shell.BottomMouldedEdges };
        float zmin = all.Min(m => m.Positions!.Min(p => p.Z));
        float ymin = all.Min(m => m.Positions!.Min(p => p.Y)), ymax = all.Max(m => m.Positions!.Max(p => p.Y));
        GroupModel3D Outer(GroupModel3D parent, bool front)
        {
            var transform = new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(1, D(124)/(ymax-ymin), 1));
            transform.Children.Add(new TranslateTransform3D(0, -D(124)*(ymin+ymax)/(2*(ymax-ymin)), -D(12)-zmin));
            // Flip the disc-facing normal without moving the hinge-side X edge.
            if(front) transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),180)));
            var group = new GroupModel3D { Transform = transform }; parent.Children.Add(group);
            _multiOuterHalves.Add(group);
            foreach(var model in standardTray)
                AddMesh((HelixToolkit.SharpDX.MeshGeometry3D)model.Geometry!,model.Material!,model.IsTransparent,
                    model.CullMode==SharpDX.Direct3D11.CullMode.None,group,false);
            AddMesh(shell.BottomPerimeter,clear,true,true,group,false);
            AddMesh(shell.BottomMouldedEdges,clear,true,true,group,false);
            return group;
        }
        var rearOuter=Outer(_baseRoot,false); var frontOuter=Outer(_multiFront,true);
        // Keep these in measured case coordinates, outside the reused STL's
        // normalization transform. Front print is upright when closed.
        if(artwork?.Front is {} frontPrint)
            AddArtwork(frontPrint,D(-69),D(69),D(-58.75f),D(58.75f),D(12.05f),false,_multiFront,"Multi front jacket",twoSided:false);
        if(artwork?.Back is {} backPrint)
            AddArtwork(backPrint,D(-69),D(69),D(-58.75f),D(58.75f),D(-12.05f),true,_baseRoot,"Multi back jacket",twoSided:false);

        // The standard mesh alone does not include the folded inlay, and its
        // shallow reused walls leave an open view into the four-disc stack.
        // Each OUTER half owns its two side walls/inlay folds. They must follow
        // that half during opening rather than forming a static box around it.
        var sideBacking = new PhongMaterial { Name="Multi outer side backing", DiffuseColor=new Color4(.055f,.058f,.063f,1),
            SpecularColor=new Color4(.08f,.08f,.08f,1), SpecularShininess=24 };
        var paper = new PhongMaterial { Name="Multi placeholder spine paper", DiffuseColor=new Color4(.86f,.83f,.73f,1),
            SpecularColor=new Color4(.01f,.01f,.01f,1), SpecularShininess=8 };
        void SideFolds(GroupModel3D parent,bool front)
        {
            var sides=new GroupModel3D(); parent.Children.Add(sides);
            if(front)sides.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),180));
            foreach(float sign in new[]{-1f,1f})
            {
                // Backing spans -12..-3.5 mm; paper is the standard 6 mm fold.
                AddBox(new Vector3(D(sign*70.8f),0,D(-7.75f)),D(.25f),D(122),D(8.5f),sideBacking,false,sides,false);
                var spine=front ? (sign<0?artwork?.FrontLeft:artwork?.FrontRight) : (sign<0?artwork?.BackRight:artwork?.BackLeft);
                // Labels are scan-left / scan-right when viewed from outside.
                // Front group's X rotation requires a 180-degree UV correction.
                if(front)spine=MultiCaseArtwork.Rotate(spine,180);
                if(spine is null)
                    AddBox(new Vector3(D(sign*70.96f),0,D(-8)),D(.05f),D(117.5f),D(MultiCaseDimensions.SidePaperWidth),paper,false,sides,false);
                else {
                    var fold=new MeshBuilder(true,true,true);
                    float x=D(sign*70.99f),top=D(58.75f),bottom=-top,near=D(-5),far=D(-11);
                    if(sign<0)
                        fold.AddQuad(new Vector3(x,top,near),new Vector3(x,top,far),new Vector3(x,bottom,far),new Vector3(x,bottom,near),
                            new Vector2(1,0),new Vector2(0,0),new Vector2(0,1),new Vector2(1,1));
                    else
                        fold.AddQuad(new Vector3(x,top,far),new Vector3(x,top,near),new Vector3(x,bottom,near),new Vector3(x,bottom,far),
                            new Vector2(1,0),new Vector2(0,0),new Vector2(0,1),new Vector2(1,1));
                    AddMesh(fold.ToMeshGeometry3D(),new PhongMaterial {Name=$"Multi {(front?"front":"back")} spine {sign}",
                        DiffuseColor=new Color4(1,1,1,1),DiffuseMap=CreateTexture(spine),RenderDiffuseMap=true,
                        SpecularColor=new Color4(.035f,.035f,.035f,1),SpecularShininess=10},false,false,sides,false);
                }
                // Clear lips surround the paper without a duplicate full pane.
                foreach(float z in new[]{-11.6f,-3.9f})
                    AddBox(new Vector3(D(sign*70.94f),0,D(z)),D(.1f),D(122),D(.6f),clear,true,sides,false);
            }
        }
        SideFolds(_baseRoot,false);SideFolds(_multiFront,true);

        // Double-sided 1 mm plate. Corner cut-outs are a tessellated prototype
        // approximation of the supplied scans; no scan texture is baked in.
        Vector3 P(float x,float y,float z)=>new(D(x),D(y),D(z));
        // Both long side edges have a substantial black wall, not only the
        // 1 mm central plate. Upper/lower ribbed rails remain independent.
        AddBox(P(-69.875f,0,0),D(1.75f),D(122),D(MultiCaseDimensions.SideWallDepth),resin,resinTransparent,_multiCenter,false);
        AddMesh(CreateMultiRecessedSideWall(),resin,resinTransparent,false,_multiCenter,false);
        AddMesh(CreateMultiSmoothPlate(),resin,resinTransparent,true,_multiCenter,false);
        foreach(float y in new[]{-61.5f,61.5f})
        {
            // The widened tab replaces this rail segment; overlapping boxes
            // here would produce coplanar faces and visible flicker.
            AddMesh(CreateMultiRoundedRail(-62.75f,70.75f,10,y,0,.8f),resin,resinTransparent,false,_multiCenter,false);
            // 8 mm tab length is provisional; width and axis positions are measured.
            AddMesh(CreateMultiRoundedRail(-70.75f,-62.75f,12,y,1.2f,1.2f,true),resin,resinTransparent,false,_multiCenter,false);
            var pins = new MeshBuilder(true,false,false);
            foreach(float z in new[]{-7f,7f})
                pins.AddCylinder(P(MultiCaseDimensions.HingeX,y-.5f,z),P(MultiCaseDimensions.HingeX,y+.5f,z),D(1.5f),20);
            AddMesh(pins.ToMeshGeometry3D(),resin,resinTransparent,true,_multiCenter,false);
            // Low-relief ribs on the outward faces of the two long rails.
            var ribs = new MeshBuilder(true,false,false);
            for(float x=-65;x<70;x+=.65f)
                ribs.AddBox(P(x,y+Math.Sign(y)*.52f,0),D(.2f),D(.08f),D(19));
            AddMesh(ribs.ToMeshGeometry3D(),resin,resinTransparent,false,_multiCenter,false);
        }
        // Retain the existing standard hub's actual triangles and height.
        var source = shell.BottomTray;
        var hub = new MeshBuilder(true,false,false);
        var indices = source.Indices!; var points = source.Positions!;
        var triangles = new List<(Vector3 A,Vector3 B,Vector3 C)>();
        bool InHub(Vector3 p)=>Math.Pow(p.X-.044f,2)+Math.Pow(p.Y-.004f,2)<D(11)*D(11);
        for(int i=0;i<indices.Count;i+=3)
        {
            var a=points[indices[i]];var b=points[indices[i+1]];var c=points[indices[i+2]];
            if(InHub(a)&&InHub(b)&&InHub(c)) triangles.Add((a,b,c));
        }
        if(triangles.Count==0) throw new InvalidOperationException("標準トレーの保持部を取得できませんでした。");
        float hubBottom=triangles.Min(t=>Math.Min(t.A.Z,Math.Min(t.B.Z,t.C.Z)));
        Vector3 HubPoint(Vector3 p)=>new(p.X-.044f,p.Y-.004f,p.Z-hubBottom+D(.5f));
        foreach(var t in triangles) hub.AddTriangle(HubPoint(t.A),HubPoint(t.B),HubPoint(t.C));
        var hubGeometry=hub.ToMeshGeometry3D();
        AddMesh(hubGeometry,resin,resinTransparent,true,_multiCenter,false);
        var reverse = new GroupModel3D { Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),180)) };
        _multiCenter.Children.Add(reverse); AddMesh(hubGeometry,resin,resinTransparent,true,reverse,false);
        void Disc(GroupModel3D parent,float z,bool flip,int slot,bool outer=false)
        {
            var (number,picture)=artwork?.DiscAtSlot(slot) ?? (slot,null);
            if(picture is null)return;
            var group=new GroupModel3D(); parent.Children.Add(group); _multiDiscs.Add(group);
            if(flip) group.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),180));
            var spin=new AxisAngleRotation3D(new Vector3D(0,0,1),0);
            var spinRoot=new GroupModel3D {Transform=new RotateTransform3D(spin,outer?new Point3D(.044,.004,0):new Point3D())};
            group.Children.Add(spinRoot);
            _multiDiscStates[number]=new MultiDiscState {Number=number,Slot=slot,Root=group,Seat=parent,SpinRoot=spinRoot,
                SeatTransform=group.Transform??Transform3D.Identity,Spin=spin};
            // Outer discs share the unchanged standard tray coordinates and
            // its seating transform, rather than a separate guessed Z offset.
              // Orientation follows the physical seat, not the printed disc number.
              if(slot is 1 or 3)picture=MultiCaseArtwork.Rotate(picture,180);
              AddDisc(picture,
                outer ? new Vector3(.044f,.004f,-StandardCaseDepth*.136f) : new Vector3(0,0,D(z)),
                outer ? 1.018f : D(60),outer ? .128f : D(7.5f),outer ? .020f : D(1.2f),spinRoot,"Multi prototype disc "+number);
        }
        Disc(frontOuter,0,false,1,true); Disc(_multiCenter,3,false,2);
        Disc(_multiCenter,3,true,3); Disc(rearOuter,0,false,4,true);
        if(artwork?.BookletFront is {} booklet) {
            // Photo reference: booklet on the right-hand central tray at first
            // opening, not on the front outer tray or exterior jacket.
            // Thickness is provisional; retain the case's measured envelope.
            var paperBody=new PhongMaterial {Name="Multi booklet paper",
                DiffuseColor=new Color4(.88f,.86f,.80f,1),SpecularShininess=4};
            float left=D(MultiCaseDimensions.BookletLeft),right=D(MultiCaseDimensions.BookletRight);
            AddBox(new Vector3((left+right)/2,0,D(5)),D(MultiCaseDimensions.BookletWidth),D(120),D(.4f),paperBody,false,_multiCenter,false);
            AddArtwork(booklet,left,right,D(-60),D(60),D(5.21f),false,_multiCenter,"Multi booklet front",twoSided:false);
            if(artwork.BookletBack is {} bookletBack)
                AddArtwork(bookletBack,left,right,D(-60),D(60),D(4.79f),true,_multiCenter,"Multi booklet back",twoSided:false);
        }
        SetMultiCaseProgress(0);
    }

    internal static HelixToolkit.SharpDX.MeshGeometry3D CreateMultiSmoothPlate()
    {
        // Clip a fine circular polygon against the straight diagonal edge.
        // No occupancy grid: every hole-wall vertex lies on this contour.
        var circle=Enumerable.Range(0,256).Select(i=>new Vector2(
            49+16*(float)Math.Cos(i*Math.PI/128),44+16*(float)Math.Sin(i*Math.PI/128))).ToArray();
        var hole=new List<Vector2>();
        for(int i=0;i<circle.Length;i++) {
            var a=circle[i];var b=circle[(i+1)%circle.Length];
            float da=a.X+a.Y-80,db=b.X+b.Y-80;
            if(da>=0)hole.Add(a);
            if((da>=0)!=(db>=0))hole.Add(a+(b-a)*(da/(da-db)));
        }
        // Horizontal bands split at every contour vertex triangulate the
        // remaining face without bridging the holes, including the chord.
        var levels=hole.Select(p=>p.Y).Append(0).Append(61).Distinct().Order().ToArray();
        (float Left,float Right) Span(float y) {
            var xs=new List<float>();
            for(int i=0;i<hole.Count;i++) {
                var a=hole[i];var b=hole[(i+1)%hole.Count];
                if(y<Math.Min(a.Y,b.Y)||y>Math.Max(a.Y,b.Y))continue;
                if(a.Y==b.Y){xs.Add(a.X);xs.Add(b.X);}
                else xs.Add(a.X+(b.X-a.X)*(y-a.Y)/(b.Y-a.Y));
            }
            return (xs.Min(),xs.Max());
        }
        var mesh=new MeshBuilder(true,false,false);
        foreach(float sx in new[]{-1f,1f})foreach(float sy in new[]{-1f,1f}) {
            Vector3 V(float x,float y,float z)=>new(D(sx*x),D(sy*y),D(z));
            void Triangle(Vector3 a,Vector3 b,Vector3 c) {
                if(Vector3.Cross(b-a,c-a).LengthSquared()<1e-18f)return;
                if(sx*sy<0)mesh.AddTriangle(a,c,b);else mesh.AddTriangle(a,b,c);
            }
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
            void Face(float y0,float y1,float l0,float r0,float l1,float r1) {
                Quad(V(l0,y0,.5f),V(r0,y0,.5f),V(r1,y1,.5f),V(l1,y1,.5f));
                Quad(V(l1,y1,-.5f),V(r1,y1,-.5f),V(r0,y0,-.5f),V(l0,y0,-.5f));
            }
            for(int i=0;i<levels.Length-1;i++) {
                float a=levels[i],b=levels[i+1],mid=(a+b)/2;
                if(mid<hole.Min(p=>p.Y)||mid>hole.Max(p=>p.Y)){Face(a,b,0,69,0,69);continue;}
                var lo=Span(a);var hi=Span(b);
                Face(a,b,0,lo.Left,0,hi.Left);Face(a,b,lo.Right,69,hi.Right,69);
            }
            for(int i=0;i<hole.Count;i++) {
                var a=hole[i];var b=hole[(i+1)%hole.Count];
                Quad(V(a.X,a.Y,-.5f),V(b.X,b.Y,-.5f),V(b.X,b.Y,.5f),V(a.X,a.Y,.5f));
            }
            Quad(V(69,0,-.5f),V(69,61,-.5f),V(69,61,.5f),V(69,0,.5f));
            Quad(V(69,61,-.5f),V(0,61,-.5f),V(0,61,.5f),V(69,61,.5f));
        }
        return mesh.ToMeshGeometry3D();
    }

    // Free (right) side: a finger recess interrupts the front-facing edge.
    // Photo and img332/334 estimates: 20 mm span, 1.8 mm edge drop,
    // 0.6 mm inward bow. Keep the opposite hinge side and rear edge unchanged.
    internal static HelixToolkit.SharpDX.MeshGeometry3D CreateMultiRecessedSideWall()
    {
        var mesh=new MeshBuilder(true,false,false);
        Vector3 V(float x,float y,float z)=>new(D(x),D(y),D(z));
        Vector3[] Section(float y) {
            float curve=Math.Abs(y)<10 ? .5f*(1+(float)Math.Cos(Math.PI*y/10)) : 0;
            float top=MultiCaseDimensions.SideWallDepth/2-1.8f*curve;
            return new[]{V(69,y,-3.5f),V(70.75f,y,-3.5f),V(70.75f-.6f*curve,y,top),V(69,y,top)};
        }
        // Shared rings and smooth cosine profile avoid a box-shaped notch.
        var previous=Section(-61);
        mesh.AddQuad(previous[0],previous[1],previous[2],previous[3]);
        for(int step=1;step<=244;step++) {
            var next=Section(-61+step*.5f);
            for(int edge=0;edge<4;edge++) {
                int following=(edge+1)%4;
                mesh.AddQuad(previous[edge],next[edge],next[following],previous[following]);
            }
            previous=next;
        }
        mesh.AddQuad(previous[3],previous[2],previous[1],previous[0]);
        return mesh.ToMeshGeometry3D();
    }

    // Circular corner profiles in the scan's X/Z plane. Radii are provisional
    // estimates from img330, not measured values. Extrude without changing the
    // outer envelope or hinge axes. The tab and rail meet at a butt joint.
    internal static HelixToolkit.SharpDX.MeshGeometry3D CreateMultiRoundedRail(
        float left,float right,float halfDepth,float y,float leftRadius,float rightRadius,bool endNotch=false)
    {
        var outline=new List<Vector2>();
        void Arc(float cx,float cz,float radius,int start)
        {
            if(radius==0){outline.Add(new Vector2(cx,cz));return;}
            const int segments=12;
            for(int i=0;i<=segments;i++) {
                double angle=(start+90d*i/segments)*Math.PI/180;
                outline.Add(new Vector2(cx+radius*(float)Math.Cos(angle),cz+radius*(float)Math.Sin(angle)));
            }
        }
        Arc(left+leftRadius,-halfDepth+leftRadius,leftRadius,180);
        Arc(right-rightRadius,-halfDepth+rightRadius,rightRadius,270);
        Arc(right-rightRadius,halfDepth-rightRadius,rightRadius,0);
        Arc(left+leftRadius,halfDepth-leftRadius,leftRadius,90);
        // Shallow, rounded central recess in the projecting end (img330).
        // 6 mm width / 1.2 mm depth are visual estimates, not measured values.
        // This shallow profile remains visible from the fan centre, so the
        // face triangles do not bridge or fill the concave cut-out.
        if(endNotch) {
            const int segments=24;
            for(int i=0;i<=segments;i++) {
                float z=3-6f*i/segments;
                float inset=.6f*(1+(float)Math.Cos(Math.PI*z/3));
                outline.Add(new Vector2(left+inset,z));
            }
        }
        Vector3 V(Vector2 p,float level)=>new(D(p.X),D(level),D(p.Y));
        var mesh=new MeshBuilder(true,false,false);
        var center=new Vector2((left+right)/2,0);
        for(int i=0;i<outline.Count;i++) {
            var a=outline[i];var b=outline[(i+1)%outline.Count];
            mesh.AddTriangle(V(center,y-.5f),V(a,y-.5f),V(b,y-.5f));
            mesh.AddTriangle(V(center,y+.5f),V(b,y+.5f),V(a,y+.5f));
            mesh.AddQuad(V(a,y-.5f),V(a,y+.5f),V(b,y+.5f),V(b,y-.5f));
        }
        return mesh.ToMeshGeometry3D();
    }

    internal void SetMultiCaseProgress(double progress)
    {
        _multiProgress=Math.Clamp(progress,0,2);
        var (front,center)=MultiCaseDimensions.Angles(progress);
        _multiFrontAngle.Angle=front; _multiCenterAngle.Angle=center;
        double opening=Math.Clamp(progress,0,1);
        _spineCardOpenTranslation.OffsetX=SpineCardOpenClearanceX*opening;
        double turningClearance=.16*Math.Sin(Math.PI*Math.Clamp(progress-1,0,1));
        _openScale.ScaleX=_openScale.ScaleY=_openScale.ScaleZ=1-.34*opening-turningClearance;
        _openCenterTranslation.OffsetX=-D(MultiCaseDimensions.HingeX)*opening;
        RequestRender();
    }
    internal void SetMultiCaseDiscsVisible(bool visible)
    {
        foreach(var disc in _multiDiscs) disc.Visibility=visible?System.Windows.Visibility.Visible:System.Windows.Visibility.Hidden;
        RequestRender();
    }
}
