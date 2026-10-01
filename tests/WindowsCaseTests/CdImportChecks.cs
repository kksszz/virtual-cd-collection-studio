using System.IO;
using NAudio.Wave;
using ZipMp3Player;
internal static class CdImportChecks
{
    internal static void Render(string output)
    {
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(Path.GetTempPath(),"vccs-cd-ui-"+Guid.NewGuid().ToString("N")));
        _=new System.Windows.Application();var window=new CdImportWindow("C:\\Music");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        object Field(string name)=>typeof(CdImportWindow).GetField(name,flags)!.GetValue(window)!;
        var correction=(System.Windows.Controls.TextBox)Field("readOffset");var correctionEnabled=(System.Windows.Controls.CheckBox)Field("offsetEnabled");
        var accurate=(System.Windows.Controls.CheckBox)Field("accurateRip");
        if(accurate.IsChecked!=true||correctionEnabled.IsChecked==true||correction.IsEnabled)throw new Exception("Verification defaults");
        void Drive(string identity)=>typeof(CdImportWindow).GetMethod("ApplyDriveOffset",flags)!.Invoke(window,[identity]);
        Drive("Test Drive A");correctionEnabled.IsChecked=true;correction.Text="6";
        Drive("Test Drive B");if(correctionEnabled.IsChecked==true||correction.Text!="0")throw new Exception("Correction leaked across drives");
        correctionEnabled.IsChecked=true;correction.Text="-667";
        Drive("Test Drive A");if(correctionEnabled.IsChecked!=true||correction.Text!="6")throw new Exception("Drive correction not restored");
        var reloaded=new CdImportWindow("C:\\Music");
        typeof(CdImportWindow).GetMethod("ApplyDriveOffset",flags)!.Invoke(reloaded,["Test Drive B"]);
        if(((System.Windows.Controls.TextBox)typeof(CdImportWindow).GetField("readOffset",flags)!.GetValue(reloaded)!).Text!="-667")throw new Exception("Persisted correction not restored");
        Drive("");if(correctionEnabled.IsChecked==true)throw new Exception("Unknown device reused correction");
        Console.WriteLine("PASS verification defaults; drive-specific offset persistence; unknown device reset");
        var autoOffset=(System.Windows.Controls.CheckBox)Field("autoOffset");
        if(autoOffset.IsChecked!=true)throw new Exception("Automatic drive offset default");
        int fetches=0;
        var officialEntries=new List<DriveOffsetLookup.Entry>{new("hp HLDS - DVDROM DUD1N",6,6,100)};
        typeof(CdImportWindow).GetField("offsetFetcher",flags)!.SetValue(window,new Func<CancellationToken,Task<List<DriveOffsetLookup.Entry>>>(_=>{fetches++;return Task.FromResult(officialEntries);}));
        void Resolve(CancellationToken token=default)=>((Task)typeof(CdImportWindow).GetMethod("ResolveDriveOffset",flags)!.Invoke(window,[false,token])!).GetAwaiter().GetResult();
        const string identified="hp HLDS | DVDROM DUD1N | MDM2 | ";Drive(identified);Resolve();
        if(correction.Text!="6"||correctionEnabled.IsChecked!=true||fetches!=1)throw new Exception("Automatic official offset not applied");
        Resolve();if(fetches!=1)throw new Exception("Saved offset needlessly fetched");
        var restored=new CdImportWindow("C:\\Music");typeof(CdImportWindow).GetMethod("ApplyDriveOffset",flags)!.Invoke(restored,[identified]);
        if(((System.Windows.Controls.TextBox)typeof(CdImportWindow).GetField("readOffset",flags)!.GetValue(restored)!).Text!="6"||!((System.Windows.Controls.TextBlock)typeof(CdImportWindow).GetField("offsetDrive",flags)!.GetValue(restored)!).ToolTip.ToString()!.Contains("AccurateRip公式一覧"))throw new Exception("Official source/value not persisted");
        correction.Text="11";correctionEnabled.IsChecked=false;
        typeof(CdImportWindow).GetMethod("ApplyOffsetLookupResult",flags)!.Invoke(window,[identified,DriveOffsetLookup.Find(officialEntries,identified),false]);
        if(correction.Text!="11"||correctionEnabled.IsChecked==true)throw new Exception("Manual/disabled saved correction overwritten");
        typeof(CdImportWindow).GetMethod("ApplyOffsetLookupResult",flags)!.Invoke(window,[identified,DriveOffsetLookup.Find(officialEntries,identified),true]);
        if(correction.Text!="6"||correctionEnabled.IsChecked!=true)throw new Exception("Explicit approved replacement not applied");
        Drive("Other | Unlisted | 1 | ");Resolve();if(correctionEnabled.IsChecked==true||correction.Text!="0")throw new Exception("Unknown drive inherited offset");
        Drive("Other | Offline | 1 | ");typeof(CdImportWindow).GetField("offsetList",flags)!.SetValue(window,null);
        typeof(CdImportWindow).GetField("offsetFetcher",flags)!.SetValue(window,new Func<CancellationToken,Task<List<DriveOffsetLookup.Entry>>>(_=>Task.FromException<List<DriveOffsetLookup.Entry>>(new System.Net.Http.HttpRequestException("test offline"))));
        Resolve();if(correctionEnabled.IsChecked==true||correction.Text!="0"||!((System.Windows.Controls.TextBlock)Field("status")).Text.Contains("取得できません"))throw new Exception("Offline lookup damaged setting");
        typeof(CdImportWindow).GetField("offsetFetcher",flags)!.SetValue(window,new Func<CancellationToken,Task<List<DriveOffsetLookup.Entry>>>(_=>Task.FromException<List<DriveOffsetLookup.Entry>>(new InvalidDataException("bad table"))));
        Resolve();if(!((System.Windows.Controls.TextBlock)Field("status")).Text.Contains("取得できません"))throw new Exception("Malformed list escaped UI handling");
        typeof(CdImportWindow).GetMethod("ApplyOffsetLookupResult",flags)!.Invoke(window,[identified,DriveOffsetLookup.Find(officialEntries,identified),true]);
        if(correction.Text!="0")throw new Exception("Stale response updated different drive");
        autoOffset.IsChecked=false;var manualWindow=new CdImportWindow("C:\\Music");
        if(((System.Windows.Controls.CheckBox)typeof(CdImportWindow).GetField("autoOffset",flags)!.GetValue(manualWindow)!).IsChecked==true)throw new Exception("Manual lookup preference not restored");
        autoOffset.IsChecked=true;Drive(identified);
        Console.WriteLine("PASS automatic offset; saved/manual settings protected; approved replacement; unknown/offline/malformed/stale response; auto preference persistence");
        ((System.Windows.Controls.TextBox)Field("album")).Text="Dream Horizon";((System.Windows.Controls.TextBox)Field("artist")).Text="G-GRIP";
        ((System.Windows.Controls.DataGrid)Field("tracks")).ItemsSource=Enumerable.Range(1,10).Select(i=>new CdImportTrack{Number=i,Title="日本語の曲名を編集できます "+i,Artist="G-GRIP",Duration="04:00"}).ToList();
        var format=(System.Windows.Controls.ComboBox)Field("format");var bitrate=(System.Windows.Controls.ComboBox)Field("bitrate");
        if(bitrate.IsEnabled)throw new Exception("FLAC bitrate must be disabled");format.SelectedItem="MP3";if(!bitrate.IsEnabled)throw new Exception("MP3 bitrate must be enabled");
        var grid=(System.Windows.Controls.DataGrid)Field("tracks");
        var previewButton=(System.Windows.Controls.Button)Field("previewSelected");
        if(previewButton.IsEnabled)throw new Exception("Preview enabled before CD load");
        typeof(CdImportWindow).GetField("disc",flags)!.SetValue(window,new CueAlbumReader.Disc("","","","",1,180000,Enumerable.Range(1,10).Select(i=>new CueAlbumReader.CueTrack(i,(i-1)*18000,"Track "+i,"")).ToList(),""));
        grid.SelectedIndex=0;
        if(!previewButton.IsEnabled||!grid.Columns[2].IsReadOnly)throw new Exception("Loaded track preview control");
        typeof(CdImportWindow).GetField("busy",flags)!.SetValue(window,true);
        typeof(CdImportWindow).GetMethod("UpdatePreviewControls",flags)!.Invoke(window,null);
        if(previewButton.IsEnabled)throw new Exception("Preview enabled during import");
        typeof(CdImportWindow).GetField("busy",flags)!.SetValue(window,false);
        typeof(CdImportWindow).GetMethod("UpdatePreviewControls",flags)!.Invoke(window,null);
        ((System.Windows.Controls.TextBox)Field("year")).Text="1990";
        if(grid.Columns.Count!=16||((List<CdImportTrack>)grid.ItemsSource)[0].Album!="Dream Horizon")throw new Exception("Tag columns/common sync");
        var bar=(System.Windows.Controls.ProgressBar)Field("progressBar");
        window.UpdateImportProgress(new CdImportProgress(2,1,10,null,"エンコード中"));if(!bar.IsIndeterminate)throw new Exception("Encoding progress");
        window.UpdateImportProgress(new CdImportProgress(2,1,10,42,"曲 2 を読み取り中…"));if(bar.IsIndeterminate||bar.Value!=42)throw new Exception("Read progress");
        window.UpdateImportProgress(new CdImportProgress(1,1,10,100,"検証結果のテスト表示","一致（v2・信頼度 12）"));
        if(((List<CdImportTrack>)grid.ItemsSource)[0].Verification!="一致（v2・信頼度 12）")throw new Exception("Per-track verification progress missing");
        format.SelectedItem="FLAC";
        var root=(System.Windows.Controls.DockPanel)window.Content;root.Background=window.Background;
        root.Measure(new System.Windows.Size(1192,760));root.Arrange(new System.Windows.Rect(0,0,1192,760));root.UpdateLayout();
        CheckAlignment();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1192,760,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(root);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(output))png.Save(file);
        root.Measure(new System.Windows.Size(984,660));root.Arrange(new System.Windows.Rect(0,0,984,660));root.UpdateLayout();
        CheckAlignment();
        var narrow=new System.Windows.Media.Imaging.RenderTargetBitmap(984,660,96,96,System.Windows.Media.PixelFormats.Pbgra32);narrow.Render(root);
        var narrowPng=new System.Windows.Media.Imaging.PngBitmapEncoder();narrowPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(narrow));using(var file=File.Create(Path.Combine(Path.GetDirectoryName(output)!,Path.GetFileNameWithoutExtension(output)+"-narrow.png")))narrowPng.Save(file);
        Console.WriteLine("PASS CD import UI: format toggle; rendered "+output);
        void CheckAlignment(){
            System.Windows.Rect Bounds(string name){var element=(System.Windows.FrameworkElement)Field(name);return element.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(0,0,element.ActualWidth,element.ActualHeight));}
            var reference=Bounds("album");
            foreach(var name in new[]{"drives","candidates","artist","format","destination"})if(Math.Abs(Bounds(name).Left-reference.Left)>.5)throw new Exception("Misaligned field: "+name);
            if(Math.Abs(Bounds("artist").Right-reference.Right)>.5)throw new Exception("Metadata field widths differ");
            if(Bounds("readOnOpen").Right>root.ActualWidth||Bounds("autoApply").Right>root.ActualWidth)throw new Exception("Import options clipped");
            foreach(var name in new[]{"autoOffset","lookupOffset","readOffset","offsetDrive"})if(Bounds(name).Right>root.ActualWidth)throw new Exception("Drive correction controls clipped: "+name);
            if(grid.ActualHeight<110)throw new Exception("Track list unusable at small height");
            Console.WriteLine("PASS aligned import labels/fields at width "+root.ActualWidth);
        }
    }
    internal static void Run(string? drive,bool full=false)
    {
        static void Require(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        var toc=new byte[28];toc[2]=1;toc[3]=2;
        void Entry(int i,byte number,int seconds){int p=4+i*8;toc[p+1]=0x10;toc[p+2]=number;toc[p+5]=(byte)(seconds/60);toc[p+6]=(byte)(seconds%60);}
        Entry(0,1,2);Entry(1,2,182);Entry(2,0xaa,362);
        var disc=CdAudioSource.ParseToc(toc);
        Require(disc.Tracks.Count==2&&disc.Tracks[0].Frame==0&&disc.Frames==27000,"TOC MSF to LBA");
        Require(disc.Toc=="1 2 27150 150 13650","MusicBrainz TOC offsets");
        toc[5]|=4;try{CdAudioSource.ParseToc(toc);throw new Exception("Accepted data track");}catch(NotSupportedException){Console.WriteLine("PASS mixed/data CD rejected");}toc[5]=0x11;
        try{CdAudioSource.ParseToc(toc);throw new Exception("Accepted preemphasis");}catch(NotSupportedException){Console.WriteLine("PASS preemphasis rejected");}
        toc[5]=0x10;toc[13]|=4;
        var fullToc=new byte[37];fullToc[1]=35;fullToc[2]=1;fullToc[3]=2;
        fullToc[4]=1;fullToc[7]=0xA1;fullToc[12]=1; // Session 1 ends at audio track 1.
        fullToc[15]=1;fullToc[18]=0xA2;fullToc[23]=3;fullToc[24]=0; // 03:00:00 lead-out.
        fullToc[26]=2;fullToc[29]=0xA1;fullToc[34]=2;
        long audioEnd=CdAudioSource.ParseFirstSessionLeadout(fullToc,toc);
        var enhanced=CdAudioSource.ParseToc(toc,audioEnd);
        Require(enhanced.Tracks.Count==1&&enhanced.Frames==13350&&enhanced.Frames<13500,"Enhanced CD excludes data session and inter-session gap");
        fullToc[12]=2;
        try{CdAudioSource.ParseFirstSessionLeadout(fullToc,toc);throw new Exception("Accepted same-session data");}catch(NotSupportedException){Console.WriteLine("PASS same-session data rejected");}
        toc[13]=0x10;
        Require(CdImportService.SafeName("../CON:").IndexOfAny(Path.GetInvalidFileNameChars())<0&&CdImportService.SafeName("CON")=="_CON","safe output names");
        var folder=Path.Combine(Path.GetTempPath(),"vccs-cd-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string wav=Path.Combine(folder,"test.wav");
        var stage=Path.Combine(folder,"stage");Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(stage,"01 test.flac"),"fixture");
        var single=Path.Combine(folder,"single");CdImportService.PublishSingleDisc(stage,single);
        Require(File.Exists(Path.Combine(single,"01 test.flac"))&&!Directory.Exists(Path.Combine(single,"Disc1"))&&FolderAlbumLayout.IsRoot(single),"single-disc tracks at album root");
        Directory.CreateDirectory(stage);File.WriteAllText(Path.Combine(stage,"01 test.flac"),"replacement");
        try{CdImportService.PublishSingleDisc(stage,single);throw new Exception("Single-disc overwrite allowed");}catch(IOException){Require(File.ReadAllText(Path.Combine(single,"01 test.flac"))=="fixture"&&Directory.Exists(stage),"single-disc existing album preserved");}
        using(var writer=new WaveFileWriter(wav,new WaveFormat(44100,16,2)))for(int i=0;i<44100*2;i++){short sample=(short)(Math.Sin(i*2*Math.PI*440/44100)*16000);var b=BitConverter.GetBytes(sample);writer.Write(b,0,2);writer.Write(b,0,2);}
        foreach(string format in new[]{"FLAC","MP3"})foreach(int rate in format=="FLAC"?new[]{320}:new[]{192,256,320}){
            string output=Path.Combine(folder,$"test-{rate}.{format.ToLowerInvariant()}");
            CdImportService.Encode(wav,output,format,rate,CancellationToken.None);
            var track=new CdImportTrack{Number=1,Title="日本語 / 曲名",Artist="歌手 / Artist",Genre="Pop / Rock",Composer="作曲者 / Composer",Comment="日本語のコメント"};
            var tagPlan=new CdImportPlan("",disc,folder,"複数枚組","Album / Artist",1990,2,2,format,rate,[track]);
            CdImportService.WriteTags(output,tagPlan,track);
            CdImportService.Verify(wav,output,format,CancellationToken.None);
            using var check=TagLib.File.Create(output);Require(check.Tag.Title=="日本語 / 曲名"&&check.Tag.Disc==2,$"{format} {rate}: encode, decode, tags" );
        }
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{CdImportService.Encode(wav,Path.Combine(folder,"cancel.flac"),"FLAC",320,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS cancellation");}}
        if(drive is not null){using var cd=new CdAudioSource(drive);var block=cd.Read(cd.Disc.Tracks[0].Frame,16);Require(block.AsSpan().SequenceEqual(cd.Read(cd.Disc.Tracks[0].Frame,16)),"real CD repeated read matches");Console.WriteLine($"CD: {cd.Disc.Tracks.Count} tracks; {cd.Disc.DiscId}");cd.VerifyDisc();}
        if(full&&drive is not null){
            CueAlbumReader.Disc real;using(var cd=new CdAudioSource(drive))real=cd.Disc;
            var metadata=CueMetadataLookup.SearchAsync(real,CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"MusicBrainz: {metadata.Count} candidates");foreach(var m in metadata.Take(3))Console.WriteLine(m.Description);
            var shortest=real.Tracks.Select((t,i)=>new{Track=t,Length=(i+1<real.Tracks.Count?real.Tracks[i+1].Frame:real.Frames)-t.Frame}).MinBy(t=>t.Length)!.Track;
            foreach(string format in new[]{"FLAC","MP3"}){
                var plan=new CdImportPlan(drive,real,folder,"Test CD "+format,"Test Artist",2026,1,2,format,320,[new CdImportTrack{Number=shortest.Number,Title="テスト曲",Artist="Test Artist"}]);
                string result=CdImportService.Import(plan,new Progress<string>(Console.WriteLine),CancellationToken.None);
                var library=ZipAlbumReader.OpenFolder(result);Require(library.Tracks.Count==1,format+" real track, transactional publish, library import");
                try{CdImportService.Import(plan,new Progress<string>(),CancellationToken.None);throw new Exception("Overwrite allowed");}catch(IOException){Console.WriteLine("PASS existing disc not overwritten");}
                string second=CdImportService.Import(plan with{DiscNumber=2},new Progress<string>(Console.WriteLine),CancellationToken.None);
                Require(ZipAlbumReader.OpenFolder(second).Tracks.Count==2,format+" multi-disc album grouped");
            }
        }
        Console.WriteLine("Test files retained: "+folder);
    }
}
