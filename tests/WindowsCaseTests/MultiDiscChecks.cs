using System.Reflection;
using System.Windows.Media.Media3D;
using ZipMp3Player;

internal static class MultiDiscChecks
{
    internal static void RunGpu(DxJewelCaseScene scene,MultiCaseArtwork art)
    {
        void Pump() {
            var frame=new System.Windows.Threading.DispatcherFrame();
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
            timer.Tick+=(_,_)=>frame.Continue=false;timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);timer.Stop();
        }
        scene.SetItem(new JewelCaseCoverFlowItem("interaction","Test","","","White",null,null,null,null,null,null,null,false){MultiCase=art},0,0);
        scene.SetCaseOpen(true,false);scene.SetViewZoom(.85);Pump();
        System.Windows.Point Find(int number) {
            for(int y=40;y<scene.Viewport.ActualHeight-40;y+=25)
                for(int x=40;x<scene.Viewport.ActualWidth-40;x+=25) {
                    var p=new System.Windows.Point(x,y);
                    if(scene.SelectMultiDiscAt(p)&&scene.SelectedMultiDisc==number)return p;
                }
            throw new Exception("Disc hit target missing: "+number);
        }
        var first=Find(1);
        if(!scene.IsDiscHit(first))throw new Exception("Multi-disc double-click hit");
        scene.SetSelectedMultiDiscRemoved(true);Pump();
        var removed=Find(1);
        if(!scene.BeginDiscDrag(removed))throw new Exception("Removed disc drag start");
        scene.DragDiscTo(new System.Windows.Point(removed.X+35,removed.Y+20));scene.EndDiscDrag();
        scene.SetSelectedMultiDiscRemoved(false);Pump();
        Find(2);
        Console.WriteLine("PASS rendered CD hit, extraction, dragging, return and second-disc selection");
        scene.SetDiscPlaying(false);
    }
    internal static void Run(JewelCaseCoverFlowItem item)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        using var scene=new DxJewelCaseScene();
        scene.SetItem(item,0,0);scene.SetCaseOpen(true,false);
        var field=typeof(DxJewelCaseScene).GetField("_multiDiscStates",flags)!;
        object State(int n)=>((System.Collections.IDictionary)field.GetValue(scene)!)[n]!;
        T Value<T>(int n,string name)=>(T)State(n).GetType().GetField(name,flags)!.GetValue(State(n))!;
        double Angle(int n)=>Value<AxisAngleRotation3D>(n,"Spin").Angle;
        Check(scene.SelectMultiDisc(1)&&!scene.SelectMultiDisc(4),"only exposed disc can be selected");
        scene.SetSelectedMultiDiscRemoved(true);
        Check(Value<bool>(1,"Removed")&&!Value<bool>(2,"Removed"),"extraction affects only selected disc");
        scene.ActivateSelectedMultiDisc();scene.SetDiscPlaying(true);
        System.Threading.Thread.Sleep(20);
        typeof(DxJewelCaseScene).GetMethod("AdvanceDiscSpin",flags)!.Invoke(scene,null);
        Check(Angle(1)!=0&&Angle(2)==0&&Angle(3)==0&&Angle(4)==0,"only activated disc rotates");
        scene.SetDiscPlaying(false);
        double stopped=Angle(1);
        Check(scene.SelectMultiDisc(2),"select second exposed disc");
        scene.ActivateSelectedMultiDisc();scene.SetDiscPlaying(true);
        System.Threading.Thread.Sleep(20);
        typeof(DxJewelCaseScene).GetMethod("AdvanceDiscSpin",flags)!.Invoke(scene,null);
        Check(Angle(1)==stopped&&Angle(2)!=0,"activation switches spin without rotating previous disc");
        scene.SetDiscPlaying(false);
        scene.SetItem(item,0,0);
        Check(Value<bool>(1,"Removed")&&scene.SelectedMultiDisc==2,"artwork rebuild preserves per-disc extraction and selection");
        scene.SetCaseOpen(false,false);
        Check(!Value<bool>(1,"Removed")&&!scene.SelectMultiDisc(1),"closing returns discs and blocks hidden selection");
        var pair=item with {Key="pair",MultiCase=item.MultiCase! with {Disc3=null,Disc4=null}};
        scene.SetItem(pair,0,0);scene.SetCaseOpen(true,false);
        Check(!scene.SelectMultiDisc(2),"rear Disc2 inaccessible until central tray turns");
        scene.TurnMultiCase(true,false);
        Check(scene.SelectMultiDisc(2),"rear physical seat retains logical Disc2 identity");
        scene.SetSelectedMultiDiscRemoved(true);
        Check(Value<bool>(2,"Removed"),"rear Disc2 individually removable");
        scene.SetItem(item with {Key="covered",MultiCase=item.MultiCase! with {BookletFront=item.MultiCase!.Front}},0,0);
        scene.SetCaseOpen(false,false);scene.SetCaseOpen(true,false);
        Check(!scene.SelectMultiDisc(2),"booklet blocks hidden disc interaction");
        scene.CancelMultiDiscPlayback();scene.SetDiscPlaying(true);
        Check(!(bool)typeof(DxJewelCaseScene).GetField("_discPlaying",flags)!.GetValue(scene)!,"no activated disc means no automatic multi-disc spin");
    }
}
