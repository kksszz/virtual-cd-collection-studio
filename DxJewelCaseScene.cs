using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using DxMesh = HelixToolkit.SharpDX.MeshGeometry3D;
using DxMaterial = HelixToolkit.Wpf.SharpDX.Material;
using DxPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using MediaColor = System.Windows.Media.Color;
using DxCullMode = SharpDX.Direct3D11.CullMode;

namespace ZipMp3Player;

/// <summary>
/// DirectX 11 renderer for the selected jewel case.  The model is original,
/// procedural geometry; no LaunchBox model or texture is redistributed.
/// </summary>
internal sealed class DxJewelCaseScene : IDisposable
{
    // The printable parts are exported in their open, print-bed orientation:
    // the bottom hinge is on the left while the top hinge is on the right.
    // After mirroring the top into its assembled orientation, both hinge
    // barrels meet slightly inboard of the outer left edge.
    private const double AssembledHingeX = -1.085;
    private static readonly Lazy<StlCaseGeometry> CaseGeometry = new(LoadStlCaseGeometry);
    private readonly DefaultEffectsManager _effects = new();
    private readonly GroupModel3D _caseRoot = new();
    private readonly GroupModel3D _baseRoot = new();
    private readonly GroupModel3D _lidRoot = new();
    private readonly GroupModel3D _spineCardRoot = new();
    private readonly TranslateTransform3D _spineCardTranslation = new();
    private readonly TranslateTransform3D _spineCardOpenTranslation = new();
    private readonly TranslateTransform3D _spineCardDragTranslation = new();
    private readonly DispatcherTimer _spineCardMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private TaskCompletionSource<bool>? _spineCardMotionCompletion;
    private EventHandler? _spineCardMotionTick;
    private double _spineCardProgress;
    private double _spineCardRemovedOffsetX = -1;
    private const double SpineCardRemovedOffsetY = -0.08;
    private const double SpineCardRemovedOffsetZ = -0.16;
    private const double SpineCardOpenClearanceX = -2.35;
    private Point3D? _spineCardDragPoint;
    private Vector3D _spineCardDragNormal;
    private readonly GroupModel3D _bookletRoot = new();
    private readonly TranslateTransform3D _bookletTranslation = new();
    private readonly AxisAngleRotation3D _bookletTilt = new(new Vector3D(0, 1, 0), 0);
    private readonly RotateTransform3D _bookletLift;
    private readonly DispatcherTimer _bookletMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private TaskCompletionSource<bool>? _bookletMotionCompletion;
    private double _bookletProgress;
    private readonly GroupModel3D _discRoot = new();
    private readonly Dictionary<BitmapSource, TextureModel> _textureCache =
        new(ReferenceEqualityComparer.Instance);
    private readonly AxisAngleRotation3D _pitchRotation = new(new Vector3D(1, 0, 0), -2);
    private readonly AxisAngleRotation3D _yawRotation = new(new Vector3D(0, 1, 0), -10);
    private readonly AxisAngleRotation3D _lidHingeRotation = new(new Vector3D(0, 1, 0), 0);
    private readonly TranslateTransform3D _openCenterTranslation = new();
    private readonly ScaleTransform3D _openScale = new(1, 1, 1);
    private readonly ScaleTransform3D _viewZoom = new(1, 1, 1);
    private readonly TranslateTransform3D _viewPan = new();
    private readonly TranslateTransform3D _discTranslation = new();
    private readonly AxisAngleRotation3D _discTiltRotation = new(new Vector3D(0, 1, 0), 0);
    // Once removed, keep the complete tilted disc above the highest case/tray
    // surface. The value includes the projected radius, thickness and a gap.
    private const double RemovedDiscMinimumZ = 0.35;
    private readonly Transform3DGroup _caseTransform = new();
    private readonly DispatcherTimer _animationRenderTimer;
    private int _caseAnimationGeneration;
    private int _discAnimationGeneration;
    private bool _discRemoved;
    private Point3D? _discDragPoint;
    private Vector3D _discDragNormal;
    private readonly DynamicReflectionMap3D _reflection = new()
    {
        Size = 256,
        NearField = 0.08,
        FarField = 20,
        IsDynamicScene = false
    };

    public Viewport3DX Viewport { get; }

    public DxJewelCaseScene()
    {
        _caseTransform.Children.Add(_openCenterTranslation);
        _caseTransform.Children.Add(_openScale);
        _caseTransform.Children.Add(_viewZoom);
        _caseTransform.Children.Add(new RotateTransform3D(_pitchRotation));
        _caseTransform.Children.Add(new RotateTransform3D(_yawRotation));
        _caseTransform.Children.Add(new TranslateTransform3D(0, 0.12, 0.46));
        _caseTransform.Children.Add(_viewPan);
        _caseRoot.Transform = _caseTransform;
        _lidRoot.Transform = new RotateTransform3D(_lidHingeRotation, new Point3D(AssembledHingeX, 0, 0));
        _bookletLift = new RotateTransform3D(_bookletTilt);
        var bookletTransform = new Transform3DGroup();
        bookletTransform.Children.Add(_bookletLift);
        bookletTransform.Children.Add(_bookletTranslation);
        _bookletRoot.Transform = bookletTransform;
        var discTransform = new Transform3DGroup();
        discTransform.Children.Add(new RotateTransform3D(_discTiltRotation));
        discTransform.Children.Add(_discTranslation);
        _discRoot.Transform = discTransform;
        var spineCardTransform = new Transform3DGroup();
        spineCardTransform.Children.Add(_spineCardTranslation);
        spineCardTransform.Children.Add(_spineCardOpenTranslation);
        spineCardTransform.Children.Add(_spineCardDragTranslation);
        _spineCardRoot.Transform = spineCardTransform;
        _caseRoot.Children.Add(_baseRoot);
        _caseRoot.Children.Add(_lidRoot);
        _caseRoot.Children.Add(_spineCardRoot);

        Viewport = new Viewport3DX
        {
            EffectsManager = _effects,
            BackgroundColor = MediaColor.FromRgb(11, 15, 21),
            Camera = new DxPerspectiveCamera
            {
                Position = new Point3D(0, 0.12, 5.25),
                LookDirection = new Vector3D(0, -0.08, -5.25),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 34
            },
            MSAA = MSAALevel.Two,
            FXAALevel = FXAALevel.Medium,
            // Weighted OIT resolves all transparent acrylic in one pass.
            // Depth peeling rendered the scene up to five times per frame.
            OITRenderMode = OITRenderType.SinglePassWeighted,
            OITDepthPeelingIteration = 2,
            EnableOITDepthPeelingDynamicIteration = false,
            EnableSSAO = true,
            SSAOQuality = SSAOQuality.Low,
            SSAOIntensity = 1.05,
            SSAOSamplingRadius = 0.35,
            IsShadowMappingEnabled = true,
            IsRotationEnabled = false,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            // Picking is handled by the parent's preview events so model clicks
            // do not take over the established case-rotation controls.
            EnableMouseButtonHitTest = false,
            EnableDpiScale = true,
            EnableD2DRendering = false
        };
        _animationRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationRenderTimer.Tick += (_, _) => Viewport.InvalidateRender();
        Viewport.Items.Add(new AmbientLight3D { Color = MediaColor.FromRgb(58, 66, 76) });
        Viewport.Items.Add(new DirectionalLight3D
        {
            Color = MediaColor.FromRgb(178, 174, 168),
            Direction = new Vector3D(-0.7, -0.8, -1)
        });
        Viewport.Items.Add(new DirectionalLight3D
        {
            Color = MediaColor.FromRgb(92, 94, 96),
            Direction = new Vector3D(0.9, 0.15, -0.5)
        });
        Viewport.Items.Add(new PointLight3D
        {
            Color = MediaColor.FromRgb(138, 136, 132),
            Position = new Point3D(-2.4, 2.2, 3.2),
            Range = 9
        });
        Viewport.Items.Add(new PointLight3D
        {
            Color = MediaColor.FromRgb(100, 100, 98),
            Position = new Point3D(2.8, -0.4, 1.8),
            Range = 8
        });

        // Product-photography light cards are outside the camera frustum but
        // visible to the reflection probe. They give clear acrylic its
        // characteristic long white edge highlights.
        AddStudioCard(
            new Vector3(-3.5f, 2.7f, 2.4f), new Vector3(3.5f, 2.7f, 2.4f),
            new Vector3(3.5f, 2.7f, -2.2f), new Vector3(-3.5f, 2.7f, -2.2f),
            new Color4(0.18f, 0.18f, 0.17f, 1));
        AddStudioCard(
            new Vector3(-3.3f, 2.3f, -1.5f), new Vector3(-3.3f, 2.3f, 3.4f),
            new Vector3(-3.3f, -2.3f, 3.4f), new Vector3(-3.3f, -2.3f, -1.5f),
            new Color4(0.09f, 0.09f, 0.085f, 1));
        AddStudioCard(
            new Vector3(3.5f, 2.0f, 3.0f), new Vector3(3.5f, 2.0f, -1.8f),
            new Vector3(3.5f, -2.0f, -1.8f), new Vector3(3.5f, -2.0f, 3.0f),
            new Color4(0.055f, 0.055f, 0.052f, 1));

        _reflection.Children.Add(_caseRoot);
        Viewport.Items.Add(_reflection);
    }

