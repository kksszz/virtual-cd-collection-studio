import jp.virtualcd.player.PlaybackBudget;
public final class PlaybackBudgetTest {
    private static void check(boolean value){if(!value)throw new AssertionError();}
    public static void main(String[] args){
        long hour=3600000;var b=new PlaybackBudget(0,0);
        b.update(100,false);check(b.used()==0);b.update(100,true);
        b.update(100+hour,false);check(b.used()==hour);
        b.update(100+8*hour,false);check(b.used()==hour);
        b.update(100+8*hour,true);b.update(100+10*hour,true);check(b.due(3*hour));
        b.reset(100+10*hour);check(!b.due(3*hour)&&b.used()==0);
        b.update(100+11*hour,true);check(b.used()==0);
        var restored=new PlaybackBudget(hour,0);restored.update(1000,false);check(restored.used()==hour);
        restored.update(1000,true);restored.update(61000,false);check(restored.used()==hour+60000);
        System.out.println("PASS playback-only wall time, pause, 3h boundary, expiry reset, process restore");
    }
}
