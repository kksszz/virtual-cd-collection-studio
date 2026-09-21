package jp.virtualcd.player.library;

import java.util.Locale;
import java.util.function.Consumer;

/** Byte-based payload progress; reused bytes are not counted as network traffic. */
public final class SyncProgress {
    private final long total;
    private final int files;
    private final Consumer<String> output;
    private long done,received,reused,last;
    private int completed;
    public SyncProgress(long total,int files,Consumer<String> output){this.total=total;this.files=files;this.output=output;}
    public static String size(long bytes){return bytes>=1024L*1024*1024?String.format(Locale.ROOT,"%.2f GiB",bytes/(1024d*1024*1024)):String.format(Locale.ROOT,"%.1f MiB",bytes/(1024d*1024));}
    public String text(String stage,String name){
        int percent=total==0?100:(int)Math.min(100,done*100d/total);
        return stage+" · "+percent+"% · "+size(done)+" / "+size(total)
            +jp.virtualcd.player.LanguageStrings.text("\n受信 ","\nReceived ")+size(received)+jp.virtualcd.player.LanguageStrings.text(" · 再利用 "," · Reused ")+size(reused)+" · "+completed+" / "+files+jp.virtualcd.player.LanguageStrings.text("ファイル"," files")
            +(name.isEmpty()?"":"\n"+name);
    }
    public void report(String stage,String name){output.accept(text(stage,name));last=System.nanoTime();}
    public void received(long bytes,String name){done+=bytes;received+=bytes;if(System.nanoTime()-last>=250_000_000L)report(jp.virtualcd.player.LanguageStrings.text("PCから転送中","Receiving from PC"),name);}
    public void reused(long bytes){done+=bytes;reused+=bytes;completed++;if(System.nanoTime()-last>=250_000_000L)report(jp.virtualcd.player.LanguageStrings.text("既存ファイルを確認中","Checking existing files"),"");}
    public void completed(){completed++;report(jp.virtualcd.player.LanguageStrings.text("PCから転送中","Receiving from PC"),"");}
}
