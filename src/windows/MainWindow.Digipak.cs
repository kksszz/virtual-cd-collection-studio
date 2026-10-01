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
        } finally { _updatingCaseType=false; }
    }
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
            UpdateBookletExtractionCombo();QueueCoverFlowRefresh();StatusText.Text=$"ケースタイプを{choice.Content}に設定しました";
        } catch(Exception ex){MessageBox.Show(this,ex.Message,"ケースタイプを保存できません",MessageBoxButton.OK,MessageBoxImage.Warning);UpdateCaseTypeCombo();}
    }
    private static BitmapSource? LoadDigipakPicture(IReadOnlyList<AlbumImageSource> sources,IReadOnlyDictionary<string,string> roles,string role,string fallback,int width)
    {
        // Explicit role assignments always win. Filename fallback is only used
        // for this measured prototype's known scans, never a general image guess.
        var source=sources.FirstOrDefault(s=>roles.ContainsKey(s.RoleKey)&&GetEffectiveArtworkRole(s,roles)==role)
            ??sources.FirstOrDefault(s=>string.Equals(Path.GetFileName(s.DisplayName),fallback,StringComparison.OrdinalIgnoreCase))
            ??sources.FirstOrDefault(s=>GetEffectiveArtworkRole(s,roles)==role);
        if(source is null)return null;try{return LoadBitmap(source,width);}catch{return null;}
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
            var inner=Picture("DigipakInnerLeft","Booklet010.jpg");
            var outer=Picture("DigipakOuterRight","Booklet011.jpg");
            BitmapSource? Spine(BitmapSource? image,int total){
                if(image is null||image.PixelWidth<=image.PixelHeight*1.12)return null;
                int start=Math.Clamp((int)Math.Round(image.PixelWidth*138d/total),0,image.PixelWidth-1);
                var crop=new CroppedBitmap(image,new Int32Rect(start,0,image.PixelWidth-start,image.PixelHeight));crop.Freeze();return crop;
            }
            return new(TrimSpine(inner,138,150),TrimSpine(outer,138,148),Picture("DigipakTrays","Digipac001.jpg")){
                LeftFold=Picture("LeftSpine","")??(actRaiser?Spine(inner,150):null),
                RightFold=Picture("RightSpine","")??(actRaiser?Spine(outer,148):null),
                FarRightFold=Picture("DigipakFarFold",""),OuterFarRight=Picture("DigipakOuterFarRight",""),
                Tray1=Picture("DigipakTray1",""),Tray2=Picture("DigipakTray2",""),Tray3=Picture("DigipakTray3",""),
                ThirdDisc=Picture("Disc3",""),
                DiscCount=settings.CaseType=="Digipak3"?3:2,BookletExtraction=settings.BookletExtraction=="Left"?"Left":"Top"
            };
        }
    }
}
