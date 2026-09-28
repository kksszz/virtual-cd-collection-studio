using System.IO;
using System.Text;
using System.Text.Json;

namespace ZipMp3Player;

public partial class MainWindow
{
    private static string ExportMobileLyrics(ZipAlbum album,string directory)
    {
        var tracks=new List<object>();
        foreach(var track in album.Tracks)
        {
            var saved=GetSavedLyricsPath(album,track);
            var external=FindExternalLyricsFile(album,track);
            var zip=FindZipLyricsFile(album,track);
            byte[]? bytes=null;
            if(File.Exists(saved))bytes=ReadLimited(saved);
            else if(external is not null)bytes=ReadLimited(external);
            else if(zip is not null){
                if(zip.UncompressedSize>1024*1024)throw new IOException("歌詞が大きすぎます: "+track.Title);
                bytes=ReadZipTextBytes(zip);
            }
            if(bytes is null)continue;
            if(bytes.Length>1024*1024)throw new IOException("歌詞が大きすぎます: "+track.Title);
            string file=track.IsArchiveEntry?track.FileName.Replace('\\','/'):
                track.CuePath.Length>0?Path.GetFileName(track.SourcePath)+" · Track "+track.TrackNumber:
                Path.GetFileName(track.SourcePath);
            tracks.Add(new{file,title=track.Title,number=track.TrackNumber,disc=track.DiscNumber,text=DecodeLyricsText(bytes,false)});
        }
        var output=Path.Combine(directory,"lyrics.json");
        var data=JsonSerializer.SerializeToUtf8Bytes(new{format="virtual-cd-lyrics",version=1,tracks});
        if(data.Length>8*1024*1024)throw new IOException("アルバムの歌詞が8MBを超えています");
        File.WriteAllBytes(output,data);return output;
        static byte[] ReadLimited(string file){if(new FileInfo(file).Length>1024*1024)throw new IOException("歌詞が大きすぎます: "+Path.GetFileName(file));return File.ReadAllBytes(file);}
    }
}
