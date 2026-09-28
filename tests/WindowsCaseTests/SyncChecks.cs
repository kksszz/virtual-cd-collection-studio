using System.IO;
using System.Text.Json.Nodes;
using ZipMp3Player;

internal static class SyncChecks
{
    public static void Run(string output,string glb,bool serve)
    {
        Directory.CreateDirectory(output);string source=Path.Combine(output,"source","Sync Test"),target=Path.Combine(output,"target");Directory.CreateDirectory(source);
        using(var file=new BinaryWriter(File.Create(Path.Combine(source,"01-Silence.wav")))){file.Write("RIFF"u8);file.Write(36+16000);file.Write("WAVEfmt "u8);file.Write(16);file.Write((short)1);file.Write((short)1);file.Write(8000);file.Write(16000);file.Write((short)2);file.Write((short)16);file.Write("data"u8);file.Write(16000);file.Write(new byte[16000]);}
        var sync=MobileSync.Open(new MobileSync.FolderTarget(target)).GetAwaiter().GetResult();sync.Send(source,"Sync Test","Virtual CD",glb).GetAwaiter().GetResult();
        string id=MobileSync.Hash(Path.GetFullPath(source).ToUpperInvariant());var manifest=JsonNode.Parse(File.ReadAllText(Path.Combine(target,"vcd-sync.json")))!;
        string music=(string)manifest["albums"]![id]!["music"]!;var audio=Path.Combine(target,music.Replace('/',Path.DirectorySeparatorChar),"01-Silence.wav");var stamp=File.GetLastWriteTimeUtc(audio);
        sync.Send(source,"Sync Test","Virtual CD",glb).GetAwaiter().GetResult();if(File.GetLastWriteTimeUtc(audio)!=stamp)throw new Exception("Unchanged payload rewritten");
        foreach(string bad in new[]{"../escape","/absolute","C:/escape","a/../b","a\\b"}){bool rejected=false;try{MobileSync.Safe(bad);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Unsafe path accepted");}
        using var server=new MobileSyncServer(target);server.Select(new[]{id});using var client=new System.Net.Http.HttpClient();string local=server.Address.Replace(new Uri(server.Address).Host,"127.0.0.1");
        var wire=JsonNode.Parse(client.GetStringAsync(local+"vcd-sync.json").GetAwaiter().GetResult())!;if(wire["albums"]!.AsObject().Count!=1)throw new Exception("Manifest selection");
        foreach(var f in wire["albums"]![id]!["files"]!.AsArray()){string path=(string)f!["path"]!,hash=(string)f["sha256"]!;var bytes=client.GetByteArrayAsync(local+path).GetAwaiter().GetResult();if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()!=hash)throw new Exception("HTTP payload mismatch");}
        if(server.TransferStatus.Contains("同期完了"))throw new Exception("Sending alone must not complete sync");
        var manifestBytes=client.GetByteArrayAsync(local+"vcd-sync.json").GetAwaiter().GetResult();string digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes)).ToLowerInvariant();
        void Report(bool complete){using var body=new System.Net.Http.StringContent(new JsonObject{["manifest"]=digest,["message"]="50% · 10 / 20 MiB",["complete"]=complete}.ToJsonString(),System.Text.Encoding.UTF8,"application/json");using var response=client.PostAsync(local+"sync-status",body).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();}
        Report(false);if(!server.TransferStatus.Contains("50%")||server.TransferStatus.Contains("同期完了"))throw new Exception("Live status");
        Report(true);if(!server.TransferStatus.Contains("端末で保存・検証済み"))throw new Exception("Missing receiver acknowledgement");
        server.Select(Array.Empty<string>());wire=JsonNode.Parse(client.GetStringAsync(local+"vcd-sync.json").GetAwaiter().GetResult())!;if(wire["albums"]!.AsObject().Count!=0)throw new Exception("Unselected album exposed");server.Select(new[]{id});
        Console.WriteLine("PASS sync: immutable payloads, unchanged skip, path rejection, selected-only HTTP manifest and SHA-256 payloads");
        if(serve){Console.WriteLine("SYNC_ADDRESS="+server.Address);Console.Out.Flush();Task.Delay(TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();}
    }
}
