package jp.virtualcd.player.library;

import android.content.Context;
import android.view.*;
import android.widget.*;
import androidx.media3.common.MediaItem;
import java.util.List;
import java.util.function.Consumer;

/** Separate, non-focusable star target; tapping the row still plays and tracks checked state. */
public final class TrackAdapter extends BaseAdapter {
    private final Context context;private final List<MediaItem> items;private final ListeningState state;private final Consumer<MediaItem> favorite,properties;
    public TrackAdapter(Context context,List<MediaItem> items,ListeningState state,Consumer<MediaItem> favorite,Consumer<MediaItem> properties){this.context=context;this.items=items;this.state=state;this.favorite=favorite;this.properties=properties;}
    public int getCount(){return items.size();}public MediaItem getItem(int p){return items.get(p);}public long getItemId(int p){return p;}
    public static String number(MediaItem item,int position){Integer n=item.mediaMetadata.trackNumber,d=item.mediaMetadata.discNumber;
        String value=String.format(java.util.Locale.ROOT,"%02d",n!=null&&n>0?n:position+1);return d!=null&&d>1?d+"-"+value:value;}
    public View getView(int p,View recycled,ViewGroup parent){Row row=recycled instanceof Row?(Row)recycled:new Row(context);MediaItem item=getItem(p);
        row.number.setText(number(item,p));row.title.setText(item.mediaMetadata.title);FavoriteButton.bind(row.star,state.contains("favoriteTracks",item.mediaId),String.valueOf(item.mediaMetadata.title));
        row.star.setOnClickListener(v->favorite.accept(item));row.info.setContentDescription(item.mediaMetadata.title+"：曲のプロパティ");row.info.setOnClickListener(v->properties.accept(item));return row;}
    private static final class Row extends LinearLayout implements Checkable {
        final TextView number,title;final Button star;final ImageButton info;private boolean checked;
        Row(Context c){super(c);setGravity(Gravity.CENTER_VERTICAL);setMinimumHeight(PlayerStyle.dp(c,52));setPadding(PlayerStyle.dp(c,6),0,0,0);
            number=new TextView(c);number.setTextSize(12);number.setGravity(Gravity.CENTER);addView(number,new LayoutParams(PlayerStyle.dp(c,44),-2));
            title=new TextView(c);title.setTextSize(16);title.setMaxLines(2);title.setEllipsize(android.text.TextUtils.TruncateAt.END);addView(title,new LayoutParams(0,-2,1));
            star=FavoriteButton.create(c);addView(star,new LayoutParams(PlayerStyle.dp(c,48),PlayerStyle.dp(c,48)));
            info=new ImageButton(c);info.setFocusable(false);info.setImageDrawable(new InfoIcon());info.setScaleType(ImageView.ScaleType.FIT_CENTER);
            int pad=PlayerStyle.dp(c,13);info.setPadding(pad,pad,pad,pad);info.setBackground(new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x3370cde6),null,null));
            info.setTooltipText("曲のプロパティ");addView(info,new LayoutParams(PlayerStyle.dp(c,44),PlayerStyle.dp(c,48)));setChecked(false);}
        public boolean isChecked(){return checked;}public void toggle(){setChecked(!checked);}public void setChecked(boolean checked){this.checked=checked;setActivated(checked);
            setBackgroundColor(checked?0xff29596a:android.graphics.Color.TRANSPARENT);title.setTextColor(0xffeef4fa);number.setTextColor(checked?0xffb9eefa:0xff91a7b9);}
    }
    private static final class InfoIcon extends android.graphics.drawable.Drawable {
        private final android.graphics.Paint paint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        public void draw(android.graphics.Canvas canvas){canvas.save();canvas.translate(getBounds().left,getBounds().top);canvas.scale(getBounds().width()/24f,getBounds().height()/24f);
            paint.setColor(0xff9cafbf);paint.setStrokeWidth(1.7f);paint.setStyle(android.graphics.Paint.Style.STROKE);canvas.drawCircle(12,12,9,paint);
            paint.setStrokeCap(android.graphics.Paint.Cap.ROUND);canvas.drawLine(12,11,12,17,paint);paint.setStyle(android.graphics.Paint.Style.FILL);canvas.drawCircle(12,7,1,paint);canvas.restore();}
        public int getIntrinsicWidth(){return 24;}public int getIntrinsicHeight(){return 24;}
        public void setAlpha(int a){}public void setColorFilter(android.graphics.ColorFilter f){}public int getOpacity(){return android.graphics.PixelFormat.TRANSLUCENT;}
    }
}
