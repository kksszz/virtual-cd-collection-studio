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
        _=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Directory.CreateDirectory(output);
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
        var portrait=BitmapSource.Create(100,200,600,600,PixelFormats.Gray8,null,Enumerable.Repeat((byte)160,20000).ToArray(),100);portrait.Freeze();
        var portraitBounds=fit.ImageBounds(portrait);Require(Math.Abs(portraitBounds.Width/portraitBounds.Height-.5)<.00001,"Portrait pages preserve proportions");
        try{new BookletPrintPaginator([new("bad","Page",()=>throw new IOException("broken image"))],paper,printable,new(true)).ValidatePages();throw new Exception("Broken page ignored");}catch(IOException){Console.WriteLine("PASS image failure aborts preflight");}
        Require(fit.PageCount==3&&fit.IsPageCountValid&&fit.GetPage(-1)==DocumentPage.Missing&&fit.GetPage(3)==DocumentPage.Missing,"Paginator page count and missing-page contract");
        fit.ValidatePages();var page=fit.GetPage(0);Require(page.Size==paper&&page.ContentBox==fitBounds,"DocumentPage contains only image layout");
        var raster=new RenderTargetBitmap((int)Math.Ceiling(paper.Width),(int)Math.Ceiling(paper.Height),96,96,PixelFormats.Pbgra32);raster.Render(page.Visual);Save(raster,Path.Combine(output,"booklet-print-page.png"));
        using(var package=System.IO.Packaging.Package.Open(Path.Combine(output,"booklet-print-check.xps"),FileMode.Create,FileAccess.ReadWrite))using(var xps=new XpsDocument(package))XpsDocument.CreateXpsDocumentWriter(xps).Write(fit);
        Console.WriteLine("PASS offline XPS serialization (no printer queue)");
        var many=Enumerable.Range(1,11).Select(i=>new BookletPage("Page "+i,"Page",()=>i%2==0?portrait:bitmap)).ToArray();
        foreach(int n in new[]{1,2,4,6,9})foreach(bool wide in new[]{false,true})
        {
            var paperSize=wide?landscape:paper;
            var multi=new BookletPrintPaginator(many,paperSize,new Rect(15,20,paperSize.Width-30,paperSize.Height-40),new(true,PagesPerSheet:n));multi.ValidatePages();
            Require(multi.PageCount==(many.Length+n-1)/n,$"{n}-up {(wide?"landscape":"portrait")} sheet count");
            bool separated=true;for(int a=0;a<n;a++)for(int b=a+1;b<n;b++)separated&=!multi.CellBounds(a).IntersectsWith(multi.CellBounds(b));Require(separated,"layout cells do not overlap");
            Require(multi.SheetLabel(0).StartsWith("Page 1")&&multi.SheetLabel(multi.PageCount-1).EndsWith("Page 11"),"source order and last partial sheet retained");
            var tail=(DrawingVisual)multi.GetPage(multi.PageCount-1).Visual;
            Require(tail.Drawing.Children.OfType<ImageDrawing>().Count()==(many.Length-1)%n+1,"last sheet contains only remaining images; unused cells blank");
            var draws=((DrawingVisual)multi.GetPage(0).Visual).Drawing.Children.OfType<ImageDrawing>().ToArray();
            Require(draws.Select((d,slot)=>multi.CellBounds(slot).Contains(d.Rect)&&Math.Abs(d.Rect.Width/d.Rect.Height-(slot%2==0?2:.5))<.00001).All(x=>x),"mixed image proportions retained inside each cell");
            var pageImage=new RenderTargetBitmap((int)Math.Ceiling(paperSize.Width),(int)Math.Ceiling(paperSize.Height),96,96,PixelFormats.Pbgra32);pageImage.Render(multi.GetPage(0).Visual);Save(pageImage,Path.Combine(output,$"booklet-{n}-up-{(wide?"landscape":"portrait")}.png"));
        }
        try{new BookletPrintPaginator(pages,paper,printable,new(false,120,10,4)).ValidatePages();throw new Exception("Exact width overflow ignored");}catch(InvalidOperationException){Console.WriteLine("PASS N-up exact size overflow aborts preflight");}
        try{new BookletPrintPaginator(pages,paper,printable,new(true,PagesPerSheet:3));throw new Exception("Unsupported N-up accepted");}catch(ArgumentException){Console.WriteLine("PASS unsupported N-up rejected");}
        try{new BookletPrintPaginator(pages,new Size(40,40),new Rect(0,0,40,40),new(true,MarginMm:0,PagesPerSheet:9,GapMm:30)).ValidatePages();throw new Exception("Invalid spacing accepted");}catch(InvalidOperationException){Console.WriteLine("PASS excessive image gap rejected");}
        var four=new BookletPrintPaginator(many,paper,printable,new(true,PagesPerSheet:4));four.ValidatePages();
        using(var package=System.IO.Packaging.Package.Open(Path.Combine(output,"booklet-four-up-check.xps"),FileMode.Create,FileAccess.ReadWrite))using(var xps=new XpsDocument(package))XpsDocument.CreateXpsDocumentWriter(xps).Write(four);
        Console.WriteLine("PASS N-up offline XPS serialization without a printer");
        int touched=0;
        var lazy=new BookletPrintPaginator([new("visible","Page",()=>{touched++;return bitmap;}),new("later","Page",()=>throw new IOException("later source unreadable"))],paper,printable,new(true));
        _=lazy.GetPage(0);Require(touched==2,"preview loads only the visible sheet");
        try{lazy.ValidatePages();throw new Exception("Preflight skipped later source");}catch(IOException){Console.WriteLine("PASS full print preflight checks sources outside visible preview");}
        var reduced=new BookletPrintPaginator([new("thumbnail","Page",()=>portrait)],paper,printable,new(false,120,0),_=>new Size(601,301));
        var reducedBounds=reduced.GetPage(0).ContentBox;
        Require(Math.Abs(reducedBounds.Width/reducedBounds.Height-601d/301)<.00001,"thumbnail preview layout uses original dimensions, not rounded thumbnail ratio");
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var options=new BookletPrintWindow(3,1,pages);
        object Field(string name)=>typeof(BookletPrintWindow).GetField(name,flags)!.GetValue(options)!;
        bool Apply()=>(bool)typeof(BookletPrintWindow).GetMethod("TryApply",flags)!.Invoke(options,null)!;
        Require(Apply()&&options.FirstPage==2&&options.LastPage==2&&options.Settings.Fit,"Current-page print settings default");
        foreach(int choice in new[]{1,2,3,4})
        {
            ((ComboBox)Field("split")).SelectedIndex=0;((ComboBox)Field("scope")).SelectedIndex=0;((ComboBox)Field("split")).SelectedIndex=choice;
            Require(Apply()&&((ComboBox)Field("scope")).SelectedIndex==1&&options.FirstPage==1&&options.LastPage==3,"N-up automatically includes all pages: "+options.Settings.PagesPerSheet);
        }
        ((ComboBox)Field("split")).SelectedIndex=0;Require(Apply()&&options.FirstPage==1&&options.LastPage==3,"returning to one-up keeps selected all-page scope");
        ((ComboBox)Field("scope")).SelectedIndex=1;Require(Apply()&&options.FirstPage==1&&options.LastPage==3,"All-page print settings");
        ((ComboBox)Field("scope")).SelectedIndex=2;((TextBox)Field("first")).Text="2";((TextBox)Field("last")).Text="3";Require(Apply()&&options.FirstPage==2&&options.LastPage==3,"Range settings validated");
        ((ComboBox)Field("split")).SelectedIndex=2;Require(Apply()&&((ComboBox)Field("scope")).SelectedIndex==2&&options.FirstPage==2&&options.LastPage==3,"N-up preserves explicit page range");
        ((ComboBox)Field("scope")).SelectedIndex=0;Require(Apply()&&options.FirstPage==2&&options.LastPage==2,"manual current-page override remains available");((ComboBox)Field("scope")).SelectedIndex=2;
        ((TextBox)Field("first")).Text="0";Require(!Apply(),"Invalid UI page range blocked");((TextBox)Field("first")).Text="1";
        ((ComboBox)Field("scale")).SelectedIndex=1;((TextBox)Field("width")).Text="NaN";Require(!Apply(),"Nonfinite UI width blocked");((TextBox)Field("width")).Text="120";
        ((TextBox)Field("margin")).Text="-1";Require(!Apply(),"Negative UI margin blocked");((TextBox)Field("margin")).Text="10";Require(Apply()&&!options.Settings.Fit&&options.Settings.WidthMm==120,"Exact sizing settings");
        ((ComboBox)Field("scale")).SelectedIndex=0;((ComboBox)Field("split")).SelectedIndex=2;
        Require(Apply()&&options.Settings.PagesPerSheet==4&&options.Settings.Fit,"four images per sheet UI setting");
        ((TextBox)Field("gap")).Text="NaN";Require(!Apply(),"nonfinite image spacing blocked");((TextBox)Field("gap")).Text="5";Require(Apply(),"image spacing setting");
        var root=(System.Windows.Controls.Panel)options.Content;root.Background=options.Background;root.Measure(new Size(500,600));double uiHeight=root.DesiredSize.Height;root.Arrange(new Rect(0,0,500,uiHeight));root.UpdateLayout();
        var ui=new RenderTargetBitmap(500,(int)Math.Ceiling(uiHeight),96,96,PixelFormats.Pbgra32);ui.Render(root);Save(ui,Path.Combine(output,"booklet-print-settings.png"));
        options.Close();Console.WriteLine("PASS settings rendered; no native print dialog or physical print invoked");
        var live=new BookletPrintWindow(many.Length,0,many);
        object LiveField(string name)=>typeof(BookletPrintWindow).GetField(name,flags)!.GetValue(live)!;
        void Move(int step)=>typeof(BookletPrintWindow).GetMethod("MovePreview",flags)!.Invoke(live,[step]);
        void Wait(Func<bool> ready)
        {
            var frame=new System.Windows.Threading.DispatcherFrame();var watch=System.Diagnostics.Stopwatch.StartNew();
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>{if(ready()||watch.Elapsed>TimeSpan.FromSeconds(10))frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);timer.Stop();
            if(!ready())Console.WriteLine("Preview diagnostics: "+((TextBlock)LiveField("error")).Text+" / "+((TextBlock)LiveField("previewStatus")).Text+" / rendering="+LiveField("rendering"));
            Require(ready(),"live preview completed within timeout");
        }
        void WaitImage()=>Wait(()=>(bool)LiveField("previewReady"));
        ((ComboBox)LiveField("split")).SelectedIndex=2;WaitImage();
        Require(((ComboBox)LiveField("scope")).SelectedIndex==1&&live.LastPage==many.Length,"live preview auto-switches from current to all pages");
        Require((int)LiveField("previewCount")==3&&((Image)LiveField("previewImage")).Source is DrawingImage,"embedded preview shows four-up layout");
        Require(live.SelectedPrinter is null&&!((Button)LiveField("print")).IsEnabled,"preview alone cannot submit a print job");
        Move(1);WaitImage();Require((int)LiveField("previewIndex")==1,"inline preview next sheet");
        Move(99);WaitImage();Require((int)LiveField("previewIndex")==2,"inline preview final partial sheet");
        ((TextBox)LiveField("margin")).Text="NaN";Require(((Image)LiveField("previewImage")).Source is null&&!((Button)LiveField("print")).IsEnabled,"invalid edited setting immediately clears stale preview");
        ((TextBox)LiveField("margin")).Text="12";((TextBox)LiveField("gap")).Text="8";((ComboBox)LiveField("orientation")).SelectedIndex=1;WaitImage();
        Require(live.Settings.MarginMm==12&&live.Settings.GapMm==8&&((Image)LiveField("previewImage")).Width>((Image)LiveField("previewImage")).Height,"margin, spacing and orientation update in place");
        ((ComboBox)LiveField("split")).SelectedIndex=3;((ComboBox)LiveField("split")).SelectedIndex=4;WaitImage();Require((int)LiveField("previewCount")==2,"rapid edits display only latest nine-up setting");
        ((ComboBox)LiveField("split")).SelectedIndex=2;((ComboBox)LiveField("orientation")).SelectedIndex=0;WaitImage();Move(-99);WaitImage();
        var liveRoot=(Panel)live.Content;liveRoot.Background=live.Background;
        foreach(var size in new[]{new Size(1100,660),new Size(580,860)})
        {
            liveRoot.Measure(size);liveRoot.Arrange(new Rect(size));liveRoot.UpdateLayout();
            var layout=(Grid)LiveField("layout");var pane=(DockPanel)LiveField("previewPane");var controls=(ScrollViewer)LiveField("controls");
            Require(size.Width>900?Grid.GetColumn(pane)==1:Grid.GetRow(pane)==1,"preview moves right / below with available width");
            Require(pane.ActualHeight>100&&controls.ActualHeight>100,"preview and editable settings remain visible");
            var screenshot=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);screenshot.Render(liveRoot);Save(screenshot,Path.Combine(output,size.Width>900?"booklet-print-inline-wide.png":"booklet-print-inline-narrow.png"));
        }
        live.Close();
        var coalesced=new BookletPrintWindow(3,0,pages.Select(p=>p with{LoadImage=()=>{System.Threading.Thread.Sleep(200);return bitmap;}}).ToArray());
        ((ComboBox)typeof(BookletPrintWindow).GetField("scope",flags)!.GetValue(coalesced)!).SelectedIndex=1;
        Wait(()=>(bool)typeof(BookletPrintWindow).GetField("rendering",flags)!.GetValue(coalesced)!);
        ((TextBox)typeof(BookletPrintWindow).GetField("margin",flags)!.GetValue(coalesced)!).Text="17";
        ((ComboBox)typeof(BookletPrintWindow).GetField("split",flags)!.GetValue(coalesced)!).SelectedIndex=2;
        Wait(()=>(bool)typeof(BookletPrintWindow).GetField("previewReady",flags)!.GetValue(coalesced)!);
        Require(coalesced.Settings.MarginMm==17&&coalesced.Settings.PagesPerSheet==4&&(int)typeof(BookletPrintWindow).GetField("previewCount",flags)!.GetValue(coalesced)! == 1,"in-flight older render cannot replace latest settings");coalesced.Close();
        var badLive=new BookletPrintWindow(1,0,[new("bad","Page",()=>throw new IOException("fixture failure"))]);
        Wait(()=>((TextBlock)typeof(BookletPrintWindow).GetField("error",flags)!.GetValue(badLive)!).Text.Contains("fixture failure"));
        Require(!((Button)typeof(BookletPrintWindow).GetField("print",flags)!.GetValue(badLive)!).IsEnabled,"failed preview cannot enable printing");badLive.Close();
        var closing=new BookletPrintWindow(1,0,[new("slow","Page",()=>{System.Threading.Thread.Sleep(200);return bitmap;})]);
        Wait(()=>(bool)typeof(BookletPrintWindow).GetField("rendering",flags)!.GetValue(closing)!);closing.Close();
        Wait(()=>!(bool)typeof(BookletPrintWindow).GetField("rendering",flags)!.GetValue(closing)!);Console.WriteLine("PASS closing during background preview safely discards result");
        static void Save(BitmapSource image,string path){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(path);png.Save(stream);}
    }
}
