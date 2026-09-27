package jp.virtualcd.player;

import android.app.Activity;
import android.app.AlertDialog;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;
import androidx.media3.common.Player;
import jp.virtualcd.player.audio.SoundPreferences;

/** Same live playback options in the library, track list and 3D view. */
public final class PlaybackOptions {
    private PlaybackOptions(){}
    public static void bind(Button button,Player player){
        button.setEnabled(player!=null);
        button.setSelected(player!=null&&(player.getShuffleModeEnabled()||player.getRepeatMode()!=Player.REPEAT_MODE_OFF));
    }
    public static AlertDialog show(Activity activity,Player player){
        return show(activity,player,new SoundPreferences(activity));
    }
    static AlertDialog show(Activity activity,Player player,SoundPreferences store){
        var body=new LinearLayout(activity);body.setOrientation(LinearLayout.VERTICAL);
        int padding=Math.round(20*activity.getResources().getDisplayMetrics().density);
        body.setPadding(padding,0,padding,padding);
        var scroll=new ScrollView(activity);scroll.addView(body);
        var repeat=new CheckBox(activity);repeat.setText(LanguageStrings.text("全曲リピート","Repeat all"));repeat.setTag("repeat");body.addView(repeat);
        var shuffle=new CheckBox(activity);shuffle.setText(LanguageStrings.text("シャッフル","Shuffle"));shuffle.setTag("shuffle");body.addView(shuffle);
        repeat.setOnClickListener(v->player.setRepeatMode(repeat.isChecked()?Player.REPEAT_MODE_ALL:Player.REPEAT_MODE_OFF));
        shuffle.setOnClickListener(v->player.setShuffleModeEnabled(shuffle.isChecked()));
        var speedLabel=new TextView(activity);body.addView(speedLabel);
        var speed=new SeekBar(activity);speed.setMax(30);speed.setTag("speed");speed.setContentDescription(LanguageStrings.text("再生速度","Playback speed"));body.addView(speed);
        var pitchLabel=new TextView(activity);body.addView(pitchLabel);
        var pitch=new SeekBar(activity);pitch.setMax(24);pitch.setTag("pitch");pitch.setContentDescription(LanguageStrings.text("ピッチ","Pitch"));body.addView(pitch);
        int touchHeight=Math.round(48*activity.getResources().getDisplayMetrics().density);
        speed.setLayoutParams(new LinearLayout.LayoutParams(-1,touchHeight));pitch.setLayoutParams(new LinearLayout.LayoutParams(-1,touchHeight));
        Runnable refresh=()->{
            speed.setProgress((store.speedPercent()-50)/5);pitch.setProgress(store.pitchSemitones()+12);
            speedLabel.setText(String.format(java.util.Locale.ROOT,LanguageStrings.text("再生速度：%.2f倍","Playback speed: %.2f×"),store.speedPercent()/100.0));
            pitchLabel.setText(String.format(java.util.Locale.ROOT,LanguageStrings.text("ピッチ：%+d 半音","Pitch: %+d semitones"),store.pitchSemitones()));
        };
        var tuningListener=new SeekBar.OnSeekBarChangeListener(){
            public void onStartTrackingTouch(SeekBar s){}
            public void onStopTrackingTouch(SeekBar s){}
            public void onProgressChanged(SeekBar s,int value,boolean user){if(user){
                store.saveTuning(s==speed?50+value*5:store.speedPercent(),s==pitch?value-12:store.pitchSemitones());refresh.run();
            }}
        };
        speed.setOnSeekBarChangeListener(tuningListener);pitch.setOnSeekBarChangeListener(tuningListener);
        var reset=new Button(activity);reset.setTag("resetTuning");reset.setText(LanguageStrings.text("速度・ピッチを標準に戻す","Reset speed and pitch"));body.addView(reset);
        reset.setOnClickListener(v->{store.saveTuning(100,0);refresh.run();});
        var note=new TextView(activity);note.setText(LanguageStrings.text("再生中に反映・自動保存。速度とピッチは独立して調整できます。","Applied during playback and saved automatically. Speed and pitch can be adjusted independently."));body.addView(note);
        refresh.run();
        var dialog=new AlertDialog.Builder(activity).setTitle(LanguageStrings.text("再生オプション","Playback options"))
            .setView(scroll).setPositiveButton(LanguageStrings.text("閉じる","Close"),null).create();
        var listener=new Player.Listener(){
            @Override public void onEvents(Player p,Player.Events events){
                if(dialog.isShowing()){repeat.setChecked(p.getRepeatMode()!=Player.REPEAT_MODE_OFF);shuffle.setChecked(p.getShuffleModeEnabled());}
            }
        };
        android.content.SharedPreferences.OnSharedPreferenceChangeListener changed=(prefs,key)->refresh.run();
        store.preferences.registerOnSharedPreferenceChangeListener(changed);
        player.addListener(listener);dialog.setOnDismissListener(d->{player.removeListener(listener);store.preferences.unregisterOnSharedPreferenceChangeListener(changed);});dialog.show();
        repeat.setChecked(player.getRepeatMode()!=Player.REPEAT_MODE_OFF);
        shuffle.setChecked(player.getShuffleModeEnabled());
        return dialog;
    }
}
