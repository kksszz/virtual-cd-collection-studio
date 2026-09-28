using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed class EditBackupRetention(string statePath)
{
    internal const int RetentionDays = 7;
    internal sealed record Entry(string Backup, string Source, long Length, long WriteTicks, long CreationTicks, DateTimeOffset ObservedAt);
    internal sealed record DeletedEntry(string Backup, DateTimeOffset DeletedAt);
    internal sealed class State
    {
        public List<Entry> Entries { get; set; } = [];
        public List<DeletedEntry> Deleted { get; set; } = [];
    }
    internal sealed record Result(int Deleted, int Retained);
    private static readonly Regex BackupName = new(
        @"^(?<source>.+)\.(?:(?:crop|rotation)-backup-[0-9a-f]{32}|tag-backup-\d{8}-\d{9}(?:-\d{1,3})?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? SourceName(string path)
    {
        var match = BackupName.Match(Path.GetFileName(path));
        if (!match.Success) return null;
        var source = match.Groups["source"].Value;
        return ZipAlbumReader.IsSupportedArchivePath(source) || ZipAlbumReader.IsStandardAudioPath(source)
            || IsImage(source) ? source : null;
    }
    private static bool IsImage(string path) => new[] { ".jpg", ".jpeg", ".png" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool NoLinks(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0) return false;
            for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch { return false; }
    }
    private static bool Same(Entry entry, FileInfo file) => file.Exists && file.Length == entry.Length
        && file.LastWriteTimeUtc.Ticks == entry.WriteTicks && file.CreationTimeUtc.Ticks == entry.CreationTicks;

    internal Result Run(IReadOnlyList<string> musicRoots, string? backupFolder, DateTimeOffset now, Func<bool>? keepRunning = null)
    {
        var state = new State();
        if (File.Exists(statePath))
        {
            // A corrupt ledger must not cause immediate age-based deletion.
            state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath)) ?? new State();
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in musicRoots.Concat(string.IsNullOrWhiteSpace(backupFolder) ? [] : new[] { backupFolder })
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (keepRunning?.Invoke() == false) return new(0, state.Entries.Count);
            if (!Directory.Exists(root) || !NoLinks(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                         { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    if (keepRunning?.Invoke() == false) return new(0, state.Entries.Count);
                    if (!FolderAlbumLayout.IsTemporary(file)) files.Add(Path.GetFullPath(file));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var sources = files.Where(file => SourceName(file) is null)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var old = state.Entries.GroupBy(e => e.Backup, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<Entry>();
        foreach (var backup in files)
        {
            var name = SourceName(backup);
            if (name is null || !NoLinks(backup)) continue;
            var source = Path.Combine(Path.GetDirectoryName(backup)!, name);
            if (!File.Exists(source))
            {
                // A sibling backup with a missing source must never be associated with
                // an unrelated album just because its filename happens to match.
                if (string.IsNullOrWhiteSpace(backupFolder)
                    || !backup.StartsWith(Path.GetFullPath(backupFolder).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (!sources.TryGetValue(name, out var matching) || matching.Length != 1) continue;
                source = matching[0];
            }
            if (!NoLinks(source)) continue;
            try
            {
                var info = new FileInfo(backup);
                // Copied backups inherit the original's modification date. Never use it as the retention start.
                var entry = old.TryGetValue(backup, out var previous) && Same(previous, info)
                    && string.Equals(previous.Source, source, StringComparison.OrdinalIgnoreCase)
                    ? previous : new Entry(backup, source, info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks, now);
                candidates.Add(entry);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var entry in candidates) old[entry.Backup] = entry;
        state.Entries = old.Values.ToList();
        Save(state); // Persist the observation before any deletion.
        var deleted = 0;
        foreach (var entry in candidates.Where(e => now >= e.ObservedAt.AddDays(RetentionDays)).Take(50))
        {
            if (keepRunning?.Invoke() == false) break;
            try
            {
                if (!NoLinks(entry.Backup) || !NoLinks(entry.Source) || !Same(entry, new FileInfo(entry.Backup))) continue;
                // Keep the current source locked against edits/replacement throughout validation and deletion.
                using var source = new FileStream(entry.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!ReadableSource(entry.Source, source)) continue;
                string hash;
                using (var backup = File.OpenRead(entry.Backup)) hash = Convert.ToHexString(SHA256.HashData(backup));
                if (!Same(entry, new FileInfo(entry.Backup)) || keepRunning?.Invoke() == false) continue;
                FolderZipConversion.DeleteVerifiedFile(entry.Backup, hash);
                state.Entries.RemoveAll(e => string.Equals(e.Backup, entry.Backup, StringComparison.OrdinalIgnoreCase));
                state.Deleted.Add(new(entry.Backup, now));
                deleted++;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Retaining edit backup: " + entry.Backup + ": " + ex.Message); }
        }
        state.Deleted = state.Deleted.TakeLast(1000).ToList();
        Save(state);
        return new(deleted, candidates.Count - deleted);
    }
    private static bool ReadableSource(string path, Stream input)
    {
        if (input.Length == 0) return false;
        if (ZipAlbumReader.IsSupportedArchivePath(path))
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count == 0) return false;
            foreach (var entry in archive.Entries)
            {
                using var content = entry.Open();
                content.CopyTo(Stream.Null);
            }
            var album = ZipAlbumReader.Open(path);
            if (album.Tracks.Count == 0 || album.Tracks.Any(track => !track.IsSupported)) return false;
            foreach (var track in album.Tracks)
            {
                using var audio = TrackAudioReader.Open(track);
                if (audio.Reader.Read(new byte[4096], 0, 4096) == 0) return false;
            }
            return true;
        }
        if (IsImage(path))
        {
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 && decoder.Frames[0].PixelWidth > 1 && decoder.Frames[0].PixelHeight > 1;
        }
        using var reader = new NAudio.Wave.AudioFileReader(path);
        return reader.Read(new byte[4096], 0, 4096) > 0;
    }
    private void Save(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, statePath, true);
    }
}
