using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed record BookletPrintSettings(bool Fit,double WidthMm=120,double MarginMm=10);
internal sealed class BookletPrintPaginator : DocumentPaginator
{
    private readonly IReadOnlyList<BookletPage> pages;
    private readonly BookletPrintSettings settings;
    private readonly Rect printable;
    private Size size;
    internal const double UnitsPerMm=96/25.4;
    internal BookletPrintPaginator(IReadOnlyList<BookletPage> pages,Size paper,Rect printable,BookletPrintSettings settings)
    {
        if(pages.Count==0||!Valid(paper.Width)||!Valid(paper.Height)||printable.IsEmpty||!Valid(printable.Width)||!Valid(printable.Height)||!double.IsFinite(printable.X)||!double.IsFinite(printable.Y)||printable.X<0||printable.Y<0||printable.Right>paper.Width+.1||printable.Bottom>paper.Height+.1)throw new ArgumentException("Invalid printable paper area");
        if(!double.IsFinite(settings.WidthMm)||settings.WidthMm<=0||settings.WidthMm>1000||!double.IsFinite(settings.MarginMm)||settings.MarginMm<0||settings.MarginMm>50)throw new ArgumentException("Invalid print size/margin");
        this.pages=pages;size=paper;this.printable=printable;this.settings=settings;
    }
    private static bool Valid(double value)=>double.IsFinite(value)&&value>0;
    internal static IReadOnlyList<BookletPage> Select(IReadOnlyList<BookletPage> pages,int first,int last)
    {
        if(first<1||last<first||last>pages.Count)throw new ArgumentOutOfRangeException(nameof(first),"Invalid booklet page range");
        return pages.Skip(first-1).Take(last-first+1).ToArray();
    }
    internal Rect ImageBounds(BitmapSource image)
    {
        // A margin is relative to the sheet edge, while hardware unprintable borders also apply.
        double margin=settings.MarginMm*UnitsPerMm;
        var area=Rect.Intersect(printable,new Rect(margin,margin,Math.Max(0,size.Width-margin*2),Math.Max(0,size.Height-margin*2)));
        if(area.IsEmpty||!Valid(area.Width)||!Valid(area.Height))throw new InvalidOperationException(LocalizationService.Select("余白が大きすぎます。","Margins leave no printable area."));
        if(image.PixelWidth<1||image.PixelHeight<1)throw new InvalidDataException("Empty image");
        double width=settings.Fit?Math.Min(area.Width,area.Height*image.PixelWidth/image.PixelHeight):settings.WidthMm*UnitsPerMm;
        double height=width*image.PixelHeight/image.PixelWidth;
        if(width>area.Width+.01||height>area.Height+.01)throw new InvalidOperationException(LocalizationService.Select("指定サイズが印刷可能範囲を超えます。用紙・向き・余白・幅を変更するか「用紙に収める」を選んでください。","Requested size exceeds the printable area. Change paper, orientation, margin or width, or choose Fit."));
        return new(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height);
    }
    internal void ValidatePages()
    {
        // Before submitting a job, fail on unreadable images or oversized exact-size pages.
        // Do not retain every decoded page: a large booklet could exhaust memory.
        foreach(var page in pages){var image=page.LoadImage();_ = ImageBounds(image);}
    }
    public override DocumentPage GetPage(int pageNumber)
    {
        if(pageNumber<0||pageNumber>=pages.Count)return DocumentPage.Missing;
        var image=pages[pageNumber].LoadImage();var bounds=ImageBounds(image);
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(Brushes.White,null,new Rect(size));drawing.DrawImage(image,bounds);}
        return new DocumentPage(visual,size,new Rect(size),bounds);
    }
    public override bool IsPageCountValid=>true;
    public override int PageCount=>pages.Count;
    public override Size PageSize{get=>size;set{if(value!=size)throw new NotSupportedException("Paper size is fixed for this print job.");}}
    public override IDocumentPaginatorSource Source=>null!;
}
