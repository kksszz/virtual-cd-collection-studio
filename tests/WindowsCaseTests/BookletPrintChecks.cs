using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using ZipMp3Player;

internal static class BookletPrintChecks
{
    internal static void Run(string output)
    {
        _=new Application();Directory.CreateDirectory(output);
        static void Require(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
            dc.DrawRectangle(Brushes.CornflowerBlue,null,new Rect(0,0,600,300));dc.DrawRectangle(Brushes.OrangeRed,null,new Rect(0,0,300,150));dc.DrawRectangle(Brushes.Gold,null,new Rect(300,150,300,150));
        }
        var bitmap=new RenderTargetBitmap(600,300,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        var pages=Enumerable.Range(1,3).Select(i=>new BookletPage("Page "+i,"Page",()=>bitmap)).ToArray();
        var chosen=BookletPrintPaginator.Select(pages,2,3);
        Require(chosen.Count==2&&chosen[0].Name=="Page 2"&&chosen[1].Name=="Page 3","Print range is 1-based and retains viewer order");
        foreach(var range in new[]{(0,1),(2,1),(1,4)})try{BookletPrintPaginator.Select(pages,range.Item1,range.Item2);throw new Exception("Bad range accepted");}catch(ArgumentOutOfRangeException){Console.WriteLine("PASS invalid print range rejected");}
        const double u=BookletPrintPaginator.UnitsPerMm;
        var paper=new Size(210*u,297*u);var printable=new Rect(15,20,paper.Width-30,paper.Height-40);
        var fit=new BookletPrintPaginator(pages,paper,printable,new(true));var fitBounds=fit.ImageBounds(bitmap);
        Require(Math.Abs(fitBounds.Width/fitBounds.Height-2)<.00001&&fitBounds.Left>=10*u-.001&&fitBounds.Right<=paper.Width-10*u+.001,"Fit keeps aspect ratio and sheet margins");
        var exact=new BookletPrintPaginator(pages,paper,printable,new(false,120,0));var exactBounds=exact.ImageBounds(bitmap);
        Require(Math.Abs(exactBounds.Width-120*u)<.00001&&Math.Abs(exactBounds.Height-60*u)<.00001,"Exact width is 120mm, height 60mm independent of image DPI");
        Require(exactBounds.Left>=printable.Left&&exactBounds.Right<=printable.Right,"Hardware unprintable borders respected");
        try{new BookletPrintPaginator(pages,paper,printable,new(false,240)).ValidatePages();throw new Exception("Oversize accepted");}catch(InvalidOperationException){Console.WriteLine("PASS oversized exact dimensions stop before job submission");}
        var landscape=new Size(paper.Height,paper.Width);new BookletPrintPaginator(pages,landscape,new Rect(0,0,landscape.Width,landscape.Height),new(false,240)).ValidatePages();Console.WriteLine("PASS 240mm spread fits landscape A4");
        var portrait=BitmapSource.Create(100,200,600,600,PixelFormats.Gray8,null,new byte[20000],100);portrait.Freeze();
        var portraitBounds=fit.ImageBounds(portrait);Require(Math.Abs(portraitBounds.Width/portraitBounds.Height-.5)<.00001,"Portrait pages preserve proportions");
        try{new BookletPrintPaginator([new("bad","Page",()=>throw new IOException("broken image"))],paper,printable,new(true)).ValidatePages();throw new Exception("Broken page ignored");}catch(IOException){Console.WriteLine("PASS image failure aborts preflight");}
        Require(fit.PageCount==3&&fit.IsPageCountValid&&fit.GetPage(-1)==DocumentPage.Missing&&fit.GetPage(3)==DocumentPage.Missing,"Paginator page count and missing-page contract");
        fit.ValidatePages();var page=fit.GetPage(0);Require(page.Size==paper&&page.ContentBox==fitBounds,"DocumentPage contains only image layout");
        var raster=new RenderTargetBitmap((int)Math.Ceiling(paper.Width),(int)Math.Ceiling(paper.Height),96,96,PixelFormats.Pbgra32);raster.Render(page.Visual);Save(raster,Path.Combine(output,"booklet-print-page.png"));
        using(var package=System.IO.Packaging.Package.Open(Path.Combine(output,"booklet-print-check.xps"),FileMode.Create,FileAccess.ReadWrite))using(var xps=new XpsDocument(package))XpsDocument.CreateXpsDocumentWriter(xps).Write(fit);
        Console.WriteLine("PASS offline XPS serialization (no printer queue)");
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var options=new BookletPrintWindow(3,1);
        object Field(string name)=>typeof(BookletPrintWindow).GetField(name,flags)!.GetValue(options)!;
        bool Apply()=>(bool)typeof(BookletPrintWindow).GetMethod("TryApply",flags)!.Invoke(options,null)!;
        Require(Apply()&&options.FirstPage==2&&options.LastPage==2&&options.Settings.Fit,"Current-page print settings default");
        ((ComboBox)Field("scope")).SelectedIndex=1;Require(Apply()&&options.FirstPage==1&&options.LastPage==3,"All-page print settings");
        ((ComboBox)Field("scope")).SelectedIndex=2;((TextBox)Field("first")).Text="2";((TextBox)Field("last")).Text="3";Require(Apply()&&options.FirstPage==2&&options.LastPage==3,"Range settings validated");
        ((TextBox)Field("first")).Text="0";Require(!Apply(),"Invalid UI page range blocked");((TextBox)Field("first")).Text="1";
        ((ComboBox)Field("scale")).SelectedIndex=1;((TextBox)Field("width")).Text="NaN";Require(!Apply(),"Nonfinite UI width blocked");((TextBox)Field("width")).Text="120";
        ((TextBox)Field("margin")).Text="-1";Require(!Apply(),"Negative UI margin blocked");((TextBox)Field("margin")).Text="10";Require(Apply()&&!options.Settings.Fit&&options.Settings.WidthMm==120,"Exact sizing settings");
        var root=(System.Windows.Controls.Panel)options.Content;root.Background=options.Background;root.Measure(new Size(500,600));double uiHeight=root.DesiredSize.Height;root.Arrange(new Rect(0,0,500,uiHeight));root.UpdateLayout();
        var ui=new RenderTargetBitmap(500,(int)Math.Ceiling(uiHeight),96,96,PixelFormats.Pbgra32);ui.Render(root);Save(ui,Path.Combine(output,"booklet-print-settings.png"));
        options.Close();Console.WriteLine("PASS settings rendered; no native print dialog or physical print invoked");
        static void Save(BitmapSource image,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);png.Save(stream);}
    }
}
