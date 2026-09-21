package jp.virtualcd.player;

import android.app.*;
import android.content.*;
import android.media.*;
import android.os.*;
import android.widget.Toast;

/** Durable timer completion evidence, separate from crashes and user-requested exits. */
final class TimerNotice {
    static final String CHANNEL="auto-stop-complete";
    static final int ID=3010;
    static boolean record(android.content.SharedPreferences prefs,long at,long limit,long used){
        // Persist the reason and budget reset together, before releasing the service.
        return prefs.edit().putLong("lastStoppedAt",at).putString("lastStopReason","timer")
            .putLong("lastStopLimit",limit).putLong("lastStopUsed",used).putBoolean("stopNoticePending",true).putLong("used",0).commit();
    }
    static String summary(android.content.SharedPreferences prefs){
        long at=prefs.getLong("lastStoppedAt",0);if(at<=0)return jp.virtualcd.player.LanguageStrings.text("タイマー終了の記録はまだありません。","No timer completion recorded yet.");
        String time=java.text.DateFormat.getDateTimeInstance(java.text.DateFormat.MEDIUM,java.text.DateFormat.SHORT).format(new java.util.Date(at));
        return time+jp.virtualcd.player.LanguageStrings.text("\n自動停止タイマー（","\nThe auto-stop timer (")+(prefs.getLong("lastStopLimit",0)/60000)+jp.virtualcd.player.LanguageStrings.text("分）により再生を停止しました。"," minutes) stopped playback.");
    }
    static void announce(Context context){
        String text=jp.virtualcd.player.LanguageStrings.text("自動停止タイマーの時間になりました。再生を停止しました。","The auto-stop timer has elapsed. Playback stopped.");
        try{Toast.makeText(context,text,Toast.LENGTH_LONG).show();}catch(RuntimeException ignored){}
        try{
            NotificationManager manager=context.getSystemService(NotificationManager.class);
            NotificationChannel channel=new NotificationChannel(CHANNEL,jp.virtualcd.player.LanguageStrings.text("自動停止タイマーの終了","Auto-stop timer completed"),NotificationManager.IMPORTANCE_DEFAULT);
            channel.setDescription(jp.virtualcd.player.LanguageStrings.text("タイマー終了の通知。短い終了音と、次回起動時の終了理由を表示します。","Timer completion notification with a short sound and the stop reason on next launch."));channel.setSound(null,null);channel.enableVibration(false);manager.createNotificationChannel(channel);
            PendingIntent open=PendingIntent.getActivity(context,3010,new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
            if(manager.areNotificationsEnabled())manager.notify(ID,new Notification.Builder(context,CHANNEL).setSmallIcon(android.R.drawable.ic_lock_idle_alarm).setContentTitle(jp.virtualcd.player.LanguageStrings.text("自動停止タイマーで終了しました","Stopped by the auto-stop timer"))
                .setContentText(text).setStyle(new Notification.BigTextStyle().bigText(summary(AutoStopSettings.preferences(context))))
                .setContentIntent(open).setAutoCancel(true).setCategory(Notification.CATEGORY_STATUS).setVisibility(Notification.VISIBILITY_PRIVATE).build());
            AudioManager audio=context.getSystemService(AudioManager.class);channel=manager.getNotificationChannel(CHANNEL);
            // Never raise volume or bypass silent mode, DND, or a disabled notification channel.
            if(manager.areNotificationsEnabled()&&channel.getImportance()>=NotificationManager.IMPORTANCE_DEFAULT&&manager.getCurrentInterruptionFilter()==NotificationManager.INTERRUPTION_FILTER_ALL
                &&audio.getRingerMode()==AudioManager.RINGER_MODE_NORMAL&&audio.getStreamVolume(AudioManager.STREAM_NOTIFICATION)>0){
                ToneGenerator tone=new ToneGenerator(AudioManager.STREAM_NOTIFICATION,35);
                try{tone.startTone(ToneGenerator.TONE_PROP_BEEP,200);new Handler(Looper.getMainLooper()).postDelayed(tone::release,500);}catch(RuntimeException error){tone.release();}
            }
        }catch(RuntimeException error){android.util.Log.w("AutoStopTimer","Timer notification unavailable",error);}
    }
    static void showPending(Activity activity){
        var prefs=AutoStopSettings.preferences(activity);if(!prefs.getBoolean("stopNoticePending",false)||activity.isFinishing()||activity.isDestroyed())return;
        long at=prefs.getLong("lastStoppedAt",0);
        new AlertDialog.Builder(activity).setTitle(jp.virtualcd.player.LanguageStrings.text("前回はタイマーで終了しました","Last session ended by the timer")).setMessage(summary(prefs)+jp.virtualcd.player.LanguageStrings.text("\n\nクラッシュではなく、設定時間による自動停止です。","\n\nThis was a scheduled automatic stop, not a crash."))
            .setPositiveButton(jp.virtualcd.player.LanguageStrings.text("確認","Confirm"),(d,w)->{if(prefs.getLong("lastStoppedAt",0)==at)prefs.edit().putBoolean("stopNoticePending",false).apply();activity.getSystemService(NotificationManager.class).cancel(ID);}).show();
    }
}
