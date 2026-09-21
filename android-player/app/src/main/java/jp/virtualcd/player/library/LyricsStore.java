package jp.virtualcd.player.library;

import android.app.Activity;
import android.net.Uri;
import android.widget.*;
import androidx.media3.common.MediaItem;
import jp.virtualcd.player.case3d.CasePackage;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;

public final class LyricsStore {
    public static final int LIMIT=8*1024*1024;
    private static File file(android.content.Context c,Uri album)throws Exception{
        return new File(new File(c.getFilesDir(),"lyrics"),CasePackage.hash(album.toString().getBytes(StandardCharsets.UTF_8))+".json");
    }
    private static JSONArray parse(byte[] data)throws Exception{
        var json=new JSONObject(new String(data,StandardCharsets.UTF_8));
        if(!"virtual-cd-lyrics".equals(json.getString("format"))||json.getInt("version")!=1)throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応の歌詞形式です","Unsupported lyrics format"));
        var tracks=json.getJSONArray("tracks");if(tracks.length()>10000)throw new IOException(jp.virtualcd.player.LanguageStrings.text("歌詞が多すぎます","Too many lyrics"));
        for(int i=0;i<tracks.length();i++)if(tracks.getJSONObject(i).getString("text").length()>1024*1024)throw new IOException(jp.virtualcd.player.LanguageStrings.text("歌詞が長すぎます","Lyrics are too long"));
        return tracks;
    }
    public static void save(android.content.Context c,Uri album,byte[] data)throws Exception{
        if(data.length>LIMIT)throw new IOException(jp.virtualcd.player.LanguageStrings.text("歌詞サイズ超過","Lyrics exceed size limit"));parse(data);
        File destination=file(c,album);if(!destination.getParentFile().isDirectory()&&!destination.getParentFile().mkdirs())throw new IOException(jp.virtualcd.player.LanguageStrings.text("歌詞を保存できません","Unable to save lyrics"));
        var atomic=new android.util.AtomicFile(destination);FileOutputStream out=null;
        try{out=atomic.startWrite();out.write(data);atomic.finishWrite(out);}catch(Exception ex){if(out!=null)atomic.failWrite(out);throw ex;}
    }
    public static String find(byte[] data,MediaItem item)throws Exception{
        var tracks=parse(data);var m=item.mediaMetadata;
        String filename=m.extras==null?"":m.extras.getString(TrackProperties.FILE,"");
        JSONObject found=null;
        if(!filename.isEmpty())for(int i=0;i<tracks.length();i++){
            var t=tracks.getJSONObject(i);if(t.optString("file").equalsIgnoreCase(filename)){if(found!=null)return "";found=t;}
        }
        if(found==null)for(int i=0;i<tracks.length();i++){
            var t=tracks.getJSONObject(i);
            if(m.title!=null&&t.optString("title").equals(m.title.toString())
                &&(m.trackNumber==null||t.optInt("number")==m.trackNumber)
                &&(m.discNumber==null||m.discNumber==0||t.optInt("disc")==0||t.optInt("disc")==m.discNumber)){
                if(found!=null)return "";found=t;
            }
        }
        if(found==null)return "";
        // Plain reading mode: preserve lines but omit LRC timing/metadata tags.
        return found.getString("text").replaceAll("(?m)\\[(?:\\d{1,3}:\\d{2}(?:[.:]\\d{1,3})?)\\]","")
            .replaceAll("(?im)^\\[(?:ar|ti|al|by|offset|re|ve):[^\\r\\n]*\\]\\s*","").trim();
    }
    public static void show(Activity activity,MediaItem item){
        var text=new TextView(activity);text.setText(jp.virtualcd.player.LanguageStrings.text("歌詞を読み込んでいます…","Loading lyrics…"));text.setTextSize(17);text.setTextColor(0xffe8edf5);text.setTextIsSelectable(true);
        int pad=PlayerStyle.dp(activity,20);text.setPadding(pad,pad,pad,pad);
        var scroll=new ScrollView(activity);scroll.setBackgroundColor(0xff19212b);scroll.addView(text);
        var dialog=new android.app.AlertDialog.Builder(activity).setTitle(item.mediaMetadata.title==null?jp.virtualcd.player.LanguageStrings.text("歌詞","Lyrics"):item.mediaMetadata.title+jp.virtualcd.player.LanguageStrings.text(" — 歌詞"," — Lyrics")).setView(scroll).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).show();
        new Thread(()->{
            String value;
            try{
                String source=item.mediaMetadata.extras==null?"":item.mediaMetadata.extras.getString(ListeningState.ALBUM_URI,"");
                if(source.isEmpty())value="";
                else try(var in=new android.util.AtomicFile(file(activity,Uri.parse(source))).openRead()){value=find(CasePackage.readBytes(in,LIMIT),item);}
                if(value.isBlank())value=jp.virtualcd.player.LanguageStrings.text("この曲の歌詞はありません。Windowsで歌詞を登録し、モバイル同期で再転送してください。","No lyrics for this track. Add lyrics on Windows and transfer again with Mobile Sync.");
            }catch(FileNotFoundException ex){value=jp.virtualcd.player.LanguageStrings.text("歌詞はまだ同期されていません。Windowsからアルバムを再同期してください。","Lyrics have not been synced yet. Resync the album from Windows.");}
            catch(Exception ex){value=jp.virtualcd.player.LanguageStrings.text("歌詞を読み込めませんでした。","Unable to load lyrics.")+ex.getMessage();}
            final String result=value;activity.runOnUiThread(()->{if(!activity.isDestroyed()&&dialog.isShowing())text.setText(result);});
        },"lyrics-reader").start();
    }
}
