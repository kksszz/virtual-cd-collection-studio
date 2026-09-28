using System.IO;

namespace ZipMp3Player;

internal static class LibraryAlbumAvailability
{
    // Exists() returns false for both absent and inaccessible paths. Only a successful
    // parent listing may confirm absence; a disconnected share must retain the cache.
    internal static bool IsConfirmedMissing(string path)
    {
        var target=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        for(var child=target;!string.IsNullOrWhiteSpace(child);){
            var parent=Path.GetDirectoryName(child);if(string.IsNullOrWhiteSpace(parent))return false;
            try{
                return !Directory.EnumerateFileSystemEntries(parent).Any(entry=>string.Equals(Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar),child,StringComparison.OrdinalIgnoreCase));
            }
            catch(DirectoryNotFoundException){child=parent;}
            catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
        return false;
    }
}
