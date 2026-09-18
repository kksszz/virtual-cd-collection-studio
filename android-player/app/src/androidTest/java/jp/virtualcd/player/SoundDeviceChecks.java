package jp.virtualcd.player;

import android.content.Context;
import androidx.media3.common.C;
import androidx.media3.common.audio.AudioProcessor;
import jp.virtualcd.player.audio.*;
import java.nio.*;

@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
final class SoundDeviceChecks {
    static void run(Context context)throws Exception{
        checkTuning(.75f,1f);checkTuning(1f,2f);checkTuning(1.25f,.5f);
        String namespace="sound-smoke-"+System.nanoTime();SoundPreferences store=new SoundPreferences(context,namespace);
        try{
            if(!store.read().faithful||!store.gapless())throw new AssertionError("Unsafe defaults");
            if(!store.playbackParameters().equals(androidx.media3.common.PlaybackParameters.DEFAULT))throw new AssertionError("Tuning defaults");
            store.saveTuning(125,3);var restoredTuning=new SoundPreferences(context,namespace);
            if(restoredTuning.speedPercent()!=125||restoredTuning.pitchSemitones()!=3||Math.abs(restoredTuning.playbackParameters().pitch-Math.pow(2,.25))>0.00001)throw new AssertionError("Independent speed/pitch persistence");
            store.saveTuning(999,-99);if(store.speedPercent()!=200||store.pitchSemitones()!=-12||store.playbackParameters().pitch!=.5f)throw new AssertionError("Tuning bounds");
            store.saveTuning(100,0);if(!store.playbackParameters().equals(androidx.media3.common.PlaybackParameters.DEFAULT))throw new AssertionError("Tuning reset");
            int[] gains={-12,-9,-6,-3,0,2,4,6,9,12};SoundConfig configured=new SoundConfig(false,5,true,true,true,true,83,gains);
            store.save(configured,false);SoundPreferences restored=new SoundPreferences(context,namespace);
            if(!configured.equals(restored.read())||restored.gapless())throw new AssertionError("Sound settings persistence");
            SoundConfig bypass=new SoundConfig(true,5,true,true,true,true,100,gains);SoundProcessor processor=new SoundProcessor(bypass);
            processor.configure(new AudioProcessor.AudioFormat(48000,2,C.ENCODING_PCM_16BIT));processor.flush();
            processor.queueInput(AudioProcessor.EMPTY_BUFFER);if(processor.getOutput().hasRemaining())throw new AssertionError("Empty input");
            ByteBuffer data=ByteBuffer.allocateDirect(4096).order(ByteOrder.LITTLE_ENDIAN);for(int i=0;i<2048;i++)data.putShort((short)(i*33));data.flip();
            byte[] expected=new byte[data.remaining()];data.duplicate().get(expected);processor.queueInput(data);ByteBuffer output=processor.getOutput();byte[] actual=new byte[output.remaining()];output.get(actual);
            if(!java.util.Arrays.equals(expected,actual)||data.hasRemaining())throw new AssertionError("PCM bypass or byte count");
            processor.setConfig(configured);
            for(int n=0;n<8;n++){ByteBuffer input=ByteBuffer.allocateDirect(4096).order(ByteOrder.LITTLE_ENDIAN);for(int i=0;i<2048;i++)input.putShort((short)(Math.sin(i*.2)*32000));input.flip();
                processor.queueInput(input);output=processor.getOutput().order(ByteOrder.LITTLE_ENDIAN);if(output.remaining()!=4096||input.hasRemaining())throw new AssertionError("Dropped PCM");
                if(n>1)while(output.hasRemaining())if(Math.abs((int)output.getShort())>32113)throw new AssertionError("Limiter overflow");}
            processor.setConfig(bypass);
            for(int n=0;n<4;n++){ByteBuffer input=ByteBuffer.wrap(expected);processor.queueInput(input);output=processor.getOutput();actual=new byte[output.remaining()];output.get(actual);}
            if(!java.util.Arrays.equals(expected,actual))throw new AssertionError("Live bypass does not settle");
            processor.queueEndOfStream();if(!processor.isEnded())throw new AssertionError("EOS");processor.reset();
            processor.configure(new AudioProcessor.AudioFormat(22050,1,C.ENCODING_PCM_16BIT));processor.flush();processor.queueInput(ByteBuffer.wrap(new byte[]{1,2}));if(processor.getOutput().remaining()!=2)throw new AssertionError("Format change");processor.reset();
        }finally{store.preferences.edit().clear().commit();}
    }
    private static void checkTuning(float speed,float pitch)throws Exception{
        var sonic=new androidx.media3.common.audio.SonicAudioProcessor();sonic.setSpeed(speed);sonic.setPitch(pitch);
        sonic.configure(new AudioProcessor.AudioFormat(48000,1,C.ENCODING_PCM_16BIT));sonic.flush(AudioProcessor.StreamMetadata.DEFAULT);
        ByteBuffer signal=ByteBuffer.allocateDirect(96000*2).order(ByteOrder.nativeOrder());for(int i=0;i<96000;i++)signal.putShort((short)(12000*Math.sin(2*Math.PI*1000*i/48000)));signal.flip();
        sonic.queueInput(signal);sonic.queueEndOfStream();var bytes=new java.io.ByteArrayOutputStream();
        while(true){ByteBuffer block=sonic.getOutput();if(!block.hasRemaining())break;byte[] data=new byte[block.remaining()];block.get(data);bytes.write(data);}
        ByteBuffer output=ByteBuffer.wrap(bytes.toByteArray()).order(ByteOrder.nativeOrder());int count=output.remaining()/2;
        if(Math.abs(count-96000/speed)>1000)throw new AssertionError("Speed duration: "+speed+" => "+count);
        int start=count/4,end=count*3/4,crossings=0;short previous=output.getShort(start*2);
        for(int i=start+1;i<end;i++){short sample=output.getShort(i*2);if(previous<=0&&sample>0)crossings++;previous=sample;}
        double hz=crossings*48000.0/(end-start);if(Math.abs(hz-1000*pitch)>1000*pitch*.04)throw new AssertionError("Pitch independent of tempo: "+hz);
        sonic.reset();
    }
}
