using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace ZipMp3Player;

internal sealed class ArtworkDiscWindow:Window
{
    private readonly BitmapSource source;
    private readonly Canvas canvas;
    private readonly Ellipse outline=new(){Stroke=Brushes.Orange,Fill=Brushes.Transparent};
    private readonly List<Ellipse> handles=[];
    private readonly Image preview=new(){Height=220,Stretch=Stretch.Uniform};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};
    private readonly Button save=new(){Content="切り抜いて保存"};
    private readonly Slider angle=new(){Minimum=-180,Maximum=180,SmallChange=.1,LargeChange=1};
    private int dragging=-1;private Point start;private Rect original;private bool busy;
    internal DiscCrop Crop{get;private set;}
    internal ArtworkDiscWindow(string name,BitmapSource source)
    {
        this.source=source;Title="DISCの切り抜き — "+name;Width=1150;Height=800;MinWidth=850;MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(21,23,27));Foreground=Brushes.White;
        var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(45,53,65))));style.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8)));style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3)));Resources.Add(typeof(Button),style);
        double diameter=Math.Min(source.PixelWidth,source.PixelHeight)*.8;
        Crop=new(new Rect((source.PixelWidth-diameter)/2,(source.PixelHeight-diameter)/2,diameter,diameter));
        var root=new DockPanel{Margin=new(18)};Content=root;
        var heading=new TextBlock{Text="DISC：円の内側で移動、上下左右の点で外周を調整",FontSize=18,Margin=new(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(new Button{Content="キャンセル",IsCancel=true});footer.Children.Add(save);save.Click+=(_,_)=>{if(!busy&&Crop.Valid(source))DialogResult=true;};
        var side=new StackPanel{Width=270,Margin=new(16,0,0,0)};DockPanel.SetDock(side,Dock.Right);root.Children.Add(side);
        var auto=new Button{Content="DISCの外周を自動検出"};auto.Click+=async(_,_)=>await Detect();side.Children.Add(auto);
        side.Children.Add(status);side.Children.Add(new TextBlock{Text="印刷の回転（＋は時計回り）"});side.Children.Add(angle);
        var value=new TextBlock();side.Children.Add(value);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};side.Children.Add(buttons);
        foreach(var delta in new[]{-90d,-1d,-.1d,.1d,1d,90d}){var button=new Button{Content=delta.ToString("+0.#;-0.#")+"°",Padding=new Thickness(3)};button.Click+=(_,_)=>angle.Value=Math.Clamp(angle.Value+delta,-180,180);buttons.Children.Add(button);}
        var reset=new Button{Content="回転を0°に戻す"};reset.Click+=(_,_)=>angle.Value=0;side.Children.Add(reset);
        angle.ValueChanged+=(_,_)=>{Crop=Crop with{Angle=angle.Value};value.Text=$"{angle.Value:F1}°";Refresh();};
        side.Children.Add(new TextBlock{Text="補正後のプレビュー",Margin=new(0,10,0,4)});side.Children.Add(preview);
        side.Children.Add(new TextBlock{Text="外周を真円に補正し、正方形で保存します。円の外側は白、中央の穴は元画像のままです。\n\n上下左右方向の楕円に対応します。斜めに傾いた楕円や盤面の反りは完全には補正できません。\n\n保存前は元画像を変更しません。保存時にバックアップを残します。",TextWrapping=TextWrapping.Wrap,Margin=new(0,12,0,0)});
        canvas=new Canvas{Width=source.PixelWidth,Height=source.PixelHeight,Background=Brushes.Black};canvas.Children.Add(new Image{Source=source,Width=source.PixelWidth,Height=source.PixelHeight});
        outline.StrokeThickness=Math.Max(source.PixelWidth,source.PixelHeight)/1100d;canvas.Children.Add(outline);
        for(int i=0;i<4;i++){var handle=new Ellipse{Fill=Brushes.Orange,IsHitTestVisible=false};handles.Add(handle);canvas.Children.Add(handle);}
        root.Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Uniform});
        canvas.MouseLeftButtonDown+=(_,e)=>{if(busy)return;var p=e.GetPosition(canvas);var points=Points();int near=Enumerable.Range(0,4).MinBy(i=>(points[i]-p).LengthSquared);
            dragging=(points[near]-p).Length<Math.Max(source.PixelWidth,source.PixelHeight)/55d?near:Crop.Bounds.Contains(p)?4:-1;
            if(dragging<0)return;start=p;original=Crop.Bounds;canvas.CaptureMouse();};
        canvas.MouseMove+=(_,e)=>{if(dragging<0)return;var delta=e.GetPosition(canvas)-start;Crop=Crop with{Bounds=Adjust(original,dragging,delta,source.PixelWidth,source.PixelHeight)};Draw();};
        canvas.MouseLeftButtonUp+=(_,_)=>{dragging=-1;canvas.ReleaseMouseCapture();Refresh();};canvas.LostMouseCapture+=(_,_)=>dragging=-1;
        Refresh();Loaded+=async(_,_)=>await Detect();
    }
    internal static Rect Adjust(Rect r,int handle,Vector delta,double width,double height)
    {
        if(handle==4){r.Offset(Math.Clamp(delta.X,-r.Left,width-r.Right),Math.Clamp(delta.Y,-r.Top,height-r.Bottom));return r;}
        double l=r.Left,t=r.Top,right=r.Right,b=r.Bottom;
        if(handle==0)t=Math.Clamp(t+delta.Y,0,b-8);
        if(handle==1)right=Math.Clamp(right+delta.X,l+8,width);
        if(handle==2)b=Math.Clamp(b+delta.Y,t+8,height);
        if(handle==3)l=Math.Clamp(l+delta.X,0,right-8);
        return new Rect(new Point(l,t),new Point(right,b));
    }
    private Point[] Points(){var r=Crop.Bounds;return [new(r.X+r.Width/2,r.Top),new(r.Right,r.Y+r.Height/2),new(r.X+r.Width/2,r.Bottom),new(r.Left,r.Y+r.Height/2)];}
    private void Draw(){var r=Crop.Bounds;Canvas.SetLeft(outline,r.X);Canvas.SetTop(outline,r.Y);outline.Width=r.Width;outline.Height=r.Height;var points=Points();double size=Math.Max(source.PixelWidth,source.PixelHeight)/70d;
        for(int i=0;i<4;i++){handles[i].Width=handles[i].Height=size;Canvas.SetLeft(handles[i],points[i].X-size/2);Canvas.SetTop(handles[i],points[i].Y-size/2);}save.IsEnabled=!busy&&Crop.Valid(source);}
    private void Refresh(){Draw();preview.Source=ArtworkDisc.Render(source,Crop,500);}
    private async Task Detect(){if(busy)return;busy=true;save.IsEnabled=false;status.Text="円の外周を検出しています…";
        try{var result=await Task.Run(()=>ArtworkDisc.Detect(source));if(result is not null)Crop=result with{Angle=angle.Value};status.Text=result is null?"検出できませんでした。円を手動で合わせてください。":"検出候補です。円がDISCの外周と一致しているか確認してください。";}
        catch(Exception ex){status.Text="検出できませんでした："+ex.Message;}finally{busy=false;Refresh();}}
}
