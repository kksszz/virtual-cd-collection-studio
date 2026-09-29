using System.IO;

namespace ZipMp3Player;

internal static class CdOffsetReader
{
    internal const int SamplesPerSector=588;
    internal const int MaximumOffset=5880;
    // EAC/AccurateRip correction convention: +N skips N raw stereo samples.
    // Read only within the audio TOC; explicitly zero-pad inaccessible disc edges.
    internal static long Copy(Func<long,int,byte[]> read,Stream output,long startSector,long endSector,long discSectors,int correction,CancellationToken token,Action<double>? progress=null)
    {
        if(startSector<0||endSector<=startSector||endSector>discSectors||Math.Abs((long)correction)>MaximumOffset)throw new ArgumentOutOfRangeException(nameof(correction));
        long total=checked((endSector-startSector)*SamplesPerSector),discSamples=checked(discSectors*SamplesPerSector),padded=0;
        for(long done=0;done<total;){
            token.ThrowIfCancellationRequested();int size=(int)Math.Min(15*SamplesPerSector,total-done);
            long raw=checked(startSector*SamplesPerSector+done+correction),end=raw+size;
            long validStart=Math.Clamp(raw,0,discSamples),validEnd=Math.Clamp(end,0,discSamples);
            var block=new byte[size*4];
            if(validEnd>validStart){
                long sector=validStart/SamplesPerSector;int count=(int)((validEnd+SamplesPerSector-1)/SamplesPerSector-sector);
                var data=read(sector,count);if(data.Length!=count*2352)throw new IOException("CDの音声データが不足しています。");
                data.AsSpan((int)(validStart-sector*SamplesPerSector)*4,(int)(validEnd-validStart)*4).CopyTo(block.AsSpan((int)(validStart-raw)*4));
            }
            padded+=size-(validEnd-validStart);output.Write(block);done+=size;progress?.Invoke(done*100.0/total);
        }
        return padded;
    }
}
