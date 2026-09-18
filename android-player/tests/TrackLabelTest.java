import jp.virtualcd.player.library.TrackLabel;
public class TrackLabelTest {
    static void check(String tag,String file,String expected){
        if(!expected.equals(TrackLabel.title(tag,file)))throw new AssertionError(file);
        System.out.println("PASS track label: "+expected);
    }
    public static void main(String[] args){
        check("負けないで","01-負けないで.mp3","負けないで");
        check("  Song title  ","02-wrong.mp3","Song title");
        check(null,"Disc1/03-Song.MP3","03-Song");
        check("  ","Disc2\\04-曲名.mp3","04-曲名");
        check("Title.mp3","01.mp3","Title.mp3");
        check(null,"Disc1/02-Song.FLAC","02-Song");
        check(null,"01-Song.m4a","01-Song");
        check(null,"01-Song.WAV","01-Song");
    }
}
