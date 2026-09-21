package jp.virtualcd.player;

import android.content.Context;
import android.text.TextUtils;
import android.widget.TextView;
import jp.virtualcd.player.library.PlayerStyle;

/** A stable three-line banner; full details remain available by tapping it. */
public final class SyncStatusView extends TextView {
    public SyncStatusView(Context context){
        super(context);
        setTextSize(12);
        setLines(3);
        setGravity(android.view.Gravity.TOP);
        setEllipsize(TextUtils.TruncateAt.END);
        int padding=PlayerStyle.dp(context,8);
        setPadding(padding,padding,padding,padding);
        setTextColor(0xff211508);
        setBackgroundColor(0xffffb74d);
    }
    public void showProgress(String message){
        setText(message);
        boolean complete=message.startsWith(jp.virtualcd.player.LanguageStrings.text("PCからの転送完了","Transfer from PC complete"))||message.startsWith(jp.virtualcd.player.LanguageStrings.text("同期済みです","Already synced"));
        setBackgroundColor(complete?0xffa5d6a7:0xffffb74d);
        setVisibility(VISIBLE);
    }
}
