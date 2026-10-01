using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class MultiCaseIntegrationChecks
{
    internal static void Run(string root,MultiCaseArtwork mapped,BitmapSource front,BitmapSource back)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.NonPublic;
        const BindingFlags field=BindingFlags.Instance|BindingFlags.NonPublic;
        string albumPath=Path.Combine(root,"album");Directory.CreateDirectory(albumPath);
        using(var wav=new NAudio.Wave.WaveFileWriter(Path.Combine(albumPath,"01.wav"),new NAudio.Wave.WaveFormat(44100,16,2)))wav.Write(new byte[17640],0,17640);
        foreach(var (name,image) in new[]{("front.png",front),("back.png",back),("disc1.png",mapped.Disc1!),("disc2.png",mapped.Disc2!),("disc3.png",mapped.Disc3!),("disc4.png",mapped.Disc4!),("obi.png",front),("booklet.png",back)}) {
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(albumPath,name));png.Save(file);
        }
        var album=ZipAlbumReader.OpenFolder(albumPath);
        var main=new MainWindow();typeof(MainWindow).GetField("_album",field)!.SetValue(main,album);
        var combo=(ComboBox)main.FindName("CaseTypeCombo");
        combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"Multi24"));
        string profile=(string)typeof(MainWindow).GetMethod("GetCaseAppearancePath",stat)!.Invoke(null,[album.Path])!;
        Check(File.ReadAllText(profile).Contains("Multi24"),"normal case selector persists Multi24");
        typeof(MainWindow).GetMethod("SaveTrayColor",stat)!.Invoke(null,[album.Path,"White"]);
        Check(File.ReadAllText(profile).Contains("Multi24"),"tray change preserves Multi24");
        var bookletPanel=(StackPanel)main.FindName("BookletExtractionPanel");
        Check(bookletPanel.Visibility==Visibility.Collapsed,"24mm case hides booklet direction label and selector");
        combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"Digipak2"));
        Check(bookletPanel.Visibility==Visibility.Visible,"digipak shows booklet direction label and selector");
        combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"Standard"));
        Check(bookletPanel.Visibility==Visibility.Collapsed,"standard case hides booklet direction");
        combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"Multi24"));
        var rolesControl=(ComboBox)main.FindName("ArtworkRoleCombo");
        Check(rolesControl.Items.OfType<ComboBoxItem>().Any(i=>Equals(i.Tag,"Disc4"))&&rolesControl.Items.OfType<ComboBoxItem>().Any(i=>Equals(i.Tag,"MultiFrontWithSpines")),"regular artwork selector exposes multi-case roles");
        var sources=(System.Collections.IEnumerable)typeof(MainWindow).GetMethod("GetCaseArtworkSources",stat)!.Invoke(null,[album,Path.Combine(root,"empty")])!;
        var roles=new Dictionary<string,string>();
        foreach(var source in sources) {
            string name=(string)source.GetType().GetProperty("DisplayName")!.GetValue(source)!;
            string key=(string)source.GetType().GetProperty("RoleKey")!.GetValue(source)!;
            roles[key]=name.Contains("booklet")?"Front":name.Contains("front")?"MultiFrontWithSpines":name.Contains("back")?"BackWithSpines":name.Contains("obi")?"SpineCard":
                "Disc"+System.Text.RegularExpressions.Regex.Match(name,@"disc([1-4])").Groups[1].Value;
        }
        typeof(MainWindow).GetMethod("SaveArtworkRoles",stat)!.Invoke(null,[album.Path,roles]);
        var listType=typeof(MainWindow).GetNestedType("AlbumListItem",BindingFlags.NonPublic)!;
        var first=Activator.CreateInstance(listType,[album])!;
        listType.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(first,[640]);
        var art=(MultiCaseArtwork?)listType.GetProperty("MultiCase")!.GetValue(first);
        Check(art?.Front is not null&&art.Back is not null&&art.FrontLeft is not null&&art.FrontRight is not null&&art.Disc4 is not null,"saved roles load into normal album model");
        Check(art!.Disc1 is not null&&art.Disc2 is not null&&art.Disc3 is not null&&art.Disc4 is not null,"all four numbered disc assignments load");
        Check(art.BookletFront is not null&&art.BookletFront.PixelWidth>art.Front!.PixelWidth,"separate Front booklet is not the cropped exterior insert");
        uint Pixel(BitmapSource image) {
            var converted=new System.Windows.Media.Imaging.FormatConvertedBitmap(image,System.Windows.Media.PixelFormats.Bgra32,null,0);
            var data=new byte[4];converted.CopyPixels(new Int32Rect(image.PixelWidth/2,image.PixelHeight/2,1,1),data,4,0);
            return BitConverter.ToUInt32(data);
        }
        Check(new[]{art.Disc1!,art.Disc2!,art.Disc3!,art.Disc4!}.Select(Pixel).Distinct().Count()==4,"disc slots retain four distinct selected textures");
        Check(art.BackLeft is not null&&art.BackRight is not null,"existing BackWithSpines maps both rear spines");
        Check(!rolesControl.Items.OfType<ComboBoxItem>().Any(i=>i.Tag?.ToString()?.StartsWith("MultiBack")==true),"no duplicate 24mm Back definitions");
        Check(Enumerable.Range(1,4).All(n=>rolesControl.Items.OfType<ComboBoxItem>().Any(i=>Equals(i.Tag,"Disc"+n)&&Equals(i.Content,"Disc"+n))),"four explicit numbered disc roles");
        var second=Activator.CreateInstance(listType,[album])!;
        var legacy=roles.ToDictionary(p=>p.Key,p=>p.Value=="Disc1"?"Disc":p.Value=="BackWithSpines"?"MultiBackWithSpines":p.Value);
        typeof(MainWindow).GetMethod("SaveArtworkRoles",stat)!.Invoke(null,[album.Path,legacy]);
        ((Task)listType.GetMethod("EnsureCaseArtworkLoadedAsync")!.Invoke(second,[1200,CancellationToken.None])!).GetAwaiter().GetResult();
        Check(listType.GetProperty("MultiCase")!.GetValue(second) is MultiCaseArtwork,"async loading restores saved Multi24");
        var legacyArt=(MultiCaseArtwork)listType.GetProperty("MultiCase")!.GetValue(second)!;
        Check(legacyArt.Disc1 is not null&&legacyArt.BackLeft is not null&&legacyArt.BackRight is not null,"legacy Disc and MultiBack assignments remain readable");
        listType.GetMethod("ReleaseHighResolutionCaseArtwork")!.Invoke(second,[640]);
        Check(listType.GetProperty("MultiCase")!.GetValue(second) is MultiCaseArtwork,"high-resolution release retains case format");
        var item=new JewelCaseCoverFlowItem("integrated","Multi24","","","White",null,null,null,null,null,null,null,false){MultiCase=mapped};
        MultiDiscChecks.Run(item);
        using var scene=new DxJewelCaseScene();scene.SetItem(item,0,0);
        for(int mask=0;mask<16;mask++) {
            var partial=mapped with {Disc1=(mask&1)!=0?mapped.Disc1:null,Disc2=(mask&2)!=0?mapped.Disc2:null,
                Disc3=(mask&4)!=0?mapped.Disc3:null,Disc4=(mask&8)!=0?mapped.Disc4:null};
            scene.SetItem(item with {MultiCase=partial},0,0);
            var discs=(List<HelixToolkit.Wpf.SharpDX.GroupModel3D>)typeof(DxJewelCaseScene).GetField("_multiDiscs",field)!.GetValue(scene)!;
            Check(discs.Count==System.Numerics.BitOperations.PopCount((uint)mask),"only assigned discs have meshes: "+mask);
            for(int slot=1;slot<=4;slot++) {
                int expected=mask==3?(slot==1?1:slot==4?2:0):((mask&(1<<(slot-1)))!=0?slot:0);
                var actual=partial.DiscAtSlot(slot);
                Check(expected==0?actual.Image is null:actual.Image is not null&&actual.Number==expected,"physical disc slot "+mask+"/"+slot);
            }
            if(mask==3) {
                var halves=(List<HelixToolkit.Wpf.SharpDX.GroupModel3D>)typeof(DxJewelCaseScene).GetField("_multiOuterHalves",field)!.GetValue(scene)!;
                Check(halves.All(h=>discs.Count(d=>h.Children.Contains(d))==1),"two-disc pair uses front and rear trays");
                scene.SetMultiCaseDiscsVisible(false);scene.SetMultiCaseDiscsVisible(true);
                Check(discs.Count==2,"visibility toggle does not restore unassigned discs");
            }
        }
        scene.SetItem(item,0,0);
        Check(rolesControl.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"MultiFrontWithSpines")).Content.ToString()!.Contains("左右Spine"),"front insert role explicitly identifies both spines");
        foreach(string color in new[]{"White","Black","Gray","Clear","Auto"}) {
            scene.SetItem(item with {TrayColorMode=color},0,0);
            var center=(HelixToolkit.Wpf.SharpDX.GroupModel3D)typeof(DxJewelCaseScene).GetField("_multiCenter",field)!.GetValue(scene)!;
            var halves=(List<HelixToolkit.Wpf.SharpDX.GroupModel3D>)typeof(DxJewelCaseScene).GetField("_multiOuterHalves",field)!.GetValue(scene)!;
            var outer=halves[0].Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().First(m=>m.Material?.Name=="Tray");
            var central=center.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToArray();
            Check(central.Length>=8&&central.All(m=>ReferenceEquals(m.Material,outer.Material)&&m.IsTransparent==outer.IsTransparent),"central material matches outer tray: "+color);
        }
        scene.SetItem(item,0,0);
        var obiItem=item with {SpineCard=front,SpineCardReverse=back};
        scene.SetItem(item with {MultiCase=mapped with {BookletFront=front,BookletBack=back,Disc3=null,Disc4=null}},0,0);
        var bookletCenter=(HelixToolkit.Wpf.SharpDX.GroupModel3D)typeof(DxJewelCaseScene).GetField("_multiCenter",field)!.GetValue(scene)!;
        var bookletMesh=bookletCenter.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().Single(m=>m.Material?.Name=="Multi booklet front");
        Check(((HelixToolkit.SharpDX.MeshGeometry3D)bookletMesh.Geometry!).Positions!.All(p=>p.Z>0),"booklet belongs to central front face");
        var bookletPoints=((HelixToolkit.SharpDX.MeshGeometry3D)bookletMesh.Geometry!).Positions!;
        Check(Math.Abs(bookletPoints.Max(p=>p.X)/DigipakDimensions.Unit-69)<.001
            &&Math.Abs((bookletPoints.Max(p=>p.X)-bookletPoints.Min(p=>p.X))/DigipakDimensions.Unit-120)<.001,
            "booklet shifts right 9 mm without resizing");
        foreach(var surface in bookletCenter.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>()
            .Where(m=>m.Material?.Name is "Multi booklet back" or "Multi booklet paper")) {
            var points=((HelixToolkit.SharpDX.MeshGeometry3D)surface.Geometry!).Positions!;
            Check(Math.Abs(points.Max(p=>p.X)-bookletPoints.Max(p=>p.X))<.0001
                &&Math.Abs(points.Min(p=>p.X)-bookletPoints.Min(p=>p.X))<.0001,"booklet body and reverse share shifted edges");
        }
        scene.SetCaseOpen(true,false);scene.TurnMultiCase(true,false);
        Check(bookletCenter.Children.Contains(bookletMesh),"booklet follows central tray when turned");
        scene.SetCaseOpen(false,false);
        scene.SetItem(obiItem,0,0);
        var obiRoot=(HelixToolkit.Wpf.SharpDX.GroupModel3D)typeof(DxJewelCaseScene).GetField("_spineCardRoot",field)!.GetValue(scene)!;
        var obiMeshes=obiRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToArray();
        Check(obiMeshes.Length==6,"24mm obi includes exterior and reverse faces");
        var positions=obiMeshes.SelectMany(m=>((HelixToolkit.SharpDX.MeshGeometry3D)m.Geometry!).Positions!);
        Check(positions.Max(p=>p.Z)-positions.Min(p=>p.Z)>24*DigipakDimensions.Unit,"obi wraps outside 24mm case depth");
        scene.SetSpineCardRemoved(true,false);
        Check((double)typeof(DxJewelCaseScene).GetField("_spineCardProgress",field)!.GetValue(scene)! == 1d,"24mm obi removal");
        scene.SetSpineCardRemoved(false,false);
        scene.SetItem(item,0,0);
        Check(scene.IsMultiCase,"normal SetItem selects multi-case geometry");
        scene.SetCaseOpen(true,false);scene.TurnMultiCase(true,false);
        Check(scene.MultiCaseTurned,"normal viewer turns central tray");
        scene.SetItem(item,0,0);Check(scene.MultiCaseTurned,"artwork rebuild preserves central page");
        scene.BeginInteractiveMotion();scene.EndInteractiveMotion();Check(!scene.Viewport.EnableSSAO,"multi-case rotation retains lighting mode");
        scene.SetCaseOpen(false,false);Check(!scene.MultiCaseTurned,"closing restores central tray");
        scene.SetItem(item with {MultiCase=null},0,0);Check(!scene.IsMultiCase,"standard case still works after multi-case");
        string export=Path.Combine(root,"multi-case.glb");
        bool legacyRejected=false;var legacyPath=Path.Combine(root,"multi-case.vcd3d");
        try{MobileCaseExporter.Export(item,legacyPath);}catch(NotSupportedException){legacyRejected=true;}
        Check(legacyRejected&&!File.Exists(legacyPath),"24mm legacy snapshot cannot silently lose the multi-case model");
        MobileGlbExporter.Export(item,export);
        var glb=File.ReadAllBytes(export);
        Check(glb.Length<=32*1024*1024,"24mm fixture stays within Android's GLB size limit");
        using(var document=JsonDocument.Parse(glb.AsMemory(20,BitConverter.ToInt32(glb,12)))) {
            var gltf=document.RootElement;
            Check(gltf.GetProperty("extras").GetProperty("virtualCd").GetProperty("profile").GetString()=="multi-case-24mm-glb-1"
                &&gltf.GetProperty("nodes")[0].GetProperty("children").GetArrayLength()==8,
                "24mm GLB exports its distinct Android profile and eight moving parts");
            var names=gltf.GetProperty("nodes").EnumerateArray().Select(n=>n.TryGetProperty("name",out var name)?name.GetString():null).ToHashSet();
            Check(Enumerable.Range(1,4).All(n=>names.Contains("Disc"+n)),"24mm GLB retains four independent disc nodes");
            Check(Enumerable.Range(1,4).All(n=>gltf.GetProperty("nodes")[n+3].GetProperty("children").GetArrayLength()>0),
                "24mm GLB contains visible geometry for each assigned disc");
            Check(gltf.GetProperty("images").EnumerateArray().Any(i=>i.GetProperty("name").GetString()=="front"),
                "24mm GLB includes an Android cover thumbnail");
        }
        var obiExport=Path.Combine(root,"multi-case-obi.glb");
        MobileGlbExporter.Export(obiItem,obiExport);
        var obiGlb=File.ReadAllBytes(obiExport);
        using(var document=JsonDocument.Parse(obiGlb.AsMemory(20,BitConverter.ToInt32(obiGlb,12)))) {
            var gltf=document.RootElement;
            Check(gltf.GetProperty("extras").GetProperty("virtualCd").GetProperty("obi").ValueKind==JsonValueKind.Object
                &&gltf.GetProperty("nodes")[8].GetProperty("children").GetArrayLength()>0,
                "24mm GLB includes the optional removable spine card");
        }
        var pairExport=Path.Combine(root,"multi-case-pair.glb");
        MobileGlbExporter.Export(item with {MultiCase=mapped with {Disc3=null,Disc4=null}},pairExport);
        var pairGlb=File.ReadAllBytes(pairExport);
        using(var document=JsonDocument.Parse(pairGlb.AsMemory(20,BitConverter.ToInt32(pairGlb,12)))) {
            var nodes=document.RootElement.GetProperty("nodes");
            Check(nodes[5].GetProperty("children").GetArrayLength()>0
                &&!nodes[6].TryGetProperty("children",out _)
                &&!nodes[7].TryGetProperty("children",out _),
                "24mm two-disc GLB keeps Disc2 but omits unassigned Disc3/4 meshes");
        }
        var flow=new JewelCaseCoverFlow();flow.SetItems(new[]{item},item.Key);
        var flowScene=(DxJewelCaseScene)typeof(JewelCaseCoverFlow).GetField("_dxScene",field)!.GetValue(flow)!;
        try {
            typeof(JewelCaseCoverFlow).GetMethod("SetCaseOpen",field)!.Invoke(flow,[true,false]);
            var button=(Button)typeof(JewelCaseCoverFlow).GetField("_discButton",field)!.GetValue(flow)!;
            Check(button.IsEnabled&&button.Content.ToString()!.Contains("中央をめくる"),"existing viewer exposes central turn button");
            Check(flowScene.SelectMultiDisc(1),"viewer can select a physical disc");
            typeof(JewelCaseCoverFlow).GetMethod("UpdateMultiDiscButton",field)!.Invoke(flow,null);
            var removeButton=(Button)typeof(JewelCaseCoverFlow).GetField("_multiDiscButton",field)!.GetValue(flow)!;
            Check(removeButton.IsEnabled&&removeButton.Content.ToString()!.Contains("Disc1"),"individual extraction control identifies selected disc");
            var toolbar=(WrapPanel)removeButton.Parent;
            Check(toolbar.Children.IndexOf(removeButton)==toolbar.Children.IndexOf(button)+1,
                "individual extraction control sits beside the center-tray button");
            removeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(flowScene.SelectedMultiDiscRemoved,"individual extraction button removes only selected disc");
            int? activatedNumber=null;flow.DiscActivated+=(_,e)=>activatedNumber=e.DiscNumber;
            typeof(JewelCaseCoverFlow).GetMethod("RaiseDiscActivated",field)!.Invoke(flow,null);
            Check(activatedNumber==1,"playback event carries physical disc logical number");
            typeof(JewelCaseCoverFlow).GetMethod("SetDiscRemoved",field)!.Invoke(flow,[true,false]);
            Check(flowScene.MultiCaseTurned&&button.Content.ToString()!.Contains("中央を戻す"),"existing viewer turn action updates label and model");
            typeof(JewelCaseCoverFlow).GetMethod("SetCaseOpen",field)!.Invoke(flow,[false,false]);
            Check(!button.IsEnabled&&!flowScene.MultiCaseTurned,"closing disables turn and restores center");
            var exterior=(System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateMultiCaseExterior",stat)!.Invoke(null,[item,0d,0d,0d,1d,0d,0d])!;
            var model=((System.Windows.Media.Media3D.ModelUIElement3D)exterior.Children[0]).Model;
            Check(Math.Abs(model.Bounds.SizeZ/DigipakDimensions.Unit-24)<.1,"collection exterior retains 24 mm thickness");
            var obiExterior=(System.Windows.Media.Media3D.ContainerUIElement3D)typeof(JewelCaseCoverFlow)
                .GetMethod("CreateMultiCaseExterior",stat)!.Invoke(null,[obiItem,0d,0d,0d,1d,90d,0d])!;
            var obiGroup=(System.Windows.Media.Media3D.Model3DGroup)((System.Windows.Media.Media3D.ModelUIElement3D)obiExterior.Children[0]).Model;
            var obiSide=(System.Windows.Media.Media3D.MeshGeometry3D)((System.Windows.Media.Media3D.GeometryModel3D)obiGroup.Children[^1]).Geometry;
            Check(obiSide.Positions[0].Z>obiSide.Positions[1].Z
                &&obiSide.TextureCoordinates[0].X==1&&obiSide.TextureCoordinates[1].X==0,
                "rack spine-card print follows the DirectX viewer's back-to-front U direction");
            flow.SetItems(new[]{obiItem},obiItem.Key);
            var obiButton=(Button)typeof(JewelCaseCoverFlow).GetField("_spineCardButton",field)!.GetValue(flow)!;
            Check(obiButton.Visibility==Visibility.Visible&&obiButton.IsEnabled,"24mm obi control is visible without wrapping lock");
            typeof(JewelCaseCoverFlow).GetMethod("SetCaseOpen",field)!.Invoke(flow,[true,false]);
            Check((bool)typeof(JewelCaseCoverFlow).GetField("_isSpineCardRemoved",field)!.GetValue(flow)!,"opening 24mm case removes obi");
            typeof(JewelCaseCoverFlow).GetMethod("SetCaseOpen",field)!.Invoke(flow,[false,false]);
            typeof(JewelCaseCoverFlow).GetMethod("ApplySpineCardRemoved",field)!.Invoke(flow,[false,false]);
            Check(!(bool)typeof(JewelCaseCoverFlow).GetField("_isSpineCardRemoved",field)!.GetValue(flow)!,"closed 24mm case accepts obi again");
        } finally {flowScene.Dispose();}
        // Do not show or close the unshown main window: closing runs application
        // persistence/shutdown; this fixture has no application lifetime.
    }
}
