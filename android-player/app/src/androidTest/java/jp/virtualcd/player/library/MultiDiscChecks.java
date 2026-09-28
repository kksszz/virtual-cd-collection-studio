package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.provider.DocumentsContract;
import java.util.*;

public final class MultiDiscChecks {
    public static void run(Context c)throws Exception {
        String authority="jp.virtualcd.player.test.deletion";
        c.getContentResolver().call(Uri.parse("content://"+authority),"test-seed","multiDisc",null);
        var tree=DocumentsContract.buildTreeDocumentUri(authority,"root");
        var album=AlbumLibrary.describe(c,DocumentsContract.buildDocumentUriUsingTree(tree,"album"));
        var files=AlbumTracks.directoryAudioFiles(c,album,AlbumLibrary.children(c,album.uri));
        if(files.size()!=3)throw new AssertionError("Nested tracks, hidden sync and outside-album exclusion");
        var info=AlbumIndicators.read(c,album);if(info.count!=3||!info.formats.equals("FLAC"))throw new AssertionError("Nested track count/format badges");
        if(!files.get(0).path.equals("Disc1/01.flac")||!files.get(1).path.equals("Disc2/01.flac")||!files.get(2).path.equals("Disc10/01.flac"))throw new AssertionError("Natural disc order");
        for(String path:new String[]{"Disc1/01.flac","CD 02/01.flac","disc-10/01.flac","Album/Disk_3/Tracks/01.flac"})
            if(AlbumTracks.folderDisc(path)==0)throw new AssertionError("Disc fallback: "+path);
        for(String path:new String[]{"01.flac","Images/01.flac","Disco/01.flac","Disc0/01.flac"})
            if(AlbumTracks.folderDisc(path)!=0)throw new AssertionError("False disc fallback: "+path);
        Thread.currentThread().interrupt();
        try { AlbumTracks.directoryAudioFiles(c,album,List.of());throw new AssertionError("Cancellation ignored"); }
        catch(java.io.InterruptedIOException expected){}finally {Thread.interrupted();}
    }
    /** Finish the user's previously transferred album through the normal sync reader. */
    public static String runTransferred(Context c)throws Exception {
        String granted=c.getSharedPreferences("MainActivity",0).getString("tree",null);
        if(granted==null)throw new AssertionError("Music tree not selected");
        var tree=Uri.parse(granted);var root=DocumentsContract.buildDocumentUriUsingTree(tree,DocumentsContract.getTreeDocumentId(tree));
        var synced=MobileSync.read(c,root);int checked=0,total=0;
        for(var album:synced){
            if(!album.directory)continue;
            var children=AlbumLibrary.children(c,album.uri);
            if(children.stream().noneMatch(f->f.directory&&f.name.matches("(?i)(disc|disk|cd)[ _-]*[0-9]+")))continue;
            var tracks=AlbumTracks.load(c,album,true,null);
            int previous=0;var discs=new HashSet<Integer>();
            for(var t:tracks.tracks){if(t.disc<previous)throw new AssertionError("Disc ordering");previous=t.disc;discs.add(t.disc);}
            if(discs.size()<2||tracks.tracks.isEmpty())throw new AssertionError("Missing multi-disc audio");
            if(AlbumTracks.load(c,album).tracks.size()!=tracks.tracks.size()||AlbumIndicators.read(c,album).count!=tracks.tracks.size())throw new AssertionError("Cache or track count badge");
            if(!AlbumIndicators.has3d(c,album))throw new AssertionError("Missing case binding");
            if(AlbumTracks.readArtist(c,album).equals(jp.virtualcd.player.LanguageStrings.text("アーティスト情報なし","No artist information")))throw new AssertionError("Missing nested artist");
            checked++;total+=tracks.tracks.size();
        }
        if(checked==0)throw new AssertionError("No transferred multi-disc album found");
        return "PASS "+checked+" multi-disc album, "+total+" tracks, ordering, artist, favorites registration and 3D binding; "+synced.size()+" synced albums";
    }
}
