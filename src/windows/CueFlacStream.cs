using NAudio.Wave;
namespace ZipMp3Player;
/// <summary>A decoded, frame-aligned window; the FLAC source is never rewritten.</summary>
internal sealed class CueFlacStream : WaveStream
{
    private readonly WaveStream source;
    private readonly long start,length;
    private long position;
    public CueFlacStream(ZipTrack track){
        source=new FlakeNAudioAdapter.FlakeFileReader(track.SourcePath);
        start=track.CueStartFrame*source.WaveFormat.SampleRate/75*source.WaveFormat.BlockAlign;
        length=Math.Min(source.Length-start,(long)Math.Round(track.Duration.TotalSeconds*source.WaveFormat.SampleRate)*source.WaveFormat.BlockAlign);
        if(start<0||length<=0){source.Dispose();throw new System.IO.InvalidDataException("CUEの再生範囲が不正です");}
        try{source.Position=start;}catch{source.Dispose();throw;}
    }
    public override WaveFormat WaveFormat=>source.WaveFormat;
    public override long Length=>length;
    public override long Position{get=>position;set{long target=Math.Clamp(value,0,length)/WaveFormat.BlockAlign*WaveFormat.BlockAlign;source.Position=start+target;position=target;}}
    public override int Read(byte[] buffer,int offset,int count){int n=source.Read(buffer,offset,(int)Math.Min(count,length-position));position+=n;return n;}
    protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
}
