using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZipMp3Player;

internal sealed class CdAudioSource : IDisposable
{
    private readonly SafeFileHandle handle;
    internal CueAlbumReader.Disc Disc { get; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool DeviceIoControl(SafeFileHandle file,uint code,byte[]? input,int inputSize,byte[] output,int outputSize,out int returned,IntPtr overlapped);
    internal CdAudioSource(string drive)
    {
        if(drive.Length<2||!char.IsAsciiLetter(drive[0])||drive[1]!=':')throw new ArgumentException("CDドライブを選択してください。");
        if(new DriveInfo(drive[..2]).DriveType!=DriveType.CDRom)throw new IOException("CDドライブではありません。");
        handle=CreateFile(@"\\.\"+drive[..2],0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),"CDドライブを開けません。");}
        try{Disc=ReadToc();Control(0x24804,[1],[]);}catch{handle.Dispose();throw;}
    }
    private int Control(uint code,byte[]? input,byte[] output)
    {
        if(!DeviceIoControl(handle,code,input,input?.Length??0,output,output.Length,out int count,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"CDの読み取りに失敗しました。ディスク・接続を確認してください。");
        return count;
    }
    private CueAlbumReader.Disc ReadToc(){var bytes=new byte[804];int length=Control(0x24000,null,bytes);return ParseToc(bytes.AsSpan(0,length));}
    internal static CueAlbumReader.Disc ParseToc(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<12||bytes[2]!=1||bytes[3]<1||bytes[3]>99)throw new InvalidDataException("通常の音楽CDのTOCではありません。");
        int tracks=bytes[3];if(bytes.Length<4+(tracks+1)*8)throw new InvalidDataException("CDの曲情報が不足しています。");
        var list=new List<CueAlbumReader.CueTrack>();long end=0;
        for(int i=0;i<=tracks;i++){
            var entry=bytes.Slice(4+i*8,8);
            if(entry[6]>59||entry[7]>74)throw new InvalidDataException("CDの曲位置が不正です。");
            long sector=(entry[5]*60L+entry[6])*75+entry[7]-150;
            if(sector<0||(i>0&&sector<=list[^1].Frame))throw new InvalidDataException("CDの曲順が不正です。");
            if(i==tracks){if(entry[2]!=0xaa)throw new InvalidDataException("CDの終端が不正です。");end=sector;break;}
            if(entry[2]!=i+1||(entry[1]&13)!=0)throw new NotSupportedException("初期版ではデータ混在CD・プリエンファシスCD・4チャンネルCDには対応していません。");
            list.Add(new(i+1,sector,$"Track {i+1:00}",""));
        }
        return new("","","","",1,end,list,"");
    }
    internal void VerifyDisc(){if(ReadToc().Toc!=Disc.Toc)throw new IOException("CDが変更されたため中断しました。");}
    internal byte[] Read(long sector,int count)
    {
        if(sector<0||count<1||count>16||sector+count>Disc.Frames)throw new ArgumentOutOfRangeException(nameof(sector));
        var request=new byte[16];BitConverter.GetBytes(sector*2048).CopyTo(request,0);BitConverter.GetBytes(count).CopyTo(request,8);BitConverter.GetBytes(2).CopyTo(request,12);
        var output=new byte[count*2352];if(Control(0x2403e,request,output)!=output.Length)throw new IOException("CDの音声データが不足しています。");return output;
    }
    public void Dispose(){try{Control(0x24804,[0],[]);}catch(Win32Exception){/* Closing the handle also releases its removal lock. */}finally{handle.Dispose();}}
}
