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
        var on=new Switch(activity);on.setText("自動停止タイマーを使う");on.setChecked(enabled(prefs));box.addView(on);
        var hint=new TextView(activity);hint.setText("再生中の時間を累積して停止・画面を閉じます。一時停止／読込待ちは数えません。停止時は再生位置を保存します。");box.addView(hint);
        var last=new TextView(activity);last.setText("\n前回のタイマー終了\n"+TimerNotice.summary(prefs)+"\n\n終了時は短い音と通知でお知らせします。マナーモード・通知オフ時は鳴りません。\n");box.addView(last);
        if(android.os.Build.VERSION.SDK_INT>=33&&activity.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED){
            var allow=new Button(activity);allow.setText("終了通知を許可する");box.addView(allow);allow.setOnClickListener(v->activity.requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},3010));
        }
        var input=new EditText(activity);input.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);input.setSingleLine(true);input.setText(String.valueOf(minutes(prefs)));input.setHint("1〜1440分（3時間＝180分）");box.addView(input);
        var unit=new TextView(activity);unit.setText("停止までの累積再生時間（分） · 1〜1440分\nON/OFFの切替で累積時間をリセットします。時間変更時は累積を引き継ぎ、既に超えていれば停止します。");box.addView(unit);
        input.setEnabled(on.isChecked());on.setOnCheckedChangeListener((b,checked)->input.setEnabled(checked));
        var scroll=new ScrollView(activity);scroll.addView(box);
        var dialog=new AlertDialog.Builder(activity).setTitle("自動停止タイマー").setView(scroll).setNegativeButton("キャンセル",null).setPositiveButton("保存",null).create();
        dialog.setOnShowListener(v->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(b->{
            int value;try{value=Integer.parseInt(input.getText().toString().trim());}catch(Exception e){value=0;}
            if(value<1||value>1440){input.setError("1〜1440分で指定してください");return;}
            var edit=prefs.edit().putBoolean("enabled",on.isChecked()).putInt("minutes",value);
            if(enabled(prefs)!=on.isChecked())edit.putLong("used",0);
            edit.apply();dialog.dismiss();
        }));dialog.show();
    }
}
