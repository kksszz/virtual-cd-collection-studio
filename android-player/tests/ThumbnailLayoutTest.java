import jp.virtualcd.player.library.ThumbnailLayout;
import java.util.Arrays;
public class ThumbnailLayoutTest {
    static void test(int w,int h,String mode,int... expected){
        if(!Arrays.equals(ThumbnailLayout.region(w,h,mode),expected))throw new AssertionError(w+"x"+h+" "+mode);
        System.out.println("PASS thumbnail "+w+"x"+h+" "+mode);
    }
    public static void main(String[] args){
        test(1700,828,"auto",872,0,828,828);
        test(1700,828,"left",0,0,828,828);
        test(1700,828,"full",0,0,1700,828);
        test(600,600,"auto",0,0,600,600);
        test(800,1200,"auto",0,0,800,1200);
        test(2400,600,"auto",0,0,2400,600);
        test(1,1,"right",0,0,1,1);
    }
}
