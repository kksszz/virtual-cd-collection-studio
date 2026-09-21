package jp.virtualcd.player.library;

import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import androidx.media3.common.*;
import org.json.*;
import java.util.*;

/** App-private listening state. Source files are never opened for writing. */
public final class ListeningState {
    public static final Object FAVORITE_LOCK=new Object();
    public static final String ALBUM_URI="sourceAlbumUri";
    private final SharedPreferences prefs;
    private List<MediaItem> savedQueue=Collections.emptyList();
    public ListeningState(Context context){this(context,"listening-v1");}
    public ListeningState(Context context,String namespace){prefs=context.getSharedPreferences(namespace,Context.MODE_PRIVATE);}
    public static JSONObject encode(MediaItem item)throws JSONException{
        var m=item.mediaMetadata;var out=new JSONObject().put("id",item.mediaId);
        if(m.extras!=null&&m.extras.containsKey(CueTracks.START)){out.put("cueStart",m.extras.getString(CueTracks.START));out.put("cueEnd",m.extras.getString(CueTracks.END));out.put("cueUri",m.extras.getString(CueTracks.URI));}
        if(item.localConfiguration!=null){out.put("uri",item.localConfiguration.uri.toString());out.put("mime",item.localConfiguration.mimeType);}
        else out.put("uri",item.mediaId); // MediaController may omit localConfiguration; our media IDs are source URIs.
        out.put("title",m.title==null?"":m.title.toString()).put("artist",m.artist==null?"":m.artist.toString()).put("album",m.albumTitle==null?"":m.albumTitle.toString());
        out.put("track",m.trackNumber).put("disc",m.discNumber);out.put("source",m.extras==null?"":m.extras.getString(ALBUM_URI,""));return out;
    }
    public static MediaItem decode(JSONObject value)throws JSONException{
        Uri uri=Uri.parse(value.optString("cueUri",value.getString("uri")));if(!"content".equals(uri.getScheme())&&!"zipmp3".equals(uri.getScheme()))throw new JSONException("Unsupported URI");
        var extras=new Bundle();extras.putString(ALBUM_URI,value.optString("source"));
        if(value.has("cueStart")){extras.putString(CueTracks.START,value.getString("cueStart"));extras.putString(CueTracks.END,value.getString("cueEnd"));extras.putString(CueTracks.URI,uri.toString());}
          return new MediaItem.Builder().setMediaId(value.getString("id")).setUri(uri).setMimeType(value.optString("mime",null))
            .setClippingConfiguration(new MediaItem.ClippingConfiguration.Builder().setStartPositionMs(value.optLong("cueStart",0)).setEndPositionMs(value.optLong("cueEnd",C.TIME_END_OF_SOURCE)).build())
            .setMediaMetadata(new MediaMetadata.Builder().setTitle(value.optString("title")).setArtist(value.optString("artist"))
                .setAlbumTitle(value.optString("album")).setTrackNumber(value.optInt("track")>0?value.optInt("track"):null)
                .setDiscNumber(value.optInt("disc")>0?value.optInt("disc"):null).setExtras(extras).build()).build();
    }
    public void save(Player player){
        try{
            var edit=prefs.edit().putBoolean("shuffle",player.getShuffleModeEnabled()).putInt("repeat",player.getRepeatMode());
            if(player.getMediaItemCount()>0){
                var current=new ArrayList<MediaItem>();for(int i=0;i<player.getMediaItemCount();i++)current.add(player.getMediaItemAt(i));
                if(!current.equals(savedQueue)){var queue=new JSONArray();for(var item:current)queue.put(encode(item));edit.putString("queue",queue.toString());savedQueue=current;}
                edit.putInt("index",player.getCurrentMediaItemIndex()).putLong("position",Math.max(0,player.getCurrentPosition()));
            }
            edit.apply();
        }catch(JSONException ignored){}
    }
    public void restore(Player player){
        player.setShuffleModeEnabled(prefs.getBoolean("shuffle",false));
        int repeat=prefs.getInt("repeat",Player.REPEAT_MODE_OFF);player.setRepeatMode(repeat>=0&&repeat<=2?repeat:Player.REPEAT_MODE_OFF);
        try{
            var values=new JSONArray(prefs.getString("queue","[]"));var items=new ArrayList<MediaItem>();
            for(int i=0;i<values.length();i++)items.add(decode(values.getJSONObject(i)));
            if(!items.isEmpty()){player.setMediaItems(items,Math.max(0,Math.min(items.size()-1,prefs.getInt("index",0))),Math.max(0,prefs.getLong("position",0)));player.setPlayWhenReady(false);}
        }catch(JSONException ignored){ /* Corrupt saved state must not stop the player starting. */ }
    }
    private JSONArray array(String key){try{return new JSONArray(prefs.getString(key,"[]"));}catch(JSONException e){return new JSONArray();}}
    public void played(MediaItem item){
        if(item==null||item.localConfiguration==null)return;
        try{
            var previous=array("history");var updated=encode(item);int plays=1;
            for(int i=0;i<previous.length();i++)if(item.mediaId.equals(previous.getJSONObject(i).optString("id")))plays=previous.getJSONObject(i).optInt("plays",0)+1;
            updated.put("lastPlayed",System.currentTimeMillis()).put("plays",plays);
            var next=new JSONArray().put(updated);
            for(int i=0;i<previous.length()&&next.length()<200;i++){var entry=previous.getJSONObject(i);if(!item.mediaId.equals(entry.optString("id")))next.put(entry);}
            prefs.edit().putString("history",next.toString()).apply();
        }catch(JSONException ignored){}
    }
    public List<JSONObject> entries(String key){var list=new ArrayList<JSONObject>();var values=array(key);for(int i=0;i<values.length();i++){JSONObject item=values.optJSONObject(i);if(item!=null)list.add(item);}return list;}
    public boolean contains(String key,String id){for(var item:entries(key))if(id.equals(item.optString("id")))return true;return false;}
    public boolean toggle(String key,JSONObject item){
        synchronized(FAVORITE_LOCK){
        String id=item.optString("id");boolean had=contains(key,id);var next=new JSONArray();
        if(!had)next.put(item);for(var old:entries(key))if(!id.equals(old.optString("id")))next.put(old);
        prefs.edit().putString(key,next.toString()).putBoolean("edited|"+key+"|"+id,true).apply();return !had;
        }
    }
    public boolean favoriteEdited(String key,String id){return prefs.getBoolean("edited|"+key+"|"+id,false);}
    public void setFavorite(String key,JSONObject item,boolean enabled){
        synchronized(FAVORITE_LOCK){
            String id=item.optString("id");var next=new JSONArray();
            if(enabled)next.put(item);for(var old:entries(key))if(!id.equals(old.optString("id")))next.put(old);
            prefs.edit().putString(key,next.toString()).commit();
        }
    }
}
