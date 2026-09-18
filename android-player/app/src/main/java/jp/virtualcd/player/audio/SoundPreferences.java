package jp.virtualcd.player.audio;

import android.content.Context;
import android.content.SharedPreferences;

public final class SoundPreferences {
    public final SharedPreferences preferences;
    public SoundPreferences(Context context){this(context,"sound-v1");}
    public SoundPreferences(Context context,String namespace){preferences=context.getSharedPreferences(namespace,0);}
    public boolean gapless(){return preferences.getBoolean("gapless",true);}
    public int speedPercent(){return Math.max(50,Math.min(200,preferences.getInt("speedPercent",100)));}
    public int pitchSemitones(){return Math.max(-12,Math.min(12,preferences.getInt("pitchSemitones",0)));}
    public androidx.media3.common.PlaybackParameters playbackParameters(){return new androidx.media3.common.PlaybackParameters(speedPercent()/100f,(float)Math.pow(2,pitchSemitones()/12.0));}
    public void saveTuning(int speedPercent,int semitones){preferences.edit().putInt("speedPercent",Math.max(50,Math.min(200,speedPercent)))
        .putInt("pitchSemitones",Math.max(-12,Math.min(12,semitones))).apply();}
    public SoundConfig read(){int[] bands=new int[10];for(int i=0;i<10;i++)bands[i]=preferences.getInt("band"+i,0);
        return new SoundConfig(preferences.getBoolean("faithful",true),preferences.getInt("mode",0),preferences.getBoolean("normalize",false),
            preferences.getBoolean("clarity",false),preferences.getBoolean("eq",false),preferences.getBoolean("bass",false),preferences.getInt("amount",50),bands);}
    public void save(SoundConfig c,boolean gapless){var e=preferences.edit().putBoolean("gapless",gapless).putBoolean("faithful",c.faithful).putInt("mode",c.mode)
        .putBoolean("normalize",c.normalize).putBoolean("clarity",c.clarity).putBoolean("eq",c.eq).putBoolean("bass",c.bass).putInt("amount",c.amount);
        for(int i=0;i<10;i++)e.putInt("band"+i,c.gain(i));e.apply();}
}
