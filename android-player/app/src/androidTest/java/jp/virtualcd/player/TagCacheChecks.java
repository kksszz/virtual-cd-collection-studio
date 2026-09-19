package jp.virtualcd.player;

import android.content.*;
import android.net.Uri;
import java.io.*;
import jp.virtualcd.player.library.*;

final class TagCacheChecks {
    static void run(Context base,boolean write)throws Exception{
        Context isolated=new ContextWrapper(base){@Override public File getFilesDir(){return new File(base.getCacheDir(),"tag-cache-tests");}};
        Uri source=Uri.parse("content://test/album");
        if(write){
            var album=new AlbumTracks();album.title="保存テスト";album.artist="アーティスト";
            var track=new AlbumTracks.Track();track.uri=Uri.parse("content://test/01.flac");track.file="01.flac";track.title="曲名";track.artist=album.artist;track.album=album.title;track.disc=2;track.number=7;
            track.properties.putLong(TrackProperties.SIZE,12345678);track.properties.putString(TrackProperties.DURATION,"123000");track.properties.putString(TrackProperties.FORMAT,"audio/flac");
            album.tracks.add(track);AlbumTagCache.write(isolated,source,"signature-1",album);
        }
        var saved=AlbumTagCache.read(isolated,source);
        if(saved==null||!saved.signature.equals("signature-1")||!saved.album.title.equals("保存テスト")||saved.album.tracks.size()!=1)throw new AssertionError("Snapshot missing after process restart");
        var t=saved.album.tracks.get(0);
        if(!t.title.equals("曲名")||t.disc!=2||t.number!=7||t.properties.getLong(TrackProperties.SIZE)!=12345678||!"123000".equals(t.properties.getString(TrackProperties.DURATION)))throw new AssertionError("Metadata roundtrip failed");
        if(AlbumTagCache.read(isolated,Uri.parse("content://test/other"))!=null)throw new AssertionError("Wrong source reused");
        if(!write){
            File dir=new File(isolated.getFilesDir(),"album-tags-v1");File[] files=dir.listFiles();if(files==null||files.length!=1)throw new AssertionError("Unexpected isolated cache");
            try(var out=new FileOutputStream(files[0])){out.write("corrupt".getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            if(AlbumTagCache.read(isolated,source)!=null)throw new AssertionError("Corrupt cache accepted");
            if(!files[0].delete())throw new AssertionError("Test cleanup failed");
        }
    }
}
