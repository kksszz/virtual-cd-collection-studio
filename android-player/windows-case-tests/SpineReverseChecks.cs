using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf.SharpDX;
using ZipMp3Player;

internal static class SpineReverseChecks
{
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"obi-reverse-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(directory,"data"));
        var app=new Application();
        var assembly=typeof(MainWindow).Assembly;
        var foldType=assembly.GetType("ZipMp3Player.SpineCardArtwork")!;
        BitmapSource Scan(string name)
        {
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()){
                dc.DrawRectangle(Brushes.Red,null,new Rect(0,0,90,120));
                dc.DrawRectangle(Brushes.Green,null,new Rect(90,0,30,120));
                dc.DrawRectangle(Brushes.Blue,null,new Rect(120,0,180,120));
            }
            var bitmap=new RenderTargetBitmap(300,120,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(Path.Combine(directory,name)))encoder.Save(output);
            foldType.GetMethod("SetManualFolds")!.Invoke(null,[bitmap,.3,.4]);
            return bitmap;
        }
        var outside=Scan("outside.png");var inside=Scan("inside.png");
        var rolesPath=(string)typeof(MainWindow).GetMethod("GetArtworkRolesPath",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[directory])!;
        Directory.CreateDirectory(Path.GetDirectoryName(rolesPath)!);
        File.WriteAllText(rolesPath,JsonSerializer.Serialize(new Dictionary<string,string>{
            ["file:"+Path.Combine(directory,"outside.png")]="SpineCard",
            ["file:"+Path.Combine(directory,"inside.png")]="SpineCardReverse"}));
        var foldsPath=Path.Combine(Path.GetDirectoryName(rolesPath)!,"spine-card-folds.json");
        File.WriteAllText(foldsPath,JsonSerializer.Serialize(new Dictionary<string,object>{
            ["file:"+Path.Combine(directory,"outside.png")]=new{Left=.3,Right=.4},
            ["file:"+Path.Combine(directory,"inside.png")]=new{Left=.2,Right=.5}}));
        var album=new ZipAlbum{Path=directory,Tracks=[new ZipTrack()]};
        var itemType=typeof(MainWindow).GetNestedType("AlbumListItem",BindingFlags.NonPublic)!;
        var item=Activator.CreateInstance(itemType,album,false)!;
        itemType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(item,[640]);
        var reverse=(BitmapSource?)itemType.GetProperty("SpineCardReverseThumbnail")!.GetValue(item);
        if(reverse is null)throw new Exception("Reverse role not loaded");
        var region=foldType.GetMethod("GetRegions")!.Invoke(null,[reverse])!;
        var spine=(Int32Rect)region.GetType().GetProperty("Spine")!.GetValue(region)!;
        if(Math.Abs(spine.Width-reverse.PixelWidth*.3)>1)throw new Exception("Independent saved reverse folds lost");
        var sceneType=assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        using var scene=(IDisposable)Activator.CreateInstance(sceneType)!;
        var viewport=(Viewport3DX)sceneType.GetProperty("Viewport")!.GetValue(scene)!;
        if(viewport.ShowViewCube||viewport.ShowCoordinateSystem)
            throw new Exception("Navigation gizmos overlap the playback overlay");
        var root=(GroupModel3D)sceneType.GetField("_spineCardRoot",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(scene)!;
        var add=sceneType.GetMethod("AddSpineCard",BindingFlags.NonPublic|BindingFlags.Instance)!;
        add.Invoke(scene,[outside,inside,2.84f,2.5f,.2f]);
        var meshes=root.Children.OfType<MeshGeometryModel3D>().ToArray();
        void CheckSeams(){
            var parts=root.Children.OfType<MeshGeometryModel3D>().ToArray();
            var sideMesh=(HelixToolkit.SharpDX.MeshGeometry3D)parts.Single(m=>m.Material!.Name=="Spine Card spine").Geometry!;
            foreach(var name in new[]{"Spine Card front flap","Spine Card back flap"}){
                var flap=(HelixToolkit.SharpDX.MeshGeometry3D)parts.Single(m=>m.Material!.Name==name).Geometry!;
                if(sideMesh.Positions!.Count(p=>flap.Positions!.Contains(p))!=2)throw new Exception("Open obi seam: "+name);
            }
        }
        CheckSeams();
        if(meshes.Length!=6)throw new Exception("Expected six paper faces");
        foreach(var face in meshes){
            var material=(PhongMaterial)face.Material!;
            if(!material.RenderDiffuseMap||material.DiffuseMap is null||face.CullMode!=SharpDX.Direct3D11.CullMode.Back)
                throw new Exception("Missing texture or opposing-face culling: "+material.Name);
        }
        foreach(var pair in new[]{(0,3),(1,4),(2,5)}){
            var a=(HelixToolkit.SharpDX.MeshGeometry3D)meshes[pair.Item1].Geometry!;var b=(HelixToolkit.SharpDX.MeshGeometry3D)meshes[pair.Item2].Geometry!;
            if(System.Numerics.Vector3.Dot(a.Normals![0],b.Normals![0])>-.99f)throw new Exception("Reverse winding");
            if(a.Positions!.Any(p=>!b.Positions!.Contains(p)))throw new Exception("Paper dimensions changed");
        }
        root.Children.Clear();add.Invoke(scene,[outside,null,2.84f,2.5f,.2f]);
        CheckSeams();
        var plain=(HelixToolkit.SharpDX.MeshGeometry3D)root.Children.OfType<MeshGeometryModel3D>().Single(m=>m.Material!.Name=="Spine Card paper reverse").Geometry!;
        var outsideSide=(HelixToolkit.SharpDX.MeshGeometry3D)root.Children.OfType<MeshGeometryModel3D>().Single(m=>m.Material!.Name=="Spine Card spine").Geometry!;
        if(outsideSide.Positions!.Any(p=>!plain.Positions!.Contains(p)))throw new Exception("Plain reverse seam");
        if(root.Children.OfType<MeshGeometryModel3D>().Count(m=>((PhongMaterial)m.Material!).Name=="Spine Card paper reverse")!=1)
            throw new Exception("White reverse fallback lost");
        Console.WriteLine("PASS separate persisted reverse role/folds, six textured opposing faces, exterior dimensions, white fallback. Fixture: "+directory);
    }
}
