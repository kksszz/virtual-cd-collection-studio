package jp.virtualcd.player;

import android.app.*;
import android.content.Intent;
import android.net.Uri;
import android.os.*;
import android.widget.*;
import androidx.media3.common.*;
import jp.virtualcd.player.library.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
final class UiStartupLatencyChecks {
    interface Action {void run()throws Exception;}
    static void main(Action action)throws Exception{
        var done=new CountDownLatch(1);var failure=new AtomicReference<Throwable>();
        new Handler(Looper.getMainLooper()).post(()->{try{action.run();}catch(Throwable e){failure.set(e);}finally{done.countDown();}});
        if(!done.await(45,TimeUnit.SECONDS))throw new AssertionError("Main thread stalled >45s");
        if(failure.get()!=null)throw new AssertionError(failure.get());
    }
    static Object field(Object owner,String name)throws Exception{var f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);return f.get(owner);}
    static void method(Object owner,String name,Class<?>[] types,Object...args)throws Exception{var m=MainActivity.class.getDeclaredMethod(name,types);m.setAccessible(true);m.invoke(owner,args);}
    static String shell(Instrumentation test,String command)throws Exception{
        try(var input=new ParcelFileDescriptor.AutoCloseInputStream(test.getUiAutomation().executeShellCommand(command))){return new String(input.readAllBytes(),java.nio.charset.StandardCharsets.UTF_8).trim();}
    }
    static void run(Instrumentation test,Bundle result)throws Exception{run(test,result,false);}
    static void run(Instrumentation test,Bundle result,boolean doze)throws Exception{
        var c=test.getTargetContext();var tree=Uri.parse(c.getSharedPreferences("MainActivity",0).getString("tree",""));
        var matches=new ArrayList<AlbumLibrary.Album>();for(var a:AlbumLibrary.load(c,tree))if(a.name.contains("0083")&&!a.name.contains("MEMORY2")&&!a.name.contains("MEMORY 2"))matches.add(a);
        if(matches.size()!=1)throw new AssertionError("0083 album ambiguous");
        long started=SystemClock.elapsedRealtime();
        Activity activity=test.startActivitySync(new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        Player[] player={null};
        for(int i=0;i<300&&player[0]==null;i++){main(()->player[0]=(Player)field(activity,"player"));if(player[0]==null)Thread.sleep(100);}
        if(player[0]==null)throw new AssertionError("Controller unavailable");result.putLong("activityAndControllerMs",SystemClock.elapsedRealtime()-started);
        var oldItems=new ArrayList<MediaItem>();long[] oldPosition={0};int[] oldIndex={0};float[] oldVolume={1};
        main(()->{var p=player[0];for(int i=0;i<p.getMediaItemCount();i++)oldItems.add(p.getMediaItemAt(i));oldPosition[0]=p.getCurrentPosition();oldIndex[0]=p.getCurrentMediaItemIndex();oldVolume[0]=p.getVolume();p.pause();p.setVolume(0);});
        try{
            started=SystemClock.elapsedRealtime();main(()->method(activity,"loadAlbum",new Class<?>[]{Uri.class},matches.get(0).uri));
            var ready=new AtomicBoolean();
            for(int i=0;i<450&&!ready.get();i++){main(()->{var list=(ListView)field(activity,"tracks");ready.set(list.isEnabled()&&list.getCount()>0);});if(!ready.get())Thread.sleep(100);}
            if(!ready.get())throw new AssertionError("Track list timeout");result.putLong("albumToTracksMs",SystemClock.elapsedRealtime()-started);
            var done=new AtomicReference<CountDownLatch>();var requested=new AtomicLong();var delay=new AtomicLong();var error=new AtomicReference<Throwable>();
            Player.Listener listener=new Player.Listener(){
                @Override public void onIsPlayingChanged(boolean playing){if(playing&&done.get()!=null){delay.set(SystemClock.elapsedRealtime()-requested.get());done.get().countDown();}}
                @Override public void onPlayerError(PlaybackException ex){error.set(ex);if(done.get()!=null)done.get().countDown();}
            };
            main(()->player[0].addListener(listener));
            try{for(int pass=0;pass<2;pass++){
                if(pass==1){
                    main(()->{player[0].pause();activity.moveTaskToBack(true);});
                    if(doze){
                        shell(test,"input keyevent 223");
                        result.putString("forceIdle",shell(test,"dumpsys deviceidle force-idle deep"));
                        String idle=shell(test,"dumpsys deviceidle get deep");result.putString("idleBeforeWait",idle);
                        if(!"IDLE".equals(idle))throw new AssertionError("Deep idle not entered: "+idle);
                    }
                    Thread.sleep(30000);
                    if(doze){
                        result.putString("idleAfterWait",shell(test,"dumpsys deviceidle get deep"));
                        shell(test,"dumpsys deviceidle unforce");shell(test,"input keyevent 224");
                    }
                    main(()->c.startActivity(new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)));
                    Thread.sleep(500);
                }
                done.set(new CountDownLatch(1));final int current=pass;
                requested.set(SystemClock.elapsedRealtime());
                main(()->{result.putBoolean(current==0?"syncDuringTrackClick":"syncDuringResume",(boolean)field(activity,"syncChecking"));
                    if(current==0){var list=(ListView)field(activity,"tracks");list.performItemClick(null,0,0);}else ((Button)field(activity,"play")).performClick();});
                if(!done.get().await(45,TimeUnit.SECONDS))throw new AssertionError("UI playback timeout pass="+pass);
                if(error.get()!=null)throw new AssertionError(error.get());
                result.putLong(current==0?"trackClickToPlayingMs":doze?"doze30sResumeMs":"background30sResumeMs",delay.get());Thread.sleep(1000);
            }}finally{main(()->player[0].removeListener(listener));}
        }finally{
            if(doze){shell(test,"dumpsys deviceidle unforce");shell(test,"input keyevent 224");result.putString("idleAfterCleanup",shell(test,"dumpsys deviceidle get deep"));}
            main(()->{var p=player[0];p.pause();if(oldItems.isEmpty())p.clearMediaItems();else p.setMediaItems(oldItems,Math.max(0,oldIndex[0]),oldPosition[0]);p.setVolume(oldVolume[0]);activity.finish();});
        }
        result.putString("scope","Actual Activity + PlaybackService, muted, 30s "+(doze?"forced deep Doze":"background (not Doze)")+"; original queue restored paused");
    }
}
