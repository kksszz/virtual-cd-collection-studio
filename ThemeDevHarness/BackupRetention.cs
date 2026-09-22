using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyBackupRetention()
    {
        var root = Path.Combine(Path.GetTempPath(), "EditBackupRetention-" + Guid.NewGuid().ToString("N"));
        var music = Path.Combine(root, "music"); Directory.CreateDirectory(music);
        var statePath = Path.Combine(root, "state.json");
        var cleanup = new EditBackupRetention(statePath);
        var now = DateTimeOffset.UtcNow;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
        string Source(string name)
        {
            var path = Path.Combine(music, name + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8)));
            using var output = File.Create(path); encoder.Save(output);
            return path;
        }
        string Backup(string source, string kind = "rotation")
        {
            var path = source + "." + kind + "-backup-" + Guid.NewGuid().ToString("N");
            File.Copy(source, path);
            File.SetLastWriteTimeUtc(path, now.AddYears(-5).UtcDateTime);
            return path;
        }
        var source = Source("normal");
        var backup = Backup(source);
        var crop = Backup(source, "crop");
        var manual = source + ".backup-manual"; File.Copy(source, manual);
        var lookalike = source + ".rotation-backup-not-a-guid"; File.Copy(source, lookalike);
        var orphanSource = Source("missing");
        var orphan = Backup(orphanSource);
        var brokenSource = Source("broken");
        var broken = Backup(brokenSource);
        var modified = Backup(source);
        var locked = Backup(source);
        var cancelled = Backup(source);
        Check(cleanup.Run([music], null, now).Deleted == 0 && File.Exists(backup), "old modification time never triggers immediate deletion");
        Check(cleanup.Run([music], null, now.AddDays(7).AddTicks(-1)).Deleted == 0, "full seven-day retention");
        File.Delete(orphanSource);
        File.WriteAllText(brokenSource, "not an image");
        File.AppendAllText(modified, "changed");
        using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = cleanup.Run([music], null, now.AddDays(7));
            Check(result.Deleted == 3, "only eligible rotation/crop backups deleted");
            Check(!File.Exists(backup) && !File.Exists(crop) && !File.Exists(cancelled), "seven-day backups removed");
            Check(File.Exists(source) && File.Exists(manual) && File.Exists(lookalike), "original and unrelated files preserved");
            Check(File.Exists(orphan) && File.Exists(broken) && File.Exists(modified) && File.Exists(locked), "missing/broken/modified/locked cases preserved");
        }
        var next = Backup(source);
        cleanup.Run([music], null, now.AddDays(8));
        var reopened = new EditBackupRetention(statePath);
        Check(reopened.Run([music], null, now.AddDays(15), () => false).Deleted == 0 && File.Exists(next), "shutdown cancellation preserves backups");
        Check(reopened.Run([music], null, now.AddDays(15)).Deleted >= 1 && !File.Exists(next), "retention survives restart");
        var ledger = JsonSerializer.Deserialize<EditBackupRetention.State>(File.ReadAllText(statePath))!;
        Check(ledger.Deleted.Any(e => e.Backup == backup), "deletion audit retained");
        Check(EditBackupRetention.SourceName("album.zip.mp3.tag-backup-20260922-223344555") == "album.zip.mp3"
            && EditBackupRetention.SourceName("album.zip.mp3") is null
            && EditBackupRetention.SourceName("notes.txt.rotation-backup-" + Guid.NewGuid().ToString("N")) is null,
            "strict backup filename classification");
        var wav = Path.Combine(music, "tags.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(44100, 16, 2)))
            writer.Write(new byte[176400], 0, 176400);
        var custom = Path.Combine(root, "custom-backups"); Directory.CreateDirectory(custom);
        var tagBackup = Path.Combine(custom, "tags.wav.tag-backup-20260922-223344555");
        File.Copy(wav, tagBackup);
        cleanup.Run([music], custom, now.AddDays(16));
        using (var held = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cleanup.Run([music], custom, now.AddDays(23));
            Check(File.Exists(tagBackup), "locked source prevents backup deletion");
        }
        cleanup.Run([music], custom, now.AddDays(23));
        Check(!File.Exists(tagBackup) && File.Exists(wav), "tag backup in custom directory expires safely");
        var manualState = Path.Combine(root, "corrupt-state.json");
        File.WriteAllText(manualState, "{broken");
        var protectedBackup = Backup(source);
        try { new EditBackupRetention(manualState).Run([music], null, now.AddYears(1)); throw new Exception("corrupt ledger accepted"); }
        catch (JsonException) { Check(File.Exists(protectedBackup), "corrupt ledger fails closed"); }
        Console.WriteLine("Isolated retention fixtures: " + root);
    }
}
