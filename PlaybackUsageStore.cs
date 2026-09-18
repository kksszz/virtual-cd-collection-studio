using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace ZipMp3Player;

internal sealed class PlaybackUsageStore
{
    private readonly string _path;
    private readonly Dictionary<string, PlaybackUsageEntry> _entries = new(StringComparer.Ordinal);
    public bool IsDirty { get; private set; }

    public PlaybackUsageStore(string path) => _path = path;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<PlaybackUsageData>(File.ReadAllText(_path));
            foreach (var entry in data?.Entries ?? [])
                if (!string.IsNullOrWhiteSpace(entry.Key)) _entries[entry.Key] = entry;
        }
        catch
        {
            _entries.Clear();
        }
    }

    public PlaybackUsageEntry GetOrCreate(ZipTrack track)
    {
        var key = CreateTrackKey(track);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new PlaybackUsageEntry { Key = key };
            _entries.Add(key, entry);
        }
        entry.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "アーティスト不明" : track.Artist;
        entry.Title = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title;
        entry.Album = string.IsNullOrWhiteSpace(track.Album) ? "アルバム不明" : track.Album;
        entry.TrackDurationSeconds = Math.Max(entry.TrackDurationSeconds, track.Duration.TotalSeconds);
        IsDirty = true;
        return entry;
    }

    public void AddPlaybackTime(PlaybackUsageEntry entry, double seconds)
    {
        if (seconds <= 0) return;
        entry.TotalPlayedSeconds += seconds;
        IsDirty = true;
    }

    public void CommitPlay(PlaybackUsageEntry entry)
    {
        entry.PlayCount++;
        entry.LastPlayedLocal = DateTimeOffset.Now;
        IsDirty = true;
    }

    public void RelocateTrack(ZipTrack track, string newSourcePath)
    {
        var oldKey = CreateTrackKey(track.SourcePath, track.IsArchiveEntry || track.CuePath.Length > 0, track.FileName);
        var newKey = CreateTrackKey(newSourcePath, track.IsArchiveEntry || track.CuePath.Length > 0, track.FileName);
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal) || !_entries.TryGetValue(oldKey, out var oldEntry)) return;
        _entries.Remove(oldKey);
        if (_entries.TryGetValue(newKey, out var target))
        {
            target.TotalPlayedSeconds += oldEntry.TotalPlayedSeconds;
            target.PlayCount += oldEntry.PlayCount;
            target.TrackDurationSeconds = Math.Max(target.TrackDurationSeconds, oldEntry.TrackDurationSeconds);
            if (oldEntry.LastPlayedLocal > target.LastPlayedLocal) target.LastPlayedLocal = oldEntry.LastPlayedLocal;
        }
        else
        {
            oldEntry.Key = newKey;
            _entries[newKey] = oldEntry;
        }
        IsDirty = true;
    }

    public IReadOnlyList<PlaybackUsageEntry> Snapshot() => _entries.Values
        .OrderByDescending(entry => entry.TotalPlayedSeconds)
        .ThenByDescending(entry => entry.LastPlayedLocal)
        .Select(entry => entry.Clone())
        .ToList();

    internal void CopyTrackHistory(ZipTrack source, ZipTrack destination)
    {
        if (!_entries.TryGetValue(CreateTrackKey(source), out var original)) { RemoveTrackHistory(destination); return; }
        var key = CreateTrackKey(destination);
        var copy = original.Clone(); copy.Key = key;
        _entries[key] = copy; IsDirty = true;
    }

    internal void RemoveTrackHistory(ZipTrack track)
    { if (_entries.Remove(CreateTrackKey(track))) IsDirty = true; }

    public void Save()
    {
        if (!IsDirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            var data = new PlaybackUsageData { Entries = _entries.Values.OrderBy(entry => entry.Artist).ThenBy(entry => entry.Title).ToList() };
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
            IsDirty = false;
        }
        catch
        {
            // 再生を妨げず、次回の定期保存で再試行する。
        }
    }

    internal static string CreateTrackKey(ZipTrack track) => CreateTrackKey(track.SourcePath, track.IsArchiveEntry || track.CuePath.Length > 0, track.FileName);

    private static string CreateTrackKey(string sourcePath, bool isArchiveEntry, string fileName)
    {
        var source = Path.GetFullPath(sourcePath).ToUpperInvariant();
        var identity = isArchiveEntry ? source + "|" + fileName : source;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private sealed class PlaybackUsageData
    {
        public int Version { get; set; } = 1;
        public List<PlaybackUsageEntry> Entries { get; set; } = [];
    }
}

internal sealed class PlaybackUsageEntry
{
    public string Key { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string Album { get; set; } = "";
    public int PlayCount { get; set; }
    public double TotalPlayedSeconds { get; set; }
    public double TrackDurationSeconds { get; set; }
    public DateTimeOffset? LastPlayedLocal { get; set; }

    public PlaybackUsageEntry Clone() => (PlaybackUsageEntry)MemberwiseClone();
}
