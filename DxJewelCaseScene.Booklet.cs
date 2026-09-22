using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf.SharpDX;

namespace ZipMp3Player;

internal sealed partial class DxJewelCaseScene
{
    // Created only for the reader transition; closed-case geometry and GLB export stay unchanged.
    private Element3D? _bookletRearArtwork;
    private BitmapSource? _bookletOuterImage;
    private (float Left, float Right, float Bottom, float Top, float Z) _bookletOpeningBounds;
    private GroupModel3D? _bookletOpeningRoot;
    private readonly AxisAngleRotation3D _bookletCoverRotation = new(new Vector3D(0, 1, 0), 0);
    private readonly DispatcherTimer _bookletOpenTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private EventHandler? _bookletOpenTick;
    private TaskCompletionSource<bool>? _bookletOpenCompletion;
    private readonly List<BitmapSource> _bookletPreviewTextures = new();

    public void PrepareBookletOpening(BitmapSource? firstPage)
    {
        ResetBookletOpening();
        if (_disposed || _bookletProgress < .999 || _bookletRearArtwork is null) return;
        var b = _bookletOpeningBounds;
        BitmapSource? left = firstPage, right = null;
        if (firstPage is not null && !_textureCache.ContainsKey(firstPage)) _bookletPreviewTextures.Add(firstPage);
        if (firstPage is not null && firstPage.PixelWidth > firstPage.PixelHeight * 1.3)
        {
            int half = firstPage.PixelWidth / 2;
            left = new CroppedBitmap(firstPage, new Int32Rect(0, 0, half, firstPage.PixelHeight));
            right = new CroppedBitmap(firstPage, new Int32Rect(half, 0, firstPage.PixelWidth - half, firstPage.PixelHeight));
            left.Freeze(); right.Freeze();
            _bookletPreviewTextures.Add(left); _bookletPreviewTextures.Add(right);
        }
        _bookletOpeningRoot = new GroupModel3D();
        var cover = new GroupModel3D {
            Transform = new RotateTransform3D(_bookletCoverRotation, b.Left, 0, b.Z)
        };
        // Local -Z faces the reader: local left is the screen-right fold.
        // Reverse both the fold edge and rotation, keeping the cover moving outward.
        AddArtwork(left, b.Left, b.Right, b.Bottom, b.Top, b.Z,
            true, _bookletOpeningRoot, "Booklet first page", twoSided: false);
        AddArtwork(_bookletOuterImage, b.Left, b.Right, b.Bottom, b.Top, b.Z - .0004f,
            true, cover, "Booklet opening outer cover", twoSided: false);
        AddArtwork(right, b.Left, b.Right, b.Bottom, b.Top, b.Z,
            false, cover, "Booklet opening inner cover", twoSided: false);
        _bookletOpeningRoot.Children.Add(cover);
        _bookletRearArtwork.Visibility = Visibility.Hidden;
        _bookletRoot.Children.Add(_bookletOpeningRoot);
        RequestRender();
    }

    public Task<bool> AnimateBookletOpeningAsync(bool open)
    {
        CancelBookletOpening();
        if (_disposed) return Task.FromResult(false);
        if (_bookletOpeningRoot is null) return Task.FromResult(!open);
        double from = _bookletCoverRotation.Angle, target = open ? 180 : 0;
        if (Math.Abs(from - target) < .001) return Task.FromResult(true);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bookletOpenCompletion = completion;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double duration = (open ? 560 : 360) * Math.Abs(target - from) / 180;
        _bookletOpenTick = (_, _) => {
            double t = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration, 0, 1);
            SetBookletOpeningProgress((from + (target - from) * t * t * (3 - 2 * t)) / 180);
            RequestRender();
            if (t < 1) return;
            _bookletOpenTimer.Stop();
            _bookletOpenTimer.Tick -= _bookletOpenTick;
            _bookletOpenTick = null; _bookletOpenCompletion = null;
            InvalidateFinalFrame(); completion.TrySetResult(true);
        };
        _bookletOpenTimer.Tick += _bookletOpenTick;
        _bookletOpenTimer.Start();
        return completion.Task;
    }

    private void CancelBookletOpening()
    {
        _bookletOpenTimer.Stop();
        if (_bookletOpenTick is not null) _bookletOpenTimer.Tick -= _bookletOpenTick;
        _bookletOpenTick = null;
        _bookletOpenCompletion?.TrySetResult(false);
        _bookletOpenCompletion = null;
    }

    private void SetBookletOpeningProgress(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        _bookletCoverRotation.Angle = 180 * progress;
        // Keep the expanded spread near the case centre instead of opening off an edge.
        // Move only the extracted booklet; preserve the user's camera, zoom, case and obi.
        var fold = _bookletLift.Transform(new Point3D(_bookletOpeningBounds.Left, 0, _bookletOpeningBounds.Z));
        var extractedX = BookletPose(_bookletProgress).X;
        _bookletTranslation.OffsetX = extractedX + (AssembledHingeX - fold.X - extractedX) * progress;
    }

    private void ResetBookletOpening()
    {
        CancelBookletOpening();
        if (_bookletOpeningRoot is not null) _bookletRoot.Children.Remove(_bookletOpeningRoot);
        _bookletOpeningRoot = null;
        _bookletCoverRotation.Angle = 0;
        _bookletTranslation.OffsetX = BookletPose(_bookletProgress).X;
        if (_bookletRearArtwork is not null) _bookletRearArtwork.Visibility = Visibility.Visible;
        foreach (var image in _bookletPreviewTextures) _textureCache.Remove(image);
        _bookletPreviewTextures.Clear();
    }
}
