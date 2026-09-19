package jp.virtualcd.player;

import android.app.PendingIntent;
import android.content.Intent;
import androidx.media3.common.AudioAttributes;
import androidx.media3.common.C;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.session.MediaSession;
import androidx.media3.session.MediaSessionService;
import jp.virtualcd.player.archive.ZipTrackDataSource;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public final class PlaybackService extends MediaSessionService {
    private MediaSession session;
    private ExoPlayer player;
    private jp.virtualcd.player.library.ListeningState state;
    private final android.os.Handler handler=new android.os.Handler(android.os.Looper.getMainLooper());
    private boolean historyPending=true;
    private android.content.SharedPreferences timerPrefs;
    private PlaybackBudget budget;
    private boolean timerEnabled,expiring;
    private long timerLimit;
    private final android.content.SharedPreferences.OnSharedPreferenceChangeListener timerChanged=(p,key)->{
        if(!"enabled".equals(key)&&!"minutes".equals(key))return;
        boolean enabled=AutoStopSettings.enabled(p);
        if(enabled!=timerEnabled)budget.reset(android.os.SystemClock.elapsedRealtime());
        timerEnabled=enabled;timerLimit=AutoStopSettings.minutes(p)*60000L;updateTimer();saveTimer();
    };
    private final Runnable timerTick=()->updateTimer();
    private void saveTimer(){if(timerPrefs!=null&&budget!=null)timerPrefs.edit().putLong("used",budget.used()).apply();}
    private void updateTimer(){
        if(player==null||budget==null||expiring)return;
        handler.removeCallbacks(timerTick);
        budget.update(android.os.SystemClock.elapsedRealtime(),timerEnabled&&player.isPlaying());
        if(timerEnabled&&budget.due(timerLimit)){expireTimer();return;}
        if(timerEnabled&&player.isPlaying())handler.postDelayed(timerTick,Math.min(1000,Math.max(1,timerLimit-budget.used())));
    }
    private void expireTimer(){
        if(expiring)return;expiring=true;handler.removeCallbacksAndMessages(null);
        player.pause();state.save(player);player.stop();
        long stoppedAt=System.currentTimeMillis();
        boolean recorded=TimerNotice.record(timerPrefs,stoppedAt,timerLimit,budget.used());
        android.util.Log.i("AutoStopTimer","reason=timer at="+stoppedAt+" limitMs="+timerLimit+" usedMs="+budget.used()+" recorded="+recorded);
        budget.reset(android.os.SystemClock.elapsedRealtime());saveTimer();
        if(session!=null){removeSession(session);session.release();session=null;}
        player.release();player=null;
        TimerNotice.announce(this);
        AutoStopSettings.finishScreens();
        stopForeground(STOP_FOREGROUND_REMOVE);stopSelf();
    }
    private jp.virtualcd.player.audio.SoundPreferences sound;
    private jp.virtualcd.player.audio.SoundProcessor soundProcessor;
    private final android.content.SharedPreferences.OnSharedPreferenceChangeListener soundChanged=(p,key)->applySound();
    private void applySound(){if(player!=null){soundProcessor.setConfig(sound.read());player.setPauseAtEndOfMediaItems(!sound.gapless());
        var parameters=sound.playbackParameters();if(!parameters.equals(player.getPlaybackParameters()))player.setPlaybackParameters(parameters);}}
    private final Runnable checkpoint=new Runnable(){public void run(){if(player!=null){updateTimer();if(player==null)return;state.save(player);saveTimer();handler.postDelayed(this,5000);}}};
    @Override public void onCreate() {
        super.onCreate();
        timerPrefs=AutoStopSettings.preferences(this);timerEnabled=AutoStopSettings.enabled(timerPrefs);timerLimit=AutoStopSettings.minutes(timerPrefs)*60000L;
        budget=new PlaybackBudget(timerPrefs.getLong("used",0),android.os.SystemClock.elapsedRealtime());
        timerPrefs.registerOnSharedPreferenceChangeListener(timerChanged);
        sound=new jp.virtualcd.player.audio.SoundPreferences(this);
        soundProcessor=new jp.virtualcd.player.audio.SoundProcessor(sound.read());
        player=new ExoPlayer.Builder(this,new jp.virtualcd.player.audio.SoundRenderers(this,soundProcessor))
            .setMediaSourceFactory(new DefaultMediaSourceFactory(()->new androidx.media3.datasource.DefaultDataSource(this,new ZipTrackDataSource(this)))).build();
        applySound();sound.preferences.registerOnSharedPreferenceChangeListener(soundChanged);
        player.setAudioAttributes(new AudioAttributes.Builder().setUsage(C.USAGE_MEDIA)
            .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(),true);
        player.setHandleAudioBecomingNoisy(true);
        player.setWakeMode(C.WAKE_MODE_LOCAL);
        state=new jp.virtualcd.player.library.ListeningState(this);state.restore(player);
        new jp.virtualcd.player.audio.BoundaryPlayback(player);
        player.addListener(new androidx.media3.common.Player.Listener(){
            @Override public void onMediaItemTransition(androidx.media3.common.MediaItem item,int reason){historyPending=true;recordHistory();}
            @Override public void onIsPlayingChanged(boolean playing){if(expiring||player==null)return;recordHistory();state.save(player);updateTimer();saveTimer();}
            @Override public void onEvents(androidx.media3.common.Player p,androidx.media3.common.Player.Events events){
                if(expiring)return;
                if(events.contains(androidx.media3.common.Player.EVENT_POSITION_DISCONTINUITY)||events.contains(androidx.media3.common.Player.EVENT_MEDIA_ITEM_TRANSITION)
                    ||events.contains(androidx.media3.common.Player.EVENT_SHUFFLE_MODE_ENABLED_CHANGED)||events.contains(androidx.media3.common.Player.EVENT_REPEAT_MODE_CHANGED))state.save(p);
            }
        });
        handler.postDelayed(checkpoint,5000);
        var activity=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        session=new MediaSession.Builder(this,player).setSessionActivity(activity).build();
    }
    @Override public MediaSession onGetSession(MediaSession.ControllerInfo controller) { return session; }
    private void recordHistory(){if(historyPending&&player.isPlaying()){state.played(player.getCurrentMediaItem());historyPending=false;}}
    @Override public void onTaskRemoved(Intent rootIntent){if(player!=null)state.save(player);super.onTaskRemoved(rootIntent);}
    @Override public void onDestroy() {
        if(timerPrefs!=null)timerPrefs.unregisterOnSharedPreferenceChangeListener(timerChanged);
        if(budget!=null&&!expiring){budget.update(android.os.SystemClock.elapsedRealtime(),false);saveTimer();}
        if(sound!=null)sound.preferences.unregisterOnSharedPreferenceChangeListener(soundChanged);
        handler.removeCallbacksAndMessages(null);if(player!=null)state.save(player);
        if(session!=null) {session.getPlayer().release();session.release();session=null;}
        player=null;
        super.onDestroy();
    }
}
