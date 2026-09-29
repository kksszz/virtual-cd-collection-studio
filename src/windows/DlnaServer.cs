using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace ZipMp3Player;

// Deliberately separate from QR/token-based Android sync: standard LAN discovery is not authenticated.
internal sealed class DlnaServer : IDisposable
{
    private readonly TcpListener listener;private readonly DlnaEndpoint endpoint;private readonly DlnaCatalog catalog;private readonly string uuid;
    private readonly CancellationTokenSource stop=new();private readonly SemaphoreSlim clients=new(4);private readonly DlnaDiscovery? discovery;
    private readonly SemaphoreSlim notifySlots=new(4);
    private readonly object gate=new();private readonly Dictionary<string,Subscription> subscriptions=[];
    private readonly HttpClient notify=new(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(3)};
    private int disposed,active;private long requests;private string error="";
    private readonly uint updateId;
    internal string Address=>"http://"+endpoint.Address+":"+Port+"/";
    internal int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    internal int DiscoveryPort=>discovery?.Port??0;
    internal int AlbumCount=>catalog.AlbumCount;internal int TrackCount=>catalog.TrackCount;internal int SkippedCount=>catalog.SkippedCount;
    internal string Status=>$"配信中 — {AlbumCount}アルバム / {TrackCount}曲 · 接続 {Volatile.Read(ref active)} · 要求 {Interlocked.Read(ref requests)}"+(Volatile.Read(ref error).Length>0?"\n"+Volatile.Read(ref error):"");
    internal DlnaServer(DlnaEndpoint endpoint,IEnumerable<DlnaAlbum> selected,string uuid,int port=0,bool discover=true,int discoveryPort=1900,uint updateId=1,CancellationToken cancellation=default)
    {
        if(!Guid.TryParseExact(uuid,"D",out _))throw new ArgumentException("Invalid device UUID");
        if(!DlnaEndpoint.Private(endpoint.Address)&&!IPAddress.IsLoopback(endpoint.Address))throw new ArgumentException("Select a private LAN interface");
        this.endpoint=endpoint;this.uuid=uuid;this.updateId=updateId;catalog=new(selected,cancellation);if(catalog.TrackCount==0)throw new InvalidDataException("共有できる曲がありません。CUE分割音源・CD-DA・未対応曲などは配信対象外です。");
        cancellation.ThrowIfCancellationRequested();
        listener=new TcpListener(endpoint.Address,port);
        try{listener.Start(16);if(discover)discovery=new(endpoint,uuid,Address+"device.xml",Report,discoveryPort);_=Accept();}
        catch{listener.Stop();notify.Dispose();throw;}
    }
    private void Report(string message)=>Volatile.Write(ref error,message);
    private async Task Accept()
    {
        try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);if(!clients.Wait(0)){client.Dispose();continue;}_=Serve(client);}}
        catch(Exception ex)when(ex is OperationCanceledException or ObjectDisposedException){}catch(SocketException ex){if(!stop.IsCancellationRequested)Report("配信の待ち受けを停止しました: "+ex.Message);}
    }
    private sealed record Request(string Method,string Path,Dictionary<string,string> Headers,byte[] Body);
    private async Task<Request> ReadRequest(NetworkStream stream,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var header=new List<byte>();var one=new byte[1];
        while(header.Count<16384){if(await stream.ReadAsync(one,timeout.Token)==0)throw new IOException("Incomplete request");header.Add(one[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
        if(header.Count>=16384)throw new DlnaFault(431,"Headers too large");
        var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var first=lines[0].Split(' ');
        if(first.Length!=3||first[2] is not ("HTTP/1.0" or "HTTP/1.1")||!first[1].StartsWith('/')||first[1].Contains('#'))throw new DlnaFault(400,"Invalid request");
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1).Where(l=>l.Length>0)){int colon=line.IndexOf(':');if(colon<1||line[..colon].Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||!headers.TryAdd(line[..colon],line[(colon+1)..].Trim()))throw new DlnaFault(400,"Duplicate or invalid header");}
        if(headers.ContainsKey("Transfer-Encoding"))throw new DlnaFault(400,"Chunked request bodies not supported");
        if(first[2]=="HTTP/1.1"&&!headers.ContainsKey("Host")||headers.TryGetValue("Host",out var host)&&host!=endpoint.Address+":"+Port)throw new DlnaFault(400,"Invalid Host");
        int length=0;if(headers.TryGetValue("Content-Length",out var value)&&(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out length)||length>262144))throw new DlnaFault(413,"Body too large");
        if(first[0]!="POST"&&length!=0)throw new DlnaFault(400,"Unexpected request body");
        var body=new byte[length];if(length>0)await stream.ReadExactlyAsync(body,timeout.Token);
        return new(first[0],first[1].Split('?',2)[0],headers,body);
    }
    private async Task Serve(TcpClient client)
    {
        Interlocked.Increment(ref active);bool sent=false;
        try
        {
            if(client.Client.RemoteEndPoint is not IPEndPoint peer||!endpoint.Allows(peer.Address))return;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromMinutes(30));var ct=deadline.Token;var stream=client.GetStream();
            var request=await ReadRequest(stream,ct);Interlocked.Increment(ref requests);
            async Task Reply(int status,byte[] bytes,string type="text/xml; charset=\"utf-8\"",Dictionary<string,string>? extra=null)
            {sent=true;await Headers(stream,status,bytes.Length,type,extra,ct);if(request.Method!="HEAD")await stream.WriteAsync(bytes,ct);}
            if(request.Method is "GET" or "HEAD")
            {
                string? xml=request.Path switch{"/device.xml"=>DlnaProtocol.DeviceXml(uuid,"Virtual CD Collection Studio"),"/cd/scpd.xml"=>DlnaProtocol.Scpd(true),"/cm/scpd.xml"=>DlnaProtocol.Scpd(false),_=>null};
                if(xml is not null){await Reply(200,Encoding.UTF8.GetBytes(xml));return;}
                if(request.Path.StartsWith("/media/",StringComparison.Ordinal))
                {
                    var resource=catalog.FindResource(request.Path[7..]);if(resource is null){await Reply(404,[]);return;}resource.CheckUnchanged();
                    (long Start,long End)? range;
                    try{range=DlnaProtocol.Range(request.Headers.GetValueOrDefault("Range"),resource.Length);}catch(DlnaFault){await Reply(416,[],extra:new(){["Content-Range"]="bytes */"+resource.Length});return;}
                    long start=range?.Start??0,end=range?.End??resource.Length-1,length=end-start+1;
                    var extra=new Dictionary<string,string>{["Accept-Ranges"]="bytes",["transferMode.dlna.org"]="Streaming",["contentFeatures.dlna.org"]=DlnaCatalog.Protocol(resource.Mime).Split(':',4)[3]};
                    if(range is not null)extra["Content-Range"]=$"bytes {start}-{end}/{resource.Length}";
                    if(request.Headers.ContainsKey("TimeSeekRange.dlna.org")){await Reply(406,[]);return;}
                    if(request.Method=="HEAD"){sent=true;await Headers(stream,range is null?200:206,length,resource.Mime,extra,ct);return;}
                    // At most four requests, each bounded to 128 MiB of inflated temporary storage.
                    using var input=await resource.Open(ct);
                    if(input.CanSeek)input.Position=start;
                    else{var skip=new byte[65536];long pending=start;while(pending>0){deadline.CancelAfter(TimeSpan.FromSeconds(60));int n=await input.ReadAsync(skip.AsMemory(0,(int)Math.Min(skip.Length,pending)),ct);if(n==0)throw new IOException("Shared ZIP entry truncated");pending-=n;}}
                    sent=true;await Headers(stream,range is null?200:206,length,resource.Mime,extra,ct);
                    var buffer=new byte[65536];while(length>0){deadline.CancelAfter(TimeSpan.FromSeconds(60));int n=await input.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,length)),ct);if(n==0)throw new IOException("Shared resource truncated");await stream.WriteAsync(buffer.AsMemory(0,n),ct);length-=n;}return;
                }
                if(request.Path.StartsWith("/art/",StringComparison.Ordinal)&&request.Path.EndsWith(".jpg",StringComparison.Ordinal))
                {var bytes=catalog.Cover(request.Path[5..^4]);await Reply(bytes is null?404:200,bytes??[],"image/jpeg");return;}
                await Reply(404,[]);return;
            }
            string? service=request.Path.StartsWith("/cd/",StringComparison.Ordinal)?DlnaProtocol.Cd:request.Path.StartsWith("/cm/",StringComparison.Ordinal)?DlnaProtocol.Cm:null;
            if(request.Method=="POST"&&service is not null&&(request.Path is "/cd/control" or "/cm/control"))
            {
                try{var xml=DlnaProtocol.Control(service,request.Headers.GetValueOrDefault("SOAPACTION", ""),new UTF8Encoding(false,true).GetString(request.Body),catalog,Address,updateId);await Reply(200,Encoding.UTF8.GetBytes(xml));}
                catch(DlnaFault ex){await Reply(500,Encoding.UTF8.GetBytes(DlnaProtocol.Fault(ex.Code,ex.Message)));}
                return;
            }
            if(service is not null&&(request.Path is "/cd/event" or "/cm/event")&&(request.Method is "SUBSCRIBE" or "UNSUBSCRIBE"))
            {
                var result=Subscribe(request,peer.Address,service);
                try{await Reply(result.Status,[],extra:result.Headers);}finally{if(result.Initial is not null)_=Notify(result.Initial);}return;
            }
            await Reply(405,[]);
        }
        catch(Exception ex)when(ex is SocketException or OperationCanceledException or ObjectDisposedException){}
        catch(Exception ex)
        {
            Report("DLNA要求を処理できませんでした: "+ex.Message);
            if(!sent)try{await Headers(client.GetStream(),ex is DlnaFault f?f.Code:500,0,"text/plain",null,stop.Token);}catch{}
        }
        finally{client.Dispose();Interlocked.Decrement(ref active);clients.Release();}
    }
    private static async Task Headers(NetworkStream stream,int status,long length,string type,Dictionary<string,string>? extra,CancellationToken ct)
    {
        string reason=status switch{200=>"OK",206=>"Partial Content",400=>"Bad Request",404=>"Not Found",405=>"Method Not Allowed",406=>"Not Acceptable",412=>"Precondition Failed",413=>"Content Too Large",416=>"Range Not Satisfiable",431=>"Request Header Fields Too Large",503=>"Service Unavailable",_=>"Internal Server Error"};
        var text=new StringBuilder($"HTTP/1.1 {status} {reason}\r\nContent-Length: {length}\r\nContent-Type: {type}\r\nConnection: close\r\nServer: Windows/10 UPnP/1.0 VirtualCDCollectionStudio/0.88\r\n");
        if(extra is not null)foreach(var pair in extra)text.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");text.Append("\r\n");await stream.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()),ct);
    }
    private sealed record Subscription(string Sid,Uri Callback,IPAddress Peer,string Service,DateTime Expires);
    internal static bool SafeCallback(string text,IPAddress peer,out Uri? callback)
    {
        callback=null;if(text.Length>2048||!text.StartsWith('<')||!text.EndsWith('>')||text[1..^1].Contains('<'))return false;
        if(!Uri.TryCreate(text[1..^1],UriKind.Absolute,out var uri)||uri.Scheme!="http"||uri.UserInfo.Length>0||uri.Fragment.Length>0||!IPAddress.TryParse(uri.Host,out var ip)||!ip.Equals(peer))return false;
        callback=uri;return true;
    }
    private (int Status,Dictionary<string,string>? Headers,Subscription? Initial) Subscribe(Request r,IPAddress peer,string service)
    {
        lock(gate)
        {
            foreach(var expired in subscriptions.Values.Where(s=>s.Expires<DateTime.UtcNow).ToArray())subscriptions.Remove(expired.Sid);
            string? sid=r.Headers.GetValueOrDefault("SID");
            if(r.Method=="UNSUBSCRIBE")
            {if(r.Headers.ContainsKey("CALLBACK")||r.Headers.ContainsKey("NT")||sid is null||!subscriptions.TryGetValue(sid,out var old)||!old.Peer.Equals(peer)||old.Service!=service)return(412,null,null);subscriptions.Remove(sid);return(200,null,null);}
            int seconds=1800;if(r.Headers.TryGetValue("TIMEOUT",out var time)&&time!="Second-infinite")
            {if(!time.StartsWith("Second-",StringComparison.OrdinalIgnoreCase)||!uint.TryParse(time[7..],NumberStyles.None,CultureInfo.InvariantCulture,out uint n)||n<1)return(412,null,null);seconds=(int)Math.Min(n,1800);}
            Subscription sub;bool initial=false;
            if(sid is not null)
            {
                if(r.Headers.ContainsKey("CALLBACK")||r.Headers.ContainsKey("NT")||!subscriptions.TryGetValue(sid,out var old)||!old.Peer.Equals(peer)||old.Service!=service)return(412,null,null);sub=old with{Expires=DateTime.UtcNow.AddSeconds(seconds)};
            }
            else
            {
                if(subscriptions.Count>=16)return(503,null,null);
                if(r.Headers.GetValueOrDefault("NT")!="upnp:event"||!SafeCallback(r.Headers.GetValueOrDefault("CALLBACK", ""),peer,out var callback))return(412,null,null);
                if(!notifySlots.Wait(0))return(503,null,null);
                sid="uuid:"+Guid.NewGuid().ToString("D");sub=new(sid,callback!,peer,service,DateTime.UtcNow.AddSeconds(seconds));initial=true;
            }
            subscriptions[sid]=sub;return(200,new(){["SID"]=sid,["TIMEOUT"]="Second-"+seconds},initial?sub:null);
        }
    }
    private async Task Notify(Subscription sub)
    {
        try
        {
            XNamespace ns="urn:schemas-upnp-org:event-1-0";
            XElement Property(string name,string value)=>new(ns+"property",new XElement(name,value));
            var values=sub.Service==DlnaProtocol.Cd?new[]{Property("SystemUpdateID",updateId.ToString(CultureInfo.InvariantCulture))}:new[]{Property("SourceProtocolInfo",catalog.SourceProtocolInfo),Property("SinkProtocolInfo",""),Property("CurrentConnectionIDs","0")};
            using var request=new HttpRequestMessage(new HttpMethod("NOTIFY"),sub.Callback){Content=new StringContent(new XElement(ns+"propertyset",new XAttribute(XNamespace.Xmlns+"e",ns),values).ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"text/xml")};
            request.Headers.TryAddWithoutValidation("NT","upnp:event");request.Headers.TryAddWithoutValidation("NTS","upnp:propchange");request.Headers.TryAddWithoutValidation("SID",sub.Sid);request.Headers.TryAddWithoutValidation("SEQ","0");
            using var response=await notify.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,stop.Token);if(!response.IsSuccessStatusCode)Report("イベント通知を機器が受け付けませんでした。");
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or ObjectDisposedException){}
        finally{notifySlots.Release();}
    }
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();discovery?.Dispose();listener.Stop();notify.Dispose();lock(gate)subscriptions.Clear();}
}
