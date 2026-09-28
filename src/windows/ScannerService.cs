using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed record ScannerDevice(string Id,string Name,bool Twain=false);

internal static class ScannerService
{
    // WIA Automation objects stay on one STA and are released before its thread exits.
    private sealed class WiaSession : IDisposable
    {
        private readonly List<object> references=[];
        internal object Keep(object value){if(value is not null&&Marshal.IsComObject(value))references.Add(value);return value!;}
        internal object Get(object target,string name,params object[] args)=>Keep(target.GetType().InvokeMember(name,BindingFlags.GetProperty,null,target,args)!);
        internal object Call(object target,string name,params object[] args)=>Keep(target.GetType().InvokeMember(name,BindingFlags.InvokeMethod,null,target,args)!);
        internal void Set(object target,string name,object value)=>target.GetType().InvokeMember(name,BindingFlags.SetProperty,null,target,[value]);
        internal object Manager()=>Keep(Activator.CreateInstance(Type.GetTypeFromProgID("WIA.DeviceManager",true)!)!);
        internal object Property(object target,string id)=>Get(Get(target,"Properties"),"Item",id);
        public void Dispose(){foreach(var value in references.AsEnumerable().Reverse())try{if(Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}catch(InvalidComObjectException){} }
    }
    private static Task<T> Sta<T>(Func<WiaSession,T> work)
    {
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{using var session=new WiaSession();result.SetResult(work(session));}catch(Exception ex){while(ex is TargetInvocationException {InnerException:not null})ex=ex.InnerException;result.SetException(ex);}}){IsBackground=true,Name="Album scanner WIA"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    internal static Task<List<ScannerDevice>> Devices()=>Sta(s=>{
        var infos=s.Get(s.Manager(),"DeviceInfos");var result=new List<ScannerDevice>();
        for(int i=1;i<=Convert.ToInt32(s.Get(infos,"Count"));i++){
            var info=s.Get(infos,"Item",i);if(Convert.ToInt32(s.Get(info,"Type"))!=1)continue;
            var properties=s.Get(info,"Properties");string name="スキャナー";
            for(int n=1;n<=Convert.ToInt32(s.Get(properties,"Count"));n++){var p=s.Get(properties,"Item",n);if((string)s.Get(p,"Name")=="Name")name=Convert.ToString(s.Get(p,"Value"))??name;}
            result.Add(new((string)s.Get(info,"DeviceID"),name));
        }return result;
    });
    internal static Task<string> Scan(ScannerDevice scanner,int dpi,int dataType,string directory,int brightness=0,int contrast=0,int threshold=128)=>Sta(s=>{
        if(dpi is <50 or >1200)throw new ArgumentOutOfRangeException(nameof(dpi));
        if(dataType is not (3 or 2 or 0)||brightness is <-1000 or >1000||contrast is <-1000 or >1000||threshold is <0 or >255)throw new ArgumentException("画質設定の値が範囲外です。");
        Directory.CreateDirectory(directory);
        var infos=s.Get(s.Manager(),"DeviceInfos");object? device=null;
        for(int i=1;i<=Convert.ToInt32(s.Get(infos,"Count"));i++){var info=s.Get(infos,"Item",i);if((string)s.Get(info,"DeviceID")==scanner.Id){device=s.Call(info,"Connect");break;}}
        if(device is null)throw new IOException("スキャナーが見つかりません。USB接続を確認して一覧を更新してください。");
        var item=s.Get(s.Get(device,"Items"),"Item",1);
        foreach(string id in new[]{"6147","6148"}){
            var p=s.Property(item,id);int min=Convert.ToInt32(s.Get(p,"SubTypeMin")),max=Convert.ToInt32(s.Get(p,"SubTypeMax"));
            if(dpi<min||dpi>max)throw new NotSupportedException($"このドライバーの対応解像度は {min}～{max}dpi です。");s.Set(p,"Value",dpi);
        }
        s.Set(s.Property(item,"4103"),"Value",dataType);
        s.Set(s.Property(item,"6154"),"Value",brightness);s.Set(s.Property(item,"6155"),"Value",contrast);
        if(dataType==0)s.Set(s.Property(item,"6159"),"Value",threshold);
        s.Set(s.Property(item,"6149"),"Value",0);s.Set(s.Property(item,"6150"),"Value",0);
        foreach(string id in new[]{"6151","6152"}){var p=s.Property(item,id);s.Set(p,"Value",s.Get(p,"SubTypeMax"));}
        if(Convert.ToInt64(s.Get(s.Property(item,"6151"),"Value"))*Convert.ToInt64(s.Get(s.Property(item,"6152"),"Value"))>100_000_000)
            throw new NotSupportedException("読み取り画像が大きすぎます。600dpi以下を選択してください。");
        var image=s.Call(item,"Transfer","{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}");
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".bmp");s.Call(image,"SaveFile",path);
        return path;
    });
    internal static BitmapSource Load(string path)
    {
        using var stream=File.OpenRead(path);var image=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];image.Freeze();return image;
    }
    internal static void Png(string source,string output)
    {
        var image=Load(source);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);encoder.Save(file);
    }
    internal static void Jpeg(string source,string output)
    {
        var image=Load(source);var encoder=new JpegBitmapEncoder{QualityLevel=95};encoder.Frames.Add(BitmapFrame.Create(image));
        using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))encoder.Save(file);
        var decoded=Load(output);if(decoded.PixelWidth!=image.PixelWidth||decoded.PixelHeight!=image.PixelHeight)throw new InvalidDataException("JPG画像の検証に失敗しました。");
    }
    internal static string Error(Exception ex)=>ex.HResult switch{
        unchecked((int)0x80210006)=>"スキャナーが使用中です。EPSON Scanなど、ほかのスキャン用アプリを閉じてください。",
        unchecked((int)0x80210005)=>"スキャナーがオフラインです。USB接続を確認してください。",
        unchecked((int)0x8021000A)=>"スキャナーとの通信に失敗しました。USB接続を確認してください。",
        _=>ex.Message};
}
