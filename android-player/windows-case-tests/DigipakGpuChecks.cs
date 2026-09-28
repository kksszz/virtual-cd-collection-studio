using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZipMp3Player;

internal static class DigipakGpuChecks
{
    internal static void Run(string images)
    {
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(Path.GetTempPath(),"vccs-gpu-"+Guid.NewGuid().ToString("N")));
        _=new Application();
        const BindingFlags stat=BindingFlags.NonPublic|BindingFlags.Static;
        BitmapSource Load(string file){
            var bitmap=(BitmapSource)typeof(MainWindow).GetMethod("LoadBitmap",stat,null,[typeof(string),typeof(int)],null)!.Invoke(null,[Path.Combine(images,file),2048])!;
            if(Math.Max(bitmap.PixelWidth,bitmap.PixelHeight)>8192)throw new Exception("Oversized decoded texture");
            Console.WriteLine($"PASS bounded decode {file}: {bitmap.PixelWidth} x {bitmap.PixelHeight}");return bitmap;
        }
        var item=new JewelCaseCoverFlowItem("gpu","ActRaiser","","DIR","Clear",Load("Booklet001.jpg"),null,Load("Booklet002.jpg"),null,null,null,Load("Booklet008.jpg"),false){
            SecondDiscImage=Load("Booklet009.jpg"),Digipak=new(Load("Booklet010.jpg"),Load("Booklet011.jpg"),Load("Digipac001.jpg")){LeftFold=Load("Left_Spine.jpg"),RightFold=Load("Right_Spine.jpg")}
        };
        using var scene=new DxJewelCaseScene();scene.SetItem(item,0,0);
        int frames=0;scene.Viewport.OnRendered+=(_,_)=>Interlocked.Increment(ref frames);
        var window=new Window{Content=scene.Viewport,Width=800,Height=500,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.ToolWindow};
        try{
            window.Show();WaitFrames("closed");
            foreach(double yaw in new[]{45d,-45d,85d,-85d,135d,-135d}){scene.SetRotation(yaw,12);WaitFrames("yaw"+yaw);}
            scene.SetViewZoom(2.4);scene.BeginInteractiveMotion();
            foreach(double yaw in new[]{20d,25d,30d,35d}){scene.SetRotation(yaw,12);WaitFrames("yaw-zoom-"+yaw);}
            scene.EndInteractiveMotion();WaitFrames("yaw-zoom-still");scene.SetViewZoom(1);
            scene.SetRotation(0,0);scene.SetCaseOpen(true,false);WaitFrames("open");
            scene.SetBookletRemoved(true,false);WaitFrames("booklet out");
            scene.SetBookletRemoved(false,false);scene.SetItem(item,0,0);WaitFrames("reload");
            // Exercise the final GPU safety net independently of the decoder.
            var narrow=BitmapSource.Create(16,20000,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[16*20000*4],16*4);narrow.Freeze();
            scene.SetItem(item with{Digipak=item.Digipak! with{RightFold=narrow}},0,0);WaitFrames("oversized input safety net");
        }finally{window.Close();}
        void WaitFrames(string label){
            int initial=Volatile.Read(ref frames);var clock=System.Diagnostics.Stopwatch.StartNew();
            var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=(_,_)=>{scene.Viewport.InvalidateRender();if(scene.Viewport.RenderException is not null||Volatile.Read(ref frames)>=initial+3||clock.Elapsed.TotalSeconds>10)frame.Continue=false;};
            timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
            if(scene.Viewport.RenderException is {} error)throw new Exception("GPU failed: "+label,error);
            if(Volatile.Read(ref frames)<initial+3)throw new Exception("No GPU frames: "+label);
            Console.WriteLine("PASS DirectX rendering: "+label);
            if(label is "closed" or "open" || label.StartsWith("yaw")){
                var directory=Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")!;Directory.CreateDirectory(directory);
                var capture=HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(scene.Viewport);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(capture));using var output=File.Create(Path.Combine(directory,label+".png"));encoder.Save(output);
                Console.WriteLine("GPU preview: "+Path.Combine(directory,label+".png"));
            }
        }
    }
}
