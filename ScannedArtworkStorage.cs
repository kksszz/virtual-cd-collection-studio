using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ZipMp3Player;

internal sealed record ScannedArtwork(string Path,string Name);
internal static class ScannedArtworkStorage
{
    internal static void ValidateNames(IReadOnlyList<ScannedArtwork> pages)
    {
        if(pages.Count==0)throw new ArgumentException("スキャンした画像がありません。");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var page in pages){
            var name=page.Name;
            if(string.IsNullOrWhiteSpace(name)||name!=name.Trim()||Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase)||name.EndsWith(' ')||name.Length>150)
                throw new ArgumentException("画像名はフォルダーを含まないJPG／PNG名にしてください："+name);
            var stem=Path.GetFileNameWithoutExtension(name).Split('.')[0];
            if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("この画像名はWindowsで使用できません："+name);
            if(!names.Add(name))throw new ArgumentException("同じ画像名が複数あります："+name);
            _=ScannerService.Load(page.Path);
        }
    }
    internal static string? Save(string albumPath,IReadOnlyList<ScannedArtwork> pages,IProgress<string>? progress=null)
    {
        ValidateNames(pages);
        var exportDirectory=Path.Combine(Path.GetTempPath(),"vccs-scan-export-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportDirectory);
        try{
            var encoded=new List<ScannedArtwork>();
            foreach(var page in pages){
                if(Path.GetExtension(page.Name).Equals(".png",StringComparison.OrdinalIgnoreCase)){encoded.Add(page);continue;}
                progress?.Report("JPGへ変換しています… "+page.Name);
                var output=Path.Combine(exportDirectory,Guid.NewGuid().ToString("N")+".jpg");ScannerService.Jpeg(page.Path,output);encoded.Add(new(output,page.Name));
            }
            return SaveEncoded(albumPath,encoded,progress);
        }finally{try{foreach(var file in Directory.EnumerateFiles(exportDirectory))File.Delete(file);Directory.Delete(exportDirectory);}catch(IOException){}catch(UnauthorizedAccessException){} }
    }
    private static string? SaveEncoded(string albumPath,IReadOnlyList<ScannedArtwork> pages,IProgress<string>? progress)
    {
        if(Directory.Exists(albumPath)){
            var directory=Path.Combine(albumPath,"Images");Directory.CreateDirectory(directory);
            var targets=pages.Select(p=>Path.Combine(directory,p.Name)).ToArray();
            if(targets.Any(File.Exists))throw new IOException("同名の画像が既にあります。画像名を変更してください。既存画像は上書きしません。");
            // Roll back only files created by this batch, never pre-existing files.
            var created=new List<string>();
            try{for(int i=0;i<pages.Count;i++){
                using(var output=new FileStream(targets[i],FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    created.Add(targets[i]);using var input=File.OpenRead(pages[i].Path);input.CopyTo(output);output.Flush(true);
                }
                if(!Hash(targets[i]).SequenceEqual(Hash(pages[i].Path)))throw new IOException("保存した画像の検証に失敗しました。");
            }}catch{foreach(var path in created)File.Delete(path);throw;}
            return null;
        }
        if(!ZipAlbumReader.IsSupportedArchivePath(albumPath))throw new NotSupportedException("フォルダー／ZIP／ZIP.MP3のアルバムに保存できます。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var temporary=albumPath+"."+Guid.NewGuid().ToString("N")+".scantmp";
        var backup=albumPath+".rotation-backup-"+Guid.NewGuid().ToString("N");
        try{
            var expected=new List<(string Name,long Length,byte[] Hash)>();
            var names=pages.Select(p=>"Images/"+p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var original=new FileInfo(albumPath);long size=original.Length;var modified=original.LastWriteTimeUtc;
            using(var input=File.OpenRead(albumPath))
            using(var source=new ZipArchive(input,ZipArchiveMode.Read,false,Encoding.GetEncoding(932))){
                if(source.Entries.Any(e=>names.Contains(e.FullName.Replace('\\','/'))))throw new IOException("ZIP内に同名の画像が既にあります。画像名を変更してください。");
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
                    using(var target=new ZipArchive(output,ZipArchiveMode.Create,true,Encoding.UTF8)){
                        foreach(var entry in source.Entries){
                            progress?.Report("ZIPへ画像を追加しています… "+entry.FullName);
                            var added=target.CreateEntry(entry.FullName,CompressionLevel.NoCompression);added.LastWriteTime=entry.LastWriteTime;added.ExternalAttributes=entry.ExternalAttributes;
                            using(var read=entry.Open())using(var write=added.Open())read.CopyTo(write);
                            using var verify=entry.Open();expected.Add((entry.FullName,entry.Length,SHA256.HashData(verify)));
                        }
                        foreach(var page in pages){var name="Images/"+page.Name;var added=target.CreateEntry(name,CompressionLevel.NoCompression);using(var read=File.OpenRead(page.Path))using(var write=added.Open())read.CopyTo(write);expected.Add((name,new FileInfo(page.Path).Length,Hash(page.Path)));}
                    }output.Flush(true);
                }
                progress?.Report("音楽・既存画像・追加画像を検証しています…");
                using var checkInput=File.OpenRead(temporary);using var check=new ZipArchive(checkInput,ZipArchiveMode.Read);
                if(check.Entries.Count!=expected.Count)throw new InvalidDataException("ZIPの収録数が一致しません。元ファイルは変更していません。");
                for(int i=0;i<expected.Count;i++){var e=check.Entries[i];using var content=e.Open();if(e.FullName!=expected[i].Name||e.Length!=expected[i].Length||e.Length!=e.CompressedLength||!SHA256.HashData(content).SequenceEqual(expected[i].Hash))throw new InvalidDataException("ZIP内の内容検証に失敗しました。元ファイルは変更していません。");}
            }
            original.Refresh();if(original.Length!=size||original.LastWriteTimeUtc!=modified)throw new IOException("保存中に元のZIPが変更されました。処理を中断しました。");
            progress?.Report("検証済みのZIPに置き換えています…");
            File.Replace(temporary,albumPath,backup);
            return backup;
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static byte[] Hash(string path){using var stream=File.OpenRead(path);return SHA256.HashData(stream);}
}
