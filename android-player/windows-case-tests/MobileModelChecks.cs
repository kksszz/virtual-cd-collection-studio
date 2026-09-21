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
        Console.WriteLine(path);
    }
}
