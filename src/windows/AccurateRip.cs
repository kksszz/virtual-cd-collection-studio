using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using NAudio.Wave;

namespace ZipMp3Player;

// Independently implemented wire format/checksums. See docs/CD_IMPORT.md for references.
internal static class AccurateRip
{
    internal sealed record DiscId(int Tracks,uint Id1,uint Id2,uint Cddb)
    {
        internal string FileName=>$"dBAR-{Tracks:000}-{Id1:x8}-{Id2:x8}-{Cddb:x8}.bin";
        internal Uri Url=>new($"https://www.accuraterip.com/accuraterip/{Id1&15:x}/{(Id1>>4)&15:x}/{(Id1>>8)&15:x}/{FileName}");
    }
    internal sealed record Entry(byte Confidence,uint Crc,uint Frame450Crc);
    internal sealed record Lookup(string State,string Message,List<Entry[]> Pressings);
    internal sealed record Checksums(uint V1,uint V2);
    internal static DiscId Identify(CueAlbumReader.Disc disc)
    {
        if(disc.Tracks.Count is <1 or >99||disc.Frames<=0)throw new ArgumentException("Invalid CD TOC");
        uint id1=unchecked((uint)disc.Frames),id2=unchecked((uint)(disc.Frames*(disc.Tracks.Count+1)));
        int digits=0;
        for(int i=0;i<disc.Tracks.Count;i++){
            long offset=disc.Tracks[i].Frame;
            if(offset<0||offset>=disc.Frames||(i>0&&offset<=disc.Tracks[i-1].Frame))throw new ArgumentException("Invalid CD track offsets");
            id1=unchecked(id1+(uint)offset);id2=unchecked(id2+(uint)(Math.Max(1,offset)*(i+1)));
            for(long seconds=(offset+150)/75;seconds>0;seconds/=10)digits+=(int)(seconds%10);
        }
        uint length=(uint)((disc.Frames+150)/75-(disc.Tracks[0].Frame+150)/75);
        return new(disc.Tracks.Count,id1,id2,((uint)(digits%255)<<24)|(length<<8)|(uint)disc.Tracks.Count);
    }
    internal static Checksums Calculate(Stream pcm,long samples,bool first,bool last,CancellationToken token)
    {
        if(samples<1||samples>uint.MaxValue)throw new ArgumentOutOfRangeException(nameof(samples));
        long from=first?2940:1,to=samples-(last?2940:0),position=1;
        uint v1=0,v2=0;var buffer=new byte[32768];long remaining=checked(samples*4);
        while(remaining>0){
            token.ThrowIfCancellationRequested();int size=(int)Math.Min(buffer.Length,remaining);pcm.ReadExactly(buffer.AsSpan(0,size));
            for(int i=0;i<size;i+=4,position++)if(position>=from&&position<=to){
                ulong product=(ulong)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i,4))*(uint)position;
                v1=unchecked(v1+(uint)product);v2=unchecked(v2+(uint)product+(uint)(product>>32));
            }
            remaining-=size;
        }
        return new(v1,v2);
    }
    internal static Checksums CalculateWav(string path,bool first,bool last,CancellationToken token)
    {
        using var wav=new WaveFileReader(path);
        if(wav.WaveFormat.SampleRate!=44100||wav.WaveFormat.BitsPerSample!=16||wav.WaveFormat.Channels!=2||wav.WaveFormat.Encoding!=WaveFormatEncoding.Pcm||wav.Length%4!=0)throw new InvalidDataException("AccurateRip requires CD PCM");
        return Calculate(wav,wav.Length/4,first,last,token);
    }
    internal static List<Entry[]> Parse(ReadOnlySpan<byte> bytes,DiscId id)
    {
        var result=new List<Entry[]>();int position=0;
        while(position<bytes.Length){
            if(bytes.Length-position<13)throw new InvalidDataException("AccurateRip response is truncated");
            var header=bytes.Slice(position,13);int count=header[0];
            if(count!=id.Tracks||BinaryPrimitives.ReadUInt32LittleEndian(header[1..])!=id.Id1||BinaryPrimitives.ReadUInt32LittleEndian(header[5..])!=id.Id2||BinaryPrimitives.ReadUInt32LittleEndian(header[9..])!=id.Cddb)throw new InvalidDataException("AccurateRip disc ID mismatch");
            position+=13;if(bytes.Length-position<count*9)throw new InvalidDataException("AccurateRip tracks are truncated");
            var tracks=new Entry[count];
            for(int i=0;i<count;i++,position+=9){var entry=bytes.Slice(position,9);tracks[i]=new(entry[0],BinaryPrimitives.ReadUInt32LittleEndian(entry[1..]),BinaryPrimitives.ReadUInt32LittleEndian(entry[5..]));}
            result.Add(tracks);
        }
        if(result.Count==0)throw new InvalidDataException("Empty AccurateRip response");
        return result;
    }
    internal static async Task<Lookup> Fetch(DiscId id,CancellationToken token,HttpClient? supplied=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var owned=supplied is null?new HttpClient():null;var client=supplied??owned!;
        try{
            using var request=new HttpRequestMessage(HttpMethod.Get,id.Url);request.Headers.UserAgent.ParseAdd("VirtualCDCollectionStudio/0.89.0");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if(response.StatusCode==HttpStatusCode.NotFound)return new("not-found","未登録（読み取り失敗ではありません）",[]);
            response.EnsureSuccessStatusCode();
            const int maximum=2*1024*1024;
            if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("AccurateRip response too large");
            using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var output=new MemoryStream();var buffer=new byte[8192];int read;
            while((read=await input.ReadAsync(buffer,deadline.Token))>0){if(output.Length+read>maximum)throw new InvalidDataException("AccurateRip response too large");output.Write(buffer,0,read);}
            return new("found","照会済み",Parse(output.ToArray(),id));
        }catch(OperationCanceledException) when(!token.IsCancellationRequested){return new("unavailable","照合できませんでした（タイムアウト）",[]);}
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException){return new("unavailable","照合できませんでした（通信／応答エラー）："+ex.Message,[]);}
    }
    internal static string Match(Lookup lookup,int track,Checksums crc,bool offsetConfigured)
    {
        if(lookup.State!="found")return lookup.Message;
        var entries=lookup.Pressings.Select(p=>p[track-1]).Where(e=>e.Confidence>0).ToArray();
        int v1=entries.Where(e=>e.Crc==crc.V1).Select(e=>(int)e.Confidence).DefaultIfEmpty().Max();
        int v2=entries.Where(e=>e.Crc==crc.V2).Select(e=>(int)e.Confidence).DefaultIfEmpty().Max();
        if(Math.Max(v1,v2)>0)return $"一致（{(v2>=v1?"v2":"v1")}・信頼度 {Math.Max(v1,v2)}）";
        if(entries.Length==0)return "この曲の有効な照合記録なし";
        return offsetConfigured?"不一致（別プレス・補正値・読み取りを確認）":"不一致（補正未設定。読み取りエラーとは断定できません）";
    }
}
