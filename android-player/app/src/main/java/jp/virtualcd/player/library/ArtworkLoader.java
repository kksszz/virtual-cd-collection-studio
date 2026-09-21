package jp.virtualcd.player.library;

import android.content.Context;
import android.graphics.*;
import android.media.MediaMetadataRetriever;
import android.os.ParcelFileDescriptor;
import jp.virtualcd.player.archive.StoredZipIndex;
import java.io.*;
import java.nio.ByteBuffer;
import java.util.*;
import java.util.zip.*;

/** Small thumbnails only. Never extracts to the card; never decodes full-resolution images. */
public final class ArtworkLoader {
    private static final int LIMIT=32*1024*1024;
    private static File caseFile(Context c,AlbumLibrary.Album album)throws Exception{return new File(new File(c.getFilesDir(),"cases3d"),jp.virtualcd.player.case3d.CasePackage.hash(album.uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8))+".vcd3d");}
    public static String frontStamp(Context c,AlbumLibrary.Album album){try{var file=caseFile(c,album);return file.lastModified()+"|"+file.length();}catch(Exception ex){return "";}}
    private static Bitmap windowsFront(Context c,AlbumLibrary.Album album){
        try(var input=new android.util.AtomicFile(caseFile(c,album)).openRead()){
            var bytes=jp.virtualcd.player.case3d.CasePackage.readBytes(input,jp.virtualcd.player.case3d.CasePackage.MAX_BYTES);
            var front=jp.virtualcd.player.case3d.CasePackage.frontImage(bytes);
            // Windows has already cropped the Front texture. Do not crop the spread again.
            return front==null?null:thumbnail(front,"full");
        }catch(Exception ex){return null;}
    }
    public static Bitmap load(Context context,AlbumLibrary.Album album,String mode) {
        Bitmap defined=windowsFront(context,album);if(defined!=null)return defined;
        try {
            if(album.directory||!AudioFormats.archive(album.name)){
                var pictures=listImages(context,album);pictures.sort(Comparator.comparingInt((ImageRef r)->score(r.name)).thenComparing(r->r.name));
                int attempts=0;for(var picture:pictures){if(++attempts>8)break;try{Bitmap b=documentImage(context,picture.uri,mode,384);if(b!=null)return b;}catch(IOException ignored){}}
                AlbumLibrary.Album file=album;
                if(album.directory){var files=AlbumLibrary.children(context,album.uri);files.sort(Comparator.comparing(a->AudioFormats.natural(a.name)));
                    file=null;for(var candidate:files)if(!candidate.directory&&AudioFormats.audio(candidate.name)){file=candidate;break;}if(file==null)return null;}
                try(var metadata=new MediaMetadataRetriever()){
                    metadata.setDataSource(context,file.uri);byte[] data=metadata.getEmbeddedPicture();return data!=null&&data.length<=LIMIT?thumbnail(data,mode):null;
                }
            }
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)return null;
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){
                var channel=input.getChannel();var images=StoredZipIndex.readImages(channel);
                images.sort(Comparator.comparingInt((StoredZipIndex.Entry e)->score(e.name)).thenComparing(e->e.name));
                int attempted=0;
                for(var e:images){
                    if(Thread.currentThread().isInterrupted())return null;
                    if(e.length>LIMIT||e.packedLength>LIMIT||e.length==0||++attempted>8)continue;
                    Bitmap bitmap=readImage(context,channel,e,mode,384);if(bitmap!=null)return bitmap;
                }
                // Embedded APIC artwork when the archive has no usable separate cover.
                var track=StoredZipIndex.read(channel).get(0);
                try(var retriever=new MediaMetadataRetriever()){
                    retriever.setDataSource(input.getFD(),track.offset,track.length);
                    byte[] data=retriever.getEmbeddedPicture();if(data!=null&&data.length<=LIMIT)return thumbnail(data,mode);
                }
            }
        }catch(Exception ignored){ /* Missing art must not prevent album playback. */ }
        return null;
    }
    public static final class ImageRef {
        public final android.net.Uri uri;public final String name;public final StoredZipIndex.Entry entry;
        ImageRef(android.net.Uri uri,String name,StoredZipIndex.Entry entry){this.uri=uri;this.name=name;this.entry=entry;}
    }
    public static List<ImageRef> listImages(Context context,AlbumLibrary.Album album)throws IOException{
        var result=new ArrayList<ImageRef>();
        if(album.directory){collectImages(context,album.uri,result,0,"");}
        else if(AudioFormats.archive(album.name)){
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)throw new IOException(jp.virtualcd.player.LanguageStrings.text("アルバムを開けません","Unable to open album"));
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){for(var e:StoredZipIndex.readImages(input.getChannel()))result.add(new ImageRef(album.uri,e.name,e));}
        }
        result.sort(Comparator.comparing(r->AudioFormats.natural(r.name)));return result;
    }
    private static void collectImages(Context context,android.net.Uri folder,List<ImageRef> result,int depth,String prefix)throws IOException{
        for(var file:AlbumLibrary.children(context,folder)){
            String lower=file.name.toLowerCase(Locale.ROOT);
            if(file.directory){
                if(depth<2&&imageFolder(lower))collectImages(context,file.uri,result,depth+1,prefix+file.name+"/");
            }else if(lower.endsWith(".jpg")||lower.endsWith(".jpeg")||lower.endsWith(".png")||lower.endsWith(".webp"))result.add(new ImageRef(file.uri,prefix+file.name,null));
        }
    }
    public static boolean imageFolder(String name){String lower=name.toLowerCase(Locale.ROOT);return lower.contains("ジャケ")||lower.contains("jacket")||lower.contains("歌詞")||lower.contains("art")||lower.contains("cover")||lower.contains("scan")||lower.contains("booklet")||lower.equals("image")||lower.equals("images")||lower.equals("画像");}
    private static Bitmap documentImage(Context context,android.net.Uri uri,String mode,int maxSize)throws IOException{
        try(var input=context.getContentResolver().openInputStream(uri)){
            if(input==null)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像を開けません","Unable to open image"));
            return streamedImage(context,input,-1,mode,maxSize);
        }
    }
    public static Bitmap galleryImage(Context context,ImageRef image)throws IOException{
        return image.entry==null?documentImage(context,image.uri,"full",1600):galleryImage(context,image.uri,image.entry);
    }
    public static Bitmap galleryImage(Context context,android.net.Uri document,StoredZipIndex.Entry entry) throws IOException {
        var fd=context.getContentResolver().openFileDescriptor(document,"r");if(fd==null)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像を開けません","Unable to open image"));
        try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){
            return readImage(context,input.getChannel(),entry,"full",1600);
        }
    }
    private static Bitmap readImage(Context context,java.nio.channels.FileChannel channel,StoredZipIndex.Entry e,String mode,int maxSize) throws IOException {
        if(e.length>LIMIT||e.packedLength>LIMIT)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像サイズ超過（32MBまで）","Image exceeds size limit (32 MB)"));
        if(e.length<0||e.packedLength<0||e.offset<0||e.offset>channel.size()-e.packedLength||(e.method!=0&&e.method!=8))throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像のZIP情報が不正です","Invalid image ZIP header"));
        InputStream packed=new InputStream(){
            long position;
            @Override public int read()throws IOException{byte[] one=new byte[1];return read(one,0,1)<0?-1:one[0]&255;}
            @Override public int read(byte[] b,int off,int len)throws IOException{
                if(len==0)return 0;if(position==e.packedLength)return -1;
                int n=channel.read(ByteBuffer.wrap(b,off,(int)Math.min(len,e.packedLength-position)),e.offset+position);
                if(n<=0)throw new EOFException(jp.virtualcd.player.LanguageStrings.text("画像が途中で途切れています","Image is truncated"));position+=n;return n;
            }
        };
        if(e.method==0)return streamedImage(context,packed,e.length,mode,maxSize);
        var inflater=new Inflater(true);
        try(var decoded=new InflaterInputStream(packed,inflater)){
            return streamedImage(context,decoded,e.length,mode,maxSize);
        }finally{inflater.end();}
    }
    // Spool encoded bytes to private cache, not the SD card or a large heap buffer.
    // Validate the entire stream before decoding; BitmapFactory can swallow IO errors.
    private static Bitmap streamedImage(Context context,InputStream input,long expected,String mode,int maxSize)throws IOException{
        File temporary=File.createTempFile("artwork-",".tmp",context.getCacheDir());
        try{
            try(var output=new FileOutputStream(temporary)){
                byte[] chunk=new byte[32768];long total=0;int n;
                while((n=input.read(chunk))!=-1){
                    if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                    total+=n;if(total>LIMIT)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像サイズ超過（32MBまで）","Image exceeds size limit (32 MB)"));
                    if(expected>=0&&total>expected)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像の展開サイズが不正です","Invalid decompressed image size"));
                    output.write(chunk,0,n);
                }
                if(expected>=0&&total!=expected)throw new EOFException(jp.virtualcd.player.LanguageStrings.text("画像が途中で途切れています","Image is truncated"));
            }
            var options=new BitmapFactory.Options();options.inJustDecodeBounds=true;
            BitmapFactory.decodeFile(temporary.getAbsolutePath(),options);
            if(options.outWidth<=0||options.outHeight<=0)return null;
            options.inSampleSize=1;
            while(Math.max(options.outWidth,options.outHeight)/options.inSampleSize>maxSize*2)options.inSampleSize*=2;
            options.inJustDecodeBounds=false;options.inPreferredConfig=Bitmap.Config.RGB_565;
            return resize(BitmapFactory.decodeFile(temporary.getAbsolutePath(),options),mode,maxSize);
        }finally{temporary.delete();}
    }
    private static int score(String name){
        String s=name.substring(name.lastIndexOf('/')+1).toLowerCase(Locale.ROOT);
        if(s.contains("back")||s.contains("rear")||s.contains(jp.virtualcd.player.LanguageStrings.text("裏","Reverse"))||s.contains(jp.virtualcd.player.LanguageStrings.text("背面","Back cover"))||s.contains(jp.virtualcd.player.LanguageStrings.text("バック","Back")))return 100;
        if(s.contains("front"))return 0;
        if(s.contains("cover")||s.contains("folder")||s.contains(jp.virtualcd.player.LanguageStrings.text("表紙","Front cover"))||s.contains(jp.virtualcd.player.LanguageStrings.text("ジャケ","jacket")))return 1;
        if(s.contains("disc")||s.contains("cd")||s.contains("booklet")||s.contains(jp.virtualcd.player.LanguageStrings.text("レーベル","Disc label")))return 50;
        return 10;
    }
    private static Bitmap thumbnail(byte[] data,String mode){
        return thumbnail(data,mode,384);
    }
    private static Bitmap thumbnail(byte[] data,String mode,int maxSize){
        var options=new BitmapFactory.Options();options.inJustDecodeBounds=true;
        BitmapFactory.decodeByteArray(data,0,data.length,options);
        if(options.outWidth<=0||options.outHeight<=0)return null;
        options.inSampleSize=1;
        while(Math.max(options.outWidth,options.outHeight)/options.inSampleSize>maxSize*2)options.inSampleSize*=2;
        options.inJustDecodeBounds=false;options.inPreferredConfig=Bitmap.Config.RGB_565;
        return resize(BitmapFactory.decodeByteArray(data,0,data.length,options),mode,maxSize);
    }
    private static Bitmap resize(Bitmap bitmap,String mode,int maxSize){
        if(bitmap==null)return null;
        int[] r=ThumbnailLayout.region(bitmap.getWidth(),bitmap.getHeight(),mode);
        Bitmap cropped=Bitmap.createBitmap(bitmap,r[0],r[1],r[2],r[3]);
        if(cropped!=bitmap){bitmap.recycle();bitmap=cropped;}
        int longest=Math.max(bitmap.getWidth(),bitmap.getHeight());
        if(longest>maxSize){Bitmap scaled=Bitmap.createScaledBitmap(bitmap,Math.max(1,bitmap.getWidth()*maxSize/longest),Math.max(1,bitmap.getHeight()*maxSize/longest),true);if(scaled!=bitmap)bitmap.recycle();return scaled;}
        return bitmap;
    }
}
