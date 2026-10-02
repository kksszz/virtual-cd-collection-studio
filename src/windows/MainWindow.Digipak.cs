using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
namespace ZipMp3Player;

public partial class MainWindow
{
    private bool _updatingCaseType;
    private bool _updatingBookletExtraction;
    private static CaseAppearanceSettings LoadCaseAppearance(string albumPath)
    {
        try { var path=GetCaseAppearancePath(albumPath);return File.Exists(path)?JsonSerializer.Deserialize<CaseAppearanceSettings>(File.ReadAllText(path))??new():new(); }
        catch { return new(); }
    }
    private void UpdateCaseTypeCombo()
    {
        if(CaseTypeCombo is null)return;_updatingCaseType=true;
        try { var type=_album is null?"Standard":LoadCaseAppearance(_album.Path).CaseType;
            CaseTypeCombo.SelectedItem=CaseTypeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,type))??CaseTypeCombo.Items[0];
            CaseTypeCombo.IsEnabled=_album is not null;
            UpdateBookletExtractionCombo();
            UpdateDigipakArtworkRoleLabels(type);
        } finally { _updatingCaseType=false; }
    }
    private void UpdateDigipakArtworkRoleLabels(string caseType)
    {
        if (ArtworkRoleCombo is null) return;
        foreach (var item in ArtworkRoleCombo.Items.OfType<ComboBoxItem>())
        {
            if (DigipakArtworkRoleLabel(caseType, item.Tag?.ToString()) is { } label)
                item.Content = label;
        }
    }
    private static bool IsArtworkRoleAvailable(string caseType, string? role)
    {
        if (role is null) return false;
        var digipak = caseType is "Digipak2" or "Digipak3";
        if (role == "DigipakInnerLeft") return false; // Existing assignments remain visible when selected.
        if (role.StartsWith("Digipak", StringComparison.Ordinal))
            return digipak && (caseType == "Digipak3" || role is not
                ("DigipakOuterFarRight" or "DigipakFarFold" or "DigipakInnerFarFold" or "DigipakTray3"));
        if (role.StartsWith("Multi", StringComparison.Ordinal)) return caseType == "Multi24";
        if (role == "Disc4") return caseType == "Multi24";
        if (role == "Disc3") return caseType is "Digipak3" or "Multi24";
        if (role == "Inlay") return !digipak && caseType != "Multi24";
        if (role == "BackWithSpines") return !digipak;
        return true;
    }
    private static string? DigipakArtworkRoleLabel(string caseType, string? role) => role switch
    {
        "Back" => caseType is "Digipak2" or "Digipak3"
            ? LocalizationService.Select("Disc1の裏", "Reverse of Disc 1") : "Back",
        "DigipakOuterRight" => caseType is "Digipak2" or "Digipak3"
            ? LocalizationService.Select("Disc2の裏", "Reverse of Disc 2") : "デジパック・右外面",
        "DigipakOuterFarRight" => caseType == "Digipak3"
            ? LocalizationService.Select("Disc3の裏", "Reverse of Disc 3") : "デジパック・最右外面",
        "LeftSpine" => caseType is "Digipak2" or "Digipak3"
            ? LocalizationService.Select("デジパック・外側 左折り目", "Digipak outer left fold") : "左Spine",
        "RightSpine" => caseType is "Digipak2" or "Digipak3"
            ? LocalizationService.Select("デジパック・外側 中央折り目", "Digipak outer center fold") : "右Spine",
        "DigipakFarFold" => caseType == "Digipak3"
            ? LocalizationService.Select("デジパック・外側 右折り目", "Digipak outer right fold") : "デジパック・3番目の折り目",
        _ => null
    };
    private void UpdateBookletExtractionCombo()
    {
        if(BookletExtractionCombo is null)return;
        _updatingBookletExtraction=true;
        try {
            var settings=_album is null?new CaseAppearanceSettings():LoadCaseAppearance(_album.Path);
            var isDigipak=_album is not null&&settings.CaseType is "Digipak2" or "Digipak3";
            BookletExtractionPanel.Visibility=isDigipak?Visibility.Visible:Visibility.Collapsed;
            BookletExtractionCombo.SelectedItem=BookletExtractionCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item=>Equals(item.Tag,settings.BookletExtraction))??BookletExtractionCombo.Items[0];
            BookletExtractionCombo.IsEnabled=isDigipak;
        } finally { _updatingBookletExtraction=false; }
    }
    private void BookletExtraction_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_updatingBookletExtraction||_album is null||BookletExtractionCombo.SelectedItem is not ComboBoxItem choice)return;
        try {
            var settings=LoadCaseAppearance(_album.Path);
            settings.BookletExtraction=choice.Tag?.ToString()=="Left"?"Left":"Top";
            var path=GetCaseAppearancePath(_album.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
            _albums.FirstOrDefault(item=>string.Equals(item.Album.Path,_album.Path,StringComparison.OrdinalIgnoreCase))?.RefreshImageCount();
            QueueCoverFlowRefresh();StatusText.Text=$"冊子の取り出し方向を{choice.Content}に設定しました";
        } catch(Exception ex){MessageBox.Show(this,ex.Message,"冊子の設定を保存できません",MessageBoxButton.OK,MessageBoxImage.Warning);UpdateBookletExtractionCombo();}
    }
    private void CaseType_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(_updatingCaseType||_album is null||CaseTypeCombo.SelectedItem is not ComboBoxItem choice)return;
        try {
            var settings=LoadCaseAppearance(_album.Path);
            string oldType=settings.CaseType;
            settings.CaseType=choice.Tag?.ToString() is "Digipak2" or "Digipak3" or "Multi24"?choice.Tag!.ToString()!:"Standard";
            if(settings.CaseType=="Digipak3"&&oldType=="Standard")settings.BookletExtraction="Left";
            var path=GetCaseAppearancePath(_album.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
            _albums.FirstOrDefault(item=>string.Equals(item.Album.Path,_album.Path,StringComparison.OrdinalIgnoreCase))?.RefreshImageCount();
            UpdateBookletExtractionCombo();UpdateDigipakArtworkRoleLabels(settings.CaseType);UpdateArtworkRoleCombo();
            QueueCoverFlowRefresh();StatusText.Text=$"ケースタイプを{choice.Content}に設定しました";
        } catch(Exception ex){MessageBox.Show(this,ex.Message,"ケースタイプを保存できません",MessageBoxButton.OK,MessageBoxImage.Warning);UpdateCaseTypeCombo();}
    }
    private static BitmapSource? LoadDigipakPicture(IReadOnlyList<AlbumImageSource> sources,IReadOnlyDictionary<string,string> roles,string role,string fallback,int width)
    {
        var source=SelectDigipakPictureSource(sources,roles,role,fallback);
        if(source is null)return null;try{return LoadBitmap(source,width);}catch{return null;}
    }
    private static (BitmapSource Panel,BitmapSource Fold) SplitDigipakInnerLeftWithFold(BitmapSource image,bool threeDiscs)
    {
        int foldWidth=threeDiscs?(int)DigipakDimensions.ThreeLeftFold:(int)DigipakDimensions.LeftFold;
        int panelPixels=Math.Clamp((int)Math.Round(image.PixelWidth*DigipakDimensions.Panel/(DigipakDimensions.Panel+foldWidth)),1,image.PixelWidth-1);
        var panel=new CroppedBitmap(image,new Int32Rect(0,0,panelPixels,image.PixelHeight));
        var fold=new CroppedBitmap(image,new Int32Rect(panelPixels,0,image.PixelWidth-panelPixels,image.PixelHeight));
        panel.Freeze();fold.Freeze();return (panel,fold);
    }
    private static AlbumImageSource? SelectDigipakPictureSource(IReadOnlyList<AlbumImageSource> sources,
        IReadOnlyDictionary<string,string> roles,string role,string fallback)
    {
        // Older profiles may assign the same physical face more than once.
        // The last saved assignment wins, regardless of gallery/source order.
        foreach(var assignment in roles.Reverse())
        {
            if(!string.Equals(assignment.Value,role,StringComparison.OrdinalIgnoreCase))continue;
            var chosen=sources.FirstOrDefault(source=>string.Equals(source.RoleKey,assignment.Key,StringComparison.OrdinalIgnoreCase));
            if(chosen is not null)return chosen;
        }
        // Filename fallback is only used for the measured prototype's scans.
        return (!string.IsNullOrEmpty(fallback)
                ? sources.FirstOrDefault(s=>string.Equals(Path.GetFileName(s.DisplayName),fallback,StringComparison.OrdinalIgnoreCase))
                : null)
            ??sources.FirstOrDefault(s=>GetEffectiveArtworkRole(s,roles)==role);
    }
    private sealed partial class AlbumListItem
    {
        private BookletContent LoadDigipakBooklet()
        {
            var sources=GetCaseArtworkSources(Album,GetDownloadedArtworkDirectory(Album.Path));
            var pages=sources.Where(s=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(s.DisplayName),@"^Booklet00[1-7]\.jpg$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderBy(s=>s.DisplayName,StringComparer.OrdinalIgnoreCase).Select(s=>new BookletPage(s.DisplayName,"Page",()=>LoadBitmap(s,3000))).ToList();
            if(pages.Count==0)throw new InvalidOperationException("ブックレット画像がありません。");
            return new BookletContent(pages[0].LoadImage(),pages);
        }
        private DigipakArtwork? LoadDigipakArtwork(string directory,int width)
        {
            var settings=LoadCaseAppearance(Album.Path);
            if(settings.CaseType is not ("Digipak2" or "Digipak3"))return null;
            var sources=GetCaseArtworkSources(Album,directory);var roles=LoadArtworkRoles(Album.Path);
            bool actRaiser=settings.CaseType=="Digipak2"&&(Title.Contains("ActRaiser",StringComparison.OrdinalIgnoreCase)||Album.Path.Contains("ActRaiser",StringComparison.OrdinalIgnoreCase));
            BitmapSource? Picture(string role,string file)=>LoadDigipakPicture(sources,roles,role,actRaiser?file:"",width);
            BitmapSource? TrimSpine(BitmapSource? image,int panel,int total){if(image is null||image.PixelWidth<=image.PixelHeight*1.12)return image;
                int pixels=Math.Clamp((int)Math.Round(image.PixelWidth*(double)panel/total),1,image.PixelWidth);var crop=new CroppedBitmap(image,new Int32Rect(0,0,pixels,image.PixelHeight));crop.Freeze();return crop;}
            var combined=Picture("DigipakInnerLeftWithFold","");
            var inner=Picture("DigipakFrontBack","")
                ??Picture("DigipakInnerLeft",combined is null?"Booklet010.jpg":"");
            (BitmapSource Panel,BitmapSource Fold)? split=combined is { PixelWidth: > 1 }
                ?SplitDigipakInnerLeftWithFold(combined,settings.CaseType=="Digipak3"):null;
            var outer=Picture("DigipakOuterRight","Booklet011.jpg");
            BitmapSource? Spine(BitmapSource? image,int total){
                if(image is null||image.PixelWidth<=image.PixelHeight*1.12)return null;
                int start=Math.Clamp((int)Math.Round(image.PixelWidth*138d/total),0,image.PixelWidth-1);
                var crop=new CroppedBitmap(image,new Int32Rect(start,0,image.PixelWidth-start,image.PixelHeight));crop.Freeze();return crop;
            }
            return new(inner is null?split?.Panel:TrimSpine(inner,138,150),TrimSpine(outer,138,148),Picture("DigipakTrays","Digipac001.jpg")){
                OuterFront=Picture("DigipakFront",""),
                LeftFold=Picture("LeftSpine","")??(actRaiser?Spine(inner,150):null),
                RightFold=Picture("RightSpine","")??(actRaiser?Spine(outer,148):null),
                InnerLeftFold=Picture("DigipakInnerLeftFold","")??split?.Fold,
                InnerRightFold=Picture("DigipakInnerRightFold",""),
                InnerFarRightFold=Picture("DigipakInnerFarFold",""),
                FarRightFold=Picture("DigipakFarFold",""),OuterFarRight=Picture("DigipakOuterFarRight",""),
                Tray1=Picture("DigipakTray1",""),Tray2=Picture("DigipakTray2",""),Tray3=Picture("DigipakTray3",""),
                ThirdDisc=Picture("Disc3",""),
                DiscCount=settings.CaseType=="Digipak3"?3:2,BookletExtraction=settings.BookletExtraction=="Left"?"Left":"Top"
            };
        }
    }
}
