using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ZipMp3Player;

internal sealed class DlnaDiscovery : IDisposable
{
    private readonly UdpClient udp;private readonly DlnaEndpoint endpoint;private readonly string uuid,location;
    private readonly CancellationTokenSource stop=new();private readonly SemaphoreSlim searches=new(16);private readonly Action<string> report;
    private readonly IPEndPoint multicast=new(IPAddress.Parse("239.255.255.250"),1900);
    private int disposed;
    internal int Port=>((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    internal DlnaDiscovery(DlnaEndpoint endpoint,string uuid,string location,Action<string> report,int port=1900)
    {
        this.endpoint=endpoint;this.uuid=uuid;this.location=location;this.report=report;
        udp=new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.ExclusiveAddressUse=false;udp.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true);
            udp.Client.Bind(new IPEndPoint(IPAddress.IsLoopback(endpoint.Address)?endpoint.Address:IPAddress.Any,port));
            if(!IPAddress.IsLoopback(endpoint.Address))
            {
                udp.JoinMulticastGroup(multicast.Address,endpoint.Address);udp.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,endpoint.Address.GetAddressBytes());udp.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastTimeToLive,2);udp.MulticastLoopback=false;
                Announce(true);_=AnnounceLoop();
            }
            _=Receive();
        }
        catch{udp.Dispose();throw;}
    }
    internal static string[] Targets(string uuid)=>["upnp:rootdevice","uuid:"+uuid,DlnaProtocol.Device,DlnaProtocol.Cd,DlnaProtocol.Cm];
    private static string Usn(string uuid,string target)=>target=="uuid:"+uuid?target:"uuid:"+uuid+"::"+target;
    internal static string Response(string uuid,string target,string location)=>$"HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nDATE: {DateTime.UtcNow.ToString("r",CultureInfo.InvariantCulture)}\r\nEXT:\r\nLOCATION: {location}\r\nSERVER: Windows/10 UPnP/1.0 VirtualCDCollectionStudio/0.88\r\nST: {target}\r\nUSN: {Usn(uuid,target)}\r\n\r\n";
    internal static (string Target,int Delay)? Search(string packet)
    {
        if(packet.Length>8192)return null;var lines=packet.Split("\r\n");if(lines[0]!="M-SEARCH * HTTP/1.1")return null;
        var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1).Where(l=>l.Length>0)){int colon=line.IndexOf(':');if(colon<1||!fields.TryAdd(line[..colon],line[(colon+1)..].Trim()))return null;}
        if(fields.GetValueOrDefault("MAN")!="\"ssdp:discover\""||fields.GetValueOrDefault("HOST")!="239.255.255.250:1900"||!int.TryParse(fields.GetValueOrDefault("MX"),NumberStyles.None,CultureInfo.InvariantCulture,out int mx)||mx<1||!fields.TryGetValue("ST",out var target))return null;
        return(target,Math.Min(mx,5));
    }
    private async Task Receive()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var packet=await udp.ReceiveAsync(stop.Token);if(!endpoint.Allows(packet.RemoteEndPoint.Address)||packet.Buffer.Length>8192)continue;
                var search=Search(Encoding.ASCII.GetString(packet.Buffer));if(search is null||!searches.Wait(0))continue;
                _=Respond(search.Value,packet.RemoteEndPoint);
            }
        }
        catch(Exception ex)when(ex is OperationCanceledException or ObjectDisposedException){}catch(SocketException ex){if(!stop.IsCancellationRequested)report("DLNA discovery stopped: "+ex.Message);}
    }
    private async Task Respond((string Target,int Delay) search,IPEndPoint peer)
    {
        try
        {
            var targets=Targets(uuid).Where(t=>search.Target=="ssdp:all"||search.Target==t).ToArray();if(targets.Length==0)return;
            await Task.Delay(Random.Shared.Next(search.Delay*1000),stop.Token);
            foreach(var target in targets){var bytes=Encoding.ASCII.GetBytes(Response(uuid,target,location));await udp.SendAsync(bytes,peer,stop.Token);}
        }
        catch(Exception ex)when(ex is OperationCanceledException or SocketException or ObjectDisposedException){}finally{searches.Release();}
    }
    private void Announce(bool alive)
    {
        foreach(var target in Targets(uuid))
        {
            string headers=alive?$"CACHE-CONTROL: max-age=1800\r\nLOCATION: {location}\r\nSERVER: Windows/10 UPnP/1.0 VirtualCDCollectionStudio/0.88\r\n":"";
            var bytes=Encoding.ASCII.GetBytes($"NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nNT: {target}\r\nNTS: ssdp:{(alive?"alive":"byebye")}\r\nUSN: {Usn(uuid,target)}\r\n{headers}\r\n");udp.Send(bytes,bytes.Length,multicast);
        }
    }
    private async Task AnnounceLoop()
    {
        try{while(!stop.IsCancellationRequested){await Task.Delay(TimeSpan.FromMinutes(10),stop.Token);Announce(true);}}
        catch(Exception ex)when(ex is OperationCanceledException or ObjectDisposedException){}catch(SocketException ex){if(!stop.IsCancellationRequested)report("DLNA announcement failed: "+ex.Message);}
    }
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();if(!IPAddress.IsLoopback(endpoint.Address))try{Announce(false);}catch(SocketException){}udp.Dispose();}
}
