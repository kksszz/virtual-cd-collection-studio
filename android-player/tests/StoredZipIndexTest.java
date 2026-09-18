import jp.virtualcd.player.archive.StoredZipIndex;
import java.nio.file.*;
import java.nio.channels.FileChannel;
import java.util.*;
import java.util.zip.*;
import java.io.*;

public class StoredZipIndexTest {
    static void check(boolean condition,String text){if(!condition)throw new AssertionError(text);System.out.println("PASS "+text);}
    static void entry(ZipOutputStream out,String name,byte[] data,boolean stored)throws Exception{
        var e=new ZipEntry(name);if(stored){var crc=new CRC32();crc.update(data);e.setMethod(ZipEntry.STORED);e.setSize(data.length);e.setCompressedSize(data.length);e.setCrc(crc.getValue());}
        out.putNextEntry(e);out.write(data);out.closeEntry();
    }
    public static void main(String[] args)throws Exception {
        check(jp.virtualcd.player.library.AudioFormats.audio("Track.FLAC"),"case-insensitive FLAC detection");
        check(!jp.virtualcd.player.library.AudioFormats.audio("Album.zip.mp3"),"ZIP.MP3 not indexed as a regular MP3");
        check(!jp.virtualcd.player.library.AudioFormats.audio("Disc.iso"),"ISO not misclassified as playable audio");
        check(jp.virtualcd.player.library.AudioFormats.number("2/3")==2,"Disc total tag parsing");
        check(jp.virtualcd.player.library.AudioFormats.natural("2.flac").compareTo(jp.virtualcd.player.library.AudioFormats.natural("10.flac"))<0,"natural folder track order");
        var root=Files.createTempDirectory("virtual-cd-zip-test-");var zip=root.resolve("album.zip.mp3");
        byte[] bytes={1,2,3,4,5,6,7,8};
        try(var out=new ZipOutputStream(Files.newOutputStream(zip))){
            entry(out,"10 曲.mp3",bytes,true);entry(out,"2 曲.mp3",bytes,true);entry(out,"cover.jpg",new byte[20],false);entry(out,"Front.png",bytes,true);}
        try(var input=FileChannel.open(zip)){
            var tracks=StoredZipIndex.read(input);check(tracks.size()==2,"MP3 only");check(tracks.get(0).name.equals("2 曲.mp3"),"natural order and UTF-8 name");
            for(var track:tracks){var buffer=java.nio.ByteBuffer.allocate((int)track.length);input.read(buffer,track.offset);
                check(Arrays.equals(buffer.array(),bytes),"exact bounded MP3 byte range");}
            var images=StoredZipIndex.readImages(input);check(images.size()==2,"image index excludes MP3");
            for(var image:images){
                var buffer=java.nio.ByteBuffer.allocate((int)image.packedLength);input.read(buffer,image.offset);
                if(image.method==8){var inflater=new Inflater(true);try(var decoded=new InflaterInputStream(new ByteArrayInputStream(buffer.array()),inflater)){
                    check(Arrays.equals(decoded.readAllBytes(),new byte[20]),"deflated cover bounded byte range");}finally{inflater.end();}}
                else check(Arrays.equals(buffer.array(),bytes),"stored cover bounded byte range");
            }
        }
        var compressed=root.resolve("compressed.zip");
        try(var out=new ZipOutputStream(Files.newOutputStream(compressed))){entry(out,"song.mp3",bytes,false);}
        try(var input=FileChannel.open(compressed)){try{StoredZipIndex.read(input);throw new AssertionError("compressed accepted");}catch(IOException expected){System.out.println("PASS rejects compressed MP3");}}
        try(var input=FileChannel.open(compressed)){check(StoredZipIndex.readImages(input).isEmpty(),"missing artwork returns empty list");}
        var mixed=root.resolve("mixed.zip");
        try(var out=new ZipOutputStream(Files.newOutputStream(mixed))){entry(out,"01 Song.FLAC",bytes,true);entry(out,"02 Song.wav",bytes,true);entry(out,"03 Song.m4a",bytes,true);entry(out,"04 Song.mp3",bytes,true);entry(out,"nested.zip.mp3",bytes,true);}
        try(var input=FileChannel.open(mixed)){check(StoredZipIndex.read(input).size()==4,"four audio formats; nested archives excluded");}
        var broken=root.resolve("broken.zip");Files.write(broken,new byte[60]);
        try(var input=FileChannel.open(broken)){try{StoredZipIndex.read(input);throw new AssertionError("broken accepted");}catch(IOException expected){System.out.println("PASS rejects corrupt ZIP");}}
        var data=Files.readAllBytes(zip);data[0]=0;var badLocal=root.resolve("bad-local.zip");Files.write(badLocal,data);
        try(var input=FileChannel.open(badLocal)){try{StoredZipIndex.read(input);throw new AssertionError("bad local accepted");}catch(IOException expected){System.out.println("PASS validates local header");}}
        System.out.println("Synthetic test files retained at "+root);
        for(var sample:args){try(var input=FileChannel.open(Path.of(sample))){System.out.println("READ-ONLY sample: "+StoredZipIndex.read(input).size()+" MP3 tracks");}}
    }
}
