using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZipMp3Player;

/// <summary>Immutable payloads, a small atomic commit manifest, and no deletion of user files.</summary>
public sealed class MobileSync
{
    public interface ITarget
    {
        Task<byte[]?> Read(string relative);
        Task Put(string local, string relative, string sha256);
        Task Commit(byte[] bytes);
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string FileHash(string file) { using var stream=File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static string Safe(string relative)
    {
        if(string.IsNullOrEmpty(relative)||relative.StartsWith('/')||relative.Contains('\\')||relative.Contains(':')||relative.Any(char.IsControl)||relative.Split('/').Any(x=>x is "" or "." or ".."))throw new IOException("不正な同期パスです");
        return relative;
    }
    public sealed class FolderTarget(string root) : ITarget
    {
        public string Root=>Path.GetFullPath(root);
        string PathFor(string relative)=>Path.Combine(Path.GetFullPath(root),Safe(relative).Replace('/',Path.DirectorySeparatorChar));
        public Task<byte[]?> Read(string relative)=>Task.FromResult(File.Exists(PathFor(relative))?File.ReadAllBytes(PathFor(relative)):null);
        public Task Put(string local,string relative,string hash)
        {
            var path=PathFor(relative);if(File.Exists(path)&&FileHash(path)==hash)return Task.CompletedTask;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".partial";
            try{File.Copy(local,temp);if(FileHash(temp)!=hash)throw new IOException("転送検証に失敗しました");File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}return Task.CompletedTask;
        }
        public Task Commit(byte[] bytes){Directory.CreateDirectory(root);var temp=PathFor("vcd-sync-"+Guid.NewGuid().ToString("N")+".partial");try{File.WriteAllBytes(temp,bytes);File.Move(temp,PathFor("vcd-sync.json"),true);}finally{if(File.Exists(temp))File.Delete(temp);}return Task.CompletedTask;}
    }
    public sealed class AdbTarget(string adb,string serial,string root) : ITarget
    {
        static string Quote(string s)=>"'"+s.Replace("'","'\\''")+"'";
        string Remote(string relative)=>root.TrimEnd('/')+"/"+Safe(relative);
        async Task<(int Code,string Output)> Run(params string[] args)
        {
            var info=new ProcessStartInfo(adb){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("-s");info.ArgumentList.Add(serial);foreach(var arg in args)info.ArgumentList.Add(arg);
            using var p=Process.Start(info)??throw new IOException("ADBを起動できません");var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(30));try{await p.WaitForExitAsync(timeout.Token);}catch{p.Kill(true);throw;}
            return(p.ExitCode,(await output)+(await error));
        }
        async Task Checked(params string[] args){var r=await Run(args);if(r.Code!=0)throw new IOException("ADB転送に失敗しました: "+r.Output);}
        public async Task<byte[]?> Read(string relative){var r=await Run("shell","if [ -f "+Quote(Remote(relative))+" ]; then cat "+Quote(Remote(relative))+"; fi");if(r.Code!=0)throw new IOException(r.Output);return r.Output.Length==0?null:Encoding.UTF8.GetBytes(r.Output);}
        public async Task Put(string local,string relative,string hash)
        {
            if(!root.StartsWith("/storage/",StringComparison.Ordinal)&&!root.StartsWith("/sdcard/",StringComparison.Ordinal))throw new IOException("転送先はSDカードまたは共有ストレージ内を指定してください");
            var path=Remote(relative);var check=await Run("shell","sha256sum "+Quote(path));if(check.Code==0&&check.Output.StartsWith(hash,StringComparison.OrdinalIgnoreCase))return;
            var temp=path+"."+Guid.NewGuid().ToString("N")+".partial";
            await Checked("shell","mkdir -p "+Quote(path[..path.LastIndexOf('/')]));
            await Checked("push",local,temp);
            check=await Run("shell","sha256sum "+Quote(temp));if(check.Code!=0||!check.Output.StartsWith(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("端末側のSHA-256検証に失敗しました。未完成ファイルは反映しません。");
            await Checked("shell","mv "+Quote(temp)+" "+Quote(path));
        }
        public async Task Commit(byte[] bytes)
        {
            var local=Path.GetTempFileName();try{File.WriteAllBytes(local,bytes);await Put(local,"vcd-sync.json",FileHash(local));}finally{File.Delete(local);}
        }
    }
    private readonly ITarget target;
    private readonly JsonObject albums;
    private MobileSync(ITarget target,JsonObject albums){this.target=target;this.albums=albums;}
    public static async Task<MobileSync> Open(ITarget target)
    {
        var bytes=await target.Read("vcd-sync.json");if(bytes is null)return new(target,new());
        var root=JsonNode.Parse(bytes)?.AsObject()??throw new IOException("同期索引が不正です");
        if((string?)root["format"]!="virtual-cd-sync"||(int?)root["version"]!=1)throw new IOException("未対応の同期索引です。上書きしません。");
        return new(target,root["albums"]?.AsObject()??throw new IOException("同期索引が不正です"));
    }
    public async Task Send(string source,string title,string artist,string glb,Action<string>? progress=null,string? lyrics=null,JsonObject? favorites=null)
    {
        source=Path.GetFullPath(source);var identitySource=source;
        if(CueAlbumReader.IsCue(source)){var disc=CueAlbumReader.Read(source);if(!disc.ImagePath.EndsWith(".flac",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("モバイル同期のCUEはFLAC形式に対応しています");source=Path.GetDirectoryName(source)!;}
        bool directory=Directory.Exists(source);
        if(directory&&target is FolderTarget local&&(local.Root.Equals(source,StringComparison.OrdinalIgnoreCase)||local.Root.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new IOException("転送先は元アルバムの外にしてください");
        if(!directory&&!File.Exists(source))throw new FileNotFoundException("音源がありません",source);
        var id=Hash(identitySource.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant());
        var files=directory?Directory.EnumerateFiles(source,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}).Order(StringComparer.Ordinal).ToArray():new[]{source};
        if(files.Length==0)throw new IOException("空のアルバムです");
        var inventory=new List<(string Local,string Relative,string Hash,long Size)>();
        foreach(var file in files){progress?.Invoke("検証: "+Path.GetFileName(file));var relative=directory?Path.GetRelativePath(source,file).Replace('\\','/'):Path.GetFileName(source);Safe(relative);inventory.Add((file,relative,FileHash(file),new FileInfo(file).Length));}
        var revision=Hash(string.Join("\n",inventory.Select(f=>f.Relative+"|"+f.Size+"|"+f.Hash)));
        var prefix=".vcd-sync/"+id+"/"+revision+"/";
        long total=inventory.Sum(f=>f.Size)+new FileInfo(glb).Length+(lyrics is null?0:new FileInfo(lyrics).Length),done=0;int completed=0;
        void Report(string name)=>progress?.Invoke($"このアルバムの配置（既存再利用を含む）: {(total==0?100:done*100d/total):F0}% · {done/1048576d:F1} / {total/1048576d:F1} MiB · {completed}/{inventory.Count+1+(lyrics is null?0:1)}ファイル\n{name}");
        foreach(var file in inventory){Report(file.Relative);await target.Put(file.Local,prefix+file.Relative,file.Hash);done+=file.Size;completed++;Report(file.Relative);}
        var glbHash=FileHash(glb);var glbPath=".vcd-sync/"+id+"/"+glbHash+".glb";Report("3Dデータ");await target.Put(glb,glbPath,glbHash);done+=new FileInfo(glb).Length;completed++;Report("配置済み・同期情報を作成中");
        var record=new JsonObject{["id"]=id,["title"]=title,["artist"]=artist,["name"]=Path.GetFileName(source),["directory"]=directory,["music"]=directory?prefix.TrimEnd('/'):prefix+inventory[0].Relative,["revision"]=revision,["size"]=inventory.Sum(f=>f.Size),["glb"]=glbPath,["glbSha256"]=glbHash};
        static bool Audio(string path)=>new[]{".mp3",".flac",".wav",".m4a",".aac",".ogg",".opus",".wma",".aiff",".aif"}.Contains(Path.GetExtension(path).ToLowerInvariant());
        var audioHashes=new List<string>();
        if(directory)audioHashes.AddRange(inventory.Where(f=>Audio(f.Relative)).Select(f=>f.Hash));
        else if(Audio(source)&&!source.EndsWith(".zip.mp3",StringComparison.OrdinalIgnoreCase))audioHashes.Add(inventory[0].Hash);
        else{using var zip=System.IO.Compression.ZipFile.OpenRead(source);foreach(var entry in zip.Entries.Where(e=>Audio(e.FullName))){using var input=entry.Open();audioHashes.Add(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());}}
        record["audioFingerprint"]=Hash(string.Join("\n",audioHashes.Order(StringComparer.Ordinal)));
        record["audioCount"]=audioHashes.Count;
        record["files"]=new JsonArray(inventory.Select(f=>(JsonNode)new JsonObject{["path"]=prefix+f.Relative,["sha256"]=f.Hash,["size"]=f.Size}).Append(new JsonObject{["path"]=glbPath,["sha256"]=glbHash,["size"]=new FileInfo(glb).Length}).ToArray());
        if(lyrics is not null){
            var lyricsHash=FileHash(lyrics);var lyricsPath=".vcd-sync/"+id+"/"+lyricsHash+".lyrics.json";
            progress?.Invoke("歌詞を配置しています");await target.Put(lyrics,lyricsPath,lyricsHash);
            done+=new FileInfo(lyrics).Length;completed++;Report("歌詞を配置しました");
            record["lyrics"]=lyricsPath;record["lyricsSha256"]=lyricsHash;
            record["files"]!.AsArray().Add(new JsonObject{["path"]=lyricsPath,["sha256"]=lyricsHash,["size"]=new FileInfo(lyrics).Length});
        }
        if(favorites is not null) record["favorites"]=favorites.DeepClone();
        albums[id]=record;
        var manifest=new JsonObject{["format"]="virtual-cd-sync",["version"]=1,["albums"]=albums.DeepClone()};
        await target.Commit(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }
}
