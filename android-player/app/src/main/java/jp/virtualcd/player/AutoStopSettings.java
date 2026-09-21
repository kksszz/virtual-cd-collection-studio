package jp.virtualcd.player;

import android.app.*;
import android.content.*;
import android.widget.*;
import java.util.*;

public final class AutoStopSettings {
    private static final Set<Activity> screens=Collections.newSetFromMap(new WeakHashMap<>());
    public static void track(Activity a){screens.add(a);}
    public static void untrack(Activity a){screens.remove(a);}
    public static void finishScreens(){for(Activity a:new ArrayList<>(screens))if(!a.isFinishing())a.finish();}
    public static SharedPreferences preferences(Context c){return c.getSharedPreferences("auto-stop-v1",0);}
    public static boolean enabled(SharedPreferences p){return p.getBoolean("enabled",true);}
    public static int minutes(SharedPreferences p){return Math.max(1,Math.min(1440,p.getInt("minutes",180)));}
    public static void show(Activity activity){
        var prefs=preferences(activity);var box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);int pad=(int)(20*activity.getResources().getDisplayMetrics().density);box.setPadding(pad,pad,pad,pad);
        var on=new Switch(activity);on.setText(jp.virtualcd.player.LanguageStrings.text("自動停止タイマーを使う","Use auto-stop timer"));on.setChecked(enabled(prefs));box.addView(on);
        var hint=new TextView(activity);hint.setText(jp.virtualcd.player.LanguageStrings.text("再生中の時間を累積して停止・画面を閉じます。一時停止／読込待ちは数えません。停止時は再生位置を保存します。","Stops playback and closes the screen after the accumulated playing time. Pauses and loading do not count. Playback position is saved."));box.addView(hint);
        var last=new TextView(activity);last.setText(jp.virtualcd.player.LanguageStrings.text("\n前回のタイマー終了\n","\nLast timer completion\n")+TimerNotice.summary(prefs)+jp.virtualcd.player.LanguageStrings.text("\n\n終了時は短い音と通知でお知らせします。マナーモード・通知オフ時は鳴りません。\n","\n\nA short sound and notification announce completion. No sound in silent mode or when notifications are disabled.\n"));box.addView(last);
        if(android.os.Build.VERSION.SDK_INT>=33&&activity.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED){
            var allow=new Button(activity);allow.setText(jp.virtualcd.player.LanguageStrings.text("終了通知を許可する","Allow completion notifications"));box.addView(allow);allow.setOnClickListener(v->activity.requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},3010));
        }
        var input=new EditText(activity);input.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);input.setSingleLine(true);input.setText(String.valueOf(minutes(prefs)));input.setHint(jp.virtualcd.player.LanguageStrings.text("1〜1440分（3時間＝180分）","1–1440 minutes (3 hours = 180 minutes)"));box.addView(input);
        var unit=new TextView(activity);unit.setText(jp.virtualcd.player.LanguageStrings.text("停止までの累積再生時間（分） · 1〜1440分\nON/OFFの切替で累積時間をリセットします。時間変更時は累積を引き継ぎ、既に超えていれば停止します。","Accumulated playback time before stopping (minutes), 1–1440.\nTurning ON/OFF resets elapsed time. Changing the duration retains elapsed time and stops playback if already exceeded."));box.addView(unit);
        input.setEnabled(on.isChecked());on.setOnCheckedChangeListener((b,checked)->input.setEnabled(checked));
        var scroll=new ScrollView(activity);scroll.addView(box);
        var dialog=new AlertDialog.Builder(activity).setTitle(jp.virtualcd.player.LanguageStrings.text("自動停止タイマー","Auto-stop timer")).setView(scroll).setNegativeButton(jp.virtualcd.player.LanguageStrings.text("キャンセル","Cancel"),null).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("保存","Save"),null).create();
        dialog.setOnShowListener(v->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(b->{
            int value;try{value=Integer.parseInt(input.getText().toString().trim());}catch(Exception e){value=0;}
            if(value<1||value>1440){input.setError(jp.virtualcd.player.LanguageStrings.text("1〜1440分で指定してください","Enter 1–1440 minutes"));return;}
            var edit=prefs.edit().putBoolean("enabled",on.isChecked()).putInt("minutes",value);
            if(enabled(prefs)!=on.isChecked())edit.putLong("used",0);
            edit.apply();dialog.dismiss();
        }));dialog.show();
    }
}
