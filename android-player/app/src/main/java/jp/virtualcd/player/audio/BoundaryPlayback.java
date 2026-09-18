package jp.virtualcd.player.audio;

import androidx.media3.common.Player;
import androidx.media3.exoplayer.ExoPlayer;

/** Drained/restarted playback when gapless is disabled; never resumes explicit user pauses. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class BoundaryPlayback implements Player.Listener {
    private final ExoPlayer player;
    public BoundaryPlayback(ExoPlayer player){this.player=player;player.addListener(this);}
    @Override public void onPlayWhenReadyChanged(boolean ready,int reason){
        if(!ready&&reason==Player.PLAY_WHEN_READY_CHANGE_REASON_END_OF_MEDIA_ITEM){
            if(player.getRepeatMode()==Player.REPEAT_MODE_ONE){player.seekTo(0);player.play();}
            else if(player.hasNextMediaItem()){player.seekToNextMediaItem();player.play();}
        }
    }
}
