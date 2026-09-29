using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZipMp3Player;

internal sealed class BookletPrintWindow : Window
{
    private readonly ComboBox scope=new(){SelectedIndex=0},scale=new(){SelectedIndex=0};
    private readonly TextBox first=new(),last=new(),width=new(){Text="120"},margin=new(){Text="10"};
    private readonly TextBlock error=new(){Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,0)};
    private readonly int count,current;
    internal int FirstPage {get;private set;}
    internal int LastPage {get;private set;}
    internal BookletPrintSettings Settings {get;private set;}=new(true);
    internal BookletPrintWindow(int count,int current)
    {
        if(count<1||current<0||current>=count)throw new ArgumentOutOfRangeException(nameof(current));
        this.count=count;this.current=current;
        Title=LocalizationService.Select("ブックレットの印刷設定","Booklet print settings");Width=540;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,23,27));Foreground=Brushes.White;
        var root=new StackPanel{Margin=new(20)};Content=root;
        root.Children.Add(new TextBlock{Text=LocalizationService.Select("ページ画像を紙へ印刷","Print booklet images to paper"),FontSize=20,Margin=new(0,0,0,16)});
        scope.ItemsSource=new[]{LocalizationService.Select("現在のページ","Current page"),LocalizationService.Select("すべてのページ","All pages"),LocalizationService.Select("ページ範囲を指定","Page range")};scope.SelectedIndex=0;
        scale.ItemsSource=new[]{LocalizationService.Select("用紙に収める（縦横比を維持）","Fit to paper (keep aspect ratio)"),LocalizationService.Select("幅をmmで指定（画像ごと）","Set each image width in mm")};scale.SelectedIndex=0;
        void Row(string label,FrameworkElement field){var grid=new Grid{Margin=new(0,0,0,10)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(130)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(field,1);grid.Children.Add(field);root.Children.Add(grid);}
        Row(LocalizationService.Select("印刷するページ","Pages"),scope);
        first.Text="1";last.Text=count.ToString(CultureInfo.InvariantCulture);
        var range=new StackPanel{Orientation=Orientation.Horizontal};first.Width=70;last.Width=70;range.Children.Add(first);range.Children.Add(new TextBlock{Text=" ～ ",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,8,0)});range.Children.Add(last);range.Children.Add(new TextBlock{Text=$" / {count}",VerticalAlignment=VerticalAlignment.Center,Margin=new(8,0,0,0)});
        Row(LocalizationService.Select("ページ範囲","Range"),range);Row(LocalizationService.Select("サイズ","Sizing"),scale);
        Row(LocalizationService.Select("幅（mm）","Width (mm)"),width);Row(LocalizationService.Select("用紙端の余白（mm）","Sheet margin (mm)"),margin);
        void Update(){first.IsEnabled=last.IsEnabled=scope.SelectedIndex==2;width.IsEnabled=scale.SelectedIndex==1;}
        scope.SelectionChanged+=(_,_)=>Update();scale.SelectionChanged+=(_,_)=>Update();Update();
        foreach(var input in new Control[]{scope,scale,first,last,width,margin}){input.Height=30;input.VerticalContentAlignment=VerticalAlignment.Center;input.Foreground=Brushes.Black;input.Background=Brushes.White;}
        root.Children.Add(new TextBlock{Text=LocalizationService.Select("例：CDブックレット1面は幅120mm、見開き画像は幅240mm。\n幅指定は各画像に同じ幅を適用します。サイズが用紙に収まらない場合は印刷しません。\n次の画面でプリンター・用紙・向き・部数を選択できます。画面の影や操作ボタンは印刷しません。","Examples: 120 mm for a CD booklet page, 240 mm for a spread.\nThe same width applies to each image. Oversized images will not print.\nSelect printer, paper, orientation and copies in the next dialog. Viewer shadows and controls are not printed."),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,4,0,0)});
        root.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,14,0,0)};
        var next=new Button{Content=LocalizationService.Select("プリンターを選ぶ…","Select printer…"),IsDefault=true,Padding=new(12,6,12,6)};
        var cancel=new Button{Content=LocalizationService.Select("キャンセル","Cancel"),IsCancel=true,Margin=new(8,0,0,0),Padding=new(12,6,12,6)};
        next.Click+=(_,_)=>{if(TryApply())DialogResult=true;};buttons.Children.Add(next);buttons.Children.Add(cancel);root.Children.Add(buttons);
    }
    private bool TryApply()
    {
        error.Text="";
        int from=scope.SelectedIndex==0?current+1:1,to=scope.SelectedIndex==0?current+1:count;
        if(scope.SelectedIndex==2&&(!int.TryParse(first.Text,out from)||!int.TryParse(last.Text,out to)||from<1||to<from||to>count)){error.Text=LocalizationService.Select($"範囲は1〜{count}で、開始≦終了にしてください。",$"Enter a range from 1 to {count}, with start ≤ end.");return false;}
        bool Parse(TextBox input,out double value)=>double.TryParse(input.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)&&double.IsFinite(value);
        double w=120;
        if(scale.SelectedIndex==1&&(!Parse(width,out w)||w<=0||w>1000)){error.Text=LocalizationService.Select("幅は0より大きく1000mm以下で入力してください。","Width must be greater than 0 and at most 1000 mm.");return false;}
        if(!Parse(margin,out double m)||m<0||m>50){error.Text=LocalizationService.Select("余白は0〜50mmで入力してください。","Margin must be between 0 and 50 mm.");return false;}
        FirstPage=from;LastPage=to;Settings=new(scale.SelectedIndex==0,w,m);return true;
    }
}
