package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.util.AtomicFile;
import jp.virtualcd.player.case3d.CasePackage;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Read-only SAF sync ingestion. A committed manifest is the only publication boundary. */
public final class MobileSync {
    public static String safePath(String path)throws IOException{
        if(path.isEmpty()||path.contains("\\")||path.startsWith("/")||path.endsWith("/"))throw new IOException("不正な同期パスです");
        for(String part:path.split("/",-1))if(part.isEmpty()||part.equals(".")||part.equals(".."))throw new IOException("不正な同期パスです");
        return path;
    }
    private static class Resolver {
        final Context c;final Uri root;final Map<String,AlbumLibrary.Album> paths=new HashMap<>();final Set<String> listed=new HashSet<>();
        Resolver(Context c,Uri root){this.c=c;this.root=root;}
        AlbumLibrary.Album get(String path)throws Exception{
            safePath(path);String parent="";Uri directory=root;
            for(String part:path.split("/")){
                if(listed.add(parent))for(var child:AlbumLibrary.children(c,directory))paths.put(parent+child.name,child);
                var found=paths.get(parent+part);if(found==null)throw new IOException("転送ファイルがありません: "+path);
                parent+=part+"/";directory=found.uri;
            }return paths.get(path);
        }
    }
    public static synchronized List<AlbumLibrary.Album> read(Context c,Uri root)throws Exception{
        var resolver=new Resolver(c,root);AlbumLibrary.Album manifest=null;
        for(var file:AlbumLibrary.children(c,root))if(file.name.equals("vcd-sync.json")&&!file.directory)manifest=file;
        if(manifest==null)return Collections.emptyList();
        byte[] bytes;try(var in=c.getContentResolver().openInputStream(manifest.uri)){if(in==null)throw new IOException("同期索引を開けません");bytes=CasePackage.readBytes(in,4*1024*1024);}
        var json=new JSONObject(new String(bytes,StandardCharsets.UTF_8));
        if(!json.getString("format").equals("virtual-cd-sync")||json.getInt("version")!=1)throw new IOException("未対応の同期形式です");
        var records=json.getJSONObject("albums");if(records.length()>20000)throw new IOException("同期アルバム数の上限です");
        var prefs=c.getSharedPreferences("mobile-sync-v1",0);String rootKey=root.toString(),digest=CasePackage.hash(bytes);
        String saved=prefs.getString(rootKey+"|index",null);
        if(digest.equals(prefs.getString(rootKey+"|digest",""))&&saved!=null)return decode(saved);
        var result=new ArrayList<AlbumLibrary.Album>();var keys=records.keys();
        while(keys.hasNext()){
            if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
            String id=keys.next();var record=records.getJSONObject(id);
            if(!id.matches("[0-9a-f]{64}")||!id.equals(record.getString("id")))throw new IOException("不正なアルバムIDです");
            String prefix=".vcd-sync/"+id+"/",music=record.getString("music"),glb=record.getString("glb"),hash=record.getString("glbSha256");
            safePath(music);safePath(glb);
            if(!music.startsWith(prefix)||!glb.startsWith(prefix)||!hash.matches("[0-9a-f]{64}"))throw new IOException("アルバムの対応情報が不正です");
            var audio=resolver.get(music);if(audio.directory!=record.getBoolean("directory"))throw new IOException("音楽データの種類が不正です");
            audio=adoptExisting(c,root,record,audio,prefs);
            var folder=new File(c.getFilesDir(),"cases3d");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("保存領域を作成できません");
            String binding=CasePackage.hash(audio.uri.toString().getBytes(StandardCharsets.UTF_8));var file=new File(folder,binding+".vcd3d");
            if(!hash.equals(prefs.getString(binding,""))||!file.isFile()){
                var source=resolver.get(glb);byte[] data;try(var in=c.getContentResolver().openInputStream(source.uri)){if(in==null)throw new IOException("3Dを開けません");data=CasePackage.readBytes(in,CasePackage.MAX_BYTES);}
                if(!CasePackage.hash(data).equals(hash))throw new IOException("3Dの転送検証に失敗しました");
                try(var checked=CasePackage.parse(data)){} // Reject unsupported/corrupted GLB before replacing a working case.
                var atomic=new AtomicFile(file);FileOutputStream out=null;try{out=atomic.startWrite();out.write(data);atomic.finishWrite(out);}catch(Exception ex){if(out!=null)atomic.failWrite(out);throw ex;}
                prefs.edit().putString(binding,hash).commit();
            }
            result.add(new AlbumLibrary.Album(audio.uri,audio.directory?record.getString("title"):audio.name,record.getLong("size"),audio.modified,audio.directory));
        }
        var roots=new HashSet<>(prefs.getStringSet("roots",Collections.emptySet()));roots.add(rootKey);
        prefs.edit().putStringSet("roots",roots).putString(rootKey+"|digest",digest).putString(rootKey+"|index",encode(result)).commit();
        return result;
    }
    private static String encode(List<AlbumLibrary.Album> albums)throws Exception{var array=new JSONArray();for(var a:albums)array.put(new JSONObject().put("uri",a.uri.toString()).put("name",a.name).put("size",a.size).put("modified",a.modified).put("directory",a.directory));return array.toString();}
    private static String titleKey(String value){return java.text.Normalizer.normalize(value,java.text.Normalizer.Form.NFKC).toLowerCase(Locale.ROOT).replaceAll("[^\\p{L}\\p{N}]","").replace("deluxeedition","").replace("bonustrack","");}
    private static AlbumLibrary.Album adoptExisting(Context c,Uri root,JSONObject record,AlbumLibrary.Album fallback,android.content.SharedPreferences prefs)throws Exception{
        String fingerprint=record.optString("audioFingerprint","");if(!fingerprint.matches("[0-9a-f]{64}")||record.optInt("audioCount")<1)return fallback;
        String key=root+"|binding|"+record.getString("id")+"|"+fingerprint;
        String bound=prefs.getString(key,null);if(bound!=null)try{return AlbumLibrary.describe(c,Uri.parse(bound));}catch(Exception ignored){}
        String selected=c.getSharedPreferences("MainActivity",0).getString("tree",null);if(selected==null)return fallback;
        List<AlbumLibrary.Album> candidates;try{candidates=AlbumLibrary.load(c,Uri.parse(selected));}catch(Exception ignored){return fallback;}
        String title=titleKey(record.getString("title"));if(title.length()<4)return fallback;AlbumLibrary.Album match=null;
        for(var album:candidates){if(album.uri.toString().contains(".vcd-sync")||!titleKey(album.title()).contains(title))continue;
            try{if(AlbumIndicators.read(c,album).count!=record.getInt("audioCount"))continue;
                String cache="fingerprint|"+album.key();String actual=prefs.getString(cache,null);if(actual==null){actual=fingerprint(c,album);prefs.edit().putString(cache,actual).apply();}
                if(fingerprint.equals(actual)){if(match!=null)return fallback;match=album;}
            }catch(InterruptedIOException ex){throw ex;}catch(Exception ignored){}
        }
        if(match!=null){prefs.edit().putString(key,match.uri.toString()).commit();return match;}return fallback;
    }
    private static String fingerprint(Context c,AlbumLibrary.Album album)throws Exception{
        var hashes=new ArrayList<String>();
        if(album.directory){for(var child:AlbumLibrary.children(c,album.uri))if(!child.directory&&AudioFormats.audio(child.name))try(var in=c.getContentResolver().openInputStream(child.uri)){if(in==null)throw new IOException("音源を開けません");hashes.add(hashAudio(in,child.size));}}
        else{var fd=c.getContentResolver().openFileDescriptor(album.uri,"r");if(fd==null)throw new IOException("音源を開けません");try(var in=new android.os.ParcelFileDescriptor.AutoCloseInputStream(fd)){var channel=in.getChannel();for(var entry:jp.virtualcd.player.archive.StoredZipIndex.read(channel)){if(entry.method!=0)throw new IOException("圧縮音源は照合できません");channel.position(entry.offset);hashes.add(hashAudio(in,entry.length));}}}
        Collections.sort(hashes);return CasePackage.hash(String.join("\n",hashes).getBytes(StandardCharsets.UTF_8));
    }
    private static String hashAudio(InputStream in,long length)throws Exception{var hash=java.security.MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[65536];while(length>0){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();int n=in.read(buffer,0,(int)Math.min(length,buffer.length));if(n<0)throw new EOFException();hash.update(buffer,0,n);length-=n;}StringBuilder result=new StringBuilder();for(byte b:hash.digest())result.append(String.format(Locale.ROOT,"%02x",b&255));return result.toString();}
    public static List<AlbumLibrary.Album> cached(Context c,Uri root){try{return decode(c.getSharedPreferences("mobile-sync-v1",0).getString(root.toString()+"|index","[]"));}catch(Exception ignored){return Collections.emptyList();}}
    private static List<AlbumLibrary.Album> decode(String text)throws Exception{var result=new ArrayList<AlbumLibrary.Album>();var array=new JSONArray(text);for(int i=0;i<array.length();i++){var a=array.getJSONObject(i);result.add(new AlbumLibrary.Album(Uri.parse(a.getString("uri")),a.getString("name"),a.getLong("size"),a.getLong("modified"),a.getBoolean("directory")));}return result;}
    public static synchronized List<AlbumLibrary.Album> refresh(Context c,Uri tree,List<AlbumLibrary.Album> current)throws Exception{
        var prefs=c.getSharedPreferences("mobile-sync-v1",0);var roots=new HashSet<>(prefs.getStringSet("roots",Collections.emptySet()));
        roots.add(android.provider.DocumentsContract.buildDocumentUriUsingTree(tree,android.provider.DocumentsContract.getTreeDocumentId(tree)).toString());
        var result=new ArrayList<>(current);
        for(String root:roots){
            Uri uri=Uri.parse(root);if(!android.provider.DocumentsContract.getTreeDocumentId(uri).equals(android.provider.DocumentsContract.getTreeDocumentId(tree)))continue;
            var before=decode(prefs.getString(root+"|index","[]"));var next=read(c,uri);
            if(next.isEmpty())continue;
            var old=new HashSet<String>();for(var a:before)old.add(a.uri.toString());for(var a:next)old.add(a.uri.toString());
            result.removeIf(a->old.contains(a.uri.toString()));result.addAll(next);
        }return result;
    }
}
