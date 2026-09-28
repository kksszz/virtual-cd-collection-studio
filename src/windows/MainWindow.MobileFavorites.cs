using System.IO;
using System.Text.Json.Nodes;

namespace ZipMp3Player;

public partial class MainWindow
{
    private JsonObject ExportMobileFavorites(ZipAlbum album)
    {
        var tracks=new JsonArray();
        foreach(var track in album.Tracks)
        {
            var file=track.IsArchiveEntry?track.FileName.Replace('\\','/'):
                track.CuePath.Length>0?Path.GetFileName(track.SourcePath)+" · Track "+track.TrackNumber:
                Path.GetFileName(track.SourcePath);
            tracks.Add(new JsonObject {["file"]=file,["title"]=track.Title,["number"]=track.TrackNumber,
                ["disc"]=track.DiscNumber,["favorite"]=_favoritesStore.IsTrackFavorite(track)});
        }
        return new JsonObject {["version"]=1,["album"]=_favoritesStore.IsAlbumFavorite(album),["tracks"]=tracks};
    }
}
