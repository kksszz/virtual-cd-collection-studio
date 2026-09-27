package jp.virtualcd.player;

import android.app.Instrumentation;
import android.os.*;
import android.net.Uri;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.datasource.DefaultDataSource;
import jp.virtualcd.player.archive.*;
import jp.virtualcd.player.library.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

/** Muted independent player: does not save queue, settings, or source data. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
final class StartupLatencyChecks {
    static void run(Instrumentation test,Bundle result)throws Exception{
        var c=test.getTargetContext();
        var tree=Uri.parse(c.getSharedPreferences("MainActivity",0).getString("tree",""));
        var matches=new java.util.ArrayList<AlbumLibrary.Album>();
        for(var a:AlbumLibrary.load(c,tree))if(a.name.contains("0083")&&!a.name.contains("MEMORY2")&&!a.name.contains("MEMORY 2"))matches.add(a);
        if(matches.size()!=1)throw new AssertionError("Expected one 0083 Vol1 archive, found "+matches.size());
        var album=matches.get(0);long start=SystemClock.elapsedRealtime();
        var cached=AlbumTagCache.read(c,album.uri);
        result.putLong("tagCacheMs",SystemClock.elapsedRealtime()-start);result.putBoolean("tagCachePresent",cached!=null);
        start=SystemClock.elapsedRealtime();
        var fd=c.getContentResolver().openFileDescriptor(album.uri,"r");
        result.putLong("documentOpenMs",SystemClock.elapsedRealtime()-start);
        java.util.List<StoredZipIndex.Entry> entries;
        try(var in=new android.os.ParcelFileDescriptor.AutoCloseInputStream(fd)){
            start=SystemClock.elapsedRealtime();entries=StoredZipIndex.read(in.getChannel());
            result.putLong("zipIndexMs",SystemClock.elapsedRealtime()-start);
        }
        var item=MediaItem.fromUri(ZipTrackDataSource.trackUri(album.uri,entries.get(0).name));
        ExoPlayer[] player={null};var latch=new AtomicReference<CountDownLatch>();var failure=new AtomicReference<Throwable>();
        var elapsed=new AtomicLong();var requested=new AtomicLong();
        test.runOnMainSync(()->{
            var sound=new jp.virtualcd.player.audio.SoundPreferences(c);
            player[0]=new ExoPlayer.Builder(c,new jp.virtualcd.player.audio.SoundRenderers(c,new jp.virtualcd.player.audio.SoundProcessor(sound.read())))
                .setMediaSourceFactory(new DefaultMediaSourceFactory(()->new DefaultDataSource(c,new ZipTrackDataSource(c)))).build();
            player[0].setVolume(0);player[0].setPlaybackParameters(sound.playbackParameters());
            player[0].addListener(new Player.Listener(){
                @Override public void onIsPlayingChanged(boolean playing){if(playing&&latch.get()!=null){elapsed.set(SystemClock.elapsedRealtime()-requested.get());latch.get().countDown();}}
                @Override public void onPlayerError(PlaybackException e){failure.set(e);if(latch.get()!=null)latch.get().countDown();}
            });
        });
        try{
            for(int pass=0;pass<3;pass++){
                if(pass==1){test.runOnMainSync(()->player[0].pause());Thread.sleep(30000);}
                final int index=pass;latch.set(new CountDownLatch(1));
                test.runOnMainSync(()->{requested.set(SystemClock.elapsedRealtime());if(index!=1){player[0].stop();player[0].setMediaItem(item);player[0].prepare();}player[0].play();});
                if(!latch.get().await(45,TimeUnit.SECONDS))throw new AssertionError("Playback timeout pass="+pass);
                if(failure.get()!=null)throw new AssertionError(failure.get());
                result.putLong(new String[]{"freshPlayerStartMs","resumeAfter30sMs","reopenStartMs"}[pass],elapsed.get());
                Thread.sleep(1000);
            }
        }finally{test.runOnMainSync(()->player[0].release());}
        result.putString("scope","Muted independent player; 30-second pause, not Android deep sleep; source/settings unchanged");
    }
}
