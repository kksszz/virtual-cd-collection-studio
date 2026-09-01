using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ZipMp3Player;

/// <summary>A real tessellated page whose outer half bends around a moving fold.</summary>
internal sealed class BookletPageTurnViewport : Viewport3D
{
    private const int Segments = 32;
    private readonly MeshGeometry3D _mesh = new();
    private readonly GeometryModel3D _page = new();
    private readonly OrthographicCamera _camera = new()
    {
        Position = new Point3D(0, 0, 4), LookDirection = new Vector3D(0, 0, -4),
        UpDirection = new Vector3D(0, 1, 0), Width = 1
    };
    private DateTime _started;
    private bool _forward;
    private double _aspect = 1;

    public BookletPageTurnViewport()
    {
        Camera = _camera;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        ClipToBounds = false;
        _page.Geometry = _mesh;
        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(128, 128, 128)));
        group.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-.35, -.15, -1)));
        group.Children.Add(_page);
        Children.Add(new ModelVisual3D { Content = group });
    }

    public void Start(BitmapSource oldPage, bool forward)
    {
        Stop();
        _forward = forward;
        _aspect = Math.Clamp((double)oldPage.PixelWidth / Math.Max(1, oldPage.PixelHeight), .35, 3.2);
        _camera.Width = _aspect;
        var brush = new ImageBrush(oldPage) { Stretch = Stretch.Fill };
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(90, 255, 252, 242)), 22));
        _page.Material = material;
        _page.BackMaterial = material;
        Visibility = Visibility.Visible;
        UpdateMesh(0);
        _started = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= OnRendering;
        Visibility = Visibility.Collapsed;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var progress = Math.Clamp((DateTime.UtcNow - _started).TotalMilliseconds / 620, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        UpdateMesh(eased);
        if (progress >= 1) Stop();
    }

    private void UpdateMesh(double progress)
    {
        var positions = new Point3DCollection((Segments + 1) * 2);
        var normals = new Vector3DCollection((Segments + 1) * 2);
        var texture = new PointCollection((Segments + 1) * 2);
        var triangles = new Int32Collection(Segments * 6);
        var fold = 1 - progress;
        // At halfway, exactly half the sheet is lifted at roughly 90 degrees.
        var angle = Math.PI * progress;
        for (var column = 0; column <= Segments; column++)
        {
            var u = column / (double)Segments;
            var travelU = _forward ? u : 1 - u;
            var originalX = (travelU - .5) * _aspect;
            var foldX = (fold - .5) * _aspect;
            double x, z; Vector3D normal;
            if (travelU <= fold)
            {
                x = originalX; z = 0; normal = new Vector3D(0, 0, 1);
            }
            else
            {
                var distance = (travelU - fold) * _aspect;
                x = foldX + distance * Math.Cos(angle);
                z = distance * Math.Sin(angle);
                normal = new Vector3D(Math.Sin(angle), 0, Math.Cos(angle));
            }
            if (!_forward) { x = -x; normal.X = -normal.X; }
            positions.Add(new Point3D(x, .5, z)); positions.Add(new Point3D(x, -.5, z));
            normals.Add(normal); normals.Add(normal);
            texture.Add(new Point(u, 0)); texture.Add(new Point(u, 1));
            if (column == Segments) continue;
            var top = column * 2; var next = top + 2;
            triangles.Add(top); triangles.Add(top + 1); triangles.Add(next);
            triangles.Add(next); triangles.Add(top + 1); triangles.Add(next + 1);
        }
        _mesh.Positions = positions; _mesh.Normals = normals;
        _mesh.TextureCoordinates = texture; _mesh.TriangleIndices = triangles;
    }
}
