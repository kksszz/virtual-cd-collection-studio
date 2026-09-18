package jp.virtualcd.player.case3d;

import android.app.*;
import android.content.Intent;
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
    private Button importButton,openButton,resetButton,discButton,obiButton,wrapButton;
    private CaseSurface surface;
    private CasePackage current,pending;
    private AlertDialog confirmation;
    private File packageFile;
    private boolean resumed,destroyed,busy;
    private String albumTitle;
    private MediaController player;
    private ListenableFuture<MediaController> connection;
    private TextView playingTitle;
    private Button previousTrack,playTrack,nextTrack,stopTrack;
    private final Player.Listener playbackListener=new Player.Listener(){
        @Override public void onEvents(Player player,Player.Events events){updatePlayback();}
    };
    @Override public void onCreate(Bundle state){super.onCreate(state);
        String source=getIntent().getStringExtra("album");albumTitle=getIntent().getStringExtra("title");
        if(source==null||!"content".equals(android.net.Uri.parse(source).getScheme())){finish();return;}
        try{File folder=new File(getFilesDir(),"cases3d");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("保存領域を作れません");packageFile=new File(folder,CasePackage.hash(source.getBytes(StandardCharsets.UTF_8))+".vcd3d");}catch(Exception ex){Toast.makeText(this,ex.getMessage(),Toast.LENGTH_LONG).show();finish();return;}
        LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(0xff101820);int pad=dp(8);root.setPadding(pad,pad,pad,pad);
        root.setOnApplyWindowInsetsListener((v,insets)->{v.setPadding(pad,pad+insets.getSystemWindowInsetTop(),pad,pad+insets.getSystemWindowInsetBottom());return insets;});
        HorizontalScrollView toolbar=new HorizontalScrollView(this);toolbar.setHorizontalScrollBarEnabled(false);root.addView(toolbar,new LinearLayout.LayoutParams(-1,-2));
        LinearLayout bar=new LinearLayout(this);toolbar.addView(bar);
        caseTool(bar,"close","3D画面を閉じる",this::finish);
        importButton=caseTool(bar,"import","3Dデータを取り込む",this::pick);
        openButton=caseTool(bar,"case-open","ケースを開く／閉じる",()->{if(surface!=null)surface.toggleOpen();});
        resetButton=caseTool(bar,"refresh","初期表示に戻す",()->{if(surface!=null)surface.reset();});
        discButton=caseTool(bar,"disc-out","CDを取り出す／戻す",()->{if(surface!=null)surface.toggleDisc();});
        obiButton=caseTool(bar,"obi","帯を外す／付ける",()->{if(surface!=null)surface.toggleObi();});
        wrapButton=caseTool(bar,"wrapping","包装を外す／付ける",()->{if(surface!=null)surface.toggleWrapping();});
        caption=new TextView(this);caption.setTextColor(0xffe3ebf5);caption.setTextSize(14);caption.setPadding(pad,pad,pad,pad);root.addView(caption);
        content=new FrameLayout(this);root.addView(content,new LinearLayout.LayoutParams(-1,0,1));TextView hint=new TextView(this);hint.setText("ドラッグ：回転　ピンチ／ホイール：拡大縮小\nダブルタップ／ダブルクリック：初期表示\n開くときは包装・帯を自動で外します");hint.setTextColor(0xff9fbdce);hint.setTextSize(12);hint.setGravity(Gravity.CENTER);root.addView(hint);
        addPlaybackBar(root);setContentView(root);empty();
        if(packageFile.isFile()||new File(packageFile.getPath()+".bak").isFile())load(null);
    }
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private Button caseTool(LinearLayout row,String icon,String label,Runnable action){
        Button b=button(row,"",action);var params=new LinearLayout.LayoutParams(dp(48),dp(44));params.setMargins(dp(2),dp(4),dp(2),dp(4));b.setLayoutParams(params);ControlIcon.button(b,icon,label);return b;
    }
    private void addPlaybackBar(LinearLayout root){
        LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);root.addView(row);
        playingTitle=new TextView(this);playingTitle.setTextSize(12);playingTitle.setTextColor(0xffe3ebf5);playingTitle.setMaxLines(2);playingTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);playingTitle.setPadding(dp(6),0,dp(6),0);
        row.addView(playingTitle,new LinearLayout.LayoutParams(0,-2,1));
        previousTrack=transport(row,"previous","前の曲",()->{if(player!=null)player.seekToPreviousMediaItem();});
        playTrack=transport(row,"play","再生",()->{if(player!=null){if(player.getPlayWhenReady()&&player.getPlaybackState()!=Player.STATE_IDLE&&player.getPlaybackState()!=Player.STATE_ENDED)player.pause();else{if(player.getPlaybackState()==Player.STATE_IDLE)player.prepare();if(player.getPlaybackState()==Player.STATE_ENDED)player.seekToDefaultPosition();player.play();}}});
        nextTrack=transport(row,"next","次の曲",()->{if(player!=null)player.seekToNextMediaItem();});
        stopTrack=transport(row,"stop","停止",()->{if(player!=null)player.stop();});
        playTrack.setSelected(true);updatePlayback();
    }
    private Button transport(LinearLayout row,String icon,String label,Runnable action){
        Button b=button(row,"",action);var params=new LinearLayout.LayoutParams(dp(44),dp(44));params.setMargins(dp(2),dp(4),dp(2),dp(4));b.setLayoutParams(params);ControlIcon.button(b,icon,label);return b;
    }
    private void updatePlayback(){
        if(playingTitle==null)return;
        boolean ready=player!=null&&player.isConnected()&&player.getCurrentMediaItem()!=null;
        boolean playing=ready&&player.getPlayWhenReady()&&player.getPlaybackState()!=Player.STATE_ENDED&&player.getPlaybackState()!=Player.STATE_IDLE;
        ControlIcon.button(playTrack,playing?"pause":"play",playing?"一時停止":"再生");
        playTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_PLAY_PAUSE));
        previousTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM)&&player.hasPreviousMediaItem());
        nextTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM)&&player.hasNextMediaItem());
        stopTrack.setEnabled(ready&&player.isCommandAvailable(Player.COMMAND_STOP));
        if(!ready){playingTitle.setText("再生曲なし");return;}
        var metadata=player.getMediaMetadata();CharSequence title=metadata.title;
        String text=(title==null||title.length()==0)?"曲名不明":title.toString();
        playingTitle.setText(text);playingTitle.setContentDescription("再生中の曲："+text);playingTitle.setTooltipText(text+(metadata.artist==null?"":"\n"+metadata.artist));
    }
    @Override protected void onStart(){super.onStart();if(playingTitle==null)return;
        final var pendingConnection=new MediaController.Builder(this,new SessionToken(this,new android.content.ComponentName(this,PlaybackService.class))).buildAsync();connection=pendingConnection;
        pendingConnection.addListener(()->{if(destroyed||connection!=pendingConnection)return;try{player=pendingConnection.get();player.addListener(playbackListener);updatePlayback();}catch(Exception error){playingTitle.setText("再生機能へ接続できません");}},getMainExecutor());
    }
    @Override protected void onStop(){
        if(player!=null)player.removeListener(playbackListener);
        if(connection!=null){MediaController.releaseFuture(connection);connection=null;}player=null;updatePlayback();super.onStop();
    }
    private Button button(LinearLayout row,String title,Runnable action){Button b=new Button(this);b.setText(title);jp.virtualcd.player.library.PlayerStyle.button(b);var params=new LinearLayout.LayoutParams(0,dp(44),1);params.setMargins(dp(3),dp(4),dp(3),dp(4));row.addView(b,params);b.setOnClickListener(v->action.run());return b;}
    private void empty(){caption.setText(albumTitle+" · 3D");TextView text=new TextView(this);text.setText("Windowsの3D画面から「モバイル3D出力」で\n書き出した .vcd3d を取り込んでください。\n\n音楽ファイルは変更しません。");text.setTextColor(0xffc1d0df);text.setGravity(Gravity.CENTER);content.addView(text,new FrameLayout.LayoutParams(-1,-1));buttons(false);}
    private void buttons(boolean loading){busy=loading;importButton.setEnabled(!loading);boolean ready=!loading&&surface!=null;openButton.setEnabled(ready);resetButton.setEnabled(ready);discButton.setEnabled(ready);obiButton.setEnabled(ready&&current.hasObi);wrapButton.setEnabled(ready);}
    private void pick(){if(busy)return;Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);startActivityForResult(intent,10);}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request==10&&result==RESULT_OK&&data!=null&&data.getData()!=null)load(data.getData());}
    private void load(android.net.Uri uri){buttons(true);caption.setText("3Dデータを読み込み中…");worker.execute(()->{try{
            byte[] bytes;try(InputStream input=uri==null?new AtomicFile(packageFile).openRead():getContentResolver().openInputStream(uri)){if(input==null)throw new IOException("ファイルを開けません");bytes=CasePackage.readBytes(input,CasePackage.MAX_BYTES);}
            CasePackage next=CasePackage.parse(bytes);runOnUiThread(()->{if(destroyed){next.close();return;}
                if(uri==null){show(next);return;}pending=next;
                confirmation=new AlertDialog.Builder(this).setTitle("このアルバムに3Dデータを設定しますか？").setMessage("対象："+albumTitle+"\n\n3Dデータ："+next.title+"\n"+next.artist+"\n\n既存の3Dデータは置き換えます。音楽・画像設定・お気に入りは変更しません。")
                    .setPositiveButton("取り込む",(d,w)->{pending=null;save(bytes,next);}).setNegativeButton("キャンセル",(d,w)->cancelImport()).setOnCancelListener(d->cancelImport()).show();
            });
        }catch(Exception ex){error(ex);}});
    }
    private void cancelImport(){if(pending!=null){pending.close();pending=null;}restoreCaption();buttons(false);}
    private void save(byte[] bytes,CasePackage next){worker.execute(()->{AtomicFile target=new AtomicFile(packageFile);FileOutputStream stream=null;try{stream=target.startWrite();stream.write(bytes);target.finishWrite(stream);runOnUiThread(()->{if(destroyed)next.close();else show(next);});}catch(Exception ex){target.failWrite(stream);next.close();error(ex);}});}
    private void error(Exception error){runOnUiThread(()->{if(destroyed)return;restoreCaption();buttons(false);new AlertDialog.Builder(this).setTitle("3Dデータを読み込めません").setMessage(error.getMessage()).setPositiveButton("閉じる",null).show();});}
    private void restoreCaption(){caption.setText(current==null?albumTitle+" · 3D":current.title+"\n"+current.artist);}
    private void show(CasePackage next){if(surface!=null){surface.onPause();content.removeView(surface);}if(current!=null)current.close();current=next;content.removeAllViews();surface=new CaseSurface(this,next);content.addView(surface,new FrameLayout.LayoutParams(-1,-1));if(resumed)surface.onResume();else surface.onPause();restoreCaption();buttons(false);}
    @Override protected void onResume(){super.onResume();resumed=true;if(surface!=null)surface.onResume();}
    @Override protected void onPause(){resumed=false;if(surface!=null)surface.onPause();super.onPause();}
    @Override protected void onDestroy(){destroyed=true;if(confirmation!=null)confirmation.dismiss();if(pending!=null)pending.close();worker.shutdown();if(surface!=null)surface.onPause();if(current!=null)current.close();super.onDestroy();}
}
