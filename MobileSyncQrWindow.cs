using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ZipMp3Player;

public sealed class MobileSyncQrWindow : Window
{
    internal static Border CreateStatusPanel(TextBlock status)
    {
        // Reserve the same space while waiting, transferring, and reporting errors.
        // Long paths remain readable without resizing the QR viewport above it.
        status.Margin=new Thickness(0);
        var scroll=new ScrollViewer{Content=status,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        status.Foreground=MobileSyncAppearance.Brush("#D8E8F4");
        return new Border{Height=112,Padding=new Thickness(10),Background=MobileSyncAppearance.Brush("#223744"),CornerRadius=new CornerRadius(8),Child=scroll};
    }
    public static byte[] Encode(string address)
    {
        using var data=QRCodeGenerator.GenerateQrCode(address,QRCodeGenerator.ECCLevel.M);
        using var png=new PngByteQRCode(data);
        return png.GetGraphic(8); // Includes the four-module quiet zone; no logos or inverted colours.
    }
    public MobileSyncQrWindow(Window owner,string address,MobileSyncServer? server=null)
    {
        Owner=owner;Title="AndroidでQRコードを読み取って同期";Width=480;Height=600;MinWidth=400;MinHeight=500;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;MobileSyncAppearance.Apply(this);
        var panel=new DockPanel{Margin=new Thickness(20)};Content=panel;
        var heading=new TextBlock{Text="スマートフォーンの「設定 → PCから同期 → QRコードを読み取る」で、このコードを読み取ってください。",TextWrapping=TextWrapping.Wrap,FontSize=16,Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        if(server!=null){
            var status=new TextBlock{Text=server.TransferStatus,TextWrapping=TextWrapping.Wrap,FontSize=14,Foreground=Brushes.DarkSlateGray,Margin=new Thickness(10)};
            footer.Children.Add(CreateStatusPanel(status));
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            timer.Tick+=(_,_)=>status.Text=server.TransferStatus;timer.Start();Closed+=(_,_)=>timer.Stop();
        }
        footer.Children.Add(new TextBlock{Text="同じWi-Fiに接続してください。接続先を確認してから同期が始まります。\n転送中は元のモバイル同期画面を開いたままにしてください。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
        var manual=new Expander{Header="手動入力用URL"};manual.Content=new TextBox{Text=address,IsReadOnly=true,TextWrapping=TextWrapping.Wrap};footer.Children.Add(manual);
        var close=new Button{Content="閉じる",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,8,0,0)};close.Click+=(_,_)=>Close();footer.Children.Add(close);
        var bitmap=new BitmapImage();using(var stream=new MemoryStream(Encode(address))){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}
        var image=new Image{Source=bitmap,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);System.Windows.Automation.AutomationProperties.SetName(image,"Android同期用の接続QRコード");panel.Children.Add(image);
    }
}
