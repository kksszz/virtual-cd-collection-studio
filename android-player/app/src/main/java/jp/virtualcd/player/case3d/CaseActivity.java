package jp.virtualcd.player.case3d;

import android.app.*;
import android.os.Bundle;
import android.util.AtomicFile;
import android.view.Gravity;
import android.widget.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.*;
import androidx.media3.common.Player;
import androidx.media3.session.MediaController;
import androidx.media3.session.SessionToken;
import com.google.common.util.concurrent.ListenableFuture;
import jp.virtualcd.player.ControlIcon;
import jp.virtualcd.player.PlaybackService;

/** Read-only viewer; package binding is local to the explicitly selected SAF album URI. */
public final class CaseActivity extends Activity {
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private FrameLayout content;
    private TextView caption;
    private Button openButton,resetButton,discButton,obiButton,wrapButton;
    private CaseSurface surface;
    private CasePackage current;
    private File packageFile;
    private boolean resumed,destroyed;
    private String albumTitle;
    private MediaController player;
    private ListenableFuture<MediaController> connection;
    private TextView playingTitle;
    private Button previousTrack,playTrack,nextTrack,stopTrack;
    private LinearLayout shell,body,bar,playbackRow,side;
    private HorizontalScrollView toolbar;
    private ScrollView sidebar;
    private TextView hint;
    private final java.util.List<Button> caseButtons=new java.util.ArrayList<>();
    private Boolean wide;
    private final Player.Listener playbackListener=new Player.Listener(){
        @Override public void onEvents(Player player,Player.Events events){updatePlayback();}
    };
    @Override public void onCreate(Bundle state){super.onCreate(state);
        jp.virtualcd.player.AutoStopSettings.track(this);
        String source=getIntent().getStringExtra("album");albumTitle=getIntent().getStringExtra("title");
        if(source==null||!"content".equals(android.net.Uri.parse(source).getScheme())){finish();return;}
        try{File folder=new File(getFilesDir(),"cases3d");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException(jp.virtualcd.player.LanguageStrings.text("保存領域を作れません","Unable to create storage"));packageFile=new File(folder,CasePackage.hash(source.getBytes(StandardCharsets.UTF_8))+".vcd3d");}catch(Exception ex){Toast.makeText(this,ex.getMessage(),Toast.LENGTH_LONG).show();finish();return;}
        shell=new LinearLayout(this);shell.setBackgroundColor(0xff101820);int pad=dp(6);
        shell.setPadding(pad,pad,pad,pad);
        shell.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(pad+insets.getSystemWindowInsetLeft(),pad+insets.getSystemWindowInsetTop(),pad+insets.getSystemWindowInsetRight(),pad+insets.getSystemWindowInsetBottom());return insets;});
        body=new LinearLayout(this);body.setOrientation(LinearLayout.VERTICAL);LinearLayout root=body;shell.addView(body,new LinearLayout.LayoutParams(0,-1,1));
        sidebar=new ScrollView(this);sidebar.setFillViewport(false);sidebar.setVisibility(android.view.View.GONE);shell.addView(sidebar,new LinearLayout.LayoutParams(dp(164),-1));side=new LinearLayout(this);side.setOrientation(LinearLayout.VERTICAL);sidebar.addView(side);
        toolbar=new HorizontalScrollView(this);toolbar.setHorizontalScrollBarEnabled(false);root.addView(toolbar,new LinearLayout.LayoutParams(-1,-2));
        bar=new LinearLayout(this);toolbar.addView(bar);
        caseTool(bar,"close",jp.virtualcd.player.LanguageStrings.text("3D画面を閉じる","Close 3D view"),this::finish);
        openButton=caseTool(bar,"case-open",jp.virtualcd.player.LanguageStrings.text("ケースを開く／閉じる","Open / close case"),()->{if(surface!=null)surface.toggleOpen();});
        resetButton=caseTool(bar,"refresh",jp.virtualcd.player.LanguageStrings.text("初期表示に戻す","Reset view"),()->{if(surface!=null)surface.reset();});
        discButton=caseTool(bar,"disc-out",jp.virtualcd.player.LanguageStrings.text("CDを取り出す／戻す","Take out / insert CD"),()->{if(surface!=null)surface.toggleDisc();});
        obiButton=caseTool(bar,"obi",jp.virtualcd.player.LanguageStrings.text("帯を外す／付ける","Remove / attach obi"),()->{if(surface!=null)surface.toggleObi();});
        wrapButton=caseTool(bar,"wrapping",jp.virtualcd.player.LanguageStrings.text("包装を外す／付ける","Remove / attach wrapping"),()->{if(surface!=null)surface.toggleWrapping();});
        caption=new TextView(this);caption.setTextColor(0xffe3ebf5);caption.setTextSize(14);caption.setPadding(pad,pad,pad,pad);root.addView(caption);
        content=new FrameLayout(this);root.addView(content,new LinearLayout.LayoutParams(-1,0,1));hint=new TextView(this);hint.setText(jp.virtualcd.player.LanguageStrings.text("1本指：回転　2本指スライド：移動\nピンチ／ホイール：拡大縮小　ダブルタップ：初期表示\n開いたCDの中心を押さえ、外周をもう1本の指で引くと取り出せます","One finger: rotate · Two fingers: pan\nPinch / wheel: zoom · Double tap: reset\nHold the center of the open CD and pull its edge with another finger to remove it"));hint.setTextColor(0xff9fbdce);hint.setTextSize(12);hint.setGravity(Gravity.CENTER);root.addView(hint);
        addPlaybackBar(root);setContentView(shell);empty();
        shell.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->adaptLayout(r-l>b-t));
        if(packageFile.isFile()||new File(packageFile.getPath()+".bak").isFile())load();
    }
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private static void detach(android.view.View v){if(v.getParent() instanceof android.view.ViewGroup)((android.view.ViewGroup)v.getParent()).removeView(v);}
    private void adaptLayout(boolean landscape){
        if(wide!=null&&wide==landscape)return;wide=landscape;
        // Leave the GL surface attached: rotation must not reload textures or reset the pose.
        detach(caption);detach(playingTitle);for(Button b:caseButtons)detach(b);
        Button[] transport={previousTrack,playTrack,nextTrack,stopTrack};for(Button b:transport)detach(b);
        side.removeAllViews();toolbar.setVisibility(landscape?android.view.View.GONE:android.view.View.VISIBLE);
        hint.setVisibility(landscape?android.view.View.GONE:android.view.View.VISIBLE);playbackRow.setVisibility(landscape?android.view.View.GONE:android.view.View.VISIBLE);sidebar.setVisibility(landscape?android.view.View.VISIBLE:android.view.View.GONE);
        caption.setMaxLines(landscape?2:Integer.MAX_VALUE);caption.setEllipsize(android.text.TextUtils.TruncateAt.END);caption.setTextSize(landscape?12:14);
        if(landscape){
            side.addView(caption,new LinearLayout.LayoutParams(-1,-2));
            GridLayout tools=new GridLayout(this);tools.setColumnCount(3);side.addView(tools);
            for(Button b:caseButtons)addGridButton(tools,b);
            side.addView(playingTitle,new LinearLayout.LayoutParams(-1,-2));
            GridLayout playback=new GridLayout(this);playback.setColumnCount(3);side.addView(playback);
            for(Button b:transport)addGridButton(playback,b);
        }else{
            body.addView(caption,1,new LinearLayout.LayoutParams(-1,-2));
            for(Button b:caseButtons){bar.addView(b,new LinearLayout.LayoutParams(dp(52),dp(52)));}
            playbackRow.addView(playingTitle,new LinearLayout.LayoutParams(0,-2,1));
            for(Button b:transport){var p=new LinearLayout.LayoutParams(dp(44),dp(44));p.setMargins(dp(2),dp(4),dp(2),dp(4));playbackRow.addView(b,p);}
        }
    }
    private void addGridButton(GridLayout grid,Button button){var p=new GridLayout.LayoutParams();p.width=dp(48);p.height=dp(44);p.setMargins(dp(2),dp(2),dp(2),dp(2));grid.addView(button,p);}
    private Button caseTool(LinearLayout row,String icon,String label,Runnable action){
        Button b=button(row,"",action);var params=new LinearLayout.LayoutParams(dp(48),dp(44));params.setMargins(dp(2),dp(4),dp(2),dp(4));b.setLayoutParams(params);ControlIcon.button(b,icon,label);caseButtons.add(b);return b;
    }
    private void addPlaybackBar(LinearLayout root){
        LinearLayout row=new LinearLayout(this);playbackRow=row;row.setGravity(Gravity.CENTER_VERTICAL);root.addView(row);
        playingTitle=new TextView(this);playingTitle.setTextSize(12);playingTitle.setTextColor(0xffe3ebf5);playingTitle.setMaxLines(2);playingTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);playingTitle.setPadding(dp(6),0,dp(6),0);
        row.addView(playingTitle,new LinearLayout.LayoutParams(0,-2,1));
        previousTrack=transport(row,"previous",jp.virtualcd.player.LanguageStrings.text("前の曲","Previous track"),()->{if(player!=null)player.seekToPreviousMediaItem();});
        playTrack=transport(row,"play",jp.virtualcd.player.LanguageStrings.text("再生","Play"),()->{if(player!=null){if(player.getPlayWhenReady()&&player.getPlaybackState()!=Player.STATE_IDLE&&player.getPlaybackState()!=Player.STATE_ENDED)player.pause();else{if(player.getPlaybackState()==Player.STATE_IDLE)player.prepare();if(player.getPlaybackState()==Player.STATE_ENDED)player.seekToDefaultPosition();player.play();}}});
        nextTrack=transport(row,"next",jp.virtualcd.player.LanguageStrings.text("次の曲","Next track"),()->{if(player!=null)player.seekToNextMediaItem();});
        stopTrack=transport(row,"stop",jp.virtualcd.player.LanguageStrings.text("停止","Stop"),()->{if(player!=null)player.stop();});
        playTrack.setSelected(true);updatePlayback();
    }
    private Button transport(LinearLayout row,String icon,String label,Runnable action){
        Button b=button(row,"",action);var params=new LinearLayout.LayoutParams(dp(44),dp(44));params.setMargins(dp(2),dp(4),dp(2),dp(4));b.setLayoutParams(params);ControlIcon.button(b,icon,label);return b;
    }
    private void updatePlayback(){
        if(playingTitle==null)return;
        boolean ready=player!=null&&player.isConnected()&&player.getCurrentMediaItem()!=null;
        boolean playing=ready&&player.getPlayWhenReady()&&player.getPlaybackState()!=Player.STATE_ENDED&&player.getPlaybackState()!=Player.STATE_IDLE;
        ControlIcon.button(playTrack,playing?"pause":"play",playing?jp.virtualcd.player.LanguageStrings.text("一時停止","Pause"):jp.virtualcd.player.LanguageStrings.text("再生","Play"));
        playTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_PLAY_PAUSE));
        previousTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM)&&player.hasPreviousMediaItem());
        nextTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM)&&player.hasNextMediaItem());
        stopTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_STOP));
        if(!ready){playingTitle.setText(jp.virtualcd.player.LanguageStrings.text("再生曲なし","Nothing playing"));return;}
        var metadata=player.getMediaMetadata();CharSequence title=metadata.title;
        String text=(title==null||title.length()==0)?jp.virtualcd.player.LanguageStrings.text("曲名不明","Unknown title"):title.toString();
        CharSequence artist=metadata.artist;
        if(artist==null||artist.toString().trim().isEmpty())artist=metadata.albumArtist;
        String label=text+(artist==null||artist.toString().trim().isEmpty()?"":"\n"+artist);
        playingTitle.setText(label);playingTitle.setContentDescription(jp.virtualcd.player.LanguageStrings.text("再生中の曲：","Current track: ")+label);playingTitle.setTooltipText(label);
    }
    @Override protected void onStart(){super.onStart();if(playingTitle==null)return;
        final var pendingConnection=new MediaController.Builder(this,new SessionToken(this,new android.content.ComponentName(this,PlaybackService.class))).buildAsync();connection=pendingConnection;
        pendingConnection.addListener(()->{if(destroyed||connection!=pendingConnection)return;try{player=pendingConnection.get();player.addListener(playbackListener);updatePlayback();}catch(Exception error){playingTitle.setText(jp.virtualcd.player.LanguageStrings.text("再生機能へ接続できません","Unable to connect to playback"));}},getMainExecutor());
    }
    @Override protected void onStop(){
        if(player!=null)player.removeListener(playbackListener);
        if(connection!=null){MediaController.releaseFuture(connection);connection=null;}player=null;updatePlayback();super.onStop();
    }
    private Button button(LinearLayout row,String title,Runnable action){Button b=new Button(this);b.setText(title);jp.virtualcd.player.library.PlayerStyle.button(b);var params=new LinearLayout.LayoutParams(0,dp(44),1);params.setMargins(dp(3),dp(4),dp(3),dp(4));row.addView(b,params);b.setOnClickListener(v->action.run());return b;}
    private void empty(){caption.setText(albumTitle+" · 3D");TextView text=new TextView(this);text.setText(jp.virtualcd.player.LanguageStrings.text("3DデータはPCからの同期で受け取ります。\n\nWindowsの「モバイル同期」でこのアルバムを選択し、\nAndroidの「設定 → PCから同期」から\n接続QRコードを読み取ってください。\n\n同期後、この3D画面を開き直すと表示されます。","Receive 3D data by syncing from your PC.\n\nSelect this album in Mobile Sync on Windows,\nthen open Settings → Sync from PC on Android\nand scan the connection QR code.\n\nReopen this 3D view after syncing."));text.setTextColor(0xffc1d0df);text.setGravity(Gravity.CENTER);content.addView(text,new FrameLayout.LayoutParams(-1,-1));buttons(false);}
    private void buttons(boolean loading){boolean ready=!loading&&surface!=null;openButton.setEnabled(ready);resetButton.setEnabled(ready);discButton.setEnabled(ready);obiButton.setEnabled(ready&&current.hasObi);wrapButton.setEnabled(ready);}
    private void load(){buttons(true);caption.setText(jp.virtualcd.player.LanguageStrings.text("3Dデータを読み込み中…","Loading 3D data…"));worker.execute(()->{try{
            byte[] bytes;try(InputStream input=new AtomicFile(packageFile).openRead()){bytes=CasePackage.readBytes(input,CasePackage.MAX_BYTES);}
            CasePackage next=CasePackage.parse(bytes);runOnUiThread(()->{if(destroyed)next.close();else show(next);});
        }catch(Exception ex){error(ex);}});
    }
    private void error(Exception error){runOnUiThread(()->{if(destroyed)return;restoreCaption();buttons(false);new AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("3Dデータを読み込めません","Unable to load 3D data")).setMessage(error.getMessage()).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).show();});}
    private void restoreCaption(){caption.setText(current==null?albumTitle+" · 3D":current.title+"\n"+current.artist);}
    private void show(CasePackage next){if(surface!=null){surface.onPause();content.removeView(surface);}if(current!=null)current.close();current=next;content.removeAllViews();surface=new CaseSurface(this,next);content.addView(surface,new FrameLayout.LayoutParams(-1,-1));if(resumed)surface.onResume();else surface.onPause();restoreCaption();buttons(false);}
    @Override protected void onResume(){super.onResume();resumed=true;if(surface!=null)surface.onResume();}
    @Override protected void onPause(){resumed=false;if(surface!=null)surface.onPause();super.onPause();}
    @Override protected void onDestroy(){destroyed=true;worker.shutdown();if(surface!=null)surface.onPause();if(current!=null)current.close();super.onDestroy();}
}
