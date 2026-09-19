using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Text.Json;

namespace ZipMp3Player;
public partial class MainWindow
{
    private async void MobileSync_Click(object sender,RoutedEventArgs e)
    {
        if(_dataOperations.ActiveCount>0){StatusText.Text="データ処理の完了後に同期してください。";return;}
        var window=new Window{Title="モバイル同期 — 音楽・画像・3D",Owner=this,Width=1060,Height=740,MinWidth=700,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=System.Windows.Media.Brushes.White,Foreground=System.Windows.Media.Brushes.Black};
        MobileSyncAppearance.Apply(window);
        var panel=new DockPanel{Margin=new Thickness(16)};window.Content=panel;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var titleRow=new DockPanel{Margin=new Thickness(0,0,0,6)};header.Children.Add(titleRow);
        var title=new TextBlock{Text="モバイルへ同期",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,16,0)};DockPanel.SetDock(title,Dock.Left);titleRow.Children.Add(title);
        titleRow.Children.Add(new TextBlock{Text="音楽・画像・3Dを転送 · 既存ファイルは削除しません",TextWrapping=TextWrapping.Wrap,FontSize=12,VerticalAlignment=VerticalAlignment.Center});
        var modeRow=new DockPanel{Margin=new Thickness(0,0,0,6)};header.Children.Add(modeRow);
        var mode=new ComboBox{Width=300,ItemsSource=new[]{"Wi-Fi同期（推奨・USBデバッグ不要）","SDカード／フォルダーに出力","ADB転送（開発用）"},SelectedIndex=0,Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(mode,Dock.Left);modeRow.Children.Add(mode);
        var settingsFile=Path.Combine(DataDirectory,"mobile-sync.json");var saved=new Dictionary<string,string>();try{if(File.Exists(settingsFile))saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(settingsFile))??saved;}catch{}
        var adbRow=new DockPanel();header.Children.Add(adbRow);var browse=new Button{Content="ADBを選択…",Padding=new Thickness(8,4,8,4)};DockPanel.SetDock(browse,Dock.Right);adbRow.Children.Add(browse);
        var adb=new TextBox{Text=saved.GetValueOrDefault("adb",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Android","Sdk","platform-tools","adb.exe")),Margin=new Thickness(0,4,8,4)};adbRow.Children.Add(adb);
        browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Android Debug Bridge|adb.exe",FileName="adb.exe"};if(picker.ShowDialog(window)==true)adb.Text=picker.FileName;};
        var connection=new StackPanel{Orientation=Orientation.Horizontal};header.Children.Add(connection);
        var devices=new ComboBox{Width=370,Margin=new Thickness(0,4,8,4)};connection.Children.Add(devices);var discover=new Button{Content="接続端末を更新",Padding=new Thickness(8,4,8,4)};connection.Children.Add(discover);
        var destinationHint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,VerticalAlignment=VerticalAlignment.Center};modeRow.Children.Add(destinationHint);
        var destinationRow=new DockPanel{Margin=new Thickness(0,0,0,6)};header.Children.Add(destinationRow);
        var folderButton=new Button{Content="出力先を選択…",Visibility=Visibility.Collapsed,Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(folderButton,Dock.Right);destinationRow.Children.Add(folderButton);
        var destination=new TextBox{Text=saved.GetValueOrDefault("destination","")};destinationRow.Children.Add(destination);
        folderButton.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog();if(picker.ShowDialog(window)==true)destination.Text=picker.FolderName;};
        void ModeChanged(){bool debug=mode.SelectedIndex==2;adbRow.Visibility=connection.Visibility=debug?Visibility.Visible:Visibility.Collapsed;destinationRow.Visibility=mode.SelectedIndex==0?Visibility.Collapsed:Visibility.Visible;folderButton.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;destination.Text=debug?saved.GetValueOrDefault("destination",""):"";destinationHint.Text=mode.SelectedIndex==0?"保存先はAndroidで選択 · 転送準備後に接続QRを表示":debug?"保存先：端末のSDカード内フォルダー":"保存先：PCに接続したSDカード／フォルダー";}
        mode.SelectionChanged+=(_,_)=>ModeChanged();ModeChanged();
        var searchRow=new DockPanel{Margin=new Thickness(0,0,0,8)};header.Children.Add(searchRow);
        var searchLabel=new TextBlock{Text="検索",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};DockPanel.SetDock(searchLabel,Dock.Left);searchRow.Children.Add(searchLabel);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);searchRow.Children.Add(actions);var all=new Button{Content="表示中を全選択",Margin=new Thickness(8,0,0,0)};var clear=new Button{Content="選択解除",Margin=new Thickness(8,0,0,0)};actions.Children.Add(all);actions.Children.Add(clear);
        var search=new TextBox{ToolTip="アルバム名／アーティストで検索"};System.Windows.Automation.AutomationProperties.SetName(search,"アルバム名／アーティストで検索");searchRow.Children.Add(search);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        var selectionSummary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),FontWeight=FontWeights.SemiBold};footer.Children.Add(selectionSummary);
        footer.Children.Add(new TextBlock{Text="容量は選択元の合計です。生成する3Dは別途追加され、転送済みデータの再利用で実際の通信量は減る場合があります。",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,4,0,0)});
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};footer.Children.Add(status);
        var send=new Button{Content="選択したアルバムを同期",Background=System.Windows.Media.Brushes.Teal,Foreground=System.Windows.Media.Brushes.White,FontWeight=FontWeights.SemiBold,Padding=new Thickness(12,10,12,10)};footer.Children.Add(send);
        var list=MobileSyncAppearance.CreateTransferTable();list.ToolTip="行をクリックして選択・解除できます。";panel.Children.Add(list);
        var generatedCases=new HashSet<string>();try{using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory,"MobileSync","vcd-sync.json")));foreach(var entry in manifest.RootElement.GetProperty("albums").EnumerateObject())if(entry.Value.TryGetProperty("glb",out var glb)&&File.Exists(Path.Combine(DataDirectory,"MobileSync",glb.GetString()??"")))generatedCases.Add(entry.Name);}catch{}
        var candidates=GetAlbumBrowserSourceAlbums().Select(a=>new SyncChoice(a,generatedCases.Contains(MobileSync.Hash(Path.GetFullPath(a.Album.Path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))).ToArray();list.ItemsSource=candidates;
        void UpdateSelectionSummary(){
            var selected=list.SelectedItems.Cast<SyncChoice>().ToArray();
            int pending=selected.Count(c=>!c.SizeChecked),failed=selected.Count(c=>c.SizeChecked&&c.StoredBytes is null);
            long bytes=selected.Sum(c=>c.StoredBytes??0);
            selectionSummary.Text=$"{selected.Length} / {candidates.Length} アルバム選択  ·  {selected.Sum(c=>(long)c.Tracks):N0} 曲  ·  {(pending+failed>0?"確認済み容量":"合計容量")} {SyncChoice.FormatBytes(bytes)}"
                +(pending>0?$"  ·  確認中 {pending} 件":"")+(failed>0?$"  ·  容量取得不可 {failed} 件":"");
        }
        UpdateSelectionSummary();
        var detailCancellation=new CancellationTokenSource();window.Closed+=(_,_)=>detailCancellation.Cancel();
        _=Task.Run(async()=>{foreach(var choice in candidates){if(detailCancellation.IsCancellationRequested)break;try{
            var summary=LibrarySizeSummary.Calculate(new[]{choice.Album.Album},detailCancellation.Token);
            await Dispatcher.InvokeAsync(()=>{choice.SetSize(summary.Unreadable>0?null:summary.StoredBytes);UpdateSelectionSummary();});
        }catch(OperationCanceledException){break;}catch{await Dispatcher.InvokeAsync(()=>{choice.SetSize(null);UpdateSelectionSummary();});}}});
        var view=CollectionViewSource.GetDefaultView(list.ItemsSource);search.TextChanged+=(_,_)=>{view.Filter=o=>o is SyncChoice c&&c.Label.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase);};
        all.Click+=(_,_)=>list.SelectAll();clear.Click+=(_,_)=>list.UnselectAll();list.SelectionChanged+=(_,_)=>UpdateSelectionSummary();
        async Task<string> Adb(params string[] args){var p=new ProcessStartInfo(adb.Text){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)p.ArgumentList.Add(arg);using var process=Process.Start(p)??throw new IOException("ADBを起動できません");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}if(process.ExitCode!=0)throw new IOException(await error);return await output;}
        discover.Click+=async(_,_)=>{discover.IsEnabled=false;try{var text=await Adb("devices");devices.ItemsSource=text.Split('\n').Select(l=>l.Trim().Split('\t')).Where(v=>v.Length==2&&v[1]=="device").Select(v=>v[0]).ToArray();devices.SelectedIndex=0;status.Text=devices.Items.Count==0?"端末が見つかりません。USB接続と端末の許可画面を確認してください。":"端末を選んで転送できます。";}catch(Exception ex){status.Text=ex.Message;}finally{discover.IsEnabled=true;}};
        devices.SelectionChanged+=async(_,_)=>{if(devices.SelectedItem is not string serial||!string.IsNullOrWhiteSpace(destination.Text))return;try{var paths=await Adb("-s",serial,"shell","ls /storage");var sd=paths.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s=>System.Text.RegularExpressions.Regex.IsMatch(s,"^[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}$"));destination.Text=sd is null?"/sdcard/Music/VirtualCD":"/storage/"+sd+"/Music/VirtualCD";}catch(Exception ex){status.Text=ex.Message;}};
        MobileSyncServer? server=null;
        MobileSyncQrWindow? qrWindow=null;
        var showQr=new Button{Content="接続QRコードを表示",Visibility=Visibility.Collapsed,Margin=new Thickness(0,6,0,0),Padding=new Thickness(12,8,12,8)};footer.Children.Add(showQr);
        showQr.Click+=(_,_)=>{if(server is null)return;qrWindow?.Close();qrWindow=new MobileSyncQrWindow(window,server.Address,server);qrWindow.Show();};
        var address=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,8)};footer.Children.Add(address);
        bool running=false;window.Closing+=(_,args)=>{if(running){args.Cancel=true;status.Text="同期中です。検証が終わるまでこの画面は閉じられません。";}};window.Closed+=(_,_)=>server?.Dispose();
        send.Click+=async(_,_)=>{
            var selected=list.SelectedItems.Cast<SyncChoice>().Select(c=>c.Album).ToArray();string serial=devices.SelectedItem as string??"";int method=mode.SelectedIndex;
            if(selected.Length==0||(method==2&&serial.Length==0)||(method==1&&string.IsNullOrWhiteSpace(destination.Text))){status.Text="アルバムと転送先を選択してください。";return;}
            if(_dataOperations.ActiveCount>0){status.Text="他のデータ処理の完了後に実行してください。";return;}
            if(MessageBox.Show(window,$"{selected.Length}アルバムを同期します。\n方式: {mode.SelectedItem}\n\n音楽・画像・3Dの変更分を転送します。Wi-Fi方式ではPCにも転送用コピーを保持します。続行しますか？","モバイル同期",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            using var operation=_dataOperations.Begin();if(operation is null)return;
            running=true;send.IsEnabled=false;header.IsEnabled=false;list.IsEnabled=false;
            var temporary=Path.Combine(Path.GetTempPath(),"vcd-sync-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
            try{
                Directory.CreateDirectory(DataDirectory);File.WriteAllText(settingsFile,JsonSerializer.Serialize(new Dictionary<string,string>{{"adb",adb.Text},{"destination",destination.Text}}));
                string cache=Path.Combine(DataDirectory,"MobileSync");
                MobileSync.ITarget target=method==2?new MobileSync.AdbTarget(adb.Text,serial,destination.Text):new MobileSync.FolderTarget(method==0?cache:destination.Text);
                var sync=await MobileSync.Open(target);int count=0;
                foreach(var album in selected){
                    if(_dataOperations.CloseRequested)break;
                    status.Text=$"{count+1}/{selected.Length} 3D生成: {album.Title}";
                    if(album.Album.HasCompressedArchiveContent)throw new IOException(album.Title+": 先に無圧縮ZIP.MP3へ変換してください。");
                    await album.EnsureCaseArtworkLoadedAsync(1024,CancellationToken.None);
                    var item=new JewelCaseCoverFlowItem(album.Album.Path,album.Title,album.Artist,album.SourceBadge,album.TrayColorMode,album.CaseFrontThumbnail??album.CoverThumbnail,album.InsideFrontThumbnail,album.BackCoverThumbnail,album.SpineThumbnail,album.RightSpineThumbnail,album.InlayThumbnail,album.DiscThumbnail,false){SpineCard=album.SpineCardThumbnail,SecondDiscImage=album.SecondDiscThumbnail};
                    var glb=Path.Combine(temporary,"case.glb");MobileGlbExporter.Export(item,glb);
                    var progress=new Progress<string>(s=>status.Text=$"{(method==0?"PC内の転送準備（端末への受信はAndroidに表示）":"保存先への同期")} · {count+1}/{selected.Length} {album.Title}\n{s}");
                    await Task.Run(()=>sync.Send(album.Album.Path,album.Title,album.Artist,glb,s=>((IProgress<string>)progress).Report(s)));count++;
                }
                if(method==0){server??=new MobileSyncServer(cache);server.Select(selected.Take(count).Select(a=>MobileSync.Hash(Path.GetFullPath(a.Album.Path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));address.Text=server.Address;address.Visibility=Visibility.Visible;showQr.Visibility=Visibility.Visible;qrWindow?.Close();qrWindow=new MobileSyncQrWindow(window,server.Address,server);qrWindow.Show();status.Text=$"{count}アルバムを転送できます。Androidの設定 → PCから同期 → QRコードを読み取る で接続してください。この画面を開いたままにしてください。家庭内Wi-Fiで使用し、ファイアウォールはプライベートネットワークのみ許可してください。";}
                else status.Text=$"{count}アルバムの同期が完了しました。Androidで保存先を音楽フォルダーとして選択すると、3Dも自動反映されます。";
            }catch(Exception ex){status.Text="同期を中断しました。完了済みアルバムは使用できます。\n"+ex.Message;}
            finally{running=false;send.IsEnabled=true;header.IsEnabled=true;list.IsEnabled=true;var glb=Path.Combine(temporary,"case.glb");if(File.Exists(glb))File.Delete(glb);if(!Directory.EnumerateFileSystemEntries(temporary).Any())Directory.Delete(temporary);}
        };
        window.Show();await Task.CompletedTask;
    }
    private sealed class SyncChoice : System.ComponentModel.INotifyPropertyChanged {
        public AlbumListItem Album{get;}
        public SyncChoice(AlbumListItem album,bool has3d){Album=album;CaseStatus=has3d?"あり":"未生成";}
        public string Label=>Album.Title+" — "+Album.Artist;
        public int Tracks=>Album.Album.Tracks.Count;
        public string Format=>Album.SourceBadge+" · "+string.Join("/",Album.Album.Tracks.Select(t=>t.AudioFormat).Distinct());
        public string Images=>Album.HasImages?$"あり ({Album.ImageCount})":Album.ArtworkSummaryLoaded?"なし":"未確認";
        public string CaseStatus{get;}
        public string Size{get;private set;}="確認中…";
        public long? StoredBytes{get;private set;}
        public bool SizeChecked{get;private set;}
        public static string FormatBytes(long bytes)=>bytes>=1073741824?$"{bytes/1073741824d:F2} GiB":$"{bytes/1048576d:F1} MiB";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public void SetSize(long? bytes){StoredBytes=bytes;SizeChecked=true;Size=bytes is long value?FormatBytes(value):"取得不可";PropertyChanged?.Invoke(this,new(nameof(Size)));}
    }
}
