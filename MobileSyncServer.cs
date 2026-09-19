using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace ZipMp3Player;
/// <summary>Temporary read-only LAN endpoint. Only committed manifest files are exposed, behind a random bearer URL.</summary>
public sealed class MobileSyncServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop=new();
    private readonly string root,token=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private readonly SemaphoreSlim clients=new(4);
    private HashSet<string> selected=new(StringComparer.Ordinal);
    private readonly object statusGate=new();
    private string transferStatus="接続待ち — XperiaでQRコードを読み取ってください";
    private DateTime lastReport;
    private bool completed;
    public string TransferStatus {get{lock(statusGate)return lastReport!=default&&!completed&&DateTime.UtcNow-lastReport>TimeSpan.FromSeconds(45)?"端末からの応答待ち（完了は未確認）\n"+transferStatus:transferStatus;}}
    public void Select(IEnumerable<string> ids){selected=new HashSet<string>(ids,StringComparer.Ordinal);lock(statusGate){transferStatus="接続待ち — XperiaでQRコードを読み取ってください";lastReport=default;completed=false;}}
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public string Address=>"http://"+(System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up).OrderByDescending(n=>n.GetIPProperties().GatewayAddresses.Count>0)
        .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)&&IsPrivate(a))?.ToString()??"127.0.0.1")+":"+Port+"/"+token+"/";
    private static bool IsPrivate(IPAddress ip){var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31);}
    public MobileSyncServer(string folder){root=Path.GetFullPath(folder);listener=new TcpListener(IPAddress.Any,0);listener.Start();_=Accept();}
    private async Task Accept(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);if(!clients.Wait(0)){client.Dispose();continue;}_=Serve(client);}}catch(OperationCanceledException){}catch(SocketException) when(stop.IsCancellationRequested){}}
    private async Task Serve(TcpClient client)
    {
        try{
            if(client.Client.RemoteEndPoint is not IPEndPoint peer||(!IsPrivate(peer.Address)&&!IPAddress.IsLoopback(peer.Address)))return;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromMinutes(30));var ct=deadline.Token;
            using var stream=client.GetStream();var header=new List<byte>();var one=new byte[1];
            using(var headers=CancellationTokenSource.CreateLinkedTokenSource(ct)){headers.CancelAfter(TimeSpan.FromSeconds(10));while(header.Count<8192){if(await stream.ReadAsync(one,headers.Token)==0)return;header.Add(one[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}}
            var headerLines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var line=headerLines[0].Split(' ');if(line.Length!=3||(line[0]!="GET"&&line[0]!="POST"))return;
            string prefix="/"+token+"/";if(!line[1].StartsWith(prefix,StringComparison.Ordinal))return;
            string relative=Uri.UnescapeDataString(line[1][prefix.Length..]);MobileSync.Safe(relative);
            byte[] manifest=File.ReadAllBytes(Path.Combine(root,"vcd-sync.json"));var allowed=new HashSet<string>(StringComparer.Ordinal){"vcd-sync.json"};
            var json=JsonNode.Parse(manifest)!;var albums=json["albums"]!.AsObject();var selection=selected;
            foreach(var id in albums.Select(a=>a.Key).Where(id=>!selection.Contains(id)).ToArray())albums.Remove(id);
            manifest=Encoding.UTF8.GetBytes(json.ToJsonString());foreach(var record in albums)foreach(var file in record.Value!["files"]!.AsArray())allowed.Add((string)file!["path"]!);
            if(line[0]=="POST"){
                if(relative!="sync-status")return;
                var lengthHeaders=headerLines.Where(h=>h.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(lengthHeaders.Length!=1||!int.TryParse(lengthHeaders[0].Split(':',2)[1].Trim(),out var length)||length<1||length>4096)return;
                var payload=new byte[length];using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));await stream.ReadExactlyAsync(payload,timeout.Token);
                var report=System.Text.Json.JsonDocument.Parse(payload).RootElement;
                var expected=Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
                if(report.GetProperty("manifest").GetString()!=expected)return;
                var message=report.GetProperty("message").GetString()??"";if(message.Length>1200)return;
                bool done=report.GetProperty("complete").GetBoolean();
                lock(statusGate){transferStatus=(done?"同期完了 — 端末で保存・検証済み\n":"Xperiaからの受信状況\n")+message;completed=done;lastReport=DateTime.UtcNow;}
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),ct);return;
            }
            if(!allowed.Contains(relative))return;
            if(relative=="vcd-sync.json"){await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {manifest.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n"),ct);await stream.WriteAsync(manifest,ct);}
            else{using var file=File.OpenRead(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {file.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n"),ct);await file.CopyToAsync(stream,ct);}
        }catch(Exception ex)when(ex is IOException or SocketException or OperationCanceledException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException){}finally{client.Dispose();clients.Release();}
    }
    public void Dispose(){stop.Cancel();listener.Stop();}
}
