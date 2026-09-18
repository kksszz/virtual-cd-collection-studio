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
    private static final int LIMIT=8*1024*1024;
    public static Bitmap load(Context context,AlbumLibrary.Album album,String mode) {
        try {
            if(album.directory||!AudioFormats.archive(album.name)){
                var pictures=listImages(context,album);pictures.sort(Comparator.comparingInt((ImageRef r)->score(r.name)).thenComparing(r->r.name));
                int attempts=0;for(var picture:pictures){if(++attempts>8)break;try{Bitmap b=thumbnail(readDocument(context,picture.uri),mode);if(b!=null)return b;}catch(IOException ignored){}}
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
                    byte[] data=readImage(channel,e);
                    Bitmap bitmap=thumbnail(data,mode);if(bitmap!=null)return bitmap;
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
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)throw new IOException("アルバムを開けません");
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){for(var e:StoredZipIndex.readImages(input.getChannel()))result.add(new ImageRef(album.uri,e.name,e));}
        }
        result.sort(Comparator.comparing(r->AudioFormats.natural(r.name)));return result;
    }
    private static void collectImages(Context context,android.net.Uri folder,List<ImageRef> result,int depth,String prefix)throws IOException{
        for(var file:AlbumLibrary.children(context,folder)){
            String lower=file.name.toLowerCase(Locale.ROOT);
            if(file.directory){
                if(depth<2&&(lower.contains("ジャケ")||lower.contains("jacket")||lower.contains("歌詞")||lower.contains("art")||lower.contains("cover")||lower.contains("scan")||lower.contains("booklet")))collectImages(context,file.uri,result,depth+1,prefix+file.name+"/");
            }else if(lower.endsWith(".jpg")||lower.endsWith(".jpeg")||lower.endsWith(".png")||lower.endsWith(".webp"))result.add(new ImageRef(file.uri,prefix+file.name,null));
        }
    }
    private static byte[] readDocument(Context context,android.net.Uri uri)throws IOException{
        try(var input=context.getContentResolver().openInputStream(uri);var output=new ByteArrayOutputStream()){
            if(input==null)throw new IOException("画像を開けません");byte[] chunk=new byte[8192];int n;
            while((n=input.read(chunk))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(output.size()+n>LIMIT)throw new IOException("画像サイズ超過（8MBまで）");output.write(chunk,0,n);}return output.toByteArray();
        }
    }
    public static Bitmap galleryImage(Context context,ImageRef image)throws IOException{
        return image.entry==null?thumbnail(readDocument(context,image.uri),"full",1600):galleryImage(context,image.uri,image.entry);
    }
    public static Bitmap galleryImage(Context context,android.net.Uri document,StoredZipIndex.Entry entry) throws IOException {
        var fd=context.getContentResolver().openFileDescriptor(document,"r");if(fd==null)throw new IOException("画像を開けません");
        try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){
            return thumbnail(readImage(input.getChannel(),entry),"full",1600);
        }
    }
    private static byte[] readImage(java.nio.channels.FileChannel channel,StoredZipIndex.Entry e) throws IOException {
        if(e.length>LIMIT||e.packedLength>LIMIT)throw new IOException("8MBを超える画像は現在の試作では表示できません");
        byte[] packed=new byte[(int)e.packedLength];var buffer=ByteBuffer.wrap(packed);
        while(buffer.hasRemaining()){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();int n=channel.read(buffer,e.offset+buffer.position());if(n<=0)throw new EOFException();}
        if(e.method!=8)return packed;
        var inflater=new Inflater(true);
        try(var decoded=new InflaterInputStream(new ByteArrayInputStream(packed),inflater);var output=new ByteArrayOutputStream()){
            byte[] chunk=new byte[8192];int n;
            while((n=decoded.read(chunk))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(output.size()+n>LIMIT||output.size()+n>e.length)throw new IOException("画像サイズ超過");output.write(chunk,0,n);}
            return output.toByteArray();
        }finally{inflater.end();}
    }
    private static int score(String name){
        String s=name.substring(name.lastIndexOf('/')+1).toLowerCase(Locale.ROOT);
        if(s.contains("back")||s.contains("rear")||s.contains("裏")||s.contains("背面")||s.contains("バック"))return 100;
        if(s.contains("front"))return 0;
        if(s.contains("cover")||s.contains("folder")||s.contains("表紙")||s.contains("ジャケ"))return 1;
        if(s.contains("disc")||s.contains("cd")||s.contains("booklet")||s.contains("レーベル"))return 50;
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
        Bitmap bitmap=BitmapFactory.decodeByteArray(data,0,data.length,options);if(bitmap==null)return null;
        int[] r=ThumbnailLayout.region(bitmap.getWidth(),bitmap.getHeight(),mode);
        Bitmap cropped=Bitmap.createBitmap(bitmap,r[0],r[1],r[2],r[3]);
        if(cropped!=bitmap){bitmap.recycle();bitmap=cropped;}
        int longest=Math.max(bitmap.getWidth(),bitmap.getHeight());
        if(longest>maxSize){Bitmap scaled=Bitmap.createScaledBitmap(bitmap,Math.max(1,bitmap.getWidth()*maxSize/longest),Math.max(1,bitmap.getHeight()*maxSize/longest),true);if(scaled!=bitmap)bitmap.recycle();return scaled;}
        return bitmap;
    }
}
