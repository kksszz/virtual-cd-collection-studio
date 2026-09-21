package jp.virtualcd.player.archive;

import android.content.Context;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import androidx.media3.common.C;
import androidx.media3.datasource.BaseDataSource;
import androidx.media3.datasource.DataSpec;
import java.io.IOException;
import java.nio.ByteBuffer;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public final class ZipTrackDataSource extends BaseDataSource {
    private final Context context;
    private ParcelFileDescriptor.AutoCloseInputStream input;
    private Uri uri;
    private long position, remaining;
    private boolean opened;
    public ZipTrackDataSource(Context context) { super(false); this.context=context.getApplicationContext(); }
    public static Uri trackUri(Uri document, String name) {
        return new Uri.Builder().scheme("zipmp3").authority("track")
            .appendQueryParameter("document",document.toString()).appendQueryParameter("entry",name).build();
    }
    @Override public long open(DataSpec spec) throws IOException {
        transferInitializing(spec);
        try {
            uri=spec.uri;
            String document=uri.getQueryParameter("document"), name=uri.getQueryParameter("entry");
            if (!"zipmp3".equals(uri.getScheme()) || document==null || name==null) throw new IOException(jp.virtualcd.player.LanguageStrings.text("曲の識別情報が不正です","Invalid track identity"));
            Uri source=Uri.parse(document);
            if (!"content".equals(source.getScheme())) throw new IOException(jp.virtualcd.player.LanguageStrings.text("端末のファイルを選択してください","Select a file on the device"));
            ParcelFileDescriptor fd=context.getContentResolver().openFileDescriptor(source,"r");
            if (fd==null) throw new IOException(jp.virtualcd.player.LanguageStrings.text("SDカード／音源を開けません","Unable to open the SD card or audio"));
            input=new ParcelFileDescriptor.AutoCloseInputStream(fd);
            StoredZipIndex.Entry entry=null;
            for (var candidate:StoredZipIndex.read(input.getChannel())) if(candidate.name.equals(name)) {entry=candidate;break;}
            if (entry==null) throw new IOException(jp.virtualcd.player.LanguageStrings.text("ZIP内の曲が見つかりません","Track not found in ZIP"));
            if(spec.position<0 || spec.position>entry.length) throw new IOException(jp.virtualcd.player.LanguageStrings.text("シーク位置が範囲外です","Seek position out of range"));
            position=entry.offset+spec.position;
            remaining=entry.length-spec.position;
            if (spec.length!=C.LENGTH_UNSET) remaining=Math.min(remaining,spec.length);
            opened=true; transferStarted(spec); return remaining;
        } catch (IOException | RuntimeException ex) { close(); throw new IOException(jp.virtualcd.player.LanguageStrings.text("音源を開けません: ","Unable to open audio: ")+ex.getMessage(),ex); }
    }
    @Override public int read(byte[] buffer,int offset,int length) throws IOException {
        if(length==0) return 0;
        if(remaining==0) return C.RESULT_END_OF_INPUT;
        if(input==null) throw new IOException(jp.virtualcd.player.LanguageStrings.text("音源が閉じられています","Audio source is closed"));
        int count=input.getChannel().read(ByteBuffer.wrap(buffer,offset,(int)Math.min(length,remaining)),position);
        if(count<=0) throw new IOException(jp.virtualcd.player.LanguageStrings.text("音源が途中で切れています","Audio is truncated"));
        position+=count; remaining-=count; bytesTransferred(count); return count;
    }
    @Override public Uri getUri() { return uri; }
    @Override public void close() throws IOException {
        try { if(input!=null) input.close(); }
        finally { input=null; uri=null; if(opened) {opened=false; transferEnded();} }
    }
}
