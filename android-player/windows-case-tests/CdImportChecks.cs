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
        ((System.Windows.Controls.TextBox)Field("album")).Text="Dream Horizon";((System.Windows.Controls.TextBox)Field("artist")).Text="G-GRIP";
        ((System.Windows.Controls.DataGrid)Field("tracks")).ItemsSource=Enumerable.Range(1,10).Select(i=>new CdImportTrack{Number=i,Title="日本語の曲名を編集できます "+i,Artist="G-GRIP",Duration="04:00"}).ToList();
        var format=(System.Windows.Controls.ComboBox)Field("format");var bitrate=(System.Windows.Controls.ComboBox)Field("bitrate");
        if(bitrate.IsEnabled)throw new Exception("FLAC bitrate must be disabled");format.SelectedItem="MP3";if(!bitrate.IsEnabled)throw new Exception("MP3 bitrate must be enabled");
        var grid=(System.Windows.Controls.DataGrid)Field("tracks");
        ((System.Windows.Controls.TextBox)Field("year")).Text="1990";
        if(grid.Columns.Count!=14||((List<CdImportTrack>)grid.ItemsSource)[0].Album!="Dream Horizon")throw new Exception("Tag columns/common sync");
        var bar=(System.Windows.Controls.ProgressBar)Field("progressBar");
        window.UpdateImportProgress(new CdImportProgress(2,1,10,null,"エンコード中"));if(!bar.IsIndeterminate)throw new Exception("Encoding progress");
        window.UpdateImportProgress(new CdImportProgress(2,1,10,42,"曲 2 を読み取り中…"));if(bar.IsIndeterminate||bar.Value!=42)throw new Exception("Read progress");
        var root=(System.Windows.Controls.DockPanel)window.Content;root.Background=window.Background;
        root.Measure(new System.Windows.Size(1192,700));root.Arrange(new System.Windows.Rect(0,0,1192,700));root.UpdateLayout();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1192,700,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(root);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using var file=File.Create(output);png.Save(file);
        Console.WriteLine("PASS CD import UI: format toggle; rendered "+output);
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
