package jp.virtualcd.player.library;

import android.app.Activity;
import android.app.Dialog;
import android.os.Bundle;
import android.widget.*;
import androidx.media3.common.MediaItem;
import java.util.Locale;

/** Read-only snapshot captured alongside album tags; opening the sheet does no IO or playback work. */
public final class TrackProperties {
    public static final String FILE="property.file",SIZE="property.bytes",DURATION="property.duration",BITRATE="property.bitrate",
        RATE="property.sampleRate",FORMAT="property.format",YEAR="property.year",GENRE="property.genre",COMPOSER="property.composer";
    public static String describe(MediaItem item){
        Bundle data=item.mediaMetadata.extras==null?new Bundle():item.mediaMetadata.extras;var m=item.mediaMetadata;StringBuilder text=new StringBuilder();
        field(text,"曲名",m.title);field(text,"アーティスト",m.artist);field(text,"アルバム",m.albumTitle);
        field(text,"曲番号",m.trackNumber==null?null:m.trackNumber.toString());field(text,"Disc番号",m.discNumber==null?null:m.discNumber.toString());
        field(text,"年",data.getString(YEAR));field(text,"ジャンル",data.getString(GENRE));field(text,"作曲者",data.getString(COMPOSER));
        long duration=number(data.getString(DURATION));field(text,"再生時間",duration>0?String.format(Locale.ROOT,"%d:%02d",duration/60000,duration/1000%60):null);
        field(text,"音声形式",data.getString(FORMAT));long bitrate=number(data.getString(BITRATE));field(text,"ビットレート",bitrate>0?String.format(Locale.ROOT,"%.0f kbps（メタデータ値）",bitrate/1000.0):null);
        long rate=number(data.getString(RATE));field(text,"サンプルレート",rate>0?String.format(Locale.ROOT,"%.1f kHz",rate/1000.0):null);
        long bytes=data.getLong(SIZE,-1);field(text,"曲ファイルのサイズ",bytes>=0?String.format(Locale.ROOT,"%.2f MiB（%,d bytes）",bytes/1048576.0,bytes):null);
        field(text,"ファイル名",data.getString(FILE));
        android.net.Uri uri=item.localConfiguration!=null?item.localConfiguration.uri:android.net.Uri.parse(item.mediaId);
        boolean archive="zipmp3".equals(uri.getScheme());field(text,"格納形式",archive?"ZIP内の音楽ファイル":"通常ファイル");
        String source=data.getString(ListeningState.ALBUM_URI);
        if(source==null||source.isEmpty())source=archive&&uri.isHierarchical()?uri.getQueryParameter("document"):uri.toString();
        field(text,"保存元（URI）",source);return text.toString().trim();
    }
    private static long number(String value){try{return Long.parseLong(value);}catch(Exception ignored){return -1;}}
    private static void field(StringBuilder out,String label,CharSequence value){out.append(label).append("\n").append(value==null||value.toString().trim().isEmpty()?"不明／未設定":value).append("\n\n");}
    public static Dialog show(Activity activity,MediaItem item){
        ScrollView scroll=new ScrollView(activity);TextView text=new TextView(activity);text.setText(describe(item));text.setTextColor(0xffe8edf5);text.setTextSize(15);text.setTextIsSelectable(true);
        int padding=PlayerStyle.dp(activity,20);text.setPadding(padding,padding,padding,padding);scroll.setBackgroundColor(0xff19212b);scroll.addView(text);
        return new android.app.AlertDialog.Builder(activity).setTitle("曲のプロパティ（読み取り専用）").setView(scroll).setPositiveButton("閉じる",null).show();
    }
}
