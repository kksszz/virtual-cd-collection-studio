package jp.virtualcd.player;

/** The scanner owns camera permission and lifecycle; no camera runs during normal playback. */
public final class SyncQrCaptureActivity extends com.journeyapps.barcodescanner.CaptureActivity {
    @Override public void onCreate(android.os.Bundle state){super.onCreate(state);AutoStopSettings.track(this);}
    @Override protected void onDestroy(){AutoStopSettings.untrack(this);super.onDestroy();}
}
