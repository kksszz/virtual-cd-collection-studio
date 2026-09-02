using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ZipMp3Player;

public sealed record JewelCaseCoverFlowItem(
    string Key,
    string Title,
    string Artist,
    string SourceBadge,
    string TrayColorMode,
    BitmapSource? FrontCover,
    BitmapSource? InsideFrontCover,
    BitmapSource? BackCover,
    BitmapSource? SpineCover,
    BitmapSource? RightSpineCover,
    BitmapSource? InlayCover,
    BitmapSource? DiscImage,
    bool IsPlaying)
{
    public Func<BookletContent>? LoadBooklet { get; init; }
    public BitmapSource? SpineCard { get; init; }
    internal bool CollectionPresentation { get; init; }
    // Inlay is the inside of the rear insert, not the booklet or exterior Back.
    // A standard full scan is 6 + 138 + 6 mm wide by 118 mm high.
    internal (BitmapSource? Panel, BitmapSource? Left, BitmapSource? Right) SplitInlay()
    {
        if (InlayCover is not { } image) return (null, null, null);
        var regions = RearInsertArtwork.GetRegions(image, forceSpines: false);
        return (RearInsertArtwork.Crop(image, regions.Panel),
            regions.Left is { } left ? RearInsertArtwork.Crop(image, left) : null,
            regions.Right is { } right ? RearInsertArtwork.Crop(image, right) : null);
    }
}

public sealed class JewelCaseCoverFlowSelectionChangedEventArgs(JewelCaseCoverFlowItem item) : EventArgs
{
    public JewelCaseCoverFlowItem Item { get; } = item;
}

