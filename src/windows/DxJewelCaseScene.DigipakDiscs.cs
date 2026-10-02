using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;

namespace ZipMp3Player;

internal sealed partial class DxJewelCaseScene
{
    private sealed class DigipakDiscState
    {
        internal required GroupModel3D Root;
        internal required AxisAngleRotation3D Spin;
        internal readonly AxisAngleRotation3D Tilt = new(new Vector3D(0, 1, 0), 0);
        internal readonly TranslateTransform3D Offset = new();
        internal bool Removed;
    }

    private readonly Dictionary<int, DigipakDiscState> _digipakDiscStates = [];
    private readonly AxisAngleRotation3D _thirdDiscSpinRotation = new(new Vector3D(0, 0, 1), 0);
    private int? _digipakSelectedDisc, _digipakPlayingDisc;
    private DigipakDiscState? _digipakDragDisc;
    internal int? SelectedDigipakDisc => _digipakSelectedDisc;
    internal bool SelectedDigipakDiscRemoved => _digipakSelectedDisc is int number
        && _digipakDiscStates.TryGetValue(number, out var disc) && disc.Removed;
    internal bool CanOperateSelectedDigipakDisc => _caseIsOpen && _digipakProgress >= .999
        && _digipakSelectedDisc is int number && _digipakDiscStates.ContainsKey(number);

    private void RegisterDigipakDisc(int number, GroupModel3D root, AxisAngleRotation3D spin, float x, float y)
    {
        var disc = new DigipakDiscState { Root = root, Spin = spin };
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(spin, new Point3D(x, y, 0)));
        transform.Children.Add(new RotateTransform3D(disc.Tilt, new Point3D(x, y, 0)));
        transform.Children.Add(disc.Offset);
        root.Transform = transform;
        _digipakDiscStates[number] = disc;
    }

    internal bool SelectDigipakDisc(int number)
    {
        if (!_caseIsOpen || _digipakProgress < .999 || !_digipakDiscStates.ContainsKey(number)) return false;
        _digipakSelectedDisc = number;
        return true;
    }

    internal bool SelectDigipakDiscAt(Point position)
    {
        if (!_caseIsOpen || _digipakProgress < .999) return false;
        var hits = Viewport.FindHits(position)?.OrderBy(hit => hit.Distance);
        if (hits is null) return false;
        foreach (var hit in hits)
            foreach (var (number, disc) in _digipakDiscStates)
                if (disc.Root.Children.OfType<MeshGeometryModel3D>().Any(mesh =>
                    ReferenceEquals(hit.ModelHit, mesh) || ReferenceEquals(hit.ModelHit, mesh.SceneNode)))
                {
                    return SelectDigipakDisc(number);
                }
        return false;
    }

    internal void ActivateSelectedDigipakDisc()
    {
        if (!CanOperateSelectedDigipakDisc) return;
        SetDiscPlaying(false);
        _digipakPlayingDisc = _digipakSelectedDisc;
    }

    internal void SetDigipakPlayingDisc(int? number)
    {
        if (!_isDigipak || _digipakPlayingDisc == number) return;
        SetDiscPlaying(false);
        _digipakPlayingDisc = number is int n && _digipakDiscStates.ContainsKey(n) ? n : null;
    }

    internal void CancelDigipakDiscPlayback()
    {
        SetDiscPlaying(false);
        _digipakPlayingDisc = null;
    }

    internal void SetSelectedDigipakDiscRemoved(bool removed)
    {
        if (!CanOperateSelectedDigipakDisc || _digipakSelectedDisc is not int number) return;
        var disc = _digipakDiscStates[number];
        if (disc.Removed == removed) return;
        EndDiscDrag();
        disc.Offset.OffsetX = removed ? .62 : 0;
        disc.Offset.OffsetY = removed ? .08 : 0;
        disc.Offset.OffsetZ = removed ? .62 : 0;
        disc.Tilt.Angle = removed ? -12 : 0;
        disc.Removed = removed;
        RequestRender();
    }

    private void ReturnDigipakDiscs()
    {
        foreach (var disc in _digipakDiscStates.Values)
        {
            disc.Offset.OffsetX = disc.Offset.OffsetY = disc.Offset.OffsetZ = 0;
            disc.Tilt.Angle = 0;
            disc.Removed = false;
        }
        _digipakDragDisc = null;
    }

    private bool BeginDigipakDiscDrag(Point position)
    {
        if (!SelectDigipakDiscAt(position) || !SelectedDigipakDiscRemoved
            || Viewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera) return false;
        var hit = Viewport.FindHits(position)?.OrderBy(result => result.Distance).FirstOrDefault();
        if (hit is null) return false;
        _digipakDragDisc = _digipakDiscStates[_digipakSelectedDisc!.Value];
        _discDragPoint = new Point3D(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
        _discDragNormal = camera.LookDirection;
        return true;
    }

    private void DragDigipakDiscTo(Point position)
    {
        if (_digipakDragDisc is not { } disc || _discDragPoint is not { } previous) return;
        var point = Viewport.UnProjectOnPlane(position, previous, _discDragNormal);
        if (point is not { } current) return;
        var inverse = _caseTransform.Value;
        if (!inverse.HasInverse) return;
        inverse.Invert();
        var delta = inverse.Transform(current - previous);
        disc.Offset.OffsetX += delta.X;
        disc.Offset.OffsetY += delta.Y;
        disc.Offset.OffsetZ = Math.Max(.35, disc.Offset.OffsetZ + delta.Z);
        _discDragPoint = point;
        RequestRender();
    }

    private sealed record DigipakDiscSnapshot(int Number, bool Removed, double X, double Y, double Z, double Tilt, double Spin);
    private DigipakDiscSnapshot[] CaptureDigipakDiscs() => _digipakDiscStates.Select(pair =>
        new DigipakDiscSnapshot(pair.Key, pair.Value.Removed, pair.Value.Offset.OffsetX,
            pair.Value.Offset.OffsetY, pair.Value.Offset.OffsetZ, pair.Value.Tilt.Angle, pair.Value.Spin.Angle)).ToArray();

    private void RestoreDigipakDiscs(DigipakDiscSnapshot[] snapshots)
    {
        foreach (var snapshot in snapshots)
            if (_digipakDiscStates.TryGetValue(snapshot.Number, out var disc))
            {
                disc.Spin.Angle = snapshot.Spin;
                if (!snapshot.Removed || !_caseIsOpen) continue;
                disc.Offset.OffsetX = snapshot.X;
                disc.Offset.OffsetY = snapshot.Y;
                disc.Offset.OffsetZ = snapshot.Z;
                disc.Tilt.Angle = snapshot.Tilt;
                disc.Removed = true;
            }
    }
}
