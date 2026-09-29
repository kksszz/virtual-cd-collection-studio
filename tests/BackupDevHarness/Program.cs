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
File.WriteAllText(Path.Combine(data, "album-added.json"), "original-added");
File.WriteAllText(Path.Combine(data, "cd-import.json"), "original-drive-settings");
File.WriteAllText(Path.Combine(data, "artwork", "album-a", "cover.jpg"), "original-cover");
File.WriteAllText(Path.Combine(data, "lyrics", "track.txt"), "original-lyrics");

AppDataBackupService.CreateBackup(data, backup);
File.WriteAllText(Path.Combine(data, "settings.json"), "changed-settings");
File.WriteAllText(Path.Combine(data, "favorites.json"), "changed-favorites");
File.WriteAllText(Path.Combine(data, "album-added.json"), "changed-added");
File.WriteAllText(Path.Combine(data, "cd-import.json"), "changed-drive-settings");
File.WriteAllText(Path.Combine(data, "artwork", "album-a", "cover.jpg"), "changed-cover");
File.WriteAllText(Path.Combine(data, "lyrics", "track.txt"), "changed-lyrics");
var safety = AppDataBackupService.RestoreBackup(data, backup);

Require(File.ReadAllText(Path.Combine(data, "settings.json")) == "original-settings", "settings restore");
Require(File.ReadAllText(Path.Combine(data, "favorites.json")) == "original-favorites", "favorites restore");
Require(File.ReadAllText(Path.Combine(data, "album-added.json")) == "original-added", "album-added restore");
Require(File.ReadAllText(Path.Combine(data, "cd-import.json")) == "original-drive-settings", "drive correction restore");
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
var app = Path.Combine(root, "app");
var musicA = Path.Combine(root, "music-A");
var musicB = Path.Combine(root, "music-B");
Directory.CreateDirectory(app);
Directory.CreateDirectory(Path.Combine(musicA, "Album", "Images"));
Directory.CreateDirectory(Path.Combine(musicA, "Empty"));
Directory.CreateDirectory(musicB);
File.WriteAllBytes(Path.Combine(app, "Virtual CD Collection Studio.exe"), [1, 2, 3, 4]);
File.WriteAllText(Path.Combine(musicA, "Album", "01.flac"), "original-audio");
File.WriteAllText(Path.Combine(musicA, "Album", "Images", "Front.jpg"), "original-art");
File.WriteAllText(Path.Combine(musicB, "02.mp3"), "excluded-audio");
var archivePath = Path.Combine(root, "full.vccsbackup");
var selected = CollectionBackupService.Sources(data, app, true, true, [musicA]);
var estimate = CollectionBackupService.Estimate(selected, archivePath);
Require(estimate.Files >= 9 && estimate.Bytes > 0, "full backup estimate");
var result = CollectionBackupService.Create(selected, archivePath, CompressionLevel.Fastest);
Require(result.Files == estimate.Files, "full backup file count");
var contents = CollectionBackupService.Validate(archivePath);
Require(contents.Sources.Count == 3 && contents.Files.Any(file => file.Path == "Libraries/0001/Album/01.flac"), "full backup manifest");
Require(!contents.Files.Any(file => file.Path.Contains("excluded", StringComparison.OrdinalIgnoreCase)), "unselected music excluded");
var restored = Path.Combine(root, "restored");
CollectionBackupService.RestoreToNewFolder(archivePath, restored);
Require(File.ReadAllText(Path.Combine(restored, "Data", "cd-import.json")) == "original-drive-settings", "full settings restored");
Require(File.ReadAllText(Path.Combine(restored, "Libraries", "0001", "Album", "01.flac")) == "original-audio", "full music restored");
Require(Directory.Exists(Path.Combine(restored, "Libraries", "0001", "Empty")), "empty directory restored");
Require(File.ReadAllBytes(Path.Combine(restored, "Application", "Virtual CD Collection Studio.exe")).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "app restored");
Require(File.Exists(Path.Combine(restored, "RESTORE-README.txt")), "restore instructions");
Require(File.ReadAllText(Path.Combine(musicA, "Album", "01.flac")) == "original-audio", "source music retained");
try { CollectionBackupService.RestoreToNewFolder(archivePath, restored); throw new Exception("restore overwrote target"); }
catch (IOException) { }
try { CollectionBackupService.Create(selected, Path.Combine(musicA, "unsafe.vccsbackup"), CompressionLevel.Fastest); throw new Exception("backup inside source accepted"); }
catch (InvalidOperationException) { }
var corrupted = Path.Combine(root, "corrupt.vccsbackup");
File.Copy(archivePath, corrupted);
using (var stream = File.Open(corrupted, FileMode.Open, FileAccess.ReadWrite))
using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
using (var entry = zip.GetEntry("Libraries/0001/Album/01.flac")!.Open())
{
    entry.SetLength(0);
    entry.Write("modified-audio"u8);
}
try { CollectionBackupService.RestoreToNewFolder(corrupted, Path.Combine(root, "invalid-restore")); throw new Exception("corruption accepted"); }
catch (InvalidDataException) { }
Require(!Directory.Exists(Path.Combine(root, "invalid-restore")), "corrupt backup leaves target untouched");
Console.WriteLine("PASS full backup: selectable roots, application/data/music, SHA-256, non-overwrite restore, corrupted archive rejection");
Directory.Delete(root, recursive: true);

static void Require(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Failed: {name}");
}
