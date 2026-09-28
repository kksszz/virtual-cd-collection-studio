using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZipMp3Player;

internal static class ScannerChecks
{
    internal static void Run(string output)
    {
        static void Require(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS "+text);}
        var root=Path.Combine(Path.GetTempPath(),"vccs-scan-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string source=Path.Combine(root,"source.png");var bitmap=BitmapSource.Create(30,20,96,96,PixelFormats.Bgr24,null,Enumerable.Repeat((byte)90,30*20*3).ToArray(),90);bitmap.Freeze();
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(source))encoder.Save(file);
        var pages=new[]{new ScannedArtwork(source,"Front.png"),new ScannedArtwork(source,"Booklet001.png")};
        var folder=Path.Combine(root,"album");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"music.flac"),"unchanged audio");
        ScannedArtworkStorage.Save(folder,pages);Require(File.Exists(Path.Combine(folder,"Images","Front.png"))&&File.Exists(Path.Combine(folder,"Images","Booklet001.png")),"multiple pages stored inside folder album");
        try{ScannedArtworkStorage.Save(folder,pages);throw new Exception("Collision accepted");}catch(IOException){Require(File.ReadAllText(Path.Combine(folder,"music.flac"))=="unchanged audio","existing files preserved on collision");}
        foreach(var name in new[]{"../escape.png","CON.png","not.gif"})try{ScannedArtworkStorage.ValidateNames([new(source,name)]);throw new Exception("Unsafe name accepted");}catch(ArgumentException){Console.WriteLine("PASS rejected filename "+name);}
        ScannedArtworkStorage.Save(folder,[new(source,"Booklet002.jpg")]);var jpg=File.ReadAllBytes(Path.Combine(folder,"Images","Booklet002.jpg"));Require(jpg[0]==255&&jpg[1]==216&&ScannerService.Load(Path.Combine(folder,"Images","Booklet002.jpg")).PixelWidth==30,"JPG extension contains real JPEG, dimensions preserved");
        var nested=Path.Combine(root,"multi-disc");foreach(var disc in new[]{"Disc1","Disc2"}){var dir=Path.Combine(nested,disc);Directory.CreateDirectory(dir);using var wav=new NAudio.Wave.WaveFileWriter(Path.Combine(dir,"01.wav"),new NAudio.Wave.WaveFormat(44100,16,2));wav.Write(new byte[176400],0,176400);}
        Require(FolderAlbumLayout.IsRoot(nested)&&ZipAlbumReader.OpenFolder(nested).Tracks.Count==2,"unmarked Disc1/Disc2 album opens as one album");
        var cached=ZipAlbumReader.OpenFolder(nested);ScannedArtworkStorage.Save(nested,[new(source,"Booklet001.jpg")]);var updated=ZipAlbumReader.RefreshFolderArtwork(cached);Require(updated.Tracks.Count==2&&updated.ImageCount==1&&ReferenceEquals(updated.Tracks[0],cached.Tracks[0]),"artwork refresh preserves cached music and creates missing Images folder");
        Require(!LibraryAlbumAvailability.IsConfirmedMissing(nested)&&LibraryAlbumAvailability.IsConfirmedMissing(Path.Combine(root,"truly-deleted")),"existing album retained; deletion requires successful parent listing");
        var refresh=typeof(MainWindow).GetMethod("RefreshChangedAlbums",BindingFlags.Static|BindingFlags.NonPublic)!;var existingType=typeof(MainWindow).GetNestedType("ExistingLibraryAlbum",BindingFlags.NonPublic)!;var existing=Array.CreateInstance(existingType,1);existing.SetValue(Activator.CreateInstance(existingType,[nested,false,cached]),0);
        using(var lockAudio=File.Open(Path.Combine(nested,"Disc1","01.wav"),FileMode.Open,FileAccess.Read,FileShare.None)){
            var result=refresh.Invoke(null,[new[]{Path.Combine(nested,"Images","Booklet001.jpg")},existing,CancellationToken.None])!;
            var removed=(HashSet<string>)result.GetType().GetProperty("Removed")!.GetValue(result)!;
            var refreshed=(System.Collections.ICollection)result.GetType().GetProperty("Refreshed")!.GetValue(result)!;
            Require(removed.Count==0&&refreshed.Count==1,"image watcher event retains album even when audio is exclusively locked");
        }
        var zip=Path.Combine(root,"fixture.zip.mp3");using(var file=File.Create(zip))using(var archive=new ZipArchive(file,ZipArchiveMode.Create,false,Encoding.UTF8)){
            foreach(var (name,data) in new[]{("Disc1/音楽.mp3","audio fixture"),("Images/Back.png","existing image fixture"),("Note.txt","existing notes")}){using var writer=new StreamWriter(archive.CreateEntry(name,CompressionLevel.Optimal).Open());writer.Write(data);}
        }
        Dictionary<string,byte[]> Contents(string path){using var file=File.OpenRead(path);using var archive=new ZipArchive(file,ZipArchiveMode.Read);return archive.Entries.ToDictionary(e=>e.FullName,e=>{using var s=e.Open();return SHA256.HashData(s);});}
        var before=Contents(zip);var backup=ScannedArtworkStorage.Save(zip,pages);var after=Contents(zip);
        Require(backup is not null&&File.Exists(backup)&&after.Count==5&&before.All(e=>after[e.Key].SequenceEqual(e.Value)),"ZIP batch: Japanese names, audio/images/notes unchanged; backup retained");
        using(var file=File.OpenRead(zip))using(var archive=new ZipArchive(file,ZipArchiveMode.Read))Require(archive.Entries.All(e=>e.Length==e.CompressedLength),"ZIP.MP3 remains uncompressed");
        var saved=File.ReadAllBytes(zip);try{ScannedArtworkStorage.Save(zip,pages);throw new Exception("ZIP collision accepted");}catch(IOException){Require(saved.SequenceEqual(File.ReadAllBytes(zip)),"ZIP collision leaves original byte-for-byte unchanged");}
        var app=new Application();var window=new AlbumScanWindow("テストアルバム",(_,_)=>Task.CompletedTask);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var scannerCombo=(ComboBox)typeof(AlbumScanWindow).GetField("scanners",flags)!.GetValue(window)!;scannerCombo.ItemsSource=new[]{new ScannerDevice("EPSON GT-S650","EPSON GT-S650（TWAIN）",true)};scannerCombo.SelectedIndex=0;
        Require(((ComboBox)typeof(AlbumScanWindow).GetField("dpi",flags)!.GetValue(window)!).IsEnabled&&!((WrapPanel)typeof(AlbumScanWindow).GetField("quality",flags)!.GetValue(window)!).IsEnabled,"direct TWAIN permits resolution but disables unused WIA settings");
        ((CheckBox)typeof(AlbumScanWindow).GetField("showDriverSettings",flags)!.GetValue(window)!).IsChecked=true;
        Require(!((ComboBox)typeof(AlbumScanWindow).GetField("dpi",flags)!.GetValue(window)!).IsEnabled,"driver UI mode disables local resolution");
        ((CheckBox)typeof(AlbumScanWindow).GetField("showDriverSettings",flags)!.GetValue(window)!).IsChecked=false;
        ((ObservableCollection<AlbumScanPage>)typeof(AlbumScanWindow).GetField("pages",flags)!.GetValue(window)!).Add(new(source,"Booklet001.jpg"));
        var list=(ListBox)typeof(AlbumScanWindow).GetField("list",flags)!.GetValue(window)!;list.SelectedIndex=0;
        var preview=(Image)typeof(AlbumScanWindow).GetField("preview",flags)!.GetValue(window)!;var frame=new System.Windows.Threading.DispatcherFrame();var started=DateTime.UtcNow;var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(_,_)=>{if(preview.Source is not null||DateTime.UtcNow-started>TimeSpan.FromSeconds(5)){timer.Stop();frame.Continue=false;}};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);Require(preview.Source is not null,"selected scanned image loads into preview on UI dispatcher");
        var panel=(DockPanel)window.Content;panel.Background=window.Background;panel.Measure(new Size(968,620));panel.Arrange(new Rect(0,0,968,620));panel.UpdateLayout();
        Require(((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)).Foreground is SolidColorBrush brush&&brush.Color.R<100,"scan list filenames have explicit readable foreground");
        var render=new RenderTargetBitmap(968,620,96,96,PixelFormats.Pbgra32);render.Render(panel);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(render));using(var file=File.Create(output))png.Save(file);
        Console.WriteLine("Rendered scanner UI: "+output);Console.WriteLine("Test fixture retained: "+root);
    }
    internal static void Device(string outputFolder)
    {
        var devices=ScannerService.Devices().GetAwaiter().GetResult();foreach(var device in devices)Console.WriteLine("WIA: "+device.Name);
        var scanner=devices.First(d=>d.Name.Contains("GT-S650"));
        var result=ScannerService.Scan(scanner,100,3,outputFolder).GetAwaiter().GetResult();var bitmap=ScannerService.Load(result);
        if(bitmap.PixelWidth<100||bitmap.PixelHeight<100)throw new Exception("Invalid scanned size");
        Console.WriteLine($"PASS direct WIA scan through application service: {bitmap.PixelWidth}x{bitmap.PixelHeight}; {result}");
    }
    internal static void Album(string folder)
    {
        var album=ZipAlbumReader.OpenFolder(folder);
        if(album.Tracks.Count==0)throw new Exception("No tracks found");
        Console.WriteLine($"PASS album can be rediscovered: {album.Tracks.Count} tracks; {album.ImageCount} images; {album.Tracks[0].Album}");
    }
}
