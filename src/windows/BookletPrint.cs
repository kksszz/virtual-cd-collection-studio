using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed record BookletPrintSettings(bool Fit,double WidthMm=120,double MarginMm=10,int PagesPerSheet=1,double GapMm=5);
internal sealed class BookletPrintPaginator : DocumentPaginator
{
    private readonly IReadOnlyList<BookletPage> pages;
    private readonly BookletPrintSettings settings;
    private readonly Rect printable;
    private readonly Size size;
    private readonly Func<int,Size>? imageDimensions;
    private sealed record Placement(int Source,Rect Bounds,int PixelWidth,int PixelHeight);
    private readonly IReadOnlyList<Placement>?[] sheets;
    internal const double UnitsPerMm=96/25.4;
    internal BookletPrintPaginator(IReadOnlyList<BookletPage> pages,Size paper,Rect printable,BookletPrintSettings settings,Func<int,Size>? imageDimensions=null)
    {
        if(pages.Count==0||!Valid(paper.Width)||!Valid(paper.Height)||printable.IsEmpty||!Valid(printable.Width)||!Valid(printable.Height)||!double.IsFinite(printable.X)||!double.IsFinite(printable.Y)||printable.X<0||printable.Y<0||printable.Right>paper.Width+.1||printable.Bottom>paper.Height+.1)throw new ArgumentException("Invalid printable paper area");
        if(!double.IsFinite(settings.WidthMm)||settings.WidthMm<=0||settings.WidthMm>1000||!double.IsFinite(settings.MarginMm)||settings.MarginMm<0||settings.MarginMm>50)throw new ArgumentException("Invalid print size/margin");
        if(settings.PagesPerSheet is not (1 or 2 or 4 or 6 or 9)||!double.IsFinite(settings.GapMm)||settings.GapMm<0||settings.GapMm>30)throw new ArgumentException("Invalid pages per sheet / gap");
        this.pages=pages;size=paper;this.printable=printable;this.settings=settings;this.imageDimensions=imageDimensions;sheets=new IReadOnlyList<Placement>?[PageCount];
    }
    private static bool Valid(double value)=>double.IsFinite(value)&&value>0;
    internal static IReadOnlyList<BookletPage> Select(IReadOnlyList<BookletPage> pages,int first,int last)
    {
        if(first<1||last<first||last>pages.Count)throw new ArgumentOutOfRangeException(nameof(first),"Invalid booklet page range");
        return pages.Skip(first-1).Take(last-first+1).ToArray();
    }
    internal Rect PrintableBounds
    {
        get{double m=settings.MarginMm*UnitsPerMm;var area=Rect.Intersect(printable,new Rect(m,m,Math.Max(0,size.Width-m*2),Math.Max(0,size.Height-m*2)));
            if(area.IsEmpty||!Valid(area.Width)||!Valid(area.Height))throw new InvalidOperationException(LocalizationService.Select("余白が大きすぎます。","Margins leave no printable area."));return area;}
    }
    internal Rect CellBounds(int slot)
    {
        if(slot<0||slot>=settings.PagesPerSheet)throw new ArgumentOutOfRangeException(nameof(slot));
        bool landscape=size.Width>size.Height;
        var (columns,rows)=settings.PagesPerSheet switch{2=>landscape?(2,1):(1,2),4=>(2,2),6=>landscape?(3,2):(2,3),9=>(3,3),_=>(1,1)};
        var area=PrintableBounds;double gap=settings.GapMm*UnitsPerMm;
        double w=(area.Width-(columns-1)*gap)/columns,h=(area.Height-(rows-1)*gap)/rows;
        if(!Valid(w)||!Valid(h))throw new InvalidOperationException(LocalizationService.Select("画像間隔が大きすぎます。","Image spacing leaves no room for pages."));
        return new(area.X+slot%columns*(w+gap),area.Y+slot/columns*(h+gap),w,h);
    }
    internal Rect ImageBounds(BitmapSource image)=>ImageBounds(image,CellBounds(0));
    private Rect ImageBounds(BitmapSource image,Rect area)=>ImageBounds(new Size(image.PixelWidth,image.PixelHeight),area);
    private Rect ImageBounds(Size pixels,Rect area)
    {
        if(!Valid(pixels.Width)||!Valid(pixels.Height))throw new InvalidDataException("Empty image");
        double width=settings.Fit?Math.Min(area.Width,area.Height*pixels.Width/pixels.Height):settings.WidthMm*UnitsPerMm;
        double height=width*pixels.Height/pixels.Width;
        if(width>area.Width+.01||height>area.Height+.01)throw new InvalidOperationException(LocalizationService.Select("指定サイズが配置枠を超えます。用紙・向き・余白・1枚あたりのページ数・幅を変更するか「配置枠に収める」を選んでください。","Requested size exceeds its layout cell. Change paper, orientation, margin, pages per sheet or width, or choose Fit."));
        return new(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height);
    }
    private IReadOnlyList<Placement> EnsureSheet(int sheetIndex)
    {
        if(sheets[sheetIndex] is { } existing)return existing;
        var placements=new List<Placement>();int start=sheetIndex*settings.PagesPerSheet;
        // A live preview only needs the visible sheet. Full preflight still checks all sheets.
        for(int slot=0;slot<settings.PagesPerSheet&&start+slot<pages.Count;slot++)
        {
            var pixels=imageDimensions?.Invoke(start+slot)??Dimensions(pages[start+slot].LoadImage());var bounds=ImageBounds(pixels,CellBounds(slot));
            placements.Add(new(start+slot,bounds,(int)pixels.Width,(int)pixels.Height));
        }
        return sheets[sheetIndex]=placements;
    }
    internal string SheetLabel(int index)=>string.Join(" · ",EnsureSheet(index).Select(p=>pages[p.Source].Name));
    internal void ValidatePages(){for(int i=0;i<PageCount;i++)_ = EnsureSheet(i);}
    public override DocumentPage GetPage(int pageNumber)
    {
        if(pageNumber<0||pageNumber>=PageCount)return DocumentPage.Missing;var placements=EnsureSheet(pageNumber);
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        var content=Rect.Empty;
        using(var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White,null,new Rect(size));
            foreach(var placement in placements)
            {
                var image=pages[placement.Source].LoadImage();
                var pixels=imageDimensions?.Invoke(placement.Source)??Dimensions(image);
                if(pixels.Width!=placement.PixelWidth||pixels.Height!=placement.PixelHeight)throw new InvalidDataException(LocalizationService.Select("画像の寸法が変わりました。プレビューを作り直してください。","Image dimensions changed. Rebuild the preview."));
                drawing.DrawImage(image,placement.Bounds);content.Union(placement.Bounds);
            }
        }
        return new DocumentPage(visual,size,new Rect(size),content);
    }
    public override bool IsPageCountValid=>true;
    private static Size Dimensions(BitmapSource image)=>new(image.PixelWidth,image.PixelHeight);
    public override int PageCount=>(pages.Count+settings.PagesPerSheet-1)/settings.PagesPerSheet;
    public override Size PageSize{get=>size;set{if(value!=size)throw new NotSupportedException("Paper size is fixed for this print job.");}}
    public override IDocumentPaginatorSource Source=>null!;
}
