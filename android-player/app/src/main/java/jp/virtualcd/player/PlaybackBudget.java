package jp.virtualcd.player;

/** Counts wall-clock playback time, not media position or playback speed. */
public final class PlaybackBudget {
    private long used,last;
    private boolean running;
    public PlaybackBudget(long restored,long now){used=Math.max(0,restored);last=now;}
    public void update(long now,boolean playing){if(running)used+=Math.max(0,now-last);last=now;running=playing;}
    public long used(){return used;}
    public boolean due(long limit){return used>=limit;}
    public void reset(long now){used=0;last=now;running=false;}
}
