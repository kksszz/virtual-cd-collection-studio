using System.IO;
using ZipMp3Player;
internal static class ScanResumeChecks
{
    internal static void Run()
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
        var root=Path.Combine(Path.GetTempPath(),"vccs-resume-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var music=Path.Combine(root,"album");Directory.CreateDirectory(music);
        var track=Path.Combine(music,"01.wav");File.WriteAllText(track,"fixture");
        var path=Path.Combine(root,"resume.jsonl");var store=new LibraryScanCheckpoint(path);
        var stamp=LibraryScanCheckpoint.Stamp(music,CancellationToken.None);
        store.Save(music,stamp,new ZipAlbum{Path=music,Tracks=[],Images=[],TextFiles=[]},CancellationToken.None);
        var resumed=new LibraryScanCheckpoint(path);
        Check(resumed.Find(music,stamp)?.Path==music,"completed album restored across process state");
        File.AppendAllText(path,"{partial");
        Check(new LibraryScanCheckpoint(path).Find(music,stamp)!=null,"interrupted final journal entry ignored");
        File.AppendAllText(track,"changed");var changed=LibraryScanCheckpoint.Stamp(music,CancellationToken.None);
        Check(resumed.Find(music,changed)==null,"changed file requires reanalysis");
        resumed.Save(music,changed,new ZipAlbum{Path=music,Tracks=[],Images=[],TextFiles=[]},CancellationToken.None);
        Check(new LibraryScanCheckpoint(path).Find(music,changed)!=null,"append after interrupted line recovers");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{LibraryScanCheckpoint.Stamp(music,cancellation.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){Console.WriteLine("PASS cancellation");}
        resumed.Complete();Check(!File.Exists(path),"completed scan clears only its checkpoint");
        var gate=new DataOperationGate();var scope=gate.Begin()!;scope.Dispose();scope.Dispose();Check(gate.ActiveCount==0,"import releases shutdown gate before scan (idempotent)");
        var scanRoot=Path.Combine(root,"scan");Directory.CreateDirectory(scanRoot);
        foreach(var name in new[]{"A","B"}){
            var folder=Path.Combine(scanRoot,name);Directory.CreateDirectory(folder);
            using var writer=new NAudio.Wave.WaveFileWriter(Path.Combine(folder,"01.wav"),new NAudio.Wave.WaveFormat(44100,16,2));writer.Write(new byte[176400],0,176400);
        }
        var worker=typeof(MainWindow).GetMethod("ScanWorker",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        var updateType=worker.GetParameters()[1].ParameterType.GetGenericArguments()[0];
        using var stopAfterAlbum=new CancellationTokenSource();
        var progress=Activator.CreateInstance(typeof(CancelAfterAlbum<>).MakeGenericType(updateType),stopAfterAlbum)!;
        try{worker.Invoke(null,[new[]{scanRoot},progress,stopAfterAlbum.Token,new LibraryScanCheckpoint(path),Array.Empty<string>()]);throw new Exception("scan did not stop");}
        catch(System.Reflection.TargetInvocationException ex)when(ex.InnerException is OperationCanceledException){Console.WriteLine("PASS worker stops between albums");}
        // Exclusive audio lock proves the resumed scanner uses the completed album rather than decoding it again.
        using var locked=File.Open(Path.Combine(scanRoot,"A","01.wav"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var silent=Activator.CreateInstance(typeof(Progress<>).MakeGenericType(updateType))!;
        var result=(System.Collections.ICollection)worker.Invoke(null,[new[]{scanRoot},silent,CancellationToken.None,new LibraryScanCheckpoint(path),Array.Empty<string>()])!;
        Check(result.Count==2,"resumed worker reuses first album and parses remaining album");
    }
    public sealed class CancelAfterAlbum<T>(CancellationTokenSource cancellation):IProgress<T>
    {
        public void Report(T value){if(typeof(T).GetProperty("Album")?.GetValue(value)!=null)cancellation.Cancel();}
    }
}
