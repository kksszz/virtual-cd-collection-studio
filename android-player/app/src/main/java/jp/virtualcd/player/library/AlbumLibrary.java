package jp.virtualcd.player.library;

import android.content.Context;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.util.AtomicFile;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** SAF-only, read-only discovery. The small index is stored in the app's private directory. */
public final class AlbumLibrary {
    public static final class Album {
        public final Uri uri;
        public final String name;
        public final long size, modified;
        public final boolean directory;
        public Album(Uri uri,String name,long size,long modified){this(uri,name,size,modified,false);}
        public Album(Uri uri,String name,long size,long modified,boolean directory){this.uri=uri;this.name=name;this.size=size;this.modified=modified;this.directory=directory;}
        public String title(){return name.replaceFirst("(?i)\\.zip(?:\\.mp3)?$","");}
        public String key(){return uri+"|"+size+"|"+modified;}
    }
    public interface Progress { void update(int albums); }
    public static Album describe(Context context,Uri uri)throws IOException{
        try(var c=context.getContentResolver().query(uri,new String[]{DocumentsContract.Document.COLUMN_DISPLAY_NAME,DocumentsContract.Document.COLUMN_SIZE,DocumentsContract.Document.COLUMN_LAST_MODIFIED,DocumentsContract.Document.COLUMN_MIME_TYPE},null,null,null)){
            if(c==null||!c.moveToFirst())throw new IOException("ファイル情報を取得できません");
            return new Album(uri,c.getString(0),c.getLong(1),c.getLong(2),DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(3)));
        }
    }
    public static List<Album> children(Context context,Uri directory)throws IOException{
        var result=new ArrayList<Album>();
        Uri children=DocumentsContract.buildChildDocumentsUriUsingTree(directory,DocumentsContract.getDocumentId(directory));
        try(var c=context.getContentResolver().query(children,new String[]{DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,DocumentsContract.Document.COLUMN_SIZE,DocumentsContract.Document.COLUMN_LAST_MODIFIED,DocumentsContract.Document.COLUMN_MIME_TYPE},null,null,null)){
            if(c==null)throw new IOException("フォルダーを開けません");
            while(c.moveToNext()){
                if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                result.add(new Album(DocumentsContract.buildDocumentUriUsingTree(directory,c.getString(0)),c.getString(1),c.getLong(2),c.getLong(3),DocumentsContract.Document.MIME_TYPE_DIR.equals(c.getString(4))));
            }
        }return result;
    }
    public static List<Album> scan(Context context,Uri tree,Progress progress) throws IOException {
        var result=new ArrayList<Album>();var pending=new ArrayDeque<String>();var visited=new HashSet<String>();
        pending.add(DocumentsContract.getTreeDocumentId(tree));
        String[] columns={DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,DocumentsContract.Document.COLUMN_SIZE,DocumentsContract.Document.COLUMN_LAST_MODIFIED};
        while(!pending.isEmpty()){
            if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
            String id=pending.removeFirst();if(!visited.add(id))continue;
            long audioSize=0,modified=0;int audioCount=0;
            try(var c=context.getContentResolver().query(DocumentsContract.buildChildDocumentsUriUsingTree(tree,id),columns,null,null,null)){
                if(c==null)throw new IOException("フォルダーを読み取れません");
                while(c.moveToNext()){
                    if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                    String child=c.getString(0),name=c.getString(1),mime=c.getString(2);
                    if(DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)){if(!".vcd-sync".equals(name))pending.add(child);continue;}
                    if(name==null)continue;String lower=name.toLowerCase(Locale.ROOT);
                    if(AudioFormats.audio(name)){audioCount++;audioSize+=c.getLong(3);modified=Math.max(modified,c.getLong(4));continue;}
                    if(!AudioFormats.archive(name))continue;
                    result.add(new Album(DocumentsContract.buildDocumentUriUsingTree(tree,child),name,c.getLong(3),c.getLong(4)));
                }
            }
            if(audioCount>0){Uri folder=DocumentsContract.buildDocumentUriUsingTree(tree,id);Album info=describe(context,folder);result.add(new Album(folder,info.name,audioSize,modified,true));}
            Uri syncRoot=DocumentsContract.buildDocumentUriUsingTree(tree,id);
            try{result.addAll(MobileSync.read(context,syncRoot));}catch(java.io.InterruptedIOException ex){throw ex;}catch(Exception ex){result.addAll(MobileSync.cached(context,syncRoot));android.util.Log.w("MobileSync","同期データは未反映です",ex);}
            progress.update(result.size());
        }
        var unique=new LinkedHashMap<String,Album>();for(var album:result)unique.put(album.uri.toString(),album);result=new ArrayList<>(unique.values());
        result.sort(Comparator.comparing(Album::title,String.CASE_INSENSITIVE_ORDER));return result;
    }
    private static AtomicFile index(Context c){return new AtomicFile(new File(c.getFilesDir(),"albums-v1.json"));}
    public static void save(Context context,Uri tree,List<Album> albums) throws Exception {
        var array=new JSONArray();for(var a:albums)array.put(new JSONObject().put("uri",a.uri.toString()).put("name",a.name).put("size",a.size).put("modified",a.modified).put("directory",a.directory));
        byte[] bytes=new JSONObject().put("tree",tree.toString()).put("albums",array).toString().getBytes(StandardCharsets.UTF_8);
        var file=index(context);FileOutputStream out=null;
        try{out=file.startWrite();out.write(bytes);file.finishWrite(out);}catch(Exception e){if(out!=null)file.failWrite(out);throw e;}
    }
    public static List<Album> load(Context context,Uri tree) throws Exception {
        var file=index(context);if(!file.getBaseFile().exists())return Collections.emptyList();
        var root=new JSONObject(new String(file.readFully(),StandardCharsets.UTF_8));
        if(!root.getString("tree").equals(tree.toString()))return Collections.emptyList();
        var array=root.getJSONArray("albums");var result=new ArrayList<Album>();
        for(int i=0;i<array.length();i++){var a=array.getJSONObject(i);result.add(new Album(Uri.parse(a.getString("uri")),a.getString("name"),a.getLong("size"),a.getLong("modified"),a.optBoolean("directory",false)));}
        return result;
    }
}
