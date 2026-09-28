using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZipMp3Player;

internal static class BookletSlideshowChecks
{
    public static void Run()
    {
        var app=new Application();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)255,16).ToArray(),8);bitmap.Freeze();
        var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.BookletViewerWindow")!;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(Window w,string name)=>type.GetField(name,flags)!.GetValue(w);
        object? Call(Window w,string name,params object[] args)=>type.GetMethod(name,flags)!.Invoke(w,args);
        void Wait(Task task){while(!task.IsCompleted){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();}
        Window Make(params BookletPage[] pages)=>(Window)Activator.CreateInstance(type,["Slideshow test",new BookletContent(bitmap,pages)])!;
        void Check(bool condition,string name){if(!condition)throw new Exception(name);}
        var pages=Enumerable.Range(1,3).Select(i=>new BookletPage("Page "+i,"Page",()=>bitmap)).ToArray();
        var w=Make(pages);Wait((Task)Call(w,"ShowPageAsync",0,false)!);
        Call(w,"ToggleSlideshow");Check((bool)Field(w,"_slideshowPlaying")!,"Start");
        Check(((DispatcherTimer)Field(w,"_slideTimer")!).IsEnabled,"Timer start");
        Wait((Task)Call(w,"AdvanceSlideshowAsync")!);Check((int)Field(w,"_index")! == 1,"Next page");
        Call(w,"Navigate",-1);Check(!(bool)Field(w,"_slideshowPlaying")!,"Manual pause");
        Wait((Task)Call(w,"ShowPageAsync",2,false)!);Call(w,"ToggleSlideshow");
        Wait((Task)Call(w,"ShowPageAsync",0,false)!);Check((int)Field(w,"_index")! == 0,"Restart at first");
        Wait((Task)Call(w,"AdvanceSlideshowAsync")!);Wait((Task)Call(w,"AdvanceSlideshowAsync")!);Wait((Task)Call(w,"AdvanceSlideshowAsync")!);
        Check(!(bool)Field(w,"_slideshowPlaying")!&&(int)Field(w,"_index")! == 2,"End stops");
        ((CheckBox)Field(w,"_repeat")!).IsChecked=true;Call(w,"ToggleSlideshow");Wait((Task)Call(w,"ShowPageAsync",2,false)!);
        Wait((Task)Call(w,"AdvanceSlideshowAsync")!);Check((int)Field(w,"_index")! == 0,"Repeat wraps");
        ((ComboBox)Field(w,"_interval")!).SelectedIndex=2;Check(((DispatcherTimer)Field(w,"_slideTimer")!).Interval.TotalSeconds==10.24,"Interval change");
        var content=(FrameworkElement)w.Content;content.Measure(new Size(1126,750));content.Arrange(new Rect(0,0,1126,750));content.UpdateLayout();
        var render=new RenderTargetBitmap(1126,750,96,96,PixelFormats.Pbgra32);render.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(render));
        var screenshot=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"booklet-slideshow-"+Guid.NewGuid().ToString("N")+".png");using(var output=System.IO.File.Create(screenshot))encoder.Save(output);Console.WriteLine(screenshot);
        w.Close();Check(!((DispatcherTimer)Field(w,"_slideTimer")!).IsEnabled,"Close stops timer");
        var one=Make(pages[0]);Wait((Task)Call(one,"ShowPageAsync",0,false)!);Call(one,"ToggleSlideshow");Check(!(bool)Field(one,"_slideshowPlaying")!,"Single page disabled");one.Close();
        var broken=Make(pages[0],new BookletPage("broken","Page",()=>throw new Exception("Expected test failure")));
        Wait((Task)Call(broken,"ShowPageAsync",0,false)!);Call(broken,"ToggleSlideshow");Wait((Task)Call(broken,"AdvanceSlideshowAsync")!);
        Check(!(bool)Field(broken,"_slideshowPlaying")!,"Failure pauses");broken.Close();
        using var gate=new ManualResetEventSlim();var started=new TaskCompletionSource();
        var slow=Make(pages[0],new BookletPage("slow","Page",()=>{started.SetResult();gate.Wait();return bitmap;}));
        Wait((Task)Call(slow,"ShowPageAsync",0,false)!);Call(slow,"ToggleSlideshow");var loading=(Task)Call(slow,"AdvanceSlideshowAsync")!;Wait(started.Task);
        Check(!((DispatcherTimer)Field(slow,"_slideTimer")!).IsEnabled,"Loading timer stopped");Wait((Task)Call(slow,"AdvanceSlideshowAsync")!);
        slow.Close();gate.Set();Wait(loading);Check(!((DispatcherTimer)Field(slow,"_slideTimer")!).IsEnabled,"Late load cannot restart closed viewer");
        Console.WriteLine("PASS booklet slideshow: start/pause, manual navigation, end, repeat, interval, one page, errors, slow loads, close");
    }
}
