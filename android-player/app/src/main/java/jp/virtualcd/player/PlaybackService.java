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
    private jp.virtualcd.player.audio.SoundPreferences sound;
    private jp.virtualcd.player.audio.SoundProcessor soundProcessor;
    private final android.content.SharedPreferences.OnSharedPreferenceChangeListener soundChanged=(p,key)->applySound();
    private void applySound(){if(player!=null){soundProcessor.setConfig(sound.read());player.setPauseAtEndOfMediaItems(!sound.gapless());
        var parameters=sound.playbackParameters();if(!parameters.equals(player.getPlaybackParameters()))player.setPlaybackParameters(parameters);}}
    private final Runnable checkpoint=new Runnable(){public void run(){if(player!=null){state.save(player);handler.postDelayed(this,5000);}}};
    @Override public void onCreate() {
        super.onCreate();
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
            @Override public void onIsPlayingChanged(boolean playing){recordHistory();state.save(player);}
            @Override public void onEvents(androidx.media3.common.Player p,androidx.media3.common.Player.Events events){
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
        if(sound!=null)sound.preferences.unregisterOnSharedPreferenceChangeListener(soundChanged);
        handler.removeCallbacksAndMessages(null);if(player!=null)state.save(player);
        if(session!=null) {session.getPlayer().release();session.release();session=null;}
        player=null;
        super.onDestroy();
    }
}
