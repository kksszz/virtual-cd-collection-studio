using System.Collections.Concurrent;
using NAudio.Wave;

namespace ZipMp3Player;

/// <summary>Media Foundation COM objects never leave their owning MTA thread.</summary>
internal sealed class ThreadedFlacReader : WaveStream
{
    private readonly BlockingCollection<Action> work=new();
    private readonly Thread thread;
    private MediaFoundationReader reader=null!;
    private readonly WaveFormat format;
    private readonly long length;
    private bool disposed;
    public ThreadedFlacReader(string path){
        thread=new Thread(()=>{foreach(var action in work.GetConsumingEnumerable())action();}){IsBackground=true,Name="FLAC decoder"};
        thread.SetApartmentState(ApartmentState.MTA);thread.Start();
        try{var info=Call(()=>{reader=new MediaFoundationReader(path);return (reader.WaveFormat,reader.Length);});format=info.WaveFormat;length=info.Length;}
        catch{work.CompleteAdding();thread.Join();work.Dispose();throw;}
    }
    private T Call<T>(Func<T> action){
        ObjectDisposedException.ThrowIf(disposed,this);
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(()=>{try{result.SetResult(action());}catch(Exception ex){result.SetException(ex);}});
        return result.Task.GetAwaiter().GetResult();
    }
    public override WaveFormat WaveFormat=>format;
    public override long Length=>length;
    public override long Position{get=>Call(()=>reader.Position);set=>Call(()=>{reader.Position=value;return 0;});}
    public override int Read(byte[] buffer,int offset,int count)=>Call(()=>reader.Read(buffer,offset,count));
    protected override void Dispose(bool disposing){if(disposing&&!disposed){try{Call(()=>{reader.Dispose();return 0;});}finally{disposed=true;work.CompleteAdding();thread.Join();work.Dispose();}}base.Dispose(disposing);}
}
