using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using ZipMp3Player;

internal static class AccurateRipChecks
{
    // Explicit real-device diagnostic: no settings, library or audio files are written.
    internal static void Device(string drive,int offset)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var token=timeout.Token;
        using var cd=new CdAudioSource(drive);
        Console.WriteLine("Device: "+cd.DriveIdentity);
        var disc=cd.Disc;var id=AccurateRip.Identify(disc);
        Console.WriteLine($"TOC: {disc.Toc}; offset={offset}; ID={id.FileName}");
        var lookup=AccurateRip.Fetch(id,token).GetAwaiter().GetResult();
        Console.WriteLine($"Lookup: {lookup.State}; {lookup.Message}; pressings={lookup.Pressings.Count}");
        if(lookup.State!="found")return;
        int matched=0;
        for(int i=0;i<disc.Tracks.Count;i++){
            cd.VerifyDisc();long start=disc.Tracks[i].Frame,end=i+1<disc.Tracks.Count?disc.Tracks[i+1].Frame:disc.Frames;
            Console.WriteLine($"Reading track {i+1}/{disc.Tracks.Count} ({(end-start)/75d:F1}s)...");
            using var pcm=new MemoryStream();
            byte[] Read(long sector,int count){
                byte[]? previous=null;
                for(int attempt=0;attempt<4;attempt++){
                    token.ThrowIfCancellationRequested();var block=cd.Read(sector,count);
                    if(previous is not null&&block.AsSpan().SequenceEqual(previous))return block;
                    previous=block;
                }
                throw new IOException($"Nonmatching reads at sector {sector}");
            }
            long padding=CdOffsetReader.Copy(Read,pcm,start,end,disc.Frames,offset,token);
            pcm.Position=0;var crc=AccurateRip.Calculate(pcm,(end-start)*588,i==0,i==disc.Tracks.Count-1,token);
            string result=AccurateRip.Match(lookup,i+1,crc,true);
            if(result.StartsWith("一致"))matched++;
            Console.WriteLine($"Track {i+1}: {result}; v1={crc.V1:X8}; v2={crc.V2:X8}; padding={padding}");
        }
        cd.VerifyDisc();Console.WriteLine($"Device verification completed: {matched}/{disc.Tracks.Count} matched. No audio or settings written.");
        if(matched!=disc.Tracks.Count)Environment.ExitCode=2;
    }
    internal static void Run()
    {
        static void Require(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
        const string offsetHtml="""
            <html><table><tr><td>CD Drive</td><td>Correction Offset</td><td>Submitted By</td><td>Percentage Agree</td></tr>
            <tr><td><font>hp HLDS - DVDROM DUD1N</font></td><td>+6</td><td>6</td><td>100%</td></tr>
            <tr><td>LG Electronics - DVD-ROM XYZ</td><td>-667</td><td>10</td><td>100%</td></tr>
            <tr><td>Maker - Unsafe</td><td>[Purged]</td><td>1</td><td>100%</td></tr>
            <tr><td>Maker - Mixed</td><td>6</td><td>1</td><td>80%</td></tr>
            <tr><td>Maker - Conflict</td><td>6</td><td>1</td><td>100%</td></tr>
            <tr><td>Maker - Conflict</td><td>102</td><td>2</td><td>100%</td></tr>
            <tr><td>Other - DVDROM DUD1N</td><td>102</td><td>3</td><td>100%</td></tr>
            <tr><td>Maker - Zero</td><td>0</td><td>3</td><td>100%</td></tr>
            <tr><td>Maker - A&amp;B</td><td>6</td><td>3</td><td>100%</td></tr>
            <tr><td>Maker - Out</td><td>5881</td><td>3</td><td>100%</td></tr>
            </table></html>
            """;
        var offsetList=DriveOffsetLookup.Parse(offsetHtml);
        Require(offsetList.Count==9,"Drive table parses HTML, entities and numeric bounds");
        Require(DriveOffsetLookup.Find(offsetList," hp HLDS | DVDROM DUD1N | MDM2 | ").Match?.Samples==6,"Drive vendor/model exact match");
        Require(DriveOffsetLookup.Find(offsetList,"HP   HLDS | dvdrom dud1n | DIFFERENT | SERIAL").State=="found","Drive lookup normalizes case/whitespace, not firmware/serial");
        Require(DriveOffsetLookup.Find(offsetList,"HL-DT-ST | DVD-ROM XYZ | | ").Match?.Samples==-667,"Official vendor alias; negative correction");
        Require(DriveOffsetLookup.Find(offsetList,"Maker | A&B | | ").State=="found","Drive HTML entity decoding");
        Require(DriveOffsetLookup.Find(offsetList,"Maker | Zero | | ").Match?.Samples==0,"Zero correction is a valid setting");
        foreach(var pair in new[]{("Unsafe","purged"),("Mixed","ambiguous"),("Conflict","ambiguous"),("Out","not-found")})Require(DriveOffsetLookup.Find(offsetList,$"Maker | {pair.Item1} | | ").State==pair.Item2,"Unsafe lookup rejected: "+pair.Item1);
        Require(DriveOffsetLookup.Find(offsetList,"Other2 | DVDROM DUD1N | | ").State=="not-found","Matching model from another vendor never reused");
        Require(DriveOffsetLookup.Find(offsetList,"hp HLDS | DVDROM DUD | | ").State=="not-found","Partial/truncated model never guessed");
        Require(DriveOffsetLookup.Find(offsetList," | DVDROM DUD1N | | ").State=="unknown","Unknown vendor does not auto-configure");
        try{DriveOffsetLookup.Parse("<html>error page</html>");throw new Exception("Accepted missing drive table");}catch(InvalidDataException){Console.WriteLine("PASS missing drive table rejected");}
        using(var client=new HttpClient(new ResponseHandler(HttpStatusCode.OK,System.Text.Encoding.Latin1.GetBytes(offsetHtml))))Require(DriveOffsetLookup.Fetch(CancellationToken.None,client).GetAwaiter().GetResult().Count==9,"Drive list successful download");
        using(var client=new HttpClient(new ResponseHandler(HttpStatusCode.Forbidden,[])))try{DriveOffsetLookup.Fetch(CancellationToken.None,client).GetAwaiter().GetResult();throw new Exception("Accepted HTTP error");}catch(HttpRequestException){Console.WriteLine("PASS drive list HTTP error propagated");}
        using(var client=new HttpClient(new ResponseHandler(HttpStatusCode.OK,[])))using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();try{DriveOffsetLookup.Fetch(cancelled.Token,client).GetAwaiter().GetResult();throw new Exception("Drive cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS drive lookup cancellation");}
        }
        var disc=new CueAlbumReader.Disc("","","","",1,27000,[new(1,0,"",""),new(2,13500,"","")],"");
        var id=AccurateRip.Identify(disc);
        Require(id.Id1==40500&&id.Id2==108001&&id.Cddb==0x0d016802,"AR disc IDs use LBA, CDDB uses lead-in");
        Require(id.Url.AbsolutePath=="/accuraterip/4/3/e/dBAR-002-00009e34-0001a5e1-0d016802.bin","AR shard URL");
        var pcm=Enumerable.Repeat((byte)255,40000).ToArray();
        foreach(var vector in new[]{(false,false,0xfd04fbf8u,0xffffd8f0u),(true,false,0xfd46e842u,0xffffe46bu),(false,true,0xfe83ab6eu,0xffffe46cu),(true,true,0xfec597b8u,0xffffefe7u)}){
            var crc=AccurateRip.Calculate(new MemoryStream(pcm),10000,vector.Item1,vector.Item2,CancellationToken.None);
            Require(crc.V1==vector.Item3&&crc.V2==vector.Item4,$"AR v1/v2 overflow and boundary vector first={vector.Item1} last={vector.Item2}");
        }
        var boundary=new byte[40000];BinaryPrimitives.WriteUInt32LittleEndian(boundary.AsSpan(2938*4),7);BinaryPrimitives.WriteUInt32LittleEndian(boundary.AsSpan(2939*4),11);
        var edge=AccurateRip.Calculate(new MemoryStream(boundary),10000,true,false,CancellationToken.None);
        Require(edge.V1==11*2940,"AR first track skips exactly 2939 samples, multiplier is not reset");
        Array.Clear(boundary);BinaryPrimitives.WriteUInt32LittleEndian(boundary.AsSpan(7059*4),13);BinaryPrimitives.WriteUInt32LittleEndian(boundary.AsSpan(7060*4),17);
        Require(AccurateRip.Calculate(new MemoryStream(boundary),10000,false,true,CancellationToken.None).V1==13*7060,"AR last track skips exactly 2940 samples");
        var response=new byte[31];response[0]=2;
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(1),id.Id1);BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(5),id.Id2);BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(9),id.Cddb);
        response[13]=12;BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(14),0xfd04fbf8);BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(18),99);
        response[22]=3;BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(23),0xffffd8f0);
        var lookup=new AccurateRip.Lookup("found","",AccurateRip.Parse(response,id));
        Require(AccurateRip.Match(lookup,1,new(0xfd04fbf8,0),true).Contains("信頼度 12"),"AR v1 matches stored CRC");
        Require(AccurateRip.Match(lookup,2,new(0,0xffffd8f0),true).Contains("v2"),"AR v2 matches SAME stored CRC, not frame450 CRC");
        Require(AccurateRip.Match(lookup,1,new(99,99),false).StartsWith("不一致"),"AR frame450 CRC is not a track match");
        var duplicate=AccurateRip.Parse(response.Concat(response).ToArray(),id);
        Require(AccurateRip.Match(new("found","",duplicate),1,new(0xfd04fbf8,0),true).Contains("信頼度 12"),"AR duplicate pressings do not inflate confidence");
        foreach(var invalid in new[]{response[..^1],response.Concat(new byte[]{0}).ToArray(),Array.Empty<byte>()}){
            try{AccurateRip.Parse(invalid,id);throw new Exception("Accepted malformed AR");}catch(InvalidDataException){Console.WriteLine("PASS malformed AR response rejected");}
        }
        var wrong=(byte[])response.Clone();wrong[1]^=1;
        try{AccurateRip.Parse(wrong,id);throw new Exception("Accepted wrong AR ID");}catch(InvalidDataException){Console.WriteLine("PASS wrong disc ID rejected");}
        foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.Forbidden,HttpStatusCode.InternalServerError,HttpStatusCode.OK}){
            using var client=new HttpClient(new ResponseHandler(status,response));
            var found=AccurateRip.Fetch(id,CancellationToken.None,client).GetAwaiter().GetResult();
            Require(found.State==(status==HttpStatusCode.NotFound?"not-found":status==HttpStatusCode.OK?"found":"unavailable"),"AR HTTP state: "+status);
        }
        using(var client=new HttpClient(new ResponseHandler(HttpStatusCode.OK,wrong))){Require(AccurateRip.Fetch(id,CancellationToken.None,client).GetAwaiter().GetResult().State=="unavailable","Invalid server response does not become mismatch/not-found");}
        using(var client=new HttpClient(new ResponseHandler(HttpStatusCode.OK,response)))using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();try{AccurateRip.Fetch(id,cancelled.Token,client).GetAwaiter().GetResult();throw new Exception("Cancellation swallowed");}catch(OperationCanceledException){Console.WriteLine("PASS AR cancellation propagated");}
        }
        var raw=new byte[100*2352];for(int i=0;i<raw.Length/4;i++)BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i*4),unchecked((uint)(i*7919+123)));
        foreach(int offset in new[]{0,6,-6,588,-588,667,-667,5880,-5880})foreach(var range in new[]{(0L,100L),(20L,80L)}){
            int calls=0;using var output=new MemoryStream();
            long padding=CdOffsetReader.Copy((sector,count)=>{RequireRead(sector,count);calls++;return raw.AsSpan((int)sector*2352,count*2352).ToArray();},output,range.Item1,range.Item2,100,offset,CancellationToken.None);
            var actual=output.ToArray();long expectedPadding=0;
            Require(actual.Length==(range.Item2-range.Item1)*2352,"Offset output length preserved");
            for(int i=0;i<actual.Length/4;i++){
                long source=range.Item1*588+i+offset;uint expected=0;
                if(source>=0&&source<raw.Length/4)expected=BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan((int)source*4));else expectedPadding++;
                if(BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(i*4))!=expected)throw new Exception("Offset sample mapping: "+offset+" at "+i);
            }
            Require(padding==expectedPadding&&calls>0,$"Offset {offset}: sign, sector/chunk boundaries, exact samples and padding");
        }
        using(var cancelled=new CancellationTokenSource()){
            cancelled.Cancel();try{CdOffsetReader.Copy((_,_)=>throw new Exception("Read after cancellation"),Stream.Null,0,100,100,6,cancelled.Token);throw new Exception("Ignored cancellation");}catch(OperationCanceledException){Console.WriteLine("PASS offset cancellation");}
        }
        static void RequireRead(long sector,int count){if(sector<0||count<1||count>16||sector+count>100)throw new Exception("Read outside audio TOC/block limit");}
    }
    private sealed class ResponseHandler(HttpStatusCode status,byte[] content):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(status){Content=new ByteArrayContent(content)});}
    }
}
