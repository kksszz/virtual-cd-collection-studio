using System.IO;

namespace ZipMp3Player;

internal static class LibraryScanScope
{
    internal static bool Contains(string root,string path)
    {
        var fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var fullPath=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        return fullRoot.Equals(fullPath,StringComparison.OrdinalIgnoreCase)||fullPath.StartsWith(fullRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    internal static string[] EnabledRoots(IEnumerable<string> roots,IEnumerable<string> disabled)
    {
        var hidden=disabled.ToArray();return roots.Where(root=>!hidden.Any(parent=>Contains(parent,root))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static IEnumerable<string> Files(string root,bool recursive,IReadOnlyList<string>? excluded=null)
    {
        var directories=new Queue<string>();directories.Enqueue(root);
        var options=new EnumerationOptions{RecurseSubdirectories=false,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
        while(directories.Count>0){var directory=directories.Dequeue();if(excluded?.Any(hidden=>Contains(hidden,directory))==true)continue;
            foreach(var file in Directory.EnumerateFiles(directory,"*",options))if(excluded?.Any(hidden=>Contains(hidden,file))!=true)yield return file;
            if(recursive)foreach(var child in Directory.EnumerateDirectories(directory,"*",options))if(excluded?.Any(hidden=>Contains(hidden,child))!=true)directories.Enqueue(child);
        }
    }
    internal static IReadOnlyList<ZipAlbum> Merge(IReadOnlyList<ZipAlbum> previous,IReadOnlyList<ZipAlbum> scanned,IReadOnlyList<string> roots,IReadOnlyList<string>? excluded=null)
    {
        var albums=scanned.ToDictionary(a=>a.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var old in previous){
            if(albums.ContainsKey(old.Path))continue;
            if(excluded?.Any(root=>Contains(root,old.Path))==true||!roots.Any(root=>Contains(root,old.Path))||!LibraryAlbumAvailability.IsConfirmedMissing(old.Path))albums.Add(old.Path,old);
        }
        return albums.Values.ToList();
    }
}
