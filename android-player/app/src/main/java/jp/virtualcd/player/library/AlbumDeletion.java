package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.provider.DocumentsContract;
import jp.virtualcd.player.LanguageStrings;
import jp.virtualcd.player.case3d.CasePackage;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Explicit, album-scoped SAF deletion. Never sends a deletion request to the PC. */
public final class AlbumDeletion {
    private static android.content.SharedPreferences prefs(Context c){return c.getSharedPreferences("album-deletions-v1",0);}
    private static String rootKey(Uri tree){
        String id;try{id=DocumentsContract.getDocumentId(tree);}catch(IllegalArgumentException ex){id=DocumentsContract.getTreeDocumentId(tree);}
        return DocumentsContract.buildDocumentUriUsingTree(tree,id).toString();
    }
    public static boolean excluded(Context c,Uri root,String id){return prefs(c).getBoolean("sync|"+rootKey(root)+"|"+id,false);}
    public static boolean removed(Context c,Uri album){return prefs(c).getBoolean("uri|"+album,false);}
    public static List<AlbumLibrary.Album> visible(Context c,List<AlbumLibrary.Album> albums){
        var result=new ArrayList<AlbumLibrary.Album>();for(var a:albums)if(!removed(c,a.uri))result.add(a);return result;
    }
    public static void filterRecords(Context c,Uri root,JSONObject records){
        var ids=new ArrayList<String>();records.keys().forEachRemaining(ids::add);
        for(String id:ids)if(excluded(c,root,id))records.remove(id);
    }
    /** Tombstones hide missing local data, but never veto a new PC transfer. */
    public static boolean needsTransfer(Context c,Uri root,JSONObject records)throws Exception{
        boolean needed=false;var ids=records.keys();
        while(ids.hasNext())if(excluded(c,root,ids.next()))needed=true;
        if(needed)for(var entry:prefs(c).getAll().entrySet())if(entry.getKey().startsWith("pending|")){
            var pending=new JSONObject((String)entry.getValue());var keys=pending.getJSONArray("sync");
            for(int i=0;i<keys.length();i++){var recordIds=records.keys();while(recordIds.hasNext())
                if(keys.getString(i).equals("sync|"+rootKey(root)+"|"+recordIds.next()))
                    throw error("未完了の削除を再試行してから転送してください","Retry the incomplete deletion before transferring");}
        }
        return needed;
    }
    /** Called only after all incoming payloads and the manifest have been saved. */
    public static void transferred(Context c,Uri root,JSONObject records,List<Uri> audioUris)throws Exception{
        if(!needsTransfer(c,root,records))return;
        // Invalidate ingestion caches first, so a crash cannot leave a stale index.
        var mobile=c.getSharedPreferences("mobile-sync-v1",0);var cache=mobile.edit();
        for(String key:mobile.getAll().keySet())if(key.endsWith("|digest"))cache.remove(key);
        if(!cache.commit())throw error("同期情報を更新できません","Unable to update sync state");
        var edit=prefs(c).edit();var ids=records.keys();
        while(ids.hasNext())edit.remove("sync|"+rootKey(root)+"|"+ids.next());
        for(var uri:audioUris)edit.remove("uri|"+uri);
        if(!edit.commit())throw error("再転送の完了状態を保存できません","Unable to save retransfer state");
    }
    public static final class Plan {
        public final Uri tree,album;
        public final String title;
        public final List<AlbumLibrary.Album> files=new ArrayList<>(),directories=new ArrayList<>();
        final Set<String> syncKeys=new HashSet<>();
        Plan(Uri tree,AlbumLibrary.Album album){this.tree=tree;this.album=album.uri;title=album.title();}
        public long bytes(){long total=0;for(var f:files)total+=Math.max(0,f.size);return total;}
    }
    private static IOException error(String ja,String en){return new IOException(LanguageStrings.text(ja,en));}
    private static AlbumLibrary.Album relative(Context c,Uri root,String path)throws Exception{
        MobileSync.safePath(path);Uri parent=root;AlbumLibrary.Album found=null;
        for(String part:path.split("/")){
            found=null;for(var child:AlbumLibrary.children(c,parent))if(part.equals(child.name)){
                if(found!=null)throw error("同名のファイルがあるため削除できません","Duplicate names prevent safe deletion");found=child;
            }
            if(found==null)return null;parent=found.uri;
        }return found;
    }
    private static boolean same(Uri a,Uri b){return Objects.equals(a.getAuthority(),b.getAuthority())
        && DocumentsContract.getDocumentId(a).equals(DocumentsContract.getDocumentId(b));}
    private static void within(Context c,Uri tree,Uri target)throws Exception{
        Uri root=Uri.parse(rootKey(tree));
        if(!Objects.equals(root.getAuthority(),target.getAuthority())||same(root,target)
            ||!DocumentsContract.isChildDocument(c.getContentResolver(),root,target))
            throw error("音楽フォルダー本体や範囲外のファイルは削除できません","Cannot delete the music root or files outside it");
    }
    private static boolean exists(Context c,Uri uri)throws Exception{
        Uri root=DocumentsContract.buildDocumentUriUsingTree(uri,DocumentsContract.getTreeDocumentId(uri));
        try{
            if(!same(root,uri)&&!DocumentsContract.isChildDocument(c.getContentResolver(),root,uri))return false;
            try(var cursor=c.getContentResolver().query(uri,new String[]{DocumentsContract.Document.COLUMN_DOCUMENT_ID},null,null,null)){
                if(cursor==null)throw error("ファイルの存在を確認できません","Unable to verify document existence");
                return cursor.moveToFirst();
            }
        }catch(SecurityException ex){
            // A deleted tree document also fails isChildDocument on some providers.
            // Do not equate permission failure with absence: prove absence by reading
            // the granted tree. Any inaccessible directory aborts this check safely.
            var pending=new ArrayDeque<Uri>();var seen=new HashSet<String>();pending.add(root);
            while(!pending.isEmpty()){
                Uri directory=pending.removeFirst();
                if(!seen.add(DocumentsContract.getDocumentId(directory)))continue;
                if(seen.size()>100000)throw error("確認対象が多すぎます","Too many directories to verify");
                for(var child:AlbumLibrary.children(c,directory)){
                    if(same(child.uri,uri))throw ex;
                    if(child.directory)pending.add(child.uri);
                }
            }
            return false;
        }
    }
    private static void writable(Context c,Uri uri)throws Exception{
        try(var cursor=c.getContentResolver().query(uri,new String[]{DocumentsContract.Document.COLUMN_FLAGS},null,null,null)){
            if(cursor==null||!cursor.moveToFirst()||(cursor.getInt(0)&DocumentsContract.Document.FLAG_SUPPORTS_DELETE)==0)
                throw error("削除権限がありません。設定から音楽フォルダーを選び直してください","No delete permission. Select the music folder again in Settings");
        }
    }
    public static Plan prepare(Context c,Uri tree,AlbumLibrary.Album album,List<AlbumLibrary.Album> library)throws Exception{
        if(tree==null)throw error("音楽フォルダーを選択してください","Choose a music folder");
        var plan=new Plan(tree,album);
        String pending=prefs(c).getString("pending|"+album.uri,null);
        if(pending!=null){
            var json=new JSONObject(pending);
            if(!rootKey(tree).equals(json.getString("tree")))throw error("削除先のフォルダーが変わりました","Deletion folder changed");
            loadItems(json.getJSONArray("files"),plan.files);loadItems(json.getJSONArray("directories"),plan.directories);
            var keys=json.getJSONArray("sync");for(int i=0;i<keys.length();i++)plan.syncKeys.add(keys.getString(i));
            var remaining=new ArrayList<AlbumLibrary.Album>();remaining.addAll(plan.directories);remaining.addAll(plan.files);
            plan.files.clear();plan.directories.clear();
            var current=new ArrayList<AlbumLibrary.Album>();
            for(var item:remaining)if(exists(c,item.uri))current.add(AlbumLibrary.describe(c,item.uri));
            collect(c,tree,plan,library,current);return plan;
        }
        within(c,tree,album.uri);
        var targets=new LinkedHashMap<String,AlbumLibrary.Album>();targets.put(album.uri.toString(),album);
        var sync=c.getSharedPreferences("mobile-sync-v1",0);
        var roots=new HashSet<>(sync.getStringSet("roots",Collections.emptySet()));roots.add(rootKey(tree));
        for(String value:roots){
            Uri root=Uri.parse(value);
            if(!Objects.equals(tree.getAuthority(),root.getAuthority())||!DocumentsContract.getTreeDocumentId(tree).equals(DocumentsContract.getTreeDocumentId(root)))continue;
            var manifest=relative(c,root,"vcd-sync.json");if(manifest==null)continue;
            JSONObject records;
            try(var in=c.getContentResolver().openInputStream(manifest.uri)){
                if(in==null)throw error("同期情報を読めません","Unable to read sync information");
                var json=new JSONObject(new String(CasePackage.readBytes(in,4*1024*1024),StandardCharsets.UTF_8));
                if(!"virtual-cd-sync".equals(json.getString("format"))||json.getInt("version")!=1)throw error("同期情報の形式が不正です","Invalid sync manifest");
                records=json.getJSONObject("albums");
            }
            var ids=records.keys();while(ids.hasNext()){
                String id=ids.next();if(!id.matches("[0-9a-f]{64}"))throw error("同期IDが不正です","Invalid sync ID");
                var record=records.getJSONObject(id);String music=MobileSync.safePath(record.getString("music"));
                if(!music.startsWith(".vcd-sync/"+id+"/"))throw error("同期パスが不正です","Invalid sync path");
                var audio=relative(c,root,music);
                String bound=sync.getString(value+"|binding|"+id+"|"+record.optString("audioFingerprint"),"");
                if((audio!=null&&same(audio.uri,album.uri))||album.uri.toString().equals(bound)){
                    var container=relative(c,root,".vcd-sync/"+id);
                    if(container==null||!container.directory)throw error("転送フォルダーを確認できません","Unable to verify transfer folder");
                    targets.put(container.uri.toString(),container);
                    plan.syncKeys.add("sync|"+rootKey(root)+"|"+id);
                }
            }
        }
        // No guesses about sidecar files shared with other albums.
        collect(c,tree,plan,library,targets.values());return plan;
    }
    private static void collect(Context c,Uri tree,Plan plan,List<AlbumLibrary.Album> library,Collection<AlbumLibrary.Album> targets)throws Exception{
        var pendingItems=new ArrayDeque<>(targets);var seen=new HashSet<String>();
        while(!pendingItems.isEmpty()){
            var item=pendingItems.removeFirst();String identity=item.uri.getAuthority()+"|"+DocumentsContract.getDocumentId(item.uri);
            if(!seen.add(identity))continue;if(seen.size()>100000)throw error("削除対象が多すぎます","Too many deletion targets");
            within(c,tree,item.uri);writable(c,item.uri);
            for(var other:library)if(!same(other.uri,plan.album)&&same(other.uri,item.uri))
                throw error("別のアルバムを含むため削除できません","Cannot delete a folder containing another album");
            if(item.directory){plan.directories.add(item);pendingItems.addAll(AlbumLibrary.children(c,item.uri));}
            else plan.files.add(item);
        }
    }
    private static JSONArray items(List<AlbumLibrary.Album> items)throws Exception{
        var result=new JSONArray();for(var a:items)result.put(new JSONObject().put("uri",a.uri.toString()).put("name",a.name)
            .put("size",a.size).put("modified",a.modified).put("directory",a.directory));return result;
    }
    private static void loadItems(JSONArray values,List<AlbumLibrary.Album> items)throws Exception{
        for(int i=0;i<values.length();i++){var a=values.getJSONObject(i);items.add(new AlbumLibrary.Album(Uri.parse(a.getString("uri")),a.getString("name"),a.getLong("size"),a.getLong("modified"),a.getBoolean("directory")));}
    }
    public static void delete(Context c,Plan plan)throws Exception{
        synchronized(SyncDownload.class){synchronized(MobileSync.class){
            // Preflight every remaining file before deleting the first one.
            for(var f:plan.files){
                if(!exists(c,f.uri))continue;within(c,plan.tree,f.uri);writable(c,f.uri);
                var current=AlbumLibrary.describe(c,f.uri);
                if(current.directory||!current.name.equals(f.name)||current.size!=f.size||current.modified!=f.modified)
                    throw error("確認後にファイルが変わったため削除を中止しました","Files changed after confirmation; deletion stopped");
            }
            var json=new JSONObject().put("tree",rootKey(plan.tree)).put("files",items(plan.files))
                .put("directories",items(plan.directories)).put("sync",new JSONArray(plan.syncKeys));
            var edit=prefs(c).edit().putString("pending|"+plan.album,json.toString());for(String key:plan.syncKeys)edit.putBoolean(key,true);
            if(!edit.commit())throw error("削除状態を保存できません","Unable to save deletion state");
            for(var f:plan.files)if(exists(c,f.uri)){
                if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                within(c,plan.tree,f.uri);
                var current=AlbumLibrary.describe(c,f.uri);
                if(current.directory||!current.name.equals(f.name)||current.size!=f.size||current.modified!=f.modified)
                    throw error("確認後にファイルが変わったため削除を中止しました","Files changed after confirmation; deletion stopped");
                if(!DocumentsContract.deleteDocument(c.getContentResolver(),f.uri))
                    throw error("一部のファイルを削除できません。再試行してください","Some files could not be deleted. Please retry");
            }
            // Never recursively delete a directory: newly added/unlisted files must survive.
            var dirs=new ArrayList<>(plan.directories);
            while(!dirs.isEmpty()){
                boolean progress=false;
                for(var iterator=dirs.iterator();iterator.hasNext();){
                    var dir=iterator.next();
                    if(!exists(c,dir.uri)){iterator.remove();progress=true;continue;}
                    within(c,plan.tree,dir.uri);
                    if(!AlbumLibrary.children(c,dir.uri).isEmpty())continue;
                    if(!DocumentsContract.deleteDocument(c.getContentResolver(),dir.uri))throw error("フォルダーを削除できません","Unable to delete folder");
                    iterator.remove();progress=true;
                }
                if(!progress)throw error("フォルダーに未削除のファイルがあります。再試行してください","Files remain in the folder. Please retry");
            }
            String hash=CasePackage.hash(plan.album.toString().getBytes(StandardCharsets.UTF_8));
            for(String path:new String[]{"cases3d/"+hash+".vcd3d","lyrics/"+hash+".json","album-tags-v1/"+hash+".json"}){
                var file=new File(c.getFilesDir(),path);new android.util.AtomicFile(file).delete();
                if(file.exists()||new File(file+".bak").exists()||new File(file+".new").exists())
                    throw error("関連キャッシュを削除できません。再試行してください","Unable to delete related cache. Please retry");
            }
            new ListeningState(c).removeAlbum(plan.album.toString());
            if(!prefs(c).edit().putBoolean("uri|"+plan.album,true).remove("pending|"+plan.album).commit())
                throw error("ファイルは削除しましたが、一覧の更新に失敗しました","Files deleted, but library state could not be saved");
        }}
    }
    public static void allowRedownload(Context c)throws Exception{
        synchronized(SyncDownload.class){synchronized(MobileSync.class){
            for(String key:prefs(c).getAll().keySet())if(key.startsWith("pending|"))
                throw error("未完了の削除があります。先に削除を再試行してください","A deletion is incomplete. Retry it before allowing retransfer");
            if(!prefs(c).edit().clear().commit())throw error("再転送設定を保存できません","Unable to save retransfer settings");
            for(String namespace:new String[]{"sync-download-v1","mobile-sync-v1"}){
                var p=c.getSharedPreferences(namespace,0);var edit=p.edit();
                for(String key:p.getAll().keySet())if(key.endsWith("|manifest")||key.endsWith("|digest"))edit.remove(key);
                if(!edit.commit())throw error("同期情報を更新できません","Unable to update sync state");
            }
        }}
    }
}
