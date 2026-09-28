using System.IO;

namespace ZipMp3Player;

internal static class CueAlbumIdentity
{
    // Only collapse a cue when its parent album already has every cue segment.
    // A raw FLAC, another pressing, or a standalone BIN/CUE must remain visible.
    internal static bool IsCoveredBy(ZipAlbum cue, ZipAlbum folder)
    {
        if (!CueAlbumReader.IsCue(cue.Path) || cue.Tracks.Count == 0
            || !Same(Path.GetDirectoryName(cue.Path) ?? "", folder.Path)) return false;
        return cue.Tracks.All(track => folder.Tracks.Any(other =>
            Same(other.CuePath, cue.Path) && Same(track.SourcePath, other.SourcePath)
            && track.CueStartFrame == other.CueStartFrame && track.TrackNumber == other.TrackNumber));
    }
    private static bool Same(string a,string b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
}
