package jp.virtualcd.player.archive;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.FileChannel;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/** Read-only ZIP directory parser. Never extracts files; entries describe bounded byte ranges.
 * ZIP64, encryption, split archives and compressed MP3 are deliberately unsupported in v0.1. */
public final class StoredZipIndex {
    public static final class Entry {
        public final String name;
        public final long offset, length, packedLength;
        public final int method;
        public Entry(String name, long offset, long length) { this(name,offset,length,length,0); }
        public Entry(String name, long offset, long length, long packedLength, int method) { this.name=name; this.offset=offset; this.length=length; this.packedLength=packedLength; this.method=method; }
        public String toString() { return name; }
    }
    private static long u32(ByteBuffer b, int n) { return Integer.toUnsignedLong(b.getInt(n)); }
    private static int u16(ByteBuffer b, int n) { return Short.toUnsignedInt(b.getShort(n)); }
    private static ByteBuffer read(FileChannel channel, long offset, int length) throws IOException {
        if (offset < 0 || length < 0 || offset > channel.size() - length) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIP範囲が不正です","Invalid ZIP range"));
        ByteBuffer b=ByteBuffer.allocate(length).order(ByteOrder.LITTLE_ENDIAN);
        while (b.hasRemaining()) {
            int n=channel.read(b, offset+b.position());
            if (n <= 0) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPを読み取れません","Unable to read ZIP"));
        }
        b.flip(); return b;
    }
    public static List<Entry> read(FileChannel channel) throws IOException {
        return readEntries(channel,false);
    }
    public static List<Entry> readImages(FileChannel channel) throws IOException {
        return readEntries(channel,true);
    }
    private static List<Entry> readEntries(FileChannel channel, boolean images) throws IOException {
        long size=channel.size();
        if (size < 22) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPではありません","Not a ZIP file"));
        int tailSize=(int)Math.min(65557, size);
        ByteBuffer tail=read(channel, size-tailSize, tailSize);
        int e=-1;
        for (int i=tailSize-22;i>=0;i--) if (u32(tail,i)==0x06054b50L && i+22+u16(tail,i+20)==tailSize) {e=i;break;}
        if (e<0) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIP終端情報がありません。PC側で無圧縮ZIPへ変換してください","ZIP end record is missing. Convert to an uncompressed ZIP on the PC."));
        if (u16(tail,e+4)!=0 || u16(tail,e+6)!=0 || u16(tail,e+8)!=u16(tail,e+10)) throw new IOException(jp.virtualcd.player.LanguageStrings.text("分割ZIPは未対応です","Split ZIP files are not supported"));
        int count=u16(tail,e+10);
        long central=u32(tail,e+16), centralSize=u32(tail,e+12);
        if (count==65535 || central==0xffffffffL || centralSize==0xffffffffL) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIP64は未対応です","ZIP64 is not supported"));
        if (central+centralSize>size-tailSize+e) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPディレクトリ範囲が不正です","Invalid ZIP directory range"));
        List<Entry> result=new ArrayList<>(); var names=new java.util.HashSet<String>(); long position=central;
        for (int i=0;i<count;i++) {
            if (Thread.currentThread().isInterrupted()) throw new IOException(jp.virtualcd.player.LanguageStrings.text("読込を中止しました","Loading canceled"));
            ByteBuffer h=read(channel,position,46);
            if (u32(h,0)!=0x02014b50L) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPディレクトリが壊れています","Corrupt ZIP directory"));
            int flags=u16(h,8), method=u16(h,10), nameSize=u16(h,28);
            byte[] nameBytes=read(channel,position+46,nameSize).array();
            String name=new String(nameBytes,(flags&2048)!=0?StandardCharsets.UTF_8:Charset.forName("windows-31j"));
            position+=46L+nameSize+u16(h,30)+u16(h,32);
            if (position>central+centralSize) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPディレクトリが途切れています","Truncated ZIP directory"));
            String lower=name.toLowerCase(Locale.ROOT);
            if (images ? !(lower.endsWith(".jpg")||lower.endsWith(".jpeg")||lower.endsWith(".png")||lower.endsWith(".webp")) : !jp.virtualcd.player.library.AudioFormats.audio(name)) continue;
            if (!names.add(name)) throw new IOException(jp.virtualcd.player.LanguageStrings.text("同名のMP3が重複しています","Duplicate MP3 filenames"));
            if(images && ((flags&1)!=0 || (method!=0&&method!=8))) continue;
            if (!images && ((flags&1)!=0 || method!=0)) throw new IOException(jp.virtualcd.player.LanguageStrings.text("圧縮・暗号化された音声です。PC側で無圧縮ZIPに変換してください","Compressed or encrypted audio. Convert to an uncompressed ZIP on the PC."));
            long length=u32(h,24), localOffset=u32(h,42);
            long packed=u32(h,20);
            if (length==0xffffffffL || localOffset==0xffffffffL || packed==0xffffffffL || (method==0&&packed!=length)) throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応のZIP形式です","Unsupported ZIP format"));
            ByteBuffer local=read(channel,localOffset,30);
            if (u32(local,0)!=0x04034b50L || u16(local,8)!=method || (u16(local,6)&1)!=0) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIPローカル情報が不正です","Invalid ZIP local header"));
            long offset=localOffset+30+u16(local,26)+u16(local,28);
            if (offset>central || packed>central-offset) throw new IOException(jp.virtualcd.player.LanguageStrings.text("データ範囲が不正です","Invalid data range"));
            result.add(new Entry(name,offset,length,packed,method));
        }
        result.sort((a,b)->naturalKey(a.name).compareTo(naturalKey(b.name)));
        if (!images && result.isEmpty()) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIP内に対応音声（MP3/FLAC/WAV/M4A）がありません","No supported audio (MP3/FLAC/WAV/M4A) in ZIP"));
        return result;
    }
    private static String naturalKey(String name) {
        var matcher=java.util.regex.Pattern.compile("[0-9]+").matcher(name.toLowerCase(Locale.ROOT));
        var out=new StringBuffer();
        while (matcher.find()) matcher.appendReplacement(out, "000000000000".substring(0,Math.max(0,12-matcher.group().length()))+matcher.group());
        matcher.appendTail(out); return out.toString();
    }
}
