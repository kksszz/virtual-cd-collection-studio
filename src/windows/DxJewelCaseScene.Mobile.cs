using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using Mesh = ZipMp3Player.MobileGlbGeometry.Mesh;

namespace ZipMp3Player;

internal sealed partial class DxJewelCaseScene
{
    internal const float MobileScale = 1.42f / 2.42f;
    internal const float MobileHingeX = (float)AssembledHingeX * MobileScale;
    internal sealed record MobileScene(List<Mesh> Meshes, Dictionary<string, BitmapSource> Images);

    // Capture the same assembled geometry as Windows, before camera/animation transforms.
    // No second, hand-maintained approximation of the shell or retaining parts.
    internal static MobileScene CaptureMobile(JewelCaseCoverFlowItem item)
    {
        using var scene = new DxJewelCaseScene();
        scene.SetItem(item, 0, 0);
        var images = new Dictionary<string, BitmapSource>();
        var roles = new Dictionary<BitmapSource, string>(ReferenceEqualityComparer.Instance);
        void Register(string role, BitmapSource? bitmap) { if(bitmap is not null) { images[role]=bitmap; roles.TryAdd(bitmap,role); } }
        Register("front", item.FrontCover); Register("insideFront", item.InsideFrontCover);
        Register("back", item.BackCover); Register("spine", item.SpineCover);
        Register("rightSpine", item.RightSpineCover); Register("disc", item.DiscImage);
        var meshes = new List<Mesh>();
        void Visit(Element3D element, Matrix3D parent, int part)
        {
            if(ReferenceEquals(element,scene._discRoot)) part=2;
            if(ReferenceEquals(element,scene._secondDiscRoot)) return; // Existing single-disc mobile contract.
            var matrix=element.Transform?.Value ?? Matrix3D.Identity; matrix.Append(parent);
            if(element is GroupModel3D group) { foreach(var child in group.Children) Visit(child,matrix,part); return; }
            if(element is not MeshGeometryModel3D model || model.Geometry is not HelixToolkit.SharpDX.MeshGeometry3D g
                || g.Positions is null || g.Indices is null) return;
            string role=""; var color=new float[]{1,1,1,1}; int finish=1;
            if(model.Material is PBRMaterial pbr) { var c=pbr.AlbedoColor; color=[c.Red,c.Green,c.Blue,c.Alpha]; finish=pbr.MetallicFactor>.5?2:1; }
            if(model.Material is PhongMaterial phong) {
                var c=phong.DiffuseColor; color=[c.Red,c.Green,c.Blue,c.Alpha]; finish=0;
                if(phong.DiffuseMap is not null) {
                    var bitmap=scene._textureCache.FirstOrDefault(p=>ReferenceEquals(p.Value,phong.DiffuseMap)).Key;
                    if(bitmap is not null) { if(!roles.TryGetValue(bitmap,out role!)) { role="detail"+images.Count; Register(role,bitmap); } }
                }
            }
            if(part>=4) finish=3;
            for(int c=0;c<4;c++)color[c]=Math.Clamp(color[c],0,1);
            if(color[3]<=0) return;
            bool twoSided=model.CullMode==SharpDX.Direct3D11.CullMode.None;
            var values=new List<float>(g.Indices.Count*8*(twoSided?2:1));
            for(int t=0;t<g.Indices.Count;t+=3) {
                var a=g.Positions[g.Indices[t]];var b=g.Positions[g.Indices[t+1]];var c=g.Positions[g.Indices[t+2]];
                var face=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(b-a,c-a));
                if(!float.IsFinite(face.X))continue;
                void Vertex(int index, bool reverse) {
                    var v=g.Positions[index];var p=matrix.Transform(new Point3D(v.X,v.Y,v.Z));
                    var n=g.Normals is {Count:>0}?g.Normals[index]:face;
                    var normal=matrix.Transform(new Vector3D(n.X,n.Y,n.Z)); normal.Normalize(); if(reverse)normal.Negate();
                    var uv=g.TextureCoordinates is {Count:>0}?g.TextureCoordinates[index]:default;
                    values.AddRange([(float)p.X*MobileScale,(float)p.Y*MobileScale,(float)p.Z*MobileScale,uv.X,uv.Y,(float)normal.X,(float)normal.Y,(float)normal.Z]);
                }
                for(int i=0;i<3;i++)Vertex(g.Indices[t+i],false);
                if(twoSided)for(int i=2;i>=0;i--)Vertex(g.Indices[t+i],true);
            }
            if(values.Count>0)meshes.Add(new Mesh(values.ToArray(),role,color,part,finish));
        }
        Visit(scene._baseRoot,Matrix3D.Identity,0);
        // The lid is closed: its group rotation is deliberately not baked into vertices.
        Visit(scene._frontPanelRoot,Matrix3D.Identity,1);
        Visit(scene._spineCardRoot,Matrix3D.Identity,3);
        Visit(scene._wrappingUpperRoot,Matrix3D.Identity,4);
        Visit(scene._wrappingLowerRoot,Matrix3D.Identity,5);
        Visit(scene._tearTapeRoot,Matrix3D.Identity,6);
        // Batch material-equivalent pieces per moving part to keep draw calls bounded.
        var result=new List<Mesh>();
        foreach(var group in meshes.GroupBy(m=>$"{m.part}|{m.finish}|{m.texture}|{string.Join(',',m.color)}")) {
            var first=group.First();
            result.Add(new Mesh(group.SelectMany(m=>m.vertices).ToArray(),first.texture,first.color,first.part,first.finish));
        }
        if(result.Sum(m=>m.count)>600000)throw new System.IO.InvalidDataException("高精細モデルの頂点数が上限を超えました。");
        return new(result,images);
    }
}
