using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Numerics;

namespace ZipMp3Player;

/// <summary>Self-contained glTF 2.0: actual meshes, PNGs, PBR materials and standard TRS animations.</summary>
public static class MobileGlbExporter
{
    public static void Export(JewelCaseCoverFlowItem item,string destination)
    {
        string snapshot=Path.Combine(Path.GetTempPath(),"virtual-cd-glb-"+Guid.NewGuid().ToString("N")+".vcd3d");
        string output=Path.GetFullPath(destination)+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            MobileCaseExporter.ExportSnapshot(item,snapshot);
            ConvertSnapshotCore(snapshot,output,DxJewelCaseScene.CaptureMobile(item));
            if(new FileInfo(output).Length>32*1024*1024)throw new InvalidDataException("GLB exceeds Android's 32 MiB limit.");
            File.Move(output,destination,overwrite:true);
        }
        finally{if(File.Exists(snapshot))File.Delete(snapshot);if(File.Exists(output))File.Delete(output);}
    }
    public static void ConvertSnapshot(string source,string destination)
        => ConvertSnapshotCore(source,destination,null);
    private static void ConvertSnapshotCore(string source,string destination,DxJewelCaseScene.MobileScene? desktop)
    {
        if(new FileInfo(source).Length>32*1024*1024)throw new InvalidDataException("Snapshot exceeds 32 MiB.");
        using var zip=ZipFile.OpenRead(source);
        if(zip.Entries.Count>32||zip.GetEntry("manifest.json") is not {Length: <=16384})throw new InvalidDataException("Invalid snapshot manifest.");
        using var manifestInput=zip.GetEntry("manifest.json")!.Open();
        using var document=JsonDocument.Parse(ReadBounded(manifestInput,16384));
        var m=document.RootElement;
        bool multi=m.GetProperty("version").GetInt32()==6&&m.GetProperty("model").GetString()=="multi-case-24mm-v1";
        bool three=m.GetProperty("version").GetInt32()==5&&m.GetProperty("model").GetString()=="digipak-three-disc-v1";
        bool digipak=three||m.GetProperty("version").GetInt32()==4&&m.GetProperty("model").GetString()=="digipak-two-disc-v1";
        bool sideBooklet=digipak&&m.TryGetProperty("bookletExtraction",out var extraction)&&extraction.GetString()=="Left";
        if(m.GetProperty("format").GetString()!="virtual-cd-case"||(!multi&&!digipak&&m.GetProperty("version").GetInt32() is not (1 or 2 or 3))||(multi||digipak)&&desktop is null)throw new InvalidDataException("Unsupported snapshot.");
        var images=new Dictionary<string,byte[]>();
        foreach(var property in m.GetProperty("textures").EnumerateObject()){
            if(!new[]{"front","insideFront","back","spine","rightSpine","inlay","inlayLeft","inlayRight","disc","obiFront","obiSpine","obiBack","obiFrontInside","obiSpineInside","obiBackInside"}.Contains(property.Name)||property.Value.GetProperty("file").GetString()!=property.Name+".png")throw new InvalidDataException("Invalid texture role.");
            var entry=zip.GetEntry(property.Name+".png");if(entry is null||entry.Length>5*1024*1024)throw new InvalidDataException("Invalid texture size.");
            using var input=entry.Open();byte[] png=ReadBounded(input,5*1024*1024);
            if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).Equals(property.Value.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Texture checksum mismatch.");
            images[property.Name]=png;
        }
        bool obi=!digipak&&m.TryGetProperty("obi",out var _)&&m.GetProperty("obi").ValueKind==JsonValueKind.Object;
        var o=obi?m.GetProperty("obi"):default;
        bool wrapped=!digipak&&m.TryGetProperty("wrapped",out var w)&&w.GetBoolean();
        if(obi){foreach(string key in new[]{"frontWidthMm","backWidthMm"}){float width=o.GetProperty(key).GetSingle();if(!float.IsFinite(width)||width<1||width>140)throw new InvalidDataException("Invalid obi width.");}}
        var model=desktop?.Meshes ?? MobileGlbGeometry.Build(new(m.GetProperty("tray").GetString()!,obi,obi?o.GetProperty("frontWidthMm").GetSingle()/100:0,obi?o.GetProperty("backWidthMm").GetSingle()/100:0,images.ContainsKey("disc")));
        if(desktop is not null)foreach(var (role,bitmap) in desktop.Images){
            System.Windows.Media.Imaging.BitmapSource image=bitmap;
            double scale=Math.Min(1,1024d/Math.Max(image.PixelWidth,image.PixelHeight));
            if(scale<1)image=new System.Windows.Media.Imaging.TransformedBitmap(image,new System.Windows.Media.ScaleTransform(scale,scale));
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output=new MemoryStream();encoder.Save(output);if(output.Length>5*1024*1024)throw new InvalidDataException("Texture exceeds limit");images[role]=output.ToArray();
        }
        if(images.Count>32)throw new InvalidDataException("Texture count exceeds limit");
        var writer=new GlbWriter();
        var textureIds=new Dictionary<string,int>();
        foreach(var (role,png) in images){int id=writer.Images.Count;writer.Images.Add(new {name=role,mimeType="image/png",bufferView=writer.View(png)});writer.Textures.Add(new {source=id,sampler=0});textureIds[role]=id;}
        string[] names=multi?["Rear","Center","Front","Disc1","Disc2","Disc3","Disc4","SpineCard"]:
            three?["Center","LeftPanel","Disc1","RightPanel","Disc2","Booklet","LeftFold","RightFold","FarRightPanel","Disc3","FarRightFold"]:
            digipak?["Center","LeftPanel","Disc1","RightPanel","Disc2","Booklet","LeftFold","RightFold"]:["Case","Lid","Disc","Obi","FilmTop","FilmBottom","Tape"];
        var children=Enumerable.Range(0,names.Length).Select(_=>new List<int>()).ToArray();
        writer.Nodes.Add(new {name="CD case (metres)",scale=new[]{.1f,.1f,.1f},children=Enumerable.Range(1,names.Length).ToArray()});
        for(int p=0;p<names.Length;p++)writer.Nodes.Add(new {});
        foreach(var mesh in model){
            var positions=new float[mesh.count*3];var normals=new float[mesh.count*3];var uv=new float[mesh.count*2];
            for(int v=0;v<mesh.count;v++){Array.Copy(mesh.vertices,v*8,positions,v*3,3);Array.Copy(mesh.vertices,v*8+5,normals,v*3,3);Array.Copy(mesh.vertices,v*8+3,uv,v*2,2);}
            var pbr=new Dictionary<string,object>{{"baseColorFactor",mesh.color},{"metallicFactor",mesh.finish==2?0.8f:0f},{"roughnessFactor",mesh.finish==0?.85f:.22f}};
            if(textureIds.TryGetValue(mesh.texture,out int texture))pbr["baseColorTexture"]=new {index=texture};
            int material=writer.Materials.Count;writer.Materials.Add(new {name=names[mesh.part]+" / "+mesh.texture,pbrMetallicRoughness=pbr,alphaMode=mesh.color[3]<1?"BLEND":"OPAQUE",doubleSided=false,extras=new {virtualCdFinish=mesh.finish}});
            int index=writer.Meshes.Count;writer.Meshes.Add(new {primitives=new[]{new {attributes=new Dictionary<string,int>{{"POSITION",writer.Accessor(positions,3,true,true)},{"NORMAL",writer.Accessor(normals,3,false,true)},{"TEXCOORD_0",writer.Accessor(uv,2,false,true)}},material,mode=4}}});
            children[mesh.part].Add(writer.Nodes.Count);writer.Nodes.Add(new {name=names[mesh.part]+" "+index,mesh=index});
        }
        for(int p=0;p<names.Length;p++){float initialScale=!multi&&!digipak&&p>=4&&!wrapped?0:1;var node=new Dictionary<string,object>{{"name",names[p]},{"scale",new[]{initialScale,initialScale,initialScale}},{"extras",new {virtualCdPart=p}}};if(children[p].Count>0)node["children"]=children[p];
            if(digipak){var pose=DigipakPose(p,0,0,0,three,sideBooklet);node["translation"]=new[]{pose.Shift.X,pose.Shift.Y,pose.Shift.Z};node["rotation"]=new[]{pose.Rotation.X,pose.Rotation.Y,pose.Rotation.Z,pose.Rotation.W};}writer.Nodes[p+1]=node;}
        // The clips are useful in ordinary glTF viewers too. Application buttons retain
        // their interruption-safe sequencing; extras identify roles, not replacement geometry.
        foreach(string clip in multi?new[]{"Open","Turn","Disc1Out","Disc2Out","Disc3Out","Disc4Out","ObiOff"}:
            digipak?new[]{"Open","DiscOut","BookletOut"}:new[]{"Open","DiscOut","ObiOff","WrapOff"}){
            var samplers=new List<object>();var channels=new List<object>();
            const int count=41;float[] times=Enumerable.Range(0,count).Select(i=>i*.04f).ToArray();int time=writer.Accessor(times,1,true);
            for(int part=1;part<names.Length;part++){
                if(!multi&&!digipak&&part==3&&!obi)continue;
                var translations=new float[count*3];var rotations=new float[count*4];var scales=new float[count*3];
                for(int frame=0;frame<count;frame++){
                    float t=times[frame],wrap=wrapped?Math.Clamp(t/.4f,0,1):1;
                    float band=clip=="WrapOff"?0:Math.Clamp((t-.4f)/.4f,0,1);
                    float lid=clip is "Open" or "DiscOut"?Math.Clamp((t-.8f)/.4f,0,1):0;
                    float disc=clip=="DiscOut"?Math.Clamp((t-1.2f)/.4f,0,1):0;
                    Vector3 shift=Vector3.Zero;Quaternion rotation=Quaternion.Identity;float scale=1;
                    if(multi) {
                        float progress=clip=="Turn"?1+Math.Clamp(t/1.6f,0,1):clip=="Open"?Math.Clamp(t/1.6f,0,1):
                            clip is "Disc3Out" or "Disc4Out"?2:clip.StartsWith("Disc",StringComparison.Ordinal)?1:0;
                        int multiDiscNumber=clip.StartsWith("Disc",StringComparison.Ordinal)?clip[4]-'0':0;
                        var pose=MultiCasePose(part,progress,part==multiDiscNumber+2?Math.Clamp((t-1.2f)/.4f,0,1):0,
                            clip=="ObiOff"?Math.Clamp(t/1.6f,0,1):0);
                        shift=pose.Shift;rotation=pose.Rotation;
                    } else if(digipak) {
                        float progress=Math.Clamp(t/1.2f,0,1),take=Math.Clamp((t-1.2f)/.4f,0,1);
                        var pose=DigipakPose(part,progress,clip=="DiscOut"?take:0,clip=="BookletOut"?take:0,three,sideBooklet);shift=pose.Shift;rotation=pose.Rotation;
                    } else {
                    if(part==1){rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-155*lid*MathF.PI/180);var pivot=desktop is null?new Vector3(-.69f,0,.045f):new Vector3(DxJewelCaseScene.MobileHingeX,0,0);shift=pivot-Vector3.Transform(pivot,rotation);}
                    if(part==2){rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitX,-25*disc*MathF.PI/180);var pivot=desktop is null?new Vector3(.06f,0,0):new Vector3(.044f,.004f,0)*DxJewelCaseScene.MobileScale;shift=new Vector3(.30f*disc,.08f*disc,.65f*disc)+pivot-Vector3.Transform(pivot,rotation);}
                    if(part==3){shift=new(-1.2f*band,0,.06f*band);scale=1-band;}
                    if(part>=4){shift=part==4?new(0,.8f*wrap,.15f*wrap):part==5?new(0,-.3f*wrap,.08f*wrap):new(1.3f*wrap,0,.2f*wrap);scale=1-wrap;}
                    }
                    translations[frame*3]=shift.X;translations[frame*3+1]=shift.Y;translations[frame*3+2]=shift.Z;
                    rotations[frame*4]=rotation.X;rotations[frame*4+1]=rotation.Y;rotations[frame*4+2]=rotation.Z;rotations[frame*4+3]=rotation.W;
                    scales[frame*3]=scales[frame*3+1]=scales[frame*3+2]=scale;
                }
                void Channel(string path,float[] values,int width){int sampler=samplers.Count;samplers.Add(new {input=time,output=writer.Accessor(values,width),interpolation="LINEAR"});channels.Add(new {sampler,target=new {node=part+1,path}});}
                Channel("translation",translations,3);Channel("rotation",rotations,4);Channel("scale",scales,3);
            }
            writer.Animations.Add(new {name=clip,samplers,channels});
        }
        var profile=new {profile=multi?"multi-case-24mm-glb-1":three?"digipak-three-disc-glb-1":digipak?"digipak-two-disc-glb-1":desktop is null?"jewel-case-glb-1":"jewel-case-glb-2",title=m.GetProperty("title").GetString(),artist=m.GetProperty("artist").GetString(),tray=m.GetProperty("tray").GetString(),obi=obi?JsonSerializer.Deserialize<object>(o.GetRawText()):null,wrapped,bookletExtraction=digipak?(sideBooklet?"Left":"Top"):null,
            attribution=desktop is null||digipak?null:"CD, DVD case — Moder (@MHKK_1419475), https://www.printables.com/model/647946-cd-dvd-case — CC BY 4.0 https://creativecommons.org/licenses/by/4.0/ . Modified: normalized, separated, remeshed and textured by Virtual CD Collection Studio."};
        writer.Save(destination,profile);
    }
    // Closed meshes are captured from the measured Windows assembly. These
    // two pivots reproduce its nested front/centre hinges in mobile units.
    internal static (Vector3 Shift,Quaternion Rotation) MultiCasePose(int part,float progress,float discOut=0,float obiOff=0)
    {
        progress=Math.Clamp(progress,0,2);
        float unit=DigipakDimensions.Unit*DxJewelCaseScene.MobileScale;
        float hinge=MultiCaseDimensions.HingeX*unit;
        var centerPivot=new Vector3(hinge,0,-7*unit);
        var frontPivot=new Vector3(hinge,0,7*unit);
        float turn=Math.Max(0,progress-1),opening=Math.Min(progress,1);
        var center=Quaternion.CreateFromAxisAngle(Vector3.UnitY,-MathF.PI*turn);
        var front=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI*(-opening+turn));
        bool followsCenter=part is 1 or 2 or 3 or 4 or 5;
        bool followsFront=part is 2 or 3;
        var rotation=Quaternion.Identity;
        var shift=Vector3.Zero;
        if(followsFront){rotation=front;shift=frontPivot-Vector3.Transform(frontPivot,front);}
        if(followsCenter){shift=Vector3.Transform(shift,center)+centerPivot-Vector3.Transform(centerPivot,center);rotation=Quaternion.Normalize(center*rotation);}
        if(part is >=3 and <=6 && discOut>0)shift+=new Vector3(.28f*discOut,.08f*discOut,.55f*discOut);
        if(part==7&&obiOff>0)shift+=new Vector3(-1.1f*obiOff,0,.05f*obiOff);
        return (shift,rotation);
    }
    // Flat bind pose in centimetres, identical to the Android pose contract.
    internal static (Vector3 Shift,Quaternion Rotation) DigipakPose(int part,float open,float disc,float booklet)
        => DigipakPose(part,open,disc,booklet,false,false);
    internal static (Vector3 Shift,Quaternion Rotation) DigipakPose(int part,float open,float disc,float booklet,bool three,bool sideBooklet)
    {
        var angles=DigipakDimensions.Angles(open);var threeAngles=DigipakDimensions.ThreeAngles(open);
        float x=0,z=0,angle=0;
        if(part is 1 or 5 or 6){x=three?-.78f:-.75f;z=three?.09f:.05f;angle=three?(float)threeAngles.Left:(float)angles.Left;}
        if(part is 3 or 4 or 7){x=three ? .7725f : .74f;z=three?.09f:.045f;angle=three?(float)threeAngles.Right:(float)angles.Right;}
        if(part is 8 or 9 or 10){x=2.29f;z=.045f;angle=(float)threeAngles.FarRight;}
        if(part is 6 or 7 or 10){angle*=.5f;x=part==6?-.69f:part==7 ? .69f : 2.235f;z=0;} // GLB folds pivot at their fixed edge.
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,angle*MathF.PI/180);var pivot=new Vector3(x,0,z);
        var shift=pivot-Vector3.Transform(pivot,rotation);
        if(part is 8 or 9 or 10){var parent=DigipakPose(3,open,0,0,three,false);shift=parent.Shift+Vector3.Transform(shift,parent.Rotation);rotation=Quaternion.Normalize(parent.Rotation*rotation);}
        if(part is 2 or 4 or 9){var center=new Vector3(part==2?-.01f:part==4?(three?1.535f:1.47f):3.025f,.005f,0);var tilt=Quaternion.CreateFromAxisAngle(Vector3.UnitX,-25*disc*MathF.PI/180);var local=new Vector3(.30f*disc,.08f*disc,.65f*disc)+center-Vector3.Transform(center,tilt);shift+=Vector3.Transform(local,rotation);rotation=Quaternion.Normalize(rotation*tilt);}
        if(part==5)shift+=Vector3.Transform(new Vector3(sideBooklet?-1.32f*booklet:0,sideBooklet?0:1.25f*booklet,.06f*booklet),rotation);
        return(shift,rotation);
    }
    private static byte[] ReadBounded(Stream input,int limit){using var bytes=new MemoryStream();byte[] buffer=new byte[8192];int n;while((n=input.Read(buffer))>0){if(bytes.Length+n>limit)throw new InvalidDataException("Snapshot entry exceeds limit.");bytes.Write(buffer,0,n);}return bytes.ToArray();}
    private sealed class GlbWriter
    {
        readonly MemoryStream binary=new();
        readonly List<object> views=new(),accessors=new();
        internal readonly List<object> Images=new(),Textures=new(),Materials=new(),Meshes=new(),Nodes=new(),Animations=new();
        internal int View(byte[] bytes,int? target=null){while(binary.Length%4!=0)binary.WriteByte(0);int offset=(int)binary.Length;binary.Write(bytes);int index=views.Count;var view=new Dictionary<string,object>{{"buffer",0},{"byteOffset",offset},{"byteLength",bytes.Length}};if(target!=null)view["target"]=target.Value;views.Add(view);return index;}
        internal int Accessor(float[] values,int width,bool bounds=false,bool vertex=false){byte[] bytes=new byte[values.Length*4];Buffer.BlockCopy(values,0,bytes,0,bytes.Length);int view=View(bytes,vertex?34962:null);var a=new Dictionary<string,object>{{"bufferView",view},{"componentType",5126},{"count",values.Length/width},{"type",width==1?"SCALAR":"VEC"+width}};
            if(bounds){var min=Enumerable.Repeat(float.MaxValue,width).ToArray();var max=Enumerable.Repeat(float.MinValue,width).ToArray();for(int i=0;i<values.Length;i++){min[i%width]=Math.Min(min[i%width],values[i]);max[i%width]=Math.Max(max[i%width],values[i]);}a["min"]=min;a["max"]=max;}int index=accessors.Count;accessors.Add(a);return index;}
        internal void Save(string path,object profile){
            var root=new {asset=new {version="2.0",generator="Virtual CD Collection Studio"},scene=0,scenes=new[]{new {nodes=new[]{0}}},nodes=Nodes,meshes=Meshes,materials=Materials,images=Images.Count>0?Images:null,textures=Textures.Count>0?Textures:null,samplers=Textures.Count>0?new[]{new {magFilter=9729,minFilter=9729,wrapS=33071,wrapT=33071}}:null,animations=Animations,accessors,bufferViews=views,buffers=new[]{new {byteLength=(int)binary.Length}},extras=new {virtualCd=profile}};
            byte[] json=JsonSerializer.SerializeToUtf8Bytes(root,new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});int jl=(json.Length+3)&~3,bl=((int)binary.Length+3)&~3;long length=28L+jl+bl;if(length>32*1024*1024)throw new InvalidDataException("GLBが32MiBの上限を超えました。");
            string full=Path.GetFullPath(path),temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var file=new BinaryWriter(new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))){file.Write(0x46546c67);file.Write(2);file.Write((int)length);file.Write(jl);file.Write(0x4e4f534a);file.Write(json);for(int i=json.Length;i<jl;i++)file.Write((byte)32);file.Write(bl);file.Write(0x004e4942);file.Write(binary.ToArray());for(long i=binary.Length;i<bl;i++)file.Write((byte)0);}File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
