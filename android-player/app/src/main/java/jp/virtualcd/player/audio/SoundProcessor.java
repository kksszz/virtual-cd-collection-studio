package jp.virtualcd.player.audio;

import androidx.media3.common.C;
import androidx.media3.common.audio.BaseAudioProcessor;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Kept in the PCM pipeline even during bypass, so settings can change without stopping playback. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class SoundProcessor extends BaseAudioProcessor {
    private volatile SoundConfig requested;
    private SoundConfig applied;
    private SoundEngine engine,previous;
    private double[] frame,oldFrame;
    private int fadeRemaining,fadeLength;
    public SoundProcessor(SoundConfig initial){requested=initial;}
    public void setConfig(SoundConfig config){requested=config;}
    @Override protected AudioFormat onConfigure(AudioFormat format)throws UnhandledAudioFormatException{
        if(format.encoding!=C.ENCODING_PCM_16BIT)throw new UnhandledAudioFormatException(format);
        return format;
    }
    @Override protected void onFlush(){
        if(inputAudioFormat.sampleRate<=0||inputAudioFormat.channelCount<=0)return;
        applied=requested;engine=new SoundEngine(applied,inputAudioFormat.sampleRate,inputAudioFormat.channelCount);previous=null;
        frame=new double[inputAudioFormat.channelCount];oldFrame=frame.clone();fadeRemaining=0;fadeLength=Math.max(1,inputAudioFormat.sampleRate/50);
    }
    @Override public void queueInput(ByteBuffer input){
        // Media3 supplies EMPTY_BUFFER while draining at track boundaries.
        if(!input.hasRemaining())return;
        SoundConfig next=requested;
        // Finish a 20 ms crossfade before starting another, even during fast slider movement.
        if(!next.equals(applied)&&fadeRemaining==0){previous=engine;engine=new SoundEngine(next,inputAudioFormat.sampleRate,inputAudioFormat.channelCount);
            engine.inheritLevels(previous);applied=next;fadeRemaining=fadeLength;}
        ByteBuffer out=replaceOutputBuffer(input.remaining()).order(ByteOrder.LITTLE_ENDIAN);input.order(ByteOrder.LITTLE_ENDIAN);
        if(!engine.active()&&fadeRemaining==0){out.put(input);out.flip();return;}
        int channels=inputAudioFormat.channelCount;
        while(input.remaining()>=channels*2){
            for(int c=0;c<channels;c++)oldFrame[c]=frame[c]=input.getShort()/32768.0;
            engine.process(frame);
            if(fadeRemaining>0){previous.process(oldFrame);double mix=1-(double)fadeRemaining/fadeLength;
                for(int c=0;c<channels;c++)frame[c]=oldFrame[c]+(frame[c]-oldFrame[c])*mix;
                if(--fadeRemaining==0)previous=null;
            }
            for(int c=0;c<channels;c++)out.putShort((short)Math.max(-32768,Math.min(32767,Math.round(frame[c]*32768))));
        }
        out.flip();
    }
    @Override protected void onReset(){engine=previous=null;applied=null;frame=oldFrame=null;fadeRemaining=0;}
}
