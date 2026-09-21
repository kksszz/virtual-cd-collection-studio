using System.IO;
using System.Text.Json;
namespace ZipMp3Player;

internal sealed class AlbumAddedStore
{
    private readonly string path;
    private readonly Dictionary<string,long> dates;
    private bool dirty;
    public AlbumAddedStore(string path)
    {
        this.path=path;
        try { dates=File.Exists(path)
            ? new(JsonSerializer.Deserialize<Dictionary<string,long>>(File.ReadAllText(path)) ?? [],StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
        {
            dates=new(StringComparer.OrdinalIgnoreCase);
            // Preserve malformed history before starting a replacement.
            if(File.Exists(path))try { File.Copy(path,path+".backup-"+Guid.NewGuid().ToString("N")); } catch { }
        }
    }
    public long Get(string album)=>dates.GetValueOrDefault(Path.GetFullPath(album));
    public void Observe(string album,long timestamp)
    {
        if(dates.TryAdd(Path.GetFullPath(album),timestamp))dirty=true;
    }
    public void Save()
    {
        if(!dirty)return;
        try {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(dates));
        File.Move(temporary,path,true);dirty=false;
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {
            System.Diagnostics.Trace.WriteLine("Album added history could not be saved: "+ex.Message);
        }
    }
}
