using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ZipMp3Player;
using DxGroup=HelixToolkit.Wpf.SharpDX.GroupModel3D;
using DxElement=HelixToolkit.Wpf.SharpDX.Element3D;
using DxMesh=HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;

internal static class DigipakChecks
{
    internal static void Run(string? images)
    {
        var root=Path.Combine(Path.GetTempPath(),"vccs-digipak-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(root,"data"));_=new Application();
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        const BindingFlags stat=BindingFlags.Static|BindingFlags.NonPublic;
        var albumKey=Path.Combine(root,"fixture-album");
        string settingsPath=(string)typeof(MainWindow).GetMethod("GetCaseAppearancePath",stat)!.Invoke(null,[albumKey])!;Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath,"{\"TrayColor\":\"Clear\",\"CaseType\":\"Digipak2\"}");
        typeof(MainWindow).GetMethod("SaveTrayColor",stat)!.Invoke(null,[albumKey,"White"]);
        Check(File.ReadAllText(settingsPath).Contains("Digipak2")&&File.ReadAllText(settingsPath).Contains("White"),"tray setting preserves case format");
        var window=new MainWindow();var tray=(ComboBox)window.FindName("TrayColorCombo");var type=(ComboBox)window.FindName("CaseTypeCombo");
        Check(ReferenceEquals(tray.Parent,type.Parent)&&type.Items.Count==2,"case type selector beside tray color");
        var navigation=(WrapPanel)window.FindName("ImageNavigationPanel");
        foreach(int width in new[]{742,500}){
            navigation.Measure(new Size(width,double.PositiveInfinity));navigation.Arrange(new Rect(0,0,width,navigation.DesiredSize.Height));
            Check(navigation.Children.Cast<FrameworkElement>().All(e=>e is StackPanel),"navigation wraps labelled control groups at "+width);
            var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(navigation),null,new Rect(0,0,width,navigation.ActualHeight));
            var bitmap=new RenderTargetBitmap(width,(int)Math.Ceiling(navigation.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(root,"toolbar-"+width+".png"));encoder.Save(output);
        }
        DigipakArtwork? mappedArtwork=null;
        if(images is not null){
            var realAlbum=ZipAlbumReader.OpenFolder(Path.GetDirectoryName(images)!);
            var profile=(string)typeof(MainWindow).GetMethod("GetCaseAppearancePath",stat)!.Invoke(null,[realAlbum.Path])!;
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(profile,"{\"TrayColor\":\"Clear\",\"CaseType\":\"Digipak2\"}");
            var listType=typeof(MainWindow).GetNestedType("AlbumListItem",BindingFlags.NonPublic)!;
            var listItem=Activator.CreateInstance(listType,[realAlbum])!;
            listType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(listItem,[640]);
            var mapped=(DigipakArtwork?)listType.GetProperty("Digipak")!.GetValue(listItem);
            mappedArtwork=mapped;
            Check(mapped?.InnerLeft is not null&&mapped.OuterRight is not null&&mapped.Trays is not null,"album scans load through saved case profile");
            Check(mapped?.LeftFold is not null&&mapped.RightFold is not null,"scan spine strips are preserved");
            Check(listType.GetProperty("SecondDiscThumbnail")!.GetValue(listItem) is not null,"album profile loads second disc artwork");
            var sources=((System.Collections.IEnumerable)typeof(MainWindow).GetMethod("GetCaseArtworkSources",stat)!.Invoke(null,[realAlbum,Path.Combine(root,"empty-downloads")])!).Cast<object>();
            var roles=new Dictionary<string,string>();
            foreach(var source in sources){var sourceType=source.GetType();string name=(string)sourceType.GetProperty("DisplayName")!.GetValue(source)!;if(Path.GetFileName(name) is "Booklet010.jpg" or "Booklet011.jpg")roles[(string)sourceType.GetProperty("RoleKey")!.GetValue(source)!]=name.EndsWith("010.jpg")?"LeftSpine":"RightSpine";}
            typeof(MainWindow).GetMethod("SaveArtworkRoles",stat)!.Invoke(null,[realAlbum.Path,roles]);
            listType.GetMethod("RefreshImageCount")!.Invoke(listItem,null);listType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(listItem,[640]);
            var manual=(DigipakArtwork)listType.GetProperty("Digipak")!.GetValue(listItem)!;
            Check(manual.LeftFold!.PixelWidth>manual.LeftFold.PixelHeight&&manual.RightFold!.PixelWidth>manual.RightFold.PixelHeight,"manual LeftSpine/RightSpine overrides strip fallback without cropping");
        }
        BitmapSource? Load(string name){if(images is null)return null;var file=Path.Combine(images,name);using var stream=File.OpenRead(file);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1400;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;}
        var item=new JewelCaseCoverFlowItem("fixture","ActRaiser","Yuzo Koshiro","DIR","Clear",Load("Booklet001.jpg"),null,Load("Booklet002.jpg"),null,null,null,Load("Booklet008.jpg"),false){SecondDiscImage=Load("Booklet009.jpg"),Digipak=new(Load("Booklet010.jpg"),Load("Booklet011.jpg"),Load("Digipac001.jpg"))};
        if(mappedArtwork is not null)item=item with{Digipak=mappedArtwork};
        using var scene=new DxJewelCaseScene();scene.SetItem(item,0,0);
        object Field(string name)=>typeof(DxJewelCaseScene).GetField(name,flags)!.GetValue(scene)!;
        List<(DxMesh Model,Matrix3D Matrix)> Meshes(){List<(DxMesh,Matrix3D)> result=[];
            void Visit(DxElement element,Matrix3D parent){var matrix=element.Transform?.Value??Matrix3D.Identity;matrix.Append(parent);if(element is DxGroup group){foreach(var child in group.Children)Visit(child,matrix);}else if(element is DxMesh model)result.Add((model,matrix));}
            Visit((DxGroup)Field("_baseRoot"),Matrix3D.Identity);Visit((DxGroup)Field("_lidRoot"),Matrix3D.Identity);return result;
        }
        List<Point3D> PaperPoints()=>Meshes().Where(m=>m.Model.Material?.Name=="Digipak paper edges"&&m.Model.Geometry.Positions?.Count is >=16 and <=24).SelectMany(m=>m.Model.Geometry.Positions!.Select(v=>m.Matrix.Transform(new Point3D(v.X,v.Y,v.Z)))).ToList();
        var closed=PaperPoints();Check(closed.Count>0,"three paper panels constructed");
        double depth=(closed.Max(p=>p.Z)-closed.Min(p=>p.Z))/DigipakDimensions.Unit;
        Check(Math.Abs(depth-11)<.02,"closed paper/tray stack is 11 mm");
        Check(((DxGroup)Field("_discRoot")).Children.Count>0&&((DxGroup)Field("_secondDiscRoot")).Children.Count>0,"both discs have real geometry");
        Render("closed.png");
        if(images is not null){
            var right=(HelixToolkit.SharpDX.MeshGeometry3D)Meshes().Single(m=>m.Model.Material?.Name=="Digipak right fold artwork").Model.Geometry;
            Check(right.Normals!.All(n=>n.X>.8f),"right spine normals point outward");
            Check(right.TextureCoordinates![0].X<right.TextureCoordinates[2].X,"right spine horizontal mapping is flipped");
            var paper=Meshes().Where(m=>m.Model.Material?.Name=="Digipak paper edges"&&m.Model.Geometry.Positions?.Count==16).ToList();
            Check(paper.Count==1,"printed left panel has edges only, no competing solid faces");
            Check(((HelixToolkit.SharpDX.MeshGeometry3D)paper[0].Model.Geometry).Normals!.All(n=>Math.Abs(n.Z)<.001),"no solid face behind cover artwork");
        }
        scene.BeginInteractiveMotion();scene.EndInteractiveMotion();
        Check(!scene.Viewport.IsShadowMappingEnabled&&!scene.Viewport.EnableSSAO,"digipak paper self-shadow remains disabled after rotation");
        typeof(DxJewelCaseScene).GetMethod("SetDigipakProgress",flags)!.Invoke(scene,[.5]);
        Check(Math.Abs(((AxisAngleRotation3D)Field("_digipakLeftAngle")).Angle)<.001&&Math.Abs(((AxisAngleRotation3D)Field("_digipakRightAngle")).Angle+180)<.001,"first stage opens only left cover");Render("first-fold.png");
        scene.SetCaseOpen(true,false);var opened=PaperPoints();
        if(images is not null)Check(Meshes().Count(m=>m.Model.Material?.Name is "Digipak left fold artwork" or "Digipak right fold artwork")==2,"both fold surfaces have textures");
        Check(Math.Abs((opened.Max(p=>p.X)-opened.Min(p=>p.X))/DigipakDimensions.Unit-436)<.02,"full-open width is 436 mm");
        Check(Math.Abs(((AxisAngleRotation3D)Field("_digipakRightAngle")).Angle)<.001,"second stage opens right tray");Render("open.png");
        scene.SetBookletRemoved(true,false);Check(((TranslateTransform3D)Field("_bookletTranslation")).OffsetY>0,"booklet slides upward");Render("booklet-out.png");
        scene.SetBookletRemoved(false,false);Check(((TranslateTransform3D)Field("_bookletTranslation")).OffsetY==0,"booklet returns to slit");
        scene.SetItem(item,0,0);Check(((AxisAngleRotation3D)Field("_digipakRightAngle")).Angle==0,"artwork refresh retains open pose");
        scene.SetDiscRemoved(true,false);scene.SetDiscRemoved(false,false);
        try{MobileCaseExporter.Export(item,Path.Combine(root,"unsupported.vcd3d"));throw new Exception("Incorrect single-disc mobile export accepted");}catch(NotSupportedException){Console.WriteLine("PASS unsupported mobile export explicitly rejected");}
        scene.SetCaseOpen(false,false);scene.SetItem(item with{Digipak=null},0,0);Check(!scene.IsDigipak,"standard case still builds after format switch");
        Check(scene.Viewport.IsShadowMappingEnabled&&scene.Viewport.EnableSSAO,"standard case lighting remains enabled");
        Console.WriteLine("Previews: "+root);
        void Render(string name){
            var group=new Model3DGroup();group.Children.Add(new AmbientLight(Color.FromRgb(190,190,190)));group.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-1,-3)));
            var textures=(Dictionary<BitmapSource,HelixToolkit.SharpDX.TextureModel>)Field("_textureCache");
            foreach(var (model,matrix) in Meshes()){
                if(model.Geometry is not HelixToolkit.SharpDX.MeshGeometry3D g||g.Positions is null||g.Indices is null)continue;
                var geometry=new MeshGeometry3D{Positions=new Point3DCollection(g.Positions.Select(p=>matrix.Transform(new Point3D(p.X,p.Y,p.Z)))),TriangleIndices=new Int32Collection(g.Indices)};
                if(g.TextureCoordinates is not null)geometry.TextureCoordinates=new PointCollection(g.TextureCoordinates.Select(p=>new Point(p.X,p.Y)));
                Brush brush=Brushes.Gray;
                if(model.Material is HelixToolkit.Wpf.SharpDX.PhongMaterial phong){var bitmap=textures.FirstOrDefault(t=>ReferenceEquals(t.Value,phong.DiffuseMap)).Key;brush=bitmap is null?Brushes.LightGray:new ImageBrush(bitmap);}
                else if(model.Material is HelixToolkit.Wpf.SharpDX.PBRMaterial pbr){var color=pbr.AlbedoColor;brush=new SolidColorBrush(Color.FromScRgb(color.Alpha,color.Red,color.Green,color.Blue));}
                var material=new DiffuseMaterial(brush);group.Children.Add(new GeometryModel3D(geometry,material){BackMaterial=model.CullMode==SharpDX.Direct3D11.CullMode.None?material:null});
            }
            var viewport=new Viewport3D{Width=1200,Height=540,Camera=new OrthographicCamera(new Point3D(0,1.8,10),new Vector3D(0,-1.8,-10),new Vector3D(0,1,0),8.2)};viewport.Children.Add(new ModelVisual3D{Content=group});
            var panel=new Grid{Width=1200,Height=540,Background=new SolidColorBrush(Color.FromRgb(15,18,23))};panel.Children.Add(viewport);panel.Measure(new Size(1200,540));panel.Arrange(new Rect(0,0,1200,540));panel.UpdateLayout();
            var render=new RenderTargetBitmap(1200,540,96,96,PixelFormats.Pbgra32);render.Render(panel);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using var output=File.Create(Path.Combine(root,name));png.Save(output);
        }
    }
}
