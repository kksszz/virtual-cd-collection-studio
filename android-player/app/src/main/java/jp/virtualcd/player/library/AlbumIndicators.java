package jp.virtualcd.player.library;

import android.content.Context;
import android.os.ParcelFileDescriptor;
import java.util.*;
import jp.virtualcd.player.archive.StoredZipIndex;

public final class AlbumIndicators {
    public static final class Info {
        public final String formats;public final int count;
        public Info(String formats,int count){this.formats=formats;this.count=count;}
    }
    public static String formatNames(Collection<String> names){
        var formats=new TreeSet<String>();for(String name:names){if(AudioFormats.audio(name))formats.add(name.substring(name.lastIndexOf('.')+1).toUpperCase(Locale.ROOT));}
        return String.join("/",formats);
    }
    /** Names only: do not decode every track to render the list. */
    public static Info read(Context context,AlbumLibrary.Album album)throws Exception{
        var names=new ArrayList<String>();
        if(album.directory){var children=AlbumLibrary.children(context,album.uri);var cue=CueTracks.read(context,album,children);if(cue!=null)return new Info("FLAC · CUE",cue.tracks.size());for(var file:children)if(!file.directory)names.add(file.name);}
        else if(AudioFormats.archive(album.name)){
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)return new Info("",-1);
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){for(var entry:StoredZipIndex.read(input.getChannel()))names.add(entry.name);}
        }else names.add(album.name);
        return summarize(names);
    }
    public static Info summarize(Collection<String> names){int count=0;for(String name:names)if(AudioFormats.audio(name))count++;return new Info(formatNames(names),count);}
    public static boolean has3d(Context c,AlbumLibrary.Album album){
        try{String hash=jp.virtualcd.player.case3d.CasePackage.hash(album.uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
            var file=new java.io.File(new java.io.File(c.getFilesDir(),"cases3d"),hash+".vcd3d");return file.isFile()&&file.length()>0;
        }catch(Exception ignored){return false;}
    }
}
