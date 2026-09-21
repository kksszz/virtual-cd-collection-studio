package jp.virtualcd.player.library;

import android.content.Context;
import android.graphics.Bitmap;
import android.net.Uri;
import java.io.*;
import java.util.zip.*;
import jp.virtualcd.player.archive.StoredZipIndex;

public final class ArtworkLimitTest {
    public static void run(Context context)throws Exception{
        File image=File.createTempFile("art-test-",".png",context.getCacheDir());
        File zip=File.createTempFile("art-test-",".zip",context.getCacheDir());
        try{
            Bitmap source=Bitmap.createBitmap(1800,900,Bitmap.Config.RGB_565);
            source.eraseColor(0xff36bad0);
            try(var out=new FileOutputStream(image)){source.compress(Bitmap.CompressFormat.PNG,100,out);}finally{source.recycle();}
            // Valid image with padding exercises encoded byte limits without huge test bitmaps.
            try(var f=new RandomAccessFile(image,"rw")){f.setLength(9*1024*1024);}
            check(ArtworkLoader.galleryImage(context,new ArtworkLoader.ImageRef(Uri.fromFile(image),"front.png",null)));
            for(int method:new int[]{ZipEntry.STORED,ZipEntry.DEFLATED}){
                CRC32 crc=new CRC32();try(var in=new FileInputStream(image)){byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)crc.update(b,0,n);}
                try(var out=new ZipOutputStream(new FileOutputStream(zip))){
                    var entry=new ZipEntry("front.png");entry.setMethod(method);entry.setSize(image.length());entry.setCrc(crc.getValue());
                    out.putNextEntry(entry);try(var in=new FileInputStream(image)){in.transferTo(out);}out.closeEntry();
                }
                StoredZipIndex.Entry entry;try(var in=new FileInputStream(zip)){entry=StoredZipIndex.readImages(in.getChannel()).get(0);}
                check(ArtworkLoader.galleryImage(context,Uri.fromFile(zip),entry));
                reject(()->ArtworkLoader.galleryImage(context,Uri.fromFile(zip),new StoredZipIndex.Entry(entry.name,entry.offset,entry.length+1,entry.packedLength,entry.method)));
                reject(()->ArtworkLoader.galleryImage(context,Uri.fromFile(zip),new StoredZipIndex.Entry(entry.name,entry.offset,1,entry.packedLength,entry.method)));
                try(var f=new RandomAccessFile(zip,"rw")){f.setLength(entry.offset+entry.packedLength-1);}
                reject(()->ArtworkLoader.galleryImage(context,Uri.fromFile(zip),entry));
            }
            try(var f=new RandomAccessFile(image,"rw")){f.setLength(32*1024*1024);}
            check(ArtworkLoader.galleryImage(context,new ArtworkLoader.ImageRef(Uri.fromFile(image),"front.png",null)));
            try(var f=new RandomAccessFile(image,"rw")){f.setLength(32*1024*1024+1);}
            reject(()->ArtworkLoader.galleryImage(context,new ArtworkLoader.ImageRef(Uri.fromFile(image),"front.png",null)));
            File[] leftovers=context.getCacheDir().listFiles((d,n)->n.startsWith("artwork-")&&n.endsWith(".tmp"));
            if(leftovers!=null&&leftovers.length!=0)throw new AssertionError("Temporary artwork leaked");
        }finally{image.delete();zip.delete();}
    }
    private static void check(Bitmap b){if(b==null)throw new AssertionError("Image missing");try{if(b.getWidth()!=1600||b.getHeight()!=800)throw new AssertionError("Gallery size");}finally{b.recycle();}}
    private interface Read{Bitmap run()throws IOException;}
    private static void reject(Read read)throws IOException{try{Bitmap b=read.run();if(b!=null)b.recycle();}catch(IOException expected){return;}throw new AssertionError("Invalid size accepted");}
}
