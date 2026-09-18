package jp.virtualcd.player.audio;

import android.content.Context;
import androidx.media3.exoplayer.DefaultRenderersFactory;
import androidx.media3.exoplayer.audio.AudioSink;
import androidx.media3.exoplayer.audio.DefaultAudioSink;
import androidx.media3.common.audio.AudioProcessor;

@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class SoundRenderers extends DefaultRenderersFactory {
    private final SoundProcessor processor;
    public SoundRenderers(Context context,SoundProcessor processor){super(context);this.processor=processor;}
    @Override protected AudioSink buildAudioSink(Context context,boolean floatOutput,boolean playbackParameters){
        // Float/offload bypass custom processors. Always use the software PCM path for live DSP.
        return new DefaultAudioSink.Builder(context).setEnableFloatOutput(false).setEnableAudioOutputPlaybackParameters(false)
            .setAudioProcessors(new AudioProcessor[]{processor}).build();
    }
}
