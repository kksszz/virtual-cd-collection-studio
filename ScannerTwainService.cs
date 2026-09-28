using System.Diagnostics;
using System.IO;
using System.Text;

namespace ZipMp3Player;

internal static class ScannerTwainService
{
    private static async Task<string[]> Run(params string[] args)
    {
        var helper=Path.Combine(AppContext.BaseDirectory,"ScannerTools","ScannerTwainBridge.exe");
        if(!File.Exists(helper))throw new IOException("TWAIN連携プログラムがありません。ScannerToolsフォルダーを含めてアプリを配置してください。");
        var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("TWAIN連携プログラムを起動できません。");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();var lines=(await output).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        if(process.ExitCode!=0)throw new IOException("TWAINでの読み取りに失敗しました。他のスキャンアプリを閉じ、接続を確認してください。\n"+await error);
        return lines;
    }
    internal static async Task<List<ScannerDevice>> Devices()=> (await Run("list")).Where(s=>s.StartsWith("SOURCE\t",StringComparison.Ordinal)).Select(s=>s[7..]).Where(s=>!s.StartsWith("WIA-",StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).Select(s=>new ScannerDevice(s,s+"（TWAIN）",true)).ToList();
    internal static async Task<List<string>> Scan(ScannerDevice device,string directory,bool showSettings=true,int dpi=600,int color=0)
    {
        var lines=await Run(showSettings?"scan":"scan-direct",device.Id,directory,dpi.ToString(System.Globalization.CultureInfo.InvariantCulture),(color==0?2:color==1?1:0).ToString());var root=Path.GetFullPath(directory)+Path.DirectorySeparatorChar;
        var paths=lines.Where(s=>s.StartsWith("IMAGE\t",StringComparison.Ordinal)).Select(s=>Path.GetFullPath(s[6..])).ToList();
        foreach(var path in paths){if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new IOException("TWAIN画像の保存先を確認できませんでした。");_ = ScannerService.Load(path);}
        return paths;
    }
}
