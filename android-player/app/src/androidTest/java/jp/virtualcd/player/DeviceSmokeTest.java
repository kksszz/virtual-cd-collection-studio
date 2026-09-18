package jp.virtualcd.player;

import android.app.Instrumentation;
import android.os.*;
import android.net.Uri;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.datasource.DefaultDataSource;
import jp.virtualcd.player.archive.ZipTrackDataSource;
import jp.virtualcd.player.library.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;

/** On-device, read-only smoke checks against the user's already-authorized Music tree.
 * Test playback is muted. No source or persistent library data is changed. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class DeviceSmokeTest extends Instrumentation {
    private boolean stateOnly;
    private boolean caseOnly;
    private boolean loadingOnly;
    @Override public void onCreate(Bundle args){super.onCreate(args);stateOnly="state".equals(args.getString("mode"));caseOnly="case3d".equals(args.getString("mode"));loadingOnly="loading".equals(args.getString("mode"));start();}
    @Override public void onStart(){
        Bundle result=new Bundle();
        try{
            if(loadingOnly){testAlbumLoading(result);finish(-1,result);return;}
            if(caseOnly){CaseDeviceChecks.run(this);result.putString("case3d","PASS Windows export round-trip, schema/hash/path/size rejection, GPU closed/open/rotation/landscape/context recreation");finish(-1,result);return;}
            SoundDeviceChecks.run(getTargetContext());result.putString("sound","PASS settings persistence, PCM bypass/live switch, limiter, EOS, format change");
            testListeningState();result.putString("listeningState","PASS queue/position/shuffle/repeat restore, favorites, newest-first history");
            if(stateOnly){result.putString("stream","PASS listening state tests (isolated test preferences)\n");finish(-1,result);return;}
            var context=getTargetContext();String tree=context.getSharedPreferences("MainActivity",0).getString("tree",null);
            if(tree==null)throw new AssertionError("No user-authorized tree");
            var albums=AlbumLibrary.scan(context,Uri.parse(tree),count->{});
            long dirs=albums.stream().filter(a->a.directory).count();result.putLong("directoryAlbums",dirs);result.putInt("allAlbums",albums.size());
            AlbumLibrary.Album flac=null,mp3=null,zip=null;
            for(var album:albums){
                if(!album.directory){if(zip==null)zip=album;continue;}
                for(var file:AlbumLibrary.children(context,album.uri)){
                    if(file.name.toLowerCase(Locale.ROOT).endsWith(".flac")&&flac==null)flac=file;
                    if(AudioFormats.audio(file.name)&&file.name.toLowerCase(Locale.ROOT).endsWith(".mp3")&&mp3==null)mp3=file;
                }
                if(flac!=null&&mp3!=null&&zip!=null)break;
            }
            if(flac==null||mp3==null||zip==null)throw new AssertionError("Need FLAC, regular MP3 and ZIP samples");
            for(var file:new AlbumLibrary.Album[]{flac,mp3,zip}){
                var tracks=AlbumTracks.load(context,file);if(tracks.tracks.isEmpty())throw new AssertionError("Empty playlist");
                var properties=tracks.tracks.get(0).item().mediaMetadata.extras;
                if(properties==null||properties.getLong(TrackProperties.SIZE)<=0||properties.getString(TrackProperties.FILE)==null||Long.parseLong(properties.getString(TrackProperties.DURATION,"0"))<=0)throw new AssertionError("Real-file properties missing");
                testPlayback(tracks.tracks.get(0).item());result.putString(file==flac?"FLAC":file==mp3?"MP3":"ZIP","PASS DSP decode, position advances, seek");
            }
            var sample=AlbumTracks.load(context,mp3).tracks.get(0).item();testBoundaries(sample,true,Player.REPEAT_MODE_OFF);testBoundaries(sample,false,Player.REPEAT_MODE_OFF);
            testBoundaries(sample,false,Player.REPEAT_MODE_ONE);testBoundaries(sample,false,Player.REPEAT_MODE_ALL);result.putString("boundaries","PASS gapless ON/OFF, last track, repeat one/all");
            result.putString("stream","PASS: folder scan + FLAC/MP3/ZIP decode and seek; muted; source files unchanged\n");finish(-1,result);
        }catch(Throwable e){result.putString("stream","FAIL: "+e+"\n");finish(0,result);}
    }
    private void testAlbumLoading(Bundle output)throws Exception{
        var context=getTargetContext();String tree=context.getSharedPreferences("MainActivity",0).getString("tree",null);
        if(tree==null)throw new AssertionError("No authorized library");
        var albums=AlbumLibrary.scan(context,Uri.parse(tree),count->{});
        AlbumLibrary.Album dir=null,zip=null;
        for(var album:albums){if(album.directory&&dir==null)dir=album;if(!album.directory&&zip==null)zip=album;}
        if(dir==null||zip==null)throw new AssertionError("Need directory and ZIP albums");
        for(var selected:new AlbumLibrary.Album[]{dir,zip}){
            var source=AlbumLibrary.describe(context,selected.uri);int[] state={0,0};long[] previewAt={0};long start=SystemClock.elapsedRealtime();
            AlbumTracks.Progress progress=new AlbumTracks.Progress(){
                public void preview(AlbumTracks preview){state[0]=preview.tracks.size();previewAt[0]=SystemClock.elapsedRealtime()-start;if(state[0]==0)throw new AssertionError("Empty preview");}
                public void update(int count,int total){if(total!=state[0]||count<=state[1])throw new AssertionError("Invalid progress");state[1]=count;}
            };
            var cold=AlbumTracks.load(context,source,true,progress);long coldMs=SystemClock.elapsedRealtime()-start;
            if(state[0]!=cold.tracks.size()||state[1]!=cold.tracks.size())throw new AssertionError("Preview/progress mismatch");
            long warmStart=SystemClock.elapsedRealtime();var warm=AlbumTracks.load(context,source,false,null);long warmMs=SystemClock.elapsedRealtime()-warmStart;
            if(cold!=warm)throw new AssertionError("Cache missed unchanged source");
            var forced=AlbumTracks.load(context,source,true,null);if(forced==warm)throw new AssertionError("Refresh did not bypass cache");
            var changed=new AlbumLibrary.Album(source.uri,source.name,source.size,source.modified+1,source.directory);
            if(AlbumTracks.load(context,changed,false,null)==forced)throw new AssertionError("Changed signature reused cache");
            Thread.currentThread().interrupt();boolean cancelled=false;
            try{AlbumTracks.load(context,source,true,null);}catch(java.io.InterruptedIOException expected){cancelled=true;}finally{Thread.interrupted();}
            if(!cancelled)throw new AssertionError("Cancelled load continued");
            output.putString(source.directory?"directoryLoading":"zipLoading","PASS preview="+previewAt[0]+"ms cold="+coldMs+"ms warm="+warmMs+"ms tracks="+cold.tracks.size()+"; refresh, invalidation, cancellation");
        }
    }
    private void testListeningState()throws Exception{
        var context=getTargetContext();String namespace="listening-smoke-"+System.nanoTime();
        var failure=new AtomicReference<Throwable>();
        try{runOnMainSync(()->{
            ExoPlayer a=null,b=null;
            try{
                for(String icon:new String[]{"current","settings","back","history","star","refresh","play","pause","previous","next","stop","shuffle","repeat","repeat-one"}){
                    var button=new android.widget.Button(context);PlayerStyle.button(button);int height=PlayerStyle.dp(context,44);button.setLayoutParams(new android.widget.LinearLayout.LayoutParams(144,height));ControlIcon.button(button,icon,icon);
                    button.measure(android.view.View.MeasureSpec.makeMeasureSpec(144,android.view.View.MeasureSpec.EXACTLY),android.view.View.MeasureSpec.makeMeasureSpec(height,android.view.View.MeasureSpec.EXACTLY));button.layout(0,0,144,height);
                    var bitmap=android.graphics.Bitmap.createBitmap(144,height,android.graphics.Bitmap.Config.ARGB_8888);button.draw(new android.graphics.Canvas(bitmap));int lines=0,minY=height,maxY=0,minX=144,maxX=0;
                    for(int y=0;y<height;y++)for(int x=0;x<144;x++){int color=bitmap.getPixel(x,y);if(android.graphics.Color.red(color)>150&&android.graphics.Color.green(color)>150){lines++;minY=Math.min(minY,y);maxY=Math.max(maxY,y);minX=Math.min(minX,x);maxX=Math.max(maxX,x);}}
                    bitmap.recycle();if(lines<20)throw new AssertionError("Invisible icon: "+icon);
                    if(Math.abs((minY+maxY)/2.0-height/2.0)>5||Math.abs((minX+maxX)/2.0-72)>8)throw new AssertionError("Uncentered icon: "+icon);
                }
                var source=new Bundle();source.putString(ListeningState.ALBUM_URI,"content://test/album");
                MediaItem first=new MediaItem.Builder().setUri("content://test/one.flac").setMediaId("content://test/one.flac")
                    .setMediaMetadata(new MediaMetadata.Builder().setTitle("曲名1").setExtras(source).build()).build();
                MediaItem second=new MediaItem.Builder().setUri("content://test/two.mp3").setMediaId("content://test/two.mp3")
                    .setMediaMetadata(new MediaMetadata.Builder().setTitle("曲名2").setExtras(source).build()).build();
                var store=new ListeningState(context,namespace);a=new ExoPlayer.Builder(context).build();
                a.setMediaItems(Arrays.asList(first,second),1,65000);a.setShuffleModeEnabled(true);a.setRepeatMode(Player.REPEAT_MODE_ALL);store.save(a);
                b=new ExoPlayer.Builder(context).build();new ListeningState(context,namespace).restore(b);
                if(b.getMediaItemCount()!=2||b.getCurrentMediaItemIndex()!=1||b.getCurrentPosition()!=65000||b.getPlayWhenReady()||!b.getShuffleModeEnabled()||b.getRepeatMode()!=Player.REPEAT_MODE_ALL)throw new AssertionError("Restore mismatch");
                if(!"content://test/album".equals(b.getCurrentMediaItem().mediaMetadata.extras.getString(ListeningState.ALBUM_URI)))throw new AssertionError("Lost album identity");
                if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=1)throw new AssertionError("Restored focus mismatch");
                if(MainActivity.playingIndex(Arrays.asList(second,first),b.getCurrentMediaItem())!=0)throw new AssertionError("Focus must match identity, not queue index");
                b.seekToPreviousMediaItem();if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=0)throw new AssertionError("Previous focus mismatch");
                b.seekToNextMediaItem();if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=1)throw new AssertionError("Next focus mismatch");
                if(MainActivity.playingIndex(Arrays.asList(first),second)!=-1)throw new AssertionError("Other album must not highlight");
                if(MainActivity.playingAlbum(null)!=null)throw new AssertionError("Empty now-playing destination");
                if(!Uri.parse("content://test/album").equals(MainActivity.playingAlbum(second)))throw new AssertionError("Now-playing album identity");
                var zipUri=ZipTrackDataSource.trackUri(Uri.parse("content://test/music.zip.mp3"),"02 Song.mp3");
                if(!Uri.parse("content://test/music.zip.mp3").equals(MainActivity.playingAlbum(new MediaItem.Builder().setMediaId(zipUri.toString()).build())))throw new AssertionError("Legacy ZIP destination");
                if(!Uri.parse("content://test/single.flac").equals(MainActivity.playingAlbum(new MediaItem.Builder().setUri("content://test/single.flac").build())))throw new AssertionError("Single-file destination");
                if(MainActivity.playingAlbum(new MediaItem.Builder().setMediaId("zipmp3:invalid").build())!=null)throw new AssertionError("Malformed destination");
                if(!"02".equals(TrackAdapter.number(second,1)))throw new AssertionError("Fallback track number");
                MediaItem numbered=first.buildUpon().setMediaMetadata(first.mediaMetadata.buildUpon().setTrackNumber(7).setDiscNumber(2).build()).build();
                if(!"2-07".equals(TrackAdapter.number(numbered,0))||ListeningState.decode(ListeningState.encode(numbered)).mediaMetadata.trackNumber!=7)throw new AssertionError("Tagged track number");
                var clicked=new AtomicReference<MediaItem>();var details=new AtomicReference<MediaItem>();var adapter=new TrackAdapter(context,Arrays.asList(numbered),store,clicked::set,details::set);
                android.view.ViewGroup row=(android.view.ViewGroup)adapter.getView(0,null,new android.widget.ListView(context));
                ((android.widget.Button)row.getChildAt(2)).performClick();if(clicked.get()!=numbered)throw new AssertionError("Star click target");
                clicked.set(null);row.getChildAt(3).performClick();if(details.get()!=numbered||clicked.get()!=null)throw new AssertionError("Properties must not toggle favorite");
                var properties=new Bundle();properties.putString(TrackProperties.FILE,"song.flac");properties.putString(TrackProperties.DURATION,"254000");properties.putString(TrackProperties.BITRATE,"900000");properties.putString(TrackProperties.RATE,"44100");properties.putLong(TrackProperties.SIZE,1048576);
                var propertyItem=numbered.buildUpon().setMediaMetadata(numbered.mediaMetadata.buildUpon().setExtras(properties).build()).build();String description=TrackProperties.describe(propertyItem);
                if(!description.contains("4:14")||!description.contains("900 kbps")||!description.contains("44.1 kHz")||!description.contains("song.flac")||!description.contains("1.00 MiB"))throw new AssertionError("Property formatting");
                if(!TrackProperties.describe(first).contains("不明／未設定"))throw new AssertionError("Unknown properties must not be invented");
                store.played(first);store.played(second);store.played(first);
                var history=new ListeningState(context,namespace).entries("history");
                if(history.size()!=2||!first.mediaId.equals(history.get(0).getString("id"))||history.get(0).getInt("plays")!=2)throw new AssertionError("History ordering/deduplication");
                store.toggle("favoriteTracks",ListeningState.encode(second));
                if(!new ListeningState(context,namespace).contains("favoriteTracks",second.mediaId))throw new AssertionError("Favorite not persisted");
                store.toggle("favoriteTracks",ListeningState.encode(second));if(store.contains("favoriteTracks",second.mediaId))throw new AssertionError("Favorite not removed");
                for(int i=0;i<205;i++)store.played(first.buildUpon().setMediaId("content://test/track"+i).build());
                if(store.entries("history").size()!=200)throw new AssertionError("History not bounded");
            }catch(Throwable e){failure.set(e);}finally{if(a!=null)a.release();if(b!=null)b.release();}
        });if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{context.getSharedPreferences(namespace,0).edit().clear().commit();}
    }
    private void testBoundaries(MediaItem item,boolean gapless,@Player.RepeatMode int repeat)throws Exception{
        CountDownLatch done=new CountDownLatch(1);AtomicReference<Throwable> failure=new AtomicReference<>();ExoPlayer[] player={null};Handler main=new Handler(Looper.getMainLooper());
        runOnMainSync(()->{var c=getTargetContext();var config=new jp.virtualcd.player.audio.SoundConfig(true,0,false,false,false,false,0,new int[10]);
            player[0]=new ExoPlayer.Builder(c,new jp.virtualcd.player.audio.SoundRenderers(c,new jp.virtualcd.player.audio.SoundProcessor(config)))
                .setMediaSourceFactory(new DefaultMediaSourceFactory(()->new DefaultDataSource(c,new ZipTrackDataSource(c)))).build();
            ExoPlayer p=player[0];p.setVolume(0);p.setPauseAtEndOfMediaItems(!gapless);new jp.virtualcd.player.audio.BoundaryPlayback(p);p.setRepeatMode(repeat);
            p.addListener(new Player.Listener(){int jumps;
                @Override public void onPlayerError(PlaybackException error){failure.set(error);done.countDown();}
                @Override public void onPositionDiscontinuity(Player.PositionInfo old,Player.PositionInfo current,int reason){
                    if(reason==Player.DISCONTINUITY_REASON_AUTO_TRANSITION||reason==Player.DISCONTINUITY_REASON_SEEK)jumps++;
                    if(repeat!=Player.REPEAT_MODE_OFF&&jumps>=3){if(repeat==Player.REPEAT_MODE_ONE&&p.getCurrentMediaItemIndex()!=0)failure.set(new AssertionError("Repeat one advanced"));
                        p.pause();main.postDelayed(()->{if(p.getPlayWhenReady())failure.set(new AssertionError("Explicit pause resumed"));done.countDown();},150);}
                }
                @Override public void onPlaybackStateChanged(int state){if(state==Player.STATE_ENDED&&repeat==Player.REPEAT_MODE_OFF){
                    if(p.getCurrentMediaItemIndex()!=1)failure.set(new AssertionError("Last track not reached"));done.countDown();}}
            });
            var clipped=item.buildUpon().setClippingConfiguration(new MediaItem.ClippingConfiguration.Builder().setStartPositionMs(1000).setEndPositionMs(1600).build()).build();
            p.setMediaItems(Arrays.asList(clipped,clipped.buildUpon().setMediaId(item.mediaId+"#test-next").build()));p.prepare();p.play();
        });
        try{if(!done.await(12,TimeUnit.SECONDS))throw new AssertionError("Boundary timeout gapless="+gapless+" repeat="+repeat);if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{runOnMainSync(()->{main.removeCallbacksAndMessages(null);player[0].release();});}
    }
    private void testPlayback(MediaItem item)throws Exception{
        CountDownLatch done=new CountDownLatch(1);AtomicReference<Throwable> failure=new AtomicReference<>();ExoPlayer[] player={null};
        Handler main=new Handler(Looper.getMainLooper());
        runOnMainSync(()->{
            var context=getTargetContext();var config=new jp.virtualcd.player.audio.SoundConfig(false,5,true,true,true,true,83,new int[]{1,2,3,0,-1,0,1,2,3,4});
            player[0]=new ExoPlayer.Builder(context,new jp.virtualcd.player.audio.SoundRenderers(context,new jp.virtualcd.player.audio.SoundProcessor(config))).setMediaSourceFactory(new DefaultMediaSourceFactory(()->new DefaultDataSource(context,new ZipTrackDataSource(context)))).build();
            player[0].setVolume(0);player[0].addListener(new Player.Listener(){boolean started;
                @Override public void onPlayerError(PlaybackException error){failure.set(error);done.countDown();}
                @Override public void onPlaybackStateChanged(int state){if(state==Player.STATE_READY&&!started){started=true;
                    main.postDelayed(()->{try{
                        if(player[0].getCurrentPosition()<=0)throw new AssertionError("Playback did not advance");
                        if(!player[0].isCurrentMediaItemSeekable()||player[0].getDuration()<=0)throw new AssertionError("Not seekable");
                        if(Math.abs(player[0].getPlaybackParameters().speed-1.25f)>.001)throw new AssertionError("Initial speed");
                        player[0].setPlaybackParameters(new PlaybackParameters(.75f,.5f));
                        long target=player[0].getDuration()/2;player[0].seekTo(target);
                        main.postDelayed(()->{try{if(player[0].getPlaybackState()!=Player.STATE_READY||Math.abs(player[0].getCurrentPosition()-target)>6000)throw new AssertionError("Seek failed");
                            if(!player[0].getPlaybackParameters().equals(new PlaybackParameters(.75f,.5f)))throw new AssertionError("Live speed/pitch lost after seek");}
                            catch(Throwable e){failure.set(e);}finally{done.countDown();}},3000);
                    }catch(Throwable e){failure.set(e);done.countDown();}},1500);
                }}
            });player[0].setPlaybackParameters(new PlaybackParameters(1.25f,(float)Math.pow(2,2/12.0)));player[0].setMediaItem(item);player[0].prepare();player[0].play();
        });
        try{if(!done.await(25,TimeUnit.SECONDS))throw new AssertionError("Playback timed out");if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{runOnMainSync(()->{main.removeCallbacksAndMessages(null);player[0].release();});}
    }
}
