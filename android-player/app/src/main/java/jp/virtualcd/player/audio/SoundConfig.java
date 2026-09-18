package jp.virtualcd.player.audio;

import java.util.Arrays;

/** Immutable settings snapshot shared with the audio thread. */
public final class SoundConfig {
    public static final int[] FREQUENCIES={31,62,125,250,500,1000,2000,4000,8000,16000};
    public final boolean faithful,normalize,clarity,eq,bass;
    public final int mode,amount;
    private final int[] gains;
    private final boolean active;
    public SoundConfig(boolean faithful,int mode,boolean normalize,boolean clarity,boolean eq,boolean bass,int amount,int[] gains){
        this.faithful=faithful;this.mode=Math.max(0,Math.min(5,mode));this.normalize=normalize;this.clarity=clarity;this.eq=eq;this.bass=bass;
        this.amount=Math.max(0,Math.min(100,amount));this.gains=new int[10];
        for(int i=0;i<Math.min(10,gains.length);i++)this.gains[i]=Math.max(-12,Math.min(12,gains[i]));
        active=!faithful&&(this.mode!=0||normalize||clarity||(eq&&Arrays.stream(this.gains).anyMatch(g->g!=0))||(bass&&this.amount>0));
    }
    public int gain(int band){return gains[band];}
    public int[] gains(){return gains.clone();}
    public boolean active(){return active;}
    @Override public boolean equals(Object other){if(!(other instanceof SoundConfig))return false;SoundConfig c=(SoundConfig)other;
        return faithful==c.faithful&&mode==c.mode&&normalize==c.normalize&&clarity==c.clarity&&eq==c.eq&&bass==c.bass&&amount==c.amount&&Arrays.equals(gains,c.gains);}
    @Override public int hashCode(){return java.util.Objects.hash(faithful,mode,normalize,clarity,eq,bass,amount,Arrays.hashCode(gains));}
}
