using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;

namespace ZipMp3Player;

internal sealed partial class CdImportWindow:Window
{
    private readonly ComboBox drives=new(){MinWidth=100},format=new(){ItemsSource=new[]{"FLAC","MP3"},SelectedIndex=0,Width=100},bitrate=new(){ItemsSource=new[]{320,256,192},SelectedIndex=0,Width=85};
    private readonly TextBox album=new(){Width=240},artist=new(){Width=220},year=new(){Width=60},discNumber=new(){Text="1",Width=45},discCount=new(){Text="1",Width=45},destination=new(){MinWidth=350};
    private readonly ComboBox candidates=new(){DisplayMemberPath=nameof(CueMetadata.Description),Width=680};
    private readonly CheckBox autoApply=new(){Content="自動で反映",IsChecked=true,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0),ToolTip="曲情報を取得したとき、最初の候補を自動で反映します。別の候補は選択して「候補を反映」を押してください。"};
    private readonly CheckBox readOnOpen=new(){Content="開くときにCDを読み込む",IsChecked=true,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new(10,0,0,0)};
    private readonly CheckBox accurateRip=new(){Content="AccurateRip照合",IsChecked=true,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,ToolTip="CD構成を送信して音声チェックサムを照合します。未登録・通信失敗でも保存を続けます。非商用利用向けです。"};
    private readonly CheckBox offsetEnabled=new(){Content="補正値を使用",IsChecked=false,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new(18,0,8,0)};
    private readonly TextBox readOffset=new(){Text="0",Width=78,Height=30,VerticalContentAlignment=VerticalAlignment.Center,IsEnabled=false,ToolTip="EAC/AccurateRipと同じ符号の補正値（ステレオサンプル数）。範囲 -5880〜+5880。ドライブ型番の正しい値を確認して入力してください。"};
    private readonly TextBlock offsetDrive=new(){Text="CD再読込後にドライブを表示",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};
    private readonly CheckBox autoOffset=new(){Content="未設定ドライブの補正値を自動取得",IsChecked=true,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0),ToolTip="CD読込時にAccurateRip公式一覧を取得します。メーカー・型番の完全一致のみ設定し、保存済みの値は上書きしません。ドライブ情報は送信しません。"};
    private readonly Button lookupOffset=new(){Content="補正値を取得",Height=30,Padding=new(8,4,8,4),Margin=new(0),ToolTip="公式一覧から現在のドライブの補正値を取得します。保存済み設定を変更する場合は確認します。"};
    private sealed record DriveOffset(bool Enabled,int Samples,string Source="");
    private Dictionary<string,DriveOffset> driveOffsets=new();
    private List<DriveOffsetLookup.Entry>? offsetList;
    private Func<CancellationToken,Task<List<DriveOffsetLookup.Entry>>> offsetFetcher=token=>DriveOffsetLookup.Fetch(token);
    private string loadedDriveIdentity="";
    private bool loadingOffset;
    private readonly Button eject=new(){Content="⏏ CDを取り出す",Margin=new(5,0,0,0),Padding=new(8,4,8,4),ToolTip="選択したドライブのCDを取り出します。試聴中は停止してから取り出します。"};
    private readonly TextBlock bitrateLabel=new(){Text="MP3 kbps",Margin=new(6,0,5,0),VerticalAlignment=VerticalAlignment.Center};
    private readonly Border bitrateUnused=new(){Width=85,Background=new SolidColorBrush(Color.FromRgb(44,49,58)),BorderBrush=new SolidColorBrush(Color.FromRgb(74,82,96)),BorderThickness=new(1),Padding=new(5,3,5,3),ToolTip="FLACではMP3ビットレートは使用しません",Child=new TextBlock{Text="—",Foreground=Brushes.Gray,VerticalAlignment=VerticalAlignment.Center}};
    private readonly DataGrid tracks=new(){AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Margin=new(0,8,0,8)};
    private readonly TextBlock status=new(){Text="音楽CDをセットして「CDを読み込む」を押してください。",TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progressBar=new(){Minimum=0,Maximum=100,Height=16,Margin=new(0,6,0,6)};
    private readonly TextBlock progressCaption=new(){Text="取り込み待機中",Margin=new(0,3,0,0)};
    private readonly StackPanel controls=new();
    private readonly Button start=new(){Content="取り込み開始",Padding=new(16,7,16,7),IsEnabled=false},cancel=new(){Content="閉じる",Padding=new(16,7,16,7)};
    private CueAlbumReader.Disc? disc;
    private string loadedDrive="";
    private bool busy;
    private CancellationTokenSource? cancellation;
    internal string? ImportedAlbum {get;private set;}
    private readonly string settingsPath;
    private sealed record Settings(string Format,int Bitrate,string Destination,bool AutoApply=true,bool ReadOnOpen=true,bool AccurateRip=true,Dictionary<string,DriveOffset>? DriveOffsets=null,bool AutoOffset=true);
    internal CdImportWindow(string initialFolder, Action? beforePreview = null)
    {
        this.beforePreview = beforePreview;
        Title="CDから取り込む — FLAC / MP3";Width=1280;Height=820;MinWidth=1000;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=(Brush)new BrushConverter().ConvertFromString("#15171B")!;Foreground=Brushes.White;
        Resources=(ResourceDictionary)XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="Button"><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="Background" Value="#2C313A"/><Setter Property="BorderBrush" Value="#4A5260"/><Setter Property="Margin" Value="4,2"/></Style>
              <Style TargetType="TextBox"><Setter Property="Foreground" Value="#18202A"/><Setter Property="Background" Value="#F3F4F6"/><Setter Property="CaretBrush" Value="#18202A"/><Setter Property="Padding" Value="4,3"/></Style>
              <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#18202A"/><Setter Property="Background" Value="#F3F4F6"/><Setter Property="Padding" Value="5,3"/></Style>
              <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="#18202A"/><Setter Property="Background" Value="#F3F4F6"/></Style>
              <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#2B313A"/><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="BorderBrush" Value="#4A5260"/><Setter Property="BorderThickness" Value="0,0,1,1"/><Setter Property="Padding" Value="7,5"/><Setter Property="FontWeight" Value="SemiBold"/></Style>
              <Style TargetType="DataGridCell"><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="BorderBrush" Value="#353B45"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#236FA1"/><Setter Property="Foreground" Value="White"/></Trigger></Style.Triggers></Style>
              <Style TargetType="DataGrid"><Setter Property="Background" Value="#1E2127"/><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="RowBackground" Value="#1E2127"/><Setter Property="AlternatingRowBackground" Value="#23272E"/><Setter Property="BorderBrush" Value="#414957"/><Setter Property="HorizontalGridLinesBrush" Value="#353B45"/><Setter Property="VerticalGridLinesBrush" Value="#353B45"/><Setter Property="RowHeaderWidth" Value="0"/></Style>
              <Style TargetType="ProgressBar"><Setter Property="Foreground" Value="#2879B8"/><Setter Property="Background" Value="#2C313A"/><Setter Property="BorderBrush" Value="#4A5260"/></Style>
            </ResourceDictionary>
            """);
        settingsPath=Path.Combine(Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZipMp3Player"),"cd-import.json");
        destination.Text=initialFolder;
        try{if(File.Exists(settingsPath)&&JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) is {} s){format.SelectedItem=s.Format;bitrate.SelectedItem=s.Bitrate;destination.Text=s.Destination;autoApply.IsChecked=s.AutoApply;readOnOpen.IsChecked=s.ReadOnOpen;accurateRip.IsChecked=s.AccurateRip;driveOffsets=s.DriveOffsets??new();autoOffset.IsChecked=s.AutoOffset;}}catch{}
        if(format.SelectedIndex<0)format.SelectedIndex=0;if(bitrate.SelectedIndex<0)bitrate.SelectedIndex=0;
        autoApply.Checked+=(_,_)=>SaveImportSettings();autoApply.Unchecked+=(_,_)=>SaveImportSettings();
        readOnOpen.Checked+=(_,_)=>SaveImportSettings();readOnOpen.Unchecked+=(_,_)=>SaveImportSettings();
        accurateRip.Checked+=(_,_)=>SaveImportSettings();accurateRip.Unchecked+=(_,_)=>SaveImportSettings();
        autoOffset.Checked+=(_,_)=>SaveImportSettings();autoOffset.Unchecked+=(_,_)=>SaveImportSettings();
        lookupOffset.Click+=(_,_)=>Run(token=>ResolveDriveOffset(true,token));
        offsetEnabled.Checked+=(_,_)=>{readOffset.IsEnabled=true;SaveOffsetSettings();};offsetEnabled.Unchecked+=(_,_)=>{readOffset.IsEnabled=false;SaveOffsetSettings();};
        readOffset.TextChanged+=(_,_)=>SaveOffsetSettings();
        drives.ItemsSource=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.CDRom).Select(d=>d.Name).ToList();drives.SelectedIndex=0;
        var root=new DockPanel{Margin=new(14)};Content=root;
        // Keep the track list and bottom actions usable when the window is short.
        var formScroll=new ScrollViewer{Content=controls,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=330};
        DockPanel.SetDock(formScroll,Dock.Top);root.Children.Add(formScroll);
        root.SizeChanged+=(_,_)=>formScroll.MaxHeight=Math.Max(200,root.ActualHeight-370);
        controls.Children.Add(new TextBlock{Text="CDから音楽を取り込む",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        BuildImportForm();
        drives.SelectionChanged+=(_,_)=>{
            if(drives.SelectedItem as string!=loadedDrive){disc=null;loadedDriveIdentity="";tracks.ItemsSource=null;loadingOffset=true;try{offsetEnabled.IsChecked=false;readOffset.Text="0";offsetDrive.Text="CDを再読込してください";}finally{loadingOffset=false;}}
            UpdatePreviewControls();
        };
        UpdateFormatControls();format.SelectionChanged+=(_,_)=>UpdateFormatControls();
        controls.Children.Add(new Border{Background=(Brush)new BrushConverter().ConvertFromString("#1D2B36")!,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#36536A")!,BorderThickness=new(1),CornerRadius=new(4),Padding=new(10),Margin=new(0,8,0,5),Child=new TextBlock{Text="曲名・アーティスト・ジャンル・作曲者・コメントは表で編集できます。アルバム・年・Discは上部と連動します。\n1枚組はアルバム直下に保存。複数枚組のみDisc1・Disc2に分けます。情報取得時はCD構成のみを送信します。",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)new BrushConverter().ConvertFromString("#A9D8B5")!}});
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);AddPreviewControls(footer);footer.Children.Add(progressCaption);footer.Children.Add(progressBar);footer.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(start);buttons.Children.Add(cancel);footer.Children.Add(buttons);
        tracks.Columns.Add(new DataGridCheckBoxColumn{Header="取込",Binding=new Binding(nameof(CdImportTrack.Selected))});
        foreach(var entry in new[]{("曲",nameof(CdImportTrack.Number)),("曲名",nameof(CdImportTrack.Title)),("アーティスト",nameof(CdImportTrack.Artist)),("時間",nameof(CdImportTrack.Duration))})tracks.Columns.Add(new DataGridTextColumn{Header=entry.Item1,Binding=new Binding(entry.Item2),IsReadOnly=entry.Item2 is nameof(CdImportTrack.Number) or nameof(CdImportTrack.Duration),Width=entry.Item2 is nameof(CdImportTrack.Title) or nameof(CdImportTrack.Artist)?new DataGridLength(1,DataGridLengthUnitType.Star):DataGridLength.Auto});
        tracks.Columns[0].MinWidth=45;tracks.Columns[1].MinWidth=45;tracks.Columns[2].MinWidth=280;tracks.Columns[3].MinWidth=200;tracks.Columns[4].MinWidth=70;
        foreach(var entry in new[]{("アルバム",nameof(CdImportTrack.Album),180),("アルバムアーティスト",nameof(CdImportTrack.AlbumArtist),170),("年",nameof(CdImportTrack.Year),65),("ジャンル",nameof(CdImportTrack.Genre),130),("作曲者",nameof(CdImportTrack.Composer),150),("コメント",nameof(CdImportTrack.Comment),220),("曲総数",nameof(CdImportTrack.TrackCount),75),("Disc",nameof(CdImportTrack.DiscNumber),65),("Disc総数",nameof(CdImportTrack.DiscCount),85)})
            tracks.Columns.Add(new DataGridTextColumn{Header=entry.Item1,Binding=new Binding(entry.Item2){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=entry.Item3,IsReadOnly=entry.Item2 is not (nameof(CdImportTrack.Genre) or nameof(CdImportTrack.Composer) or nameof(CdImportTrack.Comment))});
        foreach(var input in new[]{album,artist,year,discNumber,discCount})input.TextChanged+=(_,_)=>SyncCommonTags();
        AddPreviewColumn();
        tracks.Columns.Insert(6,new DataGridTextColumn{Header="AccurateRip結果",Binding=new Binding(nameof(CdImportTrack.Verification)),IsReadOnly=true,Width=300});
        root.Children.Add(tracks);start.Click+=(_,_)=>Import();cancel.Click+=(_,_)=>{if(busy){cancellation?.Cancel();status.Text="中断を要求しました。現在の読み取り・保存処理の終了を待っています…";}else Close();};
        Closing+=PreviewWindowClosing;
        bool opened=false;Loaded+=(_,_)=>{if(opened)return;opened=true;if(readOnOpen.IsChecked==true)ReadDisc();};
    }
    private static void AddButton(Panel row,string label,Action action){var button=new Button{Content=label,Margin=new(5,0,0,0),Padding=new(8,4,8,4)};button.Click+=(_,_)=>action();row.Children.Add(button);}
    private void UpdateFormatControls(){bool mp3=(string?)format.SelectedItem=="MP3";bitrate.IsEnabled=mp3;bitrate.Visibility=mp3?Visibility.Visible:Visibility.Hidden;bitrateUnused.Visibility=mp3?Visibility.Collapsed:Visibility.Visible;bitrateLabel.Foreground=mp3?Brushes.White:Brushes.Gray;}
    private async void Run(Func<CancellationToken,Task> operation){if(busy||previewTransition||previewCloseRequested)return;busy=true;progressBar.Value=0;progressBar.IsIndeterminate=false;progressCaption.Text="処理中";tracks.IsEnabled=false;UpdatePreviewControls();cancel.Content="中断";cancellation=new();try{await StopPreviewCore();await operation(cancellation.Token);}catch(OperationCanceledException){status.Text="中断しました。";progressCaption.Text="中断";}catch(Exception ex){status.Text=ex.Message;progressCaption.Text="処理を中断しました";}finally{busy=false;progressBar.IsIndeterminate=false;tracks.IsEnabled=true;UpdatePreviewControls();cancel.Content="閉じる";cancellation.Dispose();cancellation=null;}}
    private void SyncCommonTags(){
        if(tracks.ItemsSource is not List<CdImportTrack> rows)return;
        foreach(var row in rows){row.Album=album.Text.Trim();row.AlbumArtist=artist.Text.Trim();row.Year=uint.TryParse(year.Text,out uint y)?y:0;row.DiscNumber=int.TryParse(discNumber.Text,out int n)?n:0;row.DiscCount=int.TryParse(discCount.Text,out int c)?c:0;row.TrackCount=disc?.Tracks.Count??rows.Count;}
        tracks.Items.Refresh();
    }
    internal void UpdateImportProgress(CdImportProgress value){
        if(value.Verification is not null&&tracks.ItemsSource is List<CdImportTrack> rows&&rows.FirstOrDefault(r=>r.Number==value.Track) is {} row){row.Verification=value.Verification;tracks.Items.Refresh();}
        progressBar.IsIndeterminate=value.Percent is null;
        if(value.Percent is double percent)progressBar.Value=Math.Clamp(percent,0,100);
        progressCaption.Text=$"選択 {value.TotalTracks}曲中 {value.CompletedTracks}曲完了"+(value.Track>0?$" ／ 曲 {value.Track}" : "")+(value.Percent is double p?$" — {p:F0}%":" — 処理中");status.Text=value.Message;
    }
    private void ReadDisc(){if(busy||previewTransition||previewCloseRequested)return;if(drives.SelectedItem is not string drive){status.Text="CDドライブが見つかりません。接続を確認してください。";return;}disc=null;start.IsEnabled=false;candidates.ItemsSource=null;tracks.ItemsSource=null;Run(async token=>{
        status.Text="CDの曲構成を読み込んでいます…";
        var device=await Task.Run(()=>{using var cd=new CdAudioSource(drive);return (cd.Disc,cd.DriveIdentity);},token);token.ThrowIfCancellationRequested();var read=device.Disc;disc=read;loadedDrive=drive;loadedDriveIdentity=device.DriveIdentity;
        ApplyDriveOffset(loadedDriveIdentity);
        tracks.ItemsSource=read.Tracks.Select((t,i)=>new CdImportTrack{Number=t.Number,Title=t.Title,Duration=TimeSpan.FromSeconds(((i+1<read.Tracks.Count?read.Tracks[i+1].Frame:read.Frames)-t.Frame)/75.0).ToString(@"mm\:ss")}).ToList();
        SyncCommonTags();progressCaption.Text="取り込み待機中";
        string offsetMessage="";
        if(autoOffset.IsChecked==true&&!driveOffsets.ContainsKey(loadedDriveIdentity)&&loadedDriveIdentity.Length>0){await ResolveDriveOffset(false,token);offsetMessage="\n"+status.Text;}
        status.Text=$"{read.Tracks.Count}曲を検出しました。曲情報を取得するか手入力してください。"+offsetMessage;
    });}
    private void ApplyDriveOffset(string identity){
        loadedDriveIdentity=identity;loadingOffset=true;
        try{
            var setting=loadedDriveIdentity.Length>0&&driveOffsets.TryGetValue(loadedDriveIdentity,out var saved)?saved:new DriveOffset(false,0);
            if(Math.Abs((long)setting.Samples)>CdOffsetReader.MaximumOffset)setting=new(false,0);
            readOffset.Text=setting.Samples.ToString();offsetEnabled.IsChecked=setting.Enabled;readOffset.IsEnabled=setting.Enabled;
            offsetDrive.Text=loadedDriveIdentity.Length>0?"ドライブ: "+loadedDriveIdentity:"ドライブ識別不可：補正値は今回のみ使用し、自動復元しません。";
            offsetDrive.ToolTip=offsetDrive.Text+"\n型番・ファームウェア・識別番号で設定を保存します。同じ型番でも機器を変更した場合は補正値を確認してください。"+(setting.Source.Length>0?"\n取得元: "+setting.Source:"\n取得元: 手入力または未設定");
        }finally{loadingOffset=false;}
    }
    private async Task ResolveDriveOffset(bool explicitRequest,CancellationToken token){
        string identity=loadedDriveIdentity;
        if(identity.Length==0){status.Text="CDを読み込み、ドライブを識別してから補正値を取得してください。";return;}
        if(!explicitRequest&&driveOffsets.ContainsKey(identity))return;
        status.Text="AccurateRip公式一覧から補正値を取得しています…";
        try{
            if(explicitRequest||offsetList is null)offsetList=await offsetFetcher(token);
            token.ThrowIfCancellationRequested();
            if(identity!=loadedDriveIdentity)return;
            var result=DriveOffsetLookup.Find(offsetList,identity);
            bool replace=false;
            if(explicitRequest&&result.State=="found"&&driveOffsets.ContainsKey(identity)){
                replace=MessageBox.Show(this,result.Message+"\n\n保存済みの補正設定をこの値に置き換えて有効にしますか？","ドライブ補正値",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
                if(!replace){status.Text="保存済みの補正設定を維持しました。";return;}
            }
            ApplyOffsetLookupResult(identity,result,replace);
        }catch(OperationCanceledException) when(!token.IsCancellationRequested){status.Text="補正値の取得がタイムアウトしました。設定は変更せず、手入力できます。";}
        catch(Exception ex) when(ex is System.Net.Http.HttpRequestException or IOException or InvalidDataException or System.Text.RegularExpressions.RegexMatchTimeoutException){status.Text="補正値を取得できませんでした。設定は変更せず、手入力できます。\n"+ex.Message;}
    }
    private void ApplyOffsetLookupResult(string identity,DriveOffsetLookup.Result result,bool replaceSaved){
        if(identity!=loadedDriveIdentity||identity.Length==0)return;
        if(!replaceSaved&&driveOffsets.ContainsKey(identity)){status.Text="保存済みの補正設定を維持しました。";return;}
        if(result.State=="found"&&result.Match?.Samples is int samples){
            driveOffsets[identity]=new(true,samples,"AccurateRip公式一覧 "+DateTimeOffset.Now.ToString("yyyy-MM-dd")+" / "+result.Match.Name);
            ApplyDriveOffset(identity);SaveImportSettings();
            status.Text="補正値を設定しました。"+result.Message;
        }else status.Text=result.Message;
    }
    private void Lookup(){if(disc is null)return;Run(async token=>{status.Text="MusicBrainzへ照会しています…";var found=await CueMetadataLookup.SearchAsync(disc,token);token.ThrowIfCancellationRequested();SetMetadataCandidates(found);});}
    private void EjectDisc(){
        if(drives.SelectedItem is not string drive)return;
        Run(async token=>{
            status.Text="CDを取り出しています…";
            await Task.Run(()=>{token.ThrowIfCancellationRequested();CdAudioSource.Eject(drive);},token);
            disc=null;loadedDrive="";tracks.ItemsSource=null;candidates.ItemsSource=null;
            progressCaption.Text="取り込み待機中";previewStatus.Text="次のCDを読み込むと試聴できます。";
            status.Text="CDを取り出しました。次のCDをセットして「CDを読み込む」を押してください。";
            if(ImportedAlbum is not null)status.Text+="\n取り込み済みのアルバムは、閉じるとライブラリへ登録します。";
        });
    }
    private void SetMetadataCandidates(List<CueMetadata> found){
        candidates.ItemsSource=found;candidates.SelectedIndex=found.Count>0?0:-1;
        if(found.Count==0){status.Text="該当情報がありません。曲名などを手入力して取り込めます。";return;}
        if(autoApply.IsChecked==true){ApplyCandidate();status.Text=$"{found.Count}候補。最初の候補を自動反映しました。違う場合は候補を選び「候補を反映」を押してください。";}
        else status.Text=$"{found.Count}候補。選択して「候補を反映」を押し、曲名を確認してください。";
    }
    private void SaveImportSettings(){
        try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(new Settings((string)format.SelectedItem,(int)bitrate.SelectedItem,destination.Text.Trim(),autoApply.IsChecked==true,readOnOpen.IsChecked==true,accurateRip.IsChecked==true,driveOffsets,autoOffset.IsChecked==true)));}
        catch(Exception ex){status.Text="CD取り込み設定を保存できませんでした："+ex.Message;}
    }
    private void SaveOffsetSettings(){
        if(loadingOffset)return;
        if(loadedDriveIdentity.Length>0&&int.TryParse(readOffset.Text,out int offset)&&Math.Abs((long)offset)<=CdOffsetReader.MaximumOffset){
            string source=driveOffsets.TryGetValue(loadedDriveIdentity,out var previous)&&previous.Samples==offset?previous.Source:"";
            driveOffsets[loadedDriveIdentity]=new(offsetEnabled.IsChecked==true,offset,source);SaveImportSettings();
        }
    }
    private void ApplyCandidate(){if(candidates.SelectedItem is not CueMetadata m||tracks.ItemsSource is not List<CdImportTrack> rows)return;tracks.CommitEdit(DataGridEditingUnit.Cell,true);tracks.CommitEdit(DataGridEditingUnit.Row,true);album.Text=m.Album;year.Text=m.Year;discNumber.Text=m.DiscNumber.ToString();discCount.Text=m.DiscCount.ToString();artist.Text=string.IsNullOrWhiteSpace(m.AlbumArtist)?m.Tracks.FirstOrDefault()?.Artist??"":m.AlbumArtist;for(int i=0;i<rows.Count;i++){rows[i].Title=m.Tracks[i].Title;rows[i].Artist=m.Tracks[i].Artist;}tracks.Items.Refresh();}
    private void Import(){
        tracks.CommitEdit(DataGridEditingUnit.Cell,true);tracks.CommitEdit(DataGridEditingUnit.Row,true);
        if(disc is null||tracks.ItemsSource is not List<CdImportTrack> rows)return;
        if(string.IsNullOrWhiteSpace(album.Text)||string.IsNullOrWhiteSpace(artist.Text)||string.IsNullOrWhiteSpace(destination.Text)||!int.TryParse(discNumber.Text,out int number)||!int.TryParse(discCount.Text,out int count)||number<1||count<number||count>99||(!string.IsNullOrWhiteSpace(year.Text)&&(!uint.TryParse(year.Text,out _)||uint.Parse(year.Text)>9999))){status.Text="アルバム名・アーティスト・保存先・Disc番号・年を確認してください。";return;}
        var selected=rows.Where(r=>r.Selected).Select(r=>new CdImportTrack{Number=r.Number,Title=r.Title.Trim(),Artist=string.IsNullOrWhiteSpace(r.Artist)?artist.Text.Trim():r.Artist.Trim(),Genre=r.Genre.Trim(),Composer=r.Composer.Trim(),Comment=r.Comment.Trim()}).ToList();
        if(selected.Count==0||selected.Any(r=>r.Title.Length==0)){status.Text="曲を選択し、曲名を入力してください。";return;}
        int offset=0;bool configured=offsetEnabled.IsChecked==true;
        if(configured&&(!int.TryParse(readOffset.Text,out offset)||Math.Abs((long)offset)>CdOffsetReader.MaximumOffset)){status.Text="補正値を -5880〜+5880 の整数で入力してください。";return;}
        foreach(var row in rows.Where(r=>r.Selected))row.Verification="";tracks.Items.Refresh();
        var plan=new CdImportPlan(loadedDrive,disc,destination.Text.Trim(),album.Text.Trim(),artist.Text.Trim(),uint.TryParse(year.Text,out uint y)?y:0,number,count,(string)format.SelectedItem,(int)bitrate.SelectedItem,selected,offset,accurateRip.IsChecked==true,configured,loadedDriveIdentity);
        Run(async token=>{
            SaveOffsetSettings();SaveImportSettings();
            var progress=new Progress<string>(s=>status.Text=s);
            var detail=new Progress<CdImportProgress>(UpdateImportProgress);
            ImportedAlbum=await Task.Run(()=>CdImportService.Import(plan,progress,token,detail),token);
            foreach(var track in selected)rows.First(r=>r.Number==track.Number).Verification=track.Verification;
            tracks.Items.Refresh();
            progressBar.IsIndeterminate=false;progressBar.Value=100;progressCaption.Text=$"選択 {selected.Count}曲すべて完了";
            int matched=selected.Count(t=>t.Verification.StartsWith("一致")),mismatched=selected.Count(t=>t.Verification.StartsWith("不一致"));
            status.Text="取り込み完了。閉じるとライブラリへ登録します。\n"+ImportedAlbum+$"\nAccurateRip：一致 {matched}曲 ／ 不一致 {mismatched}曲 ／ 未確認・OFF {selected.Count-matched-mismatched}曲。詳細は曲一覧とcd-import.log。";
        });
    }
}
