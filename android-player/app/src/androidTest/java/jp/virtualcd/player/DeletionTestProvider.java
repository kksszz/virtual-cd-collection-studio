package jp.virtualcd.player;

import android.database.*;
import android.os.*;
import android.provider.DocumentsContract;
import android.provider.DocumentsProvider;
import java.io.*;
import java.util.*;

/** Only serves freshly generated, in-memory test documents. No user storage access. */
public final class DeletionTestProvider extends DocumentsProvider {
    @Override public void attachInfo(android.content.Context context,android.content.pm.ProviderInfo info){
        // DocumentsProvider requires this contract. Only this test APK then relaxes
        // access to its generated in-memory fixtures; production has no provider.
        var contract=new android.content.pm.ProviderInfo(info);
        contract.readPermission="android.permission.MANAGE_DOCUMENTS";
        contract.writePermission="android.permission.MANAGE_DOCUMENTS";
        super.attachInfo(context,contract);
        setReadPermission(null);setWritePermission(null);
    }
    static final String AUTHORITY="jp.virtualcd.player.test.deletion";
    static final String ID="a".repeat(64);
    static final class Node {
        final String id,parent,name;final boolean directory;byte[] data;long modified=1;
        Node(String id,String parent,String name,boolean dir,String text){this.id=id;this.parent=parent;this.name=name;directory=dir;data=text.getBytes(java.nio.charset.StandardCharsets.UTF_8);}
    }
    final Map<String,Node> nodes=new LinkedHashMap<>();
    private void add(String id,String parent,String name,boolean dir,String text){nodes.put(id,new Node(id,parent,name,dir,text));}
    @Override public boolean onCreate(){return true;}
    @Override public synchronized Bundle call(String method,String arg,Bundle extras){
        if(method.equals("test-seed")){
            nodes.clear();add("root",null,"Test music",true,"");add("album","root","Test album",true,"");
            add("track","album","01.mp3",false,"audio");add("cover","album","cover.png",false,"picture");
            add("other","root","Other album",true,"");add("other-track","other","02.mp3",false,"do not delete");
            if("sync".equals(arg)){
                nodes.remove("album");nodes.remove("track");nodes.remove("cover");
                add("sync","root",".vcd-sync",true,"");add("container","sync",ID,true,"");
                add("track","container","music.zip.mp3",false,"audio");add("glb","container","case.glb",false,"model");
                add("lyrics","container","lyrics.json",false,"lyrics");
                add("manifest","root","vcd-sync.json",false,
                    "{\"format\":\"virtual-cd-sync\",\"version\":1,\"albums\":{\""+ID+"\":{\"music\":\".vcd-sync/"+ID+"/music.zip.mp3\"}}}");
            }
            return new Bundle();
        }
        if(method.equals("test-change")){nodes.get("track").modified++;return new Bundle();}
        if(method.equals("test-add")){add("new","album","new.txt",false,"new file");return new Bundle();}
        return super.call(method,arg,extras);
    }
    private static final String[] COLUMNS={DocumentsContract.Document.COLUMN_DOCUMENT_ID,DocumentsContract.Document.COLUMN_DISPLAY_NAME,
        DocumentsContract.Document.COLUMN_MIME_TYPE,DocumentsContract.Document.COLUMN_SIZE,DocumentsContract.Document.COLUMN_LAST_MODIFIED,DocumentsContract.Document.COLUMN_FLAGS};
    private void row(MatrixCursor cursor,Node node){
        var row=cursor.newRow();for(String column:cursor.getColumnNames()){
            Object value=switch(column){
                case DocumentsContract.Document.COLUMN_DOCUMENT_ID -> node.id;
                case DocumentsContract.Document.COLUMN_DISPLAY_NAME -> node.name;
                case DocumentsContract.Document.COLUMN_MIME_TYPE -> node.directory?DocumentsContract.Document.MIME_TYPE_DIR:"application/octet-stream";
                case DocumentsContract.Document.COLUMN_SIZE -> node.data.length;
                case DocumentsContract.Document.COLUMN_LAST_MODIFIED -> node.modified;
                case DocumentsContract.Document.COLUMN_FLAGS -> DocumentsContract.Document.FLAG_SUPPORTS_DELETE;
                default -> null;
            };row.add(value);
        }
    }
    @Override public Cursor queryRoots(String[] projection){return new MatrixCursor(new String[]{DocumentsContract.Root.COLUMN_ROOT_ID});}
    @Override public synchronized Cursor queryDocument(String id,String[] projection){var cursor=new MatrixCursor(projection==null?COLUMNS:projection);if(nodes.containsKey(id))row(cursor,nodes.get(id));return cursor;}
    @Override public synchronized Cursor queryChildDocuments(String parent,String[] projection,String sort){var cursor=new MatrixCursor(projection==null?COLUMNS:projection);for(var n:nodes.values())if(parent.equals(n.parent))row(cursor,n);return cursor;}
    @Override public synchronized boolean isChildDocument(String parent,String child){for(Node n=nodes.get(child);n!=null;n=nodes.get(n.parent))if(parent.equals(n.parent))return true;return false;}
    @Override public synchronized void deleteDocument(String id)throws FileNotFoundException{
        if("root".equals(id)||"other".equals(id)||"other-track".equals(id))throw new FileNotFoundException("Protected fixture");
        for(var n:nodes.values())if(id.equals(n.parent))throw new FileNotFoundException("Not empty");
        nodes.remove(id);
    }
    @Override public synchronized ParcelFileDescriptor openDocument(String id,String mode,CancellationSignal signal)throws FileNotFoundException{
        if(!"r".equals(mode)||!nodes.containsKey(id))throw new FileNotFoundException();
        try{
            var pair=ParcelFileDescriptor.createPipe();byte[] data=nodes.get(id).data;
            new Thread(()->{try(var out=new ParcelFileDescriptor.AutoCloseOutputStream(pair[1])){out.write(data);}catch(IOException ignored){}}).start();
            return pair[0];
        }catch(IOException ex){throw new FileNotFoundException(ex.getMessage());}
    }
}
