using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class RotationChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vcd-rotation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", Path.Combine(directory, "settings"));
        var method = typeof(MainWindow).Assembly.GetType("ZipMp3Player.ArtworkRotationWriter")!.GetMethod("Rotate")!;
        string Rotate(string path, string? name, int degrees) =>
            (string)method.Invoke(null, [path, name, degrees, null, null])!;
        var bitmap = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0,0,255,255, 0,255,0,255, 255,0,0,255, 255,255,255,255, 0,0,0,255, 0,255,255,255 }, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var original = memory.ToArray();
        var path = Path.Combine(directory, "画像.png");
        File.WriteAllBytes(path, original);
        var backup = Rotate(path, null, 90);
        if (!File.ReadAllBytes(backup).SequenceEqual(original)) throw new Exception("Backup changed");
        void Check(Stream stream)
        {
            using var seekable = new MemoryStream(); stream.CopyTo(seekable); seekable.Position = 0;
            var frame = BitmapDecoder.Create(seekable, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth != 3 || frame.PixelHeight != 2) throw new Exception($"Rotation dimensions: {frame.PixelWidth} x {frame.PixelHeight}");
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[24]; converted.CopyPixels(pixels, 12, 0);
            if (!pixels.Take(4).SequenceEqual(new byte[]{0,0,0,255})) throw new Exception("Clockwise rotation pixels");
        }
        using (var stream = File.OpenRead(path)) Check(stream);
        var zipPath = Path.Combine(directory, "album.zip.mp3");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var output = zip.CreateEntry("音楽.mp3", CompressionLevel.NoCompression).Open()) output.Write(new byte[4096]);
            using (var output = zip.CreateEntry("画像/表.png").Open()) output.Write(original);
            using (var output = zip.CreateEntry("lyrics.txt").Open()) output.Write(new byte[]{1,2,3});
        }
        var zipOriginal = File.ReadAllBytes(zipPath);
        var zipBackup = Rotate(zipPath, "画像/表.png", 90);
        var hasCompressed = typeof(MainWindow).Assembly.GetType("ZipMp3Player.ZipStorageConversionService")!
            .GetMethod("HasCompressedEntries")!;
        if ((bool)hasCompressed.Invoke(null, [zipPath])!) throw new Exception("ZIP compression badge would remain");
        if (!File.ReadAllBytes(zipBackup).SequenceEqual(zipOriginal)) throw new Exception("ZIP backup changed");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.Entries.Any(entry => entry.Length != entry.CompressedLength))
                throw new Exception("All rebuilt entries must be stored");
            using var image = zip.GetEntry("画像/表.png")!.Open(); Check(image);
            var audio = zip.GetEntry("音楽.mp3")!;
            if (audio.Length != 4096 || audio.CompressedLength != 4096) throw new Exception("Stored audio changed");
        }
        var unchanged = File.ReadAllBytes(zipPath);
        try { Rotate(zipPath, "missing.png", 90); throw new Exception("Expected failure"); }
        catch (TargetInvocationException) { }
        if (!File.ReadAllBytes(zipPath).SequenceEqual(unchanged)) throw new Exception("Failure changed original");
        if (Directory.EnumerateFiles(directory, "*.rotationtmp").Any()) throw new Exception("Temporary file leak");
        Console.WriteLine("PASS rotation: PNG pixels, ZIP Japanese names, stored audio, backups, failure safety");

        var app = new System.Windows.Application();
        System.Threading.SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        var window = new MainWindow();
        var type = typeof(MainWindow);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var imageType = type.GetNestedType("AlbumImageSource", BindingFlags.NonPublic)!;
        var stagedPath = Path.Combine(directory, "staged.png");
        File.WriteAllBytes(stagedPath, original);
        object source = Activator.CreateInstance(imageType, ["staged.png", stagedPath, null, 0])!;
        var images = (System.Collections.IList)type.GetField("_albumImages", flags)!.GetValue(window)!;
        images.Add(source);
        type.GetField("_album", flags)!.SetValue(window, new ZipAlbum { Path = directory, Tracks = [new ZipTrack { Title = "Test", FileName = "song.mp3" }] });
        type.GetField("_albumImageIndex", flags)!.SetValue(window, 0);
        type.GetField("_selectedAlbumImageIndex", flags)!.SetValue(window, 0);
        var stage = type.GetMethod("StageArtworkRotation", flags)!;
        for (int i = 0; i < 4; i++) source = stage.Invoke(window, [source, 90])!;
        if (type.GetField("_pendingArtworkRotation", flags)!.GetValue(window) is not null) throw new Exception("Full turn should not save");
        if (!File.ReadAllBytes(stagedPath).SequenceEqual(original)) throw new Exception("Preview wrote to disk");
        source = stage.Invoke(window, [source, 90])!;
        if (!File.ReadAllBytes(stagedPath).SequenceEqual(original)) throw new Exception("Quarter turn wrote immediately");
        var commit = (Task<bool>)type.GetMethod("CommitArtworkRotationAsync", flags)!.Invoke(window, null)!;
        var overlay = (System.Windows.Controls.Border)window.FindName("ArtworkSavingOverlay");
        if (overlay.Visibility != System.Windows.Visibility.Visible || !window.IsEnabled)
            throw new Exception("Save overlay missing or entire window disabled");
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = commit.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        if (!commit.GetAwaiter().GetResult()) throw new Exception("Deferred commit failed");
        if (overlay.Visibility != System.Windows.Visibility.Collapsed) throw new Exception("Save overlay not cleared");
        using (var stream = File.OpenRead(stagedPath)) Check(stream);
        if (type.GetField("_pendingArtworkRotation", flags)!.GetValue(window) is not null) throw new Exception("Pending rotation not cleared");
        var roles = (Dictionary<string,int>)type.GetField("_currentArtworkRotations", flags)!.GetValue(window)!;
        if (roles.Count != 0) throw new Exception("Saved preview rotation remains");
        var artworkRoles = (Dictionary<string,string>)type.GetField("_currentArtworkRoles", flags)!.GetValue(window)!;
        var bookletButton = (System.Windows.Controls.Button)window.FindName("AlbumBookletButton");
        var updateButtons = type.GetMethod("UpdateArtworkRotationButtons", flags)!;
        artworkRoles["file:" + stagedPath] = "FrontSpread";
        updateButtons.Invoke(window, null);
        if (!bookletButton.IsEnabled) throw new Exception("Direct booklet button disabled for spread");
        artworkRoles["file:" + stagedPath] = "Front";
        updateButtons.Invoke(window, null);
        if (bookletButton.IsEnabled) throw new Exception("Direct booklet button enabled without inside");
        Console.WriteLine("PASS direct booklet button: front spread enabled, incomplete artwork disabled");
        overlay.Visibility = System.Windows.Visibility.Visible;
        ((System.Windows.Controls.TextBlock)window.FindName("ArtworkSavingFile")).Text = "新しいフォルダ/Scan183.jpg";
        ((System.Windows.Controls.TextBlock)window.FindName("ArtworkSavingStage")).Text = "3 / 4　保存内容を検証しています…\n音楽など、他のファイルが変わっていないか確認中";
        overlay.Measure(new System.Windows.Size(900,600));
        overlay.Arrange(new System.Windows.Rect(0,0,900,600));
        overlay.UpdateLayout();
        var preview = new RenderTargetBitmap(900,600,96,96,PixelFormats.Pbgra32);
        preview.Render(overlay);
        var previewEncoder = new PngBitmapEncoder(); previewEncoder.Frames.Add(BitmapFrame.Create(preview));
        var previewPath = Path.Combine(directory,"saving-preview.png");
        using(var output = File.Create(previewPath)) previewEncoder.Save(output);
        Console.WriteLine(previewPath);
        window.Close();
        Console.WriteLine("PASS deferred preview: four turns cancel, no immediate writes, commit writes once");
    }
}
