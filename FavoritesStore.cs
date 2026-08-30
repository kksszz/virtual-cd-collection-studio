using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZipMp3Player;

internal sealed class FavoritesStore
{
    private readonly string _path;
    private readonly HashSet<string> _albumKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _trackKeys = new(StringComparer.Ordinal);
    public bool IsDirty { get; private set; }

    public FavoritesStore(string path) => _path = path;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<FavoritesData>(File.ReadAllText(_path));
            _albumKeys.Clear();
            _trackKeys.Clear();
            foreach (var key in data?.AlbumKeys ?? [])
                if (!string.IsNullOrWhiteSpace(key)) _albumKeys.Add(key);
            foreach (var key in data?.TrackKeys ?? [])
                if (!string.IsNullOrWhiteSpace(key)) _trackKeys.Add(key);
        }
        catch
        {
            _albumKeys.Clear();
            _trackKeys.Clear();
        }
    }

    public bool IsAlbumFavorite(ZipAlbum album) => _albumKeys.Contains(CreateAlbumKey(album.Path));
    public bool IsTrackFavorite(ZipTrack track) => _trackKeys.Contains(CreateTrackKey(track.SourcePath, track.IsArchiveEntry, track.FileName));

    public bool ToggleAlbum(ZipAlbum album)
    {
        var key = CreateAlbumKey(album.Path);
        var enabled = !_albumKeys.Remove(key);
        if (enabled) _albumKeys.Add(key);
        IsDirty = true;
        return enabled;
    }

    public bool ToggleTrack(ZipTrack track)
    {
        var key = CreateTrackKey(track.SourcePath, track.IsArchiveEntry, track.FileName);
        var enabled = !_trackKeys.Remove(key);
        if (enabled) _trackKeys.Add(key);
        IsDirty = true;
        return enabled;
    }

    public void RelocateAlbum(ZipAlbum album, string newAlbumPath) =>
        Relocate(_albumKeys, CreateAlbumKey(album.Path), CreateAlbumKey(newAlbumPath));

    public void RelocateTrack(ZipTrack track, string newSourcePath) =>
        Relocate(_trackKeys, CreateTrackKey(track.SourcePath, track.IsArchiveEntry, track.FileName),
            CreateTrackKey(newSourcePath, track.IsArchiveEntry, track.FileName));

    public void Save()
    {
        if (!IsDirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            var data = new FavoritesData
            {
                AlbumKeys = _albumKeys.Order().ToList(),
                TrackKeys = _trackKeys.Order().ToList()
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
            IsDirty = false;
        }
        catch { }
    }

    private void Relocate(HashSet<string> keys, string oldKey, string newKey)
    {
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal) || !keys.Remove(oldKey)) return;
        keys.Add(newKey);
        IsDirty = true;
    }

    private static string CreateAlbumKey(string albumPath) => Hash(Path.GetFullPath(albumPath).ToUpperInvariant());

    private static string CreateTrackKey(string sourcePath, bool isArchiveEntry, string fileName)
    {
        var source = Path.GetFullPath(sourcePath).ToUpperInvariant();
        return Hash(isArchiveEntry ? source + "|" + fileName : source);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class FavoritesData
    {
        public int Version { get; set; } = 1;
        public List<string> AlbumKeys { get; set; } = [];
        public List<string> TrackKeys { get; set; } = [];
    }
}