    public void SetItem(JewelCaseCoverFlowItem item, double yaw, double pitch)
    {
        EndDiscDrag();
        EndSpineCardDrag();
        ResetSpineCardDragOffset();
        _baseRoot.Children.Clear();
        _lidRoot.Children.Clear();
        _spineCardRoot.Children.Clear();
        _bookletRoot.Children.Clear();
        _lidRoot.Children.Add(_bookletRoot);
        _discRoot.Children.Clear();
        _baseRoot.Children.Add(_discRoot);
        const float width = 2.42f;
        const float height = 2.12f;
        // Keep the slim central body and printed spine at the established
        // depth. Only the narrow top/bottom rails project farther outward.
        const float depth = 0.125f;
        const float discCenterX = 0.044f;
        const float discOuterRadius = 1.018f;

        var acrylic = new PBRMaterial
        {
            Name = "Clear acrylic",
            AlbedoColor = new Color4(0.985f, 0.985f, 0.975f, 0.040f),
            MetallicFactor = 0.02,
            RoughnessFactor = 0.075,
            ReflectanceFactor = 0.36,
            ClearCoatStrength = 0.42,
            ClearCoatRoughness = 0.035,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var mouldedEdgeAcrylic = new PBRMaterial
        {
            Name = "Thick moulded acrylic edges",
            // Real jewel-case rails and corners contain substantially more
            // plastic than the broad image windows. They read as milky-clear
            // solid forms rather than almost invisible panes.
            // Neutral dense acrylic. A near-white albedo over a white tray
            // turns the whole perimeter into a paper-like white frame.
            AlbedoColor = new Color4(0.57f, 0.59f, 0.59f, 0.34f),
            MetallicFactor = 0,
            RoughnessFactor = 0.16,
            ReflectanceFactor = 0.43,
            ClearCoatStrength = 0.30,
            ClearCoatRoughness = 0.09,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var manualStopAcrylic = new PBRMaterial
        {
            Name = "Clear manual retaining lip",
            // A narrow but relatively thick clear moulding: less transparent
            // than the broad lid window so its boundary remains readable.
            AlbedoColor = new Color4(0.61f, 0.63f, 0.62f, 0.34f),
            MetallicFactor = 0,
            RoughnessFactor = 0.19,
            ReflectanceFactor = 0.40,
            ClearCoatStrength = 0.24,
            ClearCoatRoughness = 0.10,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var backFrameAcrylic = new PBRMaterial
        {
            Name = "Dense clear back artwork frame",
            AlbedoColor = new Color4(0.40f, 0.42f, 0.43f, 0.62f),
            MetallicFactor = 0,
            RoughnessFactor = 0.17,
            ReflectanceFactor = 0.46,
            ClearCoatStrength = 0.28,
            ClearCoatRoughness = 0.075,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var trayColor = item.TrayColorMode switch
        {
            // Slightly warm moulded resin.  A neutral/high-value white is
            // washed out by the environment lighting and looks painted.
            "White" => new Color4(0.76f, 0.735f, 0.66f, 1),
            "Black" => new Color4(0.025f, 0.029f, 0.035f, 1),
            "Gray" => new Color4(0.29f, 0.31f, 0.33f, 1),
            "Clear" => new Color4(0.90f, 0.92f, 0.93f, 0.12f),
            _ => new Color4(0.045f, 0.052f, 0.064f, 1)
        };
        var trayIsClear = string.Equals(item.TrayColorMode, "Clear", StringComparison.OrdinalIgnoreCase);
        var tray = new PBRMaterial
        {
            Name = "Tray",
            AlbedoColor = trayColor,
            // A small resin-colour ambient term prevents deep moulded wells
            // from collapsing to scene-black while preserving their relief.
            EmissiveColor = item.TrayColorMode switch
            {
                "White" => new Color4(0.055f, 0.050f, 0.040f, 1),
                "Gray" => new Color4(0.018f, 0.019f, 0.020f, 1),
                "Black" => new Color4(0.002f, 0.002f, 0.003f, 1),
                _ => new Color4(0.004f, 0.007f, 0.006f, 1)
            },
            MetallicFactor = 0,
            RoughnessFactor = trayIsClear ? 0.12 : 0.42,
            ReflectanceFactor = trayIsClear ? 0.42 : 0.22,
            ClearCoatStrength = trayIsClear ? 0.30 : 0,
            RenderEnvironmentMap = trayIsClear
        };
        var trayGrooveColor = item.TrayColorMode switch
        {
            "White" => new Color4(0.61f, 0.59f, 0.53f, 1),
            "Black" => new Color4(0.012f, 0.014f, 0.017f, 1),
            "Gray" => new Color4(0.20f, 0.215f, 0.23f, 1),
            _ => new Color4(0.026f, 0.031f, 0.039f, 1)
        };
        var trayGroove = new PBRMaterial
        {
            Name = "Tray moulding grooves",
            AlbedoColor = trayGrooveColor,
            MetallicFactor = 0,
            RoughnessFactor = 0.58,
            ReflectanceFactor = 0.10
        };
        var bookletPageEdge = new PBRMaterial
        {
            Name = "Booklet page edges",
            AlbedoColor = new Color4(0.72f, 0.71f, 0.68f, 1),
            MetallicFactor = 0,
            RoughnessFactor = 0.94,
            ReflectanceFactor = 0.02
        };

        // The shell is the attributed CC BY 4.0 reference model itself,
        // normalized to jewel-case proportions. The lower STL is separated
        // into its dark disc tray and clear perimeter; the upper STL remains
        // a clear acrylic lid. This replaces the former collection of boxes.
        var shell = CaseGeometry.Value;
        AddMesh(shell.BottomTray, tray, trayIsClear, true, _baseRoot, false);
        AddMesh(shell.BottomPerimeter, acrylic, true, true, _baseRoot);
        AddMesh(shell.BottomMouldedEdges, mouldedEdgeAcrylic, true, true, _baseRoot);
        AddMesh(shell.TopLid, acrylic, true, true, _lidRoot);
        AddMesh(shell.TopMouldedEdges, mouldedEdgeAcrylic, true, true, _lidRoot);

        // The removable tray wraps up around the outer spine side. Without
        // this wall the reverse of the printed spine is visible from inside the
        // opened case, unlike the moulded right-rear corner of a real tray.
        AddTraySpineCover(width, height, depth, tray, trayIsClear);
        AddTrayRecessBacking(width, height, depth, tray, trayIsClear);

        // Opaque replacement trays have a finely ribbed hinge-side band.  The
        // ribs are real shallow geometry, not painted lines, so their contrast
        // follows the light as the case rotates.  A clear tray already exposes
        // the STL surface and does not need this opaque moulding overlay.
        if (!trayIsClear)
            AddTraySpineRibs(width, height,
                shell.FrontArtworkArea.Z + 0.001f, tray, trayGroove);
        AddManualRetainingLip(width, height, shell.FrontArtworkArea.Z + 0.001f,
            manualStopAcrylic);

        // Use the inset plane actually modelled into the upper STL. Its source
        // dimensions are about 120.6 x 124.8 mm; using a generic 120 x 120
        // booklet rectangle leaves visible strips along this particular model.
        const float caseWidthMm = 142f;
        const float caseHeightMm = 125f;
        var unitX = width / caseWidthMm;
        var unitY = height / caseHeightMm;
        // The detected plane is the lid's available recess, not the printed
        // sheet itself. Place a true 120 x 120 mm booklet inside that recess so
        // the moulded acrylic border remains visible on every side.
        var frontPlaneCenterX = (shell.FrontArtworkArea.Left + shell.FrontArtworkArea.Right) / 2;
        var frontPlaneCenterY = (shell.FrontArtworkArea.Bottom + shell.FrontArtworkArea.Top) / 2;
        var bookletWidthMm = 120f * unitX;
        var bookletHeightMm = 120f * unitY;
        // On this case the front manual continues to the disc's left tangent.
        // Keep the right case inset fixed and extend only the left artwork edge;
        // otherwise a crescent of the disc remains visible beside the manual.
        // The booklet is retained by the raised lip at the right edge of the
        // ribbed tray band. Its artwork must begin after that lip rather than
        // running underneath the moulding.
        var bookletLeft = Math.Max(discCenterX - discOuterRadius,
            TrayManualStopRight(width) + 0.002f);
        var bookletRight = frontPlaneCenterX + bookletWidthMm / 2;
        var bookletBottom = frontPlaneCenterY - bookletHeightMm / 2;
        var bookletTop = frontPlaneCenterY + bookletHeightMm / 2;
        var bookletWidth = bookletRight - bookletLeft;
        var bookletHeight = bookletTop - bookletBottom;
        _bookletLift.CenterX = (bookletLeft + bookletRight) / 2;
        _bookletLift.CenterY = (bookletBottom + bookletTop) / 2;
        _bookletLift.CenterZ = shell.FrontArtworkArea.Z;
        // Only the actual page edges have thickness.  The artwork itself is a
        // surface nested in the upper STL, so the clear hinge strip remains
        // genuinely empty instead of looking like a continuation of the page.
        for (var page = 0; page < 4; page++)
        {
            var pageZ = shell.FrontArtworkArea.Z - 0.004f + page * 0.0015f;
            AddBox(new Vector3(bookletRight + 0.0015f, 0, pageZ),
                0.0025f, bookletHeight - 0.008f, 0.0012f, bookletPageEdge, false, _bookletRoot);
            AddBox(new Vector3((bookletLeft + bookletRight) / 2, bookletBottom - 0.0015f, pageZ),
                bookletWidth - 0.008f, 0.0025f, 0.0012f, bookletPageEdge, false, _bookletRoot);
        }
        AddArtwork(item.FrontCover, bookletLeft, bookletRight, bookletBottom, bookletTop,
            shell.FrontArtworkArea.Z + 0.001f, false, _bookletRoot);
        AddArtwork(item.InsideFrontCover, bookletLeft, bookletRight, bookletBottom, bookletTop,
            shell.FrontArtworkArea.Z - 0.003f, true, _bookletRoot);

        // The rear insert is 150 x 118 mm including two 6 mm spines. The flat
        // back window therefore displays the central 138 x 118 mm panel.
        var backArtworkWidth = 138f * unitX;
        var backArtworkHeight = 118f * unitY;
        // Missing panels stay unprinted; unrelated artwork must not be stretched onto them.
        AddArtwork(item.BackCover,
            -backArtworkWidth / 2, backArtworkWidth / 2,
            -backArtworkHeight / 2, backArtworkHeight / 2,
            // The STL's central lower tray is opaque. Place the printed rear
            // inlay immediately under the exterior acrylic surface so it is
            // not hidden behind that tray when viewed from the back.
            -depth / 2 - 0.001f, true, _baseRoot);
        AddBackArtworkFrame(width, height, backArtworkWidth, backArtworkHeight,
            -depth / 2 - 0.002f, backFrameAcrylic);
        var inlay = item.SplitInlay();
        // Two independent printed sides of the same sheet. Keep the interior
        // below the tray floor, so opaque resin still hides it naturally.
        AddArtwork(inlay.Panel,
            -backArtworkWidth / 2, backArtworkWidth / 2,
            -backArtworkHeight / 2, backArtworkHeight / 2,
            -depth / 2 + 0.001f, false, _baseRoot, "Inlay artwork");
        // Back is viewed from -Z: its source-right edge is at world -X.
        // Inlay faces +Z, so its left/right strips stay in world order.
        AddSpine(item.RightSpineCover,
            -width / 2 - 0.001f, backArtworkHeight, depth, true, _baseRoot, inlay.Left);
        AddSpine(item.SpineCover,
            width / 2 + 0.001f, backArtworkHeight, depth, false, _baseRoot, inlay.Right);
        AddSpineCard(item.SpineCard, width, height, depth);
        // 120 mm disc in a roughly 125 mm-high jewel case.
        AddDisc(item.DiscImage, new Vector3(discCenterX, 0.004f, -0.017f),
            discOuterRadius, 0.128f, 0.020f, _discRoot);

        ApplyRotation(yaw, pitch);
        Viewport.InvalidateRender();
    }

    public void SetRotation(double yaw, double pitch)
    {
        ApplyRotation(yaw, pitch);
        Viewport.InvalidateRender();
    }

    public void SetViewPan(double x, double y)
    {
        _viewPan.OffsetX = x;
        _viewPan.OffsetY = y;
        Viewport.InvalidateRender();
    }

    public void SetViewZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.55, 2.40);
        _viewZoom.ScaleX = _viewZoom.ScaleY = _viewZoom.ScaleZ = zoom;
        Viewport.InvalidateRender();
    }

    public void SetCaseOpen(bool open, bool animate = true)
    {
        var animationGeneration = ++_caseAnimationGeneration;
        // Negative rotation lifts the lid toward the viewer instead of passing
        // it through the tray. At -180 degrees both inner faces are coplanar.
        var targetAngle = open ? -180d : 0d;
        var targetScale = open ? 0.68d : 1d;
        // Centre the two-piece open assembly in the viewport. The translation
        // follows the actual hinge rather than the case's outer bounding edge.
        var targetOffsetX = open ? -AssembledHingeX : 0d;
        var targetSpineCardOffsetX = open ? SpineCardOpenClearanceX : 0d;
        if (!animate)
        {
            _animationRenderTimer.Stop();
            _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
            _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _lidHingeRotation.Angle = targetAngle;
            _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
            _openCenterTranslation.OffsetX = targetOffsetX;
            _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
            Viewport.InvalidateRender();
            return;
        }

        var currentAngle = _lidHingeRotation.Angle;
        var currentScaleX = _openScale.ScaleX;
        var currentScaleY = _openScale.ScaleY;
        var currentScaleZ = _openScale.ScaleZ;
        var currentOffsetX = _openCenterTranslation.OffsetX;
        var currentSpineCardOffsetX = _spineCardOpenTranslation.OffsetX;
        _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
        _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        _lidHingeRotation.Angle = targetAngle;
        _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
        _openCenterTranslation.OffsetX = targetOffsetX;
        _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
        // Helix renders on demand. WPF transform animations alone do not
        // continuously invalidate its DirectX surface, so drive redraws only
        // for the duration of the lid animation.
        _animationRenderTimer.Start();

        var easing = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
        };
        System.Windows.Media.Animation.DoubleAnimation Animation(double from, double to) => new()
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(1180),
            EasingFunction = easing,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        var angleAnimation = Animation(currentAngle, targetAngle);
        angleAnimation.Completed += (_, _) =>
        {
            if (animationGeneration != _caseAnimationGeneration) return;
            _animationRenderTimer.Stop();
            // Commit exact final values. This prevents a completed or rapidly
            // reversed animation from leaving the lid/artwork partly open.
            _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
            _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _lidHingeRotation.Angle = targetAngle;
            _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
            _openCenterTranslation.OffsetX = targetOffsetX;
            _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
            Viewport.InvalidateRender();
        };
        _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty,
            angleAnimation,
            System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty,
            Animation(currentScaleX, targetScale));
        _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty,
            Animation(currentScaleY, targetScale));
        _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty,
            Animation(currentScaleZ, targetScale));
        _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty,
            Animation(currentOffsetX, targetOffsetX));
        _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty,
            Animation(currentSpineCardOffsetX, targetSpineCardOffsetX));
    }

    public void SetDiscRemoved(bool removed, bool animate = true)
    {
        EndDiscDrag();
        _discRemoved = removed;
        var generation = ++_discAnimationGeneration;
        var targetX = removed ? 0.62d : 0d;
        var targetY = removed ? 0.08d : 0d;
        var targetZ = removed ? 0.62d : 0d;
        var targetAngle = removed ? -12d : 0d;

        void Commit()
        {
            _discTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _discTranslation.BeginAnimation(TranslateTransform3D.OffsetYProperty, null);
            _discTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
            _discTiltRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _discTranslation.OffsetX = targetX;
            _discTranslation.OffsetY = targetY;
            _discTranslation.OffsetZ = targetZ;
            _discTiltRotation.Angle = targetAngle;
        }

        if (!animate)
        {
            Commit();
            Viewport.InvalidateRender();
            return;
        }

        var fromX = _discTranslation.OffsetX;
        var fromY = _discTranslation.OffsetY;
        var fromZ = _discTranslation.OffsetZ;
        var fromAngle = _discTiltRotation.Angle;
        Commit();
        _animationRenderTimer.Start();
        var easing = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
        };
        System.Windows.Media.Animation.DoubleAnimation Animation(double from, double to) => new()
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(920),
            EasingFunction = easing,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };
        var lift = Animation(fromZ, targetZ);
        lift.Completed += (_, _) =>
        {
            if (generation != _discAnimationGeneration) return;
            _animationRenderTimer.Stop();
            Commit();
            Viewport.InvalidateRender();
        };
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty,
            Animation(fromX, targetX));
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetYProperty,
            Animation(fromY, targetY));
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, lift);
        _discTiltRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty,
            Animation(fromAngle, targetAngle));
    }

    public void SetSpineCardRemoved(bool removed, bool animate = true)
    {
        EndSpineCardDrag();
        // A manually placed obi always returns along its normal extraction
        // path, rather than flying into the case from an arbitrary drag point.
        if (!removed) ResetSpineCardDragOffset();
        if (animate) { _ = AnimateSpineCardAsync(removed); return; }
        CancelSpineCardMotion();
        SetSpineCardProgress(removed ? 1 : 0);
    }

    private void SetSpineCardProgress(double progress)
    {
        _spineCardProgress = Math.Clamp(progress, 0, 1);
        // Pull the still-folded obi sideways off the left-hand case spine.
        // A small ease at either end keeps the paper motion visually physical.
        var eased = _spineCardProgress * _spineCardProgress * (3 - 2 * _spineCardProgress);
        _spineCardTranslation.OffsetX = _spineCardRemovedOffsetX * eased;
        _spineCardTranslation.OffsetY = SpineCardRemovedOffsetY * eased;
        _spineCardTranslation.OffsetZ = SpineCardRemovedOffsetZ * eased;
        Viewport.InvalidateRender();
    }

    private void CancelSpineCardMotion()
    {
        _spineCardMotionTimer.Stop();
        if (_spineCardMotionTick is not null) _spineCardMotionTimer.Tick -= _spineCardMotionTick;
        _spineCardMotionTick = null;
        _spineCardMotionCompletion?.TrySetResult(false);
        _spineCardMotionCompletion = null;
    }

    public Task<bool> AnimateSpineCardAsync(bool removed)
    {
        CancelSpineCardMotion();
        var from = _spineCardProgress;
        var target = removed ? 1d : 0d;
        if (Math.Abs(from - target) < .00001)
        {
            SetSpineCardProgress(target);
            return Task.FromResult(true);
        }

        // Completion is raised by the DispatcherTimer on the UI thread so the
        // caller can immediately continue with the case hinge animation.
        var completion = new TaskCompletionSource<bool>();
        _spineCardMotionCompletion = completion;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var duration = .9 * Math.Abs(target - from);
        _spineCardMotionTick = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            SetSpineCardProgress(from + (target - from) * t);
            if (t < 1) return;
            _spineCardMotionTimer.Stop();
            _spineCardMotionTimer.Tick -= _spineCardMotionTick;
            _spineCardMotionTick = null;
            _spineCardMotionCompletion = null;
            completion.TrySetResult(true);
        };
        _spineCardMotionTimer.Tick += _spineCardMotionTick;
        _spineCardMotionTimer.Start();
        return completion.Task;
    }

    public void SetBookletRemoved(bool removed, bool animate)
    {
        if (animate) { _ = AnimateBookletAsync(removed); return; }
        CancelBookletMotion();
        SetBookletProgress(removed ? 1 : 0);
    }

    // Slide almost an entire booklet width beneath all four tabs before lifting
    // it. This prevents the paper from visibly passing through the retaining
    // claws. Insertion retraces the same physical path.
    private static (double X, double Y, double Z, double Tilt) BookletPose(double progress)
    {
        (double Time, double X, double Y, double Z, double Tilt)[] stops =
        [
            (0, 0, 0, 0, 0),
            (.10, .08, 0, -.010, 0),
            (.42, 1.58, 0, -.018, 0),
            (.56, 2.08, 0, -.035, 0),
            (.72, 2.08, .05, -.58, -10),
            (1, .45, .10, -1.25, -16)
        ];
        progress = Math.Clamp(progress, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            if (progress > stops[i].Time) continue;
            var a = stops[i - 1]; var b = stops[i];
            var t = (progress - a.Time) / (b.Time - a.Time);
            t = t * t * (3 - 2 * t);
            return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t, a.Tilt + (b.Tilt - a.Tilt) * t);
        }
        return (.45, .10, -1.25, -16);
    }

    private void SetBookletProgress(double progress)
    {
        _bookletProgress = Math.Clamp(progress, 0, 1);
        var pose = BookletPose(_bookletProgress);
        _bookletTranslation.OffsetX = pose.X;
        _bookletTranslation.OffsetY = pose.Y;
        _bookletTranslation.OffsetZ = pose.Z;
        _bookletTilt.Angle = pose.Tilt;
        Viewport.InvalidateRender();
    }

    private EventHandler? _bookletMotionTick;
    private void CancelBookletMotion()
    {
        _bookletMotionTimer.Stop();
        if (_bookletMotionTick is not null) _bookletMotionTimer.Tick -= _bookletMotionTick;
        _bookletMotionTick = null;
        _bookletMotionCompletion?.TrySetResult(false);
        _bookletMotionCompletion = null;
    }

    public Task<bool> AnimateBookletAsync(bool removed)
    {
        CancelBookletMotion();
        var from = _bookletProgress; var target = removed ? 1d : 0d;
        if (Math.Abs(from - target) < .00001) { SetBookletProgress(target); return Task.FromResult(true); }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bookletMotionCompletion = completion;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var duration = 2.1 * Math.Abs(target - from);
        _bookletMotionTick = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            SetBookletProgress(from + (target - from) * t);
            if (t < 1) return;
            _bookletMotionTimer.Stop();
            _bookletMotionTimer.Tick -= _bookletMotionTick;
            _bookletMotionTick = null; _bookletMotionCompletion = null;
            completion.TrySetResult(true);
        };
        _bookletMotionTimer.Tick += _bookletMotionTick;
        _bookletMotionTimer.Start();
        return completion.Task;
    }

    public bool BeginDiscDrag(System.Windows.Point position)
    {
        if (!_discRemoved || Viewport.Camera is not DxPerspectiveCamera camera) return false;
        var hit = Viewport.FindHits(position)?.OrderBy(result => result.Distance).FirstOrDefault();
        if (hit is null || !_discRoot.Children.OfType<MeshGeometryModel3D>().Any(mesh =>
            ReferenceEquals(hit.ModelHit, mesh) || ReferenceEquals(hit.ModelHit, mesh.SceneNode))) return false;
        // Stop the extraction animation at its current position, without snapping
        // to the animation's destination when the user grabs a moving disc.
        ++_discAnimationGeneration;
        var offset = new Vector3D(_discTranslation.OffsetX, _discTranslation.OffsetY, _discTranslation.OffsetZ);
        var tilt = _discTiltRotation.Angle;
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetYProperty, null);
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
        _discTiltRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _discTranslation.OffsetX = offset.X;
        _discTranslation.OffsetY = offset.Y;
        _discTranslation.OffsetZ = offset.Z;
        _discTiltRotation.Angle = tilt;
        _animationRenderTimer.Stop();
        _discDragPoint = new Point3D(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
        _discDragNormal = camera.LookDirection;
        return true;
    }

    public void DragDiscTo(System.Windows.Point position)
    {
        if (!_discRemoved || _discDragPoint is not { } previous) return;
        var point = Viewport.UnProjectOnPlane(position, previous, _discDragNormal);
        if (point is not { } current) return;
        var inverse = _caseTransform.Value;
        if (!inverse.HasInverse) return;
        inverse.Invert();
        // Screen-parallel movement converted back into case coordinates. This
        // keeps the grabbed point beneath the cursor at any case angle/zoom.
        var delta = inverse.Transform(current - previous);
        var constrained = ConstrainRemovedDiscOffset(new Vector3D(
            _discTranslation.OffsetX + delta.X,
            _discTranslation.OffsetY + delta.Y,
            _discTranslation.OffsetZ + delta.Z));
        _discTranslation.OffsetX = constrained.X;
        _discTranslation.OffsetY = constrained.Y;
        _discTranslation.OffsetZ = constrained.Z;
        _discDragPoint = current;
        Viewport.InvalidateRender();
    }

    private static Vector3D ConstrainRemovedDiscOffset(Vector3D proposed) =>
        new(proposed.X, proposed.Y, Math.Max(RemovedDiscMinimumZ, proposed.Z));

    public void EndDiscDrag() => _discDragPoint = null;

    public bool BeginSpineCardDrag(System.Windows.Point position)
    {
        if (_spineCardProgress < .999 || Viewport.Camera is not DxPerspectiveCamera camera) return false;
        var hit = Viewport.FindHits(position)?.OrderBy(result => result.Distance).FirstOrDefault();
        if (hit is null || !_spineCardRoot.Children.OfType<MeshGeometryModel3D>().Any(mesh =>
            ReferenceEquals(hit.ModelHit, mesh) || ReferenceEquals(hit.ModelHit, mesh.SceneNode))) return false;
        CancelSpineCardMotion();
        _spineCardDragPoint = new Point3D(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
        _spineCardDragNormal = camera.LookDirection;
        return true;
    }

    public void DragSpineCardTo(System.Windows.Point position)
    {
        if (_spineCardProgress < .999 || _spineCardDragPoint is not { } previous) return;
        var point = Viewport.UnProjectOnPlane(position, previous, _spineCardDragNormal);
        if (point is not { } current) return;
        var inverse = _caseTransform.Value;
        if (!inverse.HasInverse) return;
        inverse.Invert();
        var delta = inverse.Transform(current - previous);
        _spineCardDragTranslation.OffsetX += delta.X;
        _spineCardDragTranslation.OffsetY += delta.Y;
        _spineCardDragTranslation.OffsetZ += delta.Z;
        _spineCardDragPoint = current;
        Viewport.InvalidateRender();
    }

    public void EndSpineCardDrag() => _spineCardDragPoint = null;

    private void ResetSpineCardDragOffset()
    {
        _spineCardDragTranslation.OffsetX = 0;
        _spineCardDragTranslation.OffsetY = 0;
        _spineCardDragTranslation.OffsetZ = 0;
    }

    private void ApplyRotation(double yaw, double pitch)
    {
        // Rotation changes only this transform. Geometry, materials and GPU
        // textures remain resident instead of being rebuilt on every mouse move.
        _pitchRotation.Angle = pitch;
        _yawRotation.Angle = yaw;
    }

    private void AddBox(Vector3 center, float x, float y, float z, DxMaterial material,
        bool transparent = false, GroupModel3D? target = null)
    {
        var builder = new MeshBuilder(true, true, true);
        builder.AddBox(center, x, y, z);
        AddMesh(builder.ToMeshGeometry3D(), material, transparent, false, target);
    }

    private void AddTraySpineRibs(float width, float height, float frontSurfaceZ,
        DxMaterial material, DxMaterial grooveMaterial)
    {
        var grooveFloor = new MeshBuilder(true, true, true);
        var ribs = new MeshBuilder(true, true, true);

        // This is the removable tray's narrow hinge/spine column, rather than
        // the clear case perimeter.  Closely spaced longitudinal ribs reproduce
        // the injection-moulded grip visible on white and coloured jewel trays.
        const int ribCount = 23;
        // Begin at the tray-side edge.  Leaving an inset here produced a broad
        // smooth white stripe that does not exist on the reference tray.
        var bandLeft = -width / 2 + 0.003f;
        var bandRight = -width / 2 + 0.245f;
        // Continue almost to the upper/lower tray shoulders. Keep only a very
        // small clearance so the ribs do not intersect the clear case rails.
        var ribLength = height - 0.045f;
        const float grooveDepth = 0.0022f;
        var recessedSurfaceZ = frontSurfaceZ - grooveDepth;

        // Front-facing surfaces only: a volumetric riser exposes an artificial
        // white cross-section in the spine view and interferes with the upper
        // and lower STL halves.  These shallow planes retain the moulded relief
        // from the front while disappearing naturally when seen edge-on.
        grooveFloor.AddQuad(
            new Vector3(bandLeft, ribLength / 2, recessedSurfaceZ),
            new Vector3(bandLeft, -ribLength / 2, recessedSurfaceZ),
            new Vector3(bandRight, -ribLength / 2, recessedSurfaceZ),
            new Vector3(bandRight, ribLength / 2, recessedSurfaceZ));
        var spacing = (bandRight - bandLeft) / (ribCount - 1);
        var ribWidth = spacing * 0.72f;
        for (var index = 0; index < ribCount; index++)
        {
            var x = bandLeft + (bandRight - bandLeft) * index / (ribCount - 1);
            var left = Math.Max(bandLeft, x - ribWidth / 2);
            var right = Math.Min(bandRight, x + ribWidth / 2);
            ribs.AddQuad(
                new Vector3(left, ribLength / 2, frontSurfaceZ),
                new Vector3(left, -ribLength / 2, frontSurfaceZ),
                new Vector3(right, -ribLength / 2, frontSurfaceZ),
                new Vector3(right, ribLength / 2, frontSurfaceZ));
        }

        AddMesh(grooveFloor.ToMeshGeometry3D(), grooveMaterial, false, false, _baseRoot);
        AddMesh(ribs.ToMeshGeometry3D(), material, false, false, _baseRoot);
    }

    private void AddManualRetainingLip(float width, float height, float frontSurfaceZ,
        DxMaterial stopMaterial)
    {
        var manualStop = new MeshBuilder(true, true, true);
        const float stopWidth = 0.014f;
        var stopRight = TrayManualStopRight(width);
        var stopLength = height - 0.045f;
        manualStop.AddBox(new Vector3(stopRight - stopWidth / 2, 0,
                frontSurfaceZ + 0.0018f),
            stopWidth, stopLength, 0.0036f);
        // This lip belongs to the clear lid, not to the removable coloured
        // tray. It therefore follows the lid when opened and always uses the
        // denser moulded-acrylic material regardless of tray colour.
        AddMesh(manualStop.ToMeshGeometry3D(), stopMaterial, true, false, _lidRoot);
    }

    // The ribbed band's original right boundary is +0.245 from the case edge.
    // Centre the 0.014-wide clear lip on that boundary, rather than growing the
    // entire lip into the booklet side.
    private static float TrayManualStopRight(float width) => -width / 2 + 0.252f;

    private void AddTraySpineCover(float width, float height, float depth,
        DxMaterial material, bool transparent)
    {
        const float wallThickness = 0.030f;
        AddBox(new Vector3(width / 2 - wallThickness / 2, 0, 0),
            wallThickness, height - 0.060f, depth - 0.016f,
            material, transparent, _baseRoot);
    }

    private void AddTrayRecessBacking(float width, float height, float depth,
        DxMaterial material, bool transparent)
    {
        // Several triangular reliefs in the printable STL are true openings,
        // not recessed faces. A physical opaque injection-moulded tray has a
        // continuous resin floor behind them. This thin internal backing stops
        // the dark scene background showing through while remaining in front
        // of neither the exterior Back artwork nor the clear perimeter rails.
        const float backingThickness = 0.003f;
        // Place it immediately below the disc (whose lower face is about
        // Z=-0.027), rather than at the exterior shell. A deep backing creates
        // unnaturally black projected triangles beneath the support features.
        var backingZ = -depth / 2 + depth * 0.26f;
        AddBox(new Vector3(0.035f, 0, backingZ),
            // Keep the backing inside the clear perimeter. The triangular
            // support faces are now classified as tray material directly, so
            // extending this panel to the rails only creates a white frame
            // when the case is closed.
            width - 0.145f, height - 0.105f, backingThickness,
            material, transparent, _baseRoot);
    }

    private void AddBackArtworkFrame(float width, float height, float artworkWidth,
        float artworkHeight, float z, DxMaterial material)
    {
        var frame = new MeshBuilder(true, true, true);
        var outerLeft = -width / 2 + 0.012f;
        var outerRight = width / 2 - 0.012f;
        var outerBottom = -height / 2 + 0.012f;
        var outerTop = height / 2 - 0.012f;
        var innerLeft = -artworkWidth / 2;
        var innerRight = artworkWidth / 2;
        var innerBottom = -artworkHeight / 2;
        var innerTop = artworkHeight / 2;

        static void AddBackQuad(MeshBuilder builder, float left, float right,
            float bottom, float top, float depthZ)
        {
            // Clockwise from +Z so the visible outside face points toward -Z.
            builder.AddQuad(new Vector3(left, top, depthZ),
                new Vector3(right, top, depthZ),
                new Vector3(right, bottom, depthZ),
                new Vector3(left, bottom, depthZ));
        }

        AddBackQuad(frame, outerLeft, innerLeft, outerBottom, outerTop, z);
        AddBackQuad(frame, innerRight, outerRight, outerBottom, outerTop, z);
        AddBackQuad(frame, innerLeft, innerRight, innerTop, outerTop, z);
        AddBackQuad(frame, innerLeft, innerRight, outerBottom, innerBottom, z);
        AddMesh(frame.ToMeshGeometry3D(), material, true, true, _baseRoot);
    }

    private sealed record StlCaseGeometry(
        DxMesh BottomTray,
        DxMesh BottomPerimeter,
        DxMesh BottomMouldedEdges,
        DxMesh TopLid,
        DxMesh TopMouldedEdges,
        StlArtworkArea FrontArtworkArea);

    private sealed record StlArtworkArea(float Left, float Right, float Bottom, float Top, float Z);

    private static StlCaseGeometry LoadStlCaseGeometry()
    {
        const float width = 2.42f;
        const float height = 2.12f;
        const float depth = 0.125f;
        var bottom = LoadStlTriangles("ZipMp3Player.Assets.CdCaseBottom.stl");
        var top = LoadStlTriangles("ZipMp3Player.Assets.CdCaseTop.stl");
        var trayBuilder = new MeshBuilder(true, true, true);
        var perimeterBuilder = new MeshBuilder(true, true, true);
        var bottomEdgeBuilder = new MeshBuilder(true, true, true);
        var lidBuilder = new MeshBuilder(true, true, true);
        var topEdgeBuilder = new MeshBuilder(true, true, true);
        AppendNormalizedStl(bottom, width, height, depth, false,
            trayBuilder, perimeterBuilder, bottomEdgeBuilder);
        AppendNormalizedStl(top, width, height, depth, true,
            lidBuilder, null, topEdgeBuilder);
        var frontArtworkArea = FindFrontArtworkArea(top, width, height, depth);
        return new StlCaseGeometry(trayBuilder.ToMeshGeometry3D(),
            perimeterBuilder.ToMeshGeometry3D(), bottomEdgeBuilder.ToMeshGeometry3D(),
            lidBuilder.ToMeshGeometry3D(), topEdgeBuilder.ToMeshGeometry3D(), frontArtworkArea);
    }

    private static StlArtworkArea FindFrontArtworkArea(
        List<(Vector3 A, Vector3 B, Vector3 C)> triangles,
        float targetWidth, float targetHeight, float targetDepth)
    {
        var allVertices = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToList();
        var minimum = new Vector3(allVertices.Min(v => v.X), allVertices.Min(v => v.Y), allVertices.Min(v => v.Z));
        var maximum = new Vector3(allVertices.Max(v => v.X), allVertices.Max(v => v.Y), allVertices.Max(v => v.Z));
        var center = (minimum + maximum) / 2;
        var sourceWidth = maximum.X - minimum.X;
        var sourceHeight = maximum.Y - minimum.Y;

        // Locate the broad, full-height inset face while excluding the outer
        // lid face and narrower structural panels.
        var plane = triangles
            .GroupBy(triangle => MathF.Round((triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3 * 20) / 20)
            .Select(group =>
            {
                var vertices = group.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToList();
                var minX = vertices.Min(v => v.X);
                var maxX = vertices.Max(v => v.X);
                var minY = vertices.Min(v => v.Y);
                var maxY = vertices.Max(v => v.Y);
                return new { Z = group.Key, MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY,
                    Width = maxX - minX, Height = maxY - minY };
            })
            .Where(candidate => candidate.Height >= sourceHeight * 0.94f
                && candidate.Width >= sourceWidth * 0.78f
                && candidate.Width <= sourceWidth * 0.90f)
            .OrderByDescending(candidate => candidate.Width * candidate.Height)
            .FirstOrDefault();

        if (plane is null)
            return new StlArtworkArea(-targetWidth * 0.36f, targetWidth * 0.49f,
                -targetHeight / 2, targetHeight / 2, targetDepth * 0.42f);

        var scaleX = targetWidth / sourceWidth;
        var scaleY = targetHeight / sourceHeight;
        float TransformX(float sourceX) => -(sourceX - center.X) * scaleX;
        float TransformY(float sourceY) => (sourceY - center.Y) * scaleY;
        var x0 = TransformX(plane.MinX);
        var x1 = TransformX(plane.MaxX);
        var normalizedZ = (plane.Z - minimum.Z) / (maximum.Z - minimum.Z);
        const float halfShellSpan = 0.64f;
        var z = targetDepth / 2 - normalizedZ * targetDepth * halfShellSpan;
        return new StlArtworkArea(Math.Min(x0, x1), Math.Max(x0, x1),
            TransformY(plane.MinY), TransformY(plane.MaxY), z);
    }

    private static List<(Vector3 A, Vector3 B, Vector3 C)> LoadStlTriangles(string resourceName)
    {
        using var stream = typeof(DxJewelCaseScene).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded STL resource was not found: {resourceName}");
        using var reader = new BinaryReader(stream);
        _ = reader.ReadBytes(80);
        var count = reader.ReadUInt32();
        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            _ = ReadStlVector(reader); // Source normal; MeshBuilder recalculates it after scaling.
            var a = ReadStlVector(reader);
            var b = ReadStlVector(reader);
            var c = ReadStlVector(reader);
            _ = reader.ReadUInt16();
            triangles.Add((a, b, c));
        }
        return triangles;
    }

    private static Vector3 ReadStlVector(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void AppendNormalizedStl(List<(Vector3 A, Vector3 B, Vector3 C)> triangles,
        float targetWidth, float targetHeight, float targetDepth, bool top,
        MeshBuilder primary, MeshBuilder? perimeter, MeshBuilder mouldedEdges)
    {
        var vertices = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToList();
        var minimum = new Vector3(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z));
        var maximum = new Vector3(vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
        var center = (minimum + maximum) / 2;
        var scaleX = targetWidth / (maximum.X - minimum.X);
        var scaleY = targetHeight / (maximum.Y - minimum.Y);
        var zRange = maximum.Z - minimum.Z;

        Vector3 Transform(Vector3 source)
        {
            var normalizedZ = (source.Z - minimum.Z) / zRange;
            // The top STL is exported with its hinge on the right so both
            // printable halves can lie open. Mirror it into the closed-case
            // assembly, where its hinge mates with the bottom's left hinge.
            var assembledX = (source.X - center.X) * scaleX * (top ? -1 : 1);
            // Both STL halves include a moulded mating lip. Let those lips
            // interlock around Z=0 instead of leaving a visible air gap when
            // the case is closed. Outer faces remain at the exact case depth.
            const float halfShellSpan = 0.64f;
            return new Vector3(assembledX, (source.Y - center.Y) * scaleY,
                top ? targetDepth / 2 - normalizedZ * targetDepth * halfShellSpan
                    : -targetDepth / 2 + normalizedZ * targetDepth * halfShellSpan);
        }

        foreach (var triangle in triangles)
        {
            var a = Transform(triangle.A);
            var b = Transform(triangle.B);
            var c = Transform(triangle.C);
            var centroid = (a + b + c) / 3;
            // Separate only the thick horizontal rails and corner blocks.
            // Broad front/back windows and the spine stay in the clearer
            // material so artwork brightness and legibility are unchanged.
            var isMouldedEdge = top
                ? Math.Abs(centroid.Y) > targetHeight * 0.445f
                    // The whole hinge-side strip ends at the booklet recess,
                    // not merely at the outermost rail. Treating only the rail
                    // as thick acrylic lets the disc show through beside the
                    // front manual when the case is closed.
                    || centroid.X < -targetWidth * 0.402f
                    || centroid.X > targetWidth * 0.485f
                // On the lower STL, the band between 44.5% and 47.5% contains
                // the tray's four triangular support reliefs, not the clear
                // outer rail. Classifying that whole band as acrylic leaves
                // four dark tips around an opaque tray.
                : Math.Abs(centroid.Y) > targetHeight * 0.475f
                    || (Math.Abs(centroid.X) > targetWidth * 0.455f
                        && Math.Abs(centroid.Y) > targetHeight * 0.365f);
            var isTopOrBottomRail = Math.Abs(centroid.Y) > targetHeight * 0.445f;
            if (isTopOrBottomRail)
            {
                // The source rails span the full 0..10 mm depth, but the shell
                // normalisation intentionally compresses ordinary geometry to
                // 64%. Restore only these rails to the full case depth. Both
                // halves then share Z=0 as their centre and as the hinge axis,
                // while the tray, disc and broad lid surfaces remain unchanged.
                const float shellSpanRatio = 0.64f;
                static Vector3 RestoreFullRailDepth(Vector3 vertex, bool lid,
                    float depth)
                {
                    var normalized = lid
                        ? (depth / 2 - vertex.Z) / (depth * shellSpanRatio)
                        : (vertex.Z + depth / 2) / (depth * shellSpanRatio);
                    vertex.Z = lid
                        ? depth / 2 - normalized * depth
                        : -depth / 2 + normalized * depth;
                    return vertex;
                }
                a = RestoreFullRailDepth(a, top, targetDepth);
                b = RestoreFullRailDepth(b, top, targetDepth);
                c = RestoreFullRailDepth(c, top, targetDepth);
            }
            if (top)
            {
                // X and Z are both mirrored for the assembled upper shell, so
                // the two reflections preserve the original winding order.
                (isMouldedEdge ? mouldedEdges : primary).AddTriangle(a, b, c);
                continue;
            }
            // The removable inner tray is the raised structure: disc well,
            // hub, corner supports and the ribbed hinge-side column. The large
            // low rear plate belongs to the clear outer case and must not take
            // the selected tray color.
            const float bottomShellSpan = 0.64f;
            var traySurfaceThreshold = -targetDepth / 2 + targetDepth * bottomShellSpan * 0.20f;
            var isOuterClearRim = Math.Abs(centroid.X) > targetWidth * 0.485f
                || Math.Abs(centroid.Y) > targetHeight * 0.485f;
            var isRaisedInnerTray = centroid.Z > traySurfaceThreshold && !isOuterClearRim;
            // The shallow wells and triangular reliefs are moulded into the
            // same removable tray as the raised disc ring. They previously
            // fell through to the clear perimeter mesh, exposing the dark
            // scene background as black "holes". Keep every inner, non-rail
            // surface in the tray material so White/Black/Gray/Clear settings
            // all behave like the corresponding physical resin.
            var isRecessedInnerTray = !isOuterClearRim && !isMouldedEdge;
            if (isRaisedInnerTray || isRecessedInnerTray)
                primary.AddTriangle(a, b, c);
            else if (isMouldedEdge)
                mouldedEdges.AddTriangle(a, b, c);
            else
                (perimeter ?? primary).AddTriangle(a, b, c);
        }
    }

    private void AddCylinder(Vector3 start, Vector3 end, float radius, DxMaterial material, bool transparent)
    {
        var builder = new MeshBuilder(true, true, true);
        builder.AddCylinder(start, end, radius, 32, true, true);
        AddMesh(builder.ToMeshGeometry3D(), material, transparent);
    }

    private void AddMouldedLidDetails(float width, float height, float depth, DxMaterial material,
        DxMaterial recessMaterial)
    {
        var builder = new MeshBuilder(true, true, true);

        // Closely spaced injection-moulded ribs belong to the top and bottom
        // X-Z edge faces. Each rib crosses almost the full case depth rather
        // than being drawn as a decoration on the front cover.
        const int ribCount = 78;
        for (var index = 0; index < ribCount; index++)
        {
            var x = -1.02f + index * (2.04f / (ribCount - 1));
            builder.AddBox(new Vector3(x, -height / 2 + 0.015f, 0),
                0.0052f, 0.018f, depth + 0.010f);
            builder.AddBox(new Vector3(x, height / 2 - 0.014f, 0),
                0.0052f, 0.016f, depth + 0.010f);
        }

        // Inner shoulder and thin sealing lip visible as parallel highlights.
        builder.AddBox(new Vector3(0.055f, height / 2 - 0.057f, depth / 2 + 0.004f),
            width - 0.285f, 0.011f, 0.014f);
        builder.AddBox(new Vector3(0.055f, -height / 2 + 0.064f, depth / 2 + 0.004f),
            width - 0.285f, 0.011f, 0.014f);
        builder.AddBox(new Vector3(width / 2 - 0.052f, 0, depth / 2 + 0.004f),
            0.012f, height - 0.190f, 0.014f);

        // Paired trapezoidal catches are moulded into the narrow top/bottom
        // faces. They are deliberately absent from the front artwork plane.
        foreach (var x in new[] { -0.30f, 0.54f })
        {
            AddEdgeCatch(builder, x, height, depth, false);
            AddEdgeCatch(builder, x, height, depth, true);
        }
        foreach (var y in new[] { -height / 2 + 0.052f, height / 2 - 0.052f })
            builder.AddBox(new Vector3(width / 2 - 0.040f, y, depth / 2 + 0.008f),
                0.055f, 0.060f, 0.020f);

        AddMesh(builder.ToMeshGeometry3D(), material, true);

        // Dark inset behind each catch gives the same readable mechanical gap
        // seen in a real closed jewel case.
        var recesses = new MeshBuilder(true, true, true);
        foreach (var x in new[] { -0.30f, 0.54f })
        {
            recesses.AddBox(new Vector3(x, -height / 2 - 0.001f, 0), 0.245f, 0.008f, depth * 0.62f);
            recesses.AddBox(new Vector3(x, height / 2 + 0.001f, 0), 0.245f, 0.008f, depth * 0.62f);
        }
        AddMesh(recesses.ToMeshGeometry3D(), recessMaterial, false, true);
    }

    private static void AddEdgeCatch(MeshBuilder builder, float centerX, float height,
        float depth, bool top)
    {
        var outerY = top ? height / 2 + 0.010f : -height / 2 - 0.010f;
        var innerY = top ? height / 2 - 0.010f : -height / 2 + 0.010f;
        const float outerHalfWidth = 0.105f;
        const float innerHalfWidth = 0.075f;
        var backZ = -depth * 0.25f;
        var frontZ = depth * 0.25f;

        // Trapezoidal prism in the X-Z edge plane, extruded only through the
        // narrow Y thickness of the top/bottom moulding.
        var a0 = new Vector3(centerX - outerHalfWidth, outerY, backZ);
        var a1 = new Vector3(centerX + outerHalfWidth, outerY, backZ);
        var a2 = new Vector3(centerX + innerHalfWidth, outerY, frontZ);
        var a3 = new Vector3(centerX - innerHalfWidth, outerY, frontZ);
        var b0 = new Vector3(centerX - outerHalfWidth, innerY, backZ);
        var b1 = new Vector3(centerX + outerHalfWidth, innerY, backZ);
        var b2 = new Vector3(centerX + innerHalfWidth, innerY, frontZ);
        var b3 = new Vector3(centerX - innerHalfWidth, innerY, frontZ);
        builder.AddQuad(a0, a1, a2, a3);
        builder.AddQuad(b3, b2, b1, b0);
        builder.AddQuad(a0, b0, b1, a1);
        builder.AddQuad(a1, b1, b2, a2);
        builder.AddQuad(a2, b2, b3, a3);
        builder.AddQuad(a3, b3, b0, a0);
    }

    private void AddArtwork(BitmapSource? bitmap, float left, float right, float bottom, float top,
        float z, bool reverse, GroupModel3D? target = null, string name = "Artwork")
    {
        var builder = new MeshBuilder(true, true, true);
        if (!reverse)
        {
            // Counter-clockwise from the camera: normal points toward +Z.
            builder.AddQuad(new Vector3(left, top, z), new Vector3(left, bottom, z),
                new Vector3(right, bottom, z), new Vector3(right, top, z),
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }
        else
        {
            // Back face normal points toward -Z.
            builder.AddQuad(new Vector3(left, top, z), new Vector3(right, top, z),
                new Vector3(right, bottom, z), new Vector3(left, bottom, z),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        var material = new PhongMaterial
        {
            Name = name,
            DiffuseColor = new Color4(1, 1, 1, 1),
            DiffuseMap = CreateTexture(bitmap),
            RenderDiffuseMap = bitmap is not null,
            // Printed artwork is behind the acrylic. Keep its own highlight
            // subtle so pale scans retain fine lines instead of clipping.
            SpecularColor = new Color4(0.035f, 0.035f, 0.035f, 1),
            SpecularShininess = 10,
            EnableAutoTangent = true
        };
        // The rear plane is viewed through an STL half whose source winding is
        // reversed during assembly. Disable back-face culling for that plane;
        // otherwise a valid Back texture is discarded even though the side
        // Spine textures remain visible.
        AddMesh(builder.ToMeshGeometry3D(), material, false, reverse, target);
    }

    private void AddSpine(BitmapSource? bitmap, float x, float height, float depth, bool leftSide,
        GroupModel3D? target = null, BitmapSource? insideBitmap = null)
    {
        var builder = new MeshBuilder(true, true, true);
        if (leftSide)
        {
            // Source-right strip: U=0 meets the Back and U=1 reaches Front.
            // Winding faces outward (-X).
            builder.AddQuad(new Vector3(x, height / 2, depth / 2 - 0.01f),
                new Vector3(x, height / 2, -depth / 2 + 0.01f),
                new Vector3(x, -height / 2, -depth / 2 + 0.01f),
                new Vector3(x, -height / 2, depth / 2 - 0.01f),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        else
        {
            // Source-left strip: U=1 meets the Back and U=0 reaches Front.
            // Winding faces outward (+X).
            builder.AddQuad(new Vector3(x, height / 2, -depth / 2 + 0.01f),
                new Vector3(x, height / 2, depth / 2 - 0.01f),
                new Vector3(x, -height / 2, depth / 2 - 0.01f),
                new Vector3(x, -height / 2, -depth / 2 + 0.01f),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        AddMesh(builder.ToMeshGeometry3D(), new PhongMaterial
        {
            Name = "Spine artwork",
            DiffuseColor = new Color4(1, 1, 1, 1),
            DiffuseMap = CreateTexture(bitmap),
            RenderDiffuseMap = bitmap is not null,
            SpecularColor = new Color4(0.035f, 0.035f, 0.035f, 1),
            SpecularShininess = 10
        }, false, false, target);

        // Only an explicitly assigned Inlay prints the inside. Do not mirror
        // exterior spine artwork onto the paper reverse. Fold each inner strip
        // from its matching edge of the central Inlay panel.
        var reverse = new MeshBuilder(true, true, true);
        var innerX = x + (leftSide ? 0.001f : -0.001f);
        if (leftSide)
        {
            reverse.AddQuad(new Vector3(innerX, height / 2, -depth / 2 + 0.01f),
                new Vector3(innerX, height / 2, depth / 2 - 0.01f),
                new Vector3(innerX, -height / 2, depth / 2 - 0.01f),
                new Vector3(innerX, -height / 2, -depth / 2 + 0.01f),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        else
        {
            reverse.AddQuad(new Vector3(innerX, height / 2, depth / 2 - 0.01f),
                new Vector3(innerX, height / 2, -depth / 2 + 0.01f),
                new Vector3(innerX, -height / 2, -depth / 2 + 0.01f),
                new Vector3(innerX, -height / 2, depth / 2 - 0.01f),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        AddMesh(reverse.ToMeshGeometry3D(), new PhongMaterial
        {
            Name = "Spine paper reverse",
            DiffuseColor = insideBitmap is null ? new Color4(0.82f, 0.80f, 0.73f, 1) : new Color4(1, 1, 1, 1),
            DiffuseMap = CreateTexture(insideBitmap),
            RenderDiffuseMap = insideBitmap is not null,
            SpecularColor = new Color4(0.02f, 0.02f, 0.02f, 1),
            SpecularShininess = 4
        }, false, false, target);
    }

    private void AddSpineCard(BitmapSource? bitmap, float caseWidth, float caseHeight, float caseDepth)
    {
        if (bitmap is null) return;
        var (back, spine, front) = SpineCardArtwork.Split(bitmap);
        // Fit the detected centre panel to the complete outside width of the
        // physical spine, and use that horizontal scale for both flaps. Height
        // is independent: scanner proportions and fold detection must not make
        // a tall obi extend behind the case frame and lose its top/bottom edge.
        var wrappedSpineWidth = caseDepth + 0.030f;
        var horizontalScale = wrappedSpineWidth / Math.Max(1, spine.PixelWidth);
        var backWidth = horizontalScale * back.PixelWidth;
        var frontWidth = horizontalScale * front.PixelWidth;
        // Leave a visible gap beside the closed case. A small forward depth
        // separation prevents the folded paper from disappearing beneath the
        // opaque lid after it opens to the left.
        _spineCardRemovedOffsetX = -(Math.Max(backWidth, frontWidth) + 0.34f);
        const float obiHeightMm = 120f;
        const float caseHeightMm = 125f;
        var cardHeight = caseHeight * obiHeightMm / caseHeightMm;
        var outsideX = -caseWidth / 2 - 0.014f;
        // All three paper faces share the same physical fold line. Separating
        // the flap origin from the side plane leaves a visible crack at steep
        // viewing angles.
        var foldX = outsideX;
        var frontZ = caseDepth / 2 + 0.015f;
        var backZ = -caseDepth / 2 - 0.015f;

        // The source is laid flat as Back flap | Spine | Front flap. Each flap
        // stays with the physical case face it covers when the lid is opened.
        AddArtwork(back, foldX, foldX + backWidth, -cardHeight / 2, cardHeight / 2,
            backZ, true, _spineCardRoot, "Spine Card back flap");
        AddArtwork(front, foldX, foldX + frontWidth, -cardHeight / 2, cardHeight / 2,
            frontZ, false, _spineCardRoot, "Spine Card front flap");

        var side = new MeshBuilder(true, true, true);
        side.AddQuad(new Vector3(outsideX, cardHeight / 2, frontZ),
            new Vector3(outsideX, cardHeight / 2, backZ),
            new Vector3(outsideX, -cardHeight / 2, backZ),
            new Vector3(outsideX, -cardHeight / 2, frontZ),
            new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        AddMesh(side.ToMeshGeometry3D(), new PhongMaterial
        {
            Name = "Spine Card spine",
            DiffuseColor = new Color4(1, 1, 1, 1),
            DiffuseMap = CreateTexture(spine),
            RenderDiffuseMap = true,
            SpecularColor = new Color4(0.02f, 0.02f, 0.02f, 1),
            SpecularShininess = 6,
            EnableAutoTangent = true
        }, false, false, _spineCardRoot);
    }

    private void AddDisc(BitmapSource? bitmap, Vector3 center, float outerRadius, float innerRadius,
        float thickness, GroupModel3D target)
    {
        const int segments = 96;
        var top = new MeshBuilder(true, true, true);
        var underside = new MeshBuilder(true, true, true);
        var clearHub = new MeshBuilder(true, true, true);
        var edges = new MeshBuilder(true, true, true);
        var rainbowBands = new[]
        {
            (Builder: new MeshBuilder(true, true, true), Radius: outerRadius * 0.47f),
            (Builder: new MeshBuilder(true, true, true), Radius: outerRadius * 0.68f),
            (Builder: new MeshBuilder(true, true, true), Radius: outerRadius * 0.86f)
        };
        var hubOuterRadius = outerRadius * 0.285f;
        var bottomZ = center.Z - thickness;
        for (var index = 0; index < segments; index++)
        {
            var angle0 = (float)(Math.PI * 2 * index / segments);
            var angle1 = (float)(Math.PI * 2 * (index + 1) / segments);
            Vector3 Point(float angle, float radius, float z) =>
                new(center.X + MathF.Cos(angle) * radius,
                    center.Y + MathF.Sin(angle) * radius, z);
            var inner0 = Point(angle0, innerRadius, center.Z);
            var outer0 = Point(angle0, outerRadius, center.Z);
            var outer1 = Point(angle1, outerRadius, center.Z);
            var inner1 = Point(angle1, innerRadius, center.Z);
            var inner0Bottom = inner0 - new Vector3(0, 0, thickness);
            var outer0Bottom = outer0 - new Vector3(0, 0, thickness);
            var outer1Bottom = outer1 - new Vector3(0, 0, thickness);
            var inner1Bottom = inner1 - new Vector3(0, 0, thickness);
            Vector2 Uv(Vector3 point) => new(0.5f + (point.X - center.X) / (outerRadius * 2),
                0.5f - (point.Y - center.Y) / (outerRadius * 2));
            top.AddQuad(inner0, outer0, outer1, inner1,
                Uv(inner0), Uv(outer0), Uv(outer1), Uv(inner1));

            var hub0 = Point(angle0, hubOuterRadius, bottomZ);
            var hub1 = Point(angle1, hubOuterRadius, bottomZ);
            underside.AddQuad(hub1, outer1Bottom, outer0Bottom, hub0);
            clearHub.AddQuad(inner1Bottom, hub1, hub0, inner0Bottom);
            edges.AddQuad(outer0Bottom, outer0, outer1, outer1Bottom);
            edges.AddQuad(inner1Bottom, inner1, inner0, inner0Bottom);

            foreach (var band in rainbowBands)
            {
                const float halfBandWidth = 0.008f;
                var innerBand0 = Point(angle0, band.Radius - halfBandWidth, bottomZ - 0.0006f);
                var outerBand0 = Point(angle0, band.Radius + halfBandWidth, bottomZ - 0.0006f);
                var innerBand1 = Point(angle1, band.Radius - halfBandWidth, bottomZ - 0.0006f);
                var outerBand1 = Point(angle1, band.Radius + halfBandWidth, bottomZ - 0.0006f);
                band.Builder.AddQuad(innerBand1, outerBand1, outerBand0, innerBand0);
            }
        }

        DxMaterial topMaterial;
        if (bitmap is not null)
        {
            topMaterial = new PhongMaterial
            {
                Name = "Disc artwork",
                DiffuseColor = new Color4(1, 1, 1, 1),
                DiffuseMap = CreateTexture(bitmap),
                RenderDiffuseMap = true,
                SpecularColor = new Color4(0.16f, 0.16f, 0.16f, 1),
                SpecularShininess = 34
            };
        }
        else
        {
            topMaterial = new PBRMaterial
            {
                Name = "Compact disc label side",
                AlbedoColor = new Color4(0.63f, 0.69f, 0.73f, 1),
                MetallicFactor = 0.72,
                RoughnessFactor = 0.18,
                ReflectanceFactor = 0.72,
                ClearCoatStrength = 0.32,
                ClearCoatRoughness = 0.08,
                RenderEnvironmentMap = true
            };
        }
        var undersideMaterial = new PBRMaterial
        {
            Name = "CD silver recording surface",
            AlbedoColor = new Color4(0.70f, 0.75f, 0.77f, 1),
            MetallicFactor = 0.88,
            RoughnessFactor = 0.105,
            ReflectanceFactor = 0.92,
            ClearCoatStrength = 0.46,
            ClearCoatRoughness = 0.045,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var hubMaterial = new PBRMaterial
        {
            Name = "CD clear inner hub",
            AlbedoColor = new Color4(0.72f, 0.78f, 0.80f, 0.42f),
            MetallicFactor = 0.08,
            RoughnessFactor = 0.075,
            ReflectanceFactor = 0.68,
            ClearCoatStrength = 0.52,
            ClearCoatRoughness = 0.035,
            RenderEnvironmentMap = true
        };
        var edgeMaterial = new PBRMaterial
        {
            Name = "CD cut edges",
            AlbedoColor = new Color4(0.52f, 0.57f, 0.59f, 1),
            MetallicFactor = 0.34,
            RoughnessFactor = 0.24,
            ReflectanceFactor = 0.58,
            RenderEnvironmentMap = true
        };
        AddMesh(top.ToMeshGeometry3D(), topMaterial, false, false, target);
        AddMesh(underside.ToMeshGeometry3D(), undersideMaterial, false, false, target);
        AddMesh(clearHub.ToMeshGeometry3D(), hubMaterial, true, false, target);
        AddMesh(edges.ToMeshGeometry3D(), edgeMaterial, false, true, target);

        var rainbowColors = new[]
        {
            new Color4(0.18f, 0.74f, 0.82f, 0.24f),
            new Color4(0.76f, 0.25f, 0.67f, 0.20f),
            new Color4(0.88f, 0.68f, 0.22f, 0.18f)
        };
        for (var index = 0; index < rainbowBands.Length; index++)
        {
            AddMesh(rainbowBands[index].Builder.ToMeshGeometry3D(), new PBRMaterial
            {
                Name = "CD interference band",
                AlbedoColor = rainbowColors[index],
                MetallicFactor = 0.62,
                RoughnessFactor = 0.08,
                ReflectanceFactor = 0.86,
                ClearCoatStrength = 0.30,
                ClearCoatRoughness = 0.04,
                RenderEnvironmentMap = true
            }, true, false, target, false);
        }
    }

    private void AddMesh(DxMesh geometry, DxMaterial material, bool transparent, bool twoSided = false,
        GroupModel3D? target = null, bool castsShadow = true)
    {
        (target ?? _caseRoot).Children.Add(new MeshGeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            IsTransparent = transparent,
            IsThrowingShadow = castsShadow && !transparent,
            IsHitTestVisible = true,
            CullMode = twoSided ? DxCullMode.None : DxCullMode.Back
        });
    }

    private void AddStudioCard(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color4 color)
    {
        var builder = new MeshBuilder(true, false, false);
        builder.AddQuad(a, b, c, d);
        var material = new PBRMaterial
        {
            Name = "Studio reflection card",
            AlbedoColor = color,
            EmissiveColor = color,
            MetallicFactor = 0,
            RoughnessFactor = 1,
            ReflectanceFactor = 0
        };
        Viewport.Items.Add(new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = material,
            IsHitTestVisible = false,
            IsThrowingShadow = false
        });
    }

    private TextureModel? CreateTexture(BitmapSource? bitmap)
    {
        if (bitmap is null) return null;
        if (_textureCache.TryGetValue(bitmap, out var cached)) return cached;

        BitmapSource source = bitmap;
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            source = converted;
        }
        var stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        // WPF album art is encoded in sRGB. Marking it as linear UNorm makes
        // mid-tones too bright and washes out fine pencil/line artwork.
        var texture = new TextureModel(pixels, SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb,
            source.PixelWidth, source.PixelHeight);
        _textureCache[bitmap] = texture;
        return texture;
    }

    public void Dispose()
    {
        CancelSpineCardMotion();
        CancelBookletMotion();
        _animationRenderTimer.Stop();
        Viewport.Items.Clear();
        _textureCache.Clear();
        _effects.Dispose();
    }
}
