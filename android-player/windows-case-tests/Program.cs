using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Length==1&&args[0]=="--booklet-opening"){BookletOpeningChecks.Run();return;}
        if(args.Length==1&&args[0]=="--artwork-cache"){ArtworkCacheChecks.Run();return;}
        if(args.Length==1&&args[0]=="--booklet-slideshow"){BookletSlideshowChecks.Run();return;}
        if(args.Length==3&&args[0]=="--desktop-sample"){
            var app=new Application();using var sampleZip=ZipFile.OpenRead(args[1]);
            using var metadata=JsonDocument.Parse(sampleZip.GetEntry("manifest.json")!.Open());var m=metadata.RootElement;
            if(m.TryGetProperty("obi",out var band)&&band.ValueKind==JsonValueKind.Object)throw new Exception("This sample helper requires an album without obi");
            BitmapSource? Image(string role){var entry=sampleZip.GetEntry(role+".png");if(entry is null)return null;using var stream=entry.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);bytes.Position=0;var image=BitmapDecoder.Create(bytes,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];image.Freeze();return image;}
            var sampleItem=new JewelCaseCoverFlowItem("sample",m.GetProperty("title").GetString()!,m.GetProperty("artist").GetString()!,"DIR",m.GetProperty("tray").GetString()!,Image("front"),Image("insideFront"),Image("back"),Image("spine"),Image("rightSpine"),null,Image("disc"),false);
            MobileGlbExporter.Export(sampleItem,args[2]);Console.WriteLine("PASS desktop sample: "+args[2]);return;
        }
        if(args.Length==1&&args[0]=="--mobile-model"){MobileModelChecks.Run();return;}
        if(args.Length==1&&args[0]=="--front-panel-bounds"){
            var app=new Application();
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            using var scene=(IDisposable)Activator.CreateInstance(type)!;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            type.GetMethod("AddClearFrontPanel",flags)!.Invoke(scene,[2.42f,2.12f,.177f,new HelixToolkit.Wpf.SharpDX.PhongMaterial{Name="test"}]);
            var panelRoot=(HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_frontPanelRoot",flags)!.GetValue(scene)!;
            var mesh=(HelixToolkit.SharpDX.MeshGeometry3D)panelRoot.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().Single().Geometry!;
            var pts=mesh.Positions!;
            var left=pts.Min(p=>p.X);var right=pts.Max(p=>p.X);
            var shell=type.GetMethod("GetCoverFlowShellGeometry",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            var artworkLeft=(float)shell.GetType().GetProperty("FrontLeft")!.GetValue(shell)!;
            var stopInnerEdge=artworkLeft-(.90f-.55f/2)*2.42f/142f;
            if(Math.Abs(left-stopInnerEdge)>.000001f)throw new Exception("Panel does not end at artwork-facing stop edge");
            if(left< -1.12660f-.000001f||Math.Abs(right-(1.21f-.65f*2.42f/142f))>.000001f)throw new Exception("Lid panel crosses hinge or changed right boundary");
            if(Math.Abs(pts.Max(p=>p.Z)-pts.Min(p=>p.Z)-.85f*.177f/10f)>.000001f)throw new Exception("Panel thickness changed");
            // At 180 degrees the lid's entire sheet must lie on its own side.
            if(pts.Any(p=>2*(-1.12660f)-p.X> -1.12660f+.000001f))throw new Exception("Opened panel overlaps Back across hinge");
            Console.WriteLine("PASS clear panel: no hinge crossing closed/open; right edge and thickness preserved");return;
        }
        if(args.Length==1&&args[0]=="--back-diagnostic"){
            var app=new Application();
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            using var scene=(IDisposable)Activator.CreateInstance(type)!;
            var diagnosticRoot=(HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_baseRoot",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
            var frontDiagnosticRoot=(HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField("_frontPanelRoot",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
            var names=new[]{"Dense clear back artwork frame","Clear opening-side Spine frame","Tray","Spine artwork","Clear front panel","Front lid moulded rails","Booklet retaining clips"};
            var meshes=names.Select(n=>new HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D{Material=new HelixToolkit.Wpf.SharpDX.PhongMaterial{Name=n}}).ToArray();
            for(int i=0;i<meshes.Length;i++)(i<4?diagnosticRoot:frontDiagnosticRoot).Children.Add(meshes[i]);
            foreach(var (key,index) in new[]{("frame",0),("shell",1),("tray",2),("front-panel",4),("front-rails",5),("front-clips",6),("none",-1),("frame",0),("none",-1)}){
                type.GetMethod("SetBackDiagnosticLayer")!.Invoke(scene,[key]);
                for(int i=0;i<meshes.Length;i++)if((meshes[i].Visibility==Visibility.Hidden)!=(i==index))throw new Exception("Layer isolation/restore failed");
            }
            Console.WriteLine("PASS diagnostic single-layer isolation, switching/restoration, artwork unchanged");return;
        }
        if(args.Length==1&&args[0]=="--tape-residue"){
            var app=new Application();
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            using var scene=(IDisposable)Activator.CreateInstance(type)!;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var update=type.GetMethod("UpdateTearTapeGeometry",flags)!;
            foreach(double progress in new[]{0,.2,.48,.7,1,1,.7,.2,0,1,0}){
                update.Invoke(scene,[progress]);
                foreach(var (field,visible) in new[]{("_tearTapeFrontRoot",progress<.48),("_tearTapeBackRoot",progress<1),("_tearTapeSideRoot",progress<.42)}){
                    var group=(HelixToolkit.Wpf.SharpDX.GroupModel3D)type.GetField(field,flags)!.GetValue(scene)!;
                    if((group.Visibility==Visibility.Visible)!=visible)throw new Exception($"Tape visibility mismatch: {field} at {progress}");
                }
            }
            Console.WriteLine("PASS tape: strips hidden after removal, partial pull preserved, repeated rewrap restores visibility");return;
        }
        if(args.Length==1&&args[0]=="--shell-components"){
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            var shell=type.GetMethod("GetCoverFlowShellGeometry",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            foreach(var name in new[]{"BottomTray","BottomPerimeter","BottomMouldedEdges","TopLid"}){
                var mesh=(System.Windows.Media.Media3D.MeshGeometry3D)shell.GetType().GetProperty(name)!.GetValue(shell)!;
                var parents=Enumerable.Range(0,mesh.Positions.Count).ToArray();
                int Root(int n){while(parents[n]!=n){parents[n]=parents[parents[n]];n=parents[n];}return n;}
                void Join(int a,int b){parents[Root(a)]=Root(b);}
                var seen=new Dictionary<(long,long,long),int>();
                for(int i=0;i<parents.Length;i++){var p=mesh.Positions[i];var key=((long)Math.Round(p.X*100000),(long)Math.Round(p.Y*100000),(long)Math.Round(p.Z*100000));if(seen.TryGetValue(key,out var old))Join(i,old);else seen[key]=i;}
                for(int i=0;i<mesh.TriangleIndices.Count;i+=3){Join(mesh.TriangleIndices[i],mesh.TriangleIndices[i+1]);Join(mesh.TriangleIndices[i],mesh.TriangleIndices[i+2]);}
                var groups=Enumerable.Range(0,parents.Length).GroupBy(Root).ToArray();
                Console.WriteLine(name+" components="+groups.Length);
                foreach(var g in groups.OrderBy(g=>g.Count()).Take(15)){
                    var pts=g.Select(i=>mesh.Positions[i]).ToArray();
                    Console.WriteLine($"n={pts.Length} X {pts.Min(p=>p.X):F5}..{pts.Max(p=>p.X):F5} Y {pts.Min(p=>p.Y):F5}..{pts.Max(p=>p.Y):F5} Z {pts.Min(p=>p.Z):F5}..{pts.Max(p=>p.Z):F5}");
                }
            }return;
        }
        if(args.Length==1&&args[0]=="--spine-inside"){
            var app=new Application();
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            using var scene=(IDisposable)Activator.CreateInstance(type)!;
            foreach(var left in new[]{true,false}){
                var group=new HelixToolkit.Wpf.SharpDX.GroupModel3D();
                type.GetMethod("AddSpine",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(scene,
                    [null,left?-1.21f:1.21f,2f,.177f,left,group,null,(float?)-.05895f,(float?)-.05975f]);
                var meshes=group.Children.OfType<HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D>().ToArray();
                var outer=(HelixToolkit.SharpDX.MeshGeometry3D)meshes.Single(m=>m.Material.Name=="Spine artwork").Geometry;
                var inner=(HelixToolkit.SharpDX.MeshGeometry3D)meshes.Single(m=>m.Material.Name=="Spine paper reverse").Geometry;
                if(outer.Positions.Any(p=>Math.Abs(p.X)>=1.21f))throw new Exception("Spine still outside shell");
                float expected=(left?-1:1)*(1.21f*138f/142f-.0008f);
                if(inner.Positions.Any(p=>Math.Abs(p.X-expected)>.000001f))throw new Exception("Inlay seam moved");
                if(Math.Abs(Math.Abs(outer.Positions[0].X-inner.Positions[0].X)-.0008f)>.000001f)throw new Exception("Paper faces overlap");
                if(Math.Abs(inner.Positions.Min(p=>p.Z)+.05895f)>.000001f)throw new Exception("Inner tray depth changed");
                var foldX=(float)type.GetMethod("OuterSpinePrintX",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[left?-1.21f:1.21f,left])!;
                if(outer.Positions.Any(p=>Math.Abs(p.X-foldX)>.000001f)
                    ||Math.Abs(outer.Positions.Min(p=>p.Z)+.05975f)>.000001f
                    ||Math.Abs(outer.Positions.Max(p=>p.Z)-outer.Positions.Min(p=>p.Z)-.1062f)>.000001f
                    ||1.21f-Math.Abs(foldX)<.03f)
                    throw new Exception("Outer Back/Spine fold or front boundary mismatch");
            }
            Console.WriteLine("PASS 138mm Back, 6mm unstretched Spine, guard rail clearance, tray depth and paper thickness");return;
        }
        if(args.Length==1&&args[0]=="--rear-skin"){
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
            var shell=type.GetMethod("GetCoverFlowShellGeometry",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            int Count(string name){
                var mesh=(System.Windows.Media.Media3D.MeshGeometry3D)shell.GetType().GetProperty(name)!.GetValue(shell)!;
                int count=0;
                for(int i=0;i<mesh.TriangleIndices.Count;i+=3){
                    var points=Enumerable.Range(0,3).Select(j=>mesh.Positions[mesh.TriangleIndices[i+j]]).ToArray();
                    if(points.All(p=>p.Z<=-.177/2+.0001))count++;
                }
                return count;
            }
            if(Count("BottomTray")!=0||Count("BottomPerimeter")==0)throw new Exception("Rear skin must be clear, not tray");
            Console.WriteLine("PASS rear skin: rear-most triangles are clear; opaque tray does not cover outer rear plane");return;
        }
        if(args.Length==1&&args[0]=="--recent-order"){
            var dir=Path.Combine(Path.GetTempPath(),"album-order-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.AlbumAddedStore")!;
            object Open()=>Activator.CreateInstance(type,[Path.Combine(dir,"dates.json")])!;
            void Add(object store,string album,long date)=>type.GetMethod("Observe")!.Invoke(store,[Path.Combine(dir,album),date]);
            long Get(object store,string album)=>(long)type.GetMethod("Get")!.Invoke(store,[Path.Combine(dir,album)])!;
            var store=Open();Add(store,"old",0);Add(store,"new",100);Add(store,"new",200);
            type.GetMethod("Save")!.Invoke(store,null);store=Open();
            if(Get(store,"old")!=0||Get(store,"NEW")!=100)throw new Exception("First-seen persistence failed");
            Add(store,"latest",300);
            if(Get(store,"latest")<=Get(store,"new"))throw new Exception("Date order failed");
            Console.WriteLine("PASS recent order: baseline, persistence, repeat scan, case-insensitive identity");return;
        }
        if(args.Length==1&&args[0]=="--spine-return"){SpineReturnChecks.Run();return;}
        if(args.Length==1&&args[0]=="--favorites"){FavoritesChecks.Run();return;}
        if(args.Length==1&&args[0]=="--crop"){CropChecks.Run();return;}
        if(args.Length==1&&args[0]=="--rotation"){RotationChecks.Run();return;}
        if(args.Length==1&&args[0]=="--youtube-search"){
            var build=typeof(MainWindow).GetMethod("BuildTrackYouTubeUrl",BindingFlags.Static|BindingFlags.NonPublic)!;
            void Check(ZipTrack track,string? query){
                var actual=(string?)build.Invoke(null,[track]);
                var expected=query is null?null:"https://www.youtube.com/results?search_query="+Uri.EscapeDataString(query);
                if(actual!=expected)throw new Exception($"YouTube URL mismatch: {actual}");
            }
            Check(new ZipTrack{Artist="森口博子",Title="JUST COMMUNICATION"},"森口博子 JUST COMMUNICATION");
            Check(new ZipTrack{Artist=" A&B ",Title=" Song #1 ? + / "},"A&B Song #1 ? + /");
            Check(new ZipTrack{Artist="アーティスト不明",Title="曲"},"曲");
            Check(new ZipTrack{Artist="Unknown Artist",FileName="folder/song.flac"},"song");
            Check(new ZipTrack{Title="Netherstorm",Artist="Frozen Crown",CueStartFrame=12000},"Frozen Crown Netherstorm");
            Check(new ZipTrack(),null);
            Console.WriteLine("PASS YouTube search: Japanese, escaping, unknown artist, filename fallback, CUE, empty track");return;
        }
        if(args.Length==1&&args[0]=="--lyrics"){LyricsChecks.Run();return;}
        if(args.Length==2&&args[0]=="--cue-identity"){
            var directory=Path.GetFullPath(args[1]);var cuePath=Directory.GetFiles(directory,"*.cue").Single();
            var cueAlbum=ZipAlbumReader.Open(cuePath);var folderAlbum=ZipAlbumReader.OpenFolder(directory);
            var identity=typeof(MainWindow).Assembly.GetType("ZipMp3Player.CueAlbumIdentity")!.GetMethod("IsCoveredBy",BindingFlags.Static|BindingFlags.NonPublic)!;
            bool Covered(ZipAlbum a,ZipAlbum b)=>(bool)identity.Invoke(null,[a,b])!;
            if(!Covered(cueAlbum,folderAlbum)||Covered(folderAlbum,cueAlbum))throw new Exception("CUE/folder canonical direction");
            if(Covered(cueAlbum,new ZipAlbum{Path=directory,Tracks=folderAlbum.Tracks.Skip(1).ToArray()}))throw new Exception("Partial coverage suppressed");
            if(Covered(cueAlbum,new ZipAlbum{Path=directory,Tracks=[new ZipTrack{SourcePath=cueAlbum.Tracks[0].SourcePath}]}))throw new Exception("Unsplit FLAC suppressed cue");
            if(Covered(cueAlbum,new ZipAlbum{Path=directory+"-other",Tracks=folderAlbum.Tracks}))throw new Exception("Different album suppressed");
            Console.WriteLine("PASS CUE identity: "+cueAlbum.Tracks.Count+" segments covered by image-bearing folder; partial/raw/different album preserved");return;
        }
        if(args.Length==1&&args[0]=="--booklet-button"){
            var app=new Application();var flow=new JewelCaseCoverFlow();var type=typeof(JewelCaseCoverFlow);
            void Set(string name,object value)=>type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(flow,value);
            bool loaded=false;
            var bookletItem=new JewelCaseCoverFlowItem("test","test","","DIR","White",null,null,null,null,null,null,null,false){LoadBooklet=()=>{loaded=true;throw new Exception("Loader should not run");}};
            Set("_items",new[]{bookletItem});Set("_selectedIndex",0);
            var button=(System.Windows.Controls.Button)type.GetField("_bookletButton",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(flow)!;
            foreach(bool unwrapped in new[]{false,true})foreach(bool busy in new[]{false,true})foreach(bool reading in new[]{false,true}){
                Set("_isWrappingOpened",unwrapped);Set("_isCaseTransitioning",busy);Set("_isOpeningBooklet",reading);
                type.GetMethod("UpdateBookletButton",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(flow,null);
                if(button.IsEnabled!=(unwrapped&&!busy&&!reading))throw new Exception("Booklet enable mismatch");
                if(!unwrapped&&!button.ToolTip.ToString()!.Contains("テープ"))throw new Exception("Missing wrapping guidance");
                if(!button.IsEnabled)((Task)type.GetMethod("OpenBookletAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(flow,null)!).GetAwaiter().GetResult();
            }
            if(loaded)throw new Exception("Blocked request loaded booklet");
            Set("_isWrappingOpened",true);Set("_isCaseTransitioning",false);Set("_isOpeningBooklet",false);
            Set("_items",new[]{bookletItem with {LoadBooklet=null}});
            type.GetMethod("UpdateBookletButton",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(flow,null);
            if(button.IsEnabled)throw new Exception("Missing booklet enabled");
            Console.WriteLine("PASS booklet button: wrapped/unwrapped, animation, reader, no booklet, guarded click");return;
        }
        if(args.Length==1&&args[0]=="--spine-reverse"){SpineReverseChecks.Run();return;}
        if(args.Length==2&&args[0]=="--sync-table"){SyncStyleChecks.RunTable(args[1]);return;}
        if(args.Length==1&&args[0]=="--qr-status-layout"){
            var app=new Application();
            var status=new System.Windows.Controls.TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=14};
            var box=(System.Windows.Controls.Border)typeof(MobileSyncQrWindow).GetMethod("CreateStatusPanel",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[status])!;
            var panel=new System.Windows.Controls.DockPanel();System.Windows.Controls.DockPanel.SetDock(box,System.Windows.Controls.Dock.Bottom);panel.Children.Add(box);
            var qr=new System.Windows.Controls.Border();panel.Children.Add(qr);
            foreach(double width in new[]{340d,420d})foreach(var message in new[]{"接続待ち","Xperiaからの受信状況\nPCから転送中 · 40% · 128.8 MiB / 316.6 MiB\n受信 128.8 MiB · 再利用 0.0 MiB · 4 / 12ファイル",string.Join("\n",Enumerable.Repeat(new string('長',120),20)),"同期完了"}){
                status.Text=message;panel.Measure(new Size(width,440));panel.Arrange(new Rect(0,0,width,440));panel.UpdateLayout();
                if(Math.Abs(box.ActualHeight-112)>.1||Math.Abs(qr.ActualHeight-328)>.1||Math.Abs(qr.TranslatePoint(new Point(),panel).Y)>.1)throw new Exception("QR moved on status update");
            }
            Console.WriteLine("PASS QR status layout: fixed panel/QR geometry at two widths, waiting/progress/long error/completion");return;
        }
        if(args.Length==1&&args[0]=="--disc-pull"){
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DiscPullGesture")!;var g=Activator.CreateInstance(type)!;
            bool Begin(double a,double b)=>(bool)type.GetMethod("Begin")!.Invoke(g,[7,new Point(100,100),a,9,new Point(200,100),b])!;
            bool Move(Point a,Point b)=>(bool)type.GetMethod("Move")!.Invoke(g,[7,a,9,b])!;
            if(!Begin(0,1)||Move(new Point(102,100),new Point(210,100))||!Move(new Point(102,100),new Point(232,100))||Move(new Point(102,100),new Point(250,100)))throw new Exception("Grab jitter/one-shot");
            if(!Begin(1,0)||!Move(new Point(132,100),new Point(200,100)))throw new Exception("Reverse order");
            Begin(0,1);if(Move(new Point(125,100),new Point(240,100))||Move(new Point(100,100),new Point(240,100)))throw new Exception("Hub drift cancellation");
            if(Begin(.5,1)||Begin(double.NaN,1))throw new Exception("Ordinary pinch captured");
            Begin(0,1);type.GetMethod("Cancel")!.Invoke(g,null);if(Move(new Point(100,100),new Point(240,100)))throw new Exception("Cancel");
            Console.WriteLine("PASS disc pull: hub/edge, either order, jitter, one-shot, cancellation, pinch exclusion");return;
        }
        if(args.Length==2&&args[0]=="--gapless-flac"){
            var album=ZipAlbumReader.OpenFolder(args[1]);var assembly=typeof(MainWindow).Assembly;var entryType=assembly.GetType("ZipMp3Player.PlaybackQueueEntry")!;var gaplessType=assembly.GetType("ZipMp3Player.GaplessPlaybackStream")!;
            foreach(bool faithful in new[]{false,true}){
                var entries=Array.CreateInstance(entryType,album.Tracks.Count);for(int i=0;i<album.Tracks.Count;i++)entries.SetValue(Activator.CreateInstance(entryType,album,i,-1),i);
                using var stream=(NAudio.Wave.WaveStream)Activator.CreateInstance(gaplessType,entries,0,faithful,false,0)!;
                Task.Run(()=>{var data=new byte[32768];long total=0;for(int i=0;i<12000;i++){int n=stream.Read(data,0,data.Length);if(n==0)break;total+=n;}Console.WriteLine($"PASS FLAC cross-thread read faithful={faithful}, bytes={total}");}).GetAwaiter().GetResult();
                stream.CurrentTime=TimeSpan.FromSeconds(15);Task.Run(()=>{var data=new byte[32768];if(stream.Read(data,0,data.Length)==0)throw new Exception("Cross-thread seek failed");}).GetAwaiter().GetResult();
            }return;
        }
        if(args.Length==2&&args[0]=="--cue-flac"){
            var album=ZipAlbumReader.OpenFolder(args[1]);if(album.Tracks.Count!=11||!album.Tracks.Any(t=>t.Title=="To Infinity")||album.Tracks[0].Artist!="Frozen Crown")throw new Exception("CUE metadata: "+string.Join(" | ",album.Tracks.Select(t=>$"{t.TrackNumber}: {t.Title} / {t.Artist}")));
            var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.TrackAudioReader")!;
            using var baseline=new NAudio.Wave.MediaFoundationReader(album.Tracks[0].SourcePath);
            var scratch=new byte[262144];
            foreach(var track in album.Tracks){var stopwatch=System.Diagnostics.Stopwatch.StartNew();using var owner=(IDisposable)type.GetMethod("Open",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[track])!;Console.WriteLine($"Track {track.TrackNumber} open/seek: {stopwatch.ElapsedMilliseconds} ms");
                var reader=(NAudio.Wave.WaveStream)type.GetProperty("Reader")!.GetValue(owner)!;
                if(Math.Abs(reader.TotalTime.TotalSeconds-track.Duration.TotalSeconds)>0.05)throw new Exception("Clip duration");
                long target=track.CueStartFrame*baseline.WaveFormat.SampleRate/75*baseline.WaveFormat.BlockAlign;
                while(baseline.Position<target){int got=baseline.Read(scratch,0,(int)Math.Min(scratch.Length,target-baseline.Position));if(got==0)throw new Exception("Baseline EOF");}
                byte[] expected=new byte[4096];baseline.ReadExactly(expected);
                byte[] data=new byte[4096];reader.ReadExactly(data);if(!data.SequenceEqual(expected))throw new Exception($"Track {track.TrackNumber} PCM start mismatch at frame {track.CueStartFrame}");
                reader.CurrentTime=TimeSpan.FromSeconds(Math.Max(0,reader.TotalTime.TotalSeconds-1));if(reader.Read(data,0,data.Length)==0)throw new Exception("Seek end");reader.Position=reader.Length;if(reader.Read(data,0,data.Length)!=0)throw new Exception("Clip end");
                reader.Position=0;reader.ReadExactly(data);if(!data.SequenceEqual(expected))throw new Exception("Backward seek PCM mismatch");
                var assembly=typeof(MainWindow).Assembly;var entryType=assembly.GetType("ZipMp3Player.PlaybackQueueEntry")!;var gaplessType=assembly.GetType("ZipMp3Player.GaplessPlaybackStream")!;
                var entries=Array.CreateInstance(entryType,1);entries.SetValue(Activator.CreateInstance(entryType,album,Array.IndexOf(album.Tracks.ToArray(),track),-1),0);
                using var gapless=(NAudio.Wave.WaveStream)Activator.CreateInstance(gaplessType,entries,0,true,false,0)!;
                Task.Run(()=>gapless.ReadExactly(data)).GetAwaiter().GetResult();if(!data.SequenceEqual(expected))throw new Exception("Gapless selected track PCM mismatch");
            }Console.WriteLine("PASS Frozen Crown: 11 CUE tracks, metadata, FLAC decode, seek and bounded EOF for every track");return;
        }
        if(args.Length==2&&args[0]=="--qr-test"){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);File.WriteAllBytes(args[1],MobileSyncQrWindow.Encode("http://192.168.11.63:50703/0123456789abcdef0123456789abcdef0123456789abcdef/"));Console.WriteLine("PASS Windows QR fixture: "+args[1]);return;}
        if(args.Length==2&&args[0]=="--sync-style"){SyncStyleChecks.Run(args[1]);return;}
        if(args.Length>=3&&args[0]=="--sync-test"){SyncChecks.Run(args[1],args[2],args.Contains("--serve"));return;}
        if(args.Length==3&&args[0]=="--convert-glb"){MobileGlbExporter.ConvertSnapshot(args[1],args[2]);Console.WriteLine("PASS GLB conversion: "+args[2]);return;}
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        JewelCaseCoverFlowItem item;
        if (args.Length > 1)
        {
            // Exercise exactly the desktop's prepared artwork path, without a window or settings writes.
            var album = Directory.Exists(args[1]) ? ZipAlbumReader.OpenFolder(args[1]) : ZipAlbumReader.Open(args[1]);
            var type = typeof(MainWindow).GetNestedType("AlbumListItem", BindingFlags.NonPublic)!;
            var model = Activator.CreateInstance(type, album)!;
            type.GetMethod("EnsureCaseArtworkLoaded")!.Invoke(model, [2048]);
            object? Get(string name) => type.GetProperty(name)!.GetValue(model);
            item = new(album.Path, (string)Get("Title")!, (string)Get("Artist")!, "", (string)Get("TrayColorMode")!,
                (BitmapSource?)Get("CaseFrontThumbnail"), (BitmapSource?)Get("InsideFrontThumbnail"), (BitmapSource?)Get("BackCoverThumbnail"),
                (BitmapSource?)Get("SpineThumbnail"), (BitmapSource?)Get("RightSpineThumbnail"), (BitmapSource?)Get("InlayThumbnail"), (BitmapSource?)Get("DiscThumbnail"), false) { SpineCard = (BitmapSource?)Get("SpineCardThumbnail") };
        }
        else
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DarkSlateBlue, null, new Rect(0, 0, 1800, 1800));
                dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 900, 900));
                dc.DrawRectangle(Brushes.Turquoise, null, new Rect(900, 0, 900, 900));
                dc.DrawText(new FormattedText("FRONT\n3D TEST", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 210, Brushes.White, 1), new Point(180, 1100));
            }
            var image = new RenderTargetBitmap(1800,1800,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();
            var obiVisual = new DrawingVisual();
            using(var dc=obiVisual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DarkBlue,null,new Rect(0,0,800,1200));
                dc.DrawRectangle(Brushes.Crimson,null,new Rect(300,0,100,1200));
                dc.DrawRectangle(Brushes.Goldenrod,null,new Rect(400,0,400,1200));
                dc.DrawText(new FormattedText("BACK     S   FRONT",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),45,Brushes.White,1),new Point(20,130));
            }
            var obiImage=new RenderTargetBitmap(800,1200,96,96,PixelFormats.Pbgra32);obiImage.Render(obiVisual);obiImage.Freeze();
            var folds=typeof(MainWindow).Assembly.GetType("ZipMp3Player.SpineCardArtwork")!;
            folds.GetMethod("SetManualFolds")!.Invoke(null,[obiImage,300d/800,400d/800]);
            item = new("private-windows-path", "3D TEST", "Orientation / hinge", "DIR", "White", image, image, image, null, null, null, image, false) { SpineCard = obiImage };
        }
        MobileCaseExporter.Export(item, output);
        MobileCaseExporter.Export(item, Path.ChangeExtension(output,".glb"));
        if(args.Length==1)MobileCaseExporter.Export(item with {FrontCover=null,InsideFrontCover=null,BackCover=null,SpineCover=null,RightSpineCover=null,InlayCover=null,DiscImage=null,SpineCard=null},Path.ChangeExtension(output,".empty.glb"));
        Console.WriteLine("GLB export: " + Path.ChangeExtension(output,".glb"));
        using var zip = ZipFile.OpenRead(output);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        var root = manifest.RootElement;
        if(root.GetProperty("version").GetInt32()!=2 || root.GetProperty("model").GetString()!="jewel-case-v2")throw new Exception("Schema");
        if(args.Length==1 && (Math.Abs(root.GetProperty("obi").GetProperty("frontWidthMm").GetDouble()-40)>0.1 || Math.Abs(root.GetProperty("obi").GetProperty("backWidthMm").GetDouble()-30)>0.1))throw new Exception("Manual folds / physical scale lost");
        foreach(var property in root.GetProperty("textures").EnumerateObject())
        {
            using var stream=zip.GetEntry(property.Value.GetProperty("file").GetString()!)!.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);
            if(Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant()!=property.Value.GetProperty("sha256").GetString())throw new Exception("Hash");
            bytes.Position=0;var image=BitmapFrame.Create(bytes,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            if(image.PixelWidth>1024 || image.PixelHeight>1024)throw new Exception("Size");
            Console.WriteLine($"PASS {property.Name}: {image.PixelWidth}x{image.PixelHeight}");
        }
        if(root.ToString().Contains(item.Key))throw new Exception("Windows path leaked");
        Console.WriteLine("PASS export: schema, hashes, bounded textures, no source path/audio; " + output);
    }
}
