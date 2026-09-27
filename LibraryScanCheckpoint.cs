using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZipMp3Player;

// Append-only completed albums: interruption can lose the last line, never the library.
internal sealed class LibraryScanCheckpoint
{
    private sealed record Entry(int Version,string Path,string Stamp,ZipAlbum Album);
    private static readonly object FileLock=new();
    private readonly string file;
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    internal LibraryScanCheckpoint(string file)
    {
        this.file=file;
        lock(FileLock)
        {
            try
            {
                if(!File.Exists(file)){
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                    using var marker=File.Open(file,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read);
                    return;
                }
                foreach(var line in File.ReadLines(file))
                    try{var entry=JsonSerializer.Deserialize<Entry>(line);if(entry?.Version==1)entries[entry.Path]=entry;}catch(JsonException){}
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    internal static string Stamp(string path,CancellationToken token)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var paths=Directory.Exists(path)
            ? Directory.EnumerateFiles(path,"*",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint}).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)
            : new[]{path}.AsEnumerable();
        foreach(var item in paths)
        {
            token.ThrowIfCancellationRequested();
            var info=new FileInfo(item);
            hash.AppendData(Encoding.UTF8.GetBytes($"{item}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal ZipAlbum? Find(string path,string stamp)=>entries.TryGetValue(path,out var entry)&&entry.Stamp==stamp?entry.Album:null;
    internal void Save(string path,string stamp,ZipAlbum album,CancellationToken token)
    {
        var entry=new Entry(1,path,stamp,album);
        if(entries.TryGetValue(path,out var previous)&&previous.Stamp==stamp)return;
        lock(FileLock)
        {
            token.ThrowIfCancellationRequested();
            try{
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                File.AppendAllText(file,"\n"+JsonSerializer.Serialize(entry,new JsonSerializerOptions{IgnoreReadOnlyProperties=true})+"\n");
                entries[path]=entry;
            }catch(IOException ex){System.Diagnostics.Debug.WriteLine(ex);}catch(UnauthorizedAccessException ex){System.Diagnostics.Debug.WriteLine(ex);}
        }
    }
    internal void Complete(){lock(FileLock){try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}}
}
