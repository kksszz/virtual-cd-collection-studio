package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.provider.DocumentsContract;
import jp.virtualcd.player.case3d.CasePackage;
import org.json.*;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.*;

/** Explicitly paired, private-LAN-only download into a user-authorized SAF tree. */
public final class SyncDownload {
    private static void reportToPc(String base,String digest,String message,boolean complete){
        HttpURLConnection report=null;
        try{
            byte[] body=new JSONObject().put("manifest",digest).put("message",message).put("complete",complete).toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
            report=(HttpURLConnection)new URL(base+"sync-status").openConnection();report.setConnectTimeout(800);report.setReadTimeout(800);report.setInstanceFollowRedirects(false);report.setRequestMethod("POST");report.setDoOutput(true);report.setFixedLengthStreamingMode(body.length);report.setRequestProperty("Content-Type","application/json");
            try(var out=report.getOutputStream()){out.write(body);}report.getResponseCode();
        }catch(Exception ignored){/* Reporting failure must not cancel verified music transfer. */}finally{if(report!=null)report.disconnect();}
    }
    public static String validateAddress(String text)throws Exception{
        var uri=new java.net.URI(text.trim());String host=uri.getHost();
        if(!"http".equals(uri.getScheme())||host==null||!host.matches("[0-9.]+")||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null)throw new IOException("Windowsに表示された家庭内LANのHTTPアドレスを入力してください");
        var address=InetAddress.getByName(host);if(!(address instanceof Inet4Address)||!address.isSiteLocalAddress())throw new IOException("家庭内LANのアドレスのみ利用できます");
        if(!uri.getPath().matches("/[0-9a-f]{48}/?"))throw new IOException("接続コードを含むアドレス全体を入力してください");
        return text.trim().replaceAll("/+$","")+"/";
    }
    private static HttpURLConnection connect(String base,String relative)throws Exception{
        MobileSync.safePath(relative);StringBuilder path=new StringBuilder();for(String part:relative.split("/")){if(path.length()>0)path.append('/');path.append(java.net.URLEncoder.encode(part,"UTF-8").replace("+","%20"));}
        var connection=(HttpURLConnection)new URL(base+path).openConnection();connection.setConnectTimeout(4000);connection.setReadTimeout(15000);connection.setInstanceFollowRedirects(false);
        if(connection.getResponseCode()!=200){connection.disconnect();throw new IOException("PCとの同期接続を確認してください");}return connection;
    }
    private static final class Tree {
        final Context c;final Uri root;final Map<String,Uri> directories=new HashMap<>();final Map<String,Map<String,AlbumLibrary.Album>> listings=new HashMap<>();
        Tree(Context c,Uri tree){this.c=c;root=DocumentsContract.isDocumentUri(c,tree)?tree:DocumentsContract.buildDocumentUriUsingTree(tree,DocumentsContract.getTreeDocumentId(tree));directories.put("",root);}
        Map<String,AlbumLibrary.Album> list(String parent)throws Exception{if(!listings.containsKey(parent)){var map=new HashMap<String,AlbumLibrary.Album>();for(var a:AlbumLibrary.children(c,dir(parent)))map.put(a.name,a);listings.put(parent,map);}return listings.get(parent);}
        Uri dir(String relative)throws Exception{if(directories.containsKey(relative))return directories.get(relative);int slash=relative.lastIndexOf('/');String parent=slash<0?"":relative.substring(0,slash),name=relative.substring(slash+1);Uri parentUri=dir(parent);var existing=list(parent).get(name);Uri uri;if(existing!=null){if(!existing.directory)throw new IOException("同期フォルダー名とファイルが重複しています");uri=existing.uri;}else{uri=DocumentsContract.createDocument(c.getContentResolver(),parentUri,DocumentsContract.Document.MIME_TYPE_DIR,name);if(uri==null)throw new IOException("保存先に書き込めません。音楽フォルダーを選び直してください");listings.remove(parent);}directories.put(relative,uri);return uri;}
        AlbumLibrary.Album existing(String path)throws Exception{int slash=path.lastIndexOf('/');return list(slash<0?"":path.substring(0,slash)).get(path.substring(slash+1));}
        Uri create(String path)throws Exception{int slash=path.lastIndexOf('/');String parent=slash<0?"":path.substring(0,slash);var uri=DocumentsContract.createDocument(c.getContentResolver(),dir(parent),"application/octet-stream",path.substring(slash+1));listings.remove(parent);if(uri==null)throw new IOException("保存先に書き込めません");return uri;}
        void invalidate(){listings.clear();}
    }
    public static synchronized void pull(Context c,Uri tree,String address,java.util.function.Consumer<String> progress)throws Exception{
        String base=validateAddress(address);byte[] manifest;var connection=connect(base,"vcd-sync.json");try(var in=connection.getInputStream()){manifest=CasePackage.readBytes(in,4*1024*1024);}finally{connection.disconnect();}
        var prefs=c.getSharedPreferences("sync-download-v1",0);String digest=CasePackage.hash(manifest),key=tree.toString();
        if(digest.equals(prefs.getString(key+"|manifest",""))){reportToPc(base,digest,"前回の保存・検証が完了しています（変更なし）",true);return;}
        var json=new JSONObject(new String(manifest,java.nio.charset.StandardCharsets.UTF_8));if(!json.getString("format").equals("virtual-cd-sync")||json.getInt("version")!=1)throw new IOException("未対応の同期形式です");
        var records=json.getJSONObject("albums");var files=new LinkedHashMap<String,JSONObject>();var ids=records.keys();
        while(ids.hasNext()){String id=ids.next();if(!id.matches("[0-9a-f]{64}"))throw new IOException("不正な同期IDです");var record=records.getJSONObject(id);var array=record.getJSONArray("files");for(int i=0;i<array.length();i++){var file=array.getJSONObject(i);String path=MobileSync.safePath(file.getString("path"));if(!path.startsWith(".vcd-sync/"+id+"/")||!file.getString("sha256").matches("[0-9a-f]{64}")||file.getLong("size")<0||file.getLong("size")>16L*1024*1024*1024)throw new IOException("不正な転送情報です");if(files.put(path,file)!=null)throw new IOException("重複した転送情報です");if(files.size()>100000)throw new IOException("転送ファイルが多すぎます");}}
        long total=0;for(var file:files.values())total=Math.addExact(total,file.getLong("size"));
        final long[] lastReport={0};
        var meter=new SyncProgress(total,files.size(),message->{progress.accept(message);long now=System.nanoTime();if(now-lastReport[0]>1_000_000_000L){lastReport[0]=now;reportToPc(base,digest,message,false);}});meter.report("転送の準備中","");
        var target=new Tree(c,tree);int index=0;
        for(var item:files.entrySet()){
            if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();String path=item.getKey(),hash=item.getValue().getString("sha256");long size=item.getValue().getLong("size");index++;
            var existing=target.existing(path);if(existing!=null&&existing.size==size&&hash.equals(prefs.getString(key+"|"+path,""))){meter.reused(size);continue;}
            String name=path.substring(path.lastIndexOf('/')+1);meter.report("PCから転送中",name);
            if(existing!=null){meter.report("既存ファイルを検証中",name);try(var in=c.getContentResolver().openInputStream(existing.uri)){if(in!=null&&hashStream(in,null,size).equals(hash)){prefs.edit().putString(key+"|"+path,hash).commit();meter.reused(size);continue;}}throw new IOException("転送先に異なるデータがあります。既存ファイルを保護して中断しました。");}
            Uri temp=target.create(path+"."+UUID.randomUUID()+".partial");boolean complete=false;connection=null;
            try{connection=connect(base,path);try(var in=connection.getInputStream();var out=c.getContentResolver().openOutputStream(temp,"wt")){if(out==null)throw new IOException("SDカードに書き込めません");if(!hashStream(in,out,size,n->meter.received(n,name)).equals(hash))throw new IOException("転送データの検証に失敗しました");}
                Uri renamed=DocumentsContract.renameDocument(c.getContentResolver(),temp,path.substring(path.lastIndexOf('/')+1));if(renamed==null)throw new IOException("転送ファイルを確定できません");complete=true;prefs.edit().putString(key+"|"+path,hash).commit();target.invalidate();
            }finally{if(connection!=null)connection.disconnect();if(!complete)try{DocumentsContract.deleteDocument(c.getContentResolver(),temp);}catch(Exception ignored){}}
            meter.completed();
        }
        // Only publish once all payloads have been verified. A partial JSON is rejected by the reader.
        meter.report("転送済み・同期情報を保存中","");
        var old=target.existing("vcd-sync.json");byte[] previous=null;
        var backup=new android.util.AtomicFile(new File(c.getFilesDir(),"sync-commit-"+CasePackage.hash(key.getBytes(java.nio.charset.StandardCharsets.UTF_8))+".json"));
        if(old!=null){try(var in=c.getContentResolver().openInputStream(old.uri)){if(in==null)throw new IOException("既存の同期情報を確認できません");previous=CasePackage.readBytes(in,4*1024*1024);}
            JSONObject merged;try{merged=new JSONObject(new String(previous,java.nio.charset.StandardCharsets.UTF_8));}catch(JSONException broken){try(var in=backup.openRead()){previous=CasePackage.readBytes(in,4*1024*1024);}merged=new JSONObject(new String(previous,java.nio.charset.StandardCharsets.UTF_8));}
            if(!merged.getString("format").equals("virtual-cd-sync")||merged.getInt("version")!=1)throw new IOException("既存の同期索引は未対応です");
            var all=merged.getJSONObject("albums");var incoming=records.keys();while(incoming.hasNext()){String id=incoming.next();all.put(id,records.getJSONObject(id));}manifest=merged.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
        }
        Uri commit=old==null?target.create("vcd-sync.json"):old.uri;
        FileOutputStream saved=null;try{saved=backup.startWrite();saved.write(manifest);backup.finishWrite(saved);}catch(Exception ex){if(saved!=null)backup.failWrite(saved);throw ex;}
        try{try(var out=c.getContentResolver().openOutputStream(commit,"wt")){if(out==null)throw new IOException("同期情報を保存できません");out.write(manifest);}}
        catch(Exception ex){if(previous!=null)try(var out=c.getContentResolver().openOutputStream(commit,"wt")){if(out!=null)out.write(previous);}catch(Exception ignored){}throw ex;}
        if(!prefs.edit().putString(key+"|manifest",digest).commit())throw new IOException("同期完了状態を保存できませんでした。再接続してください");
        meter.report("PCからの転送完了","");reportToPc(base,digest,meter.text("保存・検証完了",""),true);
    }
    private static String hashStream(InputStream in,OutputStream out,long expected)throws Exception{return hashStream(in,out,expected,n->{});}
    private static String hashStream(InputStream in,OutputStream out,long expected,java.util.function.LongConsumer progress)throws Exception{var sha=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[65536];long count=0;int n;while((n=in.read(buffer))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();count+=n;if(count>expected)throw new IOException("転送サイズが一致しません");sha.update(buffer,0,n);if(out!=null)out.write(buffer,0,n);progress.accept(n);}if(count!=expected)throw new IOException("転送が途中で切断されました");StringBuilder result=new StringBuilder();for(byte b:sha.digest())result.append(String.format(Locale.ROOT,"%02x",b&255));return result.toString();}
}
