package jp.virtualcd.player.library;

import android.content.*;
import org.json.*;
import java.util.*;

public final class FavoriteSyncTest {
    public static void run(Context base)throws Exception{
        String prefix="favorite-test-"+UUID.randomUUID()+"-";
        Context c=new ContextWrapper(base){@Override public SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(prefix+name,mode);}};
        var p=c.getSharedPreferences("favorite-sync-v1",0);var state=new ListeningState(c);
        var entry=new JSONObject().put("id","content://test/one").put("source","content://test/album").put("title","曲");
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,true,false);
        if(!state.contains("favoriteTracks","content://test/one"))throw new AssertionError("first import");
        state.toggle("favoriteTracks",entry);
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,true,false);
        if(state.contains("favoriteTracks","content://test/one"))throw new AssertionError("Android removal overwritten");
        FavoriteSync.setMode(c,FavoriteSync.ALWAYS);
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,true,false);
        if(!state.contains("favoriteTracks","content://test/one"))throw new AssertionError("always add");
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,false,false);
        if(state.contains("favoriteTracks","content://test/one"))throw new AssertionError("always remove");
        FavoriteSync.setMode(c,FavoriteSync.NEVER);
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,true,false);
        if(state.contains("favoriteTracks","content://test/one"))throw new AssertionError("never");
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",entry,true,true);
        if(!state.contains("favoriteTracks","content://test/one")||FavoriteSync.mode(c)!=FavoriteSync.NEVER)throw new AssertionError("one shot");
        var revised=new JSONObject(entry.toString()).put("id","content://test/revision");
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",revised,false,false);
        if(!state.contains("favoriteTracks","content://test/revision")||state.contains("favoriteTracks","content://test/one"))throw new AssertionError("revision binding");
        var untouched=new JSONObject().put("id","content://unrelated");
        state.toggle("favoriteTracks",untouched);
        FavoriteSync.applyOne(state,p,"track1","favoriteTracks",revised,false,true);
        if(!state.contains("favoriteTracks","content://unrelated"))throw new AssertionError("unrelated favorite lost");
        var album=new JSONObject().put("id","content://album");
        FavoriteSync.setMode(c,FavoriteSync.FIRST);
        FavoriteSync.applyOne(state,p,"album1","favoriteAlbums",album,true,false);
        if(!state.contains("favoriteAlbums","content://album"))throw new AssertionError("album import");
        FavoriteSync.applyCached(c,new JSONObject().put("legacy",new JSONObject()),true);
        if(!state.contains("favoriteAlbums","content://album"))throw new AssertionError("legacy changed favorites");
        var track=new AlbumTracks.Track();track.file="disc.flac · Track 2";track.title="Same";track.number=2;track.disc=1;
        var target=new JSONObject().put("file",track.file).put("title","Same").put("number",2).put("disc",1);
        if(FavoriteSync.match(target,List.of(track))!=track||FavoriteSync.match(target,List.of(track,track))!=null)throw new AssertionError("CUE/ambiguous match");
        p.edit().clear().commit();c.getSharedPreferences("listening-v1",0).edit().clear().commit();
    }
}
