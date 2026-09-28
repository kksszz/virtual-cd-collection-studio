// x86 .NET Framework bridge for installed 32-bit TWAIN data sources.
// TWAIN wire layouts follow the TWAIN Working Group specification (pack=2).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential,Pack=2,CharSet=CharSet.Ansi)]
    internal struct Version {public ushort Major,Minor,Language,Country;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=34)]public string Info;}
    [StructLayout(LayoutKind.Sequential,Pack=2,CharSet=CharSet.Ansi)]
    internal struct Identity {public uint Id;public Version Version;public ushort ProtocolMajor,ProtocolMinor;public uint Groups;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=34)]public string Manufacturer;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=34)]public string Family;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=34)]public string Product;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]internal struct Capability {public ushort Id,Container;public IntPtr Handle;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]internal struct CustomData {public uint Length;public IntPtr Handle;}
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry",CharSet=CharSet.Ansi)]static extern ushort Parent(ref Identity app,IntPtr dest,uint group,ushort type,ushort message,ref IntPtr value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry",CharSet=CharSet.Ansi)]static extern ushort Source(ref Identity app,IntPtr dest,uint group,ushort type,ushort message,ref Identity value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry",CharSet=CharSet.Ansi)]static extern ushort Cap(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref Capability value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry",CharSet=CharSet.Ansi)]static extern ushort Custom(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref CustomData value);
    [StructLayout(LayoutKind.Sequential,Pack=2)]struct UserInterface{public ushort Show,Modal;public IntPtr Parent;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]struct TwainEvent{public IntPtr Event;public ushort Message;}
    [StructLayout(LayoutKind.Sequential)]struct NativeMessage{public IntPtr Window;public uint Message;public IntPtr WParam,LParam;public uint Time;public int X,Y;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]struct Pending{public ushort Count;public uint End;}
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry")]static extern ushort Ui(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref UserInterface value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry")]static extern ushort Event(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref TwainEvent value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry")]static extern ushort Native(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref IntPtr value);
    [DllImport("twain_32.dll",EntryPoint="DSM_Entry")]static extern ushort Transfers(ref Identity app,ref Identity dest,uint group,ushort type,ushort message,ref Pending value);
    [DllImport("kernel32.dll")]static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
    [DllImport("kernel32.dll")]static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")]static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr handle);
    static Identity app=new Identity{Version=new Version{Major=1,Minor=0,Language=0,Country=1,Info="Album scan bridge"},ProtocolMajor=1,ProtocolMinor=9,Groups=3,Manufacturer="Virtual CD Collection Studio",Family="Artwork",Product="Virtual CD Album Scan"};
    static Identity device;
    static bool openedManager,openedSource;
    static IntPtr window;
    static bool enabled;
    static void Set(ushort id,ushort type,uint value){
        var cap=new Capability{Id=id,Container=5,Handle=GlobalAlloc(0x42,new UIntPtr(6))};
        if(cap.Handle==IntPtr.Zero)throw new OutOfMemoryException();
        try{var p=GlobalLock(cap.Handle);if(p==IntPtr.Zero)throw new IOException("TWAIN capability lock failed");try{Marshal.WriteInt16(p,(short)type);Marshal.WriteInt32(p,2,(int)value);}finally{GlobalUnlock(cap.Handle);}var rc=Cap(ref app,ref device,1,1,6,ref cap);if(rc!=0&&rc!=2)throw new IOException("TWAIN setting 0x"+id.ToString("x4")+" failed (RC="+rc+")");}finally{GlobalFree(cap.Handle);}
    }
    static void SaveDib(IntPtr handle,string path){
        ulong size=GlobalSize(handle).ToUInt64();if(size<40||size>600000000)throw new IOException("Invalid or oversized scanner image");
        var p=GlobalLock(handle);if(p==IntPtr.Zero)throw new IOException("Scanner image lock failed");
        try{var dib=new byte[(int)size];Marshal.Copy(p,dib,0,dib.Length);int header=BitConverter.ToInt32(dib,0),width=BitConverter.ToInt32(dib,4),height=BitConverter.ToInt32(dib,8);ushort bits=BitConverter.ToUInt16(dib,14);uint compression=BitConverter.ToUInt32(dib,16),colors=BitConverter.ToUInt32(dib,32);
            if(header<40||header>dib.Length||width<=0||height==0||(long)width*Math.Abs((long)height)>100000000||bits>32||compression>3)throw new IOException("Unsupported scanner bitmap");
            if(colors==0&&bits<=8)colors=1u<<bits;
            long offset=14L+header+colors*4L+(header==40&&compression==3?12:0);if(offset>14L+(long)size)throw new IOException("Invalid bitmap palette");
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write((ushort)0x4d42);writer.Write((uint)(size+14));writer.Write(0u);writer.Write((uint)offset);writer.Write(dib);writer.Flush();stream.Position=0;using(var image=Image.FromStream(stream))using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))image.Save(file,ImageFormat.Png);}
        }finally{GlobalUnlock(handle);}
    }
    sealed class ScanLoop:IMessageFilter{
        readonly string folder;readonly ApplicationContext context;readonly IntPtr buffer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMessage)));public Exception Failure;
        public ScanLoop(string directory,ApplicationContext ctx){folder=directory;context=ctx;}
        public bool PreFilterMessage(ref Message message){
            if(!enabled)return false;
            var native=new NativeMessage{Window=message.HWnd,Message=(uint)message.Msg,WParam=message.WParam,LParam=message.LParam};Marshal.StructureToPtr(native,buffer,false);var evt=new TwainEvent{Event=buffer};var rc=Event(ref app,ref device,1,2,0x601,ref evt);
            if(rc!=4)return false;
            try{if(evt.Message==0x101){
                // Transfer all selected frames before disabling the source. The batch editor owns naming.
                ushort remaining;do{IntPtr dib=IntPtr.Zero;ushort transfer=Native(ref app,ref device,2,0x104,1,ref dib);try{if(transfer==6){if(dib==IntPtr.Zero)throw new IOException("Scanner returned an empty image");var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");SaveDib(dib,path);Console.WriteLine("IMAGE\t"+path);Console.Out.Flush();}else if(transfer!=3)throw new IOException("TWAIN transfer failed (RC="+transfer+")");}finally{if(dib!=IntPtr.Zero)GlobalFree(dib);}
                    var pending=new Pending();if(Transfers(ref app,ref device,1,5,0x701,ref pending)!=0)throw new IOException("TWAIN transfer completion failed");remaining=pending.Count;if(transfer==3)remaining=0;
                }while(remaining!=0);Finish();
            }else if(evt.Message==0x102||evt.Message==0x103)Finish();}
            catch(Exception ex){Failure=ex;Finish();}return true;
        }
        void Finish(){var pending=new Pending();Transfers(ref app,ref device,1,5,7,ref pending);context.ExitThread();}
        public void Release(){Marshal.FreeHGlobal(buffer);}
    }
    static string CapabilityValue(ushort id,ushort message=2)
    {
        var cap=new Capability{Id=id};ushort code=Cap(ref app,ref device,1,1,message,ref cap);
        if(code!=0)return "unsupported (RC="+code+")";
        try{
            var p=GlobalLock(cap.Handle);if(p==IntPtr.Zero)throw new IOException("TWAIN capability lock failed");
            try{
                int offset=2;ushort type=(ushort)Marshal.ReadInt16(p);uint item;
                if(cap.Container==5)item=(uint)Marshal.ReadInt32(p,offset);
                else if(cap.Container==6)item=(uint)Marshal.ReadInt32(p,18);
                else if(cap.Container==4){uint index=(uint)Marshal.ReadInt32(p,6);offset=14+(int)index*(type==1||type==4||type==6?2:4);item=type==1||type==4||type==6?(uint)(ushort)Marshal.ReadInt16(p,offset):(uint)Marshal.ReadInt32(p,offset);}
                else return "container="+cap.Container;
                if(type==7)return ((short)(item&0xffff)+(item>>16)/65536d).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(type==1||type==4||type==6)item&=0xffff;
                return "type="+type+" value="+item;
            }finally{GlobalUnlock(cap.Handle);}
        }finally{if(cap.Handle!=IntPtr.Zero)GlobalFree(cap.Handle);}
    }
    static void SupportedCaps()
    {
        var cap=new Capability{Id=0x1005};if(Cap(ref app,ref device,1,1,1,ref cap)!=0)return;
        try{var p=GlobalLock(cap.Handle);try{int count=Marshal.ReadInt32(p,2);if(cap.Container!=3||count<0||count>1024)return;for(int i=0;i<count;i++){ushort id=(ushort)Marshal.ReadInt16(p,6+i*2);Console.WriteLine("SUPPORTED\t0x"+id.ToString("x4")+"\t"+CapabilityValue(id));}}finally{GlobalUnlock(cap.Handle);}}finally{GlobalFree(cap.Handle);}
    }
    [STAThread]static int Main(string[] args)
    {
        Console.OutputEncoding=Encoding.UTF8;
        if(args.Length==2&&args[0]=="dib-check")try{
            byte[] dib=new byte[56];using(var stream=new MemoryStream(dib))using(var writer=new BinaryWriter(stream)){writer.Write(40);writer.Write(2);writer.Write(2);writer.Write((ushort)1);writer.Write((ushort)24);writer.Write(0);writer.Write(16);writer.Write(23622);writer.Write(23622);writer.Write(0);writer.Write(0);for(int n=40;n<56;n++)dib[n]=(byte)(n*3);}
            var h=GlobalAlloc(0x42,new UIntPtr((uint)dib.Length));try{var p=GlobalLock(h);Marshal.Copy(dib,0,p,dib.Length);GlobalUnlock(h);SaveDib(h,args[1]);using(var image=Image.FromFile(args[1]))if(image.Width!=2||image.Height!=2||Math.Abs(image.HorizontalResolution-600)>1)throw new IOException("DIB pixel/resolution verification failed");}finally{GlobalFree(h);}Console.WriteLine("PASS native DIB converted to PNG; size and 600dpi preserved");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        using(var form=new Form())try{
            window=form.Handle;if(Parent(ref app,IntPtr.Zero,1,4,0x301,ref window)!=0)throw new IOException("TWAIN manager open failed");openedManager=true;
            var identity=new Identity();ushort code=Source(ref app,IntPtr.Zero,1,3,4,ref identity);bool found=false;
            string requested=args.Length>1?args[1]:"EPSON GT-S650";
            while(code==0){Console.WriteLine("SOURCE\t"+identity.Product);if(identity.Product==requested){device=identity;found=true;}code=Source(ref app,IntPtr.Zero,1,3,5,ref identity);}
            if(args.Length>0&&args[0]=="list")return 0;
            if(!found)throw new IOException("GT-S650 TWAIN source not found");
            if(Source(ref app,IntPtr.Zero,1,3,0x401,ref device)!=0)throw new IOException("GT-S650 TWAIN source open failed (close other scanning apps)");openedSource=true;
            if(args.Length>0&&args[0]=="configure-check"){
                Set(0x0102,4,0);Set(0x0101,4,2);Set(0x112b,4,24);Set(0x1118,7,600);Set(0x1119,7,600);Set(0x0103,4,0);
                Console.WriteLine("PIXEL\t"+CapabilityValue(0x0101));Console.WriteLine("DEPTH\t"+CapabilityValue(0x112b));Console.WriteLine("XRES\t"+CapabilityValue(0x1118));Console.WriteLine("YRES\t"+CapabilityValue(0x1119));return 0;
            }
            if(args.Length>0&&(args[0]=="scan"||args[0]=="scan-direct")){
                bool showUi=args[0]=="scan";
                if(args.Length<3)throw new ArgumentException("scan requires source and output directory");var folder=Path.GetFullPath(args[2]);if(!Directory.Exists(folder))throw new DirectoryNotFoundException(folder);
                int resolution=args.Length>3?int.Parse(args[3]):600;int pixelType=args.Length>4?int.Parse(args[4]):2;
                if(resolution<50||resolution>1200||pixelType<0||pixelType>2)throw new ArgumentException("Invalid scan settings");
                if(!showUi&&!CapabilityValue(0x100e,1).EndsWith("value=1"))throw new IOException("This TWAIN driver requires its settings window");
                Set(0x0102,4,0); // inches
                Set(0x0101,4,(uint)pixelType);Set(0x112b,4,(uint)(pixelType==2?24:pixelType==1?8:1));Set(0x1118,7,(uint)resolution);Set(0x1119,7,(uint)resolution);Set(0x0103,4,0); // native DIB
                if(!showUi)Set(0x100b,6,0); // no driver progress window
                using(var context=new ApplicationContext()){
                    var filter=new ScanLoop(folder,context);Application.AddMessageFilter(filter);
                    try{var ui=new UserInterface{Show=(ushort)(showUi?1:0),Modal=0,Parent=window};if(Ui(ref app,ref device,1,9,0x502,ref ui)!=0)throw new IOException("TWAIN scanner could not be enabled");enabled=true;Application.Run(context);if(filter.Failure!=null)throw filter.Failure;}
                    finally{Application.RemoveMessageFilter(filter);filter.Release();}
                }
                Console.WriteLine("DONE");return 0;
            }
            foreach(var id in new ushort[]{0x100e,0x1014,0x1015,0x0101,0x1118,0x1119,0x112b,0x1100,0x1101,0x1103,0x1108})Console.WriteLine("CAP\t0x"+id.ToString("x4")+"\t"+CapabilityValue(id));
            SupportedCaps();
            foreach(var id in new ushort[]{0x100e,0x1014,0x1015})Console.WriteLine("GET\t0x"+id.ToString("x4")+"\t"+CapabilityValue(id,1));
            var custom=new CustomData();code=Custom(ref app,ref device,1,12,1,ref custom);
            Console.WriteLine("CUSTOM\tRC="+code+" bytes="+custom.Length);if(custom.Handle!=IntPtr.Zero)GlobalFree(custom.Handle);
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
        finally{if(enabled){var ui=new UserInterface{Parent=window};Ui(ref app,ref device,1,9,0x501,ref ui);}if(openedSource)Source(ref app,IntPtr.Zero,1,3,0x402,ref device);if(openedManager)Parent(ref app,IntPtr.Zero,1,4,0x302,ref window);}
    }
}
