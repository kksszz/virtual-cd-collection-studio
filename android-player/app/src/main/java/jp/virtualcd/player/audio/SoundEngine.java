package jp.virtualcd.player.audio;

/** Frame-based DSP; no Android dependencies, allocation-free processing. Source files are never written.
 * Windows listening profiles ported to PCM with linked-channel normalization and peak protection. */
public final class SoundEngine {
    private static final double[][] PROFILES={
        {0,0,0,0,0,1,1,1,0,0,1},
        {.5,-.4,.3,.7,.012,.82,1.3,.98,0,0,1},
        {.9,-.8,.6,1.3,.022,.74,1.6,.97,0,0,1},
        {1.4,-1.3,1,2.1,.036,.66,2,.95,0,0,1},
        {3.2,-2.4,2.7,4.6,.095,.56,2.8,.88,.06,.08,1.08},
        {2,-1.7,2.2,3.8,.070,.62,2.2,.86,.22,.20,1.24}};
    private final SoundConfig config;
    private final int rate,channels,window;
    private final Biquad[][] tone,eq,clarity;
    private final Biquad[] exciter,sub,bass;
    private final double[] env,slow,clarityEnv,bassEnv,profile;
    private final double attack,release,slowStep,clarityAttack,clarityRelease,bassGain;
    private double energy,smoothedEnergy,gainDb,targetDb,limiterGain=1;
    private int frames;
    private boolean hasLevel;
    public SoundEngine(SoundConfig config,int rate,int channels){
        if(rate<1000||channels<1)throw new IllegalArgumentException("Invalid PCM format");
        this.config=config;this.rate=rate;this.channels=channels;window=Math.max(1,rate/10);profile=PROFILES[config.mode];
        tone=new Biquad[channels][4];eq=new Biquad[channels][10];clarity=new Biquad[channels][4];
        exciter=new Biquad[channels];sub=new Biquad[channels];bass=new Biquad[channels];
        env=new double[channels];slow=new double[channels];clarityEnv=new double[channels];bassEnv=new double[channels];
        int[] tf={90,360,2600,9000},cf={90,360,2600,8200};double[] q={.75,.85,.9,.7},cg={2.4,-1.1,2,1.2};
        for(int c=0;c<channels;c++){
            for(int i=0;i<4;i++){tone[c][i]=Biquad.peak(rate,tf[i],q[i],profile[i]);clarity[c][i]=Biquad.peak(rate,cf[i],i==3?.75:q[i],cg[i]);}
            for(int i=0;i<10;i++)eq[c][i]=Biquad.peak(rate,SoundConfig.FREQUENCIES[i],.9,config.gain(i));
            exciter[c]=Biquad.pass(rate,4800,.707,true);sub[c]=Biquad.pass(rate,29,.707,true);bass[c]=Biquad.pass(rate,145,.78,false);
        }
        attack=Math.exp(-1.0/(.008*rate));release=Math.exp(-1.0/(.180*rate));slowStep=Math.exp(-1.0/(.550*rate));
        clarityAttack=Math.exp(-1.0/(.010*rate));clarityRelease=Math.exp(-1.0/(.260*rate));bassGain=Math.pow(10,config.amount/200.0)-1;
    }
    public boolean active(){return config.active();}
    public void inheritLevels(SoundEngine old){if(old==null)return;
        if(config.normalize&&old.config.normalize){gainDb=old.gainDb;targetDb=old.targetDb;smoothedEnergy=old.smoothedEnergy;hasLevel=old.hasLevel;}
        limiterGain=old.limiterGain;
    }
    /** One complete interleaved frame. Same gain for all channels preserves stereo balance. */
    public void process(double[] frame){
        if(!active())return;
        double norm=1;
        if(config.normalize){
            for(int c=0;c<channels;c++)energy+=frame[c]*frame[c];
            if(++frames>=window){double measured=energy/(frames*channels);if(measured>.00001){
                smoothedEnergy=hasLevel?smoothedEnergy*.95+measured*.05:measured;hasLevel=true;
                targetDb=clamp(-18-10*Math.log10(smoothedEnergy),-18,9);}
                energy=0;frames=0;
            }
            gainDb+=clamp(targetDb-gainDb,-12.0/rate,1.5/rate);norm=Math.pow(10,gainDb/20);
        }
        for(int c=0;c<channels;c++){
            double x=frame[c]*norm;
            if(config.mode!=0){
                for(Biquad filter:tone[c])x=filter.apply(x);
                x+=Math.tanh(exciter[c].apply(x)*3.2)/3.2*profile[4];
                double level=Math.abs(x),co=level>env[c]?attack:release;env[c]=co*env[c]+(1-co)*level;slow[c]=slowStep*slow[c]+(1-slowStep)*level;
                if(slow[c]<.28)x*=1+profile[8]*(1-slow[c]/.28);
                if(env[c]>slow[c]*1.18)x*=1+clamp(env[c]/Math.max(slow[c],.001)-1.18,0,1)*profile[9];
                if(env[c]>profile[5])x*=(profile[5]+(env[c]-profile[5])/profile[6])/Math.max(env[c],.000001);
                x*=profile[7];
            }
            if(config.eq)for(Biquad filter:eq[c])x=filter.apply(x);
            if(config.bass&&config.amount>0){double clean=sub[c].apply(x),band=bass[c].apply(clean),level=Math.abs(band),co=level>bassEnv[c]?attack:release;
                bassEnv[c]=co*bassEnv[c]+(1-co)*level;double gate=clamp((bassEnv[c]-.0015)/.0045,0,1);x=clean+band*bassGain*gate*gate*(3-2*gate);}
            if(config.clarity){for(Biquad filter:clarity[c])x=filter.apply(x);double level=Math.abs(x),co=level>clarityEnv[c]?clarityAttack:clarityRelease;
                clarityEnv[c]=co*clarityEnv[c]+(1-co)*level;double e=clarityEnv[c];if(e>.003)x*=1.62*(e<=.20?e:.20+(e-.20)/4)/e;}
            frame[c]=Double.isFinite(x)?x:0;
        }
        if(channels==2&&config.mode!=0){double mid=(frame[0]+frame[1])*.5,side=(frame[0]-frame[1])*.5*profile[10];frame[0]=mid+side;frame[1]=mid-side;}
        double peak=0;for(int c=0;c<channels;c++)peak=Math.max(peak,Math.abs(frame[c]));
        double desired=peak>.98?.98/peak:1;
        limiterGain=desired<limiterGain?desired:Math.min(desired,limiterGain+1.0/(rate*.15));
        for(int c=0;c<channels;c++)frame[c]*=limiterGain;
    }
    private static double clamp(double x,double lo,double hi){return Math.max(lo,Math.min(hi,x));}
    private static final class Biquad {
        double b0=1,b1,b2,a1,a2,z1,z2;
        double apply(double x){double y=b0*x+z1;z1=b1*x-a1*y+z2;z2=b2*x-a2*y;return y;}
        static Biquad peak(int rate,double hz,double q,double db){Biquad f=new Biquad();if(hz>=rate*.49||db==0)return f;
            double w=2*Math.PI*hz/rate,alpha=Math.sin(w)/(2*q),a=Math.pow(10,db/40),cos=Math.cos(w),a0=1+alpha/a;
            f.b0=(1+alpha*a)/a0;f.b1=-2*cos/a0;f.b2=(1-alpha*a)/a0;f.a1=-2*cos/a0;f.a2=(1-alpha/a)/a0;return f;}
        static Biquad pass(int rate,double hz,double q,boolean high){Biquad f=new Biquad();if(hz>=rate*.49)return f;
            double w=2*Math.PI*hz/rate,co=Math.cos(w),alpha=Math.sin(w)/(2*q),a0=1+alpha;
            f.b0=(high?1+co:1-co)/2/a0;f.b1=(high?-(1+co):1-co)/a0;f.b2=f.b0;f.a1=-2*co/a0;f.a2=(1-alpha)/a0;return f;}
    }
}
