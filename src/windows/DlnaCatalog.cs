using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ZipMp3Player;

internal sealed record DlnaAlbum(ZipAlbum Album,string Title,string Artist,Func<byte[]?>? Cover=null);

// A sharing session is an immutable, explicitly selected library snapshot. No paths arrive from HTTP.
internal sealed class DlnaCatalog
{
    internal const long MaximumInflatedTrackBytes=128L*1024*1024;
    internal static readonly XNamespace Didl="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/",Dc="http://purl.org/dc/elements/1.1/",Upnp="urn:schemas-upnp-org:metadata-1-0/upnp/";
    internal sealed record Node(string Id,string Parent,string Title,string Class,ZipTrack? Track=null,string Artist="",string Album="",string? RefId=null,string? CoverId=null);
    private readonly Dictionary<string,Node> nodes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,List<Node>> children=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Resource> resources=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Lazy<byte[]?>> covers=new(StringComparer.Ordinal);
    internal int TrackCount {get;private set;}
    internal int AlbumCount {get;private set;}
    internal int SkippedCount {get;private set;}
    internal string SourceProtocolInfo=>string.Join(',',resources.Values.Select(r=>Protocol(r.Mime)).Distinct(StringComparer.Ordinal));
    internal static string Protocol(string mime)=>$"http-get:*:{mime}:DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000";
    internal DlnaCatalog(IEnumerable<DlnaAlbum> selected,CancellationToken cancellation=default)
    {
        Add(new("0","-1","Virtual CD Collection Studio","object.container"));
        Add(new("albums","0","Albums / アルバム","object.container"));
        Add(new("artists","0","Artists / アーティスト","object.container"));
        Add(new("tracks","0","All tracks / すべての曲","object.container"));
        var artistIds=new Dictionary<string,string>(StringComparer.CurrentCultureIgnoreCase);
        int albumNo=0,trackNo=0;
        foreach(var album in selected)
        {
            cancellation.ThrowIfCancellationRequested();
            var playable=album.Album.Tracks.Where(CanShare).ToArray();SkippedCount+=album.Album.Tracks.Count-playable.Length;
            if(playable.Length==0)continue;
            string albumId="a"+(++albumNo).ToString(CultureInfo.InvariantCulture);
            if(album.Cover is not null)covers[albumId]=new(album.Cover,LazyThreadSafetyMode.ExecutionAndPublication);
            Add(new(albumId,"albums",Clean(album.Title),"object.container.album.musicAlbum",Artist:Clean(album.Artist),CoverId:covers.ContainsKey(albumId)?albumId:null));
            foreach(var track in playable)
            {
                cancellation.ThrowIfCancellationRequested();
                string id="t"+(++trackNo).ToString(CultureInfo.InvariantCulture);
                string artist=Clean(string.IsNullOrWhiteSpace(track.Artist)?album.Artist:track.Artist);
                if(!artistIds.TryGetValue(artist,out var artistId))
                {
                    artistId="p"+(artistIds.Count+1).ToString(CultureInfo.InvariantCulture);artistIds.Add(artist,artistId);
                    Add(new(artistId,"artists",artist,"object.container.person.musicArtist"));
                }
                var resource=new Resource(track);resources.Add(id,resource);
                var node=new Node(id,albumId,Clean(string.IsNullOrWhiteSpace(track.Title)?Path.GetFileNameWithoutExtension(track.FileName):track.Title),"object.item.audioItem.musicTrack",track,artist,Clean(album.Title),CoverId:covers.ContainsKey(albumId)?albumId:null);
                Add(node);Add(node with{Id="all-"+id,Parent="tracks",RefId=id});Add(node with{Id=artistId+"-"+id,Parent=artistId,RefId=id});
            }
        }
        TrackCount=trackNo;AlbumCount=albumNo;
        foreach(var list in children.Where(pair=>pair.Key is "albums" or "artists").Select(pair=>pair.Value))list.Sort((a,b)=>StringComparer.CurrentCultureIgnoreCase.Compare(a.Title,b.Title));
        // Some players always request the entire container (RequestedCount=0). Keep very large
        // libraries browsable without constructing an unbounded XML response in memory.
        foreach(var pair in children.Where(p=>p.Value.Count>2000).ToArray())
        {
            var items=pair.Value.ToArray();children[pair.Key]=[];
            for(int offset=0;offset<items.Length;offset+=1000)
            {
                string page="group-"+pair.Key+"-"+(offset/1000+1).ToString(CultureInfo.InvariantCulture);
                Add(new(page,pair.Key,$"{offset+1:N0}–{Math.Min(items.Length,offset+1000):N0}","object.container.storageFolder"));
                foreach(var item in items.Skip(offset).Take(1000)){var moved=item with{Parent=page};nodes[item.Id]=moved;children[page].Add(moved);}
            }
        }
    }
    internal static bool CanShare(ZipTrack t)=>t.IsSupported&&string.IsNullOrEmpty(t.CuePath)&&t.AudioFormat is "MP3" or "FLAC" or "WAV" or "M4A"
        &&(!t.IsArchiveEntry||(t.AudioFormat=="MP3"&&!t.IsEncrypted&&t.CompressionMethod is 0 or 8&&t.Size>0&&(t.CompressionMethod!=8||t.Size<=MaximumInflatedTrackBytes)));
    private static string Clean(string text)
    {
        var result=new StringBuilder();foreach(var rune in text.EnumerateRunes())
        {int n=rune.Value;if(n is 9 or 10 or 13||n>=32&&n<=0xD7FF||n>=0xE000&&n<=0xFFFD||n>=0x10000&&n<=0x10FFFF){if(result.Length+rune.Utf16SequenceLength>1024)break;result.Append(rune);}}
        return result.ToString();
    }
    private void Add(Node node){nodes.Add(node.Id,node);if(node.Track is null)children[node.Id]=[];if(children.TryGetValue(node.Parent,out var list))list.Add(node);}
    internal (string Xml,int Returned,int Total) Browse(string id,bool metadata,uint start,uint count,string sort,string filter,string baseUrl)
    {
        if(filter.Length>1024)throw new DlnaFault(402,"Filter too long");
        if(!nodes.TryGetValue(id,out var node))throw new DlnaFault(701,"No such object");
        if(!metadata&&node.Track is not null)throw new DlnaFault(710,"No such container");
        IEnumerable<Node> list=metadata?[node]:children[id];
        if(sort.Length>0)
        {
            if(sort is not ("+dc:title" or "-dc:title"))throw new DlnaFault(709,"Unsupported sort criteria");
            list=sort[0]=='+'?list.OrderBy(n=>n.Title,StringComparer.CurrentCultureIgnoreCase):list.OrderByDescending(n=>n.Title,StringComparer.CurrentCultureIgnoreCase);
        }
        int total=metadata?1:children[id].Count;
        long remaining=metadata?1:Math.Max(0,(long)total-start);
        if((count==0?remaining:Math.Min(remaining,count))>2000)throw new DlnaFault(501,"Response too large; use paged Browse");
        if(!metadata)list=list.Skip((int)Math.Min(start,int.MaxValue)).Take(count==0?int.MaxValue:(int)Math.Min(count,int.MaxValue));
        var items=list.Select(n=>ToXml(n,filter,baseUrl)).ToArray();
        string xml=new XElement(Didl+"DIDL-Lite",new XAttribute("xmlns",Didl),new XAttribute(XNamespace.Xmlns+"dc",Dc),new XAttribute(XNamespace.Xmlns+"upnp",Upnp),items).ToString(SaveOptions.DisableFormatting);
        if(xml.Length>8*1024*1024)throw new DlnaFault(501,"Response too large; use paged Browse");
        return(xml,items.Length,total);
    }
    private XElement ToXml(Node n,string filter,string url)
    {
        var fields=filter.Split(',').ToHashSet(StringComparer.Ordinal);bool Want(string field)=>fields.Contains("*")||fields.Contains(field)||fields.Any(f=>f.StartsWith(field+"@",StringComparison.Ordinal));
        var x=new XElement(Didl+(n.Track is null?"container":"item"),new XAttribute("id",n.Id),new XAttribute("parentID",n.Parent),new XAttribute("restricted","1"),new XElement(Dc+"title",n.Title),new XElement(Upnp+"class",n.Class));
        if(n.Track is null)x.Add(new XAttribute("childCount",children[n.Id].Count));
        if(n.RefId is not null)x.Add(new XAttribute("refID",n.RefId));
        if(n.Artist.Length>0&&Want("upnp:artist"))x.Add(new XElement(Upnp+"artist",n.Artist));
        if(n.Album.Length>0&&Want("upnp:album"))x.Add(new XElement(Upnp+"album",n.Album));
        if(n.CoverId is not null&&Want("upnp:albumArtURI"))x.Add(new XElement(Upnp+"albumArtURI",url+"art/"+n.CoverId+".jpg"));
        if(n.Track is { } t)
        {
            if(Want("upnp:originalTrackNumber")&&t.TrackNumber>0)x.Add(new XElement(Upnp+"originalTrackNumber",t.TrackNumber));
            if(Want("upnp:genre")&&t.Genre.Length>0)x.Add(new XElement(Upnp+"genre",Clean(t.Genre)));
            if(Want("res"))
            {
                var r=resources[n.RefId??n.Id];var res=new XElement(Didl+"res",new XAttribute("protocolInfo",Protocol(r.Mime)),url+"media/"+(n.RefId??n.Id));
                if(fields.Contains("*")||fields.Contains("res@size"))res.Add(new XAttribute("size",r.Length));
                if(fields.Contains("*")||fields.Contains("res@duration"))res.Add(new XAttribute("duration",$"{(int)t.Duration.TotalHours}:{t.Duration.Minutes:00}:{t.Duration.Seconds:00}.{t.Duration.Milliseconds:000}"));
                if(t.SampleRate>0&&(fields.Contains("*")||fields.Contains("res@sampleFrequency")))res.Add(new XAttribute("sampleFrequency",t.SampleRate));
                x.Add(res);
            }
        }
        return x;
    }
    internal Resource? FindResource(string id)=>resources.GetValueOrDefault(id);
    internal byte[]? Cover(string id){try{var bytes=covers.GetValueOrDefault(id)?.Value;return bytes is {Length:>0 and <=1048576}?bytes:null;}catch{return null;}}
    internal sealed class Resource
    {
        private readonly ZipTrack track;
        private readonly long sourceLength;
        private readonly DateTime sourceWrite;
        internal long Length {get;}
        internal string Mime {get;}
        internal Resource(ZipTrack track)
        {
            this.track=track;var file=new FileInfo(track.SourcePath);sourceLength=file.Length;sourceWrite=file.LastWriteTimeUtc;
            Length=track.IsArchiveEntry?track.Size:sourceLength;
            if(Length<=0||track.IsArchiveEntry&&(track.DataOffset<0||track.CompressedSize<0||track.DataOffset>sourceLength-track.CompressedSize||track.CompressionMethod==0&&track.CompressedSize!=track.Size))throw new InvalidDataException("Invalid shared resource bounds");
            Mime=track.AudioFormat switch{"MP3"=>"audio/mpeg","FLAC"=>"audio/flac","WAV"=>"audio/wav","M4A"=>"audio/mp4",_=>throw new NotSupportedException()};
        }
        internal void CheckUnchanged(){var file=new FileInfo(track.SourcePath);if(!file.Exists||file.Length!=sourceLength||file.LastWriteTimeUtc!=sourceWrite)throw new IOException("Shared file changed. Stop and restart sharing to update the library.");}
        internal async Task<Stream> Open(CancellationToken ct)
        {
            CheckUnchanged();
            if(!track.IsArchiveEntry)
            {
                var file=new FileStream(track.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
                try{CheckUnchanged();return file;}catch{file.Dispose();throw;}
            }
            // Resolve the current ZIP directory by exact entry name, never trust cached raw offsets.
            // Keep replacement/deletion blocked for the lifetime of this request.
            var source=new FileStream(track.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
            ZipArchive? archive=null;
            try
            {
                CheckUnchanged();Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                archive=new ZipArchive(source,ZipArchiveMode.Read,false,Encoding.GetEncoding(932));
                var matches=archive.Entries.Where(e=>e.FullName==track.FileName).Take(2).ToArray();
                if(matches.Length!=1||matches[0].Length!=Length||matches[0].CompressedLength!=track.CompressedSize)throw new InvalidDataException("Shared ZIP entry changed or is ambiguous. Stop and restart sharing.");
                if(track.CompressionMethod==0)return new DlnaArchiveStream(matches[0].Open(),archive,Length);
                using var owned=archive;
                using var input=matches[0].Open();
                string folder=Path.Combine(Path.GetTempPath(),"ZipMp3Player","Dlna");Directory.CreateDirectory(folder);
                var output=new FileStream(Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose|FileOptions.Asynchronous);
                try
                {
                    var buffer=new byte[65536];long total=0;
                    while(true){int read=await input.ReadAsync(buffer,ct);if(read==0)break;total+=read;if(total>Length||total>MaximumInflatedTrackBytes)throw new InvalidDataException("Inflated track exceeds its declared size or the DLNA limit");await output.WriteAsync(buffer.AsMemory(0,read),ct);}
                    if(total!=Length)throw new InvalidDataException("Inflated track length mismatch");output.Position=0;return output;
                }
                catch{output.Dispose();throw;}
            }
            catch{archive?.Dispose();source.Dispose();throw;}
        }
    }
}

internal sealed class DlnaArchiveStream : Stream
{
    private readonly Stream input;private readonly ZipArchive archive;private readonly long length;
    internal DlnaArchiveStream(Stream input,ZipArchive archive,long length){this.input=input;this.archive=archive;this.length=length;}
    public override bool CanRead=>true;public override bool CanSeek=>input.CanSeek;public override bool CanWrite=>false;public override long Length=>length;
    public override long Position{get=>input.Position;set=>input.Position=value;}
    public override int Read(byte[] buffer,int offset,int count)=>input.Read(buffer,offset,count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>input.ReadAsync(buffer,ct);
    public override long Seek(long offset,SeekOrigin origin)=>input.Seek(offset,origin);
    protected override void Dispose(bool disposing){if(disposing){input.Dispose();archive.Dispose();}base.Dispose(disposing);}public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}

internal sealed class DlnaFault(int code,string message):Exception(message){internal int Code=>code;}
