using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;

namespace ZipMp3Player;

internal sealed partial class DxJewelCaseScene
{
    private sealed class MultiDiscState
    {
        internal required int Number, Slot;
        internal required GroupModel3D Root, Seat, SpinRoot;
        internal required Transform3D SeatTransform;
        internal required AxisAngleRotation3D Spin;
        internal readonly TranslateTransform3D Offset = new();
        internal bool Removed;
    }
    private readonly Dictionary<int,MultiDiscState> _multiDiscStates = [];
    private int? _multiSelectedDisc, _multiPlayingDisc;
    private string? _multiItemKey;
    private bool _multiBookletPresent;
    private MultiDiscState? _multiDragDisc;
    internal int? SelectedMultiDisc => _multiSelectedDisc;
    internal bool SelectedMultiDiscRemoved => _multiSelectedDisc is int n && _multiDiscStates.TryGetValue(n,out var d) && d.Removed;

    private bool CanTouchMultiDisc(MultiDiscState d) => _caseIsOpen &&
        (d.Removed || (Math.Abs(_multiProgress-1)<.001 && (d.Slot==1 || d.Slot==2&&!_multiBookletPresent))
        || (Math.Abs(_multiProgress-2)<.001 && d.Slot is 3 or 4));
    internal bool CanOperateSelectedMultiDisc => _multiSelectedDisc is int n &&
        _multiDiscStates.TryGetValue(n,out var d)&&CanTouchMultiDisc(d);

    internal bool SelectMultiDisc(int number)
    {
        if(!_multiDiscStates.TryGetValue(number,out var d)||!CanTouchMultiDisc(d))return false;
        _multiSelectedDisc=number;return true;
    }
    internal bool SelectMultiDiscAt(Point position)
    {
        var hits=Viewport.FindHits(position)?.OrderBy(h=>h.Distance);
        if(hits is null)return false;
        foreach(var hit in hits)
            foreach(var d in _multiDiscStates.Values.Where(CanTouchMultiDisc))
                if(d.SpinRoot.Children.OfType<MeshGeometryModel3D>().Any(m=>
                    ReferenceEquals(hit.ModelHit,m)||ReferenceEquals(hit.ModelHit,m.SceneNode)))
                    return SelectMultiDisc(d.Number);
        return false;
    }
    internal void ActivateSelectedMultiDisc()
    {
        if(!CanOperateSelectedMultiDisc)return;
        SetDiscPlaying(false);
        _multiPlayingDisc=_multiSelectedDisc;
    }
    internal void CancelMultiDiscPlayback(){SetDiscPlaying(false);_multiPlayingDisc=null;}
    private static bool FindMultiTransform(GroupModel3D parent,GroupModel3D target,out Matrix3D result)
    {
        foreach(var child in parent.Children.OfType<GroupModel3D>()) {
            if(ReferenceEquals(child,target)){result=child.Transform?.Value??Matrix3D.Identity;return true;}
            if(FindMultiTransform(child,target,out result)) {
                result.Append(child.Transform?.Value??Matrix3D.Identity);return true;
            }
        }
        result=Matrix3D.Identity;return false;
    }
    internal void SetSelectedMultiDiscRemoved(bool removed)
    {
        if(_multiSelectedDisc is not int n||!_multiDiscStates.TryGetValue(n,out var d))return;
        if(removed&&!CanTouchMultiDisc(d))return;
        SetMultiDiscRemoved(d,removed);
    }
    private void SetMultiDiscRemoved(MultiDiscState d,bool removed)
    {
        if(d.Removed==removed)return;
        EndDiscDrag();
        if(removed) {
            if(!FindMultiTransform(_baseRoot,d.Root,out var seat))return;
            d.Seat.Children.Remove(d.Root);
            var transform=new Transform3DGroup();
            transform.Children.Add(new MatrixTransform3D(seat));
            d.Offset.OffsetX=0;d.Offset.OffsetY=0;d.Offset.OffsetZ=D(35);
            transform.Children.Add(d.Offset);d.Root.Transform=transform;
            _baseRoot.Children.Add(d.Root);
        } else {
            _baseRoot.Children.Remove(d.Root);d.Root.Transform=d.SeatTransform;
            d.Seat.Children.Add(d.Root);
            d.Offset.OffsetX=d.Offset.OffsetY=d.Offset.OffsetZ=0;
        }
        d.Removed=removed;RequestRender();
    }
    private void ReturnMultiDiscs()
    {
        foreach(var d in _multiDiscStates.Values)SetMultiDiscRemoved(d,false);
    }
    private bool BeginMultiDiscDrag(Point position)
    {
        if(!SelectMultiDiscAt(position)||!SelectedMultiDiscRemoved||Viewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera)return false;
        var hit=Viewport.FindHits(position)?.OrderBy(h=>h.Distance).FirstOrDefault();
        if(hit is null)return false;
        _multiDragDisc=_multiDiscStates[_multiSelectedDisc!.Value];
        _discDragPoint=new Point3D(hit.PointHit.X,hit.PointHit.Y,hit.PointHit.Z);
        _discDragNormal=camera.LookDirection;return true;
    }
    private void DragMultiDiscTo(Point position)
    {
        if(_multiDragDisc is not {} d||_discDragPoint is not {} previous)return;
        var current=Viewport.UnProjectOnPlane(position,previous,_discDragNormal);
        if(current is not {} point)return;
        var inverse=_caseTransform.Value;if(!inverse.HasInverse)return;inverse.Invert();
        var delta=inverse.Transform(point-previous);
        d.Offset.OffsetX+=delta.X;d.Offset.OffsetY+=delta.Y;
        d.Offset.OffsetZ=Math.Max(D(15),d.Offset.OffsetZ+delta.Z);
        _discDragPoint=point;RequestRender();
    }
    private sealed record MultiDiscSnapshot(int Number,bool Removed,double X,double Y,double Z,double Angle);
    private MultiDiscSnapshot[] CaptureMultiDiscs() => _multiDiscStates.Values.Select(d=>
        new MultiDiscSnapshot(d.Number,d.Removed,d.Offset.OffsetX,d.Offset.OffsetY,d.Offset.OffsetZ,d.Spin.Angle)).ToArray();
    private void RestoreMultiDiscs(MultiDiscSnapshot[] snapshots)
    {
        foreach(var s in snapshots)if(_multiDiscStates.TryGetValue(s.Number,out var d)) {
            d.Spin.Angle=s.Angle;
            if(s.Removed&&_caseIsOpen) {
                SetMultiDiscRemoved(d,true);d.Offset.OffsetX=s.X;d.Offset.OffsetY=s.Y;d.Offset.OffsetZ=s.Z;
            }
        }
    }
}
