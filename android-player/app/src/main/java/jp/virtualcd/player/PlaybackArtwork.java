package jp.virtualcd.player;

import android.content.Context;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Handler;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import androidx.media3.common.Player;
import jp.virtualcd.player.library.AlbumLibrary;
import jp.virtualcd.player.library.ArtworkLoader;
import jp.virtualcd.player.library.ListeningState;
import java.io.ByteArrayOutputStream;
import java.util.Arrays;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

/** Loads only the current album off the playback thread. Images are not persisted in the queue. */
final class PlaybackArtwork implements AutoCloseable {
    interface Loader { byte[] load(String source) throws Exception; }
    private final Player player;
    private final Handler handler;
    private final Loader loader;
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private Future<?> job;
    private String requested="",cachedSource="";
    private byte[] cached;
    private int generation;
    private boolean closed;
    private final Player.Listener listener=new Player.Listener(){
        @Override public void onEvents(Player p,Player.Events events){
            if(events.contains(Player.EVENT_MEDIA_ITEM_TRANSITION)||events.contains(Player.EVENT_TIMELINE_CHANGED))refresh();
        }
    };
    PlaybackArtwork(Context context,Player player){
        this(player,source->{
            var album=AlbumLibrary.describe(context,Uri.parse(source));
            String crop=context.getSharedPreferences("thumbnail-layout",Context.MODE_PRIVATE).getString(source,"auto");
            Bitmap bitmap=ArtworkLoader.load(context,album,crop);
            if(bitmap==null)return null;
            try(var output=new ByteArrayOutputStream()){
                return bitmap.compress(Bitmap.CompressFormat.JPEG,85,output)?output.toByteArray():null;
            }finally{bitmap.recycle();}
        });
    }
    PlaybackArtwork(Player player,Loader loader){
        this.player=player;this.loader=loader;handler=new Handler(player.getApplicationLooper());
        player.addListener(listener);refresh();
    }
    private static String source(MediaItem item){
        return item==null||item.mediaMetadata.extras==null?"":item.mediaMetadata.extras.getString(ListeningState.ALBUM_URI,"");
    }
    private void refresh(){
        if(closed)return;
        String source=source(player.getCurrentMediaItem());
        if(source.equals(requested)){
            if(!source.isEmpty()&&source.equals(cachedSource))apply(source,cached);
            return;
        }
        requested=source;int token=++generation;
        if(job!=null)job.cancel(true);
        if(source.isEmpty())return;
        if(source.equals(cachedSource)){apply(source,cached);return;}
        job=worker.submit(()->{
            byte[] result=null;
            try{result=loader.load(source);}catch(Exception ignored){/* Artwork must never prevent playback. */}
            byte[] image=result;
            handler.post(()->{
                if(closed||token!=generation||!source.equals(requested))return;
                cachedSource=source;cached=image;apply(source,image);
            });
        });
    }
    private void apply(String source,byte[] image){
        var item=player.getCurrentMediaItem();
        if(item==null||!source.equals(source(item))||Arrays.equals(item.mediaMetadata.artworkData,image))return;
        var metadata=item.mediaMetadata.buildUpon().setArtworkData(image,MediaMetadata.PICTURE_TYPE_FRONT_COVER).build();
        // Metadata-only replacement keeps the existing media source, position and play state.
        player.replaceMediaItem(player.getCurrentMediaItemIndex(),item.buildUpon().setMediaMetadata(metadata).build());
    }
    @Override public void close(){
        closed=true;generation++;player.removeListener(listener);
        if(job!=null)job.cancel(true);worker.shutdownNow();handler.removeCallbacksAndMessages(null);
        cached=null;
    }
}
