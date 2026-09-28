using System.Windows.Media.Imaging;
namespace ZipMp3Player;

// Three-panel/two-disc format. Measurements are the supplied ActRaiser sample.
public sealed record DigipakArtwork(BitmapSource? InnerLeft, BitmapSource? OuterRight, BitmapSource? Trays)
{
    public BitmapSource? LeftFold { get; init; }
    public BitmapSource? RightFold { get; init; }
}
internal static class DigipakDimensions
{
    internal const float Unit = 2.42f / 142; // Same physical scale as the standard case.
    internal const float Panel = 138, Height = 124, Paper = 1;
    internal const float LeftFold = 12, RightFold = 10, ClosedDepth = 11;
    internal const float TrayWidth = 136, TrayHeight = 124, TrayDepth = 4;
    internal const float WellDiameter = 121, HubDiameter = 15, HubHeight = 3;
    internal const float CenterX = 67, CenterFromTop = 61.5f;
    internal const float BookletWidth = 120, BookletHeight = 120, BookletDepth = .05f;
    // The actual slit is visible at about 80% of the scanned panel height.
    internal const float PocketWidth = 125, PocketFromBottom = 24, PocketLeft = 6.5f;
    internal const float Left = -Panel / 2 - LeftFold - Panel;
    internal const float Right = Panel / 2 + RightFold;
    internal const float LeftHinge = -Panel / 2 - LeftFold / 2;
    internal const float RightHinge = Panel / 2 + RightFold / 2;
    internal static (double Left, double Right) Angles(double progress) =>
        (180 * (1 - Math.Clamp(progress * 2, 0, 1)), -180 * (1 - Math.Clamp(progress * 2 - 1, 0, 1)));
}
