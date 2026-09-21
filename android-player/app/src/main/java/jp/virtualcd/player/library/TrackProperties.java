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
        field(text,jp.virtualcd.player.LanguageStrings.text("曲名","Title"),m.title);field(text,jp.virtualcd.player.LanguageStrings.text("アーティスト","Artist"),m.artist);field(text,jp.virtualcd.player.LanguageStrings.text("アルバム","Albums"),m.albumTitle);
        field(text,jp.virtualcd.player.LanguageStrings.text("曲番号","Track number"),m.trackNumber==null?null:m.trackNumber.toString());field(text,jp.virtualcd.player.LanguageStrings.text("Disc番号","Disc number"),m.discNumber==null?null:m.discNumber.toString());
        field(text,jp.virtualcd.player.LanguageStrings.text("年","Year"),data.getString(YEAR));field(text,jp.virtualcd.player.LanguageStrings.text("ジャンル","Genre"),data.getString(GENRE));field(text,jp.virtualcd.player.LanguageStrings.text("作曲者","Composer"),data.getString(COMPOSER));
        long duration=number(data.getString(DURATION));field(text,jp.virtualcd.player.LanguageStrings.text("再生時間","Duration"),duration>0?String.format(Locale.ROOT,"%d:%02d",duration/60000,duration/1000%60):null);
        field(text,jp.virtualcd.player.LanguageStrings.text("音声形式","Audio format"),data.getString(FORMAT));long bitrate=number(data.getString(BITRATE));field(text,jp.virtualcd.player.LanguageStrings.text("ビットレート","Bitrate"),bitrate>0?String.format(Locale.ROOT,jp.virtualcd.player.LanguageStrings.text("%.0f kbps（メタデータ値）","%.0f kbps (metadata)"),bitrate/1000.0):null);
        long rate=number(data.getString(RATE));field(text,jp.virtualcd.player.LanguageStrings.text("サンプルレート","Sample rate"),rate>0?String.format(Locale.ROOT,"%.1f kHz",rate/1000.0):null);
        long bytes=data.getLong(SIZE,-1);field(text,jp.virtualcd.player.LanguageStrings.text("曲ファイルのサイズ","Track file size"),bytes>=0?String.format(Locale.ROOT,"%.2f MiB（%,d bytes）",bytes/1048576.0,bytes):null);
        field(text,jp.virtualcd.player.LanguageStrings.text("ファイル名","Filename"),data.getString(FILE));
        android.net.Uri uri=item.localConfiguration!=null?item.localConfiguration.uri:android.net.Uri.parse(item.mediaId);
        boolean archive="zipmp3".equals(uri.getScheme());field(text,jp.virtualcd.player.LanguageStrings.text("格納形式","Storage format"),archive?jp.virtualcd.player.LanguageStrings.text("ZIP内の音楽ファイル","Music inside ZIP"):jp.virtualcd.player.LanguageStrings.text("通常ファイル","Regular file"));
        String source=data.getString(ListeningState.ALBUM_URI);
        if(source==null||source.isEmpty())source=archive&&uri.isHierarchical()?uri.getQueryParameter("document"):uri.toString();
        field(text,jp.virtualcd.player.LanguageStrings.text("保存元（URI）","Source (URI)"),source);return text.toString().trim();
    }
    private static long number(String value){try{return Long.parseLong(value);}catch(Exception ignored){return -1;}}
    private static void field(StringBuilder out,String label,CharSequence value){out.append(label).append("\n").append(value==null||value.toString().trim().isEmpty()?jp.virtualcd.player.LanguageStrings.text("不明／未設定","Unknown / not set"):value).append("\n\n");}
    public static Dialog show(Activity activity,MediaItem item){
        ScrollView scroll=new ScrollView(activity);TextView text=new TextView(activity);text.setText(describe(item));text.setTextColor(0xffe8edf5);text.setTextSize(15);text.setTextIsSelectable(true);
        int padding=PlayerStyle.dp(activity,20);text.setPadding(padding,padding,padding,padding);scroll.setBackgroundColor(0xff19212b);scroll.addView(text);
        return new android.app.AlertDialog.Builder(activity).setTitle(jp.virtualcd.player.LanguageStrings.text("曲のプロパティ（読み取り専用）","Track properties (read-only)")).setView(scroll).setNeutralButton(jp.virtualcd.player.LanguageStrings.text("歌詞","Lyrics"),(d,w)->LyricsStore.show(activity,item)).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).show();
    }
}
