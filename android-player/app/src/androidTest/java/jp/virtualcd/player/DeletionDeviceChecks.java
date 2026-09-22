package jp.virtualcd.player;

import android.app.Instrumentation;
import android.content.*;
import android.net.Uri;
import android.provider.DocumentsContract;
import jp.virtualcd.player.library.*;
import java.io.*;
import java.util.*;

final class DeletionDeviceChecks {
    static void run(Instrumentation instrumentation)throws Exception{
        String namespace="deletion-test-"+System.nanoTime();
        var c=new ContextWrapper(instrumentation.getTargetContext()){
            @Override public android.content.SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(namespace+name,mode);}
            @Override public File getFilesDir(){var dir=new File(super.getFilesDir(),namespace);dir.mkdirs();return dir;}
        };
        var base=Uri.parse("content://"+DeletionTestProvider.AUTHORITY);
        var tree=DocumentsContract.buildTreeDocumentUri(DeletionTestProvider.AUTHORITY,"root");
        java.util.function.Function<String,Uri> uri=id->DocumentsContract.buildDocumentUriUsingTree(tree,id);
        var resolver=c.getContentResolver();
        resolver.call(base,"test-seed","plain",null);
        var album=AlbumLibrary.describe(c,uri.apply("album"));
        var other=AlbumLibrary.describe(c,uri.apply("other"));
        try{AlbumDeletion.prepare(c,tree,AlbumLibrary.describe(c,uri.apply("root")),List.of(album,other));throw new AssertionError("root deletion allowed");}catch(IOException expected){}
        var plan=AlbumDeletion.prepare(c,tree,album,List.of(album,other));
        if(plan.files.size()!=2)throw new AssertionError("Inventory");
        resolver.call(base,"test-change",null,null);
        try{AlbumDeletion.delete(c,plan);throw new AssertionError("changed file deletion allowed");}catch(IOException expected){}
        if(AlbumLibrary.children(c,album.uri).size()!=2)throw new AssertionError("Preflight deleted files");
        plan=AlbumDeletion.prepare(c,tree,album,List.of(album,other));
        resolver.call(base,"test-add",null,null);
        try{AlbumDeletion.delete(c,plan);throw new AssertionError("unlisted file deletion allowed");}catch(IOException expected){}
        if(AlbumLibrary.children(c,album.uri).size()!=1)throw new AssertionError("New file must survive");
        if(AlbumLibrary.children(c,other.uri).size()!=1)throw new AssertionError("Other album touched");
        var retry=AlbumDeletion.prepare(c,tree,album,List.of(album,other));
        if(retry.files.size()!=1)throw new AssertionError("Retry must display remaining file for new confirmation");
        AlbumDeletion.delete(c,retry);
        if(!AlbumDeletion.removed(c,album.uri))throw new AssertionError("Retry completion");
        // Use a separate preference namespace for the independent successful sync test.
        c.getSharedPreferences("album-deletions-v1",0).edit().clear().commit();
        resolver.call(base,"test-seed","sync",null);
        album=AlbumLibrary.describe(c,uri.apply("track"));
        plan=AlbumDeletion.prepare(c,tree,album,List.of(album,other));
        if(plan.files.size()!=3)throw new AssertionError("Missing related transfer files");
        var cacheFiles=new ArrayList<File>();
        String hash=jp.virtualcd.player.case3d.CasePackage.hash(album.uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));
        for(String path:new String[]{"cases3d/"+hash+".vcd3d","lyrics/"+hash+".json","album-tags-v1/"+hash+".json"}){
            var file=new File(c.getFilesDir(),path);file.getParentFile().mkdirs();
            try(var out=new FileOutputStream(file)){out.write(1);}cacheFiles.add(file);
        }
        var listening=c.getSharedPreferences("listening-v1",0);var edit=listening.edit();
        for(String key:new String[]{"favoriteAlbums","favoriteTracks","history","queue"})edit.putString(key,new org.json.JSONArray()
            .put(new org.json.JSONObject().put("id","target").put("source",album.uri.toString()))
            .put(new org.json.JSONObject().put("id","other").put("source",other.uri.toString())).toString());
        edit.commit();
        AlbumDeletion.delete(c,plan);
        for(var file:cacheFiles)if(file.exists())throw new AssertionError("Private cache remains");
        for(String key:new String[]{"favoriteAlbums","favoriteTracks","history","queue"}){
            var entries=new org.json.JSONArray(listening.getString(key,"[]"));
            if(entries.length()!=1||!"other".equals(entries.getJSONObject(0).getString("id")))throw new AssertionError("Listening state scope: "+key);
        }
        if(!AlbumDeletion.removed(c,album.uri)||!AlbumDeletion.excluded(c,tree,DeletionTestProvider.ID))throw new AssertionError("Tombstones missing");
        var records=new org.json.JSONObject().put(DeletionTestProvider.ID,new org.json.JSONObject()).put("b".repeat(64),new org.json.JSONObject());
        if(!AlbumDeletion.needsTransfer(c,tree,records))throw new AssertionError("Deleted album must bypass unchanged-manifest shortcut");
        var localRecords=new org.json.JSONObject(records.toString());AlbumDeletion.filterRecords(c,tree,localRecords);
        if(localRecords.length()!=1||localRecords.has(DeletionTestProvider.ID))throw new AssertionError("Missing local data must stay hidden until transfer commits");
        if(AlbumLibrary.children(c,uri.apply("sync")).size()!=0||AlbumLibrary.children(c,other.uri).size()!=1)throw new AssertionError("Deletion scope");
        AlbumDeletion.transferred(c,tree,new org.json.JSONObject().put("b".repeat(64),new org.json.JSONObject()),List.of(other.uri));
        if(!AlbumDeletion.excluded(c,tree,DeletionTestProvider.ID))throw new AssertionError("Unrelated transfer cleared deleted state");
        AlbumDeletion.transferred(c,tree,records,List.of(album.uri));
        if(AlbumDeletion.removed(c,album.uri)||AlbumDeletion.excluded(c,tree,DeletionTestProvider.ID))throw new AssertionError("Explicit retransfer");
        if(AlbumDeletion.needsTransfer(c,tree,records))throw new AssertionError("Successful transfer must clear retry state");
    }
}
