package jp.virtualcd.player.library;
import java.util.Locale;
public final class AudioFormats {
    public static boolean archive(String name){String s=name.toLowerCase(Locale.ROOT);return s.endsWith(".zip.mp3")||s.endsWith(".zip");}
    public static boolean audio(String name){return !archive(name)&&mime(name)!=null;}
    public static String mime(String name){
        String s=name.toLowerCase(Locale.ROOT);
        if(s.endsWith(".mp3"))return "audio/mpeg";
        if(s.endsWith(".flac"))return "audio/flac";
        if(s.endsWith(".wav"))return "audio/wav";
        if(s.endsWith(".m4a"))return "audio/mp4";
        return null;
    }
    public static String natural(String name){
        var m=java.util.regex.Pattern.compile("[0-9]+").matcher(name.toLowerCase(Locale.ROOT));var out=new StringBuffer();
        while(m.find())m.appendReplacement(out,"000000000000".substring(0,Math.max(0,12-m.group().length()))+m.group());m.appendTail(out);return out.toString();
    }
    public static int number(String tag){try{return Math.max(0,Integer.parseInt(tag.split("/",2)[0].trim()));}catch(Exception e){return 0;}}
}
