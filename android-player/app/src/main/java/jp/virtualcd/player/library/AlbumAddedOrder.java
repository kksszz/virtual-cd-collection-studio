package jp.virtualcd.player.library;
import android.content.Context;
import java.util.*;

/** Stable first-seen dates, never file modification dates. Zero denotes pre-upgrade history. */
public final class AlbumAddedOrder {
    private static android.content.SharedPreferences prefs(Context c){return c.getSharedPreferences("album-added-v1",0);}
    public static synchronized void observe(Context c,List<AlbumLibrary.Album> albums,boolean existing){
        var p=prefs(c);var edit=p.edit();long now=existing?0:System.currentTimeMillis();boolean changed=false;
        for(var album:albums){String key="uri|"+album.uri;if(!p.contains(key)){edit.putLong(key,now);changed=true;}}
        if(changed)edit.commit();
    }
    public static long get(Context c,AlbumLibrary.Album album){return prefs(c).getLong("uri|"+album.uri,0);}
    public static synchronized void bindSync(Context c,String id,AlbumLibrary.Album album){
        var p=prefs(c);String stable="pc|"+id,uri="uri|"+album.uri;
        long first=p.contains(stable)?p.getLong(stable,0):p.getLong(uri,System.currentTimeMillis());
        if(!p.contains(stable)||!p.contains(uri)||p.getLong(uri,0)!=first)p.edit().putLong(stable,first).putLong(uri,first).commit();
    }
}
