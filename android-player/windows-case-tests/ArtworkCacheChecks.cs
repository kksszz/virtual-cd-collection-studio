using System.IO;
using System.Reflection;
using ZipMp3Player;

internal static class ArtworkCacheChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"artwork-cache-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var type=typeof(MainWindow).Assembly.GetType("ZipMp3Player.ArtworkThumbnailCache")!;
        var clear=type.GetMethod("Clear",BindingFlags.Static|BindingFlags.NonPublic)!;
        int Count(object result,string property)=>(int)result.GetType().GetProperty(property)!.GetValue(result)!;
        if(Count(clear.Invoke(null,[root])!,"Deleted")!=0)throw new Exception("Missing cache");
        var cache=Path.Combine(root,"thumbnail-cache");Directory.CreateDirectory(cache);
        var valid=Path.Combine(cache,new string('A',20)+"-"+new string('B',20)+".png");
        var locked=Path.Combine(cache,new string('C',20)+"-"+new string('D',20)+".png");
        File.WriteAllBytes(valid,[1,2,3]);File.WriteAllBytes(locked,[4,5]);
        var original=Path.Combine(root,"cover.png");File.WriteAllBytes(original,[6,7]);
        var unrelated=Path.Combine(cache,"cover.png");File.WriteAllBytes(unrelated,[8,9]);
        Directory.CreateDirectory(Path.Combine(cache,"nested"));var nested=Path.Combine(cache,"nested",Path.GetFileName(valid));File.WriteAllBytes(nested,[10]);
        foreach(string name in new[]{"favorites.json","settings.json","song.mp3"})File.WriteAllText(Path.Combine(root,name),"preserve");
        using(var handle=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
            var result=clear.Invoke(null,[root])!;
            if(Count(result,"Deleted")!=1||Count(result,"Failed")!=1)throw new Exception("Deletion/locked counts");
            if(File.Exists(valid))throw new Exception("Cache not removed");
        }
        if(Count(clear.Invoke(null,[root])!,"Deleted")!=1||Count(clear.Invoke(null,[root])!,"Deleted")!=0)throw new Exception("Retry/idempotence");
        if(!File.ReadAllBytes(original).SequenceEqual(new byte[]{6,7})||!File.ReadAllBytes(unrelated).SequenceEqual(new byte[]{8,9})||!File.Exists(nested))throw new Exception("Non-cache modified");
        foreach(string name in new[]{"favorites.json","settings.json","song.mp3"})if(File.ReadAllText(Path.Combine(root,name))!="preserve")throw new Exception("User data modified");
        Console.WriteLine("PASS artwork cache: missing folder, matching files only, no recursion, locked-file report/retry, original artwork/music/settings/favorites preserved");
    }
}
