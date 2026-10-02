using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class MobileModelChecks
{
    public static void Run()
    {
        var app=new Application();
        var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.MobileGlbGeometry")!;
        var options=type.GetNestedType("Options",BindingFlags.NonPublic)!;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        foreach(string tray in new[]{"Clear","Black","White","Gray"})
        {
            var opt=Activator.CreateInstance(options,[tray,true,.3f,.4f,true])!;
            var meshes=((System.Collections.IEnumerable)type.GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[opt])!).Cast<object>().ToArray();
            string Role(object m)=>(string)m.GetType().GetField("texture",flags)!.GetValue(m)!;
            float[] Vert(object m)=>(float[])m.GetType().GetField("vertices",flags)!.GetValue(m)!;
            float[] Axis(string role,int axis)=>Vert(meshes.Single(m=>Role(m)==role)).Where((v,i)=>i%8==axis).ToArray();
            void Near(float a,float b){if(Math.Abs(a-b)>.000002f)throw new Exception($"Geometry mismatch {tray}: {a} != {b}");}
            Near(Axis("back",0).Min(),-.69f);Near(Axis("back",0).Max(),.69f);
            Near(Axis("spine",2).Max()-Axis("spine",2).Min(),.06f);
            Near(Axis("back",2).Min(),Axis("spine",2).Min());
            Near(Axis("inlay",0).Max(),Axis("inlayRight",0).Max());
            Near(Axis("inlay",0).Min(),Axis("inlayLeft",0).Min());
            Near(Axis("inlay",2).Min(),Axis("inlayRight",2).Min());
            // No opaque tray material may hide either exterior Spine face.
            foreach(var mesh in meshes.Where(m=>Role(m)==""
                &&(int)m.GetType().GetField("part",flags)!.GetValue(m)! == 0
                &&((float[])m.GetType().GetField("color",flags)!.GetValue(m)!)[3]==1))
                if(Vert(mesh).Where((v,i)=>i%8==0).Any(x=>Math.Abs(x)>.6891f))
                    throw new Exception($"Tray occludes Spine: {tray}");
            var trayOnly=Activator.CreateInstance(type,true)!;
            type.GetMethod("detailedTray",flags)!.Invoke(trayOnly,[new float[]{.84f,.82f,.75f,1},tray=="Clear"]);
            var trayMeshes=((System.Collections.IEnumerable)type.GetField("meshes",flags)!.GetValue(trayOnly)!).Cast<object>();
            if(trayMeshes.SelectMany(Vert).Where((v,i)=>i%8==0).Any(x=>Math.Abs(x)>.6891f))
                throw new Exception($"Tray extends beyond paper fold: {tray}");
            Near(Axis("obiFront",2).Max(),Axis("obiSpine",2).Max());
            Near(Axis("obiBack",2).Min(),Axis("obiSpine",2).Min());
            Near(Axis("obiFrontInside",2).Max(),Axis("obiSpineInside",2).Max());
            Near(Axis("obiBackInside",2).Min(),Axis("obiSpineInside",2).Min());
            var glass=meshes.Single(m=>(int)m.GetType().GetField("part",flags)!.GetValue(m)! == 1
                &&Math.Abs(((float[])m.GetType().GetField("color",flags)!.GetValue(m)!)[3]-.065f)<.00001);
            Near(Vert(glass).Where((v,i)=>i%8==0).Min(),-.53f);
        }
        var path=Path.Combine(Path.GetTempPath(),"mobile-model-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
        BitmapSource Picture(int w,int h){
            var pixels=Enumerable.Repeat((byte)255,w*h*4).ToArray();
            for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(80+w%130);pixels[i+1]=(byte)(80+h%130);pixels[i+2]=(byte)(100+(w+h)%100);}
            var bmp=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);bmp.Freeze();return bmp;
        }
        var image=Picture(150,118);
        var obi=Picture(80,120);
        var item=new JewelCaseCoverFlowItem("test","Mobile geometry check","Test","DIR","Clear",image,image,image,image,image,image,image,false){SpineCard=obi,SpineCardReverse=obi};
        var snapshot=Path.Combine(path,"glb-check.vcd3d");var glb=Path.Combine(path,"glb-check.glb");
        MobileCaseExporter.Export(item,snapshot);MobileGlbExporter.Export(item,glb);
        using(var zip=ZipFile.OpenRead(snapshot)){
            using var doc=JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            if(doc.RootElement.GetProperty("version").GetInt32()!=3)throw new Exception("Snapshot version");
            foreach(var role in new[]{"inlayLeft","inlayRight","obiFrontInside","obiSpineInside","obiBackInside"})
                if(zip.GetEntry(role+".png")==null)throw new Exception("Missing exported "+role);
        }
        using(var input=new BinaryReader(File.OpenRead(glb))){
            input.ReadBytes(12);int size=input.ReadInt32();input.ReadInt32();
            using var doc=JsonDocument.Parse(input.ReadBytes(size));
            if(doc.RootElement.GetProperty("images").GetArrayLength()>32)throw new Exception("GLB image count");
            if(doc.RootElement.GetProperty("extras").GetProperty("virtualCd").GetProperty("profile").GetString()!="jewel-case-glb-2")throw new Exception("Desktop geometry profile");
        }
        Console.WriteLine("PASS mobile: four tray modes, Back/Spine and Inlay folds, front stop, both obi sides, v3 snapshot and GLB");
        foreach(string tray in new[]{"Clear","Black","White","Gray"}) {
            var digipak=item with {SpineCard=null,SpineCardReverse=null,TrayColorMode=tray,SecondDiscImage=Picture(180,180),Digipak=new(image,image,Picture(282,124)){
                OuterFront=Picture(150,119),LeftFold=Picture(12,124),RightFold=Picture(10,124),InnerLeftFold=Picture(8,124),InnerRightFold=Picture(8,124)}};
            var output=Path.Combine(path,"digipak-"+tray+".glb");MobileCaseExporter.Export(digipak,output);
            using var input=new BinaryReader(File.OpenRead(output));input.ReadBytes(12);int size=input.ReadInt32();input.ReadInt32();using var doc=JsonDocument.Parse(input.ReadBytes(size));var model=doc.RootElement;
            if(model.GetProperty("extras").GetProperty("virtualCd").GetProperty("profile").GetString()!="digipak-two-disc-glb-1")throw new Exception("Digipak profile");
            var nodes=model.GetProperty("nodes");if(nodes[0].GetProperty("children").GetArrayLength()!=8)throw new Exception("Digipak moving parts");
            foreach(int part in new[]{0,1,2,3,4,5,6,7})if(nodes[part+1].GetProperty("children").GetArrayLength()==0)throw new Exception("Missing digipak part "+part);
            foreach(var role in new[]{"disc2","outerFront","innerLeftFold","innerRightFold"})
                if(!model.GetProperty("images").EnumerateArray().Any(i=>i.GetProperty("name").GetString()==role))throw new Exception("Missing digipak image "+role);
            foreach(string clip in new[]{"Open","DiscOut","Disc1Out","Disc2Out","BookletOut"})if(!model.GetProperty("animations").EnumerateArray().Any(a=>a.GetProperty("name").GetString()==clip))throw new Exception("Missing digipak animation "+clip);
            var capture=DxJewelCaseScene.CaptureMobile(digipak);
            float[] X(int part)=>capture.Meshes.Where(m=>m.part==part).SelectMany(m=>m.vertices.Where((v,i)=>i%8==0)).ToArray();
            float[] Z(int part)=>capture.Meshes.Where(m=>m.part==part).SelectMany(m=>m.vertices.Where((v,i)=>i%8==2)).ToArray();
            if(Math.Abs(X(1).Min()+2.19f)>.001||Math.Abs(X(3).Max()-2.17f)>.001)throw new Exception("Digipak bind pose scale");
            if(Z(1).Max()-Z(1).Min()<.024f||Math.Abs(Z(5).Max()-Z(5).Min()-.015f)>.001f)
                throw new Exception("Digipak front and booklet thickness missing from mobile geometry");
            var left=MobileGlbExporter.DigipakPose(1,.5f,0,0);var right=MobileGlbExporter.DigipakPose(3,.5f,0,0);
            if(Math.Abs(left.Rotation.W-1)>.001||Math.Abs(right.Rotation.W)>.001)throw new Exception("Digipak opening order");
            if(MobileGlbExporter.DigipakPose(5,1,0,1).Shift.Y<1.24f)throw new Exception("Booklet upward slide");
        }
        Console.WriteLine("PASS mobile digipak: four trays, eight moving parts, independent disc GLB animations and flat bind pose");
        var triple=item with {SpineCard=null,SpineCardReverse=null,SecondDiscImage=Picture(180,180),
            Digipak=new DigipakArtwork(image,image,null){DiscCount=3,BookletExtraction="Left",ThirdDisc=Picture(180,180),
                Tray1=Picture(136,124),Tray2=Picture(136,124),Tray3=Picture(136,124),
                LeftFold=Picture(18,124),RightFold=Picture(17,124),FarRightFold=Picture(11,124),OuterFarRight=image}};
        var triplePath=Path.Combine(path,"digipak-three.glb");MobileCaseExporter.Export(triple,triplePath);
        using(var input=new BinaryReader(File.OpenRead(triplePath))){input.ReadBytes(12);int size=input.ReadInt32();input.ReadInt32();using var doc=JsonDocument.Parse(input.ReadBytes(size));var root=doc.RootElement;
            if(root.GetProperty("extras").GetProperty("virtualCd").GetProperty("profile").GetString()!="digipak-three-disc-glb-1")throw new Exception("Three-disc profile");
            if(root.GetProperty("extras").GetProperty("virtualCd").GetProperty("bookletExtraction").GetString()!="Left")throw new Exception("Side booklet metadata");
            if(!root.GetProperty("animations").EnumerateArray().Any(a=>a.GetProperty("name").GetString()=="Disc3Out"))throw new Exception("Missing third disc animation");
            if(root.GetProperty("nodes")[0].GetProperty("children").GetArrayLength()!=11)throw new Exception("Three-disc moving parts");
            if(!root.GetProperty("images").EnumerateArray().Any(i=>i.GetProperty("name").GetString()=="disc3"))throw new Exception("Third disc texture");}
        var tripleCapture=DxJewelCaseScene.CaptureMobile(triple);
        if(!new[]{8,9,10}.All(part=>tripleCapture.Meshes.Any(mesh=>mesh.part==part)))throw new Exception("Third panel, disc and fold geometry");
        var sideBookletZ=tripleCapture.Meshes.Where(mesh=>mesh.part==5)
            .SelectMany(mesh=>mesh.vertices.Where((value,index)=>index%8==2)).ToArray();
        if(Math.Abs(sideBookletZ.Max()-sideBookletZ.Min()-.015f)>.001f)
            throw new Exception("Side-extracted booklet thickness missing from mobile geometry");
        var bookletPose=MobileGlbExporter.DigipakPose(5,1,0,1,true,true);
        if(bookletPose.Shift.X>-.5f||Math.Abs(bookletPose.Shift.Y)>.01f)throw new Exception("Booklet slides left");
        if(MobileGlbExporter.DigipakPose(5,1,0,1,false,true).Shift.X>-.5f
            ||MobileGlbExporter.DigipakPose(5,1,0,1,true,false).Shift.Y<1.24f)
            throw new Exception("Booklet direction must be independent of disc count");
        Console.WriteLine("PASS mobile three-disc digipak: eleven moving parts, third disc and left booklet extraction");
        Console.WriteLine(path);
    }
}
