using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

public partial class MainWindow
{
    private DlnaServer? _dlnaServer;
    private Window? _dlnaWindow;
    private System.Windows.Forms.NotifyIcon? _dlnaTray;
    private readonly DispatcherTimer _dlnaScopeTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    private HashSet<string> _dlnaSharedPaths=new(StringComparer.OrdinalIgnoreCase);
    private bool _dlnaStarting;
    private CancellationTokenSource? _dlnaPrepareCancellation;

    private void StopDlna()
    {
        _dlnaServer?.Dispose();_dlnaServer=null;_dlnaSharedPaths.Clear();_dlnaScopeTimer.Stop();
        if(_dlnaTray is not null)_dlnaTray.Text="Virtual CD Collection Studio — DLNA停止";
        DlnaToolbarButton.ToolTip=LocalizationService.Select("DLNA配信：停止中","DLNA sharing: stopped");
    }
    private void DisposeDlna(){StopDlna();_dlnaWindow?.Close();if(_dlnaTray is { } tray){tray.Visible=false;tray.Dispose();tray.ContextMenuStrip?.Dispose();tray.Icon?.Dispose();}_dlnaTray=null;}
    private bool TryHideDlnaToTray()
    {
        try
        {
            if(_dlnaTray is null)
            {
                string assembly=Uri.EscapeDataString(typeof(MainWindow).Assembly.GetName().Name!);
                using var stream=Application.GetResourceStream(new Uri($"pack://application:,,,/{assembly};component/Assets/AppIcon.ico"))!.Stream;
                using var icon=new System.Drawing.Icon(stream);
                var menu=new System.Windows.Forms.ContextMenuStrip();
                void Ui(Action action)=>Dispatcher.BeginInvoke(action);
                menu.Items.Add(LocalizationService.Select("画面を表示","Show window"),null,(_,_)=>Ui(RestoreDlnaFromTray));
                menu.Items.Add(LocalizationService.Select("DLNA配信を停止","Stop DLNA sharing"),null,(_,_)=>Ui(()=>{StopDlna();StatusText.Text="DLNA配信を停止しました。";}));
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add(LocalizationService.Select("アプリを終了","Exit application"),null,(_,_)=>Ui(()=>{_forceClose=true;Close();}));
                _dlnaTray=new(){Icon=(System.Drawing.Icon)icon.Clone(),ContextMenuStrip=menu,Text="Virtual CD Collection Studio — DLNA配信中"};
                _dlnaTray.DoubleClick+=(_,_)=>Ui(RestoreDlnaFromTray);
            }
            _dlnaTray.Text="Virtual CD Collection Studio — DLNA配信中";_dlnaTray.Visible=true;
            _dlnaWindow?.Close();SaveSettings();Hide();return true;
        }
        catch(Exception ex){StatusText.Text="通知領域への常駐に失敗したため、画面を閉じませんでした: "+ex.Message;return false;}
    }
    private void RestoreDlnaFromTray(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();if(_dlnaTray is not null)_dlnaTray.Visible=false;}
    private void Dlna_Click(object sender,RoutedEventArgs e)
    {
        if(_dlnaWindow is not null){_dlnaWindow.Activate();return;}
        CreateDlnaWindow().Show();
    }
    private Window CreateDlnaWindow(bool attachOwner=true)
    {
        var candidates=GetAlbumBrowserSourceAlbums().Select(a=>new DlnaChoice(a){Share=ReferenceEquals(a,AlbumList.SelectedItem)}).OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
        var window=new Window{Title="DLNA配信 — 家庭内LAN",Owner=attachOwner?this:null,Width=900,Height=650,MinWidth=650,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};_dlnaWindow=window;MobileSyncAppearance.Apply(window);
        var panel=new DockPanel{Margin=new(16)};window.Content=panel;
        var header=new StackPanel();var headerScroll=new ScrollViewer{Content=header,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=260};DockPanel.SetDock(headerScroll,Dock.Top);panel.Children.Add(headerScroll);
        header.Children.Add(new TextBlock{Text="DLNAで音楽を配信",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        header.Children.Add(new TextBlock{Text="iPhoneのVLC / mconnectなど、UPnP・DLNA対応アプリやネットワークプレーヤー側からアルバム・曲を選んで再生できます。専用iPhone版・3Dケースの配信ではありません。",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)});
        var networkRow=new DockPanel{Margin=new(0,0,0,8)};header.Children.Add(networkRow);
        var refresh=new Button{Content="接続を更新",Margin=new(8,0,0,0),Padding=new(8,4,8,4)};DockPanel.SetDock(refresh,Dock.Right);networkRow.Children.Add(refresh);
        var networkLabel=new TextBlock{Text="使用するLAN",Width=100,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(networkLabel,Dock.Left);networkRow.Children.Add(networkLabel);
        var networks=new ComboBox{MinHeight=30};networkRow.Children.Add(networks);
        void RefreshNetworks(){try{networks.ItemsSource=DlnaEndpoint.Available();networks.SelectedIndex=0;}catch(Exception ex){StatusText.Text=ex.Message;}}RefreshNetworks();refresh.Click+=(_,_)=>RefreshNetworks();
        var acknowledgment=new CheckBox{Content=new TextBlock{Text="選択したアルバムを、信頼できる家庭内LANに公開する",TextWrapping=TextWrapping.Wrap},Foreground=Brushes.White,Margin=new(0,2,0,6)};header.Children.Add(acknowledgment);
        header.Children.Add(new TextBlock{Text="同じLANの機器から認証なしで曲名・音楽・ジャケットへアクセスできます。公衆Wi-FiやVPNは選ばないでください。ファイアウォールはプライベートネットワークだけ許可してください。配信は毎回手動で開始します。",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,0,0,10)});
        var searchRow=new DockPanel{Margin=new(0,0,0,8)};header.Children.Add(searchRow);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Right);searchRow.Children.Add(buttons);
        var all=new Button{Content="表示中を全選択",Margin=new(8,0,0,0)};var clear=new Button{Content="全解除",Margin=new(8,0,0,0)};buttons.Children.Add(all);buttons.Children.Add(clear);
        var searchLabel=new TextBlock{Text="検索",Width=100,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(searchLabel,Dock.Left);searchRow.Children.Add(searchLabel);
        var search=new TextBox{MinHeight=28,ToolTip="アルバム・アーティストを検索"};searchRow.Children.Add(search);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        var details=new StackPanel();var detailsScroll=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=150};footer.Children.Add(detailsScroll);
        var summary=new TextBlock{Margin=new(0,8,0,4),TextWrapping=TextWrapping.Wrap};details.Children.Add(summary);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8)};details.Children.Add(status);
        details.Children.Add(new TextBlock{Text="バックグラウンド動作は設定の「DLNA配信中は、閉じてもバックグラウンドで動作する」で有効にできます。配信内容の変更は停止→再開で反映します。",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new(0,0,0,8)});
        panel.SizeChanged+=(_,_)=>{headerScroll.MaxHeight=Math.Max(120,panel.ActualHeight*.42);detailsScroll.MaxHeight=Math.Max(80,panel.ActualHeight*.26);};
        var actionRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actionRow);
        var start=new Button{Content="選択したアルバムを配信",Padding=new(12,8,12,8),Margin=new(0,0,8,0)};var stopButton=new Button{Content="配信を停止",Padding=new(12,8,12,8)};actionRow.Children.Add(start);actionRow.Children.Add(stopButton);
        var list=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Extended,ItemsSource=candidates,HeadersVisibility=DataGridHeadersVisibility.Column,
            Background=MobileSyncAppearance.Brush("#1F2329"),Foreground=Brushes.White,RowBackground=MobileSyncAppearance.Brush("#1F2329"),AlternatingRowBackground=MobileSyncAppearance.Brush("#252A32"),BorderBrush=MobileSyncAppearance.Brush("#454C58"),RowHeight=32};
        var columnStyle=new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));columnStyle.Setters.Add(new Setter(Control.BackgroundProperty,MobileSyncAppearance.Brush("#2C313A")));columnStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));columnStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,6,8,6)));list.ColumnHeaderStyle=columnStyle;
        list.Columns.Add(new DataGridCheckBoxColumn{Header="共有",Binding=new Binding(nameof(DlnaChoice.Share)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=50});
        list.Columns.Add(new DataGridTextColumn{Header="アルバム",Binding=new Binding(nameof(DlnaChoice.Title)),Width=new(1,DataGridLengthUnitType.Star),IsReadOnly=true});
        list.Columns.Add(new DataGridTextColumn{Header="アーティスト",Binding=new Binding(nameof(DlnaChoice.Artist)),Width=new(1,DataGridLengthUnitType.Star),IsReadOnly=true});
        list.Columns.Add(new DataGridTextColumn{Header="対応曲",Binding=new Binding(nameof(DlnaChoice.Count)),Width=70,IsReadOnly=true});panel.Children.Add(list);
        var view=CollectionViewSource.GetDefaultView(candidates);search.TextChanged+=(_,_)=>{view.Filter=o=>o is DlnaChoice c&&(c.Title+" "+c.Artist).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase);};
        void Update()
        {
            bool running=_dlnaServer is not null;summary.Text=$"{candidates.Count(c=>c.Share)}アルバムを選択 · 配信可能 {candidates.Where(c=>c.Share).Sum(c=>c.Count)}曲（CUE分割・CD-DA・未対応曲は除外）";
            start.IsEnabled=!running&&!_dlnaStarting&&acknowledgment.IsChecked==true&&networks.SelectedItem is DlnaEndpoint&&candidates.Any(c=>c.Share&&c.Count>0);stopButton.IsEnabled=running||_dlnaStarting;stopButton.Content=_dlnaStarting?"準備をキャンセル":"配信を停止";
            header.IsEnabled=list.IsEnabled=!running&&!_dlnaStarting;
            if(running)status.Text=_dlnaServer!.Status+"\n"+_dlnaServer.Address+"device.xml";
        }
        foreach(var c in candidates)c.PropertyChanged+=(_,_)=>Update();acknowledgment.Checked+=(_,_)=>Update();acknowledgment.Unchecked+=(_,_)=>Update();networks.SelectionChanged+=(_,_)=>Update();
        all.Click+=(_,_)=>{foreach(DlnaChoice c in view)c.Share=c.Count>0;};clear.Click+=(_,_)=>{foreach(var c in candidates)c.Share=false;};
        stopButton.Click+=(_,_)=>{if(_dlnaStarting){_dlnaPrepareCancellation?.Cancel();status.Text="準備の中止を待っています…";return;}StopDlna();status.Text="DLNA配信を停止しました。";Update();};
        start.Click+=async(_,_)=>
        {
            if(_dlnaStarting||_dlnaServer is not null||networks.SelectedItem is not DlnaEndpoint endpoint||acknowledgment.IsChecked!=true)return;
            using var operation=_dataOperations.Begin();if(operation is null)return;
            _dlnaPrepareCancellation=new();var cancellation=_dlnaPrepareCancellation.Token;
            _dlnaStarting=true;Update();status.Text="選択したライブラリを準備しています…";
            try
            {
                var albums=candidates.Where(c=>c.Share).Select(c=>new DlnaAlbum(c.Item.Album,c.Title,c.Artist,c.Item.LoadDlnaCover)).ToArray();
                string idPath=Path.Combine(DataDirectory,"dlna-device-id.txt");Directory.CreateDirectory(DataDirectory);
                string uuid=File.Exists(idPath)?File.ReadAllText(idPath).Trim():"";if(!Guid.TryParseExact(uuid,"D",out _)){uuid=Guid.NewGuid().ToString("D");File.WriteAllText(idPath,uuid);}
                string updatePath=Path.Combine(DataDirectory,"dlna-system-update-id.txt");uint update=0;
                if(File.Exists(updatePath))uint.TryParse(File.ReadAllText(updatePath).Trim(),out update);update=unchecked(update+1);File.WriteAllText(updatePath,update.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _dlnaServer=await Task.Run(()=>new DlnaServer(endpoint,albums,uuid,updateId:update,cancellation:cancellation),cancellation);cancellation.ThrowIfCancellationRequested();_dlnaSharedPaths=albums.Select(a=>a.Album.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _dlnaScopeTimer.Tick-=DlnaScope_Tick;_dlnaScopeTimer.Tick+=DlnaScope_Tick;_dlnaScopeTimer.Start();
                DlnaToolbarButton.ToolTip="DLNA配信中 — クリックで停止・状態確認";
                StatusText.Text=$"DLNA配信を開始しました: {_dlnaServer.AlbumCount}アルバム / {_dlnaServer.TrackCount}曲";
            }
            catch(OperationCanceledException){StopDlna();status.Text="配信の準備を中止しました。";}
            catch(Exception ex){StopDlna();status.Text="配信を開始できませんでした: "+ex.Message;}
            finally{_dlnaStarting=false;_dlnaPrepareCancellation?.Dispose();_dlnaPrepareCancellation=null;Update();}
        };
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>Update();timer.Start();
        window.Closing+=(_,args)=>{if(_dlnaStarting)args.Cancel=true;};window.Closed+=(_,_)=>{timer.Stop();_dlnaWindow=null;};Update();if(_dlnaServer is null)status.Text="配信はOFFです。共有するアルバムとLANを選び、公開のチェックを入れて開始してください。";return window;
    }
    private void DlnaScope_Tick(object? sender,EventArgs e)
    {
        if(_dlnaServer is null)return;
        var available=GetAlbumBrowserSourceAlbums().Select(a=>a.Album.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!_dlnaSharedPaths.IsSubsetOf(available)){StopDlna();StatusText.Text="ライブラリの公開範囲が変わったため、DLNA配信を停止しました。対象を確認して再開してください。";}
    }
    private sealed class DlnaChoice(AlbumListItem item):INotifyPropertyChanged
    {
        internal AlbumListItem Item=>item;public string Title=>item.Title;public string Artist=>item.Artist;public int Count=>item.Album.Tracks.Count(DlnaCatalog.CanShare);
        private bool share;public bool Share{get=>share;set{share=value;PropertyChanged?.Invoke(this,new(nameof(Share)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed partial class AlbumListItem
    {
        internal byte[]? LoadDlnaCover()
        {
            var bitmap=LoadCoverThumbnail(GetDownloadedArtworkDirectory(Album.Path),600).Image;if(bitmap is null)return null;
            double scale=Math.Min(1,160d/Math.Max(bitmap.PixelWidth,bitmap.PixelHeight));var small=new TransformedBitmap(bitmap,new ScaleTransform(scale,scale));small.Freeze();
            var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(small));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
        }
    }
}
