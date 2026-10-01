using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

// Preview-only data: never writes the album's artwork roles or original files.
internal sealed record MultiCaseImageChoice(string Name,string Role,Func<BitmapSource> Load);
internal sealed record MultiCaseArtwork
{
    internal BitmapSource? Front { get; init; }
    internal BitmapSource? Back { get; init; }
    internal BitmapSource? FrontLeft { get; init; }
    internal BitmapSource? FrontRight { get; init; }
    internal BitmapSource? BackLeft { get; init; }
    internal BitmapSource? BackRight { get; init; }
    internal BitmapSource? Disc1 { get; init; }
    internal BitmapSource? Disc2 { get; init; }
    internal BitmapSource? Disc3 { get; init; }
    internal BitmapSource? Disc4 { get; init; }
    internal BitmapSource? BookletFront { get; init; }
    internal BitmapSource? BookletBack { get; init; }

    // Slots: front tray, centre front, centre reverse, rear tray.
    // Only the complete Disc1+Disc2 pair uses the two outer trays.
    internal (int Number, BitmapSource? Image) DiscAtSlot(int slot)
    {
        bool outerPair = Disc1 is not null && Disc2 is not null && Disc3 is null && Disc4 is null;
        return slot switch {
            1 => (1, Disc1),
            2 => (2, outerPair ? null : Disc2),
            3 => (3, Disc3),
            4 => outerPair ? (2, Disc2) : (4, Disc4),
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    internal static BitmapSource? Rotate(BitmapSource? image,int degrees)
    {
        if(image is null||degrees%360==0)return image;
        var rotated=new TransformedBitmap(image,new RotateTransform(degrees));rotated.Freeze();return rotated;
    }
    // mode: automatic, panel only, full insert with 6+138+6 mm folds.
    internal static (BitmapSource? Panel,BitmapSource? Left,BitmapSource? Right) Split(BitmapSource? image,int mode)
    {
        if(image is null)return (null,null,null);
        if(mode==1)return (image,null,null);
        var regions=RearInsertArtwork.GetRegions(image,mode==2);
        return (RearInsertArtwork.Crop(image,regions.Panel),
            regions.Left is {} left?RearInsertArtwork.Crop(image,left):null,
            regions.Right is {} right?RearInsertArtwork.Crop(image,right):null);
    }
}
