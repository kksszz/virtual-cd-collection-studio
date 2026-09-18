import jp.virtualcd.player.audio.*;

public final class SoundEngineTest {
    static SoundConfig config(boolean faithful,int mode,boolean norm,boolean clarity,boolean eq,boolean bass,int[] gains){return new SoundConfig(faithful,mode,norm,clarity,eq,bass,100,gains);}
    public static void main(String[] args){
        int[] all=new int[10];java.util.Arrays.fill(all,12);
        SoundEngine bypass=new SoundEngine(config(true,5,true,true,true,true,all),44100,2);
        for(int i=0;i<1000;i++){double a=Math.sin(i)*.99,b=Math.cos(i)*.99;double[] f={a,b};bypass.process(f);check(f[0]==a&&f[1]==b,"Faithful not exact");}
        SoundEngine flat=new SoundEngine(config(false,0,false,false,true,false,new int[10]),44100,2);double[] f={.999,-1};flat.process(f);check(f[0]==.999&&f[1]==-1,"Flat not exact");
        for(int rate:new int[]{8000,22050,44100,48000,96000})for(int mode=0;mode<6;mode++){
            SoundEngine e=new SoundEngine(config(false,mode,true,true,true,true,all),rate,2);
            double[] frame=new double[2];for(int i=0;i<rate;i++){frame[0]=Math.sin(i*.3)*.95;frame[1]=frame[0]*.7;e.process(frame);
                check(Double.isFinite(frame[0])&&Double.isFinite(frame[1])&&Math.abs(frame[0])<=.98000001&&Math.abs(frame[1])<=.98000001,"Peak safety");}
        }
        SoundEngine silent=new SoundEngine(config(false,5,true,true,true,true,all),44100,2);for(int i=0;i<44100;i++){double[] z={0,0};silent.process(z);check(z[0]==0&&z[1]==0,"Silence generates noise");}
        int[] boost=new int[10];boost[5]=6;double baseline=rms(new SoundEngine(config(false,0,false,false,false,false,boost),48000,1),1000,.03,48000,1);
        double raised=rms(new SoundEngine(config(false,0,false,false,true,false,boost),48000,1),1000,.03,48000,1);check(raised/baseline>1.95&&raised/baseline<2.05,"1 kHz +6 dB EQ");
        SoundEngine norm=new SoundEngine(config(false,0,true,false,false,false,new int[10]),48000,2);double[] stereo=new double[2];double energy=0;
        for(int i=0;i<48000*12;i++){double x=Math.sin(2*Math.PI*1000*i/48000)*.5;stereo[0]=x;stereo[1]=x*.5;norm.process(stereo);
            check(Math.abs(stereo[1]-stereo[0]*.5)<1e-10,"Stereo normalization unlinked");if(i>=48000*11)energy+=(stereo[0]*stereo[0]+stereo[1]*stereo[1])/2;}
        double db=10*Math.log10(energy/48000);check(Math.abs(db+18)<.15,"RMS target "+db);
        int[] original={1,2,3};SoundConfig immutable=config(false,0,false,false,true,false,original);original[0]=12;int[] copy=immutable.gains();copy[0]=-12;check(immutable.gain(0)==1,"Not immutable");
        System.out.println("PASS SoundEngine: bypass, all profiles/rates, silence, peak protection, EQ response, RMS/stereo, immutable settings");
    }
    static double rms(SoundEngine e,int hz,double amplitude,int rate,int channels){double sum=0;double[] frame=new double[channels];for(int i=0;i<rate;i++){java.util.Arrays.fill(frame,Math.sin(2*Math.PI*hz*i/rate)*amplitude);e.process(frame);if(i>=rate/2)sum+=frame[0]*frame[0];}return Math.sqrt(sum/(rate/2));}
    static void check(boolean condition,String message){if(!condition)throw new AssertionError(message);}
}
