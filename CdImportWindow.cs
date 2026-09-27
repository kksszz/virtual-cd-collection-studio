using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;

namespace ZipMp3Player;

internal sealed class CdImportWindow:Window
{
    private readonly ComboBox drives=new(){MinWidth=100},format=new(){ItemsSource=new[]{"FLAC","MP3"},SelectedIndex=0,Width=100},bitrate=new(){ItemsSource=new[]{320,256,192},SelectedIndex=0,Width=85};
    private readonly TextBox album=new(){Width=240},artist=new(){Width=220},year=new(){Width=60},discNumber=new(){Text="1",Width=45},discCount=new(){Text="1",Width=45},destination=new(){MinWidth=350};
    private readonly ComboBox candidates=new(){DisplayMemberPath=nameof(CueMetadata.Description),Width=680};
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
    private sealed record Settings(string Format,int Bitrate,string Destination);
    internal CdImportWindow(string initialFolder)
    {
        Title="CDから取り込む — FLAC / MP3";Width=1220;Height=760;MinWidth=900;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
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
        try{if(File.Exists(settingsPath)&&JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) is {} s){format.SelectedItem=s.Format;bitrate.SelectedItem=s.Bitrate;destination.Text=s.Destination;}}catch{}
        if(format.SelectedIndex<0)format.SelectedIndex=0;if(bitrate.SelectedIndex<0)bitrate.SelectedIndex=0;
        drives.ItemsSource=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.CDRom).Select(d=>d.Name).ToList();drives.SelectedIndex=0;
        var root=new DockPanel{Margin=new(14)};Content=root;
        DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);
        controls.Children.Add(new TextBlock{Text="CDから音楽を取り込む",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,10)});
        var row=Row();row.Children.Add(new TextBlock{Text="CDドライブ",VerticalAlignment=VerticalAlignment.Center});row.Children.Add(drives);
        AddButton(row,"CDを読み込む",ReadDisc);AddButton(row,"曲情報を取得（MusicBrainz）",Lookup);
        var info=Row();info.Children.Add(new TextBlock{Text="候補",VerticalAlignment=VerticalAlignment.Center});info.Children.Add(candidates);AddButton(info,"候補を反映",ApplyCandidate);
        var names=Row();Field(names,"アルバム",album);Field(names,"アルバムアーティスト",artist);Field(names,"年",year);
        var options=Row();Field(options,"Disc",discNumber);Field(options,"/",discCount);Field(options,"保存形式",format);Field(options,"MP3 kbps",bitrate);
        bitrate.IsEnabled=(string)format.SelectedItem=="MP3";format.SelectionChanged+=(_,_)=>bitrate.IsEnabled=(string?)format.SelectedItem=="MP3";
        var folders=Row();Field(folders,"保存先",destination);AddButton(folders,"参照",()=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="CD取り込み先"};if(picker.ShowDialog(this)==true)destination.Text=picker.FolderName;});
        controls.Children.Add(new Border{Background=(Brush)new BrushConverter().ConvertFromString("#1D2B36")!,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#36536A")!,BorderThickness=new(1),CornerRadius=new(4),Padding=new(10),Margin=new(0,8,0,5),Child=new TextBlock{Text="曲名・アーティスト・ジャンル・作曲者・コメントは表で編集できます。アルバム・年・Discは上部と連動します。\n1枚組はアルバム直下に保存。複数枚組のみDisc1・Disc2に分けます。情報取得時はCD構成のみを送信します。",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)new BrushConverter().ConvertFromString("#A9D8B5")!}});
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);footer.Children.Add(progressCaption);footer.Children.Add(progressBar);footer.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(start);buttons.Children.Add(cancel);footer.Children.Add(buttons);
        tracks.Columns.Add(new DataGridCheckBoxColumn{Header="取込",Binding=new Binding(nameof(CdImportTrack.Selected))});
        foreach(var entry in new[]{("曲",nameof(CdImportTrack.Number)),("曲名",nameof(CdImportTrack.Title)),("アーティスト",nameof(CdImportTrack.Artist)),("時間",nameof(CdImportTrack.Duration))})tracks.Columns.Add(new DataGridTextColumn{Header=entry.Item1,Binding=new Binding(entry.Item2),IsReadOnly=entry.Item2 is nameof(CdImportTrack.Number) or nameof(CdImportTrack.Duration),Width=entry.Item2 is nameof(CdImportTrack.Title) or nameof(CdImportTrack.Artist)?new DataGridLength(1,DataGridLengthUnitType.Star):DataGridLength.Auto});
        tracks.Columns[0].MinWidth=45;tracks.Columns[1].MinWidth=45;tracks.Columns[2].MinWidth=280;tracks.Columns[3].MinWidth=200;tracks.Columns[4].MinWidth=70;
        foreach(var entry in new[]{("アルバム",nameof(CdImportTrack.Album),180),("アルバムアーティスト",nameof(CdImportTrack.AlbumArtist),170),("年",nameof(CdImportTrack.Year),65),("ジャンル",nameof(CdImportTrack.Genre),130),("作曲者",nameof(CdImportTrack.Composer),150),("コメント",nameof(CdImportTrack.Comment),220),("曲総数",nameof(CdImportTrack.TrackCount),75),("Disc",nameof(CdImportTrack.DiscNumber),65),("Disc総数",nameof(CdImportTrack.DiscCount),85)})
            tracks.Columns.Add(new DataGridTextColumn{Header=entry.Item1,Binding=new Binding(entry.Item2){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=entry.Item3,IsReadOnly=entry.Item2 is not (nameof(CdImportTrack.Genre) or nameof(CdImportTrack.Composer) or nameof(CdImportTrack.Comment))});
        foreach(var input in new[]{album,artist,year,discNumber,discCount})input.TextChanged+=(_,_)=>SyncCommonTags();
        root.Children.Add(tracks);start.Click+=(_,_)=>Import();cancel.Click+=(_,_)=>{if(busy){cancellation?.Cancel();status.Text="中断を要求しました。現在の読み取り・保存処理の終了を待っています…";}else Close();};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;cancellation?.Cancel();status.Text="処理の終了を待っています。強制終了しないでください。";}};
    }
    private WrapPanel Row(){var row=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new(0,3,0,3)};controls.Children.Add(row);return row;}
    private static void Field(Panel row,string label,Control input){row.Children.Add(new TextBlock{Text=label,Margin=new(6,0,5,0),VerticalAlignment=VerticalAlignment.Center});row.Children.Add(input);}
    private static void AddButton(Panel row,string label,Action action){var button=new Button{Content=label,Margin=new(5,0,0,0),Padding=new(8,4,8,4)};button.Click+=(_,_)=>action();row.Children.Add(button);}
    private async void Run(Func<CancellationToken,Task> operation){if(busy)return;busy=true;progressBar.Value=0;progressBar.IsIndeterminate=false;progressCaption.Text="処理中";controls.IsEnabled=false;tracks.IsEnabled=false;start.IsEnabled=false;cancel.Content="中断";cancellation=new();try{await operation(cancellation.Token);}catch(OperationCanceledException){status.Text="中断しました。";progressCaption.Text="中断";}catch(Exception ex){status.Text=ex.Message;progressCaption.Text="処理を中断しました";}finally{busy=false;progressBar.IsIndeterminate=false;controls.IsEnabled=true;tracks.IsEnabled=true;start.IsEnabled=disc is not null;cancel.Content="閉じる";cancellation.Dispose();cancellation=null;}}
    private void SyncCommonTags(){
        if(tracks.ItemsSource is not List<CdImportTrack> rows)return;
        foreach(var row in rows){row.Album=album.Text.Trim();row.AlbumArtist=artist.Text.Trim();row.Year=uint.TryParse(year.Text,out uint y)?y:0;row.DiscNumber=int.TryParse(discNumber.Text,out int n)?n:0;row.DiscCount=int.TryParse(discCount.Text,out int c)?c:0;row.TrackCount=disc?.Tracks.Count??rows.Count;}
        tracks.Items.Refresh();
    }
    internal void UpdateImportProgress(CdImportProgress value){
        progressBar.IsIndeterminate=value.Percent is null;
        if(value.Percent is double percent)progressBar.Value=Math.Clamp(percent,0,100);
        progressCaption.Text=$"選択 {value.TotalTracks}曲中 {value.CompletedTracks}曲完了"+(value.Track>0?$" ／ 曲 {value.Track}" : "")+(value.Percent is double p?$" — {p:F0}%":" — 処理中");status.Text=value.Message;
    }
    private void ReadDisc(){if(drives.SelectedItem is not string drive)return;disc=null;start.IsEnabled=false;candidates.ItemsSource=null;tracks.ItemsSource=null;Run(async token=>{
        status.Text="CDの曲構成を読み込んでいます…";
        var read=await Task.Run(()=>{using var cd=new CdAudioSource(drive);return cd.Disc;},token);token.ThrowIfCancellationRequested();disc=read;loadedDrive=drive;
        tracks.ItemsSource=read.Tracks.Select((t,i)=>new CdImportTrack{Number=t.Number,Title=t.Title,Duration=TimeSpan.FromSeconds(((i+1<read.Tracks.Count?read.Tracks[i+1].Frame:read.Frames)-t.Frame)/75.0).ToString(@"mm\:ss")}).ToList();
        SyncCommonTags();progressCaption.Text="取り込み待機中";
        status.Text=$"{read.Tracks.Count}曲を検出しました。曲情報を取得するか手入力してください。";
    });}
    private void Lookup(){if(disc is null)return;Run(async token=>{status.Text="MusicBrainzへ照会しています…";var found=await CueMetadataLookup.SearchAsync(disc,token);candidates.ItemsSource=found;candidates.SelectedIndex=found.Count>0?0:-1;status.Text=found.Count>0?$"{found.Count}候補。選択して「候補を反映」を押し、曲名を確認してください。":"該当情報がありません。曲名などを手入力して取り込めます。";});}
    private void ApplyCandidate(){if(candidates.SelectedItem is not CueMetadata m||tracks.ItemsSource is not List<CdImportTrack> rows)return;album.Text=m.Album;year.Text=m.Year;discNumber.Text=m.DiscNumber.ToString();discCount.Text=m.DiscCount.ToString();artist.Text=string.IsNullOrWhiteSpace(m.AlbumArtist)?m.Tracks.FirstOrDefault()?.Artist??"":m.AlbumArtist;for(int i=0;i<rows.Count;i++){rows[i].Title=m.Tracks[i].Title;rows[i].Artist=m.Tracks[i].Artist;}tracks.Items.Refresh();}
    private void Import(){
        tracks.CommitEdit(DataGridEditingUnit.Cell,true);tracks.CommitEdit(DataGridEditingUnit.Row,true);
        if(disc is null||tracks.ItemsSource is not List<CdImportTrack> rows)return;
        if(string.IsNullOrWhiteSpace(album.Text)||string.IsNullOrWhiteSpace(artist.Text)||string.IsNullOrWhiteSpace(destination.Text)||!int.TryParse(discNumber.Text,out int number)||!int.TryParse(discCount.Text,out int count)||number<1||count<number||count>99||(!string.IsNullOrWhiteSpace(year.Text)&&(!uint.TryParse(year.Text,out _)||uint.Parse(year.Text)>9999))){status.Text="アルバム名・アーティスト・保存先・Disc番号・年を確認してください。";return;}
        var selected=rows.Where(r=>r.Selected).Select(r=>new CdImportTrack{Number=r.Number,Title=r.Title.Trim(),Artist=string.IsNullOrWhiteSpace(r.Artist)?artist.Text.Trim():r.Artist.Trim(),Genre=r.Genre.Trim(),Composer=r.Composer.Trim(),Comment=r.Comment.Trim()}).ToList();
        if(selected.Count==0||selected.Any(r=>r.Title.Length==0)){status.Text="曲を選択し、曲名を入力してください。";return;}
        var plan=new CdImportPlan(loadedDrive,disc,destination.Text.Trim(),album.Text.Trim(),artist.Text.Trim(),uint.TryParse(year.Text,out uint y)?y:0,number,count,(string)format.SelectedItem,(int)bitrate.SelectedItem,selected);
        Run(async token=>{
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(new Settings(plan.Format,plan.Bitrate,plan.Destination)));
            var progress=new Progress<string>(s=>status.Text=s);
            var detail=new Progress<CdImportProgress>(UpdateImportProgress);
            ImportedAlbum=await Task.Run(()=>CdImportService.Import(plan,progress,token,detail),token);
            progressBar.IsIndeterminate=false;progressBar.Value=100;progressCaption.Text=$"選択 {selected.Count}曲すべて完了";
            status.Text="取り込み完了。閉じるとライブラリへ登録します。\n"+ImportedAlbum;
        });
    }
}
