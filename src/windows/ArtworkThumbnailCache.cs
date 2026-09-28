using System.IO;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

internal static class ArtworkThumbnailCache
{
    // Shared with generation: deletion cannot race an in-progress thumbnail write.
    internal static readonly object SyncRoot=new();
    internal sealed record ClearResult(int Deleted,long Bytes,int Failed);
    internal static ClearResult Clear(string dataDirectory) => ClearAlbum(dataDirectory,null);
    internal static ClearResult ClearAlbum(string dataDirectory, string? albumPath)
    {
        lock(SyncRoot)
        {
            var root=Path.GetFullPath(dataDirectory);
            var directory=Path.GetFullPath(Path.Combine(root,"thumbnail-cache"));
            if(!string.Equals(Path.GetDirectoryName(directory),root.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
                throw new IOException("キャッシュフォルダーを確認できません。");
            if(!Directory.Exists(directory))return new(0,0,0);
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("リンクされたキャッシュフォルダーは削除しません。");
            int deleted=0,failed=0;long bytes=0;
            var prefix=albumPath is null?null:Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(albumPath).ToUpperInvariant())))[..20]+"-";
            // Only files created by LoadFrontSpreadThumbnail; no recursion or broad deletion.
            foreach(var path in Directory.EnumerateFiles(directory,"*.png",SearchOption.TopDirectoryOnly))
            {
                if(!Regex.IsMatch(Path.GetFileName(path),@"\A[0-9A-Fa-f]{20}-[0-9A-Fa-f]{20}\.png\z"))continue;
                if(prefix is not null&&!Path.GetFileName(path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
                try{
                    var file=new FileInfo(path);
                    if((file.Attributes&FileAttributes.ReparsePoint)!=0){failed++;continue;}
                    var size=file.Length;file.Delete();deleted++;bytes+=size;
                }
                catch(IOException){failed++;}
                catch(UnauthorizedAccessException){failed++;}
            }
            return new(deleted,bytes,failed);
        }
    }
}
