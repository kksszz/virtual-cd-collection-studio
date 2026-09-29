using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NAudio.Wave;
using CUETools.Codecs.FLAKE;

namespace ZipMp3Player;

internal sealed class CdImportTrack
{
    public bool Selected {get;set;}=true;
    public int Number {get;set;}
    public string Title {get;set;}="";
    public string Artist {get;set;}="";
    public string Duration {get;set;}="";
    public string Album {get;set;}="";
    public string AlbumArtist {get;set;}="";
    public uint Year {get;set;}
    public int TrackCount {get;set;}
    public int DiscNumber {get;set;}
    public int DiscCount {get;set;}
    public string Genre {get;set;}="";
    public string Composer {get;set;}="";
    public string Comment {get;set;}="";
    public string Verification {get;set;}="";
}
internal sealed record CdImportProgress(int Track,int CompletedTracks,int TotalTracks,double? Percent,string Message,string? Verification=null);
internal sealed record CdImportPlan(string Drive,CueAlbumReader.Disc Disc,string Destination,string Album,string Artist,uint Year,int DiscNumber,int DiscCount,string Format,int Bitrate,List<CdImportTrack> Tracks,int ReadOffset=0,bool VerifyAccurateRip=false,bool OffsetConfigured=false,string DriveIdentity="");
internal static class CdImportService
{
    internal static string SafeName(string text)
    {
        string name=new string(text.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim().TrimEnd('.');
        if(name.Length>100)name=name[..100].TrimEnd('.',' ');
        if(name.Length==0||Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))name="_"+name;
        return name;
    }
    internal static string Import(CdImportPlan plan,IProgress<string> progress,CancellationToken token,IProgress<CdImportProgress>? detail=null)
    {
        token.ThrowIfCancellationRequested();
        if(plan.Format is not ("FLAC" or "MP3")||plan.Bitrate is not (192 or 256 or 320)||plan.DiscNumber<1||plan.DiscCount<plan.DiscNumber||plan.Tracks.Count==0||Math.Abs((long)plan.ReadOffset)>CdOffsetReader.MaximumOffset||(!plan.OffsetConfigured&&plan.ReadOffset!=0))throw new ArgumentException("取り込み設定が不正です。");
        if(plan.Tracks.Any(t=>t.Number<1||t.Number>plan.Disc.Tracks.Count||string.IsNullOrWhiteSpace(t.Title))||plan.Tracks.Select(t=>t.Number).Distinct().Count()!=plan.Tracks.Count)throw new ArgumentException("取り込み曲の指定が不正です。");
        var root=Path.GetFullPath(plan.Destination);Directory.CreateDirectory(root);
        var album=Path.Combine(root,SafeName(plan.Artist)+" - "+SafeName(plan.Album));
        var destination=plan.DiscCount==1?album:Path.Combine(album,$"Disc{plan.DiscNumber}");
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("同じアルバム・ディスク番号が存在します。上書きはしません。");
        if(Directory.Exists(album)&&!FolderAlbumLayout.IsRoot(album))throw new IOException("同名の既存フォルダーがあります。別の保存先かアルバム名を指定してください。");
        using var cd=new CdAudioSource(plan.Drive);
        if(cd.Disc.Toc!=plan.Disc.Toc)throw new IOException("曲情報を読み込んだCDと異なります。再読込してください。");
        if(plan.DriveIdentity.Length>0&&cd.DriveIdentity!=plan.DriveIdentity)throw new IOException("CDドライブが変更されました。CDを再読込して補正値を確認してください。");
        // The library already excludes this reserved temporary-folder pattern.
        var stage=Path.Combine(root,".zip-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        string retained=stage;
        var log=new List<string>{"CD import: "+DateTimeOffset.Now,"TOC: "+plan.Disc.Toc,"Drive: "+cd.DriveIdentity,"Format: "+plan.Format,$"Offset correction: {plan.ReadOffset:+0;-0;0} stereo samples; configured={plan.OffsetConfigured}","Read policy: two matching reads, up to 4 attempts; drive cache flushing/C2 not implemented; no overread beyond audio TOC."};
        try{
            var arId=AccurateRip.Identify(plan.Disc);
            var lookup=new AccurateRip.Lookup("disabled","照合OFF",[]);
            if(plan.VerifyAccurateRip){progress.Report("AccurateRipへ照会しています…");lookup=AccurateRip.Fetch(arId,token).GetAwaiter().GetResult();}
            log.Add($"AccurateRip: {arId.FileName}; {lookup.State}; {lookup.Message}");
            int completed=0;
            foreach(var track in plan.Tracks){
                token.ThrowIfCancellationRequested();cd.VerifyDisc();
                detail?.Report(new(track.Number,completed,plan.Tracks.Count,0,$"曲 {track.Number}: 読み取り 0% — {track.Title}"));
                int index=track.Number-1;long start=plan.Disc.Tracks[index].Frame,end=index+1<plan.Disc.Tracks.Count?plan.Disc.Tracks[index+1].Frame:plan.Disc.Frames;
                string wav=Path.Combine(stage,"working.wav");
                long padding;
                using(var writer=new WaveFileWriter(wav,new WaveFormat(44100,16,2))){
                    byte[] ReadVerified(long sector,int count){
                        byte[]? previous=null,accepted=null;
                        for(int attempt=0;attempt<4;attempt++){
                            token.ThrowIfCancellationRequested();
                            try{var block=cd.Read(sector,count);if(previous is not null&&block.AsSpan().SequenceEqual(previous)){accepted=block;break;}previous=block;}
                            catch(System.ComponentModel.Win32Exception){previous=null;}
                            if(attempt>0)log.Add($"Track {track.Number} sector {sector}: retry {attempt}");
                        }
                        if(accepted is null)throw new IOException($"曲{track.Number}・セクター{sector}を一致して読み取れませんでした。");
                        return accepted;
                    }
                    double lastPercent=-1;
                    padding=CdOffsetReader.Copy(ReadVerified,writer,start,end,plan.Disc.Frames,plan.ReadOffset,token,percent=>{
                        if(percent-lastPercent>=1||percent==100){
                            lastPercent=percent;
                            string message=$"曲 {track.Number}: 読み取り {percent:F0}% — {track.Title}";
                            progress.Report(message);detail?.Report(new(track.Number,completed,plan.Tracks.Count,percent,message));
                        }
                    });
                    if(padding>0)log.Add($"Track {track.Number}: {padding} stereo samples zero-padded at disc boundary; edge audio NOT recovered.");
                }
                if(plan.VerifyAccurateRip){
                    var crc=AccurateRip.CalculateWav(wav,index==0,index==plan.Disc.Tracks.Count-1,token);
                    track.Verification=AccurateRip.Match(lookup,track.Number,crc,plan.OffsetConfigured);
                    log.Add($"Track {track.Number} AccurateRip: {track.Verification}; CRC v1={crc.V1:X8}, v2={crc.V2:X8}; offset={plan.ReadOffset}");
                }else track.Verification="照合OFF";
                if(padding>0)track.Verification+=$" ／ 端部無音補完 {padding}サンプル";
                cd.VerifyDisc();token.ThrowIfCancellationRequested();progress.Report($"曲 {track.Number}: {plan.Format}変換・検証中…");
                detail?.Report(new(track.Number,completed,plan.Tracks.Count,null,$"曲 {track.Number}: {plan.Format}変換中…"));
                string output=Path.Combine(stage,$"{track.Number:00} {SafeName(track.Title)}.{plan.Format.ToLowerInvariant()}");
                Encode(wav,output,plan.Format,plan.Bitrate,token);
                WriteTags(output,plan,track);
                detail?.Report(new(track.Number,completed,plan.Tracks.Count,null,$"曲 {track.Number}: 音声・タグを検証中…"));
                Verify(wav,output,plan.Format,token);File.Delete(wav);log.Add($"Track {track.Number}: verified {Path.GetFileName(output)}");
                completed++;detail?.Report(new(track.Number,completed,plan.Tracks.Count,100,$"曲 {track.Number}: 完了 — {track.Verification}",track.Verification));
            }
            token.ThrowIfCancellationRequested();cd.VerifyDisc();
            File.WriteAllLines(Path.Combine(stage,"cd-import.log"),log);
            detail?.Report(new(0,completed,plan.Tracks.Count,null,"アルバムの保存を確定しています…"));
            if(plan.DiscCount==1){
                PublishSingleDisc(stage,album);
            }else if(!Directory.Exists(album)){
                // Prepare the entire new album out of sight, then publish it atomically.
                var wrapper=Path.Combine(root,".zip-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(wrapper);
                File.WriteAllText(Path.Combine(wrapper,FolderAlbumLayout.MarkerName),JsonSerializer.Serialize(new FolderAlbumLayout.Marker(1,"CDDA",0,0)));
                Directory.Move(stage,Path.Combine(wrapper,$"Disc{plan.DiscNumber}"));retained=wrapper;Directory.Move(wrapper,album);
            }else{if(!FolderAlbumLayout.IsRoot(album))throw new IOException("保存先が変更されました。");Directory.Move(stage,destination);}
            return album;
        }catch(Exception ex){
            if(Directory.Exists(retained)){log.Add(ex.Message);try{File.WriteAllLines(Path.Combine(retained,"cd-import.log"),log);}catch(IOException){}}
            throw new IOException($"取り込みを中断しました。既存の音楽は変更していません。\n一時データ（一覧には登録しません）: {retained}\n{ex.Message}",ex);
        }
    }
    internal static void WriteTags(string output,CdImportPlan plan,CdImportTrack track)
    {
        using(var file=TagLib.File.Create(output)){
            // ID3v2.4 preserves literal slashes in artist and genre strings.
            if(file.GetTag(TagLib.TagTypes.Id3v2,false) is TagLib.Id3v2.Tag id3)id3.Version=4;
            var tag=file.Tag;tag.Title=track.Title;tag.Performers=[track.Artist];tag.AlbumArtists=[plan.Artist];tag.Album=plan.Album;
            tag.Track=(uint)track.Number;tag.TrackCount=(uint)plan.Disc.Tracks.Count;tag.Disc=(uint)plan.DiscNumber;tag.DiscCount=(uint)plan.DiscCount;tag.Year=plan.Year;
            tag.Genres=string.IsNullOrWhiteSpace(track.Genre)?[]:[track.Genre];tag.Composers=string.IsNullOrWhiteSpace(track.Composer)?[]:[track.Composer];tag.Comment=track.Comment;file.Save();
        }
        using var check=TagLib.File.Create(output);var saved=check.Tag;
        if(saved.Title!=track.Title||saved.Album!=plan.Album||saved.FirstPerformer!=track.Artist||saved.FirstAlbumArtist!=plan.Artist||saved.Year!=plan.Year
            ||saved.Track!=track.Number||saved.TrackCount!=plan.Disc.Tracks.Count||saved.Disc!=plan.DiscNumber||saved.DiscCount!=plan.DiscCount
            ||(saved.FirstGenre??"")!=track.Genre||(saved.FirstComposer??"")!=track.Composer||(saved.Comment??"")!=track.Comment)throw new IOException("タグの再読込検証に失敗しました。");
    }
    internal static void PublishSingleDisc(string stage,string album)
    {
        // Publish the verified track files directly; never merge into an existing album.
        if(Directory.Exists(album)||File.Exists(album))throw new IOException("同名のアルバムが存在します。上書きはしません。");
        File.WriteAllText(Path.Combine(stage,FolderAlbumLayout.MarkerName),JsonSerializer.Serialize(new FolderAlbumLayout.Marker(1,"CDDA",0,0)));
        Directory.Move(stage,album);
    }
    internal static void Encode(string wav,string output,string format,int bitrate,CancellationToken token)
    {
        using var input=new WaveFileReader(wav);
        if(format=="MP3"){
            MediaFoundationEncoder.EncodeToMp3(new CancelProvider(input,token),output,bitrate*1000);token.ThrowIfCancellationRequested();return;
        }
        var settings=new FlakeWriterSettings{PCM=AudioPCMConfig.RedBook,EncoderMode="5",DoMD5=true,DoVerify=true};
        var writer=new FlakeWriter(output,settings);writer.FinalSampleCount=input.Length/4;
        bool complete=false;
        try{var bytes=new byte[16384];int n;while((n=input.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();writer.Write(new AudioBuffer(AudioPCMConfig.RedBook,bytes,n/4));}complete=true;}
        finally{if(complete)writer.Close();else try{writer.Close();}catch{/* Preserve cancellation/read failure, not a partial-stream sample-count error. */}}
    }
    internal static void Verify(string wav,string output,string format,CancellationToken token)
    {
        using var original=new WaveFileReader(wav);
        using WaveStream decoded=format=="FLAC"?new FlakeNAudioAdapter.FlakeFileReader(output):new Mp3FileReader(output);
        if(decoded.WaveFormat.SampleRate!=44100||decoded.WaveFormat.Channels!=2)throw new IOException("音声形式の検証に失敗しました。");
        var bytes=new byte[32768];using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long total=0;int n;
        while((n=decoded.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();hash.AppendData(bytes,0,n);total+=n;}
        if(format=="FLAC"){
            byte[] actual=hash.GetHashAndReset();while((n=original.Read(bytes,0,bytes.Length))>0){token.ThrowIfCancellationRequested();hash.AppendData(bytes,0,n);}
            if(total!=original.Length||!actual.AsSpan().SequenceEqual(hash.GetHashAndReset()))throw new IOException("FLACの音声が読み取り元と一致しません。");
        }else if(Math.Abs(total-original.Length)>44100*4/4)throw new IOException("MP3の曲長が読み取り元と一致しません。");
    }
    private sealed class CancelProvider(IWaveProvider source,CancellationToken token):IWaveProvider
    {public WaveFormat WaveFormat=>source.WaveFormat;public int Read(byte[] buffer,int offset,int count){token.ThrowIfCancellationRequested();return source.Read(buffer,offset,count);}}
}
