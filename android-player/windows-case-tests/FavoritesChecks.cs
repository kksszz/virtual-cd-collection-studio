using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using ZipMp3Player;

internal static class FavoritesChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"vcd-favorites-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(root,"settings"));
        var app=new Application();var window=new MainWindow();
        var path=Path.Combine(root,"music");Directory.CreateDirectory(path);
        var audio=Path.Combine(path,"song.flac");File.WriteAllBytes(audio,[1,2,3]);
        var tracks=new List<ZipTrack>{
            new(){SourcePath=audio,FileName="disc track 1",Title="日本語",TrackNumber=1,CuePath=Path.Combine(path,"disc.cue")},
            new(){SourcePath=audio,FileName="disc track 2",Title="曲2",TrackNumber=2,CuePath=Path.Combine(path,"disc.cue")}
        };
        var album=new ZipAlbum{Path=path,Tracks=tracks};
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var favorites=typeof(MainWindow).GetField("_favoritesStore",flags)!.GetValue(window)!;
        favorites.GetType().GetMethod("ToggleAlbum")!.Invoke(favorites,[album]);
        favorites.GetType().GetMethod("ToggleTrack")!.Invoke(favorites,[tracks[1]]);
        var export=typeof(MainWindow).GetMethod("ExportMobileFavorites",flags)!;
        var snapshot=(JsonObject)export.Invoke(window,[album])!;
        if(!snapshot["album"]!.GetValue<bool>()||snapshot["tracks"]![0]!["favorite"]!.GetValue<bool>()
            ||!snapshot["tracks"]![1]!["favorite"]!.GetValue<bool>()
            ||snapshot["tracks"]![1]!["file"]!.GetValue<string>()!="song.flac · Track 2")
            throw new Exception("Favorite export/CUE mapping");
        var glb=Path.Combine(root,"case.glb");File.WriteAllBytes(glb,[1]);
        var target=Path.Combine(root,"target");
        var sync=MobileSync.Open(new MobileSync.FolderTarget(target)).GetAwaiter().GetResult();
        sync.Send(path,"Album","Artist",glb,favorites:snapshot).GetAwaiter().GetResult();
        JsonObject Record()=>JsonNode.Parse(File.ReadAllText(Path.Combine(target,"vcd-sync.json")))!["albums"]!.AsObject().First().Value!.AsObject();
        var before=Record();var music=before["music"]!.GetValue<string>();
        if(!before["favorites"]!["album"]!.GetValue<bool>())throw new Exception("Manifest favorites missing");
        favorites.GetType().GetMethod("ToggleAlbum")!.Invoke(favorites,[album]);
        snapshot=(JsonObject)export.Invoke(window,[album])!;
        sync.Send(path,"Album","Artist",glb,favorites:snapshot).GetAwaiter().GetResult();
        var after=Record();
        if(after["favorites"]!["album"]!.GetValue<bool>()||music!=after["music"]!.GetValue<string>())
            throw new Exception("Favorite-only change lost or music recopied");
        window.Close();
        Console.WriteLine("PASS favorites export: album, track true/false, CUE keys, manifest, favorite-only update");
    }
}
