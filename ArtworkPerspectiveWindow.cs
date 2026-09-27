using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace ZipMp3Player;
internal sealed class ArtworkPerspectiveWindow:Window
{
    private readonly BitmapSource source;
    private readonly Canvas canvas;
    private readonly Polygon outline=new(){Stroke=Brushes.Orange,Fill=Brushes.Transparent,IsHitTestVisible=false};
    private readonly Image preview=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,10)};
    private readonly Button save=new(){Content="補正して保存"};
    private readonly List<Ellipse> handles=[];
    private int dragging=-1;
    private int edge=-1;
    private Point dragStart;
    private Point[]? dragCorners;
    private bool detecting;
    internal Point[]? Corners{get;private set;}
    internal Int32Rect ManualCrop{get;private set;}
    internal double ManualAngle{get;private set;}
    internal DiscCrop? Disc{get;private set;}
    internal ArtworkPerspectiveWindow(string name,BitmapSource source)
    {
        this.source=source;Title="自動切り抜き・歪み補正 — "+name;Width=1150;Height=800;MinWidth=800;MinHeight=550;
        Background=new SolidColorBrush(Color.FromRgb(21,23,27));Foreground=Brushes.White;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(45,53,65))));style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3)));Resources.Add(typeof(Button),style);
        var root=new DockPanel{Margin=new(18)};Content=root;
        var title=new TextBlock{Text="四隅・辺をドラッグして調整（細い線の中心が切り抜き境界です）",FontSize=18,Margin=new(0,0,0,12)};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cancel=new Button{Content="キャンセル",IsCancel=true};footer.Children.Add(cancel);footer.Children.Add(save);
        save.Click+=(_,_)=>{if(Corners is not null&&ArtworkPerspective.Valid(Corners,source.PixelWidth,source.PixelHeight))DialogResult=true;};
        var side=new StackPanel{Width=270,Margin=new(16,0,0,0)};DockPanel.SetDock(side,Dock.Right);root.Children.Add(side);
        var auto=new Button{Content="外周を自動検出"};auto.Click+=async(_,_)=>await Detect();side.Children.Add(auto);
        var disc=new Button{Content="DISC（円形）の切り抜き"};disc.Click+=(_,_)=>{if(detecting)return;var dialog=new ArtworkDiscWindow(name,source){Owner=this};if(dialog.ShowDialog()==true){Disc=dialog.Crop;DialogResult=true;}};side.Children.Add(disc);
        var reset=new Button{Content="四隅を画像全体へ戻す"};reset.Click+=(_,_)=>{if(!detecting){Reset();Refresh();}};side.Children.Add(reset);
        var manual=new Button{Content="従来の切り抜き・角度調整"};manual.Click+=(_,_)=>{if(detecting)return;var dialog=new ArtworkCropWindow(name,source){Owner=this};if(dialog.ShowDialog()==true){Corners=null;ManualCrop=dialog.Crop;ManualAngle=dialog.FineAngle;DialogResult=true;}};side.Children.Add(manual);
        side.Children.Add(status);side.Children.Add(new TextBlock{Text="補正後のプレビュー",FontWeight=FontWeights.Bold});preview.Height=230;side.Children.Add(preview);
        side.Children.Add(new TextBlock{Text="橙色の枠が残す範囲です。四隅を確認してから保存してください。\n\n傾き・台形を補正します。折り目による曲面の湾曲は完全には補正できません。縦横比は辺の長さから推定します。\n\n保存までは元画像を変更しません。保存時はバックアップを残します。",TextWrapping=TextWrapping.Wrap,Margin=new(0,14,0,0)});
        canvas=new Canvas{Width=source.PixelWidth,Height=source.PixelHeight,Background=Brushes.Black};canvas.Children.Add(new Image{Source=source,Width=source.PixelWidth,Height=source.PixelHeight,Stretch=Stretch.Fill});
        outline.StrokeThickness=Math.Max(source.PixelWidth,source.PixelHeight)/1100d;canvas.Children.Add(outline);
        for(int i=0;i<4;i++){var handle=new Ellipse{Fill=Brushes.Orange,Stroke=Brushes.Black,StrokeThickness=outline.StrokeThickness/3,IsHitTestVisible=false};handles.Add(handle);canvas.Children.Add(handle);}
        root.Children.Add(new Viewbox{Stretch=Stretch.Uniform,Child=canvas});
        canvas.MouseLeftButtonDown+=(_,e)=>{if(detecting||Corners is null)return;var p=e.GetPosition(canvas);var nearest=Enumerable.Range(0,4).MinBy(i=>(Corners[i]-p).LengthSquared);
            dragging=-1;edge=-1;double tolerance=Math.Max(source.PixelWidth,source.PixelHeight)/65d;
            if((Corners[nearest]-p).Length<tolerance)dragging=nearest;
            else{int n=Enumerable.Range(0,4).MinBy(i=>Distance(p,Corners[i],Corners[(i+1)%4]));if(Distance(p,Corners[n],Corners[(n+1)%4])<tolerance)edge=n;}
            if(dragging<0&&edge<0)return;dragStart=p;dragCorners=Corners.ToArray();canvas.CaptureMouse();e.Handled=true;};
        canvas.MouseMove+=(_,e)=>{if(Corners is null)return;var p=e.GetPosition(canvas);
            if(dragging>=0)Corners[dragging]=new Point(Math.Clamp(p.X,0,source.PixelWidth),Math.Clamp(p.Y,0,source.PixelHeight));
            else if(edge>=0&&dragCorners is not null){int next=(edge+1)%4;var delta=p-dragStart;var a=dragCorners[edge];var b=dragCorners[next];delta.X=Math.Clamp(delta.X,-Math.Min(a.X,b.X),source.PixelWidth-Math.Max(a.X,b.X));delta.Y=Math.Clamp(delta.Y,-Math.Min(a.Y,b.Y),source.PixelHeight-Math.Max(a.Y,b.Y));Corners[edge]=a+delta;Corners[next]=b+delta;}else return;Draw();};
        canvas.MouseLeftButtonUp+=(_,_)=>{dragging=edge=-1;canvas.ReleaseMouseCapture();Refresh();};
        canvas.LostMouseCapture+=(_,_)=>{dragging=edge=-1;};
        Reset();Loaded+=async(_,_)=>await Detect();
    }
    private void Reset(){Corners=[new(0,0),new(source.PixelWidth,0),new(source.PixelWidth,source.PixelHeight),new(0,source.PixelHeight)];Draw();}
    private static double Distance(Point p,Point a,Point b){var v=b-a;double t=v.LengthSquared==0?0:Math.Clamp(Vector.Multiply(p-a,v)/v.LengthSquared,0,1);return (p-(a+v*t)).Length;}
    private void Draw(){if(Corners is null)return;outline.Points=new PointCollection(Corners);double r=Math.Max(source.PixelWidth,source.PixelHeight)/70d;for(int i=0;i<4;i++){handles[i].Width=handles[i].Height=r;Canvas.SetLeft(handles[i],Corners[i].X-r/2);Canvas.SetTop(handles[i],Corners[i].Y-r/2);}save.IsEnabled=!detecting&&ArtworkPerspective.Valid(Corners,source.PixelWidth,source.PixelHeight);}
    private async System.Threading.Tasks.Task Detect()
    {
        if(detecting)return;detecting=true;save.IsEnabled=false;status.Text="外周を検出しています…";
        try{var detected=await System.Threading.Tasks.Task.Run(()=>ArtworkPerspective.Detect(source));if(detected is not null)Corners=detected;
            status.Text=detected is null?"自動検出できませんでした。四隅を手動で合わせてください。":"自動検出の候補です。枠がブックレット全体を囲んでいるか確認してください。";
        }catch(Exception ex){status.Text="検出できませんでした："+ex.Message;}finally{detecting=false;Draw();Refresh();}
    }
    private void Refresh(){if(Corners is null)return;try{
        // Use a small source for interactive preview; final save uses the untouched full-resolution source.
        var small=ArtworkDeskew.Render(source,0,1000);double sx=(double)small.PixelWidth/source.PixelWidth,sy=(double)small.PixelHeight/source.PixelHeight;
        preview.Source=ArtworkPerspective.Render(small,Corners.Select(p=>new Point(p.X*sx,p.Y*sy)).ToArray(),650);Draw();
    }catch(ArgumentException){save.IsEnabled=false;status.Text="四隅が交差・接近しています。角を戻してください。";preview.Source=null;}}
}
