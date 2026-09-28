using System.Reflection;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ZipMp3Player;

internal static class SpineReturnChecks
{
    public static void Run()
    {
        var app=new Application();
        var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        var scene=Activator.CreateInstance(type)!;
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var drag=(TranslateTransform3D)type.GetField("_spineCardDragTranslation",flags)!.GetValue(scene)!;
        var translation=(TranslateTransform3D)type.GetField("_spineCardTranslation",flags)!.GetValue(scene)!;
        foreach(var progress in new[]{0d,.35,1d})foreach(var sign in new[]{-1,1})
        {
            type.GetMethod("SetSpineCardProgress",flags)!.Invoke(scene,[progress]);
            drag.OffsetX=sign*2.7;drag.OffsetY=sign*.9;drag.OffsetZ=.4;
            type.GetField("_spineCardDragPoint",flags)!.SetValue(scene,new Point3D(1,2,3));
            var task=(Task<bool>)type.GetMethod("AnimateSpineCardAsync")!.Invoke(scene,[false])!;
            if(!task.IsCompleted){
                var frame=new DispatcherFrame();
                _=task.ContinueWith(_=>app.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
                Dispatcher.PushFrame(frame);
            }
            if(!task.GetAwaiter().GetResult()||drag.OffsetX!=0||drag.OffsetY!=0||drag.OffsetZ!=0
                ||translation.OffsetX!=0||translation.OffsetY!=0||translation.OffsetZ!=0
                ||type.GetField("_spineCardDragPoint",flags)!.GetValue(scene)!=null)
                throw new Exception("Obi did not return to exact seated pose");
        }
        var clearance=(TranslateTransform3D)type.GetField("_spineCardBookletClearance",flags)!.GetValue(scene)!;
        void Wait(Task task){
            if(!task.IsCompleted){var frame=new DispatcherFrame();_=task.ContinueWith(_=>app.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));Dispatcher.PushFrame(frame);}
            task.GetAwaiter().GetResult();
        }
        foreach(var x in new[]{-3d,0d,4d})foreach(var z in new[]{-.7d,0d,.4d}){
            type.GetMethod("SetSpineCardProgress",flags)!.Invoke(scene,[1d]);
            drag.OffsetX=x;drag.OffsetY=.9;drag.OffsetZ=z;
            Wait((Task)type.GetMethod("MoveSpineCardForBookletAsync")!.Invoke(scene,[true])!);
            if(clearance.OffsetX!=0||clearance.OffsetY!=0||clearance.OffsetZ>0
                ||clearance.OffsetZ+drag.OffsetZ+translation.OffsetZ > -.30+1e-8)
                throw new Exception("Obi must retreat only in depth, behind the extraction plane");
            Wait((Task)type.GetMethod("MoveSpineCardForBookletAsync")!.Invoke(scene,[false])!);
            if(clearance.OffsetX!=0||clearance.OffsetY!=0||clearance.OffsetZ!=0||drag.OffsetX!=x||drag.OffsetY!=.9||drag.OffsetZ!=z)throw new Exception("Manual obi position changed");
        }
        var pending=(Task)type.GetMethod("MoveSpineCardForBookletAsync")!.Invoke(scene,[true])!;
        (scene as IDisposable)?.Dispose();
        Wait(pending);
        if(((DispatcherTimer)type.GetField("_obiBookletTimer",flags)!.GetValue(scene)!).IsEnabled)throw new Exception("Retreat timer remains active after disposal");
        Console.WriteLine("PASS booklet obi clearance and exact manual-position restoration");
        Console.WriteLine("PASS obi return: arbitrary drag, partial/fully removed/already seated, both directions");
    }
}
