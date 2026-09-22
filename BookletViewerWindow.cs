using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

public sealed record BookletPage(string Name, string Role, Func<BitmapSource> LoadImage);
public sealed record BookletContent(BitmapSource FrontSpread, IReadOnlyList<BookletPage> Pages);

internal sealed class BookletViewerWindow : Window
{
    private readonly BookletContent _booklet;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Grid _pageSurface = new();
    private readonly Border _pageFrame = new();
    private readonly Border _gutter = new();
    private readonly BookletPageTurnViewport _pageTurn = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { Width=220,TextTrimming=TextTrimming.CharacterEllipsis,Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _slideshow;
    private readonly ComboBox _interval = new() { Width=76,Margin=new Thickness(6,3,3,3),VerticalContentAlignment=VerticalAlignment.Center };
    private readonly CheckBox _repeat = new() { Content=LocalizationService.Select("繰り返し","Repeat"),Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,3,8,3) };
    private readonly DispatcherTimer _slideTimer = new(DispatcherPriority.Background);
    private bool _slideshowPlaying;
    private bool _loading;
    private int _index = -1;
    private int _loadGeneration;
    private double _zoom = 1;
    private Point? _dragStart;
    private Point _scrollStart;
    private bool _closed;
    private int _navigationDirection = 1;

    public BookletViewerWindow(string title, BookletContent booklet) : this(title, booklet, 0) { }

