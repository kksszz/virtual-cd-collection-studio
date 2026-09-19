package jp.virtualcd.player.library;

import android.content.Context;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import androidx.media3.common.*;
import jp.virtualcd.player.archive.*;
import java.io.*;
import java.util.*;

/** Common metadata/playlist path for ZIP entries, directory albums and individual audio files. */
public final class AlbumTracks {
    // Bounded, process-local cache: no audio bytes or artwork, and no stale disk cache.
    private static final android.util.LruCache<String,AlbumTracks> cache=new android.util.LruCache<>(1000){
        @Override protected int sizeOf(String key,AlbumTracks value){return value.tracks.size();}
    };
    public interface Progress {
        void preview(AlbumTracks tracks);
        void update(int complete,int total);
    }
    public static final class Track {
        public Uri uri;public String file,title,artist,album;public int disc,number;
        public final android.os.Bundle properties=new android.os.Bundle();
        public MediaItem item(){return new MediaItem.Builder().setUri(uri).setMediaId(uri.toString()+(properties.containsKey(CueTracks.START)?"#cue-track="+number:""))
            .setClippingConfiguration(new MediaItem.ClippingConfiguration.Builder().setStartPositionMs(Long.parseLong(properties.getString(CueTracks.START,"0"))).setEndPositionMs(Long.parseLong(properties.getString(CueTracks.END,Long.toString(C.TIME_END_OF_SOURCE)))).build()).setMimeType(AudioFormats.mime(file))
            .setMediaMetadata(new MediaMetadata.Builder().setTitle(title).setArtist(artist).setAlbumTitle(album)
                .setTrackNumber(number>0?number:null).setDiscNumber(disc>0?disc:null).setExtras(new android.os.Bundle(properties)).build()).build();}
    }
    public final List<Track> tracks=new ArrayList<>();
    public String title,artist="アーティスト情報なし";
    /** Read only enough tracks to obtain an album-list artist; never runs on the UI thread. */
    public static String readArtist(Context context,AlbumLibrary.Album album)throws IOException{
        var result=new AlbumTracks();
        if(album.directory){
            var files=AlbumLibrary.children(context,album.uri);
            var cue=CueTracks.read(context,album,files);if(cue!=null)return cue.artist;
            files.sort(Comparator.comparing(a->AudioFormats.natural(a.name)));
            for(var file:files)if(!file.directory&&AudioFormats.audio(file.name)){
                result.readFile(context,file);
                if(!result.artist.equals("アーティスト情報なし"))break;
            }
        }else if(AudioFormats.archive(album.name)){
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");
            if(fd==null)throw new IOException("音源を開けません");
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){
                for(var entry:StoredZipIndex.read(input.getChannel())){
                    result.add(input,entry.name,ZipTrackDataSource.trackUri(album.uri,entry.name),entry.offset,entry.length);
                    if(!result.artist.equals("アーティスト情報なし"))break;
                }
            }
        }else if(AudioFormats.audio(album.name))result.readFile(context,album);
        return result.artist;
    }
    public static AlbumTracks load(Context context,AlbumLibrary.Album album)throws IOException{
        return load(context,album,false,null);
    }
    public static AlbumTracks load(Context context,AlbumLibrary.Album album,boolean force,Progress progress)throws IOException{
        checkInterrupted();
        var result=new AlbumTracks();result.title=album.title();
        List<AlbumLibrary.Album> files=new ArrayList<>();
        if(album.directory){
            var children=AlbumLibrary.children(context,album.uri);
            if(children.stream().anyMatch(f->f.name.toLowerCase(Locale.ROOT).endsWith(".cue"))){
                String cueKey="cue-v1|"+album.key()+children.stream().map(AlbumLibrary.Album::key).sorted().collect(java.util.stream.Collectors.joining("\n"));
                var saved=force?null:AlbumTagCache.read(context,album.uri);if(saved!=null&&cueKey.equals(saved.signature))return saved.album;
                var cue=CueTracks.read(context,album,children);AlbumTagCache.write(context,album.uri,cueKey,cue);if(progress!=null)progress.preview(cue);return cue;
            }
            for(var file:children)if(!file.directory&&AudioFormats.audio(file.name))files.add(file);
            files.sort(Comparator.comparing(a->AudioFormats.natural(a.name)));
        }
        StringBuilder signature=new StringBuilder(album.key());
        for(var file:files)signature.append('\n').append(file.key());
        String key=signature.toString();
        if(force)cache.remove(key);
        AlbumTracks cached=force?null:cache.get(key);
        if(cached!=null)return cached;
        if(!force){var saved=AlbumTagCache.read(context,album.uri);if(saved!=null&&key.equals(saved.signature)){cache.put(key,saved.album);return saved.album;}}
        var preview=new AlbumTracks();preview.title=album.title();
        if(album.directory){
            for(var file:files)preview.basic(file.name,file.uri);
            if(progress!=null)progress.preview(preview);
            for(var file:files){checkInterrupted();result.readFile(context,file);if(progress!=null)progress.update(result.tracks.size(),files.size());}
        }else if(AudioFormats.archive(album.name)){
            var fd=context.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)throw new IOException("音源を開けません");
            try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){
                var entries=StoredZipIndex.read(input.getChannel());
                for(var entry:entries)preview.basic(entry.name,ZipTrackDataSource.trackUri(album.uri,entry.name));
                if(progress!=null)progress.preview(preview);
                for(var entry:entries){checkInterrupted();result.add(input,entry.name,ZipTrackDataSource.trackUri(album.uri,entry.name),entry.offset,entry.length);if(progress!=null)progress.update(result.tracks.size(),entries.size());}
            }
        }else if(AudioFormats.audio(album.name)){preview.basic(album.name,album.uri);if(progress!=null)progress.preview(preview);result.readFile(context,album);}
        else throw new IOException("未対応の形式です。MP3/FLAC/WAV/M4Aまたは無圧縮ZIPを選択してください");
        if(result.tracks.isEmpty())throw new IOException("対応する音楽ファイルがありません");
        result.tracks.sort(Comparator.comparingInt((Track t)->t.disc>0?t.disc:1)
            .thenComparingInt(t->t.number>0?t.number:Integer.MAX_VALUE).thenComparing(t->AudioFormats.natural(t.file)));
        for(var t:result.tracks)if(t.album==null||t.album.trim().isEmpty())t.album=result.title;
        checkInterrupted();cache.put(key,result);
        AlbumTagCache.write(context,album.uri,key,result);
        return result;
    }
    private void basic(String file,Uri uri){var t=new Track();t.file=file;t.uri=uri;t.title=TrackLabel.title(null,file);t.album=title;tracks.add(t);}
    private static void checkInterrupted()throws InterruptedIOException{if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();}
    private void readFile(Context context,AlbumLibrary.Album file)throws IOException{
        checkInterrupted();
        var fd=context.getContentResolver().openFileDescriptor(file.uri,"r");if(fd==null)throw new IOException("音源を開けません: "+file.name);
        try(var input=new ParcelFileDescriptor.AutoCloseInputStream(fd)){add(input,file.name,file.uri,0,input.getChannel().size());}
    }
    private void add(ParcelFileDescriptor.AutoCloseInputStream input,String name,Uri uri,long offset,long length)throws IOException{
        if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
        var t=new Track();t.file=name;t.uri=uri;String tagTitle=null;
        t.properties.putString(TrackProperties.FILE,name);t.properties.putLong(TrackProperties.SIZE,length);t.properties.putString(TrackProperties.FORMAT,AudioFormats.mime(name));
        try(var metadata=new MediaMetadataRetriever()){
            metadata.setDataSource(input.getFD(),offset,length);
            tagTitle=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE);
            t.artist=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST);
            t.album=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUM);
            t.disc=AudioFormats.number(metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DISC_NUMBER));
            t.number=AudioFormats.number(metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_CD_TRACK_NUMBER));
            t.properties.putString(TrackProperties.DURATION,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION));
            t.properties.putString(TrackProperties.BITRATE,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_BITRATE));
            t.properties.putString(TrackProperties.YEAR,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_YEAR));
            t.properties.putString(TrackProperties.GENRE,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_GENRE));
            t.properties.putString(TrackProperties.COMPOSER,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_COMPOSER));
            String format=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_MIMETYPE);if(format!=null)t.properties.putString(TrackProperties.FORMAT,format);
            if(android.os.Build.VERSION.SDK_INT>=31)t.properties.putString(TrackProperties.RATE,metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_SAMPLERATE));
            String albumArtist=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUMARTIST);
            if(albumArtist==null||albumArtist.trim().isEmpty())albumArtist=t.artist;
            if(artist.equals("アーティスト情報なし")&&albumArtist!=null&&!albumArtist.trim().isEmpty())artist=albumArtist;
            if(tracks.isEmpty()&&t.album!=null&&!t.album.trim().isEmpty())title=t.album;
        }catch(Exception ignored){ /* Metadata failure must not hide a playable file. */ }
        String legacy=name.toLowerCase(Locale.ROOT).endsWith(".mp3")?LegacyId3Title.read(input.getChannel(),offset,length):null;
        t.title=TrackLabel.title(legacy!=null?legacy:tagTitle,name);tracks.add(t);
    }
}
