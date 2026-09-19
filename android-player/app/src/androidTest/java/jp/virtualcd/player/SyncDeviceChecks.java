package jp.virtualcd.player;
import android.content.*;
import android.net.Uri;
import android.provider.DocumentsContract;
import jp.virtualcd.player.library.*;
import jp.virtualcd.player.case3d.CasePackage;
import org.json.*;
import java.io.*;
import java.util.*;

final class SyncDeviceChecks {
    static void run(Context c,String address)throws Exception{
        for(String bad:new String[]{"http://8.8.8.8/"+"a".repeat(48)+"/","https://192.168.1.1/","http://192.168.1.1/../"}){boolean rejected=false;try{SyncDownload.validateAddress(bad);}catch(Exception ex){rejected=true;}if(!rejected)throw new AssertionError("unsafe endpoint");}
        String granted=c.getSharedPreferences("MainActivity",0).getString("tree",null);if(granted==null)throw new AssertionError("Select Music tree first");Uri tree=Uri.parse(granted);
        if(c.getContentResolver().getPersistedUriPermissions().stream().noneMatch(p->p.getUri().equals(tree)&&p.isWritePermission()))throw new AssertionError("保存先の書き込み許可が必要です。音楽フォルダーを選び直してください。");
        Uri parent=DocumentsContract.buildDocumentUriUsingTree(tree,DocumentsContract.getTreeDocumentId(tree));Uri test=DocumentsContract.createDocument(c.getContentResolver(),parent,DocumentsContract.Document.MIME_TYPE_DIR,"VirtualCD-Sync-Test-"+UUID.randomUUID());if(test==null)throw new AssertionError("Test folder");
        var sync=c.getSharedPreferences("mobile-sync-v1",0);var download=c.getSharedPreferences("sync-download-v1",0);var oldSync=new HashMap<>(sync.getAll());var oldDownload=new HashMap<>(download.getAll());var ownedCases=new ArrayList<File>();
        ownedCases.add(new File(c.getFilesDir(),"sync-commit-"+CasePackage.hash(test.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8))+".json"));
        try{
            SyncDownload.pull(c,test,address,s->{});var albums=MobileSync.read(c,test);if(albums.size()!=1)throw new AssertionError("Automatic album discovery");var album=albums.get(0);
            var caseFile=new File(new File(c.getFilesDir(),"cases3d"),CasePackage.hash(album.uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8))+".vcd3d");ownedCases.add(caseFile);
            if(!AlbumIndicators.has3d(c,album))throw new AssertionError("Automatic 3D binding");try(var in=new FileInputStream(caseFile);var parsed=CasePackage.parse(CasePackage.readBytes(in,CasePackage.MAX_BYTES))){if(parsed.images.isEmpty())throw new AssertionError("GLB images");}
            var tracks=AlbumTracks.load(c,album);if(tracks.tracks.size()!=1)throw new AssertionError("Playable imported audio");
            long stamp=caseFile.lastModified();SyncDownload.pull(c,test,address,s->{throw new AssertionError("Unchanged payload transferred");});if(MobileSync.read(c,test).size()!=1||caseFile.lastModified()!=stamp)throw new AssertionError("Unchanged case rewritten");
            var manifest=AlbumLibrary.children(c,test).stream().filter(a->a.name.equals("vcd-sync.json")).findFirst().orElseThrow();byte[] bytes;try(var in=c.getContentResolver().openInputStream(manifest.uri)){bytes=CasePackage.readBytes(in,4*1024*1024);}
            var corrupt=new JSONObject(new String(bytes,java.nio.charset.StandardCharsets.UTF_8));String id=corrupt.getJSONObject("albums").keys().next();corrupt.getJSONObject("albums").getJSONObject(id).put("glbSha256","0".repeat(64));try(var out=c.getContentResolver().openOutputStream(manifest.uri,"wt")){out.write(corrupt.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            boolean rejected=false;try{MobileSync.read(c,test);}catch(Exception expected){rejected=true;}if(!rejected||caseFile.lastModified()!=stamp)throw new AssertionError("Corrupt transfer replaced working case");
        }finally{
            for(File f:ownedCases)if(f.isFile()&&!f.delete())throw new IOException("test case cleanup failed");
            DocumentsContract.deleteDocument(c.getContentResolver(),test);restore(sync,oldSync);restore(download,oldDownload);
        }
    }
    private static void restore(SharedPreferences prefs,Map<String,?> values){var edit=prefs.edit().clear();for(var e:values.entrySet()){Object value=e.getValue();if(value instanceof String)edit.putString(e.getKey(),(String)value);else if(value instanceof Set)edit.putStringSet(e.getKey(),(Set<String>)value);else if(value instanceof Long)edit.putLong(e.getKey(),(Long)value);else if(value instanceof Boolean)edit.putBoolean(e.getKey(),(Boolean)value);}edit.commit();}
}
