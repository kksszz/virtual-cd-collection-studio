using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed class SpineCardFoldEditorWindow : Window
{
    private readonly BitmapSource _bitmap;
    private readonly Grid _viewport = new() { Background = new SolidColorBrush(Color.FromRgb(18, 21, 26)) };
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent };
    private readonly Thumb _leftGuide = CreateGuide(Color.FromRgb(68, 185, 240));
    private readonly Thumb _rightGuide = CreateGuide(Color.FromRgb(255, 170, 65));
    private readonly TextBlock _status = new() { Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
    private bool _manual;

    public double LeftFold { get; private set; }
    public double RightFold { get; private set; }
    public bool UseAutomatic => !_manual;

    public SpineCardFoldEditorWindow(BitmapSource bitmap, double left, double right, bool manual)
    {
        _bitmap = bitmap;
        LeftFold = left;
        RightFold = right;
        _manual = manual;
        Title = LocalizationService.Select("Spine Card 折り目補正", "Spine Card Fold Adjustment");
        Width = 900; Height = 720; MinWidth = 620; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(24, 28, 34));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = LocalizationService.Select(
                "細い青線の中心＝Back／Spine境界、細い橙線の中心＝Spine／Front境界です。透明な周辺もつかめます。線をドラッグするか、画像上をクリックしてください。",
                "The center of the thin blue line is the Back/Spine boundary; the thin orange line is the Spine/Front boundary. The transparent area around each line is draggable."),
            Margin = new Thickness(2, 0, 2, 12), Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 224)),
            TextWrapping = TextWrapping.Wrap
        });

        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        _viewport.Children.Add(image);
        _overlay.Children.Add(_leftGuide); _overlay.Children.Add(_rightGuide);
        _viewport.Children.Add(_overlay);
        _viewport.SizeChanged += (_, _) => UpdateGuides();
        _leftGuide.DragDelta += (_, e) => MoveGuide(true, e.HorizontalChange);
        _rightGuide.DragDelta += (_, e) => MoveGuide(false, e.HorizontalChange);
        _overlay.MouseLeftButtonDown += (_, e) =>
        {
            var rect = ImageRect();
            var point = e.GetPosition(_overlay);
            if (point.X < rect.Left || point.X > rect.Left + rect.Width
                || point.Y < rect.Top || point.Y > rect.Top + rect.Height) return;
            var position = Math.Clamp((point.X - rect.Left) / rect.Width, .01, .99);
            if (Math.Abs(position - LeftFold) <= Math.Abs(position - RightFold))
                LeftFold = Math.Clamp(position, .01, RightFold - .02);
            else RightFold = Math.Clamp(position, LeftFold + .02, .99);
            _manual = true;
            UpdateGuides(); UpdateStatus();
            e.Handled = true;
        };
        Grid.SetRow(_viewport, 1); root.Children.Add(_viewport);

        var bottom = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(buttons, 1);
        var automatic = Button(LocalizationService.Select("自動検出に戻す", "Use Automatic Detection"));
        automatic.Click += (_, _) => { _manual = false; DialogResult = true; };
        var cancel = Button(LocalizationService.Select("キャンセル", "Cancel"));
        cancel.Click += (_, _) => DialogResult = false;
        var save = Button(LocalizationService.Select("保存", "Save"));
        save.Background = new SolidColorBrush(Color.FromRgb(39, 110, 159));
        save.Click += (_, _) => { _manual = true; DialogResult = true; };
        buttons.Children.Add(automatic); buttons.Children.Add(cancel); buttons.Children.Add(save);
        bottom.Children.Add(buttons); Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        Content = root;
        Loaded += (_, _) => { UpdateGuides(); UpdateStatus(); _leftGuide.Focus(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    private static Button Button(string content) => new()
    {
        Content = content, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4),
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 51, 61)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(73, 82, 96)), Cursor = Cursors.Hand
    };

    private static Thumb CreateGuide(Color color) => new PreciseGuideThumb(color)
    {
        // Keep a forgiving invisible hit area while drawing only a precise
        // two-pixel centre line. The old solid 13 px bar made either edge look
        // like the saved fold position.
        Width = 17, Background = Brushes.Transparent, Cursor = Cursors.SizeWE,
        ToolTip = LocalizationService.Select("細線の中心が保存される境界です", "The centre line is the saved boundary")
    };

    private sealed class PreciseGuideThumb(Color color) : Thumb
    {
        private readonly Brush _brush = new SolidColorBrush(Color.FromArgb(245, color.R, color.G, color.B));
        private readonly Brush _halo = new SolidColorBrush(Color.FromArgb(185, 8, 10, 13));

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            var x = ActualWidth / 2;
            // A dark one-pixel halo keeps the exact coloured line readable on
            // both pale and saturated scans without widening the boundary.
            drawingContext.DrawLine(new Pen(_halo, 4), new Point(x, 0), new Point(x, ActualHeight));
            drawingContext.DrawLine(new Pen(_brush, 2), new Point(x, 0), new Point(x, ActualHeight));
            var marker = new StreamGeometry();
            using (var context = marker.Open())
            {
                context.BeginFigure(new Point(x - 6, 0), true, true);
                context.LineTo(new Point(x + 6, 0), true, false);
                context.LineTo(new Point(x, 9), true, false);
            }
            marker.Freeze();
            drawingContext.DrawGeometry(_brush, new Pen(Brushes.White, 1), marker);
        }
    }

    private (double Left, double Top, double Width, double Height) ImageRect()
    {
        var availableWidth = Math.Max(1, _viewport.ActualWidth - 16);
        var availableHeight = Math.Max(1, _viewport.ActualHeight - 16);
        var scale = Math.Min(availableWidth / _bitmap.PixelWidth, availableHeight / _bitmap.PixelHeight);
        var width = _bitmap.PixelWidth * scale;
        var height = _bitmap.PixelHeight * scale;
        return ((_viewport.ActualWidth - width) / 2, (_viewport.ActualHeight - height) / 2, width, height);
    }

    private void MoveGuide(bool leftGuide, double delta)
    {
        var rect = ImageRect();
        if (rect.Width <= 1) return;
        if (leftGuide) LeftFold = Math.Clamp(LeftFold + delta / rect.Width, .01, RightFold - .02);
        else RightFold = Math.Clamp(RightFold + delta / rect.Width, LeftFold + .02, .99);
        _manual = true;
        UpdateGuides(); UpdateStatus();
    }

    private void UpdateGuides()
    {
        var rect = ImageRect();
        Place(_leftGuide, rect.Left + rect.Width * LeftFold, rect.Top, rect.Height);
        Place(_rightGuide, rect.Left + rect.Width * RightFold, rect.Top, rect.Height);
    }

    private static void Place(FrameworkElement guide, double x, double top, double height)
    {
        Canvas.SetLeft(guide, x - guide.Width / 2); Canvas.SetTop(guide, top); guide.Height = height;
    }

    private void UpdateStatus() => _status.Text = LocalizationService.Select(
        $"{(_manual ? "手動" : "自動")}　Back {LeftFold:P1}　Spine {(RightFold - LeftFold):P1}　Front {(1 - RightFold):P1}",
        $"{(_manual ? "Manual" : "Automatic")}  Back {LeftFold:P1}  Spine {(RightFold - LeftFold):P1}  Front {(1 - RightFold):P1}");
}
