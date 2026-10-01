using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZipMp3Player;

internal static class MultiCaseChecks
{
    internal static void Run(string? output)
    {
        var isolatedData=Path.Combine(Path.GetTempPath(),"vccs-multi-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedData);Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(isolatedData,"data"));
        _=new Application();
        void Check(bool ok,string label) { if(!ok)throw new Exception(label); Console.WriteLine("PASS "+label); }
        Check(MultiCaseDimensions.RailDepth==9.5*2+MultiCaseDimensions.Plate,"20 mm rail/plate stack");
        Check(MultiCaseDimensions.Width==141.5f&&MultiCaseDimensions.Height==124,"measured central envelope");
        var rounded=DxJewelCaseScene.CreateMultiRoundedRail(-70.75f,-62.75f,12,61.5f,1.2f,1.2f,true);
        float unit=DigipakDimensions.Unit;
        Check(Math.Abs(rounded.Positions!.Min(p=>p.X)/unit+70.75)<.001&&Math.Abs(rounded.Positions.Max(p=>p.Z)/unit-12)<.001,
            "rounded tab preserves measured extents");
        Check(!rounded.Positions.Any(p=>Math.Abs(p.X/unit+70.75)<.01&&Math.Abs(Math.Abs(p.Z/unit)-12)<.01),
            "sharp tab corners replaced by circular arcs");
        Check(rounded.Positions.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)),"finite rounded geometry");
        Check(rounded.Positions.Any(p=>Math.Abs(p.Z/unit)<.001&&Math.Abs(p.X/unit+69.55)<.001),
            "central end notch has 1.2 mm recess");
        Check(!rounded.Positions.Any(p=>Math.Abs(p.Z/unit)<2.9&&p.X/unit<-70.74),
            "notch is removed from both faces, not painted on");
        for(int i=0;i<rounded.Indices!.Count;i+=3) {
            var a=rounded.Positions[rounded.Indices[i]];
            var b=rounded.Positions[rounded.Indices[i+1]];
            var c=rounded.Positions[rounded.Indices[i+2]];
            var cross=System.Numerics.Vector3.Cross(b-a,c-a);
            if(cross.LengthSquared()<1e-18f)throw new Exception("degenerate rail triangle");
            if(Math.Abs(a.Y-b.Y)<1e-7&&Math.Abs(a.Y-c.Y)<1e-7&&cross.Y*(a.Y/unit-61.5)<0)
                throw new Exception("inverted notch fan triangle");
        }
        Check(true,"notched rail triangles retain outward winding");
        var plate=DxJewelCaseScene.CreateMultiSmoothPlate();
        double frontArea=0;
        for(int i=0;i<plate.Indices!.Count;i+=3) {
            var a=plate.Positions![plate.Indices[i]]/unit;
            var b=plate.Positions[plate.Indices[i+1]]/unit;
            var c=plate.Positions[plate.Indices[i+2]]/unit;
            CheckFinite(a);CheckFinite(b);CheckFinite(c);
            if(a.Z>.49&&b.Z>.49&&c.Z>.49) {
                var cross=System.Numerics.Vector3.Cross(b-a,c-a);
                if(cross.Z<=0)throw new Exception("plate face winding");
                frontArea+=cross.Z/2;
            }
        }
        void CheckFinite(System.Numerics.Vector3 p) {
            if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))throw new Exception("non-finite plate");
        }
        double distance=13/Math.Sqrt(2),cap=256*Math.Acos(distance/16)-distance*Math.Sqrt(256-distance*distance);
        double expectedArea=138*122-4*(Math.PI*256-cap);
        Check(Math.Abs(frontArea-expectedArea)<2,"smooth plate area matches four analytic cut-outs");
        Check(plate.Positions!.Count<30000,"smooth plate avoids dense occupancy grid");
        var side=DxJewelCaseScene.CreateMultiRecessedSideWall();
        var middle=side.Positions!.Where(p=>Math.Abs(p.Y/unit)<.001).ToArray();
        Check(Math.Abs(middle.Max(p=>p.Z)/unit-1.7f)<.001,"right side central edge has rounded 1.8 mm drop");
        Check(middle.Any(p=>Math.Abs(p.X/unit-70.15f)<.001&&p.Z>0),"right side recess bows inward");
        Check(side.Positions.Where(p=>Math.Abs(p.Y/unit)>10).All(p=>Math.Abs(Math.Abs(p.Z/unit)-3.5f)<.001),
            "side recess preserves ends and rear edge");
        Check(MultiCaseDimensions.Angles(0)==(0d,0d),"closed pose");
        Check(MultiCaseDimensions.Angles(1)==(-180d,0d),"front open pose");
        Check(MultiCaseDimensions.Angles(2)==(0d,-180d),"central flipped pose");
        for(int i=0;i<=100;i++) {
            var (front,center)=MultiCaseDimensions.Angles(1+i/100d);
            if(Math.Abs(front+center+180)>=1e-8)throw new Exception("front orientation at "+i);
        }
        Check(true,"front orientation retained throughout central turn");
        BitmapSource Label(string text,System.Windows.Media.Color color,int width=600,int height=500) {
            var visual=new System.Windows.Media.DrawingVisual();
            using(var dc=visual.RenderOpen()) {
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(color),null,new Rect(0,0,width,height));
                dc.DrawRectangle(System.Windows.Media.Brushes.Red,null,new Rect(0,0,24,height));
                dc.DrawRectangle(System.Windows.Media.Brushes.Blue,null,new Rect(width-24,0,24,height));
                dc.DrawText(new System.Windows.Media.FormattedText("TOP ↑\n"+text,System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,new System.Windows.Media.Typeface("Segoe UI"),42,System.Windows.Media.Brushes.Black,1),new Point(45,40));
            }
            var bitmap=new RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }
        var frontScan=Label("FRONT",System.Windows.Media.Colors.Gold);
        var backScan=Label("BACK",System.Windows.Media.Colors.LightGreen);
        var frontSplit=MultiCaseArtwork.Split(frontScan,2);var backSplit=MultiCaseArtwork.Split(backScan,2);
        Check(frontSplit.Panel!.PixelWidth==552&&frontSplit.Left!.PixelWidth==24&&frontSplit.Right!.PixelWidth==24,"full insert splits 6+138+6 without overlap");
        Check(ReferenceEquals(MultiCaseArtwork.Split(frontScan,1).Panel,frontScan),"panel-only preserves original bitmap");
        Check(MultiCaseArtwork.Rotate(frontScan,90)!.PixelWidth==500&&frontScan.PixelWidth==600,"preview rotation does not mutate source");
        var mapped=new MultiCaseArtwork {Front=frontSplit.Panel,Back=backSplit.Panel,FrontLeft=frontSplit.Left,FrontRight=frontSplit.Right,
            BackLeft=backSplit.Left,BackRight=backSplit.Right,Disc1=Label("DISC 1",System.Windows.Media.Colors.LightBlue),
            Disc2=Label("DISC 2",System.Windows.Media.Colors.LightGreen),Disc3=Label("DISC 3",System.Windows.Media.Colors.Orange),Disc4=Label("DISC 4",System.Windows.Media.Colors.Plum)};
        MultiCaseIntegrationChecks.Run(isolatedData,mapped,frontScan,backScan);
        using var scene=new DxJewelCaseScene(); scene.BuildMultiCasePrototype(new MultiCaseArtwork {
            Disc1=mapped.Disc1,Disc2=mapped.Disc2,Disc3=mapped.Disc3,Disc4=mapped.Disc4});
        var field=typeof(DxJewelCaseScene).GetField("_multiDiscs",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Check(((System.Collections.ICollection)field.GetValue(scene)!).Count==4,"four distinct disc roots");
        var halves=(IEnumerable<HelixToolkit.Wpf.SharpDX.GroupModel3D>)typeof(DxJewelCaseScene)
            .GetField("_multiOuterHalves",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(scene)!;
        Check(halves.Count()==2,"two reused outer tray assemblies");
        foreach(var half in halves) {
            var axis=half.Transform.Value.Transform(new System.Windows.Media.Media3D.Vector3D(0,0,1));
            Check(Math.Abs(axis.Length-1)<1e-8,"outer tray relief retains native depth");
            var names=half.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().Select(m=>m.Material?.Name).ToList();
            Check(names.Contains("Tray spine ribs")&&names.Contains("Tray spine groove floor")&&names.Contains("Tray spine transition"),
                "standard vertical mouldings and raised shoulder reused");
        }
        if(output is null)return;
        Directory.CreateDirectory(output);
        int frames=0; scene.Viewport.OnRendered+=(_,_)=>frames++;
        var window=new Window { Content=scene.Viewport,Width=1050,Height=700,Left=-10000,Top=-10000,
            ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.ToolWindow };
        try {
            window.Show();
            foreach(var (name,progress,discs) in new[]{("closed",0d,true),("front-open",1d,true),("turning",1.5d,false),("back-open",2d,true),("bare",1d,false),("side-left",0d,true),("side-right",0d,true),("rounded-edge",0d,false),("side-recess",1d,false),
                ("mapped-front",0d,true),("mapped-back",0d,true),("mapped-open",1d,true),("mapped-flipped",2d,true),("mapped-spines",0d,true),
                ("obi-closed",0d,true),("obi-open",1d,true),("two-disc-booklet",1d,true),("two-disc-booklet-turned",2d,true)}) {
                if(name.StartsWith("mapped-"))scene.BuildMultiCasePrototype(mapped);
                if(name.StartsWith("two-disc-booklet")) {
                    scene.BuildMultiCasePrototype(mapped with {Disc3=null,Disc4=null,BookletFront=Label("BOOKLET",System.Windows.Media.Colors.LightPink)});
                    scene.SetViewZoom(1);
                }
                if(name.StartsWith("obi-")) {
                    SpineCardArtwork.SetManualFolds(frontScan,.30,.60);
                    scene.SetItem(new JewelCaseCoverFlowItem("obi","24mm obi","","","White",null,null,null,null,null,null,null,false) {
                        MultiCase=mapped,SpineCard=frontScan,SpineCardReverse=backScan},-22,-18);
                    scene.SetCaseOpen(progress>0,false);
                    scene.SetSpineCardRemoved(progress>0,false);
                    scene.SetViewZoom(1);
                }
                scene.SetMultiCaseProgress(progress);scene.SetMultiCaseDiscsVisible(discs);
                scene.SetRotation(name=="side-left"?-90:name=="side-right"?90:-22,name.StartsWith("side-")?0:-18);
                if(name=="rounded-edge"){scene.SetRotation(0,-80);scene.SetViewZoom(.9);}
                if(name=="side-recess"){scene.SetMultiCaseProgress(0);scene.SetRotation(-75,-12);scene.SetViewZoom(.7);}
                if(name.StartsWith("mapped-"))scene.SetViewZoom(1);
                if(name=="mapped-back")scene.SetRotation(158,-18);
                if(name=="mapped-spines"){scene.SetRotation(-80,-12);scene.SetViewZoom(.7);}
                int initial=frames;var clock=System.Diagnostics.Stopwatch.StartNew();
                var frame=new DispatcherFrame();var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(40) };
                timer.Tick+=(_,_)=> {scene.Viewport.InvalidateRender();if(frames>=initial+3||scene.Viewport.RenderException is not null||clock.Elapsed.TotalSeconds>12)frame.Continue=false;};
                timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
                if(scene.Viewport.RenderException is {} ex)throw new Exception("Render failed: "+name,ex);
                Check(frames>=initial+3,"GPU frames: "+name);
                var bitmap=HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(scene.Viewport);
                if(name=="side-left"||name=="side-right") {
                    var converted=new FormatConvertedBitmap(bitmap,System.Windows.Media.PixelFormats.Bgra32,null,0);
                    var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);
                    int colored=0,paper=0;
                    for(int i=0;i<pixels.Length;i+=4) {
                        int b=pixels[i],g=pixels[i+1],r=pixels[i+2];
                        if((b>70&&b>r*1.6&&b>g*1.3)||(g>65&&g>r*1.7&&g>b*1.5)||(r>100&&r>g*1.8&&r>b*2))colored++;
                        if(r>100&&g>100&&b>65&&Math.Abs(r-g)<45)paper++;
                    }
                    Check(colored<20,"side walls occlude coloured discs: "+name+" ("+colored+")");
                    Check(paper>500,"two placeholder paper folds visible: "+name);
                }
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
            }
            MultiDiscChecks.RunGpu(scene,mapped);
            var chooser=new MultiCasePrototypeWindow(new[]{
                new MultiCaseImageChoice("front-test.png","Front",()=>frontScan),
                new MultiCaseImageChoice("disc4-test.png","Disc4",()=>backScan)},"Mapping test") {
                Owner=window,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false};
            try {
                chooser.Show();chooser.UpdateLayout();
                IEnumerable<DependencyObject> Descendants(DependencyObject root) {
                    yield return root;
                    for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
                        foreach(var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;
                }
                var controls=Descendants(chooser).ToArray();
                var combos=controls.OfType<System.Windows.Controls.ComboBox>().ToArray();
                Check(combos.Length==12,"ten image selectors and two split modes visible");
                Check(combos[0].SelectedIndex==1&&combos[^1].SelectedIndex==2,"existing roles initialize front and Disc4 slots");
                var apply=controls.OfType<System.Windows.Controls.Button>().Single(b=>Equals(b.Content,"画像を適用"));
                apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(controls.OfType<System.Windows.Controls.TextBlock>().Any(t=>t.Text.StartsWith("2種類の元画像を適用")),"repeated preview apply succeeds");
                var previewScene=(DxJewelCaseScene)typeof(MultiCasePrototypeWindow).GetField("scene",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chooser)!;
                var cache=(System.Collections.IDictionary)typeof(DxJewelCaseScene).GetField("_textureCache",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(previewScene)!;
                Check(cache.Count<=10,"repeated image changes do not accumulate texture cache");
            } finally {chooser.Close();}
        } finally {window.Close();}
    }
}
