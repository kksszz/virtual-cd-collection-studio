using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ZipMp3Player;

internal sealed class ArtworkCropWindow : Window
{
    private readonly BitmapSource _source;
    private readonly Canvas _canvas;
    private readonly Rectangle _selection;
    private readonly System.Windows.Controls.Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _dimensions = new() { Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _save;
    private readonly System.Windows.Controls.Image _image=new();
    private readonly Canvas _grid=new() { IsHitTestVisible=false };
    private readonly Slider _angle=new() { Minimum=-15,Maximum=15,TickFrequency=.1,IsSnapToTickEnabled=true,SmallChange=.1,LargeChange=1 };
    private readonly TextBlock _angleLabel=new() { Foreground=Brushes.White,Margin=new Thickness(0,6,0,6) };
    private BitmapSource _working;
    private Point? _anchor;
    public Int32Rect Crop { get; private set; }
    public double FineAngle => Math.Round(_angle.Value,1);

    public ArtworkCropWindow(string name, BitmapSource source)
    {
        _source = source;
        _working=source;
        Title = "画像の切り抜き — " + name;
        Width = 1100; Height = 760; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(21, 23, 27));
        Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new TextBlock
        {
            Text = "角度を整え、残したい範囲をドラッグで囲んでください",
            FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        Button Button(string text) => new()
        {
            Content = text, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(5, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(45, 53, 65)), Foreground = Brushes.White
        };
        var reset = Button("選択をリセット"); reset.Click += (_, _) => Reset(); buttons.Children.Add(reset);
        var cancel = Button("キャンセル"); cancel.IsCancel = true; buttons.Children.Add(cancel);
        _save = Button("保存"); _save.Background = new SolidColorBrush(Color.FromRgb(40, 121, 184));
        _save.Click += (_, _) => { if (_save.IsEnabled) DialogResult = true; }; buttons.Children.Add(_save);
        footer.Children.Add(new TextBlock
        {
            Text = "「保存」を押すまで元画像は変更されません。",
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray
        });
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
        root.Children.Add(body);
        _canvas = new Canvas { Width = source.PixelWidth, Height = source.PixelHeight, Background = Brushes.Black, ClipToBounds = true, Cursor = Cursors.Cross };
        _image.Source=source;_image.Width=source.PixelWidth;_image.Height=source.PixelHeight;_image.Stretch=Stretch.Fill;
        _canvas.Children.Add(_image);_canvas.Children.Add(_grid);
        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(255, 183, 77)),
            StrokeThickness = Math.Max(source.PixelWidth, source.PixelHeight) / 400.0,
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 183, 77)), IsHitTestVisible = false
        };
        _canvas.Children.Add(_selection);
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        body.Children.Add(new Border { Background = Brushes.Black, Padding = new Thickness(8), Child = viewbox });
        var side = new DockPanel { Margin = new Thickness(18, 0, 0, 0) };
        Grid.SetColumn(side, 1); body.Children.Add(side);
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text="角度の微調整（＋＝時計回り）",FontWeight=FontWeights.SemiBold });
        info.Children.Add(_angleLabel);info.Children.Add(_angle);
        var angleButtons=new WrapPanel { Margin=new Thickness(0,8,0,8) };
        foreach(var amount in new[]{-1d,-.1,.1,1}){
            var button=Button(amount.ToString("+0.#;-0.#")+"°");button.Padding=new Thickness(6,6,6,6);button.Click+=(_,_)=>_angle.Value=Math.Clamp(FineAngle+amount,-15,15);angleButtons.Children.Add(button);
        }
        var zero=Button("0°に戻す");zero.Padding=new Thickness(6);zero.Click+=(_,_)=>_angle.Value=0;angleButtons.Children.Add(zero);info.Children.Add(angleButtons);
        var gridToggle=new CheckBox {Content="水平・垂直グリッド",Foreground=Brushes.White,IsChecked=true,Margin=new Thickness(0,0,0,12)};
        gridToggle.Checked+=(_,_)=>_grid.Visibility=Visibility.Visible;gridToggle.Unchecked+=(_,_)=>_grid.Visibility=Visibility.Collapsed;info.Children.Add(gridToggle);
        info.Children.Add(new TextBlock { Text = "切り抜き後のプレビュー", FontSize = 16, FontWeight = FontWeights.SemiBold });
        info.Children.Add(_dimensions);
        info.Children.Add(new TextBlock
        {
            Text = "±15°まで0.1°刻みで調整できます。回転でできる余白は白色です。\n\n保存時だけ元画像を書き換え、バックアップを残します。ZIPは無圧縮で再構築します。JPEGは再圧縮されます。",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 12)
        });
        DockPanel.SetDock(info, Dock.Top); side.Children.Add(info); side.Children.Add(_preview);
        Content = root;
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _anchor = Clamp(e.GetPosition(_canvas));
            _canvas.CaptureMouse();
            SetSelection(_anchor.Value, _anchor.Value, false); e.Handled = true;
        };
        _canvas.MouseMove += (_, e) =>
        {
            if (_anchor is { } start) SetSelection(start, Clamp(e.GetPosition(_canvas)), false);
        };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_anchor is not { } start) return;
            SetSelection(start, Clamp(e.GetPosition(_canvas)), true);
            _anchor = null; _canvas.ReleaseMouseCapture(); e.Handled = true;
        };
        _canvas.LostMouseCapture += (_, _) => { _anchor = null; RefreshPreview(); };
        _angle.ValueChanged+=(_,_)=>ApplyAngle();
        ApplyAngle();
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, _canvas.Width), Math.Clamp(point.Y, 0, _canvas.Height));

    private void ApplyAngle()
    {
        _anchor=null;_canvas.ReleaseMouseCapture();
        var oldWidth=_canvas.Width;var oldHeight=_canvas.Height;var previous=Crop;
        bool full=previous.Width==0||(previous.Width==(int)oldWidth&&previous.Height==(int)oldHeight);
        var size=ArtworkDeskew.Bounds(_source.PixelWidth,_source.PixelHeight,FineAngle);
        _working=ArtworkDeskew.Render(_source,FineAngle,1600);
        _canvas.Width=_image.Width=size.Width;_canvas.Height=_image.Height=size.Height;_image.Source=_working;
        _angleLabel.Text=$"{FineAngle:+0.0;-0.0;0.0}°";
        _grid.Children.Clear();
        for(int i=1;i<8;i++){
            double x=size.Width*i/8,y=size.Height*i/8,stroke=Math.Max(size.Width,size.Height)/800;
            _grid.Children.Add(new Line {X1=x,X2=x,Y1=0,Y2=size.Height,Stroke=Brushes.LightSkyBlue,StrokeThickness=stroke,Opacity=.6});
            _grid.Children.Add(new Line {X1=0,X2=size.Width,Y1=y,Y2=y,Stroke=Brushes.LightSkyBlue,StrokeThickness=stroke,Opacity=.6});
        }
        if(full)Reset();
        else { double dx=(size.Width-oldWidth)/2,dy=(size.Height-oldHeight)/2;
            SetSelection(new Point(previous.X+dx,previous.Y+dy),new Point(previous.X+previous.Width+dx,previous.Y+previous.Height+dy),true); }
    }

    internal static Int32Rect SelectionRect(Point start, Point end, int width, int height)
    {
        var left = (int)Math.Floor(Math.Clamp(Math.Min(start.X, end.X), 0, width));
        var top = (int)Math.Floor(Math.Clamp(Math.Min(start.Y, end.Y), 0, height));
        var right = (int)Math.Ceiling(Math.Clamp(Math.Max(start.X, end.X), 0, width));
        var bottom = (int)Math.Ceiling(Math.Clamp(Math.Max(start.Y, end.Y), 0, height));
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private void SetSelection(Point start, Point end, bool preview)
    {
        Crop = SelectionRect(start, end, (int)_canvas.Width, (int)_canvas.Height);
        Canvas.SetLeft(_selection, Crop.X); Canvas.SetTop(_selection, Crop.Y);
        _selection.Width = Crop.Width; _selection.Height = Crop.Height;
        _dimensions.Text = $"元画像：{_source.PixelWidth} × {_source.PixelHeight} px\n選択範囲：{Crop.Width} × {Crop.Height} px";
        _save.IsEnabled = Crop.Width >= 2 && Crop.Height >= 2
            && (FineAngle!=0 || Crop.Width != _canvas.Width || Crop.Height != _canvas.Height);
        if (preview) RefreshPreview();
    }

    private void RefreshPreview()
    {
        if(Crop.Width<=0||Crop.Height<=0){_preview.Source=null;return;}
        double sx=_working.PixelWidth/_canvas.Width,sy=_working.PixelHeight/_canvas.Height;
        var area=SelectionRect(new Point(Crop.X*sx,Crop.Y*sy),new Point((Crop.X+Crop.Width)*sx,(Crop.Y+Crop.Height)*sy),_working.PixelWidth,_working.PixelHeight);
        _preview.Source=area.Width>0&&area.Height>0?new CroppedBitmap(_working,area):null;
    }

    private void Reset() => SetSelection(new Point(), new Point(_canvas.Width, _canvas.Height), true);
}
