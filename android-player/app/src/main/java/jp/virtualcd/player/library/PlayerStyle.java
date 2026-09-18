package jp.virtualcd.player.library;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.drawable.*;
import android.widget.*;

public final class PlayerStyle {
    public static int dp(Context c,int n){return Math.round(n*c.getResources().getDisplayMetrics().density);}
    public static GradientDrawable panel(Context c,int color,int radius){var d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(c,radius));return d;}
    public static void button(Button b){
        var c=b.getContext();b.setAllCaps(false);b.setSingleLine(true);b.setTextSize(12);b.setMinWidth(0);b.setMinimumWidth(0);b.setMinHeight(0);b.setMinimumHeight(0);
        b.setPadding(dp(c,4),0,dp(c,4),0);b.setStateListAnimator(null);b.setElevation(0);
        int[][] states={new int[]{-android.R.attr.state_enabled},new int[]{android.R.attr.state_selected},new int[]{}};
        b.setTextColor(new ColorStateList(states,new int[]{0xff657181,0xff091820,0xffdce6ee}));
        var shapes=new StateListDrawable();shapes.addState(states[0],panel(c,0xff1b222c,10));shapes.addState(states[1],panel(c,0xff70cde6,10));shapes.addState(states[2],panel(c,0xff26313e,10));
        b.setBackgroundTintList(null);b.setBackground(new RippleDrawable(ColorStateList.valueOf(0x4470cde6),shapes,panel(c,Color.WHITE,10)));
    }
}
