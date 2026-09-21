package jp.virtualcd.player.library;

import android.app.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import org.json.JSONObject;
import java.util.List;
import java.util.function.Consumer;

/** Compact sheet; title, artist and time have independent hierarchy and truncation. */
public final class SavedListDialog {
    public static Dialog show(Activity activity,List<JSONObject> entries,String key,String currentId,Consumer<JSONObject> select,Consumer<JSONObject> remove){
        boolean history="history".equals(key);var dialog=new Dialog(activity);dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
        var root=new LinearLayout(activity);root.setOrientation(LinearLayout.VERTICAL);root.setPadding(dp(activity,16),dp(activity,12),dp(activity,16),dp(activity,12));root.setBackground(PlayerStyle.panel(activity,0xff19212b,20));
        var grip=new View(activity);grip.setBackground(PlayerStyle.panel(activity,0xff536171,3));var gripParams=new LinearLayout.LayoutParams(dp(activity,36),dp(activity,4));gripParams.gravity=Gravity.CENTER;gripParams.bottomMargin=dp(activity,16);root.addView(grip,gripParams);
        var header=new LinearLayout(activity);header.setGravity(Gravity.CENTER_VERTICAL);root.addView(header);
        var title=text(activity,history?jp.virtualcd.player.LanguageStrings.text("再生履歴","Playback history"):"favoriteAlbums".equals(key)?jp.virtualcd.player.LanguageStrings.text("お気に入りのアルバム","Favorite albums"):jp.virtualcd.player.LanguageStrings.text("お気に入りの曲","Favorite tracks"),21,0xffeef4fa);title.setTypeface(null,Typeface.BOLD);header.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        var close=new Button(activity);close.setText(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"));PlayerStyle.button(close);header.addView(close,new LinearLayout.LayoutParams(dp(activity,64),dp(activity,48)));close.setOnClickListener(v->dialog.dismiss());
        var subtitle=text(activity,entries.size()+jp.virtualcd.player.LanguageStrings.text("件  ·  "," items · ")+(history?jp.virtualcd.player.LanguageStrings.text("最後に再生した順","Last played"):jp.virtualcd.player.LanguageStrings.text("★で登録を解除","Tap ★ to remove")),12,0xff94a7b9);subtitle.setPadding(0,dp(activity,8),0,dp(activity,12));root.addView(subtitle);
        var list=new ListView(activity);list.setDivider(new ColorDrawable(0xff2b3745));list.setDividerHeight(dp(activity,1));list.setSelector(new ColorDrawable(0x225fcce8));list.setClipToPadding(false);
        root.addView(list,new LinearLayout.LayoutParams(-1,0,1));
        list.setAdapter(new BaseAdapter(){
            public int getCount(){return entries.size();}public Object getItem(int p){return entries.get(p);}public long getItemId(int p){return p;}
            public View getView(int position,View recycled,ViewGroup parent){
                Row row;if(recycled==null){row=new Row();var box=new LinearLayout(activity);box.setGravity(Gravity.CENTER_VERTICAL);box.setPadding(0,dp(activity,13),0,dp(activity,13));
                    row.icon=text(activity,history?"▶":"★",16,0xff70cde6);row.icon.setGravity(Gravity.CENTER);row.icon.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);box.addView(row.icon,new LinearLayout.LayoutParams(dp(activity,36),dp(activity,40)));
                    var lines=new LinearLayout(activity);lines.setOrientation(LinearLayout.VERTICAL);lines.setPadding(dp(activity,8),0,0,0);box.addView(lines,new LinearLayout.LayoutParams(0,-2,1));
                    row.title=text(activity,"",16,0xffeef4fa);row.title.setTypeface(null,Typeface.BOLD);row.title.setMaxLines(2);row.title.setEllipsize(TextUtils.TruncateAt.END);lines.addView(row.title);
                    row.artist=text(activity,"",13,0xffa9bac9);row.artist.setSingleLine(true);row.artist.setEllipsize(TextUtils.TruncateAt.END);lines.addView(row.artist);
                    row.time=text(activity,"",11,0xff7f94a8);row.time.setPadding(0,dp(activity,4),0,0);lines.addView(row.time);
                    row.star=FavoriteButton.create(activity);row.star.setVisibility(remove==null?View.GONE:View.VISIBLE);box.addView(row.star,new LinearLayout.LayoutParams(dp(activity,48),dp(activity,48)));box.setTag(row);recycled=box;
                }else row=(Row)recycled.getTag();
                JSONObject entry=entries.get(position);row.title.setText(entry.optString("title"));
                if(remove!=null){FavoriteButton.bind(row.star,true,entry.optString("title"));row.star.setOnClickListener(v->{remove.accept(entry);entries.remove(entry);notifyDataSetChanged();subtitle.setText(entries.size()+jp.virtualcd.player.LanguageStrings.text("件 · ★で登録を解除"," items · Tap ★ to remove"));});}
                String info=entry.optString("artist"),album=entry.optString("album");if(!album.isEmpty())info+=(info.isEmpty()?"":" · ")+album;
                row.artist.setText(info);row.artist.setVisibility(info.isEmpty()?View.GONE:View.VISIBLE);
                row.time.setVisibility(history?View.VISIBLE:View.GONE);if(history)row.time.setText(jp.virtualcd.player.LanguageStrings.text("最終再生  ","Last played  ")+android.text.format.DateFormat.format("yyyy/MM/dd  HH:mm",entry.optLong("lastPlayed")));
                boolean current=entry.optString("id").equals(currentId);row.title.setTextColor(current?0xff70cde6:0xffeef4fa);
                return recycled;
            }
        });
        list.setOnItemClickListener((p,v,index,id)->{dialog.dismiss();select.accept(entries.get(index));});
        dialog.setContentView(root);dialog.show();Window window=dialog.getWindow();window.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));window.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
        var attrs=window.getAttributes();attrs.dimAmount=0.6f;attrs.gravity=Gravity.BOTTOM;attrs.width=activity.getResources().getDisplayMetrics().widthPixels-dp(activity,16);attrs.height=(int)(activity.getResources().getDisplayMetrics().heightPixels*0.76f);attrs.y=dp(activity,8);window.setAttributes(attrs);
        return dialog;
    }
    private static class Row {TextView icon,title,artist,time;Button star;}
    private static int dp(Activity a,int value){return PlayerStyle.dp(a,value);}
    private static TextView text(Activity a,String value,int size,int color){var view=new TextView(a);view.setText(value);view.setTextSize(size);view.setTextColor(color);return view;}
}
