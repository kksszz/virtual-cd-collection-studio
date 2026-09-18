package jp.virtualcd.player.library;

import android.content.Context;
import android.widget.Button;

public final class FavoriteButton {
    public static Button create(Context context){Button b=new Button(context);PlayerStyle.button(b);b.setTextSize(24);b.setFocusable(false);b.setBackgroundColor(android.graphics.Color.TRANSPARENT);return b;}
    public static void bind(Button b,boolean favorite,String title){b.setText(favorite?"★":"☆");b.setTextColor(favorite?0xffffd166:0xff9cafbf);
        String description=title+"："+(favorite?"お気に入りを解除":"お気に入りに登録");b.setContentDescription(description);b.setTooltipText(description);}
}
