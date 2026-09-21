using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class SyncStyleChecks
{
    public static void RunTable(string output){
        var app=new Application();var window=new Window();MobileSyncAppearance.Apply(window);var list=MobileSyncAppearance.CreateTransferTable();list.Resources=window.Resources;
        if(((SolidColorBrush)window.Background).Color!=Color.FromRgb(21,23,27)||((SolidColorBrush)window.Foreground).Color!=Color.FromRgb(242,243,245))throw new Exception("Dialog theme");
        list.Items.Add(new{Album=new{Title="Pimp Your Past",Artist="Fair Warning"},Tracks=11,Size="435.8 MiB",Format="DIR · FLAC",CaseStatus="あり",Images="あり (20)"});
        list.Items.Add(new{Album=new{Title="The Fallen King (Japan)",Artist="Frozen Crown"},Tracks=11,Size="343.4 MiB",Format="DIR · FLAC",CaseStatus="未生成",Images="あり (18)"});
        list.Items.Add(new{Album=new{Title="長いアルバム名の表示確認：文字が隣のセルに重ならないこと",Artist="Various Artists"},Tracks=14,Size="確認中…",Format="ZIP.MP3 · MP3",CaseStatus="あり",Images="未確認"});
        list.SelectedItems.Add(list.Items[0]);list.SelectedItems.Add(list.Items[2]);list.Measure(new Size(1020,220));list.Arrange(new Rect(0,0,1020,220));list.UpdateLayout();
        if(list.SelectedItems.Count!=2||((GridView)list.View).Columns.Count!=9)throw new Exception("Table selection or columns");
        var headers=Descendants(list).OfType<GridViewColumnHeader>().Where(h=>h.Column!=null).OrderBy(h=>h.TranslatePoint(new Point(),list).X).ToArray();
        foreach(var row in Descendants(list).OfType<GridViewRowPresenter>()){
            var cells=Descendants(row).OfType<Border>().Where(b=>b.Margin.Left==-6).ToArray();
            if(cells.Length!=8)throw new Exception("Missing table cells");
            for(int i=0;i<cells.Length;i++)if(Math.Abs(cells[i].TranslatePoint(new Point(),list).X-headers[i+1].TranslatePoint(new Point(),list).X)>.1||Math.Abs(cells[i].ActualWidth-headers[i+1].ActualWidth)>.1)throw new Exception("Header/cell alignment");
        }
        var bitmap=new RenderTargetBitmap(1020,220,96,96,PixelFormats.Pbgra32);bitmap.Render(list);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(output);encoder.Save(file);Console.WriteLine("PASS table: 8 columns, multiple selection, compact rows; "+output);
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    public static void Run(string output)
    {
        var app=new Application();var inherited=new Style(typeof(ListBoxItem));inherited.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));app.Resources.Add(typeof(ListBoxItem),inherited);
        var list=new StackPanel{Width=710,Height=180,Background=MobileSyncAppearance.Brush("#1F2329")};
        var style=MobileSyncAppearance.CreateItemStyle();
        var selected=new ListBoxItem{Content="Are You Dead Yet? -Deluxe Edition- — Children Of Bodom",Style=style,IsSelected=true,FontSize=14};
        var plain=new ListBoxItem{Content="通常行：アルバム名 — アーティスト名",Style=style,FontSize=14};
        var disabled=new ListBoxItem{Content="処理中も文字を読み取れます",Style=style,FontSize=14,IsEnabled=false};
        var template=MobileSyncAppearance.CreateAlbumTemplate();
        selected.Content=new {Album=new {Title="Are You Dead Yet? -Deluxe Edition-",Artist="Children Of Bodom"}};selected.ContentTemplate=template;
        plain.Content=new {Album=new {Title="蒼木輝雄の世界 — 花江夏樹",Artist="花江夏樹"}};plain.ContentTemplate=template;
        disabled.Content=new {Album=new {Title="Love Collection ～mint～",Artist="西野カナ"}};disabled.ContentTemplate=template;
        var testWindow=new Window();MobileSyncAppearance.Apply(testWindow);
        list.Children.Add(selected);list.Children.Add(plain);list.Children.Add(disabled);list.Measure(new Size(710,180));list.Arrange(new Rect(0,0,710,180));list.UpdateLayout();
        if(((SolidColorBrush)selected.Foreground).Color!=Colors.White||((SolidColorBrush)selected.Background).Color!=Color.FromRgb(40,91,128))throw new Exception("Selected contrast");
        if(((SolidColorBrush)plain.Foreground).Color!=Color.FromRgb(242,243,245))throw new Exception("Normal contrast");
        list.UpdateLayout();
        var bitmap=new RenderTargetBitmap(710,180,96,96,PixelFormats.Pbgra32);bitmap.Render(list);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);using(var file=File.Create(output))encoder.Save(file);
        Console.WriteLine("PASS sync list contrast: normal, selected without focus, disabled; "+Path.GetFullPath(output));
    }
}
