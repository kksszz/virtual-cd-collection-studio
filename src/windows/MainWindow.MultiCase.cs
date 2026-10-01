using System.Windows.Media.Imaging;

namespace ZipMp3Player;

public partial class MainWindow
{
    private sealed partial class AlbumListItem
    {
        public MultiCaseArtwork? MultiCase { get; private set; }
        private MultiCaseArtwork? LoadMultiCaseArtwork(string directory,int width)
        {
            if(LoadCaseAppearance(Album.Path).CaseType!="Multi24")return null;
            var sources=GetCaseArtworkSources(Album,directory);var roles=LoadArtworkRoles(Album.Path);
            BitmapSource? Picture(params string[] wanted) {
                foreach(var role in wanted) {
                    var source=sources.Where(s=>GetEffectiveArtworkRole(s,roles)==role)
                        .OrderByDescending(s=>roles.TryGetValue(s.RoleKey,out var saved)&&saved!="Auto").FirstOrDefault();
                    if(source is not null) { try{
                        var image=LoadBitmap(source,width);
                        return role.StartsWith("Disc",StringComparison.Ordinal)?DiscArtwork.CropScannerMargin(image):image;
                    }catch{return null;} }
                }
                return null;
            }
            (BitmapSource? Panel,BitmapSource? Left,BitmapSource? Right) Cover(bool front) {
                if(!front) {
                    var back=LoadCaseArtwork(directory,width);
                    return (back.Back,back.Spine,back.RightSpine);
                }
                if(Picture(front?"MultiFrontWithSpines":"BackWithSpines") is {} full)return MultiCaseArtwork.Split(full,2);
                if(Picture(front?"MultiFrontPanel":"Back") is {} panel)return (panel,null,null);
                // Reuse the existing standard Front/spread and Back assignments
                // after their established crop rules; do not split them twice.
                var existing=LoadCaseArtwork(directory,width);
                return (front?existing.Front:existing.Back,null,null);
            }
            var f=Cover(true);var b=Cover(false);
            // The separate Front booklet lies on the central tray. Do not use
            // the 24mm exterior insert (with its spines) as the booklet cover.
            BitmapSource? booklet=Picture("Front"),bookletBack=Picture("FrontInside");
            foreach(var role in new[]{"FrontSpread","FrontSpreadReversed","FrontSpreadVertical"}) {
                if(booklet is not null&&bookletBack is not null)break;
                if(Picture(role) is not {} spread)continue;
                spread=RearInsertArtwork.CropWhiteBorder(spread);
                booklet??=CropArtwork(spread,role=="FrontSpreadVertical"?"TopHalf":role=="FrontSpreadReversed"?"LeftHalf":"RightHalf");
                bookletBack??=CropArtwork(spread,role=="FrontSpreadVertical"?"BottomHalfRotated":role=="FrontSpreadReversed"?"RightHalf":"LeftHalf");
            }
            if(booklet is not null)booklet=RearInsertArtwork.CropWhiteBorder(booklet);
            return new MultiCaseArtwork {Front=f.Panel,Back=b.Panel,
                BookletFront=booklet,BookletBack=bookletBack,
                FrontLeft=Picture("MultiFrontLeft")??f.Left,FrontRight=Picture("MultiFrontRight")??f.Right,
                BackLeft=b.Left,BackRight=b.Right,
                Disc1=Picture("Disc1"),Disc2=Picture("Disc2"),Disc3=Picture("Disc3"),Disc4=Picture("Disc4")};
        }
    }
}
