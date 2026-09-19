package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.util.AtomicFile;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import org.json.*;

/** App-private, bounded, atomic metadata snapshots. Never opens an audio source. */
public final class AlbumTagCache {
    public static final class Snapshot {
        public final String signature;
        public final AlbumTracks album;
        Snapshot(String signature,AlbumTracks album){this.signature=signature;this.album=album;}
    }
    private static File directory(Context c){return new File(c.getFilesDir(),"album-tags-v1");}
    private static File path(Context c,Uri uri)throws Exception{
        byte[] hash=MessageDigest.getInstance("SHA-256").digest(uri.toString().getBytes(StandardCharsets.UTF_8));
        StringBuilder name=new StringBuilder();for(byte b:hash)name.append(String.format(Locale.ROOT,"%02x",b&255));
        return new File(directory(c),name+".json");
    }
    public static synchronized Snapshot read(Context c,Uri source){
        try{
            File path=path(c,source);if(!path.exists()||path.length()>8*1024*1024)return null;
            JSONObject root=new JSONObject(new String(new AtomicFile(path).readFully(),StandardCharsets.UTF_8));
            if(root.getInt("version")!=1||!source.toString().equals(root.getString("source")))return null;
            var album=new AlbumTracks();album.title=root.getString("title");album.artist=root.getString("artist");
            JSONArray tracks=root.getJSONArray("tracks");if(tracks.length()==0||tracks.length()>10000)return null;
            for(int i=0;i<tracks.length();i++){
                JSONObject value=tracks.getJSONObject(i);var t=new AlbumTracks.Track();t.uri=Uri.parse(value.getString("uri"));
                if(!"content".equals(t.uri.getScheme())&&!"zipmp3".equals(t.uri.getScheme()))return null;
                t.file=value.getString("file");t.title=value.getString("title");t.artist=value.optString("artist",null);t.album=value.optString("album",null);
                t.disc=value.optInt("disc");t.number=value.optInt("number");
                JSONObject props=value.getJSONObject("properties");for(Iterator<String> keys=props.keys();keys.hasNext();){String key=keys.next();
                    if(key.equals(TrackProperties.SIZE))t.properties.putLong(key,props.getLong(key));else t.properties.putString(key,props.getString(key));}
                album.tracks.add(t);
            }
            path.setLastModified(System.currentTimeMillis());
            return new Snapshot(root.getString("signature"),album);
        }catch(Exception ignored){return null;}
    }
    public static synchronized void write(Context c,Uri source,String signature,AlbumTracks album){
        AtomicFile file=null;FileOutputStream out=null;
        try{
            if(album.tracks.isEmpty()||Thread.currentThread().isInterrupted())return;
            JSONArray tracks=new JSONArray();for(var t:album.tracks){
                JSONObject props=new JSONObject();for(String key:t.properties.keySet())props.put(key,t.properties.get(key));
                tracks.put(new JSONObject().put("uri",t.uri.toString()).put("file",t.file).put("title",t.title).put("artist",t.artist)
                    .put("album",t.album).put("disc",t.disc).put("number",t.number).put("properties",props));
            }
            byte[] bytes=new JSONObject().put("version",1).put("source",source.toString()).put("signature",signature)
                .put("title",album.title).put("artist",album.artist).put("tracks",tracks).toString().getBytes(StandardCharsets.UTF_8);
            if(bytes.length>8*1024*1024)return;
            File dir=directory(c);if(!dir.isDirectory()&&!dir.mkdirs())return;
            file=new AtomicFile(path(c,source));out=file.startWrite();out.write(bytes);file.finishWrite(out);out=null;
            File[] saved=dir.listFiles((d,n)->n.matches("[0-9a-f]{64}\\.json"));if(saved==null)return;
            Arrays.sort(saved,Comparator.comparingLong(File::lastModified).reversed());long total=0;
            for(int i=0;i<saved.length;i++){total+=saved[i].length();if(i>=500||total>32L*1024*1024)new AtomicFile(saved[i]).delete();}
        }catch(Exception ignored){if(file!=null&&out!=null)file.failWrite(out);}
    }
}
