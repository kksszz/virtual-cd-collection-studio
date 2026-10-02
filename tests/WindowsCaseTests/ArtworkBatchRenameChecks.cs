using System.IO;
using System.IO.Compression;
using ZipMp3Player;

internal static class ArtworkBatchRenameChecks
{
    internal static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"vccs-artwork-rename-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first=Path.Combine(root,"a.jpg");var second=Path.Combine(root,"b.jpg");
        File.WriteAllText(first,"FIRST");File.WriteAllText(second,"SECOND");
        var swap=ArtworkBatchRenameService.Apply([
            new(first,null,"b"),new(second,null,"a")]);
        if(File.ReadAllText(first)!="SECOND"||File.ReadAllText(second)!="FIRST"
            ||swap.RoleKeys.Count!=2)throw new Exception("File image name swap failed");
        Console.WriteLine("PASS artwork rename: file swaps preserve content and role-key mapping");
        try
        {
            ArtworkBatchRenameService.ValidateRequests([new(first,null,"same"),new(second,null,"same")]);
            throw new Exception("Duplicate name was accepted before saving");
        }
        catch (InvalidOperationException) { }
        if (File.ReadAllText(first)!="SECOND"||File.ReadAllText(second)!="FIRST")
            throw new Exception("Validation changed source images");
        Console.WriteLine("PASS artwork rename: duplicate names rejected before files are changed");

        var archivePath=Path.Combine(root,"album.zip.mp3");
        using(var stream=File.Create(archivePath))
        using(var archive=new ZipArchive(stream,ZipArchiveMode.Create))
            foreach(var (name,data) in new[]{("Booklet001.jpg","IMAGE 1"),("Booklet002.jpg","IMAGE 2"),("song.mp3","AUDIO")})
            {
                var entry=archive.CreateEntry(name,CompressionLevel.NoCompression);
                using var writer=new StreamWriter(entry.Open());writer.Write(data);
            }
        var result=ArtworkBatchRenameService.Apply([
            new(archivePath,"Booklet001.jpg","Front"),new(archivePath,"Booklet002.jpg","Back")]);
        using(var archive=ZipFile.OpenRead(archivePath))
        {
            string Read(string name){using var reader=new StreamReader(archive.GetEntry(name)!.Open());return reader.ReadToEnd();}
            if(Read("Front.jpg")!="IMAGE 1"||Read("Back.jpg")!="IMAGE 2"||Read("song.mp3")!="AUDIO"
                ||archive.Entries.Any(entry=>entry.FullName=="Booklet001.jpg"))throw new Exception("ZIP rename changed content");
        }
        if(result.ArchiveBackups.Count!=1||!File.Exists(result.ArchiveBackups[0]))throw new Exception("ZIP backup missing");
        try{ArtworkBatchRenameService.Apply([new(archivePath,"Front.jpg","Back")]);throw new Exception("Collision accepted");}
        catch(IOException){}
        try{ArtworkBatchRenameService.ValidateBaseName("invalid/name");throw new Exception("Invalid name accepted");}
        catch(InvalidOperationException){}
        Console.WriteLine("PASS artwork rename: ZIP contents, immutable extensions, collision check and backup");
    }
}
