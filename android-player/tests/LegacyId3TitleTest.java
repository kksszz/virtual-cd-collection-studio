import jp.virtualcd.player.library.LegacyId3Title;
import java.nio.file.*;
import java.nio.channels.FileChannel;
import java.nio.charset.Charset;
public class LegacyId3TitleTest {
    public static void main(String[] args)throws Exception{
        if(!"突然".equals(LegacyId3Title.decode(new byte[]{(byte)0x93,(byte)0xcb,(byte)0x91,0x52})))throw new AssertionError("CP932 title");
        if(LegacyId3Title.decode("Anniversary".getBytes())!=null)throw new AssertionError("ASCII changed");
        if(LegacyId3Title.decode(new byte[]{(byte)0x81})!=null)throw new AssertionError("Invalid sequence accepted");
        System.out.println("PASS CP932 title / ASCII untouched / malformed fallback");
        for(String file:args)try(var channel=FileChannel.open(Path.of(file))){
            String title=LegacyId3Title.read(channel,0,channel.size());
            if(!"突然".equals(title))throw new AssertionError("Real title not decoded");
            System.out.println("PASS real source title (read-only)");
        }
    }
}
