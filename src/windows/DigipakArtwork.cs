using System.Windows.Media.Imaging;
namespace ZipMp3Player;

// Two-disc/three-panel and three-disc/four-panel digipaks share image roles.
public sealed record DigipakArtwork(BitmapSource? InnerLeft, BitmapSource? OuterRight, BitmapSource? Trays)
{
    public BitmapSource? OuterFront { get; init; }
    public BitmapSource? LeftFold { get; init; }
    public BitmapSource? RightFold { get; init; }
    public BitmapSource? FarRightFold { get; init; }
    public BitmapSource? InnerLeftFold { get; init; }
    public BitmapSource? InnerRightFold { get; init; }
    public BitmapSource? InnerFarRightFold { get; init; }
    public BitmapSource? OuterFarRight { get; init; }
    public BitmapSource? Tray1 { get; init; }
    public BitmapSource? Tray2 { get; init; }
    public BitmapSource? Tray3 { get; init; }
    public BitmapSource? ThirdDisc { get; init; }
    public int DiscCount { get; init; } = 2;
    public string BookletExtraction { get; init; } = "Top";
}
internal static class DigipakDimensions
{
    internal const float Unit = 2.42f / 142; // Same physical scale as the standard case.
    internal const float Panel = 138, Height = 124, Paper = 1;
    internal const float LeftFold = 12, RightFold = 10, ClosedDepth = 11;
    internal const float TrayWidth = 136, TrayHeight = 124, TrayDepth = 4;
    internal const float WellDiameter = 121, HubDiameter = 15, HubHeight = 3;
    internal const float CenterX = 67, CenterFromTop = 61.5f;
    internal const float BookletWidth = 120, BookletHeight = 120, BookletDepth = 1.5f;
    internal const float FrontHalfThickness = 1.2f;
    // The actual slit is visible at about 80% of the scanned panel height.
    internal const float PocketWidth = 125, PocketFromBottom = 24, PocketLeft = 6.5f;
    internal const float Left = -Panel / 2 - LeftFold - Panel;
    internal const float Right = Panel / 2 + RightFold;
    internal const float LeftHinge = -Panel / 2 - LeftFold / 2;
    internal const float RightHinge = Panel / 2 + RightFold / 2;
    internal static (double Left, double Right) Angles(double progress) =>
        (180 * (1 - Math.Clamp(progress * 2, 0, 1)), -180 * (1 - Math.Clamp(progress * 2 - 1, 0, 1)));
    internal const float ThreeLeftFold = 18, ThreeRightFold = 16.5f, ThreeFarFold = 11;
    // Four closed panels need distinct depth planes: center, far right, right, front.
    internal const float ThreeLeftHingeZ = 10.5f, ThreeRightHingeZ = 9, ThreeFarHingeZ = 4.5f;
    internal const float ThreeLeft = -Panel / 2 - ThreeLeftFold - Panel;
    internal const float ThreeRight = Panel / 2 + ThreeRightFold;
    internal const float ThreeFarRight = ThreeRight + Panel + ThreeFarFold;
    // The far-right flap shares the right-hand hinge direction: its intermediate
    // poses must swing in front of the case, although both +/-180 close identically.
    internal static (double Left, double Right, double FarRight) ThreeAngles(double progress) =>
        (180 * (1 - Math.Clamp(progress * 3, 0, 1)),
         -180 * (1 - Math.Clamp(progress * 3 - 1, 0, 1)),
         -180 * (1 - Math.Clamp(progress * 3 - 2, 0, 1)));
}
