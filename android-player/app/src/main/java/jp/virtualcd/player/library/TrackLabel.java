package jp.virtualcd.player.library;

public final class TrackLabel {
    public static String title(String tag,String entryName){
        if(tag!=null&&!tag.trim().isEmpty())return tag.trim();
        String file=entryName.replace('\\','/');
        file=file.substring(file.lastIndexOf('/')+1);
        return file.replaceFirst("(?i)\\.(mp3|flac|wav|m4a)$","");
    }
}
