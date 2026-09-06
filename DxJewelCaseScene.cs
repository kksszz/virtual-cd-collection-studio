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
    // Dimensions fixed from the supplied scans: 142 x 125 x 10.0 mm closed.
    // Treat this as the outer-envelope constraint. Moulded rails must remain
    // inside it and may never be added on top of the front or rear surface.
    internal const float StandardCaseDepthMm = 10.0f;
    internal const float StandardCaseDepth = 0.177f;
    // A tray card is 150 mm wide: a 138 mm rear panel plus two 6 mm folds.
    // Keep that physical print width, centred between the case's clear lips.
    internal const float StandardVisibleSpineWidthMm = 6f;
    internal const float StandardVisibleSpineDepth =
        StandardCaseDepth * StandardVisibleSpineWidthMm / StandardCaseDepthMm;
    private const float OpeningSideRearLipMm = 0.8f;
    private const float OpeningSideTrayBandMm = 2.0f;
    // The paper is below the clear outer wall, between acrylic and tray.
    internal const float StandardSpinePaperInset = 0.010f;
    // The opening-side parametric lid has a 2 mm perimeter wall. Keep that
    // spine behind the wall's inner face instead of intersecting its volume.
    internal const float OpeningSideSpinePaperInset = 0.036f;
    internal const float WrappingSideClearance = 0.016f;
    internal const float WrappingFaceClearance = 0.010f;
    internal const float WrappingEdgeClearance = 0.010f;
    internal const float SpineCardFlapClearance = 0.006f;
    // The printable parts are exported in their open, print-bed orientation:
    // the bottom hinge is on the left while the top hinge is on the right.
    // After mirroring the top into its assembled orientation, both hinge
    // barrels meet slightly inboard of the outer left edge.
    // Circle centres measured from both normalized STL hinge bores are
    // -1.12576 (base) and -1.12743 (lid). Use their common centre and build
    // the replacement lid's hinge rail around this exact axis.
    private const double AssembledHingeX = -1.12660;
    private static readonly Lazy<StlCaseGeometry> CaseGeometry = new(LoadStlCaseGeometry);
    private static readonly Lazy<CoverFlowShellGeometry> CoverFlowGeometry = new(CreateCoverFlowShellGeometry);
    private readonly DefaultEffectsManager _effects = new();
    private readonly GroupModel3D _caseRoot = new();
    private readonly GroupModel3D _baseRoot = new();
    private readonly GroupModel3D _lidRoot = new();
    private readonly GroupModel3D _frontPanelRoot = new();
    private readonly TranslateTransform3D _frontPanelSeatTranslation = new();
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
    // Keep the extracted obi just in front of the lid.  The old negative
    // offset moved its front flap through the acrylic while it slid sideways,
    // so the case depth buffer cut away part of the printed face.
    private const double SpineCardRemovedOffsetZ = 0.024;
    private const double SpineCardOpenClearanceX = -2.35;
    private Point3D? _spineCardDragPoint;
    private Vector3D _spineCardDragNormal;
    private readonly GroupModel3D _wrappingRoot = new();
    private readonly GroupModel3D _wrappingUpperRoot = new();
    private readonly GroupModel3D _wrappingLowerRoot = new();
    private readonly GroupModel3D _tearTapeRoot = new();
    private readonly GroupModel3D _tearTapeFrontRoot = new();
    private readonly GroupModel3D _tearTapeBackRoot = new();
    private readonly GroupModel3D _tearTapeSideRoot = new();
    private readonly GroupModel3D _tearTapeRibbonRoot = new();
    private readonly TranslateTransform3D _wrappingUpperTranslation = new();
    private readonly TranslateTransform3D _wrappingLowerTranslation = new();
    private readonly TranslateTransform3D _tearTapeTranslation = new();
    private readonly ScaleTransform3D _tearTapeScale = new(1, 1, 1);
    private readonly ScaleTransform3D _tearTapeBackScale = new(1, 1, 1);
    private MeshGeometryModel3D? _tearTapeTabModel;
    private MeshGeometryModel3D? _tearTapeRibbonModel;
    private float _wrappingCaseWidth;
    private float _wrappingCaseDepth;
    private float _wrappingTapeY;
    private float _wrappingUpperClearanceY;
    private float _wrappingLowerClearanceY;
    private readonly AxisAngleRotation3D _wrappingUpperPeel = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _wrappingLowerPeel = new(new Vector3D(1, 0, 0), 0);
    private readonly RotateTransform3D _wrappingUpperRotation;
    private readonly RotateTransform3D _wrappingLowerRotation;
    private readonly DispatcherTimer _wrappingMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private TaskCompletionSource<bool>? _wrappingMotionCompletion;
    private EventHandler? _wrappingMotionTick;
    private double _wrappingProgress;
    private System.Windows.Point? _wrappingDragPoint;
    private bool _draggingWrappingFilm;
    private readonly GroupModel3D _bookletRoot = new();
    private readonly TranslateTransform3D _bookletTranslation = new();
    private readonly AxisAngleRotation3D _bookletTilt = new(new Vector3D(0, 1, 0), 0);
    private readonly RotateTransform3D _bookletLift;
    private readonly DispatcherTimer _bookletMotionTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private TaskCompletionSource<bool>? _bookletMotionCompletion;
    private double _bookletProgress;
    private readonly GroupModel3D _discRoot = new();
    private readonly GroupModel3D _secondDiscRoot = new();
    private readonly AxisAngleRotation3D _discSpinRotation = new(new Vector3D(0, 0, 1), 0);
    private readonly AxisAngleRotation3D _secondDiscSpinRotation = new(new Vector3D(0, 0, 1), 0);
    private readonly System.Diagnostics.Stopwatch _discSpinClock = new();
    private bool _discPlaying;
    private string? _discItemKey;
    private double _discSpinStartAngle;
    internal const double DiscPlaybackRpm = 240;
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
    private bool _interactiveMotion;
    private bool _interactiveRenderPending;
    private bool _disposed;
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
        // The reflected studio cards are fixed. Rebuilding all six cubemap
        // faces for every mouse move adds GPU work without changing the scene.
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
        _lidRoot.Transform = new RotateTransform3D(
            _lidHingeRotation, new Point3D(AssembledHingeX, 0, 0));
        _frontPanelRoot.Transform = _frontPanelSeatTranslation;
        _bookletLift = new RotateTransform3D(_bookletTilt);
        var bookletTransform = new Transform3DGroup();
        bookletTransform.Children.Add(_bookletLift);
        bookletTransform.Children.Add(_bookletTranslation);
        _bookletRoot.Transform = bookletTransform;
        var discTransform = new Transform3DGroup();
        discTransform.Children.Add(new RotateTransform3D(_discSpinRotation, new Point3D(0.044, 0.004, 0)));
        discTransform.Children.Add(new RotateTransform3D(_discTiltRotation));
        discTransform.Children.Add(_discTranslation);
        _discRoot.Transform = discTransform;
        _secondDiscRoot.Transform = new RotateTransform3D(
            _secondDiscSpinRotation, new Point3D(0.044, 0.004, 0));
        var spineCardTransform = new Transform3DGroup();
        spineCardTransform.Children.Add(_spineCardTranslation);
        spineCardTransform.Children.Add(_spineCardOpenTranslation);
        spineCardTransform.Children.Add(_spineCardDragTranslation);
        _spineCardRoot.Transform = spineCardTransform;
        _wrappingUpperRotation = new RotateTransform3D(_wrappingUpperPeel);
        _wrappingLowerRotation = new RotateTransform3D(_wrappingLowerPeel);
        var wrappingUpperTransform = new Transform3DGroup();
        wrappingUpperTransform.Children.Add(_wrappingUpperRotation);
        wrappingUpperTransform.Children.Add(_wrappingUpperTranslation);
        _wrappingUpperRoot.Transform = wrappingUpperTransform;
        var wrappingLowerTransform = new Transform3DGroup();
        wrappingLowerTransform.Children.Add(_wrappingLowerRotation);
        wrappingLowerTransform.Children.Add(_wrappingLowerTranslation);
        _wrappingLowerRoot.Transform = wrappingLowerTransform;
        _tearTapeFrontRoot.Transform = _tearTapeScale;
        _tearTapeBackRoot.Transform = _tearTapeBackScale;
        _tearTapeRibbonRoot.Transform = _tearTapeTranslation;
        _tearTapeRoot.Children.Add(_tearTapeFrontRoot);
        _tearTapeRoot.Children.Add(_tearTapeBackRoot);
        _tearTapeRoot.Children.Add(_tearTapeSideRoot);
        _tearTapeRoot.Children.Add(_tearTapeRibbonRoot);
        _wrappingRoot.Children.Add(_wrappingUpperRoot);
        _wrappingRoot.Children.Add(_wrappingLowerRoot);
        _wrappingRoot.Children.Add(_tearTapeRoot);
        _caseRoot.Children.Add(_baseRoot);
        _caseRoot.Children.Add(_lidRoot);
        _caseRoot.Children.Add(_spineCardRoot);
        _caseRoot.Children.Add(_wrappingRoot);

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

        // Product-photography light cards sit behind the fixed camera but stay
        // visible to the reflection probe. Keeping them merely beside the
        // normal frustum allowed their grey faces to enter the extreme edges
        // on wide/full-screen viewports.
        AddStudioCard(
            new Vector3(-3.5f, 2.7f, 9.0f), new Vector3(3.5f, 2.7f, 9.0f),
            new Vector3(3.5f, 2.7f, 6.1f), new Vector3(-3.5f, 2.7f, 6.1f),
            new Color4(0.18f, 0.18f, 0.17f, 1));
        AddStudioCard(
            new Vector3(-3.8f, 2.3f, 6.1f), new Vector3(-3.8f, 2.3f, 9.2f),
            new Vector3(-3.8f, -2.3f, 9.2f), new Vector3(-3.8f, -2.3f, 6.1f),
            new Color4(0.09f, 0.09f, 0.085f, 1));
        AddStudioCard(
            new Vector3(3.8f, 2.0f, 9.2f), new Vector3(3.8f, 2.0f, 6.1f),
            new Vector3(3.8f, -2.0f, 6.1f), new Vector3(3.8f, -2.0f, 9.2f),
            new Color4(0.055f, 0.055f, 0.052f, 1));

        _reflection.Children.Add(_caseRoot);
        Viewport.Items.Add(_reflection);
    }

    public void SetItem(JewelCaseCoverFlowItem item, double yaw, double pitch)
    {
        if (!string.Equals(_discItemKey, item.Key, StringComparison.OrdinalIgnoreCase))
        {
            // A scene can be retained while CoverFlow selects another album.
            // Never carry the former disc's running clock or angular phase
            // into the newly constructed case.
            SetDiscPlaying(false);
            _discSpinRotation.Angle = 0;
            _secondDiscSpinRotation.Angle = 0;
            _discSpinStartAngle = 0;
            _discItemKey = item.Key;
        }
        CancelWrappingMotion();
        EndWrappingDrag();
        EndDiscDrag();
        EndSpineCardDrag();
        ResetSpineCardDragOffset();
        _baseRoot.Children.Clear();
        _lidRoot.Children.Clear();
        _frontPanelRoot.Children.Clear();
        _spineCardRoot.Children.Clear();
        _wrappingUpperRoot.Children.Clear();
        _wrappingLowerRoot.Children.Clear();
        _tearTapeFrontRoot.Children.Clear();
        _tearTapeBackRoot.Children.Clear();
        _tearTapeSideRoot.Children.Clear();
        _tearTapeRibbonRoot.Children.Clear();
        _tearTapeTabModel = null;
        _tearTapeRibbonModel = null;
        _bookletRoot.Children.Clear();
        _frontPanelRoot.Children.Add(_bookletRoot);
        _lidRoot.Children.Add(_frontPanelRoot);
        _discRoot.Children.Clear();
        _secondDiscRoot.Children.Clear();
        _baseRoot.Children.Add(_secondDiscRoot);
        _baseRoot.Children.Add(_discRoot);
        const float width = 2.42f;
        const float height = 2.12f;
        const float depth = StandardCaseDepth;
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
            // Keep the rail readable through highlights, but do not let its
            // body become a broad grey strip in a near-spine view.
            AlbedoColor = new Color4(0.52f, 0.54f, 0.55f, 0.23f),
            MetallicFactor = 0,
            RoughnessFactor = 0.16,
            ReflectanceFactor = 0.46,
            ClearCoatStrength = 0.34,
            ClearCoatRoughness = 0.09,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var frontSideWallAcrylic = new PBRMaterial
        {
            Name = "Clear front-lid side walls",
            // Seen square-on from a spine view, a 2 mm acrylic wall must read
            // as a continuous solid between the front plate and mating seam.
            // Using the very pale broad-panel material made this volume vanish
            // into the background and the lid looked suspended above the tray.
            AlbedoColor = new Color4(0.68f, 0.70f, 0.71f, 0.38f),
            MetallicFactor = 0,
            RoughnessFactor = 0.13,
            ReflectanceFactor = 0.48,
            ClearCoatStrength = 0.38,
            ClearCoatRoughness = 0.07,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var spineWindowAcrylic = new PBRMaterial
        {
            Name = "Clear opening-side Spine window",
            // Only the long face over the printed Spine uses this clearer
            // resin. Keep the denser material on the surrounding end frame,
            // catches and top/bottom rails so their moulded shape is retained.
            AlbedoColor = new Color4(0.88f, 0.90f, 0.91f, 0.11f),
            MetallicFactor = 0,
            RoughnessFactor = 0.10,
            ReflectanceFactor = 0.44,
            ClearCoatStrength = 0.32,
            ClearCoatRoughness = 0.06,
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
            // Sampled from the moulded main-tray area of img129.jpg.  The
            // scan median is sRGB (51, 50, 56); PBR albedo is linear RGB.
            "Black" => new Color4(0.0331f, 0.0319f, 0.0395f, 1),
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
        var traySpineGroove = new PBRMaterial
        {
            Name = "Tray spine groove floor",
            AlbedoColor = new Color4(trayColor.Red * 0.58f, trayColor.Green * 0.58f,
                trayColor.Blue * 0.58f, trayIsClear ? 0.16f : trayColor.Alpha),
            EmissiveColor = new Color4(0.0015f, 0.0015f, 0.0015f, 1),
            MetallicFactor = 0,
            RoughnessFactor = 0.58,
            ReflectanceFactor = trayIsClear ? 0.34 : 0.12,
            RenderEnvironmentMap = trayIsClear
        };
        var traySpineRib = new PBRMaterial
        {
            Name = "Tray spine ribs",
            AlbedoColor = trayColor,
            EmissiveColor = tray.EmissiveColor,
            MetallicFactor = 0,
            RoughnessFactor = trayIsClear ? 0.10 : 0.34,
            ReflectanceFactor = trayIsClear ? 0.46 : 0.28,
            ClearCoatStrength = trayIsClear ? 0.30 : 0,
            RenderEnvironmentMap = trayIsClear
        };
        var traySpineTransition = new PBRMaterial
        {
            Name = "Tray spine transition",
            AlbedoColor = trayColor,
            EmissiveColor = tray.EmissiveColor,
            MetallicFactor = 0,
            RoughnessFactor = tray.RoughnessFactor,
            ReflectanceFactor = tray.ReflectanceFactor,
            ClearCoatStrength = tray.ClearCoatStrength,
            RenderEnvironmentMap = trayIsClear
        };
        var bookletPageEdge = new PBRMaterial
        {
            Name = "Booklet page edges",
            // Keep the unprinted 1.5 mm page block visibly distinct from the
            // image-wrapped covers and fold.  The former warm near-white read
            // as an untextured gap under the scene lighting.
            AlbedoColor = new Color4(0.43f, 0.43f, 0.43f, 1),
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
        // Rebuild from the two source shells instead of layering corrective
        // boxes over them. The top STL is the complete rotating front lid;
        // its panel, rails, catches and hinge knuckles must remain one rigid
        // part. The lower STL is split only by material into the fixed clear
        // rear shell and its removable tray.
        AddMesh(shell.TopLid, acrylic, true, true, _frontPanelRoot);
        AddMesh(shell.TopMouldedEdges, frontSideWallAcrylic,
            true, true, _frontPanelRoot);
        AddTopBottomSideRibs(width, height, depth, mouldedEdgeAcrylic);

        // Once the printable STL's false tray wall is removed, the opening
        // edge must still be closed by the two transparent shell rails. Extend
        // the rear and front rails to the common Z=0 mating plane. They remain
        // separate rigid parts, but meet without an empty band when closed.
        AddOpeningSideMatingRails(width, height, depth,
            spineWindowAcrylic);

        // Continue the tray strip up to the inner edges of the two 2 mm clear
        // shell rails. It must not stop 4.5 mm early as before, nor pass through
        // the rails as a full 125 mm strip.
        AddTraySpineCover(width, height, depth, tray, traySpineGroove,
            traySpineRib, traySpineTransition, trayIsClear);

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
        // hinge-side retaining lip. Its artwork must begin after that lip
        // rather than running underneath the clear moulding.
        var bookletLeft = Math.Max(discCenterX - discOuterRadius,
            TrayManualStopRight(width) + 0.002f);
        var bookletRight = frontPlaneCenterX + bookletWidthMm / 2;
        var bookletBottom = frontPlaneCenterY - bookletHeightMm / 2;
        var bookletTop = frontPlaneCenterY + bookletHeightMm / 2;
        var bookletWidth = bookletRight - bookletLeft;
        var bookletHeight = bookletTop - bookletBottom;
        // A real jewel case has a 1 mm clear panel whose perimeter frame sits
        // another 0.5 mm outward. The booklet rests immediately behind that
        // panel, not at the frame's outer surface.
        _bookletLift.CenterX = (bookletLeft + bookletRight) / 2;
        _bookletLift.CenterY = (bookletBottom + bookletTop) / 2;
        _bookletLift.CenterZ = shell.FrontArtworkArea.Z;
        // A multi-page manual is about 1.5 mm thick. Keep its printed front at
        // the acrylic contact plane and grow the page block inward, far enough
        // to sit visibly beneath the lid's retaining claws.
        var bookletThickness = 1.5f * depth / StandardCaseDepthMm;
        var bookletFrontZ = shell.FrontArtworkArea.Z - 0.0005f;
        var bookletRearZ = bookletFrontZ - bookletThickness;
        AddOpenBookletPageBlock(bookletLeft, bookletRight,
            bookletBottom, bookletTop, bookletRearZ, bookletFrontZ,
            bookletPageEdge, leaveFoldOpen: item.FrontCover is not null);
        if (item.FrontCover is not null)
            AddBookletFoldArtwork(item.FrontCover,
                item.InsideFrontCover ?? item.FrontCover,
                bookletLeft, bookletBottom, bookletTop,
                bookletRearZ, bookletFrontZ);

        // Fine stepped edges keep the block readable as pages rather than a
        // single plastic slab.
        const int visiblePageEdges = 8;
        for (var page = 0; page < visiblePageEdges; page++)
        {
            var pageZ = bookletFrontZ
                - bookletThickness * (page + 0.5f) / visiblePageEdges;
            AddBox(new Vector3(bookletRight + 0.0015f, 0, pageZ),
                0.0025f, bookletHeight - 0.008f,
                bookletThickness / visiblePageEdges * 0.55f,
                bookletPageEdge, false, _bookletRoot, false);
            AddBox(new Vector3((bookletLeft + bookletRight) / 2, bookletBottom - 0.0015f, pageZ),
                bookletWidth - 0.008f, 0.0025f,
                bookletThickness / visiblePageEdges * 0.55f,
                bookletPageEdge, false, _bookletRoot, false);
        }
        AddArtwork(item.FrontCover, bookletLeft, bookletRight, bookletBottom, bookletTop,
            shell.FrontArtworkArea.Z - 0.0001f, false, _bookletRoot);
        AddArtwork(item.InsideFrontCover, bookletLeft, bookletRight, bookletBottom, bookletTop,
            bookletRearZ - 0.0001f, true, _bookletRoot);

        // The rear insert is 150 x 118 mm including two 6 mm spines. The flat
        // back window therefore displays the central 138 x 118 mm panel.
        var backArtworkWidth = 138f * unitX;
        var backArtworkHeight = 118f * unitY;
        // Missing panels stay unprinted; unrelated artwork must not be stretched onto them.
        AddArtwork(item.BackCover,
            -backArtworkWidth / 2, backArtworkWidth / 2,
            -backArtworkHeight / 2, backArtworkHeight / 2,
            // The lower STL includes an opaque central plate at the rear outer
            // surface. Keep the zero-thickness print immediately outside that
            // plate so it remains visible; this does not add structural depth.
            -depth / 2 - 0.0002f,
            true, _baseRoot);
        AddBackArtworkFrame(width, height, backArtworkWidth, backArtworkHeight,
            -depth / 2 - 0.0005f,
            backFrameAcrylic);
        var inlay = item.SplitInlay();
        // Two independent printed sides of the same sheet. Keep the interior
        // below the tray floor, so opaque resin still hides it naturally.
        AddArtwork(inlay.Panel,
            -backArtworkWidth / 2, backArtworkWidth / 2,
            -backArtworkHeight / 2, backArtworkHeight / 2,
            -depth / 2 + depth * 1.5f / StandardCaseDepthMm + 0.003f,
            false, _baseRoot, "Inlay artwork");
        // Back is viewed from -Z: its source-right edge is at world -X.
        // Inlay faces +Z, so its left/right strips stay in world order.
        AddSpine(item.RightSpineCover,
            -width / 2 + StandardSpinePaperInset, backArtworkHeight, depth, true, _baseRoot, inlay.Left);
        AddSpine(item.SpineCover,
            width / 2 - OpeningSideSpinePaperInset,
            backArtworkHeight, depth, false, _baseRoot, inlay.Right);
        AddSpineCard(item.SpineCard, width, height, depth);
        // Japanese caramel wrapping is represented only together with an obi.
        // Albums without a Spine Card remain ordinary, directly openable cases.
        if (item.SpineCard is not null) AddCaramelWrapping(width, height, depth);
        // 120 mm disc in a roughly 125 mm-high jewel case.
        AddDisc(item.DiscImage, new Vector3(discCenterX, 0.004f, -depth * 0.136f),
            discOuterRadius, 0.128f, 0.020f, _discRoot);
        if (item.SecondDiscImage is not null)
            AddDisc(item.SecondDiscImage, new Vector3(discCenterX, 0.004f, -depth * 0.32f),
                discOuterRadius, 0.128f, 0.018f, _secondDiscRoot, "Disc 2 artwork");

        SetWrappingProgress(_wrappingProgress);

        ApplyRotation(yaw, pitch);
        RequestRender();
    }

    public void SetRotation(double yaw, double pitch)
    {
        ApplyRotation(yaw, pitch);
        RequestRender();
    }

    public void SetViewPan(double x, double y)
    {
        _viewPan.OffsetX = x;
        _viewPan.OffsetY = y;
        RequestRender();
    }

    public void SetViewZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.55, 2.40);
        _viewZoom.ScaleX = _viewZoom.ScaleY = _viewZoom.ScaleZ = zoom;
        RequestRender();
    }

    public void BeginInteractiveMotion()
    {
        if (_disposed || _interactiveMotion) return;
        _interactiveMotion = true;
        // Preserve geometry, textures, MSAA and transparent OIT. Only the
        // finishing passes that are difficult to perceive in motion are paused.
        Viewport.EnableSSAO = false;
        Viewport.IsShadowMappingEnabled = false;
        Viewport.FXAALevel = FXAALevel.None;
        RequestRender();
    }

    public void EndInteractiveMotion()
    {
        if (_disposed || !_interactiveMotion) return;
        _interactiveMotion = false;
        if (_interactiveRenderPending)
        {
            CompositionTarget.Rendering -= OnInteractiveRenderFrame;
            _interactiveRenderPending = false;
        }
        Viewport.EnableSSAO = true;
        Viewport.IsShadowMappingEnabled = true;
        Viewport.FXAALevel = FXAALevel.Medium;
        // Produce the final still frame immediately at full quality.
        RequestRender();
    }

    private void RequestRender()
    {
        if (_disposed) return;
        if (!_interactiveMotion)
        {
            Viewport.InvalidateRender();
            return;
        }
        // MouseMove can arrive much faster than the display refresh rate. Keep
        // the newest transform, but submit at most one GPU frame per WPF frame.
        if (_interactiveRenderPending) return;
        _interactiveRenderPending = true;
        CompositionTarget.Rendering += OnInteractiveRenderFrame;
    }

    private void OnInteractiveRenderFrame(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnInteractiveRenderFrame;
        _interactiveRenderPending = false;
        if (!_disposed) Viewport.InvalidateRender();
    }

    public double WrappingProgress => _wrappingProgress;
    public const double TearCompleteProgress = 0.5;

    public void SetWrappingOpened(bool opened, bool animate = true)
    {
        EndWrappingDrag();
        if (animate) { _ = AnimateWrappingAsync(opened); return; }
        CancelWrappingMotion();
        SetWrappingProgress(opened ? 1 : 0);
    }

    public void SetWrappingCut(bool cut, bool animate = true)
    {
        EndWrappingDrag();
        if (animate) { _ = AnimateWrappingCutAsync(cut); return; }
        CancelWrappingMotion();
        SetWrappingProgress(cut ? TearCompleteProgress : 0);
    }

    public Task<bool> AnimateWrappingAsync(bool opened) =>
        AnimateWrappingToAsync(opened ? 1 : 0);

    public Task<bool> AnimateWrappingCutAsync(bool cut) =>
        AnimateWrappingToAsync(cut ? TearCompleteProgress : 0);

    private Task<bool> AnimateWrappingToAsync(double target)
    {
        EndWrappingDrag();
        CancelWrappingMotion();
        var from = _wrappingProgress;
        if (Math.Abs(from - target) < .001)
        {
            SetWrappingProgress(target);
            return Task.FromResult(true);
        }
        var completion = new TaskCompletionSource<bool>();
        _wrappingMotionCompletion = completion;
        var started = DateTime.UtcNow;
        // Each physical step gets its own deliberate motion: the tear tape is
        // pulled first, then the loosened film is lifted in a separate action.
        var duration = TimeSpan.FromMilliseconds(1500 * Math.Abs(target - from));
        _wrappingMotionTick = (_, _) =>
        {
            var linear = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds
                / Math.Max(1, duration.TotalMilliseconds), 0, 1);
            var eased = linear < .5 ? 4 * linear * linear * linear
                : 1 - Math.Pow(-2 * linear + 2, 3) / 2;
            SetWrappingProgress(from + (target - from) * eased);
            if (linear < 1) return;
            CancelWrappingMotion(true);
        };
        _wrappingMotionTimer.Tick += _wrappingMotionTick;
        _wrappingMotionTimer.Start();
        return completion.Task;
    }

    private void SetWrappingProgress(double progress)
    {
        _wrappingProgress = Math.Clamp(progress, 0, 1);
        // Stage 1: pull only the narrow tape. The broad film deliberately
        // remains tight around the case, matching the supplied reference.
        var tape = Math.Clamp(_wrappingProgress / TearCompleteProgress, 0, 1);
        UpdateTearTapeGeometry(tape);

        // Stage 2: slide the loosened film along the case first.  While any
        // broad face is still touching the acrylic it must translate straight
        // up/down: rotating or moving it towards the viewer here makes the
        // close-fitting film appear to jump free of the case.  Only after both
        // sections clear their respective edges may they peel and be gathered
        // to the right in the same hand-pull direction.
        var peelLinear = Math.Clamp((_wrappingProgress - TearCompleteProgress)
            / (1 - TearCompleteProgress), 0, 1);
        var contactSlide = Math.Sin(Math.Clamp(peelLinear / .68, 0, 1) * Math.PI / 2);
        var releaseLinear = Math.Clamp((peelLinear - .70) / .30, 0, 1);
        var release = 1 - Math.Pow(1 - releaseLinear, 3);
        // Preserve the former final poses, but interpolate toward them only
        // after the close-fitting faces have passed beyond the case edges.
        _wrappingUpperPeel.Angle = -6 * release;
        _wrappingUpperTranslation.OffsetX = 3.10 * release;
        _wrappingUpperTranslation.OffsetY = _wrappingUpperClearanceY * contactSlide + 0.08 * release;
        _wrappingUpperTranslation.OffsetZ = 0.48 * release;
        _wrappingLowerPeel.Angle = 6 * release;
        _wrappingLowerTranslation.OffsetX = 3.10 * release;
        _wrappingLowerTranslation.OffsetY = -_wrappingLowerClearanceY * contactSlide - 0.05 * release;
        _wrappingLowerTranslation.OffsetZ = 0.40 * release;
        RequestRender();
    }

    private void UpdateTearTapeGeometry(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var frontRemoved = Math.Clamp(progress / .48, 0, 1);
        var backRemoved = Math.Clamp((progress - .52) / .48, 0, 1);
        _tearTapeScale.ScaleX = Math.Max(.002, 1 - frontRemoved);
        _tearTapeBackScale.ScaleX = Math.Max(.002, 1 - backRemoved);
        _tearTapeSideRoot.Visibility = progress < .42 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
        if (_tearTapeTabModel is not null)
            _tearTapeTabModel.Visibility = progress <= .004
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;
        _tearTapeTranslation.OffsetX = _tearTapeTranslation.OffsetY = _tearTapeTranslation.OffsetZ = 0;
        if (_tearTapeRibbonModel is null) return;
        if (progress <= .004 || progress >= .996)
        {
            _tearTapeRibbonModel.Visibility = System.Windows.Visibility.Hidden;
            return;
        }

        var left = -_wrappingCaseWidth / 2;
        var right = _wrappingCaseWidth / 2;
        var frontZ = _wrappingCaseDepth / 2 + 0.014f;
        Vector3 start;
        if (progress <= .48)
        {
            var local = (float)(progress / .48);
            start = new Vector3(right - _wrappingCaseWidth * local, _wrappingTapeY, frontZ);
        }
        else
        {
            var local = (float)((progress - .48) / .52);
            if (local < .16f)
                start = new Vector3(left, _wrappingTapeY,
                    frontZ - _wrappingCaseDepth * local / .16f);
            else
                start = new Vector3(left + _wrappingCaseWidth * (local - .16f) / .84f,
                    _wrappingTapeY, -frontZ);
        }

        // While the attachment point passes around the left edge there is no
        // single flat ribbon surface that can be drawn without cutting through
        // the case. Hide that very short turn; recreate it wholly behind the
        // case as soon as the attachment point reaches the rear face.
        var backStage = progress >= .48 + .52 * .16;
        if (progress > .48 && !backStage)
        {
            _tearTapeRibbonModel.Visibility = System.Windows.Visibility.Hidden;
            return;
        }

        // The free end rises toward the user's hand while the section between
        // it and the case sags and twists. Rebuilding this small ribbon mesh is
        // inexpensive and avoids the rigid sliding-strip appearance.
        var p = (float)progress;
        var ribbonFaceZ = backStage
            ? -frontZ - 0.12f
            : frontZ + 0.018f + 0.10f * p + 0.04f * MathF.Sin(p * MathF.PI);
        var end = new Vector3(right + 0.032f - _wrappingCaseWidth * 0.82f * p,
            _wrappingTapeY - 0.012f + 0.82f * p, ribbonFaceZ);
        var control1 = start + new Vector3(-0.18f - 0.14f * p,
            0.12f + 0.18f * p, backStage ? -0.055f : 0.08f + 0.05f * p);
        var control2 = end + new Vector3(0.22f * MathF.Sin(p * MathF.PI * 2.4f),
            -0.20f - 0.08f * MathF.Cos(p * MathF.PI * 1.7f),
            backStage ? -0.035f : 0.10f * MathF.Sin(p * MathF.PI * 3.1f));

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            var u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }
        var ribbon = new MeshBuilder(true, false, false);
        const int segments = 28;
        const float halfWidth = 0.027f;
        Vector3 EdgeOffset(float t)
        {
            var before = Bezier(start, control1, control2, end, Math.Max(0, t - .002f));
            var after = Bezier(start, control1, control2, end, Math.Min(1, t + .002f));
            var tangent = after - before;
            var side = new Vector3(-tangent.Y, tangent.X,
                (backStage ? -0.16f : 0.42f) * MathF.Sin(t * MathF.PI * 3 + p * 7));
            return side.LengthSquared() < 0.000001f
                ? new Vector3(0, halfWidth, 0)
                : Vector3.Normalize(side) * halfWidth;
        }
        var previousPoint = start;
        var previousOffset = EdgeOffset(0);
        for (var index = 1; index <= segments; index++)
        {
            var t = index / (float)segments;
            var point = Bezier(start, control1, control2, end, t);
            var offset = EdgeOffset(t);
            ribbon.AddQuad(previousPoint + previousOffset, previousPoint - previousOffset,
                point - offset, point + offset);
            previousPoint = point;
            previousOffset = offset;
        }
        _tearTapeRibbonModel.Geometry = ribbon.ToMeshGeometry3D();
        _tearTapeRibbonModel.Visibility = System.Windows.Visibility.Visible;
    }

    private void CancelWrappingMotion(bool completed = false)
    {
        _wrappingMotionTimer.Stop();
        if (_wrappingMotionTick is not null)
            _wrappingMotionTimer.Tick -= _wrappingMotionTick;
        _wrappingMotionTick = null;
        var completion = _wrappingMotionCompletion;
        _wrappingMotionCompletion = null;
        if (completed) InvalidateFinalFrame();
        if (completed) completion?.TrySetResult(true);
        else completion?.TrySetResult(false);
    }

    // Helix renders on demand. A transform changed from a Render-priority
    // timer can request its last redraw in the same dispatcher pass in which
    // that timer is stopped; on some systems that redraw is then not presented
    // until the next mouse/keyboard event. Queue two redraws after the current
    // callback has unwound so the committed end pose is always presented.
    private void InvalidateFinalFrame()
    {
        if (_disposed) return;
        Viewport.InvalidateRender();
        _ = Viewport.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_disposed) return;
            Viewport.InvalidateRender();
            _ = Viewport.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (!_disposed) Viewport.InvalidateRender();
            }));
        }));
    }

    public bool BeginWrappingDrag(System.Windows.Point position)
    {
        if (_wrappingProgress >= .999) return false;
        var hit = Viewport.FindHits(position)?.OrderBy(result => result.Distance).FirstOrDefault();
        if (hit is null) return false;
        static bool HitMatches(HelixToolkit.SharpDX.HitTestResult hitResult, MeshGeometryModel3D? mesh) =>
            mesh is not null && (ReferenceEquals(hitResult.ModelHit, mesh)
                || ReferenceEquals(hitResult.ModelHit, mesh.SceneNode));
        var tapeHit = _wrappingProgress <= .01
            ? HitMatches(hit, _tearTapeTabModel)
            : HitMatches(hit, _tearTapeRibbonModel);
        var filmHit = _wrappingUpperRoot.Children.OfType<MeshGeometryModel3D>()
            .Concat(_wrappingLowerRoot.Children.OfType<MeshGeometryModel3D>()).Any(mesh =>
                ReferenceEquals(hit.ModelHit, mesh) || ReferenceEquals(hit.ModelHit, mesh.SceneNode));
        _draggingWrappingFilm = _wrappingProgress >= TearCompleteProgress - .001;
        if (_draggingWrappingFilm ? !filmHit : !tapeHit) return false;
        CancelWrappingMotion();
        _wrappingDragPoint = position;
        return true;
    }

    public void DragWrappingTo(System.Windows.Point position)
    {
        if (_wrappingDragPoint is not { } previous) return;
        var distance = Math.Max(180, Viewport.ActualWidth * .58);
        var delta = _draggingWrappingFilm
            ? ((previous.X - position.X) + (previous.Y - position.Y)) / (distance * 1.15)
            : (previous.X - position.X) / distance;
        SetWrappingProgress(_draggingWrappingFilm
            ? Math.Clamp(_wrappingProgress + delta, TearCompleteProgress, 1)
            : Math.Clamp(_wrappingProgress + delta, 0, TearCompleteProgress));
        _wrappingDragPoint = position;
    }

    public void EndWrappingDrag()
    {
        _wrappingDragPoint = null;
        _draggingWrappingFilm = false;
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
        // The dimensioned lid is already seated relative to the hinge axis;
        // it no longer needs an open-state corrective translation.
        const double targetFrontPanelSeatZ = 0d;
        if (!animate)
        {
            _animationRenderTimer.Stop();
            _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
            _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
            _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
            _frontPanelSeatTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
            _lidHingeRotation.Angle = targetAngle;
            _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
            _openCenterTranslation.OffsetX = targetOffsetX;
            _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
            _frontPanelSeatTranslation.OffsetZ = targetFrontPanelSeatZ;
            InvalidateFinalFrame();
            return;
        }

        var currentAngle = _lidHingeRotation.Angle;
        var currentScaleX = _openScale.ScaleX;
        var currentScaleY = _openScale.ScaleY;
        var currentScaleZ = _openScale.ScaleZ;
        var currentOffsetX = _openCenterTranslation.OffsetX;
        var currentSpineCardOffsetX = _spineCardOpenTranslation.OffsetX;
        var currentFrontPanelSeatZ = _frontPanelSeatTranslation.OffsetZ;
        _lidHingeRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
        _openScale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
        _openCenterTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        _spineCardOpenTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        _frontPanelSeatTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
        _lidHingeRotation.Angle = targetAngle;
        _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
        _openCenterTranslation.OffsetX = targetOffsetX;
        _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
        _frontPanelSeatTranslation.OffsetZ = targetFrontPanelSeatZ;
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
            _frontPanelSeatTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
            _lidHingeRotation.Angle = targetAngle;
            _openScale.ScaleX = _openScale.ScaleY = _openScale.ScaleZ = targetScale;
            _openCenterTranslation.OffsetX = targetOffsetX;
            _spineCardOpenTranslation.OffsetX = targetSpineCardOffsetX;
            _frontPanelSeatTranslation.OffsetZ = targetFrontPanelSeatZ;
            InvalidateFinalFrame();
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
        _frontPanelSeatTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty,
            Animation(currentFrontPanelSeatZ, targetFrontPanelSeatZ));
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
            InvalidateFinalFrame();
        };
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetXProperty,
            Animation(fromX, targetX));
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetYProperty,
            Animation(fromY, targetY));
        _discTranslation.BeginAnimation(TranslateTransform3D.OffsetZProperty, lift);
        _discTiltRotation.BeginAnimation(AxisAngleRotation3D.AngleProperty,
            Animation(fromAngle, targetAngle));
    }

    public bool IsDiscHit(System.Windows.Point position)
    {
        var hits = Viewport.FindHits(position)?.OrderBy(result => result.Distance);
        if (hits is null) return false;
        var discMeshes = _discRoot.Children.OfType<MeshGeometryModel3D>()
            .Concat(_secondDiscRoot.Children.OfType<MeshGeometryModel3D>()).ToList();
        return hits.Any(hit => discMeshes.Any(mesh => ReferenceEquals(hit.ModelHit, mesh)
            || ReferenceEquals(hit.ModelHit, mesh.SceneNode)));
    }

    public void SetDiscPlaying(bool playing)
    {
        if (_disposed) return;
        if (playing)
        {
            if (_discPlaying) return;
            _discPlaying = true;
            _discSpinStartAngle = _discSpinRotation.Angle;
            _discSpinClock.Restart();
            CompositionTarget.Rendering += OnDiscRenderFrame;
            return;
        }
        if (!_discPlaying) return;
        AdvanceDiscSpin();
        CompositionTarget.Rendering -= OnDiscRenderFrame;
        _discPlaying = false;
        _discSpinClock.Reset();
        InvalidateFinalFrame();
    }

    private void OnDiscRenderFrame(object? sender, EventArgs e) => AdvanceDiscSpin();

    private void AdvanceDiscSpin()
    {
        var elapsed = _discSpinClock.Elapsed.TotalSeconds;
        if (elapsed <= 0) return;
        var degrees = DiscPlaybackRpm * 6 * elapsed;
        // Derive every frame from one fixed start time. Dispatcher timer
        // jitter can no longer accumulate into uneven angular steps.
        _discSpinRotation.Angle = (_discSpinStartAngle - degrees) % 360;
        _secondDiscSpinRotation.Angle = _discSpinRotation.Angle;
        RequestRender();
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
        RequestRender();
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
            InvalidateFinalFrame();
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
        RequestRender();
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
            InvalidateFinalFrame();
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
        RequestRender();
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
        // A removed obi may be repositioned in the screen plane, but its
        // printed front must never be dragged back through the closed case.
        // Retaining a small positive clearance also prevents transparent OIT
        // sorting from intermittently clipping the flap against the acrylic.
        var proposedZ = _spineCardDragTranslation.OffsetZ + delta.Z;
        _spineCardDragTranslation.OffsetZ = Math.Max(
            0.018 - _spineCardTranslation.OffsetZ, proposedZ);
        _spineCardDragPoint = current;
        RequestRender();
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
        bool transparent = false, GroupModel3D? target = null, bool castsShadow = true)
    {
        var builder = new MeshBuilder(true, true, true);
        builder.AddBox(center, x, y, z);
        AddMesh(builder.ToMeshGeometry3D(), material, transparent, false, target, castsShadow);
    }

    private void AddDimensionedFrontLid(float width, float height, float depth,
        DxMaterial panelMaterial, DxMaterial sideWallMaterial)
    {
        var millimetreX = width / 142f;
        var millimetreY = height / 125f;
        var millimetreZ = depth / StandardCaseDepthMm;
        var panelThickness = millimetreZ;
        var frameProjection = millimetreZ * 0.5f;
        var frameBorderX = millimetreX * 2f;
        var frameBorderY = millimetreY * 2f;
        var interlockDepth = millimetreZ * 0.7f;
        var hingeAxisX = (float)AssembledHingeX;
        var hingeOuterRadius = millimetreZ * 2.4f;
        var hingeInnerRadius = millimetreZ * 1.7f;
        var lidLeft = hingeAxisX + hingeOuterRadius;
        var lidRight = width / 2;
        var lidCenterX = (lidLeft + lidRight) / 2;
        var lidWidth = lidRight - lidLeft;
        var panelLeft = lidLeft;
        var panelRight = lidRight - frameBorderX;

        // The clear 1 mm plate is recessed 0.5 mm below the perimeter's outer
        // face. Its inner face is the exact contact plane used by the booklet.
        var panelOuterZ = depth / 2 - frameProjection;
        var panelInnerZ = panelOuterZ - panelThickness;
        AddBox(new Vector3((panelLeft + panelRight) / 2, 0,
                (panelOuterZ + panelInnerZ) / 2),
            panelRight - panelLeft, height - frameBorderY * 2,
            panelThickness, panelMaterial, true, _frontPanelRoot, false);

        // One continuous perimeter skirt. It reaches the hinge axis and only
        // crosses it by the 0.7 mm mating tongue, so no fabricated STL plane
        // can remain floating above the tray.
        var skirtFrontZ = depth / 2;
        var skirtRearZ = -interlockDepth;
        var skirtDepth = skirtFrontZ - skirtRearZ;
        var skirtCenterZ = (skirtFrontZ + skirtRearZ) / 2;
        AddBox(new Vector3(lidCenterX, height / 2 - frameBorderY / 2, skirtCenterZ),
            lidWidth, frameBorderY, skirtDepth,
            sideWallMaterial, true, _frontPanelRoot, false);
        AddBox(new Vector3(lidCenterX, -height / 2 + frameBorderY / 2, skirtCenterZ),
            lidWidth, frameBorderY, skirtDepth,
            sideWallMaterial, true, _frontPanelRoot, false);
        // The hinge axis passes through the centre of this rail. Previously
        // the rail stayed at the case's far outer edge while rotating around
        // an unrelated inboard point, which lifted the complete front lid.
        AddBox(new Vector3(hingeAxisX, 0, skirtCenterZ),
            frameBorderX, height - frameBorderY * 2, skirtDepth,
            sideWallMaterial, true, _frontPanelRoot, false);
        AddBox(new Vector3(lidRight - frameBorderX / 2, 0, skirtCenterZ),
            frameBorderX, height - frameBorderY * 2, skirtDepth,
            sideWallMaterial, true, _frontPanelRoot, false);
        // The top and bottom rails terminate at annular hinge knuckles. A
        // rectangular rail continued through the pivot placed a solid case
        // edge across the visible hinge bore.
        AddHingeKnuckleRing(hingeAxisX,
            height / 2 - frameBorderY / 2, 0,
            hingeOuterRadius, hingeInnerRadius, frameBorderY,
            sideWallMaterial);
        AddHingeKnuckleRing(hingeAxisX,
            -height / 2 + frameBorderY / 2, 0,
            hingeOuterRadius, hingeInnerRadius, frameBorderY,
            sideWallMaterial);

    }

    private void AddHingeKnuckleRing(float centerX, float centerY, float centerZ,
        float outerRadius, float innerRadius, float length,
        DxMaterial material)
    {
        const int segments = 40;
        var builder = new MeshBuilder(true, true, true);
        var y0 = centerY - length / 2;
        var y1 = centerY + length / 2;
        for (var segment = 0; segment < segments; segment++)
        {
            var angle0 = MathF.Tau * segment / segments;
            var angle1 = MathF.Tau * (segment + 1) / segments;
            var outer0 = new Vector2(MathF.Cos(angle0), MathF.Sin(angle0)) * outerRadius;
            var outer1 = new Vector2(MathF.Cos(angle1), MathF.Sin(angle1)) * outerRadius;
            var inner0 = new Vector2(MathF.Cos(angle0), MathF.Sin(angle0)) * innerRadius;
            var inner1 = new Vector2(MathF.Cos(angle1), MathF.Sin(angle1)) * innerRadius;

            Vector3 Point(Vector2 radial, float y) =>
                new(centerX + radial.X, y, centerZ + radial.Y);

            builder.AddQuad(Point(outer0, y0), Point(outer0, y1),
                Point(outer1, y1), Point(outer1, y0));
            builder.AddQuad(Point(inner1, y0), Point(inner1, y1),
                Point(inner0, y1), Point(inner0, y0));
            builder.AddQuad(Point(inner0, y0), Point(outer0, y0),
                Point(outer1, y0), Point(inner1, y0));
            builder.AddQuad(Point(inner1, y1), Point(outer1, y1),
                Point(outer0, y1), Point(inner0, y1));
        }
        // The lid contributes the axle pin; the lower STL contributes the
        // receiving bore. Keep this cylindrical and concentric with the ring
        // instead of letting a rectangular case edge cross the opening.
        var pinRadius = innerRadius * 0.68f;
        var inwardExtension = length * 0.65f;
        var pinStartY = centerY > 0 ? y0 - inwardExtension : y0;
        var pinEndY = centerY > 0 ? y1 : y1 + inwardExtension;
        builder.AddCylinder(
            new Vector3(centerX, pinStartY, centerZ),
            new Vector3(centerX, pinEndY, centerZ),
            pinRadius, segments, true, true);
        AddMesh(builder.ToMeshGeometry3D(), material, true, false,
            _frontPanelRoot, false);
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
        AddMesh(manualStop.ToMeshGeometry3D(), stopMaterial, true, false, _frontPanelRoot);
    }

    // Centre the 0.014-wide clear retaining lip at the inner hinge-side
    // boundary, rather than growing the entire lip into the booklet side.
    private static float TrayManualStopRight(float width) => -width / 2 + 0.252f;

    private void AddTraySpineCover(float width, float caseHeight, float depth,
        DxMaterial material, DxMaterial grooveMaterial, DxMaterial ribMaterial,
        DxMaterial transitionMaterial, bool transparent)
    {
        var millimetreX = width / 142f;
        var millimetreY = caseHeight / 125f;
        var millimetreZ = depth / StandardCaseDepthMm;

        // The removable tray's ribbed spine column is about 13 mm wide in the
        // supplied face scan. Leave only the clear rear-shell skin outside it.
        var bandLeft = -width / 2 + 0.5f * millimetreX;
        var bandRight = -width / 2 + 13.5f * millimetreX;

        // The scan's long ribbed tray section is a thin plate, not a wall that
        // fills the case depth. Its rear face begins exactly where the 6 mm
        // printed spine fold ends; the former depth-relative placement crossed
        // about 0.6 mm into that artwork and left a false gap above the plate.
        // Build the tray inward from that boundary with exactly 1.5 mm of
        // physical thickness, so paper and tray meet without overlapping.
        var bandRearZ = StandardVisibleSpineDepth / 2;
        var bandFrontZ = bandRearZ + 1.5f * millimetreZ;
        // Clear trays use a smooth spine strip. Fine ribs viewed through the
        // transparent shell read as false doubled lines, while opaque white,
        // black and gray trays retain the scan-matched moulding below.
        if (transparent)
        {
            AddBox(new Vector3((bandLeft + bandRight) / 2,
                    0, (bandRearZ + bandFrontZ) / 2),
                bandRight - bandLeft, caseHeight - 4.0f * millimetreY,
                bandFrontZ - bandRearZ, material, true, _baseRoot);
            AddTraySpineTransition(bandRight, caseHeight - 4.0f * millimetreY,
                bandFrontZ, depth, millimetreX, transitionMaterial, true);
            return;
        }
        // img129 (600 dpi) measures a repeating 18-19 px cycle: about 0.78 mm,
        // with a 7 px / 0.30 mm recessed groove. Preserve the overall 1.5 mm
        // tray thickness and recess only the spaces between the raised ribs.
        const int ribCount = 17;
        var grooveDepth = 0.12f * millimetreZ;
        var grooveFloorZ = bandFrontZ - grooveDepth;
        // The scanned top and bottom clear rails occupy about 2 mm each.
        // A 121 mm tray strip therefore meets their inner faces without any
        // overlap while still reaching substantially farther than the old
        // 116 mm approximation.
        var spineHeight = caseHeight - 4.0f * millimetreY;
        AddBox(new Vector3((bandLeft + bandRight) / 2,
                0, (bandRearZ + grooveFloorZ) / 2),
            bandRight - bandLeft, spineHeight, grooveFloorZ - bandRearZ,
            material, transparent, _baseRoot);

        var grooveFloor = new MeshBuilder(true, true, true);
        grooveFloor.AddQuad(new Vector3(bandLeft, spineHeight / 2, grooveFloorZ + 0.00005f),
            new Vector3(bandLeft, -spineHeight / 2, grooveFloorZ + 0.00005f),
            new Vector3(bandRight, -spineHeight / 2, grooveFloorZ + 0.00005f),
            new Vector3(bandRight, spineHeight / 2, grooveFloorZ + 0.00005f));
        AddMesh(grooveFloor.ToMeshGeometry3D(), grooveMaterial,
            transparent, false, _baseRoot, false);

        var ribs = new MeshBuilder(true, true, true);
        var pitch = (bandRight - bandLeft) / ribCount;
        var grooveWidth = 0.30f * millimetreX;
        var ribWidth = Math.Max(pitch * 0.35f, pitch - grooveWidth);
        for (var index = 0; index < ribCount; index++)
        {
            var centerX = bandLeft + pitch * (index + 0.5f);
            ribs.AddBox(new Vector3(centerX, 0, (grooveFloorZ + bandFrontZ) / 2),
                ribWidth, spineHeight, bandFrontZ - grooveFloorZ);
        }
        AddMesh(ribs.ToMeshGeometry3D(), ribMaterial,
            transparent, false, _baseRoot, false);
        AddTraySpineTransition(bandRight, spineHeight, bandFrontZ,
            depth, millimetreX, transitionMaterial, false);
    }

    private void AddTraySpineTransition(float bandRight, float height,
        float bandFrontZ, float depth, float millimetreX,
        DxMaterial material, bool transparent)
    {
        // The raised spine strip meets the lower main tray through a short
        // moulded shoulder. A triangular prism closes the former blank space
        // while retaining a visible, continuous step rather than flattening
        // both parts to the same height.
        var traySurfaceZ = depth * 0.1162f;
        var transitionRight = bandRight + 1.2f * millimetreX;
        var topY = height / 2;
        var bottomY = -height / 2;
        var upperTop = new Vector3(bandRight, topY, bandFrontZ);
        var upperFloor = new Vector3(bandRight, topY, traySurfaceZ);
        var upperRight = new Vector3(transitionRight, topY, traySurfaceZ);
        var lowerTop = new Vector3(bandRight, bottomY, bandFrontZ);
        var lowerFloor = new Vector3(bandRight, bottomY, traySurfaceZ);
        var lowerRight = new Vector3(transitionRight, bottomY, traySurfaceZ);
        var transition = new MeshBuilder(true, true, true);
        transition.AddQuad(upperTop, upperRight, lowerRight, lowerTop);
        transition.AddQuad(upperFloor, lowerFloor, lowerRight, upperRight);
        transition.AddQuad(upperTop, lowerTop, lowerFloor, upperFloor);
        transition.AddTriangle(upperTop, upperFloor, upperRight);
        transition.AddTriangle(lowerTop, lowerRight, lowerFloor);
        AddMesh(transition.ToMeshGeometry3D(), material,
            transparent, false, _baseRoot, false);
    }

    private void AddOpeningSideMatingRails(float width, float height, float depth,
        DxMaterial material)
    {
        var millimetreX = width / 142f;
        var millimetreY = height / 125f;
        var wallThickness = 1.0f * millimetreX;
        var endInset = 0.6f * millimetreY;
        var railLength = height - endInset * 2;
        var railX = width / 2 - wallThickness / 2;
        var x0 = railX - wallThickness / 2;
        var x1 = railX + wallThickness / 2;
        var y0 = -railLength / 2;
        var y1 = railLength / 2;

        // The two rigid halves meet at Z=0 but have no caps on that mating
        // plane. The former closed boxes overlapped by 0.16 mm and their two
        // transparent end faces rendered as a dark line through the Spine.
        AddOpenEndedRailHalf(x0, x1, y0, y1, -depth / 2, 0,
            material, _baseRoot, capAtStart: true);
        AddOpenEndedRailHalf(x0, x1, y0, y1, 0, depth / 2,
            material, _frontPanelRoot, capAtStart: false);
    }

    private void AddOpenEndedRailHalf(float x0, float x1, float y0, float y1,
        float z0, float z1, DxMaterial material, GroupModel3D target,
        bool capAtStart)
    {
        var rail = new MeshBuilder(true, true, true);
        rail.AddQuad(new Vector3(x0, y1, z0), new Vector3(x0, y0, z0),
            new Vector3(x0, y0, z1), new Vector3(x0, y1, z1));
        rail.AddQuad(new Vector3(x1, y1, z1), new Vector3(x1, y0, z1),
            new Vector3(x1, y0, z0), new Vector3(x1, y1, z0));
        rail.AddQuad(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1),
            new Vector3(x1, y1, z0), new Vector3(x0, y1, z0));
        rail.AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
            new Vector3(x1, y0, z1), new Vector3(x0, y0, z1));
        var capZ = capAtStart ? z0 : z1;
        rail.AddQuad(new Vector3(x0, y1, capZ), new Vector3(x1, y1, capZ),
            new Vector3(x1, y0, capZ), new Vector3(x0, y0, capZ));
        AddMesh(rail.ToMeshGeometry3D(), material, true, true, target, false);
    }

    private void AddTopBottomSideRibs(float width, float height, float depth,
        DxMaterial material)
    {
        var millimetreX = width / 142f;
        var millimetreY = height / 125f;
        var millimetreZ = depth / StandardCaseDepthMm;
        // The rib field is recessed from all four borders of the narrow side
        // face: 10 mm at the two short ends and 1 mm at the front/rear edges.
        // The surrounding perimeter therefore remains a smooth clear frame.
        var startX = -width / 2 + 10f * millimetreX;
        var endX = width / 2 - 10f * millimetreX;
        var rearEdgeZ = -depth / 2 + 1f * millimetreZ;
        var frontEdgeZ = depth / 2 - 1f * millimetreZ;
        var ribWidth = 0.16f * millimetreX;
        var surfaceBias = 0.015f * millimetreY;
        const int ribCount = 168;
        var rearRibs = new MeshBuilder(true, true, true);
        var frontRibs = new MeshBuilder(true, true, true);

        static void AddRibFace(MeshBuilder builder, float left, float right,
            float y, float z0, float z1)
        {
            builder.AddQuad(new Vector3(left, y, z0),
                new Vector3(right, y, z0),
                new Vector3(right, y, z1),
                new Vector3(left, y, z1));
        }

        for (var index = 0; index < ribCount; index++)
        {
            var x = startX + (endX - startX) * index / (ribCount - 1);
            // The two retaining-catch blocks interrupt the fine rib field in
            // the scan; leave those short regions clean instead of drawing
            // lines through the moulded catches.
            if (Math.Abs(x + 0.30f) < 0.13f || Math.Abs(x - 0.54f) < 0.13f)
                continue;
            var left = x - ribWidth / 2;
            var right = x + ribWidth / 2;
            AddRibFace(rearRibs, left, right,
                height / 2 + surfaceBias, rearEdgeZ, 0);
            AddRibFace(rearRibs, left, right,
                -height / 2 - surfaceBias, 0, rearEdgeZ);
            AddRibFace(frontRibs, left, right,
                height / 2 + surfaceBias, 0, frontEdgeZ);
            AddRibFace(frontRibs, left, right,
                -height / 2 - surfaceBias, frontEdgeZ, 0);
        }

        // Rear ribs remain with the fixed shell; front ribs rotate with the
        // lid. This preserves the hinge behaviour while the case is opened.
        AddMesh(rearRibs.ToMeshGeometry3D(), material, true, true,
            _baseRoot, false);
        AddMesh(frontRibs.ToMeshGeometry3D(), material, true, true,
            _frontPanelRoot, false);
    }

    private void AddOpenBookletPageBlock(float left, float right,
        float bottom, float top, float rearZ, float frontZ,
        DxMaterial material, bool leaveFoldOpen)
    {
        var pages = new MeshBuilder(true, true, true);
        // Only the four cut-paper edges are geometry. The Front and Inside
        // artwork quads close the block visually, avoiding coincident opaque
        // faces that caused the dense flicker pattern over both images.
        if (!leaveFoldOpen)
            pages.AddQuad(new Vector3(left, top, rearZ),
                new Vector3(left, bottom, rearZ),
                new Vector3(left, bottom, frontZ),
                new Vector3(left, top, frontZ));
        pages.AddQuad(new Vector3(right, top, frontZ),
            new Vector3(right, bottom, frontZ),
            new Vector3(right, bottom, rearZ),
            new Vector3(right, top, rearZ));
        pages.AddQuad(new Vector3(left, top, frontZ),
            new Vector3(right, top, frontZ),
            new Vector3(right, top, rearZ),
            new Vector3(left, top, rearZ));
        pages.AddQuad(new Vector3(left, bottom, rearZ),
            new Vector3(right, bottom, rearZ),
            new Vector3(right, bottom, frontZ),
            new Vector3(left, bottom, frontZ));
        AddMesh(pages.ToMeshGeometry3D(), material, false, true,
            _bookletRoot, false);
    }

    private void AddBookletFoldArtwork(BitmapSource frontBitmap,
        BitmapSource rearBitmap, float x, float bottom, float top,
        float rearZ, float frontZ)
    {
        var middleZ = (rearZ + frontZ) / 2;
        const float edgeSample = 0.025f;

        DxMaterial Material(BitmapSource bitmap, string name) => new PhongMaterial
        {
            Name = name,
            DiffuseColor = new Color4(1, 1, 1, 1),
            DiffuseMap = CreateTexture(bitmap),
            RenderDiffuseMap = true,
            SpecularColor = new Color4(0.025f, 0.025f, 0.025f, 1),
            SpecularShininess = 5,
            EnableAutoTangent = true
        };

        // Continue the leftmost edge of the front cover around the first half
        // of the fold instead of exposing a white paper wall.
        var frontFold = new MeshBuilder(true, true, true);
        frontFold.AddQuad(new Vector3(x, top, frontZ),
            new Vector3(x, bottom, frontZ),
            new Vector3(x, bottom, middleZ),
            new Vector3(x, top, middleZ),
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(edgeSample, 1), new Vector2(edgeSample, 0));
        AddMesh(frontFold.ToMeshGeometry3D(),
            Material(frontBitmap, "Booklet front fold artwork"),
            false, true, _bookletRoot, false);

        // The second half continues into the image on the booklet's reverse.
        // Reverse U at the centre so the two sampled strips meet as a fold.
        var rearFold = new MeshBuilder(true, true, true);
        rearFold.AddQuad(new Vector3(x, top, middleZ),
            new Vector3(x, bottom, middleZ),
            new Vector3(x, bottom, rearZ),
            new Vector3(x, top, rearZ),
            new Vector2(edgeSample, 0), new Vector2(edgeSample, 1),
            new Vector2(0, 1), new Vector2(0, 0));
        AddMesh(rearFold.ToMeshGeometry3D(),
            Material(rearBitmap, "Booklet rear fold artwork"),
            false, true, _bookletRoot, false);
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

    internal sealed record CoverFlowShellGeometry(
        System.Windows.Media.Media3D.MeshGeometry3D BottomTray,
        System.Windows.Media.Media3D.MeshGeometry3D BottomPerimeter,
        System.Windows.Media.Media3D.MeshGeometry3D BottomMouldedEdges,
        System.Windows.Media.Media3D.MeshGeometry3D TopLid,
        System.Windows.Media.Media3D.MeshGeometry3D TopMouldedEdges,
        float FrontLeft, float FrontRight, float FrontBottom, float FrontTop, float FrontZ);

    internal static CoverFlowShellGeometry GetCoverFlowShellGeometry() => CoverFlowGeometry.Value;

    private static CoverFlowShellGeometry CreateCoverFlowShellGeometry()
    {
        var shell = CaseGeometry.Value;
        return new CoverFlowShellGeometry(
            Convert(shell.BottomTray), Convert(shell.BottomPerimeter), Convert(shell.BottomMouldedEdges),
            Convert(shell.TopLid), Convert(shell.TopMouldedEdges),
            shell.FrontArtworkArea.Left, shell.FrontArtworkArea.Right,
            shell.FrontArtworkArea.Bottom, shell.FrontArtworkArea.Top, shell.FrontArtworkArea.Z);

        static System.Windows.Media.Media3D.MeshGeometry3D Convert(DxMesh source)
        {
            var result = new System.Windows.Media.Media3D.MeshGeometry3D
            {
                Positions = new Point3DCollection(source.Positions!.Select(point =>
                    new Point3D(point.X, point.Y, point.Z))),
                TriangleIndices = new Int32Collection(source.Indices)
            };
            if (source.Normals is { Count: > 0 })
                result.Normals = new Vector3DCollection(source.Normals.Select(normal =>
                    new Vector3D(normal.X, normal.Y, normal.Z)));
            result.Freeze();
            return result;
        }
    }

    private static StlCaseGeometry LoadStlCaseGeometry()
    {
        const float width = 2.42f;
        const float height = 2.12f;
        const float depth = StandardCaseDepth;
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
        // Use the same dimension-constrained mapping as the lid geometry. The
        // hinge stays at Z=0 and the outer front surface is exactly +5 mm.
        const float topHingeBoreZMm = 5.30f;
        var frontSpan = topHingeBoreZMm - minimum.Z;
        var rearSpan = maximum.Z - topHingeBoreZMm;
        var halfDepth = targetDepth / 2;
        var z = plane.Z <= topHingeBoreZMm
            ? (topHingeBoreZMm - plane.Z) / frontSpan * halfDepth
            : -(plane.Z - topHingeBoreZMm) / rearSpan * halfDepth;
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
        var halfDepth = targetDepth / 2;

        Vector3 Transform(Vector3 source)
        {
            // The top STL is exported with its hinge on the right so both
            // printable halves can lie open. Mirror it into the closed-case
            // assembly, where its hinge mates with the bottom's left hinge.
            var assembledX = (source.X - center.X) * scaleX * (top ? -1 : 1);
            // Both hinge bores remain at Z=0. Map each side of each bore into
            // the fixed +/-5 mm closed envelope independently. The printable
            // STL halves differ slightly in raw depth (10.3 and 10.0 mm); using
            // those raw heights directly made the front stand 0.3 mm proud.
            // The scans define the assembled dimensions, so those dimensions
            // take precedence over small details of the printable source.
            const float topHingeBoreZMm = 5.30f;
            const float bottomHingeBoreZMm = 5.05f;
            var hingeZ = top ? topHingeBoreZMm : bottomHingeBoreZMm;
            var frontSpan = hingeZ - minimum.Z;
            var rearSpan = maximum.Z - hingeZ;
            var assembledZ = top
                ? source.Z <= hingeZ
                    ? (hingeZ - source.Z) / frontSpan * halfDepth
                    : -(source.Z - hingeZ) / rearSpan * halfDepth
                : source.Z <= hingeZ
                    ? -(hingeZ - source.Z) / frontSpan * halfDepth
                    : (source.Z - hingeZ) / rearSpan * halfDepth;
            return new Vector3(assembledX, (source.Y - center.Y) * scaleY,
                assembledZ);
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
            var isOuterClearRim = Math.Abs(centroid.X) > targetWidth * 0.485f
                || Math.Abs(centroid.Y) > targetHeight * 0.485f;
            // The printable lower STL closes the opening edge with a tall
            // vertical wall. The scanned injection-moulded tray has no such
            // wall: only its thin plate reaches this side and the clear shells
            // form the exterior. Drop only the side-facing raised triangles;
            // retain the tray's horizontal disc-support surface.
            var sourceCentroidZ = (triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3;
            var faceNormal = Vector3.Cross(b - a, c - a);
            if (faceNormal.LengthSquared() > 0)
                faceNormal = Vector3.Normalize(faceNormal);
            var isOpeningSideTrayWall = centroid.X > targetWidth * 0.40f
                && !isOuterClearRim
                && sourceCentroidZ > 1.7f
                && Math.Abs(faceNormal.X) > 0.55f;
            if (isOpeningSideTrayWall)
                continue;
            // Replace the lower STL's smooth hinge-side tray column with the
            // scan-dimensioned 13 mm ribbed plate built by AddTraySpineCover.
            // Leaving both surfaces in place puts this STL face about 0.5 mm
            // in front of the procedural ribs and hides them completely.
            // Keep the outer clear perimeter; only the removable tray resin is
            // suppressed in this strip.
            var isRebuiltTraySpine = !isOuterClearRim
                && centroid.X < -targetWidth * 0.402f;
            if (isRebuiltTraySpine)
                continue;
            // One spatial rule owns every material assignment: everything
            // inside the clear perimeter is the removable tray, regardless of
            // triangle height, slope or whether it forms a recess. The former
            // height/edge heuristics split a single white tray between tray and
            // transparent meshes, exposing the black scene through random
            // supports and catches.
            if (!isOuterClearRim)
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

        // The fine top/bottom rib field is generated once by
        // AddTopBottomSideRibs. The former 78-rib field here overlapped its
        // 168 scan-spaced ribs, producing a false heavy line every few cells.

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
        // Both printed spine folds sit centrally between the closed front and
        // rear rails. The former opening-side rear offset pushed this paper
        // downward by 1.4 mm and exposed a large false gap above it even after
        // the transparent rails themselves had been extended to meet.
        var rearZ = -StandardVisibleSpineDepth / 2;
        var frontZ = rearZ + StandardVisibleSpineDepth;
        var builder = new MeshBuilder(true, true, true);
        if (leftSide)
        {
            // Source-right strip: U=0 meets the Back and U=1 reaches Front.
            // Winding faces outward (-X).
            builder.AddQuad(new Vector3(x, height / 2, frontZ),
                new Vector3(x, height / 2, rearZ),
                new Vector3(x, -height / 2, rearZ),
                new Vector3(x, -height / 2, frontZ),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        else
        {
            // Source-left strip: U=1 meets the Back and U=0 reaches Front.
            // Winding faces outward (+X).
            builder.AddQuad(new Vector3(x, height / 2, rearZ),
                new Vector3(x, height / 2, frontZ),
                new Vector3(x, -height / 2, frontZ),
                new Vector3(x, -height / 2, rearZ),
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
            reverse.AddQuad(new Vector3(innerX, height / 2, rearZ),
                new Vector3(innerX, height / 2, frontZ),
                new Vector3(innerX, -height / 2, frontZ),
                new Vector3(innerX, -height / 2, rearZ),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
        }
        else
        {
            reverse.AddQuad(new Vector3(innerX, height / 2, frontZ),
                new Vector3(innerX, height / 2, rearZ),
                new Vector3(innerX, -height / 2, rearZ),
                new Vector3(innerX, -height / 2, frontZ),
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

    private void AddCaramelWrapping(float caseWidth, float caseHeight, float caseDepth)
    {
        const float tapeHeight = 0.035f;
        // Fit tightly like commercial caramel wrap while retaining a small,
        // deliberate depth separation from both acrylic and the obi. Using
        // one large clearance in every direction made the film look baggy;
        // making it coplanar causes depth-buffer flicker over Back artwork.
        var outerWidth = caseWidth + WrappingSideClearance * 2;
        var outerDepth = caseDepth + WrappingFaceClearance * 2;
        var top = caseHeight / 2 + WrappingEdgeClearance;
        var bottom = -caseHeight / 2 - WrappingEdgeClearance;
        // The tear tape sits roughly halfway between the previous position
        // and the lower sealed edge, as on the supplied Japanese package.
        var tapeY = bottom + (top - bottom) * 0.09f;
        var upperHeight = top - tapeY;
        var lowerHeight = tapeY - bottom;
        _wrappingCaseWidth = outerWidth;
        _wrappingCaseDepth = outerDepth;
        _wrappingTapeY = tapeY;
        _wrappingUpperClearanceY = top - tapeY + 0.10f;
        _wrappingLowerClearanceY = tapeY - bottom + 0.10f;

        _wrappingUpperRotation.CenterX = 0;
        _wrappingUpperRotation.CenterY = tapeY;
        _wrappingUpperRotation.CenterZ = outerDepth / 2;
        _wrappingLowerRotation.CenterX = 0;
        _wrappingLowerRotation.CenterY = tapeY;
        _wrappingLowerRotation.CenterZ = outerDepth / 2;
        _tearTapeScale.CenterX = -outerWidth / 2;
        _tearTapeScale.CenterY = tapeY;
        _tearTapeScale.CenterZ = 0;
        _tearTapeBackScale.CenterX = outerWidth / 2;
        _tearTapeBackScale.CenterY = tapeY;
        _tearTapeBackScale.CenterZ = 0;

        var film = new PBRMaterial
        {
            Name = "Caramel wrapping film",
            AlbedoColor = new Color4(0.985f, 0.995f, 1f, 0.17f),
            MetallicFactor = 0,
            RoughnessFactor = 0.024,
            ReflectanceFactor = 0.84,
            ClearCoatStrength = 1.0,
            ClearCoatRoughness = 0.006,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };
        var fold = new PhongMaterial
        {
            Name = "Caramel wrapping fold",
            DiffuseColor = new Color4(0.82f, 0.89f, 0.96f, 0.13f),
            SpecularColor = new Color4(0.55f, 0.60f, 0.66f, 0.22f),
            SpecularShininess = 70
        };
        var foldedFacet = new PhongMaterial
        {
            Name = "Caramel wrapping folded facet",
            // These triangles are the heat-folded film corners, not a case
            // control. Keep the fold readable without the former dark,
            // button-like patch appearing over the cover artwork.
            DiffuseColor = new Color4(0.96f, 0.98f, 1f, 0.045f),
            SpecularColor = new Color4(0.72f, 0.80f, 0.90f, 0.18f),
            SpecularShininess = 108
        };
        var tape = new PhongMaterial
        {
            Name = "Caramel tear tape",
            DiffuseColor = new Color4(0.91f, 0.94f, 0.98f, 0.24f),
            SpecularColor = new Color4(0.82f, 0.88f, 0.94f, 0.52f),
            SpecularShininess = 95
        };
        var pulledTape = new PBRMaterial
        {
            Name = "Caramel tear tape ribbon",
            AlbedoColor = new Color4(0.90f, 0.95f, 1f, 0.62f),
            MetallicFactor = 0.04,
            RoughnessFactor = 0.07,
            ReflectanceFactor = 0.78,
            ClearCoatStrength = 0.96,
            ClearCoatRoughness = 0.035,
            RenderEnvironmentMap = true,
            EnableAutoTangent = true
        };

        AddBox(new Vector3(0, (top + tapeY) / 2, 0), outerWidth, upperHeight,
            outerDepth, film, true, _wrappingUpperRoot, false);
        AddBox(new Vector3(0, (bottom + tapeY) / 2, 0), outerWidth, lowerHeight,
            outerDepth, film, true, _wrappingLowerRoot, false);
        // Heat-sealed folds are slightly denser than the broad film and make
        // the package readable even over pale cover artwork.
        AddBox(new Vector3(0, top - 0.012f, 0), outerWidth - 0.04f, 0.018f,
            outerDepth + 0.005f, fold, true, _wrappingUpperRoot, false);
        AddBox(new Vector3(0, bottom + 0.012f, 0), outerWidth - 0.04f, 0.018f,
            outerDepth + 0.005f, fold, true, _wrappingLowerRoot, false);
        // End folds belong to the narrow top face of the package. Mapping them
        // in X-Z keeps both Front and Back artwork completely unobstructed.
        var topFoldY = top + 0.009f;
        void AddTopCrease(Vector2 start, Vector2 end, GroupModel3D target)
        {
            var direction = Vector2.Normalize(end - start);
            var perpendicular = new Vector2(-direction.Y, direction.X) * 0.006f;
            var crease = new MeshBuilder(true, false, false);
            Vector3 OnTop(Vector2 point) => new(point.X, topFoldY, point.Y);
            crease.AddQuad(OnTop(start + perpendicular), OnTop(start - perpendicular),
                OnTop(end - perpendicular), OnTop(end + perpendicular));
            AddMesh(crease.ToMeshGeometry3D(), fold, true, true, target, false);
        }
        var left = -outerWidth / 2 + 0.025f;
        var right = outerWidth / 2 - 0.025f;
        var rearFoldZ = -outerDepth / 2 + 0.012f;
        var frontFoldZ = outerDepth / 2 - 0.012f;
        AddTopCrease(new Vector2(left, rearFoldZ), new Vector2(left + 0.20f, 0), _wrappingUpperRoot);
        AddTopCrease(new Vector2(left, frontFoldZ), new Vector2(left + 0.20f, 0), _wrappingUpperRoot);
        AddTopCrease(new Vector2(right, rearFoldZ), new Vector2(right - 0.20f, 0), _wrappingUpperRoot);
        AddTopCrease(new Vector2(right, frontFoldZ), new Vector2(right - 0.20f, 0), _wrappingUpperRoot);

        // Real caramel packs overlap at each sealed end. The triangular
        // facets gather excess film into the corners rather than leaving a
        // perfectly rectangular transparent box.
        void AddCornerFacet(Vector3 a, Vector3 b, Vector3 c, GroupModel3D target)
        {
            var facet = new MeshBuilder(true, false, false);
            facet.AddTriangle(a, b, c);
            AddMesh(facet.ToMeshGeometry3D(), foldedFacet, true, true, target, false);
        }
        AddCornerFacet(new Vector3(left, topFoldY, rearFoldZ),
            new Vector3(left + 0.24f, topFoldY, rearFoldZ),
            new Vector3(left + 0.14f, topFoldY, 0), _wrappingUpperRoot);
        AddCornerFacet(new Vector3(left, topFoldY, frontFoldZ),
            new Vector3(left + 0.24f, topFoldY, frontFoldZ),
            new Vector3(left + 0.14f, topFoldY, 0), _wrappingUpperRoot);
        AddCornerFacet(new Vector3(right, topFoldY, rearFoldZ),
            new Vector3(right - 0.24f, topFoldY, rearFoldZ),
            new Vector3(right - 0.14f, topFoldY, 0), _wrappingUpperRoot);
        AddCornerFacet(new Vector3(right, topFoldY, frontFoldZ),
            new Vector3(right - 0.24f, topFoldY, frontFoldZ),
            new Vector3(right - 0.14f, topFoldY, 0), _wrappingUpperRoot);

        // The Japanese-style tear tape circles the package. Its small tab is
        // exposed at the lower part of the right spine and can be dragged.
        AddBox(new Vector3(0, tapeY, outerDepth / 2 + 0.004f), outerWidth, tapeHeight,
            0.009f, tape, true, _tearTapeFrontRoot, false);
        AddBox(new Vector3(0, tapeY, -outerDepth / 2 - 0.004f), outerWidth, tapeHeight,
            0.009f, tape, true, _tearTapeBackRoot, false);
        AddBox(new Vector3(caseWidth / 2 + WrappingSideClearance + 0.005f, tapeY, 0), 0.010f,
            tapeHeight, outerDepth, tape, true, _tearTapeSideRoot, false);
        AddBox(new Vector3(-caseWidth / 2 - WrappingSideClearance - 0.005f, tapeY, 0), 0.010f,
            tapeHeight, outerDepth, tape, true, _tearTapeSideRoot, false);
        // A short two-plane tab: the inner section stays against the film,
        // while the tip bends forward and slightly downward. This gives it a
        // changing highlight and avoids the appearance of a rigid rectangle.
        var tabAnchorX = caseWidth / 2 + WrappingSideClearance + 0.002f;
        var tabZ = outerDepth / 2 + 0.010f;
        const float tabHalfHeight = 0.022f;
        var tab = new MeshBuilder(true, false, false);
        var tabMidTop = new Vector3(tabAnchorX + 0.012f, tapeY + tabHalfHeight - 0.003f, tabZ + 0.006f);
        var tabMidBottom = new Vector3(tabAnchorX + 0.012f, tapeY - tabHalfHeight - 0.004f, tabZ + 0.006f);
        var tabTipTop = new Vector3(tabAnchorX + 0.032f, tapeY + tabHalfHeight - 0.010f, tabZ + 0.020f);
        var tabTipBottom = new Vector3(tabAnchorX + 0.032f, tapeY - tabHalfHeight - 0.014f, tabZ + 0.020f);
        tab.AddQuad(new Vector3(tabAnchorX, tapeY + tabHalfHeight, tabZ),
            new Vector3(tabAnchorX, tapeY - tabHalfHeight, tabZ), tabMidBottom, tabMidTop);
        tab.AddQuad(tabMidTop, tabMidBottom, tabTipBottom, tabTipTop);
        AddMesh(tab.ToMeshGeometry3D(), pulledTape, true, true, _tearTapeSideRoot, false);
        _tearTapeTabModel = _tearTapeSideRoot.Children.OfType<MeshGeometryModel3D>().Last();
        _tearTapeRibbonModel = new MeshGeometryModel3D
        {
            Geometry = new MeshBuilder(true, false, false).ToMeshGeometry3D(),
            Material = pulledTape,
            IsTransparent = true,
            IsThrowingShadow = false,
            IsHitTestVisible = true,
            CullMode = DxCullMode.None,
            Visibility = System.Windows.Visibility.Hidden
        };
        _tearTapeRibbonRoot.Children.Add(_tearTapeRibbonModel);
    }

    private void AddSpineCard(BitmapSource? bitmap, float caseWidth, float caseHeight, float caseDepth)
    {
        if (bitmap is null) return;
        var (back, spine, front) = SpineCardArtwork.Split(bitmap);
        // Preserve the flat scan's physical aspect ratio for both flaps. The
        // former implementation derived their scale from the detected centre
        // panel and forced that panel to 10 mm. A slightly wide/manual Spine
        // selection therefore shortened the entire obi. Derive millimetres per
        // pixel from the known 120 mm card height instead; only the folded side
        // itself is constrained to the case's physical 10 mm depth.
        const float obiHeightMm = 120f;
        const float caseHeightMm = 125f;
        var cardHeight = caseHeight * obiHeightMm / caseHeightMm;
        var horizontalScale = cardHeight / Math.Max(1, back.PixelHeight);
        var backWidth = horizontalScale * back.PixelWidth;
        var frontWidth = horizontalScale * front.PixelWidth;
        // Leave a visible gap beside the closed case. A small forward depth
        // separation prevents the folded paper from disappearing beneath the
        // opaque lid after it opens to the left.
        _spineCardRemovedOffsetX = -(Math.Max(backWidth, frontWidth) + 0.34f);
        var outsideX = -caseWidth / 2 - 0.014f;
        // All three paper faces share the same physical fold line. Separating
        // the flap origin from the side plane leaves a visible crack at steep
        // viewing angles.
        var foldX = outsideX;
        var frontZ = caseDepth / 2 + SpineCardFlapClearance;
        var backZ = -caseDepth / 2 - SpineCardFlapClearance;

        // The source is laid flat as Back flap | Spine | Front flap. Each flap
        // stays with the physical case face it covers when the lid is opened.
        AddArtwork(back, foldX, foldX + backWidth, -cardHeight / 2, cardHeight / 2,
            backZ, true, _spineCardRoot, "Spine Card back flap");
        AddArtwork(front, foldX, foldX + frontWidth, -cardHeight / 2, cardHeight / 2,
            frontZ, false, _spineCardRoot, "Spine Card front flap");

        var side = new MeshBuilder(true, true, true);
        side.AddQuad(new Vector3(outsideX, cardHeight / 2, caseDepth / 2),
            new Vector3(outsideX, cardHeight / 2, -caseDepth / 2),
            new Vector3(outsideX, -cardHeight / 2, -caseDepth / 2),
            new Vector3(outsideX, -cardHeight / 2, caseDepth / 2),
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
        float thickness, GroupModel3D target, string artworkName = "Disc artwork")
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
                Name = artworkName,
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
        // The label lies immediately over the moulded tray in the closed
        // assembly. A small rasterizer bias keeps the label deterministically
        // in front without changing the disc's physical position or thickness.
        AddMesh(top.ToMeshGeometry3D(), topMaterial, false, false, target,
            depthBias: -64);
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
        GroupModel3D? target = null, bool castsShadow = true, int depthBias = 0)
    {
        (target ?? _caseRoot).Children.Add(new MeshGeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            IsTransparent = transparent,
            IsThrowingShadow = castsShadow && !transparent,
            IsHitTestVisible = true,
            CullMode = twoSided ? DxCullMode.None : DxCullMode.Back,
            DepthBias = depthBias
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
        _disposed = true;
        if (_interactiveRenderPending)
        {
            CompositionTarget.Rendering -= OnInteractiveRenderFrame;
            _interactiveRenderPending = false;
        }
        CompositionTarget.Rendering -= OnDiscRenderFrame;
        _discPlaying = false;
        _discSpinClock.Stop();
        CancelWrappingMotion();
        CancelSpineCardMotion();
        CancelBookletMotion();
        _animationRenderTimer.Stop();
        Viewport.Items.Clear();
        _textureCache.Clear();
        _effects.Dispose();
    }
}
