using System.IO;
using System.Reflection;
using NAudio.Wave;
using ZipMp3Player;
internal static class CdPreviewChecks
{
    internal static void Run(string? drive)
    {
        var disc=new CueAlbumReader.Disc("","","","",1,50,[new(1,0,"One",""),new(2,19,"Two","")],"");
        int releases=0;var calls=new List<(long Start,int Count)>();
        byte[] Read(long start,int count){calls.Add((start,count));return Enumerable.Range(0,count*2352).Select(i=>(byte)((start*2352+i)%251)).ToArray();}
        using(var provider=new CdTrackPreviewSource(disc,2,Read,()=>releases++)){
            using var bytes=new MemoryStream();var buffer=new byte[4101];int n;
            while((n=provider.Read(buffer,3,4097))>0)bytes.Write(buffer,3,n);
            var expected=Enumerable.Range(0,31*2352).Select(i=>(byte)((19L*2352+i)%251)).ToArray();
            if(!bytes.ToArray().SequenceEqual(expected)||calls.Any(c=>c.Start<19||c.Start+c.Count>50))throw new Exception("Track boundary/buffer alignment");
            if(provider.WaveFormat.SampleRate!=44100||provider.WaveFormat.Channels!=2||Math.Abs(provider.Duration.TotalSeconds-31/75d)>.001)throw new Exception("CD format/duration");
            var seek=TimeSpan.FromSeconds(.123);int offset=(int)(seek.TotalSeconds*176400)/4*4;
            provider.Seek(seek);int read=provider.Read(buffer,0,2048);
            if(!buffer.Take(read).SequenceEqual(expected.Skip(offset).Take(read)))throw new Exception("Seek inside sector mismatch");
            provider.Seek(TimeSpan.Zero);read=provider.Read(buffer,0,2048);
            if(!buffer.Take(read).SequenceEqual(expected.Take(read)))throw new Exception("Seek backward mismatch");
            provider.Seek(provider.Duration);if(provider.Read(buffer,0,4)!=0)throw new Exception("Seek at end crossed track boundary");
            provider.Dispose();provider.Dispose();if(provider.Read(buffer,0,4)!=0)throw new Exception("Read after stop");
        }
        if(releases!=1)throw new Exception("Drive lock not released exactly once");
        using(var provider=new CdTrackPreviewSource(disc,1,(_,_)=>[],()=>{})){
            try{provider.Read(new byte[4096],0,4096);throw new Exception("Short CD block ignored");}catch(IOException){}
        }
        Console.WriteLine("PASS CD preview: PCM format, partial buffers, selected track only, forward/backward/end seek, EOF, stop/dispose, read error");
        int outputReleases=0;
        var silentDisc=disc with{Frames=300,Tracks=[new(1,0,"Silent test","")]};
        var silentSource=new CdTrackPreviewSource(silentDisc,1,(_,count)=>new byte[count*2352],()=>outputReleases++);
        silentSource.Seek(TimeSpan.FromSeconds(1));
        var silentPlayer=Task.Run(()=>(CdTrackPreviewPlayer)Activator.CreateInstance(typeof(CdTrackPreviewPlayer),BindingFlags.NonPublic|BindingFlags.Instance,null,[silentSource],null)!).GetAwaiter().GetResult();
        try{
            ((WaveOutEvent)typeof(CdTrackPreviewPlayer).GetField("output",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(silentPlayer)!).Volume=0;
            silentPlayer.Play();System.Threading.Thread.Sleep(700);
            if(silentPlayer.Position.TotalSeconds<1.1)throw new Exception("Silent preview output did not advance from seek position");
        }finally{Task.Run(silentPlayer.Dispose).GetAwaiter().GetResult();}
        if(outputReleases!=1)throw new Exception("Preview output did not release source");
        Console.WriteLine("PASS asynchronous audio output and stop/release (silent fixture)");
        if(drive is not null){
            CueAlbumReader.Disc real;using(var cd=new CdAudioSource(drive))real=cd.Disc;
            using(var source=CdTrackPreviewSource.Open(drive,real,1)){var data=new byte[2352*16];if(source.Read(data,0,data.Length)!=data.Length)throw new Exception("Real preview read failed");}
            using(var player=CdTrackPreviewPlayer.Open(drive,real,1)){
                ((WaveOutEvent)typeof(CdTrackPreviewPlayer).GetField("output",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(player)!).Volume=0;
                Exception? failure=null;player.Stopped+=(_,e)=>failure=e.Exception;player.Play();
                System.Threading.Thread.Sleep(2500);
                if(failure is not null)throw failure;if(player.Position.TotalSeconds<.3)throw new Exception("No output progress");
                Console.WriteLine("PASS real CD preview output (muted): "+player.Position);
            }
            using var reopened=new CdAudioSource(drive);reopened.Read(reopened.Disc.Tracks[0].Frame,1);
            Console.WriteLine("PASS drive reusable after preview stop");
        }
    }
}
