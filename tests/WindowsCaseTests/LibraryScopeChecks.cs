using System.IO;
using System.Reflection;
using ZipMp3Player;

internal static class LibraryScopeChecks
{
    internal static void Run()
    {
        void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS "+text);}
        var root=Path.Combine(Path.GetTempPath(),"vccs-library-scope-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var active=Path.Combine(root,"enabled");var hidden=Path.Combine(root,"disabled");var nestedHidden=Path.Combine(active,"hidden-disc");
        foreach(var directory in new[]{active,hidden,nestedHidden}){
            Directory.CreateDirectory(directory);using var wav=new NAudio.Wave.WaveFileWriter(Path.Combine(directory,"01.wav"),new NAudio.Wave.WaveFormat(44100,16,2));wav.Write(new byte[176400],0,176400);
        }
        var enabled=LibraryScanScope.EnabledRoots([active,hidden],[hidden,nestedHidden]);Check(enabled.SequenceEqual(new[]{active}),"only checked roots selected for scanning");
        var worker=typeof(MainWindow).GetMethod("ScanWorker",BindingFlags.Static|BindingFlags.NonPublic)!;var updateType=worker.GetParameters()[1].ParameterType.GetGenericArguments()[0];var progress=Activator.CreateInstance(typeof(Progress<>).MakeGenericType(updateType))!;
        using var hiddenLock=File.Open(Path.Combine(hidden,"01.wav"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);using var nestedLock=File.Open(Path.Combine(nestedHidden,"01.wav"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var results=(System.Collections.IList)worker.Invoke(null,[enabled,progress,CancellationToken.None,null,new[]{hidden,nestedHidden}])!;
        Check(results.Count==1,"scanner neither traverses nor parses hidden roots/subfolders");
        var oldHidden=new ZipAlbum{Path=hidden,Tracks=[new ZipTrack{Title="retained"}]};var changed=new ZipAlbum{Path=active,Tracks=[new ZipTrack{Title="new"}]};
        var merged=LibraryScanScope.Merge([oldHidden],[changed],enabled);Check(merged.Count==2&&merged.Any(a=>ReferenceEquals(a,oldHidden)),"scoped refresh keeps unchecked albums in cache");
        Check(LibraryScanScope.Merge([oldHidden],[],[]).Count==1,"no checked roots leaves saved library intact");
        Console.WriteLine("Fixtures: "+root);
    }
}
