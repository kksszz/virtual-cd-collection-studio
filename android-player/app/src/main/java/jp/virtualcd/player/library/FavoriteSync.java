package jp.virtualcd.player.library;

import android.content.Context;
import org.json.*;
import java.util.*;

/** PC snapshots are optional; stable PC keys are distinct from Android playback URIs. */
public final class FavoriteSync {
    public static final int FIRST=0, ALWAYS=1, NEVER=2;
    public static String[] labels(){return new String[]{jp.virtualcd.player.LanguageStrings.text("初回のみ引き継ぐ（標準）","First sync only (default)"),jp.virtualcd.player.LanguageStrings.text("毎回Windowsに合わせる","Match Windows every time"),jp.virtualcd.player.LanguageStrings.text("引き継がない","Do not import")};}
    private static android.content.SharedPreferences prefs(Context c){return c.getSharedPreferences("favorite-sync-v1",0);}
    public static int mode(Context c){return Math.max(FIRST,Math.min(NEVER,prefs(c).getInt("mode",FIRST)));}
    public static void setMode(Context c,int value){prefs(c).edit().putInt("mode",value).apply();}

    public static void receive(Context c,String id,JSONObject record,AlbumLibrary.Album audio)throws Exception{
        var favorites=record.optJSONObject("favorites");if(favorites==null)return;
        if(favorites.getInt("version")!=1)throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("未対応のお気に入り情報です","Unsupported favorites data"));
        var tracks=favorites.getJSONArray("tracks");if(tracks.length()>10000)throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("お気に入り情報が多すぎます","Too many favorites"));
        var loaded=AlbumTracks.load(c,audio);
        var mapped=new JSONArray();var names=new HashSet<String>();
        for(int i=0;i<tracks.length();i++){
            var t=tracks.getJSONObject(i);String key=t.getString("file");
            if(!names.add(key.toLowerCase(Locale.ROOT)))throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("お気に入りの曲名が重複しています","Duplicate favorite track names"));
            var match=match(t,loaded.tracks);if(match==null)continue;
            var item=match.item();var extras=item.mediaMetadata.extras==null?new android.os.Bundle():new android.os.Bundle(item.mediaMetadata.extras);
            extras.putString(ListeningState.ALBUM_URI,audio.uri.toString());
            item=item.buildUpon().setMediaMetadata(item.mediaMetadata.buildUpon().setExtras(extras).build()).build();
            mapped.put(new JSONObject().put("key",key).put("entry",ListeningState.encode(item)).put("favorite",t.getBoolean("favorite")));
        }
        var album=new JSONObject().put("id",audio.uri.toString()).put("source",audio.uri.toString()).put("title",record.getString("title"));
        var snapshot=new JSONObject().put("album",album).put("favorite",favorites.getBoolean("album")).put("tracks",mapped);
        if(!prefs(c).edit().putString("snapshot|"+id,snapshot.toString()).commit())throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("お気に入り情報を保存できません","Unable to save favorites data"));
    }

    static AlbumTracks.Track match(JSONObject target,List<AlbumTracks.Track> tracks)throws Exception{
        AlbumTracks.Track found=null;
        for(var t:tracks)if(target.getString("file").equalsIgnoreCase(t.file)){if(found!=null)return null;found=t;}
        if(found!=null)return found;
        for(var t:tracks)if(target.optString("title").equals(t.title)&&target.optInt("number")==t.number
            &&(target.optInt("disc")==0||t.disc==0||target.optInt("disc")==t.disc)){if(found!=null)return null;found=t;}
        return found;
    }

    public static void applyCached(Context c,JSONObject records,boolean force)throws Exception{
        var p=prefs(c);var state=new ListeningState(c);
        synchronized(ListeningState.FAVORITE_LOCK){
            var ids=records.keys();while(ids.hasNext()){
                String id=ids.next();
                if(!records.getJSONObject(id).has("favorites"))continue; // Legacy senders must never clear favorites.
                String text=p.getString("snapshot|"+id,null);if(text==null)continue;
                var snapshot=new JSONObject(text);
                applyOne(state,p,id+"|album","favoriteAlbums",snapshot.getJSONObject("album"),snapshot.getBoolean("favorite"),force);
                var tracks=snapshot.getJSONArray("tracks");
                for(int i=0;i<tracks.length();i++){
                    var t=tracks.getJSONObject(i);
                    applyOne(state,p,id+"|track|"+t.getString("key").toLowerCase(Locale.ROOT),
                        "favoriteTracks",t.getJSONObject("entry"),t.getBoolean("favorite"),force);
                }
            }
        }
    }

    static void applyOne(ListeningState state,android.content.SharedPreferences p,String stable,String group,JSONObject entry,boolean pc,boolean force)throws Exception{
        String current=entry.getString("id"),old=p.getString("binding|"+stable,null);
        int mode=p.getInt("mode",FIRST);
        boolean known=p.getBoolean("seen|"+stable,false);
        boolean overwrite=force||mode==ALWAYS;
        boolean value;
        if(overwrite)value=pc;
        else if(old!=null&&!old.equals(current))value=state.contains(group,old);
        else if(mode==FIRST&&!known&&!state.favoriteEdited(group,current)&&!state.contains(group,current))value=pc;
        else value=state.contains(group,current);
        // Rebinding a revision preserves Android edits even when PC import is disabled.
        if(value!=state.contains(group,current)||(old!=null&&!old.equals(current)))state.setFavorite(group,entry,value);
        if(old!=null&&!old.equals(current))state.setFavorite(group,new JSONObject().put("id",old),false);
        if((!known||!current.equals(old))&&!p.edit().putString("binding|"+stable,current).putBoolean("seen|"+stable,true).commit())
            throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("お気に入りの引き継ぎ状態を保存できません","Unable to save favorites import state"));
    }
}
