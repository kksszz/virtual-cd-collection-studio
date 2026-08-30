using System.IO.Compression;
using ZipMp3Player;

var root = Path.Combine(Path.GetTempPath(), "ZipMp3Player-BackupTest-" + Guid.NewGuid().ToString("N"));
var data = Path.Combine(root, "data");
var backup = Path.Combine(root, "test.zipmp3backup");
Directory.CreateDirectory(Path.Combine(data, "artwork", "album-a"));
Directory.CreateDirectory(Path.Combine(data, "lyrics"));
File.WriteAllText(Path.Combine(data, "settings.json"), "original-settings");
File.WriteAllText(Path.Combine(data, "library.json"), "original-library");
File.WriteAllText(Path.Combine(data, "usage.json"), "original-usage");
File.WriteAllText(Path.Combine(data, "favorites.json"), "original-favorites");
File.WriteAllText(Path.Combine(data, "artwork", "album-a", "cover.jpg"), "original-cover");
File.WriteAllText(Path.Combine(data, "lyrics", "track.txt"), "original-lyrics");

AppDataBackupService.CreateBackup(data, backup);
File.WriteAllText(Path.Combine(data, "settings.json"), "changed-settings");
File.WriteAllText(Path.Combine(data, "favorites.json"), "changed-favorites");
File.WriteAllText(Path.Combine(data, "artwork", "album-a", "cover.jpg"), "changed-cover");
File.WriteAllText(Path.Combine(data, "lyrics", "track.txt"), "changed-lyrics");
var safety = AppDataBackupService.RestoreBackup(data, backup);

Require(File.ReadAllText(Path.Combine(data, "settings.json")) == "original-settings", "settings restore");
Require(File.ReadAllText(Path.Combine(data, "favorites.json")) == "original-favorites", "favorites restore");
Require(File.ReadAllText(Path.Combine(data, "artwork", "album-a", "cover.jpg")) == "original-cover", "artwork overwrite");
Require(File.ReadAllText(Path.Combine(data, "lyrics", "track.txt")) == "original-lyrics", "lyrics overwrite");
Require(File.Exists(safety), "safety backup");

var invalid = Path.Combine(root, "invalid.zipmp3backup");
using (var stream = File.Create(invalid))
using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
    zip.CreateEntry("settings.json");
try
{
    AppDataBackupService.RestoreBackup(data, invalid);
    throw new InvalidOperationException("invalid backup was accepted");
}
catch (InvalidDataException) { }

Console.WriteLine("Backup and restore test passed.");
Directory.Delete(root, recursive: true);

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Failed: {name}");
}
