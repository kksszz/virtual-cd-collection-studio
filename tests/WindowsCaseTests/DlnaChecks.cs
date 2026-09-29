using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using ZipMp3Player;

internal static class DlnaChecks
{
    private static void Check(bool value,string description){if(!value)throw new Exception(description);Console.WriteLine("PASS "+description);}
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        string folder=Path.Combine(Path.GetTempPath(),"vccs-dlna-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        // Transport fixtures are deliberately not real audio: validate exact bytes, not codec compatibility.
        byte[] data=Enumerable.Range(0,10000).Select(i=>(byte)(i%251)).ToArray();string direct=Path.Combine(folder,"track.mp3");File.WriteAllBytes(direct,data);
        string stored=Path.Combine(folder,"stored.zip"),deflated=Path.Combine(folder,"deflated.zip");
        long deflatedLength=0;
        foreach(var (zipPath,level) in new[]{(stored,CompressionLevel.NoCompression),(deflated,CompressionLevel.SmallestSize)})
        {
            using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create)){using(var song=zip.CreateEntry("01.mp3",level).Open())song.Write(data);using var secret=zip.CreateEntry("not-shared.txt").Open();secret.Write(Encoding.UTF8.GetBytes("private fixture must not be streamed"));}
            if(zipPath==deflated){using var zip=ZipFile.OpenRead(zipPath);deflatedLength=zip.GetEntry("01.mp3")!.CompressedLength;}
        }
        ZipTrack Track(string file,string title,int number=1,bool archive=false,int method=0,long packed=10000,string cue="")=>new(){SourcePath=file,FileName="01.mp3",Title=title,Album="日本語 <Album> &",Artist="Artist & A",TrackNumber=number,DiscNumber=1,IsArchiveEntry=archive,CompressionMethod=method,DataOffset=archive?3:0,Size=data.Length,CompressedSize=packed,AudioFormat="MP3",IsMp3Valid=true,BitrateKbps=128,SampleRate=44100,Duration=TimeSpan.FromSeconds(5),CuePath=cue};
        var tracks=new[]{Track(direct,"日本語 <Track> & 🎵"),Track(stored,"Stored",2,true),Track(deflated,"Deflated",3,true,8,deflatedLength),Track(direct,"CUE full image must not leak",cue:"disc.cue")};
        var album=new DlnaAlbum(new ZipAlbum{Path=folder,Tracks=tracks},"日本語 <Album> &","Artist & A",()=>new byte[]{255,216,255,217});
        var catalog=new DlnaCatalog([album]);Check(catalog.TrackCount==3&&catalog.AlbumCount==1&&catalog.SkippedCount==1,"selected snapshot excludes CUE full-image leak");
        var tooLarge=new ZipTrack{IsArchiveEntry=true,AudioFormat="MP3",Size=DlnaCatalog.MaximumInflatedTrackBytes+1,CompressionMethod=8,IsMp3Valid=true,SampleRate=44100,BitrateKbps=128,Duration=TimeSpan.FromSeconds(1)};
        Check(!DlnaCatalog.CanShare(tooLarge),"compressed-track 128 MiB cap enforced before advertising");
        var root=catalog.Browse("0",false,0,0,"","*","http://127.0.0.1:1234/");var rootXml=XDocument.Parse(root.Xml);
        Check(root.Total==3&&root.Returned==3&&rootXml.Descendants(DlnaCatalog.Didl+"container").Count()==3,"root exposes albums, artists and all tracks");
        var page=catalog.Browse("a1",false,1,1,"","*","http://127.0.0.1:1234/");Check(page.Returned==1&&page.Total==3&&XDocument.Parse(page.Xml).Descendants(DlnaCatalog.Dc+"title").Single().Value=="Stored","Browse paging retains disc/track order");
        var meta=XDocument.Parse(catalog.Browse("t1",true,0,0,"","*","http://127.0.0.1:1234/").Xml);
        Check(meta.Descendants(DlnaCatalog.Dc+"title").Single().Value==tracks[0].Title&&meta.Descendants(DlnaCatalog.Upnp+"album").Single().Value==album.Title,"DIDL escapes XML and preserves Japanese metadata");
        Check(meta.Descendants(DlnaCatalog.Didl+"res").Single().Attribute("size")!.Value=="10000"&&!meta.ToString().Contains(folder),"resource URLs are opaque IDs, not local paths");
        Check(XDocument.Parse(catalog.Browse("t1",true,0,0,"","","http://127.0.0.1/").Xml).Descendants(DlnaCatalog.Didl+"res").Count()==0,"empty Filter returns required metadata only");
        Check(XDocument.Parse(catalog.Browse("t1",true,0,0,"","res@size","http://127.0.0.1/").Xml).Descendants(DlnaCatalog.Didl+"res").Single().Attribute("size") is not null,"resource attribute Filter includes required protocolInfo");
        Check(XDocument.Parse(catalog.Browse("all-t1",true,0,0,"","*","http://127.0.0.1/").Xml).Descendants(DlnaCatalog.Didl+"item").Single().Attribute("refID")?.Value=="t1","all-track references point to canonical album item");
        void Fault(int code,Action action){try{action();throw new Exception("Missing fault");}catch(DlnaFault ex){Check(ex.Code==code,"UPnP fault "+code);}}
        Fault(701,()=>catalog.Browse("missing",false,0,0,"","*","http://127.0.0.1/"));Fault(710,()=>catalog.Browse("t1",false,0,0,"","*","http://127.0.0.1/"));Fault(709,()=>catalog.Browse("a1",false,0,0,"+invalid","*","http://127.0.0.1/"));
        Fault(402,()=>catalog.Browse("a1",false,0,0,"",new string('*',1025),"http://127.0.0.1/"));
        Check(catalog.Browse("a1",false,uint.MaxValue,0,"","*","http://127.0.0.1/").Returned==0,"large Browse index does not wrap");
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{_=new DlnaCatalog([album],canceled.Token);throw new Exception("Catalog cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS library preparation cancellation");}}
        var large=new DlnaCatalog([new(new ZipAlbum{Path=folder,Tracks=Enumerable.Range(1,2001).Select(i=>Track(direct,"Track "+i,i)).ToArray()},"Large","Artist")]);
        Check(large.Browse("tracks",false,0,0,"","*","http://127.0.0.1/").Returned==3&&large.Browse("group-tracks-1",false,0,0,"","*","http://127.0.0.1/").Returned==1000,"large library grouped for players requesting all items at once");
        foreach(bool cd in new[]{true,false})
        {
            var scpd=XDocument.Parse(DlnaProtocol.Scpd(cd));XNamespace ns="urn:schemas-upnp-org:service-1-0";var states=scpd.Descendants(ns+"stateVariable").Select(v=>v.Element(ns+"name")!.Value).ToHashSet();
            Check(scpd.Descendants(ns+"relatedStateVariable").All(s=>states.Contains(s.Value)),"all "+(cd?"ContentDirectory":"ConnectionManager")+" SCPD arguments reference declared states");
        }
        var lan=new DlnaEndpoint(IPAddress.Parse("192.168.1.4"),IPAddress.Parse("255.255.255.0"),"test");
        Check(lan.Allows(IPAddress.Parse("192.168.1.9"))&&!lan.Allows(IPAddress.Parse("192.168.2.9"))&&!lan.Allows(IPAddress.Parse("8.8.8.8")),"server restricts peers to selected private subnet");
        Check(!DlnaServer.SafeCallback("<http://example.com/>",IPAddress.Loopback,out _)&&!DlnaServer.SafeCallback("<http://169.254.169.254/>",IPAddress.Loopback,out _)&&!DlnaServer.SafeCallback("<http://127.0.0.1@8.8.8.8/>",IPAddress.Loopback,out _),"event callback blocks DNS, other hosts and credentials");
        const string uuid="74b090cc-8d62-49e4-b267-80e26e21bcd5";
        string waveFolder=Path.Combine(folder,"wave");Directory.CreateDirectory(waveFolder);string wavePath=Path.Combine(waveFolder,"01.wav");
        using(var writer=new NAudio.Wave.WaveFileWriter(wavePath,new NAudio.Wave.WaveFormat(44100,16,2)))writer.Write(new byte[176400],0,176400);
        var waveAlbum=new DlnaAlbum(ZipAlbumReader.OpenFolder(waveFolder),"WAV album","Test artist");
        Check(waveAlbum.Album.Tracks.Count==1&&DlnaCatalog.CanShare(waveAlbum.Album.Tracks[0]),"production library reader WAV is shareable");
        using var server=new DlnaServer(new(IPAddress.Loopback,IPAddress.Parse("255.0.0.0"),"loopback"),[album,waveAlbum],uuid,discover:true,discoveryPort:0,updateId:42);
        using var http=new HttpClient(new SocketsHttpHandler{UseProxy=false}){BaseAddress=new Uri(server.Address),Timeout=TimeSpan.FromSeconds(10)};
        var device=XDocument.Parse(await http.GetStringAsync("device.xml"));XNamespace devNs="urn:schemas-upnp-org:device-1-0";
        Check(device.Descendants(devNs+"UDN").Single().Value=="uuid:"+uuid&&device.Descendants(devNs+"service").Count()==2,"HTTP device description and UUID");
        foreach(var path in new[]{"cd/scpd.xml","cm/scpd.xml"})Check(XDocument.Parse(await http.GetStringAsync(path)).Root is not null,"HTTP "+path);
        async Task<HttpResponseMessage> Soap(string action,string arguments="",string service=DlnaProtocol.Cd)
        {
            var message=new HttpRequestMessage(HttpMethod.Post,service==DlnaProtocol.Cd?"cd/control":"cm/control");message.Headers.TryAddWithoutValidation("SOAPACTION",'"'+service+"#"+action+'"');
            message.Content=new StringContent($"<s:Envelope xmlns:s=\"{DlnaProtocol.Soap}\"><s:Body><u:{action} xmlns:u=\"{service}\">{arguments}</u:{action}></s:Body></s:Envelope>",Encoding.UTF8,"text/xml");return await http.SendAsync(message);
        }
        using(var response=await Soap("Browse","<ObjectID>a1</ObjectID><BrowseFlag>BrowseDirectChildren</BrowseFlag><Filter>*</Filter><StartingIndex>0</StartingIndex><RequestedCount>0</RequestedCount><SortCriteria></SortCriteria>"))
        {var body=XDocument.Parse(await response.Content.ReadAsStringAsync());Check(response.IsSuccessStatusCode&&XDocument.Parse(body.Descendants("Result").Single().Value).Descendants(DlnaCatalog.Didl+"item").Count()==3,"SOAP Browse returns nested escaped DIDL");}
        using(var response=await Soap("GetProtocolInfo",service:DlnaProtocol.Cm))Check((await response.Content.ReadAsStringAsync()).Contains("audio/mpeg")&&response.IsSuccessStatusCode,"ConnectionManager advertises actual audio MIME");
        using(var response=await Soap("GetSystemUpdateID"))Check(XDocument.Parse(await response.Content.ReadAsStringAsync()).Descendants("Id").Single().Value=="42","SystemUpdateID reflects new sharing session");
        using(var response=await Soap("GetCurrentConnectionInfo","<ConnectionID>0</ConnectionID>",DlnaProtocol.Cm))Check(response.IsSuccessStatusCode,"ConnectionManager connection info");
        using(var response=await Soap("NotAnAction"))Check(response.StatusCode==HttpStatusCode.InternalServerError&&(await response.Content.ReadAsStringAsync()).Contains("401"),"unknown SOAP action has UPnP fault");
        using(var message=new HttpRequestMessage(HttpMethod.Post,"cd/control"))
        {message.Headers.TryAddWithoutValidation("SOAPACTION",'"'+DlnaProtocol.Cd+"#Browse\"");message.Content=new StringContent("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///C:/Windows/win.ini'>]><x>&leak;</x>");using var response=await http.SendAsync(message);Check(response.StatusCode==HttpStatusCode.InternalServerError&&(await response.Content.ReadAsStringAsync()).Contains("402"),"SOAP DTD and external entity blocked");}
        foreach(var id in new[]{"t1","t2","t3"})
        {
            using var head=new HttpRequestMessage(HttpMethod.Head,"media/"+id);using var response=await http.SendAsync(head);Check(response.Content.Headers.ContentLength==data.Length&&response.Content.Headers.ContentType?.MediaType=="audio/mpeg","HEAD "+id+" metadata without streaming");
            Check((await http.GetByteArrayAsync("media/"+id)).SequenceEqual(data),"GET exact bytes "+id+" (plain / stored / deflated)");
            using var partial=new HttpRequestMessage(HttpMethod.Get,"media/"+id);partial.Headers.Range=new RangeHeaderValue(101,199);using var range=await http.SendAsync(partial);
            Check(range.StatusCode==HttpStatusCode.PartialContent&&(await range.Content.ReadAsByteArrayAsync()).SequenceEqual(data[101..200])&&range.Content.Headers.ContentRange?.From==101,"byte seeking "+id);
        }
        Check((await http.GetByteArrayAsync("media/t4")).SequenceEqual(File.ReadAllBytes(wavePath)),"valid PCM WAV container delivered without conversion");
        foreach(var rangeText in new[]{"bytes=-17","bytes=9997-"})
        {using var message=new HttpRequestMessage(HttpMethod.Get,"media/t1");message.Headers.TryAddWithoutValidation("Range",rangeText);using var response=await http.SendAsync(message);int size=rangeText=="bytes=-17"?17:3;Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(data[^size..]),"suffix / open-ended range "+rangeText);}
        foreach(var rangeText in new[]{"bytes=10000-","bytes=10-1","bytes=0-1,4-6","bytes=9223372036854775808-","items=0-3"})
        {using var message=new HttpRequestMessage(HttpMethod.Get,"media/t1");message.Headers.TryAddWithoutValidation("Range",rangeText);using var response=await http.SendAsync(message);Check((int)response.StatusCode==416&&response.Content.Headers.ContentRange?.Length==data.Length,"invalid byte range rejected "+rangeText);}
        using(var response=await http.GetAsync("media/../../outside"))Check(response.StatusCode==HttpStatusCode.NotFound,"URL traversal cannot expose files");
        Check((await http.GetByteArrayAsync("art/a1.jpg")).SequenceEqual(new byte[]{255,216,255,217}),"album art endpoint");
        using(var response=await http.GetAsync("art/missing.jpg"))Check(response.StatusCode==HttpStatusCode.NotFound,"missing art is 404");
        var callback=new TcpListener(IPAddress.Loopback,0);callback.Start();int callbackPort=((IPEndPoint)callback.LocalEndpoint).Port;
        var eventTask=Task.Run(async()=>
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));using var client=await callback.AcceptTcpClientAsync(deadline.Token);using var stream=client.GetStream();var header=new List<byte>();var one=new byte[1];
            while(true){await stream.ReadExactlyAsync(one,deadline.Token);header.Add(one[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;if(header.Count>16384)throw new Exception("oversized event");}
            string text=Encoding.ASCII.GetString(header.ToArray());int size=int.Parse(text.Split("\r\n").Single(l=>l.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1]);var body=new byte[size];await stream.ReadExactlyAsync(body,deadline.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),deadline.Token);return(text,Encoding.UTF8.GetString(body));
        });
        string sid;
        using(var request=new HttpRequestMessage(new HttpMethod("SUBSCRIBE"),"cd/event"))
        {request.Headers.TryAddWithoutValidation("CALLBACK",$"<http://127.0.0.1:{callbackPort}/events>");request.Headers.TryAddWithoutValidation("NT","upnp:event");request.Headers.TryAddWithoutValidation("TIMEOUT","Second-60");using var response=await http.SendAsync(request);Check(response.IsSuccessStatusCode,"GENA subscription accepted");sid=response.Headers.GetValues("SID").Single();}
        var received=await eventTask;callback.Stop();Check(received.Item1.StartsWith("NOTIFY ")&&received.Item1.Contains("SEQ: 0")&&XDocument.Parse(received.Item2).Descendants("SystemUpdateID").Single().Value=="42","initial GENA event reports snapshot update ID");
        using(var request=new HttpRequestMessage(new HttpMethod("SUBSCRIBE"),"cd/event")){request.Headers.TryAddWithoutValidation("SID",sid);using var response=await http.SendAsync(request);Check(response.IsSuccessStatusCode,"GENA renewal");}
        using(var request=new HttpRequestMessage(new HttpMethod("UNSUBSCRIBE"),"cd/event")){request.Headers.TryAddWithoutValidation("SID",sid);using var response=await http.SendAsync(request);Check(response.IsSuccessStatusCode,"GENA unsubscribe");}
        using(var request=new HttpRequestMessage(new HttpMethod("SUBSCRIBE"),"cd/event")){request.Headers.TryAddWithoutValidation("CALLBACK","<http://8.8.8.8/>");request.Headers.TryAddWithoutValidation("NT","upnp:event");using var response=await http.SendAsync(request);Check((int)response.StatusCode==412,"remote event callback rejected before outbound request");}
        using(var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
        {
            const string search="M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n";
            Check(DlnaDiscovery.Search(search) is not null&&DlnaDiscovery.Search(search.Replace("MX: 1","MX: -1")) is null,"SSDP search validation");
            await udp.SendAsync(Encoding.ASCII.GetBytes(search),new IPEndPoint(IPAddress.Loopback,server.DiscoveryPort));using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var targets=new HashSet<string>();for(int i=0;i<5;i++){var packet=await udp.ReceiveAsync(timeout.Token);string text=Encoding.ASCII.GetString(packet.Buffer);Check(text.Contains("LOCATION: "+server.Address+"device.xml"),"SSDP response advertises correct HTTP interface");targets.Add(text.Split("\r\n").Single(l=>l.StartsWith("ST:")).Substring(4));}
            Check(targets.SetEquals(DlnaDiscovery.Targets(uuid)),"SSDP advertises root, UUID, device and both services");
        }
        File.SetLastWriteTimeUtc(direct,DateTime.UtcNow.AddMinutes(1));using(var response=await http.GetAsync("media/t1"))Check(response.StatusCode==HttpStatusCode.InternalServerError,"changed shared file refused instead of stale bytes");
        var resource=catalog.FindResource("t3")!;using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{using var input=await resource.Open(cancel.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){Console.WriteLine("PASS canceled ZIP inflation");}}
        string temp=Path.Combine(Path.GetTempPath(),"ZipMp3Player","Dlna");Check(!Directory.Exists(temp)||!Directory.EnumerateFiles(temp).Any(),"inflated temporary files removed after responses/cancellation");
        int serverPort=server.Port;server.Dispose();using(var client=new TcpClient()){try{await client.ConnectAsync(IPAddress.Loopback,serverPort);throw new Exception("server still open");}catch(SocketException){Console.WriteLine("PASS server stops HTTP listener");}}
        Console.WriteLine("Fixtures: "+folder+" (loopback only; no real LAN sharing)");
    }
}
