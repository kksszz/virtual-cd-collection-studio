package jp.virtualcd.player;

import android.app.Instrumentation;
import android.os.Bundle;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import androidx.media3.exoplayer.ExoPlayer;
import jp.virtualcd.player.library.ListeningState;
import java.util.Arrays;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

final class PlaybackArtworkChecks {
    private static MediaItem item(String id,String source){
        var extras=new Bundle();extras.putString(ListeningState.ALBUM_URI,source);
        return new MediaItem.Builder().setMediaId(id).setUri("content://test/"+id)
            .setMediaMetadata(new MediaMetadata.Builder().setTitle(id).setExtras(extras).build()).build();
    }
    static void run(Instrumentation test)throws Exception{
        var player=new AtomicReference<ExoPlayer>();var artwork=new AtomicReference<PlaybackArtwork>();
        var firstStarted=new CountDownLatch(1);var releaseFirst=new CountDownLatch(1);
        byte[] cover={1,2,3};
        try{
            test.runOnMainSync(()->{
                var p=new ExoPlayer.Builder(test.getTargetContext()).build();player.set(p);
                p.setMediaItems(java.util.List.of(item("a","first"),item("b","second"),item("c","second"),item("d","missing")));
                artwork.set(new PlaybackArtwork(p,source->{
                    if(source.equals("first")){firstStarted.countDown();while(releaseFirst.getCount()>0){try{releaseFirst.await();}catch(InterruptedException ignored){}}return new byte[]{9};}
                    return source.equals("second")?cover:null;
                }));
            });
            if(!firstStarted.await(5,TimeUnit.SECONDS))throw new AssertionError("Async loader not started");
            test.runOnMainSync(()->player.get().seekTo(1,2345));releaseFirst.countDown();
            await(test,player,cover);
            test.runOnMainSync(()->{
                var p=player.get();if(!p.getCurrentMediaItem().mediaId.equals("b")||p.getCurrentPosition()!=2345||p.getPlayWhenReady())throw new AssertionError("Artwork changed playback");
                p.seekTo(2,1000);
            });
            await(test,player,cover);
            test.runOnMainSync(()->player.get().seekTo(3,0));await(test,player,null);
            test.runOnMainSync(()->{artwork.get().close();artwork.set(new PlaybackArtwork(player.get(),source->cover));});
            await(test,player,cover); // Restored/current item is enriched without a new transition.
        }finally{
            releaseFirst.countDown();test.runOnMainSync(()->{if(artwork.get()!=null)artwork.get().close();if(player.get()!=null)player.get().release();});
        }
    }
    private static void await(Instrumentation test,AtomicReference<ExoPlayer> player,byte[] expected)throws Exception{
        for(int i=0;i<100;i++){
            var actual=new AtomicReference<byte[]>();test.runOnMainSync(()->actual.set(player.get().getCurrentMediaItem().mediaMetadata.artworkData));
            if(Arrays.equals(actual.get(),expected))return;Thread.sleep(50);
        }
        throw new AssertionError("Artwork did not match current album");
    }
}
