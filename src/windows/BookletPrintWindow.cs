using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

internal sealed class BookletPrintWindow : Window
{
    private readonly ComboBox scope=new(){SelectedIndex=0},scale=new(){SelectedIndex=0},split=new(){SelectedIndex=0},orientation=new(){SelectedIndex=0};
    private readonly TextBox first=new(),last=new(),width=new(){Text="120"},margin=new(){Text="10"},gap=new(){Text="5"};
    private readonly TextBlock error=new(){Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,0)};
    private readonly TextBlock previewDescription=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};
    private readonly TextBlock previewStatus=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,0)};
    private readonly TextBlock previewLabel=new(){TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,6,0,0)};
    private readonly TextBlock position=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new(12,0,12,0)};
    private readonly Image previewImage=new(){Stretch=Stretch.Fill};
    private readonly Button previous=new(){Content="◀"},nextSheet=new(){Content="▶"},print=new();
    private readonly DispatcherTimer previewTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private readonly Grid layout=new();
    private readonly ScrollViewer controls=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly DockPanel previewPane=new();
    private readonly int count,current;
    private readonly IReadOnlyList<BookletPage>? pages;
    private bool rendering,closed,previewReady;
    private int revision,previewIndex,previewCount;
    private CancellationTokenSource? previewCancellation;
    private Size previewPaper;
    private Rect previewPrintable;
    // Worker-owned bounded thumbnail cache. Original pixel dimensions keep layout identical
    // to full-resolution printing, without holding all decoded booklet pages in memory.
    private sealed record CachedImage(BitmapSource Image,Size Pixels);
    private readonly Dictionary<int,CachedImage> previewCache=[];
    private readonly Queue<int> cacheOrder=[];
    internal int FirstPage {get;private set;}
    internal int LastPage {get;private set;}
    internal BookletPrintSettings Settings {get;private set;}=new(true);
    internal PrintDialog? SelectedPrinter {get;private set;}
    internal BookletPrintWindow(int count,int current,IReadOnlyList<BookletPage>? pages=null)
    {
        if(count<1||current<0||current>=count)throw new ArgumentOutOfRangeException(nameof(current));
        this.count=count;this.current=current;
        if(pages is not null&&pages.Count!=count)throw new ArgumentException("Page count differs");this.pages=pages;
        Title=LocalizationService.Select("ブックレットの印刷設定","Booklet print settings");Width=1150;Height=760;MinWidth=560;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,23,27));Foreground=Brushes.White;
        var frame=new DockPanel{Margin=new(20)};Content=frame;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);frame.Children.Add(footer);
        frame.Children.Add(layout);
        var root=new StackPanel();controls.Content=root;layout.Children.Add(controls);layout.Children.Add(previewPane);
        root.Children.Add(new TextBlock{Text=LocalizationService.Select("ページ画像を紙へ印刷","Print booklet images to paper"),FontSize=20,Margin=new(0,0,0,16)});
        scope.ItemsSource=new[]{LocalizationService.Select("現在のページ","Current page"),LocalizationService.Select("すべてのページ","All pages"),LocalizationService.Select("ページ範囲を指定","Page range")};scope.SelectedIndex=0;
        scale.ItemsSource=new[]{LocalizationService.Select("配置枠に収める（縦横比を維持）","Fit each layout cell (keep aspect ratio)"),LocalizationService.Select("幅をmmで指定（画像ごと）","Set each image width in mm")};scale.SelectedIndex=0;
        split.ItemsSource=new[]{LocalizationService.Select("1ページ / 枚（通常）","1 page per sheet"),LocalizationService.Select("2ページ / 枚","2 pages per sheet"),LocalizationService.Select("4ページ / 枚","4 pages per sheet"),LocalizationService.Select("6ページ / 枚","6 pages per sheet"),LocalizationService.Select("9ページ / 枚","9 pages per sheet")};split.SelectedIndex=0;
        orientation.ItemsSource=new[]{LocalizationService.Select("A4 縦","A4 portrait"),LocalizationService.Select("A4 横","A4 landscape")};orientation.SelectedIndex=0;
        void Row(string label,FrameworkElement field){var grid=new Grid{Margin=new(0,0,0,10)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(130)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(field,1);grid.Children.Add(field);root.Children.Add(grid);}
        Row(LocalizationService.Select("印刷するページ","Pages"),scope);
        first.Text="1";last.Text=count.ToString(CultureInfo.InvariantCulture);
        var range=new StackPanel{Orientation=Orientation.Horizontal};first.Width=70;last.Width=70;range.Children.Add(first);range.Children.Add(new TextBlock{Text=" ～ ",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,8,0)});range.Children.Add(last);range.Children.Add(new TextBlock{Text=$" / {count}",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,0,0)});
        Row(LocalizationService.Select("ページ範囲","Range"),range);Row(LocalizationService.Select("1枚にまとめる","Pages per sheet"),split);Row(LocalizationService.Select("サイズ","Sizing"),scale);
        Row(LocalizationService.Select("幅（mm）","Width (mm)"),width);Row(LocalizationService.Select("用紙端の余白（mm）","Sheet margin (mm)"),margin);Row(LocalizationService.Select("画像間隔（mm）","Image spacing (mm)"),gap);
        Row(LocalizationService.Select("仮プレビュー用紙","Sample preview paper"),orientation);
        foreach(var input in new Control[]{scope,scale,split,orientation,first,last,width,margin,gap}){input.Height=30;input.VerticalContentAlignment=VerticalAlignment.Center;input.Foreground=Brushes.Black;input.Background=Brushes.White;}
        root.Children.Add(new TextBlock{Text=LocalizationService.Select("複数画像をまとめる場合は、すべてのページ／範囲を選択してください。左→右、上→下の順に配置し、空き枠は白紙です。\n設定変更はプレビューへ自動反映されます。幅指定が配置枠を超えると印刷できません。\nプリンターを選ぶまではA4・印刷不可領域5mmの仮表示です。選択後は実際の用紙設定を同じ欄に表示します。","Select All pages or a range to combine images. Images run left to right, top to bottom; unused cells remain blank.\nSetting changes update the preview automatically. Exact widths must fit their cells.\nBefore printer selection, the sample uses A4 with 5 mm unprintable borders. Afterwards the same pane uses actual paper settings."),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,0)});
        footer.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,10,0,0)};
        var selectPrinter=new Button{Content=LocalizationService.Select("プリンター・用紙を選ぶ…","Choose printer and paper…"),IsEnabled=pages is not null,Padding=new(12,6,12,6)};
        selectPrinter.Click+=(_,_)=>ChoosePrinter();buttons.Children.Add(selectPrinter);
        print.Content=LocalizationService.Select("この内容で印刷","Print these sheets");print.Padding=new(12,6,12,6);print.Margin=new(8,0,0,0);print.IsEnabled=false;
        print.Click+=(_,_)=>{if(SelectedPrinter is not null&&previewReady&&TryApply())DialogResult=true;};buttons.Children.Add(print);
        var cancel=new Button{Content=LocalizationService.Select("キャンセル","Cancel"),IsCancel=true,Margin=new(8,0,0,0),Padding=new(12,6,12,6)};buttons.Children.Add(cancel);footer.Children.Add(buttons);
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);previewPane.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text=LocalizationService.Select("プレビュー","Preview"),FontSize=20,Margin=new(0,0,0,10)});heading.Children.Add(previewDescription);
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);previewPane.Children.Add(bottom);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,8,0,0)};
        navigation.Children.Add(previous);navigation.Children.Add(position);navigation.Children.Add(nextSheet);bottom.Children.Add(navigation);bottom.Children.Add(previewLabel);bottom.Children.Add(previewStatus);
        var backdrop=new Border{Background=new SolidColorBrush(Color.FromRgb(53,57,63)),Padding=new(12)};previewPane.Children.Add(backdrop);
        RenderOptions.SetBitmapScalingMode(previewImage,BitmapScalingMode.HighQuality);backdrop.Child=new Viewbox{Stretch=Stretch.Uniform,Child=previewImage};
        previous.Click+=(_,_)=>MovePreview(-1);nextSheet.Click+=(_,_)=>MovePreview(1);
        void Update(){first.IsEnabled=last.IsEnabled=scope.SelectedIndex==2;width.IsEnabled=scale.SelectedIndex==1;gap.IsEnabled=split.SelectedIndex!=0;}
        foreach(var combo in new[]{scope,scale,split,orientation})combo.SelectionChanged+=(_,_)=>{
            // Combining pages should not silently leave only the initially visible image.
            // Preserve an explicit custom range, and allow a later manual current-page choice.
            if(ReferenceEquals(combo,split)&&split.SelectedIndex>0&&scope.SelectedIndex==0)scope.SelectedIndex=1;
            Update();QueuePreview();
        };
        foreach(var text in new[]{first,last,width,margin,gap})text.TextChanged+=(_,_)=>QueuePreview();
        previewTimer.Tick+=(_,_)=>{previewTimer.Stop();RefreshPreview();};
        layout.SizeChanged+=(_,_)=>ArrangePreview();Loaded+=(_,_)=>QueuePreview();
        Closed+=(_,_)=>{closed=true;previewTimer.Stop();previewCancellation?.Cancel();previewImage.Source=null;if(!rendering){previewCache.Clear();cacheOrder.Clear();}};
        Update();QueuePreview();
    }
    private void ArrangePreview()
    {
        layout.ColumnDefinitions.Clear();layout.RowDefinitions.Clear();
        bool wide=layout.ActualWidth>=940;
        Grid.SetColumn(controls,0);Grid.SetRow(controls,0);
        Grid.SetColumn(previewPane,wide?1:0);Grid.SetRow(previewPane,wide?0:1);
        if(wide){layout.ColumnDefinitions.Add(new(){Width=new GridLength(460)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});controls.MaxHeight=double.PositiveInfinity;previewPane.Margin=new(20,0,0,0);}
        else{layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=new GridLength(.48,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=new GridLength(.52,GridUnitType.Star)});controls.MaxHeight=double.PositiveInfinity;previewPane.Margin=new(0,12,0,0);}
    }
    private bool TryApply()
    {
        error.Foreground=Brushes.Salmon;error.Text="";
        int from=scope.SelectedIndex==0?current+1:1,to=scope.SelectedIndex==0?current+1:count;
        if(scope.SelectedIndex==2&&(!int.TryParse(first.Text,out from)||!int.TryParse(last.Text,out to)||from<1||to<from||to>count)){error.Text=LocalizationService.Select($"範囲は1〜{count}で、開始≦終了にしてください。",$"Enter a range from 1 to {count}, with start ≤ end.");return false;}
        bool Parse(TextBox input,out double value)=>double.TryParse(input.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)&&double.IsFinite(value);
        double w=120;
        if(scale.SelectedIndex==1&&(!Parse(width,out w)||w<=0||w>1000)){error.Text=LocalizationService.Select("幅は0より大きく1000mm以下で入力してください。","Width must be greater than 0 and at most 1000 mm.");return false;}
        if(!Parse(margin,out double m)||m<0||m>50){error.Text=LocalizationService.Select("余白は0〜50mmで入力してください。","Margin must be between 0 and 50 mm.");return false;}
        double g=5;if(split.SelectedIndex!=0&&(!Parse(gap,out g)||g<0||g>30)){error.Text=LocalizationService.Select("画像間隔は0〜30mmで入力してください。","Image spacing must be between 0 and 30 mm.");return false;}
        int perSheet=split.SelectedIndex switch{1=>2,2=>4,3=>6,4=>9,_=>1};
        FirstPage=from;LastPage=to;Settings=new(scale.SelectedIndex==0,w,m,perSheet,g);return true;
    }
    private void ChoosePrinter()
    {
        try{
            var dialog=SelectedPrinter is null?new PrintDialog():new PrintDialog{PrintQueue=SelectedPrinter.PrintQueue,PrintTicket=SelectedPrinter.PrintTicket.Clone()};if(dialog.ShowDialog()!=true)return;
            var ticket=dialog.PrintTicket.Clone();ticket.PagesPerSheet=1;
            dialog.PrintTicket=dialog.PrintQueue.MergeAndValidatePrintTicket(dialog.PrintTicket,ticket).ValidatedPrintTicket;
            if(dialog.PrintTicket.PagesPerSheet is >1)throw new InvalidOperationException(LocalizationService.Select("ドライバー側の集約印刷を1ページにしてください。","Set driver pages per sheet to 1."));
            var capabilities=dialog.PrintQueue.GetPrintCapabilities(dialog.PrintTicket);var area=capabilities.PageImageableArea;
            if(area is null||capabilities.OrientedPageMediaWidth is not double w||capabilities.OrientedPageMediaHeight is not double h)throw new InvalidOperationException(LocalizationService.Select("プリンターの用紙・印刷可能範囲を取得できません。","Cannot determine paper and printable area."));
            SelectedPrinter=dialog;previewPaper=new(w,h);previewPrintable=new(area.OriginWidth,area.OriginHeight,area.ExtentWidth,area.ExtentHeight);orientation.IsEnabled=false;QueuePreview();
        }catch(Exception ex){error.Foreground=Brushes.Salmon;error.Text=LocalizationService.Select("プリンター設定を取得できません: ","Cannot read printer settings: ")+ex.Message;}
    }
    internal BookletPrintPaginator CreatePaginator()
    {
        if(pages is null||SelectedPrinter is null)throw new InvalidOperationException("Select printer first");
        return new(BookletPrintPaginator.Select(pages,FirstPage,LastPage),previewPaper,previewPrintable,Settings);
    }
    private void QueuePreview()
    {
        if(closed)return;revision++;previewCancellation?.Cancel();previewReady=false;print.IsEnabled=false;
        previewImage.Source=null;previous.IsEnabled=nextSheet.IsEnabled=false;previewLabel.Text="";position.Text="";
        previewStatus.Text=LocalizationService.Select("プレビューを更新しています…","Updating preview…");
        if(pages is null){previewStatus.Text=LocalizationService.Select("画像がありません。","No images available.");return;}
        previewTimer.Stop();previewTimer.Start();
    }
    private void MovePreview(int step)
    {
        if(!previewReady)return;previewIndex=Math.Clamp(previewIndex+step,0,previewCount-1);QueuePreview();
    }
    private CachedImage LoadPreviewImage(int source,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(previewCache.TryGetValue(source,out var existing))return existing;
        var original=pages![source].LoadImage();var pixels=new Size(original.PixelWidth,original.PixelHeight);
        double factor=Math.Min(1,1100d/Math.Max(pixels.Width,pixels.Height));
        BitmapSource scaled=factor<1?new TransformedBitmap(original,new ScaleTransform(factor,factor)):original;
        var converted=new FormatConvertedBitmap(scaled,PixelFormats.Pbgra32,null,0);int stride=checked(converted.PixelWidth*4);
        var bytes=new byte[checked(stride*converted.PixelHeight)];converted.CopyPixels(bytes,stride,0);
        var image=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Pbgra32,null,bytes,stride);image.Freeze();
        var result=new CachedImage(image,pixels);previewCache[source]=result;cacheOrder.Enqueue(source);
        while(cacheOrder.Count>12)previewCache.Remove(cacheOrder.Dequeue());ct.ThrowIfCancellationRequested();return result;
    }
    private async void RefreshPreview()
    {
        if(closed||pages is null||rendering)return;
        int requested=revision;if(!TryApply()){previewStatus.Text=LocalizationService.Select("入力を修正するとプレビューが更新されます。","Correct the input to update the preview.");return;}
        const double u=BookletPrintPaginator.UnitsPerMm;
        bool actual=SelectedPrinter is not null,landscape=orientation.SelectedIndex==1;
        var paper=actual?previewPaper:new Size((landscape?297:210)*u,(landscape?210:297)*u);
        var printable=actual?previewPrintable:new Rect(5*u,5*u,paper.Width-10*u,paper.Height-10*u);
        previewDescription.Text=actual?$"{SelectedPrinter!.PrintQueue.FullName} · {paper.Width/u:0.#} × {paper.Height/u:0.#} mm"+LocalizationService.Select("（実用紙／ドライバー集約1ページ）"," (actual paper / driver 1-up)"):LocalizationService.Select("仮表示：A4・印刷不可領域5mm。プリンター未選択です。","Sample: A4, 5 mm unprintable borders. No printer selected.");
        int firstSource=FirstPage-1,length=LastPage-FirstPage+1;var settings=Settings;
        int sheetCount=(length+settings.PagesPerSheet-1)/settings.PagesPerSheet,target=Math.Clamp(previewIndex,0,sheetCount-1);
        rendering=true;using var cancellation=new CancellationTokenSource();previewCancellation=cancellation;
        try{
            var result=await Task.Run(()=>{
                var selected=Enumerable.Range(firstSource,length).Select(i=>new BookletPage(pages[i].Name,pages[i].Role,()=>LoadPreviewImage(i,cancellation.Token).Image)).ToArray();
                var paginator=new BookletPrintPaginator(selected,paper,printable,settings,i=>LoadPreviewImage(firstSource+i,cancellation.Token).Pixels);
                var page=paginator.GetPage(target);var drawing=((DrawingVisual)page.Visual).Drawing.Clone();drawing.Freeze();
                var image=new DrawingImage(drawing);image.Freeze();return(image,caption:paginator.SheetLabel(target));
            });
            if(closed||requested!=revision)return;
            previewImage.Width=paper.Width;previewImage.Height=paper.Height;previewImage.Source=result.image;
            previewIndex=target;previewCount=sheetCount;previewReady=true;position.Text=$"{target+1} / {sheetCount}";
            previewLabel.Text=result.caption;previewLabel.ToolTip=result.caption;previewStatus.Text=LocalizationService.Select($"{length}画像 → {sheetCount}枚。設定変更を自動反映します。",$"{length} images → {sheetCount} sheets. Changes update automatically.");
            previous.IsEnabled=target>0;nextSheet.IsEnabled=target+1<sheetCount;print.IsEnabled=actual;
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!closed&&requested==revision){previewStatus.Text=LocalizationService.Select("プレビューを表示できません。設定や画像を確認してください。","Cannot display preview. Check settings and images.");error.Text=ex.Message;}}
        finally{
            rendering=false;previewCancellation=null;
            if(closed){previewCache.Clear();cacheOrder.Clear();}
            else if(requested!=revision){previewTimer.Stop();previewTimer.Start();}
        }
    }
}
