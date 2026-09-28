using System.IO;
using System.Text.Json;

namespace ZipMp3Player;

internal sealed class LibraryChangeLogStore
{
    private const int MaximumEntries = 5000;
    private readonly string _path;
    private readonly List<LibraryChangeLogEntry> _entries = [];

    public LibraryChangeLogStore(string path) => _path = path;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<LibraryChangeLogData>(File.ReadAllText(_path));
            _entries.Clear();
            _entries.AddRange((data?.Entries ?? []).Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Action) && !string.IsNullOrWhiteSpace(entry.Path))
                .OrderByDescending(entry => entry.OccurredLocal).Take(MaximumEntries));
        }
        catch
        {
            _entries.Clear();
        }
    }

    public void Add(string action, string path, string albumTitle, int trackCount)
    {
        _entries.Insert(0, new LibraryChangeLogEntry
        {
            OccurredLocal = DateTimeOffset.Now,
            Action = action,
            Path = path,
            AlbumTitle = albumTitle,
            TrackCount = Math.Max(0, trackCount)
        });
        if (_entries.Count > MaximumEntries)
            _entries.RemoveRange(MaximumEntries, _entries.Count - MaximumEntries);
    }

    public IReadOnlyList<LibraryChangeLogEntry> Snapshot() =>
        _entries.OrderByDescending(entry => entry.OccurredLocal).Select(entry => entry.Clone()).ToList();

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            var data = new LibraryChangeLogData { Entries = _entries.ToList() };
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        catch
        {
            // Library updates must remain usable even if the log cannot be saved.
        }
    }

    private sealed class LibraryChangeLogData
    {
        public int Version { get; set; } = 1;
        public List<LibraryChangeLogEntry> Entries { get; set; } = [];
    }
}

internal sealed class LibraryChangeLogEntry
{
    public DateTimeOffset OccurredLocal { get; set; }
    public string Action { get; set; } = "";
    public string AlbumTitle { get; set; } = "";
    public string Path { get; set; } = "";
    public int TrackCount { get; set; }

    public LibraryChangeLogEntry Clone() => (LibraryChangeLogEntry)MemberwiseClone();
}
