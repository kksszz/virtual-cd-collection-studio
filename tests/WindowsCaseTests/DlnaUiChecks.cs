using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZipMp3Player;

internal static class DlnaUiChecks
{
    internal static void Run(string output)
    {
        // All application state and synthetic fixtures are isolated from the user's library/settings.
        string root=Path.Combine(Path.GetTempPath(),"vccs-dlna-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Directory.CreateDirectory(output);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
        var main=new MainWindow{ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-30000,Top=-30000};
        var type=typeof(MainWindow);object? Field(string name)=>type.GetField(name,flags)!.GetValue(main);
        var settings=Field("_settings")!;Check((bool)settings.GetType().GetProperty("DlnaRunInTrayOnClose")!.GetValue(settings)! == false&&Field("_dlnaServer") is null,"DLNA and tray remain OFF by default");
        string path=Path.Combine(root,"song.mp3");File.WriteAllBytes(path,new byte[4096]);
        var track=new ZipTrack{SourcePath=path,FileName="song.mp3",AudioFormat="MP3",Title="Synthetic song",Artist="Test artist",Album="テスト用のアルバム",IsMp3Valid=true,BitrateKbps=128,SampleRate=44100,Duration=TimeSpan.FromSeconds(1)};
        var album=new ZipAlbum{Path=root,Tracks=[track]};var itemType=type.GetNestedType("AlbumListItem",BindingFlags.NonPublic)!;
        var item=Activator.CreateInstance(itemType,[album,false])!;((IList)Field("_albums")!).Add(item);
        ((ListBox)main.FindName("AlbumList")).SelectedItem=item;
        var window=(Window)type.GetMethod("CreateDlnaWindow",flags)!.Invoke(main,[false])!;
        window.ShowActivated=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-30000;window.Top=-30000;window.Show();
        var initialFrame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>initialFrame.Continue=false));Dispatcher.PushFrame(initialFrame);
        var content=(FrameworkElement)window.Content;
        ((Panel)content).Background=window.Background;
        IEnumerable<T> Descendants<T>(DependencyObject value) where T:DependencyObject
        {if(value is T found)yield return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(value);i++)foreach(var next in Descendants<T>(VisualTreeHelper.GetChild(value,i)))yield return next;}
        void Render(int width,int height,string filename)
        {
            window.Width=width;window.Height=height;window.UpdateLayout();
            var layoutFrame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>layoutFrame.Continue=false));Dispatcher.PushFrame(layoutFrame);content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,filename));png.Save(file);
        }
        Render(900,650,"dlna-sharing.png");var grid=Descendants<DataGrid>(content).Single();var buttons=Descendants<Button>(content).ToArray();
        var start=buttons.Single(b=>b.Content?.ToString()=="選択したアルバムを配信");var stop=buttons.Single(b=>b.Content?.ToString()=="配信を停止");
        Check(!start.IsEnabled&&!stop.IsEnabled&&grid.Items.Count==1,"sharing consent required and selection table populated");
        Render(650,520,"dlna-sharing-small.png");
        Point startPosition=start.TranslatePoint(new Point(0,0),content);Check(grid.ActualHeight>70&&startPosition.Y+start.ActualHeight<=content.ActualHeight+1,"small window keeps album table and action buttons visible");
        window.Close();
        var options=new SettingsWindow([],[],false,root);Check(!options.DlnaRunInTrayOnClose,"settings tray checkbox defaults OFF");options.DlnaRunInTrayOnClose=true;Check(options.DlnaRunInTrayOnClose,"settings tray preference round trips");options.Close();
        using var server=new DlnaServer(new(IPAddress.Loopback,IPAddress.Parse("255.0.0.0"),"loopback"),[new(album,"test","artist")],Guid.NewGuid().ToString("D"),discover:false);
        type.GetField("_dlnaServer",flags)!.SetValue(main,server);settings.GetType().GetProperty("DlnaRunInTrayOnClose")!.SetValue(settings,true);
        bool hidden=(bool)type.GetMethod("TryHideDlnaToTray",flags)!.Invoke(main,null)!;if(!hidden)Console.WriteLine(((TextBlock)main.FindName("StatusText")).Text);
        Check(hidden&&!main.IsVisible,"main window hides to native notification icon");
        var tray=(System.Windows.Forms.NotifyIcon)Field("_dlnaTray")!;Check(tray.Visible&&tray.ContextMenuStrip?.Items.Count==4,"native tray offers show, stop, exit");
        type.GetMethod("RestoreDlnaFromTray",flags)!.Invoke(main,null);Check(main.IsVisible&&!tray.Visible,"tray restoration shows same main window");
        // Pump only this isolated test app, then force a real close to exercise network/icon cleanup.
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        main.Close();Check(!main.IsVisible&&tray.Visible&&Field("_dlnaServer") is not null,"normal close retains DLNA server in notification area");
        type.GetField("_forceClose",flags)!.SetValue(main,true);main.Close();Check(Field("_dlnaServer") is null&&Field("_dlnaTray") is null,"real exit disposes server and notification icon");
        Console.WriteLine("UI fixtures: "+root+"; no user's settings or real LAN sharing used");
    }
}