    public BookletViewerWindow(string title, BookletContent booklet, int initialPage)
    {
        _booklet = booklet;
        Title = title + " — " + LocalizationService.Select("ジャケット閲覧", "Booklet viewer");
        Width = 1150; Height = 820; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 25));
        var layout = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        Button AddButton(string caption, Action action)
        {
            var button = new Button { Content = caption, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(3),
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 53, 65)) };
            button.Click += (_, _) => action(); toolbar.Children.Add(button); return button;
        }
        _previous = AddButton("◀", () => Navigate(-1));
        _next = AddButton("▶", () => Navigate(1));
        toolbar.Children.Add(_status);
        AddButton("−", () => { StopSlideshow(); SetZoom(_zoom / 1.25); });
        AddButton("＋", () => { StopSlideshow(); SetZoom(_zoom * 1.25); });
        AddButton(LocalizationService.Select("全体表示", "Fit"), () => { StopSlideshow(); SetZoom(1); });
        _slideshow=AddButton(LocalizationService.Select("▶ スライドショー","▶ Slideshow"),ToggleSlideshow);
        _slideshow.IsEnabled=false;
        _slideshow.Width=150;
        _slideshow.ToolTip=LocalizationService.Select("Spaceで再生・一時停止。手動操作で一時停止します。","Space to play/pause. Manual navigation pauses playback.");
        foreach(int seconds in new[]{3,5,10,15,30})_interval.Items.Add(new ComboBoxItem {Content=seconds+LocalizationService.Select("秒"," sec"),Tag=seconds});
        _interval.SelectedIndex=1;_interval.ToolTip=LocalizationService.Select("ページの表示間隔","Page display interval");toolbar.Children.Add(_interval);toolbar.Children.Add(_repeat);
        _interval.SelectionChanged+=(_,_)=>ScheduleSlide();
        _slideTimer.Tick+=async (_,_)=>await AdvanceSlideshowAsync();
        AddButton(LocalizationService.Select("ジャケットを戻す (Esc)", "Insert booklet (Esc)"), Close);
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        _pageSurface.Background = new SolidColorBrush(Color.FromRgb(238, 235, 225));
        _pageSurface.Children.Add(_image);
        _pageSurface.Children.Add(new Border
        {
            Width = 16, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(72, 22, 20, 17), Colors.Transparent, 0)
        });
        _pageSurface.Children.Add(new Border
        {
            Width = 16, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false,
            Background = new LinearGradientBrush(Colors.Transparent, Color.FromArgb(72, 22, 20, 17), 0)
        });
        _gutter.Width = 34; _gutter.HorizontalAlignment = HorizontalAlignment.Center;
        _gutter.IsHitTestVisible = false;
        _gutter.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Colors.Transparent, 0), new(Color.FromArgb(48, 30, 27, 23), .35),
                new(Color.FromArgb(110, 15, 14, 13), .5), new(Color.FromArgb(48, 30, 27, 23), .65),
                new(Colors.Transparent, 1)
            }, 0);
        _pageSurface.Children.Add(_gutter);
        _pageSurface.Children.Add(_pageTurn);
        _pageFrame.Child = _pageSurface;
        _pageFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(196, 190, 176));
        _pageFrame.BorderThickness = new Thickness(1);
        _pageFrame.Margin = new Thickness(28);
        _pageFrame.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 10, Opacity = .55, Color = Colors.Black };
        _scroll.Background = new RadialGradientBrush(Color.FromRgb(49, 55, 63), Color.FromRgb(13, 17, 22));
        _scroll.Content = _pageFrame; layout.Children.Add(_scroll); Content = layout;
        _scroll.SizeChanged += (_, _) => ResizeImage();
        _scroll.PreviewMouseWheel += (_, e) => { StopSlideshow(); SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
        _image.MouseLeftButtonDown += (_, e) =>
        {
            StopSlideshow();
            _dragStart = e.GetPosition(_scroll); _scrollStart = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset);
            _image.CaptureMouse(); _image.Cursor = Cursors.SizeAll; e.Handled = true;
        };
        _image.MouseMove += (_, e) =>
        {
            if (_dragStart is not { } start) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            var delta = e.GetPosition(_scroll) - start;
            _scroll.ScrollToHorizontalOffset(_scrollStart.X - delta.X); _scroll.ScrollToVerticalOffset(_scrollStart.Y - delta.Y);
        };
        _image.MouseLeftButtonUp += (_, _) => EndDrag();
        _image.LostMouseCapture += (_, _) => { _dragStart = null; _image.Cursor = Cursors.Arrow; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Space && !_interval.IsKeyboardFocusWithin && !_repeat.IsKeyboardFocusWithin) ToggleSlideshow();
            else if (e.Key is Key.Right or Key.PageDown) Navigate(1);
            else if (e.Key is Key.Left or Key.PageUp) Navigate(-1);
            else return;
            e.Handled = true;
        };
        Closed += (_, _) => { _closed = true; StopSlideshow(); ++_loadGeneration; EndDrag(); _pageTurn.Stop(); _image.Source = null; };
        Deactivated+=(_,_)=>StopSlideshow();
        Loaded += async (_, _) =>
        {
            _previous.IsEnabled = _next.IsEnabled = false;
            _status.Text = LocalizationService.Select("ジャケットを開いています…", "Opening booklet…");
            // Enter on the requested page (Front for direct opening, inner page after 3D).
            // Previously the full spread was
            // shown first and then used as the old page of a polygon turn,
            // which briefly produced several differently sized overlapping
            // copies during the opening transition.
            if (booklet.Pages.Count > 0)
            {
                _image.Opacity = 0;
                await ShowPageAsync(Math.Clamp(initialPage, 0, booklet.Pages.Count - 1), animateTurn: false);
                if (_closed) return;
                var reveal = new ScaleTransform(.965, .965);
                _image.RenderTransformOrigin = new Point(.5, .5);
                _image.RenderTransform = reveal;
                _image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                reveal.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.965, 1,
                    TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                reveal.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.965, 1,
                    TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else { _image.Source = null; _status.Text = LocalizationService.Select("PAGE・ライナーノーツ画像がありません。画像の用途を設定してください。", "No PAGE or Liner Notes images. Assign artwork roles first."); }
        };
    }

    private void Navigate(int delta)
    {
        StopSlideshow();
        if (_index >= 0 && _index + delta >= 0 && _index + delta < _booklet.Pages.Count)
        {
            _navigationDirection = Math.Sign(delta);
            _ = ShowPageAsync(_index + delta);
        }
    }

    private async Task ShowPageAsync(int index, bool animateTurn = true)
    {
        _slideTimer.Stop();_loading=true;_slideshow.IsEnabled=_slideshowPlaying;
        var generation = ++_loadGeneration;
        _index = index; EndDrag();
        _previous.IsEnabled = index > 0; _next.IsEnabled = index < _booklet.Pages.Count - 1;
        var page = _booklet.Pages[index];
        _status.Text = $"{index + 1} / {_booklet.Pages.Count} — {page.Name}";
        _status.ToolTip=_status.Text;
        try
        {
            var bitmap = await Task.Run(page.LoadImage);
            if (_closed || generation != _loadGeneration) return;
            PresentImage(bitmap, animateTurn, direction: _navigationDirection); SetZoom(1);
            _scroll.ScrollToHorizontalOffset(0); _scroll.ScrollToVerticalOffset(0);
        }
        catch
        {
            if (!_closed && generation == _loadGeneration)
            {
                StopSlideshow();
                _image.Source=null;
                _status.Text += LocalizationService.Select("（画像を読み込めません）", " (Unable to load image)");
            }
        }
        finally
        {
            if(!_closed&&generation==_loadGeneration){_loading=false;_slideshow.IsEnabled=_booklet.Pages.Count>1&&_image.Source is not null;ScheduleSlide();}
        }
    }

    private void ToggleSlideshow()
    {
        if(_slideshowPlaying){StopSlideshow();return;}
        if(_closed||_loading||_booklet.Pages.Count<2||_image.Source is null)return;
        _slideshowPlaying=true;_slideshow.Content=LocalizationService.Select("⏸ 一時停止","⏸ Pause");
        if(_index==_booklet.Pages.Count-1){_navigationDirection=1;_ = ShowPageAsync(0);}
        else ScheduleSlide();
    }
    private void StopSlideshow()
    {
        _slideshowPlaying=false;_slideTimer.Stop();
        _slideshow.Content=LocalizationService.Select("▶ スライドショー","▶ Slideshow");
        _slideshow.IsEnabled=!_closed&&!_loading&&_booklet.Pages.Count>1&&_image.Source is not null;
    }
    private void ScheduleSlide()
    {
        _slideTimer.Stop();
        if(!_slideshowPlaying||_loading||_closed)return;
        int seconds=(_interval.SelectedItem as ComboBoxItem)?.Tag is int value?value:5;
        // Start the dwell only after loading and allow the transition to finish.
        _slideTimer.Interval=TimeSpan.FromSeconds(seconds)+TimeSpan.FromMilliseconds(240);_slideTimer.Start();
    }
    private async Task AdvanceSlideshowAsync()
    {
        _slideTimer.Stop();
        if(!_slideshowPlaying||_loading||_closed)return;
        int next=_index+1;
        if(next>=_booklet.Pages.Count){if(_repeat.IsChecked==true)next=0;else{StopSlideshow();return;}}
        _navigationDirection=1;await ShowPageAsync(next);
    }

    private void SetZoom(double zoom) { _zoom = Math.Clamp(zoom, .25, 8); ResizeImage(); }
    private void PresentImage(BitmapSource bitmap, bool animateTurn, int direction = 1)
    {
        // Resize the page frame for the destination before animating. The old
        // polygon page-turn kept its original portrait/landscape dimensions,
        // so a tall page could briefly protrude beside a wide next page.
        _pageTurn.Stop();
        _pageTurn.Visibility = Visibility.Collapsed;
        _image.Source = bitmap;
        _gutter.Visibility = (double)bitmap.PixelWidth / Math.Max(1, bitmap.PixelHeight) >= 1.28
            ? Visibility.Visible : Visibility.Collapsed;
        ResizeImage();
        if (!animateTurn)
        {
            _image.Opacity = 1;
            _image.RenderTransform = Transform.Identity;
            return;
        }
        var slide = new TranslateTransform(direction >= 0 ? 24 : -24, 0);
        _image.RenderTransform = slide;
        _image.BeginAnimation(OpacityProperty, new DoubleAnimation(.30, 1,
            TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slide.X, 0,
            TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ResizeImage()
    {
        if (_image.Source is not BitmapSource image) return;
        var width = Math.Max(1, _scroll.ActualWidth - 84); var height = Math.Max(1, _scroll.ActualHeight - 84);
        var fit = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
        _image.Width = image.PixelWidth * fit * _zoom; _image.Height = image.PixelHeight * fit * _zoom;
        _pageFrame.Width = _image.Width; _pageFrame.Height = _image.Height;
    }
    private void EndDrag() { _dragStart = null; if (_image.IsMouseCaptured) _image.ReleaseMouseCapture(); _image.Cursor = Cursors.Arrow; }
}