public sealed class JewelCaseCoverFlow : Grid
{
    private readonly Viewport3D _viewport = new();
    private readonly DxJewelCaseScene? _dxScene;
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _detailText = new();
    private readonly TextBlock _counterText = new();
    private readonly TextBlock _emptyText = new();
    private readonly Button _fullScreenButton = new();
    private readonly Button _caseOpenButton = new();
    private readonly Button _discButton = new();
    private readonly Button _bookletButton = new();
    private readonly Button _spineCardButton = new();
    private bool _isOpeningBooklet;
    private readonly bool _isFullScreen;
    private IReadOnlyList<JewelCaseCoverFlowItem> _items = [];
    private int _selectedIndex = -1;
    private bool _isRotating;
    private Point _rotationStart;
    private double _caseYaw = -10;
    private double _casePitch = -2;
    private double _caseZoom = 1;
    private bool _isPanning;
    private Point _panStart;
    private Vector _casePan;
    private readonly TranslateTransform3D _wpfPan = new();
    private bool _isCaseOpen;
    private bool _isDiscRemoved;
    private bool _isSpineCardRemoved;
    private bool _isCaseTransitioning;
    private bool _isDraggingDisc;
    private bool _isDraggingSpineCard;
    private bool _collectionPresentation;
    private int _selectionMotionDirection;
    private readonly Dictionary<string, ContainerUIElement3D> _collectionModels =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _collectionArtworkRefreshes =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<JewelCaseCoverFlowSelectionChangedEventArgs>? ItemActivated;
    public int ItemCount => _items.Count;
    public string? SelectedKey => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Key : null;
    public BitmapSource? SelectedFrontCover => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].FrontCover : null;
    public bool CollectionPresentation
    {
        get => _collectionPresentation;
        set
        {
            if (_collectionPresentation == value) return;
            _collectionPresentation = value;
            _caseZoom = value ? 0.36 : 1;
            _caseYaw = value ? 30 : -10;
            _casePitch = -2;
            _dxScene?.SetViewZoom(_caseZoom);
            _fullScreenButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _caseOpenButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _discButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _bookletButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            _spineCardButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RebuildScene();
        }
    }

    public JewelCaseCoverFlow() : this(false)
    {
    }

    private JewelCaseCoverFlow(bool isFullScreen)
    {
        _isFullScreen = isFullScreen;
        Focusable = true;
        ClipToBounds = true;
        Background = new LinearGradientBrush(
            Color.FromRgb(11, 15, 21), Color.FromRgb(27, 35, 45), new Point(0.5, 0), new Point(0.5, 1));

        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0.18, 5.4),
            LookDirection = new Vector3D(0, -0.12, -5.4),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 35,
            NearPlaneDistance = 0.1,
            FarPlaneDistance = 100
        };
        Children.Add(_viewport);
        try
        {
            _dxScene = new DxJewelCaseScene();
            _dxScene.Viewport.MouseDoubleClick += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && !_isPanning && !_isDraggingDisc && !_isDraggingSpineCard) RaiseActivated();
            };
            Children.Add(_dxScene.Viewport);
        }
        catch
        {
            // Retain the WPF renderer on older or unsupported graphics hardware.
            _dxScene = null;
        }

        var vignette = new Border
        {
            IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42), RadiusX = 0.78, RadiusY = 0.72,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.35),
                    new GradientStop(Color.FromArgb(190, 0, 0, 0), 1)
                }
            }
        };
        Children.Add(vignette);

        var overlay = new Grid { Margin = new Thickness(8) };
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(overlay);

        _counterText.HorizontalAlignment = HorizontalAlignment.Right;
        _counterText.Foreground = new SolidColorBrush(Color.FromRgb(155, 168, 182));
        _counterText.FontSize = 11;
        _counterText.Margin = new Thickness(0, 2, 4, 0);
        overlay.Children.Add(_counterText);

        _fullScreenButton.Content = isFullScreen ? "✕" : "⛶";
        _fullScreenButton.ToolTip = isFullScreen ? "フルスクリーンを閉じる (Esc / F11)" : "フルスクリーン (F11)";
        _fullScreenButton.Width = 30;
        _fullScreenButton.Height = 25;
        _fullScreenButton.Padding = new Thickness(0);
        _fullScreenButton.Margin = new Thickness(3, 0, 0, 0);
        _fullScreenButton.HorizontalAlignment = HorizontalAlignment.Left;
        _fullScreenButton.VerticalAlignment = VerticalAlignment.Top;
        _fullScreenButton.FontSize = 16;
        _fullScreenButton.Foreground = Brushes.White;
        _fullScreenButton.Background = new SolidColorBrush(Color.FromArgb(150, 24, 32, 41));
        _fullScreenButton.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 88, 102));
        _fullScreenButton.Cursor = Cursors.Hand;
        _fullScreenButton.Click += (_, _) =>
        {
            if (_isFullScreen) Window.GetWindow(this)?.Close();
            else OpenFullScreen();
        };
        overlay.Children.Add(_fullScreenButton);

        _caseOpenButton.Width = 94;
        _caseOpenButton.Height = 25;
        _caseOpenButton.Padding = new Thickness(5, 0, 5, 0);
        _caseOpenButton.Margin = new Thickness(37, 0, 0, 0);
        _caseOpenButton.HorizontalAlignment = HorizontalAlignment.Left;
        _caseOpenButton.VerticalAlignment = VerticalAlignment.Top;
        _caseOpenButton.FontSize = 11;
        _caseOpenButton.Foreground = Brushes.White;
        _caseOpenButton.Background = new SolidColorBrush(Color.FromArgb(150, 24, 32, 41));
        _caseOpenButton.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 88, 102));
        _caseOpenButton.Cursor = Cursors.Hand;
        _caseOpenButton.Visibility = _dxScene is null ? Visibility.Collapsed : Visibility.Visible;
        _caseOpenButton.Click += async (_, _) => await SetCaseOpenAsync(!_isCaseOpen, true);
        UpdateCaseOpenButton();
        overlay.Children.Add(_caseOpenButton);

        _discButton.Width = 112;
        _discButton.Height = 25;
        _discButton.Padding = new Thickness(5, 0, 5, 0);
        _discButton.Margin = new Thickness(134, 0, 0, 0);
        _discButton.HorizontalAlignment = HorizontalAlignment.Left;
        _discButton.VerticalAlignment = VerticalAlignment.Top;
        _discButton.FontSize = 11;
        _discButton.Foreground = Brushes.White;
        _discButton.Background = new SolidColorBrush(Color.FromArgb(150, 24, 32, 41));
        _discButton.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 88, 102));
        _discButton.Cursor = Cursors.Hand;
        _discButton.Visibility = _dxScene is null ? Visibility.Collapsed : Visibility.Visible;
        _discButton.Click += (_, _) =>
        {
            if (_isCaseOpen) SetDiscRemoved(!_isDiscRemoved, true);
        };
        UpdateDiscButton();
        overlay.Children.Add(_discButton);

        _bookletButton.Content = LocalizationService.Select("▤ ジャケットを見る", "▤ View booklet");
        _bookletButton.Width = 145; _bookletButton.Height = 25;
        _bookletButton.Margin = new Thickness(252, 0, 0, 0);
        _bookletButton.HorizontalAlignment = HorizontalAlignment.Left;
        _bookletButton.VerticalAlignment = VerticalAlignment.Top;
        _bookletButton.Foreground = Brushes.White; _bookletButton.Background = _discButton.Background;
        _bookletButton.BorderBrush = _discButton.BorderBrush; _bookletButton.Cursor = Cursors.Hand;
        _bookletButton.ToolTip = LocalizationService.Select("ジャケットを取り出して、Front・PAGE・ライナーノーツ・Front背面を閲覧", "Extract the booklet and browse Front, pages, liner notes and Inside Front");
        _bookletButton.Visibility = Visibility.Collapsed;
        _bookletButton.Click += async (_, _) => await OpenBookletAsync();
        overlay.Children.Add(_bookletButton);

        _spineCardButton.Width = 150;
        _spineCardButton.Height = 25;
        _spineCardButton.Padding = new Thickness(5, 0, 5, 0);
        _spineCardButton.Margin = new Thickness(403, 0, 0, 0);
        _spineCardButton.HorizontalAlignment = HorizontalAlignment.Left;
        _spineCardButton.VerticalAlignment = VerticalAlignment.Top;
        _spineCardButton.FontSize = 11;
        _spineCardButton.Foreground = Brushes.White;
        _spineCardButton.Background = _discButton.Background;
        _spineCardButton.BorderBrush = _discButton.BorderBrush;
        _spineCardButton.Cursor = Cursors.Hand;
        _spineCardButton.Visibility = Visibility.Collapsed;
        _spineCardButton.Click += async (_, _) =>
        {
            if (!_isCaseTransitioning)
                await SetSpineCardRemovedAsync(!_isSpineCardRemoved, true);
        };
        UpdateSpineCardButton();
        overlay.Children.Add(_spineCardButton);

        var previous = CreateNavigationButton("‹", HorizontalAlignment.Left);
        previous.Click += (_, _) => MoveSelection(-1);
        Grid.SetRow(previous, 1);
        overlay.Children.Add(previous);
        var next = CreateNavigationButton("›", HorizontalAlignment.Right);
        next.Click += (_, _) => MoveSelection(1);
        Grid.SetRow(next, 1);
        overlay.Children.Add(next);

        _emptyText.Text = "アルバム画像はありません";
        _emptyText.Foreground = new SolidColorBrush(Color.FromRgb(143, 154, 167));
        _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyText.VerticalAlignment = VerticalAlignment.Center;
        _emptyText.Visibility = Visibility.Collapsed;
        Grid.SetRow(_emptyText, 1);
        overlay.Children.Add(_emptyText);

        var information = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 13, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(57, 72, 88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(28, 0, 28, 2)
        };
        var textPanel = new StackPanel();
        _titleText.Foreground = Brushes.White;
        _titleText.FontWeight = FontWeights.SemiBold;
        _titleText.FontSize = 14;
        _titleText.TextAlignment = TextAlignment.Center;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.Foreground = new SolidColorBrush(Color.FromRgb(166, 178, 191));
        _detailText.FontSize = 11;
        _detailText.Margin = new Thickness(0, 2, 0, 0);
        _detailText.TextAlignment = TextAlignment.Center;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        textPanel.Children.Add(_titleText);
        textPanel.Children.Add(_detailText);
        information.Child = textPanel;
        Grid.SetRow(information, 2);
        overlay.Children.Add(information);

        MouseWheel += (_, e) =>
        {
            if (_isFullScreen)
                AdjustZoom(e.Delta);
            else
                MoveSelection(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };
        PreviewKeyDown += OnPreviewKeyDown;
        ToolTip = LocalizationService.Select(
            "左ドラッグ: 回転（取り出したCD・帯の上では個別に移動） ／ 中央ボタンドラッグ: 全体を移動 ／ R: 位置を戻す",
            "Left drag: rotate (drag an extracted CD or obi to move it) / Middle drag: move all / R: reset position");
        PreviewMouseDown += OnDiscDragStarted;
        PreviewMouseUp += OnDiscDragEnded;
        MouseMove += OnDiscDragMoved;
        PreviewMouseDown += OnPanStarted;
        PreviewMouseUp += OnPanEnded;
        MouseMove += OnPanMoved;
        LostMouseCapture += (_, e) =>
        {
            // A child viewport/button can lose capture when we acquire it.
            // Only cancel when this control itself has actually lost capture.
            if (ReferenceEquals(e.OriginalSource, this) && !IsMouseCaptured) EndPointerDrag();
        };
        Unloaded += (_, _) => EndPointerDrag();
        MouseLeftButtonDown += OnRotationStarted;
        MouseMove += OnRotationMoved;
        MouseLeftButtonUp += OnRotationEnded;
        MouseLeave += OnRotationEnded;
    }

    public void SetItems(IReadOnlyList<JewelCaseCoverFlowItem> items, string? selectedKey = null)
    {
        var previousKey = SelectedKey;
        if (_collectionPresentation && _collectionModels.Count > 0)
        {
            var nextItems = items.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var oldItem in _items)
            {
                if (!nextItems.TryGetValue(oldItem.Key, out var nextItem) || SameExteriorArtwork(oldItem, nextItem)) continue;
                if (_collectionModels.Remove(oldItem.Key, out var stale))
                {
                    _viewport.Children.Remove(stale);
                    // A nearby/selected cover is replaced after its larger
                    // bitmap finishes decoding. Keep the replacement at the
                    // current flow slot instead of flying it in from the edge.
                    _collectionArtworkRefreshes.Add(oldItem.Key);
                }
            }
        }
        _items = items;
        _selectedIndex = selectedKey is null ? -1 : FindIndex(selectedKey);
        if (_selectedIndex < 0 && _items.Count > 0) _selectedIndex = 0;
        if (!string.Equals(previousKey, SelectedKey, StringComparison.OrdinalIgnoreCase)) ResetCaseRotation();
        RebuildScene();
    }

    private static bool SameExteriorArtwork(JewelCaseCoverFlowItem left, JewelCaseCoverFlowItem right) =>
        ReferenceEquals(left.FrontCover, right.FrontCover)
        && ReferenceEquals(left.BackCover, right.BackCover)
        && ReferenceEquals(left.SpineCover, right.SpineCover)
        && ReferenceEquals(left.RightSpineCover, right.RightSpineCover)
        && ReferenceEquals(left.InlayCover, right.InlayCover)
        && string.Equals(left.TrayColorMode, right.TrayColorMode, StringComparison.OrdinalIgnoreCase);

    public void SelectByKey(string key, bool notify = false)
    {
        var index = FindIndex(key);
        if (index < 0 || index == _selectedIndex) return;
        _selectionMotionDirection = Math.Sign(index - _selectedIndex);
        _selectedIndex = index;
        ResetCaseRotation();
        RebuildScene();
        if (notify) RaiseSelectionChanged();
    }

    private int FindIndex(string key)
    {
        for (var index = 0; index < _items.Count; index++)
            if (string.Equals(_items[index].Key, key, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private void MoveSelection(int offset)
    {
        if (_items.Count == 0) return;
        var current = _selectedIndex < 0 ? 0 : _selectedIndex;
        var next = (current + offset) % _items.Count;
        if (next < 0) next += _items.Count;
        if (next == _selectedIndex) return;
        _selectionMotionDirection = Math.Sign(offset);
        _selectedIndex = next;
        ResetCaseRotation();
        RebuildScene();
        RaiseSelectionChanged();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.R:
                ResetCasePosition();
                e.Handled = true;
                break;
            case Key.Left: MoveSelection(-1); e.Handled = true; break;
            case Key.Right: MoveSelection(1); e.Handled = true; break;
            case Key.Home: SelectIndex(0, true); e.Handled = true; break;
            case Key.End: SelectIndex(_items.Count - 1, true); e.Handled = true; break;
            case Key.F11:
                if (_isFullScreen) Window.GetWindow(this)?.Close();
                else OpenFullScreen();
                e.Handled = true;
                break;
            case Key.Escape when _isFullScreen:
                Window.GetWindow(this)?.Close();
                e.Handled = true;
                break;
            case Key.C when _selectedIndex >= 0:
                _ = SetCaseOpenAsync(!_isCaseOpen, true);
                e.Handled = true;
                break;
            case Key.D when _selectedIndex >= 0 && _isCaseOpen:
                SetDiscRemoved(!_isDiscRemoved, true);
                e.Handled = true;
                break;
            case Key.S when _selectedIndex >= 0 && HasSelectedSpineCard() && !_isCaseOpen:
                _ = SetSpineCardRemovedAsync(!_isSpineCardRemoved, true);
                e.Handled = true;
                break;
            case Key.Enter when _selectedIndex >= 0: RaiseActivated(); e.Handled = true; break;
        }
    }

    private void OpenFullScreen()
    {
        if (_items.Count == 0) return;
        var fullScreenFlow = new JewelCaseCoverFlow(true);
        fullScreenFlow.SetItems(_items, SelectedKey);
        fullScreenFlow.ApplySpineCardRemoved(_isSpineCardRemoved, false);
        fullScreenFlow.SetCaseOpen(_isCaseOpen, false);
        fullScreenFlow.SetDiscRemoved(_isDiscRemoved, false);
        fullScreenFlow.SelectionChanged += (_, e) =>
        {
            SelectByKey(e.Item.Key, true);
            // The owner reloads the newly selected cover at full resolution.
            // Refresh this full-screen clone after that queued reload completes.
            fullScreenFlow.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                fullScreenFlow.SetItems(_items, e.Item.Key)));
        };
        fullScreenFlow.ItemActivated += (_, e) =>
            ItemActivated?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(e.Item));

        var window = new Window
        {
            Title = "CoverFlow",
            Owner = Window.GetWindow(this),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            Background = Brushes.Black,
            Content = fullScreenFlow
        };
        window.ShowDialog();
        if (fullScreenFlow.SelectedKey is { } selectedKey)
            SelectByKey(selectedKey, true);
        Focus();
    }

    public static void ShowItemFullScreen(JewelCaseCoverFlowItem item, Window? owner = null)
    {
        var fullScreenFlow = new JewelCaseCoverFlow(true);
        fullScreenFlow.SetItems([item], item.Key);

        var window = new Window
        {
            Title = item.Title,
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            Background = Brushes.Black,
            Content = fullScreenFlow
        };
        window.ContentRendered += (_, _) => fullScreenFlow.Focus();
        window.ShowDialog();
    }

    private void SelectIndex(int index, bool notify)
    {
        if (index < 0 || index >= _items.Count || index == _selectedIndex) return;
        _selectionMotionDirection = Math.Sign(index - _selectedIndex);
        _selectedIndex = index;
        ResetCaseRotation();
        RebuildScene();
        if (notify) RaiseSelectionChanged();
    }

    private void RebuildScene()
    {
        if (!_collectionPresentation || _viewport.Children.Count == 0)
        {
            _viewport.Children.Clear();
            if (!_collectionPresentation)
            {
                _collectionModels.Clear();
                _collectionArtworkRefreshes.Clear();
            }
            var lightGroup = new Model3DGroup();
            lightGroup.Children.Add(new AmbientLight(Color.FromRgb(116, 126, 140)));
            lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(255, 255, 255), new Vector3D(-0.35, -0.55, -1)));
            lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(112, 164, 208), new Vector3D(0.7, 0.15, -0.5)));
            _viewport.Children.Add(new ModelVisual3D { Content = lightGroup });
        }

        var hasItems = _items.Count > 0 && _selectedIndex >= 0;
        if (_dxScene is not null) _dxScene.Viewport.Visibility = hasItems && !_collectionPresentation
            ? Visibility.Visible : Visibility.Collapsed;
        _bookletButton.Visibility = !_collectionPresentation && hasItems && _items[_selectedIndex].LoadBooklet is not null
            ? Visibility.Visible : Visibility.Collapsed;
        var hasSpineCard = hasItems && _items[_selectedIndex].SpineCard is not null;
        _spineCardButton.Visibility = !_collectionPresentation && _dxScene is not null && hasSpineCard
            ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSpineCard) _isSpineCardRemoved = false;
        UpdateSpineCardButton();
        _emptyText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        _counterText.Text = hasItems ? $"{_selectedIndex + 1} / {_items.Count}" : "0 / 0";
        _titleText.Text = hasItems ? _items[_selectedIndex].Title : "";
        _detailText.Text = hasItems
            ? $"{(_items[_selectedIndex].IsPlaying ? "▶ " : "")}{_items[_selectedIndex].Artist}  •  {_items[_selectedIndex].SourceBadge}"
            : "";
        ApplyCollectionBackground(hasItems ? _items[_selectedIndex].FrontCover : null);
        if (!hasItems)
        {
            foreach (var model in _collectionModels.Values) _viewport.Children.Remove(model);
            _collectionModels.Clear();
            _collectionArtworkRefreshes.Clear();
            return;
        }

        if (_collectionPresentation)
        {
            UpdateCollectionScene();
            return;
        }

        _dxScene?.SetItem(_items[_selectedIndex], _caseYaw, _casePitch);
        _dxScene?.SetSpineCardRemoved(_isSpineCardRemoved, false);

        const int visibleRadius = 5;
        var start = Math.Max(0, _selectedIndex - visibleRadius);
        var end = Math.Min(_items.Count - 1, _selectedIndex + visibleRadius);
        var ordered = Enumerable.Range(start, end - start + 1)
            .OrderByDescending(index => Math.Abs(index - _selectedIndex));
        foreach (var index in ordered)
        {
            var relative = index - _selectedIndex;
            if (relative == 0 && _dxScene is not null) continue;
            var model = CreateCaseModel(_items[index], relative, _caseYaw, _casePitch, _caseZoom);
            if (relative == 0) ((Transform3DGroup)model.Transform).Children.Add(_wpfPan);
            model.MouseLeftButtonDown += (_, e) =>
            {
                Focus();
                if (_selectedIndex != index)
                {
                    _selectionMotionDirection = Math.Sign(index - _selectedIndex);
                    _selectedIndex = index;
                    ResetCaseRotation();
                    RebuildScene();
                    RaiseSelectionChanged();
                }
                else if (e.ClickCount >= 2) RaiseActivated();
                e.Handled = true;
            };
            _viewport.Children.Add(model);
        }
        _viewport.RenderTransform = Transform.Identity;
        _viewport.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(150)));
    }

    private readonly record struct CollectionPose(double X, double Y, double Z, double Scale, double Yaw, double Pitch);

    private CollectionPose GetCollectionPose(int relative)
    {
        var selected = relative == 0;
        var distance = Math.Abs(relative);
        return new CollectionPose(
            selected ? 0 : Math.Sign(relative) * (0.86 + (distance - 1) * 0.28),
            selected ? 0.12 : 0.02,
            selected ? 0.62 : -0.16 - (distance - 1) * 0.10,
            selected ? _caseZoom : Math.Max(0.21, 0.38 - (distance - 1) * 0.014),
            // Keep enough of both the booklet and the physical side face in
            // view. At 72 degrees a correctly mapped 6 mm spine dominates
            // while the 120 mm front becomes an unreadable sliver.
            selected ? _caseYaw : relative < 0 ? -55 : 55,
            selected ? _casePitch : 0);
    }

    private void UpdateCollectionScene()
    {
        const int radius = 12;
        var initialLayout = _collectionModels.Count == 0;
        var start = Math.Max(0, _selectedIndex - radius);
        var end = Math.Min(_items.Count - 1, _selectedIndex + radius);
        var targetKeys = Enumerable.Range(start, end - start + 1)
            .Select(index => _items[index].Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var index in Enumerable.Range(start, end - start + 1)
            .OrderByDescending(index => Math.Abs(index - _selectedIndex)))
        {
            var item = _items[index];
            var relative = index - _selectedIndex;
            if (!_collectionModels.TryGetValue(item.Key, out var model))
            {
                var artworkRefresh = _collectionArtworkRefreshes.Remove(item.Key);
                var entryRelative = initialLayout || artworkRefresh ? relative : Math.Sign(relative == 0
                    ? (_selectionMotionDirection == 0 ? 1 : _selectionMotionDirection)
                    : relative) * (radius + 2);
                model = CreateCaseModel(item, entryRelative, _caseYaw, _casePitch, _caseZoom);
                var key = item.Key;
                model.MouseLeftButtonDown += (_, e) =>
                {
                    Focus();
                    var targetIndex = FindIndex(key);
                    if (targetIndex >= 0 && targetIndex != _selectedIndex)
                    {
                        _selectionMotionDirection = Math.Sign(targetIndex - _selectedIndex);
                        _selectedIndex = targetIndex;
                        ResetCaseRotation();
                        RebuildScene();
                        RaiseSelectionChanged();
                        e.Handled = true;
                    }
                    else if (targetIndex >= 0 && e.ClickCount >= 2)
                    {
                        RaiseActivated();
                        e.Handled = true;
                    }
                    // A single press on the selected centre case bubbles to
                    // OnRotationStarted, allowing direct left-drag rotation.
                };
                _collectionModels[item.Key] = model;
                _viewport.Children.Add(model);
                if (artworkRefresh) SetCollectionPose(model, GetCollectionPose(relative));
            }
            if (initialLayout) SetCollectionPose(model, GetCollectionPose(relative));
            else AnimateCollectionPose(model, GetCollectionPose(relative));
        }

        foreach (var pair in _collectionModels.Where(pair => !targetKeys.Contains(pair.Key)).ToList())
        {
            var transforms = (Transform3DGroup)pair.Value.Transform;
            var translation = (TranslateTransform3D)transforms.Children[3];
            var direction = Math.Sign(translation.OffsetX);
            if (direction == 0) direction = _selectionMotionDirection == 0 ? 1 : -_selectionMotionDirection;
            var exitPose = GetCollectionPose(direction * (radius + 2));
            AnimateCollectionPose(pair.Value, exitPose, () =>
            {
                if (IsCollectionKeyVisible(pair.Key)) return;
                if (_collectionModels.Remove(pair.Key, out var stale)) _viewport.Children.Remove(stale);
            });
        }
        _selectionMotionDirection = 0;
    }

    private bool IsCollectionKeyVisible(string key)
    {
        var index = FindIndex(key);
        return index >= 0 && Math.Abs(index - _selectedIndex) <= 12;
    }

    private static void SetCollectionPose(ContainerUIElement3D model, CollectionPose pose)
    {
        var transforms = (Transform3DGroup)model.Transform;
        var scale = (ScaleTransform3D)transforms.Children[0];
        var pitch = (AxisAngleRotation3D)((RotateTransform3D)transforms.Children[1]).Rotation;
        var yaw = (AxisAngleRotation3D)((RotateTransform3D)transforms.Children[2]).Rotation;
        var translation = (TranslateTransform3D)transforms.Children[3];
        scale.ScaleX = scale.ScaleY = scale.ScaleZ = pose.Scale;
        pitch.Angle = pose.Pitch;
        yaw.Angle = pose.Yaw;
        translation.OffsetX = pose.X;
        translation.OffsetY = pose.Y;
        translation.OffsetZ = pose.Z;
    }

    private static void AnimateCollectionPose(ContainerUIElement3D model, CollectionPose pose, Action? completed = null)
    {
        var transforms = (Transform3DGroup)model.Transform;
        var scale = (ScaleTransform3D)transforms.Children[0];
        var pitch = (AxisAngleRotation3D)((RotateTransform3D)transforms.Children[1]).Rotation;
        var yaw = (AxisAngleRotation3D)((RotateTransform3D)transforms.Children[2]).Rotation;
        var translation = (TranslateTransform3D)transforms.Children[3];
        // EaseOut preserves momentum when the user presses repeatedly: each
        // new destination starts immediately from the currently rendered pose
        // and then settles gently instead of pausing at every album.
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        const int durationMilliseconds = 560;
        DoubleAnimation Animation(double current, double target) => new(current, target,
            TimeSpan.FromMilliseconds(durationMilliseconds)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };

        void AnimateScale(DependencyProperty property, double target)
        {
            var current = (double)scale.GetValue(property);
            scale.BeginAnimation(property, null);
            scale.SetValue(property, target);
            scale.BeginAnimation(property, Animation(current, target));
        }
        void AnimateRotation(AxisAngleRotation3D rotation, double target)
        {
            var current = rotation.Angle;
            rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            rotation.Angle = target;
            rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, Animation(current, target));
        }
        void AnimateTranslation(DependencyProperty property, double target, bool signalsCompletion = false)
        {
            var current = (double)translation.GetValue(property);
            translation.BeginAnimation(property, null);
            translation.SetValue(property, target);
            var animation = Animation(current, target);
            if (signalsCompletion && completed is not null) animation.Completed += (_, _) => completed();
            translation.BeginAnimation(property, animation);
        }

        AnimateScale(ScaleTransform3D.ScaleXProperty, pose.Scale);
        AnimateScale(ScaleTransform3D.ScaleYProperty, pose.Scale);
        AnimateScale(ScaleTransform3D.ScaleZProperty, pose.Scale);
        AnimateRotation(pitch, pose.Pitch);
        AnimateRotation(yaw, pose.Yaw);
        AnimateTranslation(TranslateTransform3D.OffsetYProperty, pose.Y);
        AnimateTranslation(TranslateTransform3D.OffsetZProperty, pose.Z);
        AnimateTranslation(TranslateTransform3D.OffsetXProperty, pose.X, true);
    }

    private void ApplyCollectionBackground(BitmapSource? cover)
    {
        if (!_collectionPresentation || cover is null)
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(11, 15, 21), Color.FromRgb(27, 35, 45), new Point(0.5, 0), new Point(0.5, 1));
            if (_dxScene is not null) _dxScene.Viewport.BackgroundColor = Color.FromRgb(11, 15, 21);
            return;
        }
        try
        {
            const double sampleSize = 24;
            var scale = Math.Min(sampleSize / cover.PixelWidth, sampleSize / cover.PixelHeight);
            var scaled = new TransformedBitmap(cover, new ScaleTransform(scale, scale));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            long red = 0, green = 0, blue = 0;
            for (var index = 0; index < pixels.Length; index += 4)
            {
                blue += pixels[index]; green += pixels[index + 1]; red += pixels[index + 2];
            }
            var count = Math.Max(1, converted.PixelWidth * converted.PixelHeight);
            byte Tone(long total, double strength, int floor) =>
                (byte)Math.Clamp(floor + total / (double)count * strength, 0, 255);
            var top = Color.FromRgb(Tone(red, .25, 14), Tone(green, .22, 16), Tone(blue, .27, 22));
            var bottom = Color.FromRgb(Tone(red, .09, 7), Tone(green, .08, 10), Tone(blue, .11, 15));
            Background = new RadialGradientBrush(top, bottom)
            {
                Center = new Point(.5, .34), GradientOrigin = new Point(.5, .34), RadiusX = .82, RadiusY = .78
            };
            if (_dxScene is not null) _dxScene.Viewport.BackgroundColor = top;
        }
        catch
        {
            if (_dxScene is not null) _dxScene.Viewport.BackgroundColor = Color.FromRgb(11, 15, 21);
        }
    }

    private static ContainerUIElement3D CreateCaseModel(JewelCaseCoverFlowItem item, int relative,
        double selectedYaw, double selectedPitch, double selectedZoom)
    {
        // A standard jewel case is wider than its square booklet.  Keeping the
        // case and booklet proportions separate makes the object read as a CD
        // case even when it is seen almost edge-on.
        const double width = 2.42;
        const double height = 2.12;
        const double depth = 0.18;
        var collectionPresentation = item.CollectionPresentation;
        var selected = relative == 0;
        var distance = Math.Abs(relative);
        var x = selected ? 0 : Math.Sign(relative) * (collectionPresentation
            ? 0.82 + (distance - 1) * 0.22
            : 1.46 + (distance - 1) * 0.58);
        var y = selected ? 0.12 : collectionPresentation ? 0.02 : -0.04;
        var z = selected ? 0.62 : collectionPresentation
            ? -0.16 - (distance - 1) * 0.10
            : -0.45 - (distance - 1) * 0.24;
        var scale = selected ? selectedZoom : collectionPresentation
            ? Math.Max(0.24, 0.46 - (distance - 1) * 0.018)
            : Math.Max(0.62, 0.78 - (distance - 1) * 0.035);
        var angle = selected ? selectedYaw : relative < 0
            ? collectionPresentation ? -72 : 67
            : collectionPresentation ? 72 : -67;

        var group = new Model3DGroup();
        var frame = CreateMaterial(selected
            ? Color.FromArgb(112, 210, 232, 246)
            : Color.FromArgb(92, 174, 191, 205), 90);
        var frameEdge = CreateMaterial(selected
            ? Color.FromArgb(188, 198, 222, 238)
            : Color.FromArgb(160, 135, 151, 166), 105);
        var trayColor = item.TrayColorMode switch
        {
            "White" => Color.FromRgb(220, 218, 207),
            "Black" => Color.FromRgb(18, 20, 24),
            "Gray" => Color.FromRgb(90, 94, 98),
            "Clear" => Color.FromArgb(44, 224, 232, 236),
            _ => Color.FromRgb(29, 32, 37)
        };
        if (collectionPresentation)
            return CreateCollectionExteriorCaseModel(item, selected, x, y, z, scale, angle,
                selected ? selectedPitch : 0, trayColor);

        var dark = CreateMaterial(trayColor, 28);
        var back = CreateOptionalArtworkMaterial(item.BackCover, item.Title, 0.92);
        // Source Back is viewed from the opposite side of the model.
        var spine = CreateOptionalArtworkMaterial(item.RightSpineCover, item.Title, 0.96);
        var rightSpine = CreateOptionalArtworkMaterial(item.SpineCover, item.Title, 0.96);

        // Clear acrylic shell, the darker hinge rail and the two hinge knuckles.
        group.Children.Add(CreateBeveledBox(width, 0.052, depth, 0.022,
            new Point3D(0, height / 2 - 0.026, 0), frameEdge));
        group.Children.Add(CreateBeveledBox(width, 0.064, depth, 0.024,
            new Point3D(0, -height / 2 + 0.032, 0), frameEdge));
        group.Children.Add(CreateBeveledBox(0.052, height, depth, 0.020,
            new Point3D(-width / 2 + 0.026, 0, 0), frameEdge));
        group.Children.Add(CreateBeveledBox(0.047, height, depth, 0.018,
            new Point3D(width / 2 - 0.024, 0, 0), frameEdge));
        group.Children.Add(CreateBeveledBox(0.145, height - 0.13, depth * 0.82, 0.025,
            new Point3D(-width / 2 + 0.16, 0, -0.006), dark));
        group.Children.Add(CreateBeveledBox(0.105, 0.34, depth + 0.035, 0.025,
            new Point3D(-width / 2 + 0.085, height * 0.29, 0), frameEdge));
        group.Children.Add(CreateBeveledBox(0.105, 0.34, depth + 0.035, 0.025,
            new Point3D(-width / 2 + 0.085, -height * 0.29, 0), frameEdge));

        group.Children.Add(CreateQuad(
            new Point3D(width / 2 - 0.065, height / 2 - 0.075, -depth / 2 - 0.002),
            new Point3D(-width / 2 + 0.065, height / 2 - 0.075, -depth / 2 - 0.002),
            new Point3D(-width / 2 + 0.065, -height / 2 + 0.075, -depth / 2 - 0.002),
            new Point3D(width / 2 - 0.065, -height / 2 + 0.075, -depth / 2 - 0.002), back));
        group.Children.Add(CreateQuad(
            new Point3D(-width / 2 - 0.003, height / 2 - 0.07, -depth / 2 + 0.008),
            new Point3D(-width / 2 - 0.003, height / 2 - 0.07, depth / 2 - 0.008),
            new Point3D(-width / 2 - 0.003, -height / 2 + 0.07, depth / 2 - 0.008),
            new Point3D(-width / 2 - 0.003, -height / 2 + 0.07, -depth / 2 + 0.008), spine));
        group.Children.Add(CreateQuad(
            new Point3D(width / 2 + 0.003, height / 2 - 0.07, depth / 2 - 0.008),
            new Point3D(width / 2 + 0.003, height / 2 - 0.07, -depth / 2 + 0.008),
            new Point3D(width / 2 + 0.003, -height / 2 + 0.07, -depth / 2 + 0.008),
            new Point3D(width / 2 + 0.003, -height / 2 + 0.07, depth / 2 - 0.008), rightSpine));

        var inlay = item.SplitInlay();
        var insideBack = CreateOptionalArtworkMaterial(inlay.Panel, item.Title, 0.92);
        var insideLeft = CreateOptionalArtworkMaterial(inlay.Left, item.Title, 0.96);
        var insideRight = CreateOptionalArtworkMaterial(inlay.Right, item.Title, 0.96);
        void AddInside(Point3D topLeft, Point3D topRight, Point3D bottomRight, Point3D bottomLeft, Material material)
        {
            var face = CreateQuad(topLeft, topRight, bottomRight, bottomLeft, material);
            ((MeshGeometry3D)face.Geometry).TriangleIndices = [0, 2, 1, 0, 3, 2];
            face.BackMaterial = null;
            group.Children.Add(face);
        }
        AddInside(
            new Point3D(-width / 2 + 0.065, height / 2 - 0.075, -depth / 2 + 0.002),
            new Point3D(width / 2 - 0.065, height / 2 - 0.075, -depth / 2 + 0.002),
            new Point3D(width / 2 - 0.065, -height / 2 + 0.075, -depth / 2 + 0.002),
            new Point3D(-width / 2 + 0.065, -height / 2 + 0.075, -depth / 2 + 0.002), insideBack);
        AddInside(
            new Point3D(-width / 2 - 0.002, height / 2 - 0.07, depth / 2 - 0.008),
            new Point3D(-width / 2 - 0.002, height / 2 - 0.07, -depth / 2 + 0.008),
            new Point3D(-width / 2 - 0.002, -height / 2 + 0.07, -depth / 2 + 0.008),
            new Point3D(-width / 2 - 0.002, -height / 2 + 0.07, depth / 2 - 0.008), insideLeft);
        AddInside(
            new Point3D(width / 2 + 0.002, height / 2 - 0.07, -depth / 2 + 0.008),
            new Point3D(width / 2 + 0.002, height / 2 - 0.07, depth / 2 - 0.008),
            new Point3D(width / 2 + 0.002, -height / 2 + 0.07, depth / 2 - 0.008),
            new Point3D(width / 2 + 0.002, -height / 2 + 0.07, -depth / 2 + 0.008), insideRight);
        // The fallback renderer also keeps the insert behind the removable tray.
        group.Children.Add(CreateBox(width - 0.13, height - 0.15, 0.006,
            new Point3D(0, 0, -depth / 2 + 0.012), dark));

        for (var rib = 0; rib < 8; rib++)
        {
            var ribX = -0.72 + rib * 0.205;
            group.Children.Add(CreateBox(0.018, 0.060, 0.030,
                new Point3D(ribX, -height / 2 + 0.063, depth / 2 + 0.023), frameEdge));
        }

        // Small lid catches seen along the opening edge of a real jewel case.
        group.Children.Add(CreateBox(0.22, 0.035, 0.038,
            new Point3D(0.52, -height / 2 + 0.055, depth / 2 + 0.026), frameEdge));
        group.Children.Add(CreateBox(0.22, 0.035, 0.038,
            new Point3D(-0.28, -height / 2 + 0.055, depth / 2 + 0.026), frameEdge));

        if (item.FrontCover is not null)
        {
            var reflection = CreateImageMaterial(item.FrontCover, item.Title, selected ? 0.13 : 0.07, reflected: true);
            group.Children.Add(CreateQuad(
                new Point3D(-width / 2 + 0.23, -height / 2 - 0.08, depth / 2),
                new Point3D(width / 2 - 0.06, -height / 2 - 0.08, depth / 2),
                new Point3D(width / 2 - 0.06, -height / 2 - 1.00, depth / 2),
                new Point3D(-width / 2 + 0.23, -height / 2 - 1.00, depth / 2), reflection, reflected: true));
        }

        // Use a regular textured 3D material for collection artwork.  A
        // Viewport2DVisual3D is useful for interactive single-case surfaces,
        // but WPF can leave those visual-host planes white when many cases are
        // composed in one Viewport3D (and when the viewport is rendered to a
        // bitmap).  A frozen BitmapSource-backed ImageBrush renders reliably
        // for every CoverFlow entry.
        group.Children.Add(CreateQuad(
            new Point3D(-width / 2 + 0.235, height / 2 - 0.075, depth / 2 + 0.006),
            new Point3D(width / 2 - 0.065, height / 2 - 0.075, depth / 2 + 0.006),
            new Point3D(width / 2 - 0.065, -height / 2 + 0.075, depth / 2 + 0.006),
            new Point3D(-width / 2 + 0.235, -height / 2 + 0.075, depth / 2 + 0.006),
            CreateImageMaterial(item.FrontCover, item.Title, 1.0)));

        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(scale, scale, scale));
        transforms.Children.Add(new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(1, 0, 0), selected ? selectedPitch : 0)));
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), angle)));
        transforms.Children.Add(new TranslateTransform3D(x, y, z));
        var container = new ContainerUIElement3D { Transform = transforms };
        container.Children.Add(new ModelUIElement3D { Model = group });

        // A very light transparent lid over the artwork provides the acrylic
        // highlight without washing out the cover image.
        var lid = CreateMaterial(Color.FromArgb(selected ? (byte)28 : (byte)18, 226, 241, 250), 120);
        group.Children.Add(CreateQuad(
            new Point3D(-width / 2 + 0.055, height / 2 - 0.052, depth / 2 + 0.012),
            new Point3D(width / 2 - 0.052, height / 2 - 0.052, depth / 2 + 0.012),
            new Point3D(width / 2 - 0.052, -height / 2 + 0.052, depth / 2 + 0.012),
            new Point3D(-width / 2 + 0.055, -height / 2 + 0.052, depth / 2 + 0.012), lid));

        // Restrained diagonal sheen: it moves with the model and makes the lid
        // read as a separate transparent acrylic surface.
        var sheenBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30),
                new GradientStop(Color.FromArgb(selected ? (byte)42 : (byte)24, 255, 255, 255), 0.48),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.64)
            }
        };
        group.Children.Add(CreateQuad(
            new Point3D(-width / 2 + 0.07, height / 2 - 0.055, depth / 2 + 0.015),
            new Point3D(width / 2 - 0.055, height / 2 - 0.055, depth / 2 + 0.015),
            new Point3D(width / 2 - 0.055, -height / 2 + 0.055, depth / 2 + 0.015),
            new Point3D(-width / 2 + 0.07, -height / 2 + 0.055, depth / 2 + 0.015),
            new DiffuseMaterial(sheenBrush)));
        if (item.SpineCard is { } spineCard)
        {
            var (obiBack, obiSpine, obiFront) = SpineCardArtwork.Split(spineCard);
            var wrappedSpineWidth = depth + 0.040;
            var horizontalScale = wrappedSpineWidth / Math.Max(1, obiSpine.PixelWidth);
            var backWidth = horizontalScale * obiBack.PixelWidth;
            var frontWidth = horizontalScale * obiFront.PixelWidth;
            const double obiHeightMm = 120;
            const double caseHeightMm = 125;
            var cardHeight = height * obiHeightMm / caseHeightMm;
            var outsideX = -width / 2 - 0.014;
            var foldX = outsideX;
            group.Children.Add(CreateQuad(
                new Point3D(foldX, cardHeight / 2, -depth / 2 - 0.020),
                new Point3D(foldX + backWidth, cardHeight / 2, -depth / 2 - 0.020),
                new Point3D(foldX + backWidth, -cardHeight / 2, -depth / 2 - 0.020),
                new Point3D(foldX, -cardHeight / 2, -depth / 2 - 0.020),
                CreateImageMaterial(obiBack, item.Title, 0.97), reflected: true));
            group.Children.Add(CreateQuad(
                new Point3D(foldX, cardHeight / 2, depth / 2 + 0.020),
                new Point3D(foldX + frontWidth, cardHeight / 2, depth / 2 + 0.020),
                new Point3D(foldX + frontWidth, -cardHeight / 2, depth / 2 + 0.020),
                new Point3D(foldX, -cardHeight / 2, depth / 2 + 0.020),
                CreateImageMaterial(obiFront, item.Title, 0.97)));
            group.Children.Add(CreateQuad(
                new Point3D(outsideX, cardHeight / 2, depth / 2 + 0.020),
                new Point3D(outsideX, cardHeight / 2, -depth / 2 - 0.020),
                new Point3D(outsideX, -cardHeight / 2, -depth / 2 - 0.020),
                new Point3D(outsideX, -cardHeight / 2, depth / 2 + 0.020),
                CreateImageMaterial(obiSpine, item.Title, 0.97)));
        }
        return container;
    }

    private static ContainerUIElement3D CreateCollectionExteriorCaseModel(JewelCaseCoverFlowItem item,
        bool selected, double x, double y, double z, double scale, double yaw, double pitch, Color trayColor)
    {
        const double width = 2.42;
        const double height = 2.12;
        const double depth = 0.125;
        var shell = DxJewelCaseScene.GetCoverFlowShellGeometry();
        var group = new Model3DGroup();
        var inlay = item.SplitInlay();
        var backArtwork = item.BackCover ?? inlay.Panel;
        var worldLeftSpineArtwork = SelectExteriorSpine(item.RightSpineCover, inlay.Left, rightEdge: true);
        var worldRightSpineArtwork = SelectExteriorSpine(item.SpineCover, inlay.Right, rightEdge: false);

        // The collection view shares the exact normalized STL shell used by
        // the interactive DirectX viewer.  Only the closed exterior is kept:
        // no disc, booklet, hub, opening hierarchy or draggable parts.
        var tray = CreateMaterial(trayColor, 30);
        var acrylic = CreateMaterial(Color.FromArgb(28, 196, 216, 226), 105);
        var mouldedAcrylic = CreateMaterial(
            Color.FromArgb(selected ? (byte)122 : (byte)104, 155, 174, 186), 125);
        AddShell(shell.BottomTray, tray);
        AddShell(shell.BottomPerimeter, acrylic);
        AddShell(shell.BottomMouldedEdges, mouldedAcrylic);
        AddShell(shell.TopLid, acrylic);
        AddShell(shell.TopMouldedEdges, mouldedAcrylic);

        const double caseWidthMm = 142;
        const double caseHeightMm = 125;
        var unitX = width / caseWidthMm;
        var unitY = height / caseHeightMm;
        var frontCenterX = (shell.FrontLeft + shell.FrontRight) / 2;
        var frontCenterY = (shell.FrontBottom + shell.FrontTop) / 2;
        var frontWidth = 120 * unitX;
        var frontHeight = 120 * unitY;
        var frontLeft = Math.Max(0.044 - 1.018, -width / 2 + 0.254);
        var frontRight = frontCenterX + frontWidth / 2;
        var frontBottom = frontCenterY - frontHeight / 2;
        var frontTop = frontCenterY + frontHeight / 2;
        group.Children.Add(CreateQuad(
            // WPF Viewport3D does not provide the weighted order-independent
            // transparency used by the DirectX viewer. Put the print at the
            // closed lid's outer surface to prevent the transparent STL shell
            // from sorting behind the opaque tray and hiding the artwork.
            new Point3D(frontLeft, frontTop, depth / 2 + 0.002),
            new Point3D(frontRight, frontTop, depth / 2 + 0.002),
            new Point3D(frontRight, frontBottom, depth / 2 + 0.002),
            new Point3D(frontLeft, frontBottom, depth / 2 + 0.002),
            CreateImageMaterial(item.FrontCover, item.Title, 1.0)));

        var backWidth = 138 * unitX;
        var backHeight = 118 * unitY;
        var backMaterial = CreateOptionalArtworkMaterial(backArtwork, item.Title, 0.98);
        group.Children.Add(CreateQuad(
            new Point3D(backWidth / 2, backHeight / 2, -depth / 2 - 0.001),
            new Point3D(-backWidth / 2, backHeight / 2, -depth / 2 - 0.001),
            new Point3D(-backWidth / 2, -backHeight / 2, -depth / 2 - 0.001),
            new Point3D(backWidth / 2, -backHeight / 2, -depth / 2 - 0.001), backMaterial));

        // Back source-right folds around the world-left edge.  Keep both
        // physical spine faces independently textured just like the 3D viewer.
        if (worldLeftSpineArtwork is not null)
            group.Children.Add(CreateQuad(
                // Match DxJewelCaseScene.AddSpine exactly: the paper sits just
                // beneath the clear side wall and stops inside the front/back
                // acrylic lips. Extending beyond those lips makes it read as
                // an obi wrapped over the outside of the case.
                new Point3D(-width / 2 - 0.001, backHeight / 2, -depth / 2 + 0.010),
                new Point3D(-width / 2 - 0.001, backHeight / 2, depth / 2 - 0.010),
                new Point3D(-width / 2 - 0.001, -backHeight / 2, depth / 2 - 0.010),
                new Point3D(-width / 2 - 0.001, -backHeight / 2, -depth / 2 + 0.010),
                CreateImageMaterial(worldLeftSpineArtwork, item.Title, 0.99)));
        if (worldRightSpineArtwork is not null)
            group.Children.Add(CreateQuad(
                new Point3D(width / 2 + 0.001, backHeight / 2, depth / 2 - 0.010),
                new Point3D(width / 2 + 0.001, backHeight / 2, -depth / 2 + 0.010),
                new Point3D(width / 2 + 0.001, -backHeight / 2, -depth / 2 + 0.010),
                new Point3D(width / 2 + 0.001, -backHeight / 2, depth / 2 - 0.010),
                CreateImageMaterial(worldRightSpineArtwork, item.Title, 0.99)));

        if (item.FrontCover is not null)
        {
            group.Children.Add(CreateQuad(
                new Point3D(frontLeft, -height / 2 - 0.08, depth / 2),
                new Point3D(frontRight, -height / 2 - 0.08, depth / 2),
                new Point3D(frontRight, -height / 2 - 1.00, depth / 2),
                new Point3D(frontLeft, -height / 2 - 1.00, depth / 2),
                CreateImageMaterial(item.FrontCover, item.Title, selected ? 0.13 : 0.07, reflected: true),
                reflected: true));
        }

        var transforms = new Transform3DGroup();
        transforms.Children.Add(new ScaleTransform3D(scale, scale, scale));
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), pitch)));
        transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yaw)));
        transforms.Children.Add(new TranslateTransform3D(x, y, z));
        var container = new ContainerUIElement3D { Transform = transforms };
        container.Children.Add(new ModelUIElement3D { Model = group });
        return container;

        void AddShell(MeshGeometry3D geometry, Material material) =>
            group.Children.Add(new GeometryModel3D(geometry, material) { BackMaterial = material });
    }

    private static BitmapSource? SelectExteriorSpine(BitmapSource? assigned, BitmapSource? inlayFallback,
        bool rightEdge)
    {
        if (assigned is null) return inlayFallback;
        var aspect = assigned.PixelHeight > 0 ? (double)assigned.PixelWidth / assigned.PixelHeight : 0;
        // A prepared jewel-case spine is approximately 6 x 118 mm. Never
        // squeeze a square cover or disc image into the narrow side face.
        if (aspect is > 0 and <= 0.22) return assigned;
        // A complete 6+138+6 mm rear insert is also valid, but its fold must
        // be extracted before it is mapped to the case side.
        if (aspect is >= 1.12 and <= 1.58)
        {
            var regions = RearInsertArtwork.GetRegions(assigned, forceSpines: true);
            var region = rightEdge ? regions.Right : regions.Left;
            if (region is not null) return RearInsertArtwork.Crop(assigned, region.Value);
        }
        return inlayFallback;
    }

    private async Task OpenBookletAsync()
    {
        if (_isOpeningBooklet || _selectedIndex < 0 || _items[_selectedIndex].LoadBooklet is not { } loader) return;
        var key = SelectedKey;
        var title = _items[_selectedIndex].Title;
        _isOpeningBooklet = true; IsEnabled = false; EndPointerDrag();
        try
        {
            var booklet = await Task.Run(loader);
            if (!IsLoaded || key != SelectedKey) return;
            if (!_isCaseOpen)
            {
                if (!await SetCaseOpenAsync(true, true)) return;
            }
            if (!IsLoaded || key != SelectedKey) return;
            if (_dxScene is not null && !await _dxScene.AnimateBookletAsync(true)) return;
            if (!IsLoaded || key != SelectedKey) return;
            var viewer = new BookletViewerWindow(title, booklet) { Owner = Window.GetWindow(this) };
            if (_isFullScreen) viewer.WindowState = WindowState.Maximized;
            viewer.ShowDialog();
        }
        catch (Exception ex)
        {
            if (IsLoaded) MessageBox.Show(Window.GetWindow(this), ex.Message,
                LocalizationService.Select("ジャケットを表示できません", "Unable to open booklet"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            // Close the reader first so the user sees the real booklet aligned
            // with the empty lid, then slid back under its retaining tabs.
            if (_dxScene is not null && IsLoaded && key == SelectedKey)
                await _dxScene.AnimateBookletAsync(false);
            _dxScene?.SetBookletRemoved(false, false);
            _isOpeningBooklet = false; IsEnabled = true;
            if (IsLoaded) Focus();
        }
    }

    private void OnDiscDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _dxScene is null || _isPanning
            || (!_isDiscRemoved && !_isSpineCardRemoved)) return;
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != this;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ButtonBase) return;
        var position = e.GetPosition(_dxScene.Viewport);
        if (TryBeginDiscDrag(position) || TryBeginSpineCardDrag(position)) e.Handled = true;
    }

    private bool TryBeginDiscDrag(Point position)
    {
        if (!_isDiscRemoved || _dxScene is null || _isPanning || !_dxScene.BeginDiscDrag(position)) return false;
        Focus();
        _isRotating = false;
        _isDraggingDisc = CaptureMouse();
        if (_isDraggingDisc) Cursor = Cursors.SizeAll;
        else _dxScene.EndDiscDrag();
        return _isDraggingDisc;
    }

    private bool TryBeginSpineCardDrag(Point position)
    {
        if (!_isSpineCardRemoved || _dxScene is null || _isPanning || !_dxScene.BeginSpineCardDrag(position)) return false;
        Focus();
        _isRotating = false;
        _isDraggingSpineCard = CaptureMouse();
        if (_isDraggingSpineCard) Cursor = Cursors.SizeAll;
        else _dxScene.EndSpineCardDrag();
        return _isDraggingSpineCard;
    }

    private void OnDiscDragMoved(object sender, MouseEventArgs e)
    {
        if ((!_isDraggingDisc && !_isDraggingSpineCard) || _dxScene is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPointerDrag(); return; }
        var position = e.GetPosition(_dxScene.Viewport);
        if (_isDraggingDisc) _dxScene.DragDiscTo(position);
        else _dxScene.DragSpineCardTo(position);
        e.Handled = true;
    }

    private void OnDiscDragEnded(object sender, MouseButtonEventArgs e)
    {
        if ((!_isDraggingDisc && !_isDraggingSpineCard) || e.ChangedButton != MouseButton.Left) return;
        EndPointerDrag();
        e.Handled = true;
    }

    private void OnPanStarted(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _selectedIndex < 0) return;
        // Leave toolbar and navigation buttons alone, even when clicking their text.
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != this;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ButtonBase) return;
        Focus();
        EndPointerDrag();
        _panStart = e.GetPosition(this);
        _isPanning = CaptureMouse();
        if (_isPanning) Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnPanMoved(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        if (e.MiddleButton != MouseButtonState.Pressed) { EndPointerDrag(); return; }
        var current = e.GetPosition(this);
        PanBy(current - _panStart);
        _panStart = current;
        e.Handled = true;
    }

    private void PanBy(Vector delta)
    {
        if (_selectedIndex < 0 || ActualWidth <= 0) return;
        // Convert screen-space motion at the case depth. Apply translation after
        // rotation/scale so dragging remains horizontal/vertical at every angle.
        var distance = _dxScene is null ? 5.4 - 0.62 : 5.25 - 0.46;
        var fieldOfView = _dxScene is null ? 35d : 34d;
        var unitsPerPixel = 2 * distance * Math.Tan(fieldOfView * Math.PI / 360) / ActualWidth;
        _casePan += new Vector(delta.X * unitsPerPixel, -delta.Y * unitsPerPixel);
        ApplyCasePosition();
    }

    private void ApplyCasePosition()
    {
        _wpfPan.OffsetX = _casePan.X;
        _wpfPan.OffsetY = _casePan.Y;
        _dxScene?.SetViewPan(_casePan.X, _casePan.Y);
    }

    private void ResetCasePosition()
    {
        EndPointerDrag();
        _casePan = new Vector();
        ApplyCasePosition();
    }

    private void OnPanEnded(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle) return;
        EndPointerDrag();
        e.Handled = true;
    }

    private void EndPointerDrag()
    {
        _isDraggingDisc = false;
        _isDraggingSpineCard = false;
        _dxScene?.EndDiscDrag();
        _dxScene?.EndSpineCardDrag();
        _isPanning = false;
        _isRotating = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void OnRotationStarted(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning || _isDraggingDisc || _isDraggingSpineCard) return;
        Focus();
        if (_selectedIndex < 0) return;
        _isRotating = true;
        _rotationStart = e.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void OnRotationMoved(object sender, MouseEventArgs e)
    {
        if (!_isRotating || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        var delta = current - _rotationStart;
        if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5) return;
        _caseYaw = NormalizeAngle(_caseYaw + delta.X * 0.65);
        _casePitch = NormalizeAngle(_casePitch - delta.Y * 0.35);
        _rotationStart = current;
        if (_collectionPresentation && SelectedKey is { } selectedKey
            && _collectionModels.TryGetValue(selectedKey, out var selectedModel))
            SetCollectionPose(selectedModel, GetCollectionPose(0));
        else if (_dxScene is not null)
            _dxScene.SetRotation(_caseYaw, _casePitch);
        else
            RebuildScene();
        e.Handled = true;
    }

    private void OnRotationEnded(object? sender, MouseEventArgs e)
    {
        if (!_isRotating) return;
        _isRotating = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void AdjustZoom(int wheelDelta)
    {
        if (_selectedIndex < 0 || wheelDelta == 0) return;
        var notches = wheelDelta / 120d;
        _caseZoom = Math.Clamp(_caseZoom * Math.Pow(1.12, notches), 0.55, 2.40);
        if (_dxScene is not null)
            _dxScene.SetViewZoom(_caseZoom);
        else
            RebuildScene();
    }

    private void ResetCaseRotation()
    {
        ResetCasePosition();
        // The collection's selected case remains mostly frontal, but is
        // angled enough to reveal the same printed spine seen in 3D View.
        // Turn the selected case toward the viewer while retaining the sign
        // of its incoming side, avoiding a cross-centre 110-degree flip.
        _caseYaw = _collectionPresentation
            ? (_selectionMotionDirection < 0 ? -30 : 30)
            : -10;
        _casePitch = -2;
        _caseZoom = _collectionPresentation ? 0.36 : 1;
        _dxScene?.SetViewZoom(_caseZoom);
        SetCaseOpen(false, false);
        ApplySpineCardRemoved(false, false);
    }

    private bool HasSelectedSpineCard() =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count && _items[_selectedIndex].SpineCard is not null;

    private async Task<bool> SetCaseOpenAsync(bool open, bool animate)
    {
        if (_isCaseTransitioning) return false;
        _isCaseTransitioning = true;
        UpdateCaseOpenButton();
        UpdateDiscButton();
        UpdateSpineCardButton();
        var key = SelectedKey;
        try
        {
            // A folded obi physically locks the lid to the back of the case.
            // Remove it completely before beginning the hinge animation.
            if (open && HasSelectedSpineCard() && !_isSpineCardRemoved
                && !await SetSpineCardRemovedAsync(true, animate))
                return false;
            if (key != SelectedKey) return false;
            SetCaseOpen(open, animate);
            if (animate) await WaitOnDispatcherAsync(1200);
            return key == SelectedKey;
        }
        finally
        {
            _isCaseTransitioning = false;
            UpdateCaseOpenButton();
            UpdateDiscButton();
            UpdateSpineCardButton();
        }
    }

    private Task WaitOnDispatcherAsync(int milliseconds)
    {
        var completion = new TaskCompletionSource<bool>();
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds)
        };
        timer.Tick += Complete;
        timer.Start();
        return completion.Task;

        void Complete(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= Complete;
            completion.TrySetResult(true);
        }
    }

    private void SetCaseOpen(bool open, bool animate)
    {
        // Keep the invariant for non-animated state restoration and tests too.
        if (open && HasSelectedSpineCard() && !_isSpineCardRemoved)
            ApplySpineCardRemoved(true, false);
        if (!open && _isDiscRemoved)
            SetDiscRemoved(false, false);
        _isCaseOpen = open;
        _dxScene?.SetCaseOpen(open, animate);
        UpdateCaseOpenButton();
        UpdateDiscButton();
    }

    private void ApplySpineCardRemoved(bool removed, bool animate)
    {
        if (!HasSelectedSpineCard()) removed = false;
        _isSpineCardRemoved = removed;
        _dxScene?.SetSpineCardRemoved(removed, animate);
        UpdateSpineCardButton();
    }

    private async Task<bool> SetSpineCardRemovedAsync(bool removed, bool animate)
    {
        if (!HasSelectedSpineCard() || (!removed && _isCaseOpen)) return false;
        _isSpineCardRemoved = removed;
        UpdateSpineCardButton();
        if (_dxScene is null || !animate)
        {
            _dxScene?.SetSpineCardRemoved(removed, false);
            return true;
        }
        var completed = await _dxScene.AnimateSpineCardAsync(removed);
        UpdateSpineCardButton();
        return completed;
    }

    private void SetDiscRemoved(bool removed, bool animate)
    {
        if (!_isCaseOpen && removed) return;
        EndPointerDrag();
        _isDiscRemoved = removed;
        _dxScene?.SetDiscRemoved(removed, animate);
        UpdateDiscButton();
    }

    private void UpdateCaseOpenButton()
    {
        _caseOpenButton.IsEnabled = !_isCaseTransitioning;
        _caseOpenButton.Content = _isCaseOpen
            ? LocalizationService.Select("▰ ケースを閉じる", "▰ Close case")
            : LocalizationService.Select("▱ ケースを開く", "▱ Open case");
        _caseOpenButton.ToolTip = LocalizationService.Select(
            _isCaseOpen ? "CDケースを閉じる (C)" : "CDケースを開いて盤面を表示 (C)",
            _isCaseOpen ? "Close the CD case (C)" : "Open the CD case to view the disc (C)");
    }

    private void UpdateDiscButton()
    {
        _discButton.IsEnabled = _isCaseOpen && !_isCaseTransitioning;
        _discButton.Opacity = _discButton.IsEnabled ? 1 : 0.48;
        _discButton.Content = _isDiscRemoved
            ? LocalizationService.Select("◉ CDを戻す", "◉ Insert CD")
            : LocalizationService.Select("◎ CDを取り出す", "◎ Remove CD");
        _discButton.ToolTip = LocalizationService.Select(
            _isCaseOpen ? "CDを取り出す／戻す (D)。取り出したCDは左ドラッグで移動できます" : "先にケースを開いてください",
            _isCaseOpen ? "Remove/insert the CD (D). Left-drag an extracted CD to move it" : "Open the case first");
    }

    private void UpdateSpineCardButton()
    {
        var canInsert = !_isCaseOpen;
        _spineCardButton.IsEnabled = HasSelectedSpineCard() && !_isCaseTransitioning && canInsert;
        _spineCardButton.Opacity = _spineCardButton.IsEnabled ? 1 : 0.48;
        _spineCardButton.Content = _isSpineCardRemoved
            ? LocalizationService.Select("▥ Spine（帯）を戻す", "▥ Insert obi")
            : LocalizationService.Select("▤ Spine（帯）を外す", "▤ Remove obi");
        _spineCardButton.ToolTip = LocalizationService.Select(
            _isCaseOpen ? "帯は左ドラッグで移動できます。戻すには先にケースを閉じてください" : "Spine（帯）を横へスライドして外す／戻す (S)。外した帯は左ドラッグで移動できます",
            _isCaseOpen ? "Left-drag the obi to move it. Close the case before inserting it" : "Slide the spine card (obi) sideways to remove/insert it (S). Left-drag it after removal");
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    private static Viewport2DVisual3D CreateArtworkPlane(Point3D topLeft, Point3D topRight,
        Point3D bottomRight, Point3D bottomLeft, BitmapSource? image, string title)
    {
        var hostMaterial = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(hostMaterial, true);
        var artwork = new Grid
        {
            Width = 320,
            Height = 320
        };
        if (image is not null)
            // The quad already has the artwork's physical proportions. Crop-to-fill
            // would turn a 6 x 118 mm spine into a square viewbox and discard most
            // of its height before the texture is mapped onto the narrow face.
            artwork.Background = new ImageBrush(image) { Stretch = Stretch.Fill };
        else
        {
            artwork.Background = new LinearGradientBrush(Color.FromRgb(52, 67, 85), Color.FromRgb(16, 21, 29),
                new Point(0, 0), new Point(1, 1));
            artwork.Children.Add(new TextBlock
            {
                Text = "♪", Foreground = new SolidColorBrush(Color.FromRgb(137, 156, 176)),
                FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
        }
        artwork.ToolTip = title;
        var geometry = CreateQuadMesh(topLeft, topRight, bottomRight, bottomLeft, false);
        geometry.TriangleIndices = [0, 2, 1, 0, 3, 2];
        return new Viewport2DVisual3D
        {
            Geometry = geometry,
            Material = hostMaterial,
            Visual = artwork
        };
    }

    private static GeometryModel3D CreateBox(double width, double height, double depth, Point3D center, Material material)
    {
        var x0 = center.X - width / 2; var x1 = center.X + width / 2;
        var y0 = center.Y - height / 2; var y1 = center.Y + height / 2;
        var z0 = center.Z - depth / 2; var z1 = center.Z + depth / 2;
        var group = new Model3DGroup
        {
            Children =
            {
                CreateQuad(new Point3D(x0,y1,z1), new Point3D(x1,y1,z1), new Point3D(x1,y0,z1), new Point3D(x0,y0,z1), material),
                CreateQuad(new Point3D(x1,y1,z0), new Point3D(x0,y1,z0), new Point3D(x0,y0,z0), new Point3D(x1,y0,z0), material),
                CreateQuad(new Point3D(x0,y1,z0), new Point3D(x0,y1,z1), new Point3D(x0,y0,z1), new Point3D(x0,y0,z0), material),
                CreateQuad(new Point3D(x1,y1,z1), new Point3D(x1,y1,z0), new Point3D(x1,y0,z0), new Point3D(x1,y0,z1), material),
                CreateQuad(new Point3D(x0,y1,z0), new Point3D(x1,y1,z0), new Point3D(x1,y1,z1), new Point3D(x0,y1,z1), material),
                CreateQuad(new Point3D(x0,y0,z1), new Point3D(x1,y0,z1), new Point3D(x1,y0,z0), new Point3D(x0,y0,z0), material)
            }
        };
        return Flatten(group);
    }

    private static GeometryModel3D CreateBeveledBox(double width, double height, double depth,
        double bevel, Point3D center, Material material)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var halfDepth = depth / 2;
        var corner = Math.Min(bevel, Math.Min(halfWidth, halfHeight) * 0.48);
        Point[] outline =
        [
            new(-halfWidth + corner, halfHeight), new(halfWidth - corner, halfHeight),
            new(halfWidth, halfHeight - corner), new(halfWidth, -halfHeight + corner),
            new(halfWidth - corner, -halfHeight), new(-halfWidth + corner, -halfHeight),
            new(-halfWidth, -halfHeight + corner), new(-halfWidth, halfHeight - corner)
        ];
        var mesh = new MeshGeometry3D();

        // Separate vertices per face retain crisp manufactured edges while the
        // small chamfers catch the specular light.
        AddFace([new Point3D(0, 0, halfDepth), .. outline.Select(p => new Point3D(p.X, p.Y, halfDepth))], true);
        AddFace([new Point3D(0, 0, -halfDepth), .. outline.Reverse().Select(p => new Point3D(p.X, p.Y, -halfDepth))], true);
        for (var index = 0; index < outline.Length; index++)
        {
            var next = (index + 1) % outline.Length;
            AddQuad(new Point3D(outline[index].X, outline[index].Y, halfDepth),
                new Point3D(outline[next].X, outline[next].Y, halfDepth),
                new Point3D(outline[next].X, outline[next].Y, -halfDepth),
                new Point3D(outline[index].X, outline[index].Y, -halfDepth));
        }

        var translated = new MeshGeometry3D
        {
            TriangleIndices = new Int32Collection(mesh.TriangleIndices),
            TextureCoordinates = new PointCollection(mesh.TextureCoordinates)
        };
        foreach (var point in mesh.Positions)
            translated.Positions.Add(new Point3D(point.X + center.X, point.Y + center.Y, point.Z + center.Z));
        return new GeometryModel3D(translated, material) { BackMaterial = material };

        void AddFace(IReadOnlyList<Point3D> points, bool fan)
        {
            var offset = mesh.Positions.Count;
            foreach (var point in points)
            {
                mesh.Positions.Add(point);
                mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
            }
            if (!fan) return;
            for (var index = 1; index < points.Count - 1; index++)
            {
                mesh.TriangleIndices.Add(offset);
                mesh.TriangleIndices.Add(offset + index);
                mesh.TriangleIndices.Add(offset + index + 1);
            }
        }

        void AddQuad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var offset = mesh.Positions.Count;
            foreach (var point in new[] { a, b, c, d })
            {
                mesh.Positions.Add(point);
                mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
            }
            foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(offset + index);
        }
    }

    private static GeometryModel3D CreateDisc(BitmapSource image, Point3D center, double radius)
    {
        const int segments = 48;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(center);
        mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
        for (var index = 0; index <= segments; index++)
        {
            var angle = Math.PI * 2 * index / segments;
            mesh.Positions.Add(new Point3D(center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius, center.Z));
            mesh.TextureCoordinates.Add(new Point(0.5 + Math.Cos(angle) * 0.5, 0.5 - Math.Sin(angle) * 0.5));
        }
        for (var index = 0; index < segments; index++)
        {
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(index + 2);
            mesh.TriangleIndices.Add(index + 1);
        }
        var material = CreateImageMaterial(image, "Disc", 0.96);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D Flatten(Model3DGroup source)
    {
        var mesh = new MeshGeometry3D();
        Material? material = null;
        foreach (var child in source.Children.OfType<GeometryModel3D>())
        {
            if (child.Geometry is not MeshGeometry3D childMesh) continue;
            material ??= child.Material;
            var offset = mesh.Positions.Count;
            foreach (var point in childMesh.Positions) mesh.Positions.Add(point);
            foreach (var coordinate in childMesh.TextureCoordinates) mesh.TextureCoordinates.Add(coordinate);
            foreach (var triangle in childMesh.TriangleIndices) mesh.TriangleIndices.Add(offset + triangle);
        }
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D CreateQuad(Point3D topLeft, Point3D topRight, Point3D bottomRight, Point3D bottomLeft,
        Material material, bool reflected = false)
    {
        var mesh = CreateQuadMesh(topLeft, topRight, bottomRight, bottomLeft, reflected);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static MeshGeometry3D CreateQuadMesh(Point3D topLeft, Point3D topRight,
        Point3D bottomRight, Point3D bottomLeft, bool reflected)
    {
        return new MeshGeometry3D
        {
            Positions = [topLeft, topRight, bottomRight, bottomLeft],
            TriangleIndices = [0, 1, 2, 0, 2, 3],
            TextureCoordinates = reflected
                ? [new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0)]
                : [new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)]
        };
    }

    private static Material CreateMaterial(Color color, double specularPower)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(145, 255, 255, 255)), specularPower));
        return group;
    }

    private static Material CreateOptionalArtworkMaterial(BitmapSource? image, string title, double opacity)
        => image is null
            ? CreateMaterial(Color.FromRgb(225, 223, 215), 10)
            : CreateImageMaterial(image, title, opacity);

    private static Material CreateImageMaterial(BitmapSource? image, string title, double opacity, bool reflected = false)
    {
        Brush brush;
        if (image is not null)
        {
            // UVs cover the complete 3D face, whose geometry supplies the final
            // aspect ratio. Preserve the complete bitmap here; UniformToFill
            // centrally cropped tall spine scans and left only an abstract stripe.
            var imageBrush = new ImageBrush(image) { Stretch = Stretch.Fill, Opacity = opacity };
            RenderOptions.SetBitmapScalingMode(imageBrush, BitmapScalingMode.HighQuality);
            brush = imageBrush;
        }
        else
        {
            var seed = title.Aggregate(17, (value, character) => unchecked(value * 31 + character));
            var first = Color.FromRgb((byte)(48 + Math.Abs(seed % 70)), (byte)(60 + Math.Abs(seed / 7 % 80)), (byte)(82 + Math.Abs(seed / 13 % 90)));
            brush = new LinearGradientBrush(first, Color.FromRgb(17, 22, 30), new Point(0, 0), new Point(1, 1)) { Opacity = opacity };
        }
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        var emissiveBrush = brush.CloneCurrentValue();
        emissiveBrush.Opacity *= reflected ? 0.25 : 0.72;
        group.Children.Add(new EmissiveMaterial(emissiveBrush));
        if (!reflected) group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), 45));
        return group;
    }

    private static Button CreateNavigationButton(string content, HorizontalAlignment alignment) => new()
    {
        Content = content,
        Width = 30,
        Height = 54,
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 26,
        FontWeight = FontWeights.Light,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromArgb(130, 28, 37, 47)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(67, 83, 99)),
        Cursor = Cursors.Hand
    };

    private void RaiseSelectionChanged()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            SelectionChanged?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(_items[_selectedIndex]));
    }

    private void RaiseActivated()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
            ItemActivated?.Invoke(this, new JewelCaseCoverFlowSelectionChangedEventArgs(_items[_selectedIndex]));
    }
}
