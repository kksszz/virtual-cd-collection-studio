using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf.SharpDX;
using ZipMp3Player;

internal static class BookletOpeningChecks
{
    public static void Run()
    {
        var app=new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.DxJewelCaseScene")!;
        using var scene=(IDisposable)Activator.CreateInstance(type)!;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(string name)=>type.GetField(name,flags)!.GetValue(scene);
        object? Call(string name,params object?[] args)=>type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(scene,args);
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        void Wait(Task task){while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();}
        BitmapSource Image(int width,string label,Brush brush){
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                dc.DrawRectangle(brush,null,new Rect(0,0,width,240));
                dc.DrawText(new FormattedText(label,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),24,Brushes.Black,1),new Point(20,80));
            }
            var image=new RenderTargetBitmap(width,240,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return image;
        }
        var front=Image(240,"FRONT",Brushes.LightPink);var back=Image(240,"BACK",Brushes.LightBlue);var page=Image(480,"LEFT                  RIGHT",Brushes.Beige);
        var item=new JewelCaseCoverFlowItem("opening","Booklet test","","DIR","Clear",front,back,back,null,null,null,null,false);
        Call("SetItem",item,0d,0d);Call("SetWrappingOpened",true,false);Call("SetCaseOpen",true,false);
        Call("PrepareBookletOpening",page);Check(Field("_bookletOpeningRoot")==null,"Cannot unfold inside case");
        Call("SetBookletRemoved",true,false);Call("PrepareBookletOpening",page);
        var rear=(Element3D)Field("_bookletRearArtwork")!;
        Check(rear.Visibility==Visibility.Hidden,"Hide original rear during opening");
        var root=(GroupModel3D)Field("_bookletOpeningRoot")!;
        Check(root.Children.Count==2,"One stationary page and one cover");
        var turn=(AxisAngleRotation3D)Field("_bookletCoverRotation")!;
        var cover=(GroupModel3D)root.Children[1];
        var transform=(RotateTransform3D)cover.Transform;
        var pivot=new Point3D(transform.CenterX,0,transform.CenterZ);
        var stationary=(MeshGeometryModel3D)root.Children[0];
        var vertices=((HelixToolkit.SharpDX.MeshGeometry3D)stationary.Geometry).Positions;
        Check(Math.Abs(pivot.X-vertices.Min(p=>p.X))<1e-7,"Fold is on local-left / screen-right edge");
        turn.Angle=90;
        Check((transform.Transform(pivot)-pivot).Length<1e-8,"Fold pivot stays attached");
        Check(transform.Transform(new Point3D(pivot.X+1,0,pivot.Z)).Z<pivot.Z,"Reversed cover still opens away from case");
        turn.Angle=0;
        var viewport=(Viewport3DX)type.GetProperty("Viewport")!.GetValue(scene)!;
        var window=new Window{Width=1100,Height=700,Content=viewport,ShowInTaskbar=false};
        window.Show();
        var output=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"booklet-opening-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(output);
        foreach(var angle in new[]{0d,90,180}){
            Call("SetBookletOpeningProgress",angle/180);Call("RequestRender");Wait(Task.Delay(220));
            ViewportExtensions.SaveScreen(viewport,System.IO.Path.Combine(output,Math.Abs(angle)+".png"));
        }
        Call("SetBookletOpeningProgress",0d);Wait((Task)Call("AnimateBookletOpeningAsync",true)!);Check(turn.Angle==180,"Reversed opening completes");
        Wait((Task)Call("AnimateBookletOpeningAsync",false)!);Check(turn.Angle==0,"Close completes");
        var interrupted=(Task<bool>)Call("AnimateBookletOpeningAsync",true)!;
        Call("SetItem",item,0d,0d);Wait(interrupted);Check(!interrupted.Result,"Item switch cancels animation");
        Check(Field("_bookletOpeningRoot")==null && (double)Field("_bookletProgress")! == 0,"Reset geometry and extraction");
        Call("SetBookletRemoved",true,false);Call("PrepareBookletOpening",(object?)null);
        Check(Field("_bookletOpeningRoot")!=null,"Blank-paper fallback");
        Call("SetBookletRemoved",false,false);
        Check(Field("_bookletOpeningRoot")==null&&((Element3D)Field("_bookletRearArtwork")!).Visibility==Visibility.Visible,"Insertion restores original artwork");
        Call("SetBookletRemoved",true,false);Call("PrepareBookletOpening",front);
        var pending=(Task<bool>)Call("AnimateBookletOpeningAsync",true)!;window.Content=null;window.Close();scene.Dispose();Wait(pending);
        Check(!pending.Result,"Dispose cancels animation");
        Console.WriteLine("PASS booklet opening: extracted-only, attached fold, outward motion, open/close, reset, missing pages, dispose");
        Console.WriteLine(output);
    }
}
