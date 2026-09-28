using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed class AlbumScanPage : INotifyPropertyChanged
{
    internal string Path {get;}
    private string name;
    public string Name {get=>name;set{name=value;PropertyChanged?.Invoke(this,new(nameof(Name)));}}
    internal AlbumScanPage(string path,string name){Path=path;this.name=name;}
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class AlbumScanWindow : Window
{
    private readonly ComboBox scanners=new(){DisplayMemberPath=nameof(ScannerDevice.Name),MinWidth=220};
    private readonly ComboBox dpi=new(){ItemsSource=new[]{75,100,150,200,300,400,600,800,1200},Text="600",IsEditable=true,Width=90};
    private readonly ComboBox color=new(){ItemsSource=new[]{"カラー（24bit）","グレー（8bit）","白黒（1bit）"},SelectedIndex=0,Width=150};
    private readonly TextBox brightness=new(){Text="0",Width=65},contrast=new(){Text="0",Width=65},threshold=new(){Text="128",Width=55};
    private readonly ObservableCollection<AlbumScanPage> pages=[];
    private readonly ListBox list=new(){DisplayMemberPath=nameof(AlbumScanPage.Name),Width=220,Margin=new(0,0,12,0)};
    private readonly Image preview=new(){Stretch=Stretch.Uniform};
    private readonly TextBox name=new(){MinWidth=180};
    private readonly TextBlock status=new(){Text="原稿をセットして「1枚スキャン」を押してください。",TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,10)};
    private readonly ProgressBar progress=new(){Height=8,Visibility=Visibility.Collapsed,Margin=new(0,3,0,3)};
    private readonly StackPanel top=new();
    private readonly WrapPanel edits=new();
    private readonly ComboBox format=new(){ItemsSource=new[]{"JPG（品質95）","PNG（無劣化）"},SelectedIndex=0,Width=145};
    private readonly CheckBox showDriverSettings=new(){Content="EPSON／TWAINの詳細設定画面を表示",Foreground=Brushes.White,IsChecked=false,Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly WrapPanel quality=new(){Margin=new(0,6,0,0)};
    private readonly TextBlock driverNote=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray,Margin=new(0,7,0,0)};
    private readonly Button save=new(){Content="全画像をアルバムへ保存",Padding=new(16,8,16,8)},close=new(){Content="閉じる",Padding=new(16,8,16,8)};
    private readonly Func<IReadOnlyList<ScannedArtwork>,IProgress<string>,Task> persist;
    private readonly string session=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"vccs-album-scan-"+Guid.NewGuid().ToString("N"));
    private bool busy,saved;
    private int sequence;
    private readonly string? settingsPath;
    private string? savedDevice;
    private sealed record ScanSettings(int Dpi,int Color,int Brightness,int Contrast,int Threshold,string? Device);
    internal int SavedCount{get;private set;}
    internal AlbumScanWindow(string albumTitle,Func<IReadOnlyList<ScannedArtwork>,IProgress<string>,Task> persist,string? settingsPath=null)
    {
        this.persist=persist;this.settingsPath=settingsPath;Title="アルバム画像をスキャン — "+albumTitle;
        var workArea=SystemParameters.WorkArea;Width=Math.Min(1200,workArea.Width-32);Height=Math.Min(820,workArea.Height-32);MinWidth=Math.Min(850,Width);MinHeight=Math.Min(580,Height);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,23,27));Foreground=Brushes.White;
        var buttons=new Style(typeof(Button));buttons.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(44,49,58))));buttons.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));buttons.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(74,82,96))));buttons.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,5,9,5)));buttons.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3)));Resources.Add(typeof(Button),buttons);
        foreach(var type in new[]{typeof(TextBox),typeof(ComboBox),typeof(ComboBoxItem),typeof(ListBox)}){var style=new Style(type);style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(243,244,246))));style.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(Color.FromRgb(24,32,42))));Resources.Add(type,style);}
        var listItemStyle=new Style(typeof(ListBoxItem));listItemStyle.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(Color.FromRgb(24,32,42))));listItemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,5,6,5)));list.ItemContainerStyle=listItemStyle;
        Directory.CreateDirectory(session);
        var root=new DockPanel{Margin=new(16)};Content=root;DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="複数枚をスキャンして、補正後にアルバムへ保存",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        var options=new WrapPanel();options.Children.Add(scanners);Button(options,"一覧を更新",async()=>await ReloadScanners());options.Children.Add(new TextBlock{Text=" 解像度（dpi） ",VerticalAlignment=VerticalAlignment.Center});options.Children.Add(dpi);options.Children.Add(color);Button(options,"1枚スキャン",async()=>await Capture());top.Children.Add(options);
        void Field(string text,TextBox box){quality.Children.Add(new TextBlock{Text=text,Margin=new(8,0,4,0),VerticalAlignment=VerticalAlignment.Center});quality.Children.Add(box);}
        Field("明るさ",brightness);Field("コントラスト",contrast);Field("白黒しきい値",threshold);
        quality.Children.Add(new TextBlock{Text="明るさ・コントラスト：−1000～1000（標準0）／しきい値：0～255",Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});top.Children.Add(quality);
        color.SelectionChanged+=(_,_)=>threshold.IsEnabled=color.SelectedIndex==2;threshold.IsEnabled=false;
        var outputOptions=new WrapPanel{Margin=new(0,6,0,0)};outputOptions.Children.Add(new TextBlock{Text="保存形式 ",VerticalAlignment=VerticalAlignment.Center});outputOptions.Children.Add(format);outputOptions.Children.Add(showDriverSettings);top.Children.Add(outputOptions);
        showDriverSettings.Checked+=(_,_)=>UpdateControls();showDriverSettings.Unchecked+=(_,_)=>UpdateControls();
        format.SelectionChanged+=(_,_)=>{foreach(var page in pages)page.Name=System.IO.Path.ChangeExtension(page.Name,OutputExtension);};
        try{if(settingsPath is not null&&File.Exists(settingsPath)&&JsonSerializer.Deserialize<ScanSettings>(File.ReadAllText(settingsPath)) is {} settings){dpi.Text=settings.Dpi.ToString();color.SelectedIndex=Math.Clamp(settings.Color,0,2);brightness.Text=settings.Brightness.ToString();contrast.Text=settings.Contrast.ToString();threshold.Text=settings.Threshold.ToString();savedDevice=settings.Device;}}catch{}
        top.Children.Add(driverNote);scanners.SelectionChanged+=(_,_)=>UpdateControls();
        top.Children.Add(new TextBlock{Text="1枚ずつ原稿を交換して追加できます。画像を選び、切り抜き・歪み補正や回転を行ってください。\n保存名の例：Front.jpg、Back.jpg、Booklet001.jpg、Disc.jpg、Spine.jpg。既存の同名画像は上書きしません。",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,10),Foreground=new SolidColorBrush(Color.FromRgb(169,216,181))});
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(progress);footer.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(save);actions.Children.Add(close);footer.Children.Add(actions);
        var center=new DockPanel();root.Children.Add(center);DockPanel.SetDock(list,Dock.Left);center.Children.Add(list);list.ItemsSource=pages;
        var imageArea=new DockPanel();center.Children.Add(imageArea);DockPanel.SetDock(edits,Dock.Top);imageArea.Children.Add(edits);
        Button(edits,"左90°",async()=>await Rotate(-90));Button(edits,"右90°",async()=>await Rotate(90));Button(edits,"切り抜き・角度・歪み補正",async()=>await Crop());Button(edits,"元に戻す",async()=>await Undo());Button(edits,"一覧から外す",async()=>{if(list.SelectedItem is AlbumScanPage page){pages.Remove(page);list.SelectedIndex=pages.Count-1;UpdateControls();}await Task.CompletedTask;});
        var names=new DockPanel{Margin=new(3,5,3,8)};DockPanel.SetDock(names,Dock.Top);imageArea.Children.Add(names);var label=new TextBlock{Text="保存名（JPG／PNG） ",VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(label,Dock.Left);names.Children.Add(label);names.Children.Add(name);
        imageArea.Children.Add(new Border{Background=Brushes.Black,Padding=new(8),Child=preview});
        list.SelectionChanged+=async(_,_)=>{
            name.SetBinding(TextBox.TextProperty,new Binding(nameof(AlbumScanPage.Name)){Source=list.SelectedItem,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});UpdateControls();await ShowPreview();
        };
        save.Click+=async(_,_)=>await Save();close.Click+=(_,_)=>Close();
        Closing+=(_,e)=>{
            if(busy){e.Cancel=true;status.Text="処理が終わるまでお待ちください。スキャン中はドライバーの処理を安全に待ちます。";return;}
            if(!saved&&pages.Count>0&&MessageBox.Show(this,"未保存のスキャン画像を破棄して閉じますか？","スキャン画像",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
        };
        Closed+=(_,_)=>{try{foreach(var file in Directory.EnumerateFiles(session))File.Delete(file);Directory.Delete(session);}catch{/* Only this dialog's unique temporary session is eligible for cleanup. */}};
        Loaded+=async(_,_)=>await ReloadScanners();UpdateControls();
    }
    private void Button(Panel panel,string text,Func<Task> action){var button=new Button{Content=text};button.Click+=async(_,_)=>{try{await action();}catch(Exception ex){status.Text=ScannerService.Error(ex);}};panel.Children.Add(button);}
    private void UpdateControls(){top.IsEnabled=!busy;list.IsEnabled=!busy;edits.IsEnabled=!busy&&list.SelectedItem is AlbumScanPage;name.IsEnabled=edits.IsEnabled;save.IsEnabled=!busy&&pages.Count>0;close.IsEnabled=!busy;progress.IsIndeterminate=busy;progress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        bool twain=(scanners.SelectedItem as ScannerDevice)?.Twain==true;bool show=twain&&showDriverSettings.IsChecked==true;dpi.IsEnabled=color.IsEnabled=!show;quality.IsEnabled=!twain;showDriverSettings.IsEnabled=twain;
        driverNote.Text=twain?(show?"TWAIN詳細設定：EPSONの設定画面で画質を指定します。プロフェッショナル／書類向き／24bitカラー／600dpi／アンシャープマスク中／モアレ除去OFFを確認してください。":"TWAIN直接スキャン：設定画面は表示しません。解像度とカラー設定をここで指定できます。EPSON独自の画質設定・保存プリセットの継承は保証できません。必要な場合のみ詳細設定画面をONにしてください。") :"WIA：基本設定のみ対応。EPSON独自のアンシャープマスク・モアレ除去等は指定できません。";
    }
    private async Task Work(Func<Task> action){if(busy)return;busy=true;UpdateControls();try{await action();}catch(Exception ex){status.Text=ScannerService.Error(ex);}finally{busy=false;UpdateControls();}}
    private async Task ReloadScanners()=>await Work(async()=>{status.Text="スキャナーを確認しています…";var previous=(scanners.SelectedItem as ScannerDevice)?.Id??savedDevice;var devices=new List<ScannerDevice>();string warning="";
        try{devices.AddRange(await ScannerTwainService.Devices());}catch(Exception ex){warning="TWAINの一覧取得に失敗しました："+ex.Message;}
        try{devices.AddRange((await ScannerService.Devices()).Select(d=>d with {Name=d.Name+"（WIA・基本設定）"}));}catch(Exception ex){warning+=" WIAの一覧取得に失敗しました："+ex.Message;}
        scanners.ItemsSource=devices;scanners.SelectedItem=devices.FirstOrDefault(d=>d.Id==previous)??devices.FirstOrDefault(d=>d.Twain&&d.Name.Contains("GT-S650"))??devices.FirstOrDefault(d=>d.Twain)??devices.FirstOrDefault();status.Text=devices.Count==0?"スキャナーが見つかりません。ドライバーとUSB接続を確認してください。 "+warning:warning.Length>0?warning:"原稿をセットして「1枚スキャン」を押してください。";});
    private async Task Capture(){
        if(scanners.SelectedItem is not ScannerDevice device){status.Text="スキャナーを選択してください。";return;}
        if(!int.TryParse(dpi.Text,out int twainResolution)||twainResolution is <50 or >1200){status.Text="解像度は50～1200dpiで入力してください。";return;}
        if(device.Twain){await Work(async()=>{
            bool show=showDriverSettings.IsChecked==true;
            status.Text=show?"EPSONの設定画面で画質を確認し、スキャンしてください。":$"TWAINで{twainResolution}dpiの画像を読み取っています（設定画面は省略）…";
            var images=await ScannerTwainService.Scan(device,session,show,twainResolution,color.SelectedIndex);
            foreach(var path in images){var page=new AlbumScanPage(path,$"Booklet{++sequence:000}"+OutputExtension);pages.Add(page);list.SelectedItem=page;}
            await ShowPreview();status.Text=images.Count==0?"スキャンをキャンセルしました。取り込み済み画像はそのままです。":$"{pages.Count}枚取り込み済み。原稿を交換して次の1枚を追加するか、画像を補正して保存してください。";
        });return;}
        if(!int.TryParse(dpi.Text,out int resolution)||resolution is <50 or >1200||!int.TryParse(brightness.Text,out int b)||b is <-1000 or >1000||!int.TryParse(contrast.Text,out int c)||c is <-1000 or >1000||!int.TryParse(threshold.Text,out int t)||t is <0 or >255){status.Text="解像度は50～1200、明るさ・コントラストは−1000～1000、しきい値は0～255で入力してください。";return;}
        int mode=color.SelectedIndex;int dataType=mode==0?3:mode==1?2:0;
        await Work(async()=>{
            status.Text=$"{device.Name}で {resolution}dpi の画像を読み取っています…";
            var raw=await ScannerService.Scan(device,resolution,dataType,session,b,c,t);
            var path=System.IO.Path.ChangeExtension(raw,".png");await Task.Run(()=>ScannerService.Png(raw,path));File.Delete(raw);
            var page=new AlbumScanPage(path,$"Booklet{++sequence:000}"+OutputExtension);pages.Add(page);list.SelectedItem=page;
            if(settingsPath is not null)try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(new ScanSettings(resolution,mode,b,c,t,device.Id)));}catch{/* A preferences failure must not discard the scanned page. */}
            await ShowPreview();status.Text=$"{pages.Count}枚取り込み済み。原稿を交換して次の1枚を追加するか、画像を補正して保存してください。";
        });
    }
    private string OutputExtension=>format.SelectedIndex==1?".png":".jpg";
    private async Task ShowPreview(){var page=list.SelectedItem as AlbumScanPage;if(page is null){preview.Source=null;return;}try{var bitmap=await Task.Run(()=>ArtworkDeskew.Render(ScannerService.Load(page.Path),0,1600));await Dispatcher.InvokeAsync(()=>{if(ReferenceEquals(list.SelectedItem,page))preview.Source=bitmap;});}catch(Exception ex){await Dispatcher.InvokeAsync(()=>status.Text=ex.Message);}}
    private async Task Rotate(int degrees){if(list.SelectedItem is not AlbumScanPage page)return;await Work(async()=>{await Task.Run(()=>ArtworkRotationWriter.Rotate(page.Path,null,degrees,null));await ShowPreview();status.Text="回転しました。アルバムへの保存はまだ行っていません。";});}
    private async Task Crop(){
        if(list.SelectedItem is not AlbumScanPage page||busy)return;
        await Work(async()=>{
            var bitmap=await Task.Run(()=>ScannerService.Load(page.Path));var editor=new ArtworkPerspectiveWindow(page.Name,bitmap){Owner=this};if(editor.ShowDialog()!=true)return;
            await Task.Run(()=>editor.Disc is not null?ArtworkRotationWriter.Disc(page.Path,null,0,editor.Disc,null):editor.Corners is not null?ArtworkRotationWriter.Perspective(page.Path,null,0,editor.Corners,null):ArtworkRotationWriter.CropWithAngle(page.Path,null,0,editor.ManualCrop,editor.ManualAngle,null));
            await ShowPreview();status.Text="切り抜き・補正を適用しました。「元に戻す」でやり直せます。";
        });
    }
    private async Task Undo(){if(list.SelectedItem is not AlbumScanPage page)return;await Work(async()=>{var backup=await Task.Run(()=>ArtworkRotationWriter.FindUndoBackup(page.Path,null,null));if(backup is null){status.Text="戻せる補正はありません。";return;}await Task.Run(()=>ArtworkRotationWriter.Restore(page.Path,null,backup,null));await ShowPreview();status.Text="前の画像に戻しました。";});}
    private async Task Save(){
        if(busy||pages.Count==0)return;
        var batch=pages.Select(p=>new ScannedArtwork(p.Path,p.Name)).ToArray();
        await Work(async()=>{status.Text="アルバムへ保存しています…";await persist(batch,new Progress<string>(message=>status.Text=message));SavedCount=batch.Length;saved=true;});
        if(saved)DialogResult=true;
    }
}
