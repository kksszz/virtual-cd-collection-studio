package jp.virtualcd.player;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import android.view.View;
import android.widget.*;
import androidx.media3.common.*;
import androidx.media3.session.MediaController;
import androidx.media3.session.SessionToken;
import com.google.common.util.concurrent.ListenableFuture;
import jp.virtualcd.player.archive.StoredZipIndex;
import jp.virtualcd.player.archive.ZipTrackDataSource;
import java.util.*;
import java.util.concurrent.Executors;
import jp.virtualcd.player.library.*;

/** Simple prototype UI. Archive access and playback intentionally do not depend on a 2D/3D view. */
public final class MainActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final java.util.concurrent.ExecutorService syncWorker=Executors.newSingleThreadExecutor();
    private boolean syncChecking;
    private boolean syncDialogOpen;
    private final Runnable syncTick=new Runnable(){public void run(){checkMobileSync();handler.postDelayed(this,15000);}};
    @Override protected void onResume(){super.onResume();handler.removeCallbacks(syncTick);handler.post(syncTick);}
    @Override protected void onPause(){handler.removeCallbacks(syncTick);super.onPause();}
    private void checkMobileSync(){
        if(libraryTree==null||restoringLibrary||scanning||syncChecking||syncDialogOpen)return;syncChecking=true;
        final Uri tree=libraryTree;final int job=scanGeneration;final var base=library;var snapshot=new ArrayList<>(base);
        syncWorker.execute(()->{try{
            String endpoint=getPreferences(MODE_PRIVATE).getString("syncEndpoint","");
            final boolean[] transferStarted={false};
            if(!endpoint.isEmpty())try{SyncDownload.pull(this,tree,endpoint,s->{transferStarted[0]=true;showSyncProgress(s);});}catch(Exception ex){android.util.Log.w("MobileSync",jp.virtualcd.player.LanguageStrings.text("PC同期は次回再試行します","PC sync will retry next time"),ex);if(transferStarted[0])showSyncProgress(jp.virtualcd.player.LanguageStrings.text("同期を中断しました。次回再試行します。\n","Sync interrupted. Will retry next time.\n")+ex.getMessage());}
            var updated=MobileSync.refresh(this,tree,snapshot);handler.post(()->{
            syncChecking=false;if(isDestroyed()||job!=scanGeneration||!canApplyLibrarySync(base,library,restoringLibrary,scanning))return;
            boolean changed=library.size()!=updated.size();if(!changed){var previous=new HashSet<String>();for(var a:library)previous.add(a.key());for(var a:updated)if(!previous.contains(a.key())){changed=true;break;}}
            library=updated;if(libraryVisible&&changed)albumAdapter.setAlbums(library,search.getText().toString());
            refreshAlbumFavorite();tracks.invalidateViews();albumAdapter.notifyDataSetChanged();
        });}catch(Exception ex){handler.post(()->{syncChecking=false;if(!isDestroyed())status.setText(jp.virtualcd.player.LanguageStrings.text("同期を反映できません。前のデータを保持しています: ","Unable to apply sync. Previous data is preserved: ")+ex.getMessage());});}});
    }
    static boolean canApplyLibrarySync(List<?> base,List<?> current,boolean restoring,boolean scanning){
        // Even an equal-sized replacement invalidates a snapshot taken before a restore/scan.
        return !restoring&&!scanning&&base==current;
    }
    private final java.util.concurrent.ExecutorService worker=Executors.newSingleThreadExecutor();
    private final java.util.concurrent.ExecutorService coverWorker=Executors.newSingleThreadExecutor();
    private final java.util.concurrent.ExecutorService cacheWorker=Executors.newSingleThreadExecutor();
    private java.util.concurrent.Future<?> cacheJob;
    private java.util.concurrent.Future<?> albumJob,coverJob;
    private MediaController player;
    private ListenableFuture<MediaController> connection;
    private TextView status, now, time;
    private SyncStatusView syncStatus;
    private void showSyncProgress(String message){handler.post(()->{if(!isDestroyed()){syncStatus.showProgress(message);}});}
    private Button play, choose;
    private Button returnToPlaying;
    private SeekBar seek;
    private ListView tracks;
    private List<MediaItem> playlist=Collections.emptyList();
    private int generation;
    private boolean dragging;
    private AlbumAdapter albumAdapter;
    private GridView albums;
    private LinearLayout rootLayout,trackPane,playbackPane,playbackInfo,playbackControls;
    private LinearLayout screenLayout,headingRow,nowPlayingRow,sideNavigation,sideColumns;
    private ScrollView sideRail;
    private final java.util.List<Button> navigationButtons=new ArrayList<>();
    private FrameLayout headerTitleSlot;
    private Boolean wideLayout;
    private EditText search;
    private Button folder,refresh,sortButton;
    private Uri libraryTree;
    private List<AlbumLibrary.Album> library=Collections.emptyList();
    private final java.util.concurrent.ExecutorService scanner=Executors.newSingleThreadExecutor();
    private java.util.concurrent.Future<?> scanJob;
    private int scanGeneration;
    private boolean libraryVisible=true,scanning,restoringLibrary;
    private LinearLayout albumHeader;
    private ImageView albumCover;
    private TextView albumTitle,albumInfo;
    private TextView screenTitle;
    private Button albumStar,backToAlbums,historyButton,favoritesButton;
    private ArtworkGallery gallery;
    private ListeningState listening;
    private Button shuffle,repeat;
    private Uri selectedAlbum;
    private String selectedAlbumTitle="";
    private boolean resumeFocusAllowed=true;
    private android.app.Dialog savedDialog;
    private android.app.Dialog soundDialog;
    private android.app.Dialog propertiesDialog;
    @Player.RepeatMode private int repeatIconMode=Player.REPEAT_MODE_OFF;
    private boolean playIconPlaying;
    private String highlightedId;
    private final Runnable tick=new Runnable() {public void run(){ updatePlayer();handler.postDelayed(this,500);}};
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        handler.post(()->TimerNotice.showPending(this));
        AutoStopSettings.track(this);
        listening=new ListeningState(this);
        var layout=new LinearLayout(this);rootLayout=layout; layout.setOrientation(LinearLayout.VERTICAL);
        screenLayout=new LinearLayout(this);screenLayout.setOrientation(LinearLayout.HORIZONTAL);screenLayout.setBackgroundColor(0xff15191f);
        screenLayout.addView(layout,new LinearLayout.LayoutParams(0,-1,1));
        sideRail=new ScrollView(this);sideRail.setFillViewport(true);sideRail.setVisibility(View.GONE);
        sideColumns=new LinearLayout(this);sideColumns.setGravity(android.view.Gravity.CENTER_VERTICAL);sideRail.addView(sideColumns,new ScrollView.LayoutParams(-1,-2));
        sideNavigation=new LinearLayout(this);sideNavigation.setOrientation(LinearLayout.VERTICAL);sideColumns.addView(sideNavigation,new LinearLayout.LayoutParams(PlayerStyle.dp(this,50),-2));
        screenLayout.addView(sideRail,new LinearLayout.LayoutParams(PlayerStyle.dp(this,108),-1));
        layout.setBackgroundColor(0xff15191f);
        screenLayout.setOnApplyWindowInsetsListener((v,insets)->{
            int edge=PlayerStyle.dp(this,Boolean.TRUE.equals(wideLayout)?8:16);
            v.setPadding(edge+insets.getSystemWindowInsetLeft(),edge+insets.getSystemWindowInsetTop(),edge+insets.getSystemWindowInsetRight(),edge+insets.getSystemWindowInsetBottom());return insets;});
        var heading=new LinearLayout(this);headingRow=heading;heading.setBaselineAligned(false);heading.setGravity(android.view.Gravity.CENTER_VERTICAL);layout.addView(heading,new LinearLayout.LayoutParams(-1,PlayerStyle.dp(this,48)));
        backToAlbums=headerButton(heading,"back",jp.virtualcd.player.LanguageStrings.text("アルバム一覧へ戻る","Back to albums"),()->showLibrary());
        screenTitle=new TextView(this);screenTitle.setText(jp.virtualcd.player.LanguageStrings.text("アルバム","Albums"));screenTitle.setTextSize(18);screenTitle.setTextColor(0xffe8edf5);
        screenTitle.setIncludeFontPadding(false);screenTitle.setGravity(android.view.Gravity.CENTER_VERTICAL);screenTitle.setSingleLine(true);screenTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        screenTitle.setPadding(PlayerStyle.dp(this,12),0,PlayerStyle.dp(this,8),0);
        var titleSlot=new FrameLayout(this);headerTitleSlot=titleSlot;titleSlot.addView(screenTitle,new FrameLayout.LayoutParams(-1,-1));heading.addView(titleSlot,new LinearLayout.LayoutParams(0,-1,1));
        sortButton=headerButton(heading,"sort",jp.virtualcd.player.LanguageStrings.text("アルバム一覧の並び順","Album sort order"),this::showAlbumOrder);
        historyButton=headerButton(heading,"history",jp.virtualcd.player.LanguageStrings.text("再生履歴","Playback history"),()->showSaved("history"));
        favoritesButton=headerButton(heading,"star",jp.virtualcd.player.LanguageStrings.text("お気に入り一覧","Favorites"),()->showFavorites());
        refresh=headerButton(heading,"refresh",jp.virtualcd.player.LanguageStrings.text("このアルバムを再読込","Reload this album"),()->{if(selectedAlbum!=null)loadAlbum(selectedAlbum,null,true);});
        headerButton(heading,"sound",jp.virtualcd.player.LanguageStrings.text("音の設定","Sound settings"),this::showSoundSettings);
        Button settings=headerButton(heading,"settings",jp.virtualcd.player.LanguageStrings.text("設定","Settings"),()->{});settings.setOnClickListener(v->showSettings(settings));
        for(int i=0;i<heading.getChildCount();i++)if(heading.getChildAt(i) instanceof Button)navigationButtons.add((Button)heading.getChildAt(i));
        backToAlbums.setVisibility(View.GONE);refresh.setVisibility(View.GONE);
        // The file/folder actions live in Settings; keep their busy-state controls off the main screen.
        var libraryControls=new LinearLayout(this);
        choose=button(libraryControls,jp.virtualcd.player.LanguageStrings.text("ファイル","File"),()->{});
        choose.setContentDescription(jp.virtualcd.player.LanguageStrings.text("音楽ファイル・ZIPを選択","Choose music file or ZIP"));choose.setTooltipText(jp.virtualcd.player.LanguageStrings.text("音楽ファイル・ZIPを選択","Choose music file or ZIP"));
        choose.setOnClickListener(v->{Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE);
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);startActivityForResult(i,1);});
        folder=button(libraryControls,jp.virtualcd.player.LanguageStrings.text("フォルダー","Folder"),()->{
            Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION|Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
            if(libraryTree!=null)i.putExtra(android.provider.DocumentsContract.EXTRA_INITIAL_URI,libraryTree);
            startActivityForResult(i,2);
        });
        status=label(jp.virtualcd.player.LanguageStrings.text("設定からSDカードの音楽フォルダーを選択してください。","Choose your SD card music folder in Settings."),12);layout.addView(status);
        syncStatus=new SyncStatusView(this);syncStatus.setVisibility(View.GONE);layout.addView(syncStatus);
        syncStatus.setOnClickListener(v->new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("PC同期の進捗","PC sync progress")).setMessage(syncStatus.getText()).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).setNeutralButton(jp.virtualcd.player.LanguageStrings.text("表示を隠す","Hide controls"),(d,w)->syncStatus.setVisibility(View.GONE)).show());
        search=new EditText(this);search.setSingleLine(true);search.setTextColor(0xffeef4fa);search.setHintTextColor(0xff99b5c9);search.setHint(jp.virtualcd.player.LanguageStrings.text("アルバム名で検索","Search album titles"));layout.addView(search);
        status.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){updateStatusVisibility();}public void afterTextChanged(android.text.Editable e){}});
        status.setOnClickListener(v->new android.app.AlertDialog.Builder(this).setMessage(status.getText()).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).show());
        albumAdapter=new AlbumAdapter(this);albums=new GridView(this);albums.setNumColumns(1);albums.setStretchMode(GridView.STRETCH_COLUMN_WIDTH);albums.setHorizontalSpacing(PlayerStyle.dp(this,12));albums.setAdapter(albumAdapter);layout.addView(albums,new LinearLayout.LayoutParams(-1,0,1));
        albums.setOnItemClickListener((p,v,index,id)->loadAlbum(albumAdapter.getItem(index).uri));
        albums.setOnItemLongClickListener((p,v,index,id)->{albumAdapter.chooseThumbnail(index);return true;});
        search.addTextChangedListener(new android.text.TextWatcher(){
            public void beforeTextChanged(CharSequence s,int start,int count,int after){}
            public void onTextChanged(CharSequence s,int start,int before,int count){albumAdapter.filter(s.toString());}
            public void afterTextChanged(android.text.Editable e){}
        });
        trackPane=new LinearLayout(this);trackPane.setOrientation(LinearLayout.VERTICAL);trackPane.setVisibility(View.GONE);layout.addView(trackPane,new LinearLayout.LayoutParams(-1,0,1));
        albumHeader=new LinearLayout(this);albumHeader.setOrientation(LinearLayout.VERTICAL);
        albumHeader.setGravity(android.view.Gravity.CENTER_HORIZONTAL);albumHeader.setVisibility(View.GONE);
        albumCover=new ImageView(this);albumCover.setScaleType(ImageView.ScaleType.FIT_CENTER);
        albumCover.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->{
            int inset=Math.round((r-l)*0.075f);
            if(albumCover.getPaddingLeft()!=inset)albumCover.setPadding(inset,0,inset,0);
        });
        albumCover.setContentDescription(jp.virtualcd.player.LanguageStrings.text("アルバムのジャケット","Album artwork"));
        var artworkRow=new LinearLayout(this);artworkRow.setGravity(android.view.Gravity.CENTER_VERTICAL);
        // Balance the right toolbar so the artwork stays on the screen centreline.
        artworkRow.addView(new android.view.View(this),new LinearLayout.LayoutParams(PlayerStyle.dp(this,56),1));
        artworkRow.addView(albumCover,new LinearLayout.LayoutParams(0,-1,1));
        var artworkSideScroll=new ScrollView(this);artworkSideScroll.setFillViewport(true);artworkSideScroll.setVerticalScrollBarEnabled(false);
        var artworkSide=new LinearLayout(this);artworkSide.setOrientation(LinearLayout.VERTICAL);artworkSide.setGravity(android.view.Gravity.CENTER);
        artworkSideScroll.addView(artworkSide,new ScrollView.LayoutParams(-1,-2));
        artworkRow.addView(artworkSideScroll,new LinearLayout.LayoutParams(PlayerStyle.dp(this,56),-1));
        albumHeader.addView(artworkRow,new LinearLayout.LayoutParams(-1,0,1));
        albumTitle=label("",18);albumTitle.setGravity(android.view.Gravity.CENTER);albumTitle.setMaxLines(2);
        albumInfo=label("",13);albumInfo.setGravity(android.view.Gravity.CENTER);albumInfo.setMaxLines(2);
        albumStar=FavoriteButton.create(this);
        Button case3d=button(artworkSide,"3D",()->{if(selectedAlbum!=null&&!selectedAlbumTitle.isEmpty())startActivity(new Intent(this,jp.virtualcd.player.case3d.CaseActivity.class).putExtra("album",selectedAlbum.toString()).putExtra("title",selectedAlbumTitle));});
        case3d.setLayoutParams(new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44)));ControlIcon.button(case3d,"case3d",jp.virtualcd.player.LanguageStrings.text("このアルバムの3Dケース","3D case for this album"));
        Button jacket=button(artworkSide,jp.virtualcd.player.LanguageStrings.text("ジャケット","Artwork"),this::openJacketGallery);
        var jacketParams=new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44));jacketParams.setMargins(0,PlayerStyle.dp(this,6),0,PlayerStyle.dp(this,6));jacket.setLayoutParams(jacketParams);ControlIcon.button(jacket,"booklet",jp.virtualcd.player.LanguageStrings.text("ジャケット・ブックレットを読む","Read artwork / booklet"));
        artworkSide.addView(albumStar,new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,48)));albumStar.setOnClickListener(v->toggleAlbumFavorite());
        albumHeader.addView(albumTitle,new LinearLayout.LayoutParams(-1,-2));albumHeader.addView(albumInfo,new LinearLayout.LayoutParams(-1,-2));
        trackPane.addView(albumHeader,new LinearLayout.LayoutParams(-1,0,1));
        tracks=new ListView(this); trackPane.addView(tracks,new LinearLayout.LayoutParams(-1,0,1));
        tracks.setChoiceMode(ListView.CHOICE_MODE_SINGLE);
        tracks.setVisibility(View.GONE);
        tracks.setOnItemClickListener((p,v,index,id)->{if(player!=null&&!playlist.isEmpty()){
            player.setMediaItems(playlist,index,0);player.prepare();player.play();}});
        playbackPane=new LinearLayout(this);playbackPane.setOrientation(LinearLayout.VERTICAL);playbackPane.setGravity(android.view.Gravity.CENTER_VERTICAL);layout.addView(playbackPane);
        playbackInfo=new LinearLayout(this);playbackInfo.setOrientation(LinearLayout.VERTICAL);playbackPane.addView(playbackInfo);
        var nowRow=new LinearLayout(this);nowPlayingRow=nowRow;nowRow.setBaselineAligned(false);nowRow.setGravity(android.view.Gravity.CENTER_VERTICAL);playbackInfo.addView(nowRow);
        // Separate the current-track strip from the library without increasing its height.
        var playingBackground=new android.graphics.drawable.GradientDrawable();
        playingBackground.setColor(android.graphics.Color.rgb(29,42,51));
        playingBackground.setCornerRadius(PlayerStyle.dp(this,8));nowRow.setBackground(playingBackground);
        now=label(jp.virtualcd.player.LanguageStrings.text("停止中","Stopped"),16);now.setGravity(android.view.Gravity.CENTER);now.setMaxLines(2);now.setEllipsize(android.text.TextUtils.TruncateAt.END);nowRow.addView(now,new LinearLayout.LayoutParams(0,-2,1));
        returnToPlaying=headerButton(nowRow,"current",jp.virtualcd.player.LanguageStrings.text("再生中のアルバム・曲へ戻る","Go to current album and track"),()->showNowPlaying());returnToPlaying.setEnabled(false);
        now.setOnClickListener(v->showNowPlaying());now.setContentDescription(jp.virtualcd.player.LanguageStrings.text("再生中の曲名。タップしてアルバム・曲へ戻る","Current track. Tap to return to its album."));
        seek=new SeekBar(this);seek.setMax(1000);playbackInfo.addView(seek);
        time=label("0:00 / —",12);playbackInfo.addView(time);
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
            public void onStartTrackingTouch(SeekBar s){dragging=true;}
            public void onProgressChanged(SeekBar s,int value,boolean user){if(user&&player!=null&&player.getDuration()>0)time.setText(clock(player.getDuration()*value/1000)+" / "+clock(player.getDuration()));}
            public void onStopTrackingTouch(SeekBar s){if(player!=null&&player.getDuration()>0)player.seekTo(player.getDuration()*s.getProgress()/1000);dragging=false;}
        });
        var controls=new LinearLayout(this);playbackControls=controls;playbackPane.addView(controls);
        shuffle=button(controls,"",()->{if(player!=null)player.setShuffleModeEnabled(!player.getShuffleModeEnabled());});ControlIcon.button(shuffle,"shuffle",jp.virtualcd.player.LanguageStrings.text("シャッフル OFF","Shuffle OFF"));
        ControlIcon.button(button(controls,jp.virtualcd.player.LanguageStrings.text("前へ","Previous"),()->{if(player!=null)player.seekToPreviousMediaItem();}),"previous",jp.virtualcd.player.LanguageStrings.text("前の曲","Previous track"));
        play=button(controls,jp.virtualcd.player.LanguageStrings.text("再生","Play"),()->{if(player!=null){if(player.isPlaying())player.pause();else {if(player.getPlaybackState()==Player.STATE_IDLE)player.prepare();player.play();}}});
        ControlIcon.button(play,"play",jp.virtualcd.player.LanguageStrings.text("再生","Play"));
        ControlIcon.button(button(controls,jp.virtualcd.player.LanguageStrings.text("停止","Stop"),()->{if(player!=null)player.stop();}),"stop",jp.virtualcd.player.LanguageStrings.text("停止","Stop"));
        ControlIcon.button(button(controls,jp.virtualcd.player.LanguageStrings.text("次へ","Next"),()->{if(player!=null)player.seekToNextMediaItem();}),"next",jp.virtualcd.player.LanguageStrings.text("次の曲","Next track"));
        repeat=button(controls,"",()->{if(player!=null)player.setRepeatMode((player.getRepeatMode()+1)%3);});ControlIcon.button(repeat,"repeat",jp.virtualcd.player.LanguageStrings.text("リピート OFF","Repeat OFF"));
        setContentView(screenLayout);
        applyDisplayLayout(getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE);
        screenLayout.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->{if(r>l&&b>t)applyDisplayLayout(r-l>b-t);});
        connection=new MediaController.Builder(this,new SessionToken(this,new ComponentName(this,PlaybackService.class))).buildAsync();
        connection.addListener(()->{if(isDestroyed())return;try{player=connection.get(); player.addListener(new Player.Listener(){
            @Override public void onPlayerError(PlaybackException error){status.setText(jp.virtualcd.player.LanguageStrings.text("再生エラー: ","Playback error: ")+error.getMessage());}
            @Override public void onMediaItemTransition(MediaItem item,int reason){updatePlayer();}
        }); updatePlayer();
            MediaItem current=player.getCurrentMediaItem();
            if(resumeFocusAllowed&&current!=null&&current.mediaMetadata.extras!=null){String source=current.mediaMetadata.extras.getString(ListeningState.ALBUM_URI,"");if(!source.isEmpty())loadAlbum(Uri.parse(source));}
        }catch(Exception ex){status.setText(jp.virtualcd.player.LanguageStrings.text("再生機能へ接続できません: ","Unable to connect to playback: ")+ex.getMessage());}},getMainExecutor());
        String savedTree=getPreferences(MODE_PRIVATE).getString("tree",null);
        if(savedTree!=null){libraryTree=Uri.parse(savedTree);restoreLibrary();}
        else {String previous=getPreferences(MODE_PRIVATE).getString("document",null);if(previous!=null)loadAlbum(Uri.parse(previous));}
    }
    @Override public void onConfigurationChanged(android.content.res.Configuration config){super.onConfigurationChanged(config);applyDisplayLayout(config.orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE);}
    private void applyDisplayLayout(boolean wide){
        if(wideLayout!=null&&wideLayout==wide)return;wideLayout=wide;
        int first=albums.getFirstVisiblePosition();albums.setNumColumns(wide?2:1);albums.post(()->albums.setSelection(first));
        trackPane.setOrientation(wide?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);
        var artworkParams=wide?new LinearLayout.LayoutParams(0,-1,0.357f):new LinearLayout.LayoutParams(-1,0,0.85f);
        artworkParams.setMarginEnd(wide?PlayerStyle.dp(this,12):0);albumHeader.setLayoutParams(artworkParams);
        tracks.setLayoutParams(wide?new LinearLayout.LayoutParams(0,-1,0.643f):new LinearLayout.LayoutParams(-1,0,1.15f));
        albumTitle.setMaxLines(wide?1:2);albumTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);albumInfo.setMaxLines(wide?1:2);albumInfo.setEllipsize(android.text.TextUtils.TruncateAt.END);
        albumTitle.setTextSize(wide?16:18);albumTitle.setPadding(0,wide?2:8,0,wide?2:8);albumInfo.setPadding(0,wide?2:8,0,wide?2:8);
        playbackPane.setOrientation(wide?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);
        playbackInfo.setLayoutParams(new LinearLayout.LayoutParams(-1,-2));
        arrangeSideControls(wide);
        now.setMaxLines(wide?1:2);time.setPadding(0,wide?0:8,0,wide?0:8);
        status.setMaxLines(wide?1:Integer.MAX_VALUE);status.setEllipsize(wide?android.text.TextUtils.TruncateAt.END:null);
        updateCompactHeader();
        screenLayout.requestApplyInsets();
    }
    private void arrangeSideControls(boolean wide){
        for(Button button:navigationButtons){((android.view.ViewGroup)button.getParent()).removeView(button);}
        ((android.view.ViewGroup)returnToPlaying.getParent()).removeView(returnToPlaying);
        ((android.view.ViewGroup)playbackControls.getParent()).removeView(playbackControls);
        ((android.view.ViewGroup)time.getParent()).removeView(time);
        if(wide){
            for(Button button:navigationButtons)sideNavigation.addView(button);
            sideNavigation.addView(returnToPlaying);
            playbackControls.setOrientation(LinearLayout.VERTICAL);sideColumns.addView(playbackControls,new LinearLayout.LayoutParams(PlayerStyle.dp(this,56),-2));
            nowPlayingRow.addView(time,new LinearLayout.LayoutParams(-2,-2));
        }else{
            headingRow.addView(navigationButtons.get(0),0);
            for(int i=1;i<navigationButtons.size();i++)headingRow.addView(navigationButtons.get(i));
            nowPlayingRow.addView(returnToPlaying);playbackInfo.addView(time,new LinearLayout.LayoutParams(-1,-2));
            playbackControls.setOrientation(LinearLayout.HORIZONTAL);playbackPane.addView(playbackControls,new LinearLayout.LayoutParams(-1,-2));
        }
        for(int i=0;i<playbackControls.getChildCount();i++){
            var params=new LinearLayout.LayoutParams(wide?-1:0,PlayerStyle.dp(this,wide?44:48),wide?0:1);
            int margin=PlayerStyle.dp(this,wide?2:3);params.setMargins(margin,margin,margin,margin);playbackControls.getChildAt(i).setLayoutParams(params);
        }
        now.setPadding(0,wide?2:8,PlayerStyle.dp(this,8),wide?2:8);sideRail.setVisibility(wide?View.VISIBLE:View.GONE);
    }
    private void updateCompactHeader(){
        if(search==null||headerTitleSlot==null)return;
        boolean wide=Boolean.TRUE.equals(wideLayout);
        var parent=(android.view.ViewGroup)search.getParent();
        if(wide&&parent!=headerTitleSlot){parent.removeView(search);headerTitleSlot.addView(search,new FrameLayout.LayoutParams(-1,-1));}
        else if(!wide&&parent!=rootLayout){parent.removeView(search);rootLayout.addView(search,rootLayout.indexOfChild(status)+1,new LinearLayout.LayoutParams(-1,-2));}
        screenTitle.setVisibility(wide&&libraryVisible?View.GONE:View.VISIBLE);
        headingRow.setVisibility(wide&&!libraryVisible?View.GONE:View.VISIBLE);
        search.setVisibility(libraryVisible?View.VISIBLE:View.GONE);
        updateStatusVisibility();
    }
    private void updateStatusVisibility(){
        if(status==null)return;boolean wide=Boolean.TRUE.equals(wideLayout);
        String message=status.getText().toString().toLowerCase(Locale.ROOT);
        boolean important=message.contains(jp.virtualcd.player.LanguageStrings.text("エラー","Error").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("できません","Unable").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("失敗","failed").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("見つかりません","not found").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("読込中","Loading").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("読み込んで","Loading").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("検索中","Searching").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("確認中","Checking").toLowerCase(Locale.ROOT))||message.contains(jp.virtualcd.player.LanguageStrings.text("更新中","Updating").toLowerCase(Locale.ROOT))||library.isEmpty();
        status.setVisibility(!message.isEmpty()&&(!wide||important)?View.VISIBLE:View.GONE);status.setPadding(0,wide?0:8,0,wide?0:8);
    }
    private TextView label(String text,int size){var v=new TextView(this);v.setText(text);v.setTextSize(size);v.setTextColor(0xffe8edf5);v.setPadding(0,8,0,8);return v;}
    private Button button(LinearLayout parent,String text,Runnable action){var b=new Button(this);b.setText(text);PlayerStyle.button(b);
        var params=new LinearLayout.LayoutParams(0,PlayerStyle.dp(this,48),1);params.setMargins(PlayerStyle.dp(this,3),PlayerStyle.dp(this,3),PlayerStyle.dp(this,3),PlayerStyle.dp(this,3));
        parent.addView(b,params);b.setOnClickListener(v->action.run());return b;}
    private Button headerButton(LinearLayout row,String icon,String label,Runnable action){Button b=button(row,"",action);b.setLayoutParams(new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44)));ControlIcon.button(b,icon,label);return b;}
    private void updateNavigation(){backToAlbums.setVisibility(libraryVisible?View.GONE:View.VISIBLE);refresh.setVisibility(libraryVisible?View.GONE:View.VISIBLE);
        sortButton.setVisibility(libraryVisible?View.VISIBLE:View.GONE);historyButton.setVisibility(libraryVisible?View.VISIBLE:View.GONE);favoritesButton.setVisibility(libraryVisible?View.VISIBLE:View.GONE);screenTitle.setText(libraryVisible?jp.virtualcd.player.LanguageStrings.text("アルバム","Albums"):jp.virtualcd.player.LanguageStrings.text("曲一覧","Tracks"));updateCompactHeader();}
    static Uri playingAlbum(MediaItem item){
        if(item==null)return null;
        String source=item.mediaMetadata.extras==null?"":item.mediaMetadata.extras.getString(ListeningState.ALBUM_URI,"");
        if(source!=null&&!source.isEmpty()){Uri uri=Uri.parse(source);if("content".equals(uri.getScheme()))return uri;}
        Uri uri=item.localConfiguration!=null?item.localConfiguration.uri:Uri.parse(item.mediaId);
        if("zipmp3".equals(uri.getScheme())){if(!uri.isHierarchical())return null;String document=uri.getQueryParameter("document");if(document==null)return null;uri=Uri.parse(document);}
        return "content".equals(uri.getScheme())?uri:null;
    }
    private void showNowPlaying(){
        MediaItem current=player==null?null:player.getCurrentMediaItem();Uri source=playingAlbum(current);
        if(source==null){Toast.makeText(this,jp.virtualcd.player.LanguageStrings.text("再生する曲がまだ選択されていません","No track is selected for playback"),Toast.LENGTH_SHORT).show();return;}
        resumeFocusAllowed=false;
        int index=playingIndex(playlist,current);
        if(!libraryVisible&&source.equals(selectedAlbum)&&index>=0){
            tracks.clearChoices();tracks.setItemChecked(index,true);tracks.setSelectionFromTop(index,0);highlightedId=current.mediaId;return;
        }
        // Only navigate. Never pass playId, replace the queue, seek, prepare, or resume playback.
        loadAlbum(source);
    }
    private void showFavorites(){new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("お気に入り","Favorites")).setItems(new String[]{jp.virtualcd.player.LanguageStrings.text("アルバム","Albums"),jp.virtualcd.player.LanguageStrings.text("曲","Tracks")},(d,index)->showSaved(index==0?"favoriteAlbums":"favoriteTracks")).show();}
    private void showSoundSettings(){if(soundDialog!=null)soundDialog.dismiss();soundDialog=SoundSettingsDialog.show(this);}
    private void showSettings(View anchor){PopupMenu menu=new PopupMenu(this,anchor);
        menu.getMenu().add(0,2,1,jp.virtualcd.player.LanguageStrings.text("音楽フォルダーを選択","Choose music folder")).setEnabled(folder.isEnabled());menu.getMenu().add(0,3,2,jp.virtualcd.player.LanguageStrings.text("音楽ファイル・ZIPを選択","Choose music file or ZIP")).setEnabled(choose.isEnabled());
        menu.getMenu().add(0,4,3,jp.virtualcd.player.LanguageStrings.text("ライブラリ全体を再読込","Reload entire library")).setEnabled(!scanning&&libraryTree!=null);
        if(!libraryVisible){menu.getMenu().add(0,5,4,jp.virtualcd.player.LanguageStrings.text("再生履歴","Playback history"));menu.getMenu().add(0,6,5,jp.virtualcd.player.LanguageStrings.text("お気に入り一覧","Favorites"));}
        menu.getMenu().add(0,8,6,jp.virtualcd.player.LanguageStrings.text("自動停止タイマー","Auto-stop timer"));
        menu.getMenu().add(0,9,7,jp.virtualcd.player.LanguageStrings.text("PCから同期（Wi-Fi）","Sync from PC (Wi-Fi)"));
        menu.getMenu().add(0,10,8,jp.virtualcd.player.LanguageStrings.text("再生中の歌詞","Lyrics for current track")).setEnabled(player!=null&&player.getCurrentMediaItem()!=null);
        menu.getMenu().add(0,11,9,"言語 / Language");
        menu.getMenu().add(0,7,10,"Virtual CD Player "+BuildConfig.VERSION_NAME).setEnabled(false);
        menu.setOnMenuItemClickListener(item->{switch(item.getItemId()){
            case 2:folder.performClick();break;case 3:choose.performClick();break;case 4:scanLibrary();break;case 5:showSaved("history");break;case 6:showFavorites();break;case 8:AutoStopSettings.show(this);break;case 9:showPcSync();break;case 10:if(player!=null&&player.getCurrentMediaItem()!=null)LyricsStore.show(this,player.getCurrentMediaItem());break;case 11:showLanguageSettings();break;}return true;});menu.show();}
    private boolean languageChangeBusy(){
        if(!syncChecking&&!syncDialogOpen&&!scanning&&!restoringLibrary)return false;
        Toast.makeText(this,LanguageStrings.text("同期・読み込みの完了後に言語を切り替えてください。","Wait for syncing or loading to finish before changing the language."),Toast.LENGTH_LONG).show();
        return true;
    }
    private android.app.AlertDialog showLanguageSettings(){
        if(languageChangeBusy())return null;
        final int[] selected={"en".equals(LanguageStrings.code())?1:0};
        return new android.app.AlertDialog.Builder(this).setTitle("言語 / Language")
            .setSingleChoiceItems(new String[]{"日本語","English"},selected[0],(dialog,index)->selected[0]=index)
            .setNegativeButton(LanguageStrings.text("キャンセル","Cancel"),null)
            .setPositiveButton(LanguageStrings.text("適用","Apply"),(dialog,which)->{
                String code=selected[0]==1?"en":"ja";
                if(code.equals(LanguageStrings.code())||languageChangeBusy())return;
                PlayerApplication.saveLanguage(this,code);
                recreate();
            }).show();
    }
    private void showPcSync(){showPcSync(null);}
    private void showPcSync(String scanned){
        if(libraryTree==null){Toast.makeText(this,jp.virtualcd.player.LanguageStrings.text("先に設定からSDカードの音楽フォルダーを選択してください","Choose your SD card music folder in Settings first."),Toast.LENGTH_LONG).show();return;}
        var box=new LinearLayout(this);box.setOrientation(LinearLayout.VERTICAL);int pad=PlayerStyle.dp(this,20);box.setPadding(pad,pad,pad,pad);
        var hint=new TextView(this);hint.setText(scanned==null?jp.virtualcd.player.LanguageStrings.text("Windowsのモバイル同期画面に表示されたQRコードを読み取ってください。USBデバッグは不要です。選択中の音楽フォルダーへ保存します。\n同じ家庭内Wi-Fiで使用してください。URLの手動入力も可能です。","Scan the QR code shown in Mobile Sync on Windows. USB debugging is not required. Files are saved to the selected music folder.\nUse the same home Wi-Fi network. You can also enter the URL manually."):jp.virtualcd.player.LanguageStrings.text("QRコードを読み取りました。接続先：","QR code scanned. Connect to: ")+Uri.parse(scanned).getAuthority()+jp.virtualcd.player.LanguageStrings.text("\nこのPCでよければ「接続して同期」を押してください。選択中の音楽フォルダーへ保存します。","\nIf this is your PC, tap Connect and sync. Files will be saved to the selected music folder."));box.addView(hint);
        var scan=new Button(this);scan.setText(jp.virtualcd.player.LanguageStrings.text("QRコードを読み取る","Scan QR code"));box.addView(scan);
        var input=new EditText(this);input.setSingleLine();input.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI);input.setText(scanned!=null?scanned:getPreferences(MODE_PRIVATE).getString("syncEndpoint",""));input.setHint(jp.virtualcd.player.LanguageStrings.text("手動入力用URL（通常はQRを使用）","URL for manual entry (normally use QR)"));box.addView(input);
        var scroll=new ScrollView(this);scroll.addView(box);
        var favoritesLabel=new TextView(this);favoritesLabel.setText(jp.virtualcd.player.LanguageStrings.text("お気に入りの引き継ぎ（アルバム・曲）","Import favorites (albums and tracks)"));box.addView(favoritesLabel);
        var favoritesMode=new Spinner(this);var favoritesModes=new ArrayAdapter<String>(this,android.R.layout.simple_spinner_item,FavoriteSync.labels());
        favoritesModes.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);favoritesMode.setAdapter(favoritesModes);
        favoritesMode.setSelection(FavoriteSync.mode(this));box.addView(favoritesMode);
        favoritesMode.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){
            public void onNothingSelected(AdapterView<?> parent){}
            public void onItemSelected(AdapterView<?> parent,View view,int position,long id){FavoriteSync.setMode(MainActivity.this,position);}
        });
        var favoritesHelp=new TextView(this);favoritesHelp.setText(jp.virtualcd.player.LanguageStrings.text("標準では初回だけ引き継ぎ、その後のスマートフォーン側の変更を保持します。Windowsで変更後は、対象アルバムを転送し直してください。","By default, favorites are imported only once; later phone changes are kept. Resync affected albums after changing favorites on Windows."));box.addView(favoritesHelp);
        var alignFavorites=new Button(this);alignFavorites.setText(jp.virtualcd.player.LanguageStrings.text("今すぐWindowsの状態に合わせる","Match Windows now"));box.addView(alignFavorites);
        final boolean[] forceFavorites={false};
        var dialog=new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("PCから同期","Sync from PC")).setView(scroll).setNegativeButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).setNeutralButton(jp.virtualcd.player.LanguageStrings.text("自動接続を解除","Disable auto-connect"),(d,w)->getPreferences(MODE_PRIVATE).edit().remove("syncEndpoint").apply()).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("接続して同期","Connect and sync"),null).create();
        scan.setOnClickListener(v->{dialog.dismiss();var options=new com.journeyapps.barcodescanner.ScanOptions().setDesiredBarcodeFormats(com.journeyapps.barcodescanner.ScanOptions.QR_CODE).setCaptureActivity(SyncQrCaptureActivity.class).setOrientationLocked(false).setBeepEnabled(false).setPrompt(jp.virtualcd.player.LanguageStrings.text("Windowsの接続QRコードを枠内に映してください","Place the Windows connection QR code inside the frame"));startActivityForResult(options.createScanIntent(this),30);});
        Runnable connect=new Runnable(){public void run(){
            if(isDestroyed()||!dialog.isShowing())return;
            try{
            String endpoint=SyncDownload.validateAddress(input.getText().toString());input.setError(null);
            if(syncChecking){
                hint.setText(jp.virtualcd.player.LanguageStrings.text("現在の同期確認が終わるのを待っています。完了後、このPCへ自動的に接続します。","Waiting for the current sync check. This PC will be connected automatically afterwards."));
                dialog.getButton(-1).setText(jp.virtualcd.player.LanguageStrings.text("確認完了を待っています…","Waiting for the check to finish…"));dialog.getButton(-1).setEnabled(false);
                input.setEnabled(false);scan.setEnabled(false);
                handler.postDelayed(this,300);return;
            }
            final Uri tree=libraryTree;final boolean alignNow=forceFavorites[0];syncChecking=true;getPreferences(MODE_PRIVATE).edit().putString("syncEndpoint",endpoint).apply();dialog.dismiss();
            showSyncProgress(jp.virtualcd.player.LanguageStrings.text("PCに接続しています…","Connecting to PC…"));
            syncWorker.execute(()->{try{SyncDownload.pull(MainActivity.this,tree,endpoint,MainActivity.this::showSyncProgress);
                MobileSync.refresh(MainActivity.this,tree,Collections.emptyList(),alignNow);
                handler.post(()->{syncChecking=false;if(!isDestroyed()){if(alignNow)syncStatus.showProgress(jp.virtualcd.player.LanguageStrings.text("同期済みです。Windowsから受信したお気に入り状態を反映しました。","Already synced. Favorites received from Windows have been applied."));else if(syncStatus.getText().toString().equals(jp.virtualcd.player.LanguageStrings.text("PCに接続しています…","Connecting to PC…")))syncStatus.showProgress(jp.virtualcd.player.LanguageStrings.text("同期済みです。新しい転送はありません。","Already synced. No new transfers."));refreshAlbumFavorite();refreshTrackLabels();scanLibrary();}});}catch(Exception ex){showSyncProgress(jp.virtualcd.player.LanguageStrings.text("同期を中断しました。\n","Sync interrupted.\n")+ex.getMessage());handler.post(()->{syncChecking=false;if(!isDestroyed())new android.app.AlertDialog.Builder(MainActivity.this).setTitle(jp.virtualcd.player.LanguageStrings.text("同期できません","Unable to sync")).setMessage(ex.getMessage()+jp.virtualcd.player.LanguageStrings.text("\nPCの同期画面・Wi-Fi・保存先の書き込み許可を確認してください。","\nCheck the PC sync screen, Wi-Fi, and write permission for the destination.")).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"),null).show();});}});
        }catch(Exception ex){input.setError(ex.getMessage());input.setEnabled(true);scan.setEnabled(true);dialog.getButton(-1).setEnabled(true);dialog.getButton(-1).setText(jp.virtualcd.player.LanguageStrings.text("接続して同期","Connect and sync"));}
        }};
        alignFavorites.setOnClickListener(v->new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("お気に入りを上書きしますか？","Overwrite favorites?"))
            .setMessage(jp.virtualcd.player.LanguageStrings.text("転送済みアルバム・曲のお気に入りを、Windowsが転送用に準備した状態へ合わせます。スマートフォーンでの登録・解除も上書きされます。通常の引き継ぎ設定は変わりません。Windowsで変更した場合は先に転送準備をやり直してください。","Match favorites for transferred albums and tracks to the snapshot prepared on Windows. Phone changes will be overwritten. The normal import setting is unchanged. Prepare the transfer again first if you changed favorites on Windows."))
            .setNegativeButton(jp.virtualcd.player.LanguageStrings.text("キャンセル","Cancel"),null).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("上書きして同期","Overwrite and sync"),(d,w)->{forceFavorites[0]=true;connect.run();}).show());
        dialog.setOnDismissListener(d->{syncDialogOpen=false;handler.removeCallbacks(connect);});
        dialog.setOnShowListener(v->dialog.getButton(-1).setOnClickListener(v2->connect.run()));
        syncDialogOpen=true;dialog.show();
    }
    private void showAlbumOrder(){new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("アルバム一覧の並び順","Album sort order"))
        .setSingleChoiceItems(new String[]{jp.virtualcd.player.LanguageStrings.text("アルバム名順","Album title"),jp.virtualcd.player.LanguageStrings.text("アーティスト名順","Artist"),jp.virtualcd.player.LanguageStrings.text("最近追加した順","Recently added")},albumAdapter.order(),(dialog,index)->{
            albumAdapter.setOrder(index);albums.setSelection(0);dialog.dismiss();
            if(index==1)Toast.makeText(this,jp.virtualcd.player.LanguageStrings.text("未取得のアーティスト情報はバックグラウンドで確認し、完了後に並べ替えます","Missing artist information is checked in the background; albums are sorted when complete."),Toast.LENGTH_LONG).show();
        }).setNegativeButton(jp.virtualcd.player.LanguageStrings.text("キャンセル","Cancel"),null).show();}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);
        if(request==30){if(result==RESULT_OK&&data!=null){try{String text=data.getStringExtra("SCAN_RESULT");if(text==null||text.length()>512)throw new java.io.IOException(jp.virtualcd.player.LanguageStrings.text("接続用QRコードではありません","Not a connection QR code"));showPcSync(SyncDownload.validateAddress(text));}catch(Exception ex){new android.app.AlertDialog.Builder(this).setTitle(jp.virtualcd.player.LanguageStrings.text("このQRコードは利用できません","This QR code cannot be used")).setMessage(jp.virtualcd.player.LanguageStrings.text("Windowsのモバイル同期画面に表示されたQRコードを読み取ってください。","Scan the QR code shown in Mobile Sync on Windows.")).setPositiveButton(jp.virtualcd.player.LanguageStrings.text("戻る","Back"),(d,w)->showPcSync()).show();}}else showPcSync();return;}
        if((request==1||request==2)&&result==RESULT_OK&&data!=null&&data.getData()!=null){Uri uri=data.getData();
            try{if((data.getFlags()&Intent.FLAG_GRANT_WRITE_URI_PERMISSION)!=0)getContentResolver().takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION);else getContentResolver().takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION);}
            catch(SecurityException ex){status.setText(jp.virtualcd.player.LanguageStrings.text("読み取り許可を保存できません。別のファイルを選択してください。","Unable to retain read permission. Select another file."));return;}
            if(request==2){libraryTree=uri;scanLibrary();}
            else{getPreferences(MODE_PRIVATE).edit().putString("document",uri.toString()).apply();loadAlbum(uri);}}}
    private void showLibrary(){
        resumeFocusAllowed=false;
        generation++;cancelAlbumLoad();choose.setEnabled(true);libraryVisible=true;
        updateNavigation();
        trackPane.setVisibility(View.GONE);tracks.setVisibility(View.GONE);albums.setVisibility(View.VISIBLE);search.setVisibility(View.VISIBLE);
        albumHeader.setVisibility(View.GONE);
        albumAdapter.setAlbums(library,search.getText().toString());
        status.setText(scanning?jp.virtualcd.player.LanguageStrings.text("アルバムを検索中…","Searching for albums…"):library.isEmpty()?jp.virtualcd.player.LanguageStrings.text("アルバムがありません。設定から音楽フォルダーを選択してください。","No albums. Choose a music folder in Settings."):"");
    }
    private void restoreLibrary(){
        restoringLibrary=true;
        final Uri tree=libraryTree;final int job=++scanGeneration;
        scanner.execute(()->{try{var saved=AlbumLibrary.load(this,tree);handler.post(()->{
            if(isDestroyed()||job!=scanGeneration)return;library=saved;restoringLibrary=false;
            if(libraryVisible){boolean allow=resumeFocusAllowed;showLibrary();resumeFocusAllowed=allow;}
            if(saved.isEmpty()&&libraryVisible)scanLibrary();
        });}catch(Exception e){handler.post(()->{if(!isDestroyed()&&job==scanGeneration){restoringLibrary=false;scanLibrary();}});}});
    }
    private void scanLibrary(){
        if(scanJob!=null)scanJob.cancel(true);
        final Uri tree=libraryTree;final int job=++scanGeneration;
        scanning=true;restoringLibrary=false;folder.setEnabled(false);refresh.setEnabled(false);showLibrary();
        scanJob=scanner.submit(()->{try{
            final long[] last={0};var found=AlbumLibrary.scan(this,tree,count->{
                long now=android.os.SystemClock.elapsedRealtime();if(now-last[0]<250)return;last[0]=now;
                handler.post(()->{if(!isDestroyed()&&job==scanGeneration&&libraryVisible)status.setText(jp.virtualcd.player.LanguageStrings.text("検索中… ","Searching… ")+count+jp.virtualcd.player.LanguageStrings.text("アルバム","Albums"));});
            });
            if(Thread.currentThread().isInterrupted())return;
            AlbumLibrary.save(this,tree,found);
            handler.post(()->{if(isDestroyed()||job!=scanGeneration)return;
                library=found;scanning=false;folder.setEnabled(true);refresh.setEnabled(true);
                getPreferences(MODE_PRIVATE).edit().putString("tree",tree.toString()).apply();
                albumAdapter.setAlbums(library,search.getText().toString());
                if(libraryVisible)status.setText(found.isEmpty()?jp.virtualcd.player.LanguageStrings.text("アルバムがありません。設定から音楽フォルダーを選択してください。","No albums. Choose a music folder in Settings."):"");
            });
        }catch(Exception e){handler.post(()->{if(isDestroyed()||job!=scanGeneration)return;
            scanning=false;folder.setEnabled(true);refresh.setEnabled(true);
            status.setText(jp.virtualcd.player.LanguageStrings.text("一覧の更新に失敗しました: ","Failed to refresh the list: ")+e.getMessage()+jp.virtualcd.player.LanguageStrings.text("\n前回の一覧は保持しています。SDカード・読み取り許可をご確認ください。","\nThe previous list is preserved. Check the SD card and read permission."));
        });}});
    }
    @Override public void onBackPressed(){if(!libraryVisible){showLibrary();return;}super.onBackPressed();}
    private void loadAlbum(Uri document){
        loadAlbum(document,null);
    }
    private void openJacketGallery(){
        if(selectedAlbum==null||selectedAlbumTitle.isEmpty())return;
        if(gallery!=null)gallery.close();
        gallery=ArtworkGallery.show(this,selectedAlbum);
    }
    private void loadAlbum(Uri document,String playId){
        loadAlbum(document,playId,false);
    }
    private void cancelAlbumLoad(){if(albumJob!=null)albumJob.cancel(true);if(coverJob!=null)coverJob.cancel(true);if(cacheJob!=null)cacheJob.cancel(true);}
    private boolean showAlbumTracks(AlbumTracks loaded,Uri document,String playId,boolean focus){
        var items=new ArrayList<MediaItem>();
        for(var track:loaded.tracks){var item=track.item();var extra=new Bundle(track.properties);extra.putString(ListeningState.ALBUM_URI,document.toString());
            items.add(item.buildUpon().setMediaMetadata(item.mediaMetadata.buildUpon().setExtras(extra).build()).build());}
        playlist=items;selectedAlbumTitle=loaded.title;refreshTrackLabels();refreshAlbumFavorite();
        albumInfo.setText(loaded.artist+" · "+items.size()+jp.virtualcd.player.LanguageStrings.text("曲"," tracks"));choose.setEnabled(true);tracks.setEnabled(true);
        String id=playId!=null?playId:player!=null&&player.getCurrentMediaItem()!=null?player.getCurrentMediaItem().mediaId:null;
        for(int i=0;i<items.size();i++)if(items.get(i).mediaId.equals(id)){
            if(focus){tracks.setItemChecked(i,true);tracks.setSelection(i);}
            if(playId!=null&&player!=null){player.setMediaItems(items,i,0);player.prepare();player.play();return true;}break;
        }
        return false;
    }
    private void loadAlbum(Uri document,String playId,boolean force){
        cancelAlbumLoad();
        resumeFocusAllowed=false;selectedAlbum=document;selectedAlbumTitle="";
        libraryVisible=false;albums.setVisibility(View.GONE);search.setVisibility(View.GONE);trackPane.setVisibility(View.VISIBLE);tracks.setVisibility(View.VISIBLE);
        updateNavigation();
        albumHeader.setVisibility(View.VISIBLE);albumCover.setImageResource(R.drawable.ic_album);albumTitle.setText(jp.virtualcd.player.LanguageStrings.text("読み込み中…","Loading…"));albumInfo.setText("");
        albumCover.setOnClickListener(v->openJacketGallery());
        albumCover.setContentDescription(jp.virtualcd.player.LanguageStrings.text("アルバムのジャケット。タップして他の画像を表示","Album artwork. Tap to view other images."));
        ((android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(search.getWindowToken(),0);
        int job=++generation;choose.setEnabled(false);status.setText(jp.virtualcd.player.LanguageStrings.text("曲一覧とタグを読み込んでいます…","Loading tracks and tags…"));
        playlist=Collections.emptyList();tracks.setAdapter(null);tracks.setEnabled(false);
        // These flags are touched only on the main thread. Never restart playback on a later tag update.
        final boolean[] display={false,false,false}; // saved snapshot shown, validation completed, requested play consumed
        if(!force)cacheJob=cacheWorker.submit(()->{
            var saved=AlbumTagCache.read(this,document);
            if(saved!=null)handler.post(()->{if(isDestroyed()||job!=generation||display[1])return;
                display[0]=true;display[2]=showAlbumTracks(saved.album,document,display[2]?null:playId,true)||display[2];
                status.setText(jp.virtualcd.player.LanguageStrings.text("保存済みの曲情報で再生できます · 更新を確認中…","Ready to play using saved tags · Checking updates…"));
            });
        });
        albumJob=worker.submit(()->{try{
            var source=AlbumLibrary.describe(this,document);
            if(Thread.currentThread().isInterrupted())return;
            handler.post(()->{if(isDestroyed()||job!=generation)return;
                if(!display[0])albumTitle.setText(source.title());
                coverJob=coverWorker.submit(()->{try{
                    String crop=getSharedPreferences("thumbnail-layout",MODE_PRIVATE).getString(document.toString(),"auto");
                    var cover=ArtworkLoader.load(this,source,crop);
                    handler.post(()->{if(!isDestroyed()&&job==generation&&cover!=null)albumCover.setImageBitmap(cover);});
                }catch(Exception ignored){/* Artwork failure must not prevent playback. */}});
            });
            var loaded=AlbumTracks.load(this,source,force,new AlbumTracks.Progress(){
                public void preview(AlbumTracks preview){
                    handler.post(()->{if(isDestroyed()||job!=generation)return;
                        if(display[0])return;
                        display[2]=showAlbumTracks(preview,document,display[2]?null:playId,true)||display[2];
                        status.setText(jp.virtualcd.player.LanguageStrings.text("ファイル名で再生できます · タグを読込中…","Ready to play by filename · Loading tags…"));
                    });
                }
                public void update(int complete,int total){handler.post(()->{if(!isDestroyed()&&job==generation)status.setText(jp.virtualcd.player.LanguageStrings.text("再生できます · タグを更新中… ","Ready to play · Updating tags… ")+complete+" / "+total+jp.virtualcd.player.LanguageStrings.text("曲"," tracks"));});}
            });
            handler.post(()->{if(isDestroyed()||job!=generation)return;display[1]=true;
                boolean initial=playlist.isEmpty();
                display[2]=showAlbumTracks(loaded,document,display[2]?null:playId,initial)||display[2];
                status.setText("");
                if(playId!=null&&!display[2])status.setText(jp.virtualcd.player.LanguageStrings.text("保存した曲が見つかりません。移動・削除されていないか確認してください。","Saved track not found. Check whether it was moved or deleted."));
            });
        }catch(Exception ex){handler.post(()->{if(isDestroyed()||job!=generation)return;
            if(playlist.isEmpty())albumTitle.setText(jp.virtualcd.player.LanguageStrings.text("読み込めません","Unable to load"));
            status.setText(jp.virtualcd.player.LanguageStrings.text("更新を確認できません: ","Unable to check for updates: ")+ex.getMessage()+jp.virtualcd.player.LanguageStrings.text("\nSDカードと読み取り許可をご確認ください。","\nCheck the SD card and read permission."));choose.setEnabled(true);});}});
    }
    private void updatePlayer(){if(player==null)return;if(playIconPlaying!=player.isPlaying()){playIconPlaying=player.isPlaying();ControlIcon.button(play,playIconPlaying?"pause":"play",playIconPlaying?jp.virtualcd.player.LanguageStrings.text("一時停止","Pause"):jp.virtualcd.player.LanguageStrings.text("再生","Play"));}
        returnToPlaying.setEnabled(playingAlbum(player.getCurrentMediaItem())!=null);
        syncTrackFocus();
        play.setSelected(true);shuffle.setSelected(player.getShuffleModeEnabled());repeat.setSelected(player.getRepeatMode()!=Player.REPEAT_MODE_OFF);
        String shuffleLabel=player.getShuffleModeEnabled()?jp.virtualcd.player.LanguageStrings.text("シャッフル ON","Shuffle ON"):jp.virtualcd.player.LanguageStrings.text("シャッフル OFF","Shuffle OFF");shuffle.setContentDescription(shuffleLabel);shuffle.setTooltipText(shuffleLabel);
        if(repeatIconMode!=player.getRepeatMode()){repeatIconMode=player.getRepeatMode();ControlIcon.button(repeat,repeatIconMode==Player.REPEAT_MODE_ONE?"repeat-one":"repeat",repeatIconMode==Player.REPEAT_MODE_ONE?jp.virtualcd.player.LanguageStrings.text("リピート 1曲","Repeat one"):repeatIconMode==Player.REPEAT_MODE_ALL?jp.virtualcd.player.LanguageStrings.text("リピート 全曲","Repeat all"):jp.virtualcd.player.LanguageStrings.text("リピート OFF","Repeat OFF"));}
        var metadata=player.getMediaMetadata();
        String playingLabel=nowPlayingLabel(metadata,Boolean.TRUE.equals(wideLayout));
        now.setText(nowPlayingText(metadata,Boolean.TRUE.equals(wideLayout)));now.setContentDescription(jp.virtualcd.player.LanguageStrings.text("再生中：","Now playing: ")+playingLabel);now.setTooltipText(playingLabel);
        long duration=player.getDuration(),position=player.getCurrentPosition();seek.setEnabled(duration>0&&player.isCurrentMediaItemSeekable());
        if(!dragging){seek.setProgress(duration>0?(int)Math.min(1000,position*1000/duration):0);
            var tuning=player.getPlaybackParameters();String suffix=tuning.equals(PlaybackParameters.DEFAULT)?"":String.format(Locale.ROOT,jp.virtualcd.player.LanguageStrings.text("  · %.2f× / %+d半音","  · %.2f× / %+d semitones"),tuning.speed,Math.round(12*Math.log(tuning.pitch)/Math.log(2)));
            time.setText(clock(position)+" / "+(duration>0?clock(duration):"—")+suffix);}}
    static CharSequence nowPlayingText(MediaMetadata metadata,boolean wide){
        var text=new android.text.SpannableString(nowPlayingLabel(metadata,wide));
        String artist=metadata.artist==null?"":metadata.artist.toString().trim();
        if(artist.isEmpty()&&metadata.albumArtist!=null)artist=metadata.albumArtist.toString().trim();
        if(!artist.isEmpty())text.setSpan(new android.text.style.RelativeSizeSpan(0.85f),text.length()-artist.length(),text.length(),android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return text;
    }
    static String nowPlayingLabel(MediaMetadata metadata,boolean wide){
        String title=metadata.title==null?"":metadata.title.toString().trim();
        String artist=metadata.artist==null?"":metadata.artist.toString().trim();
        if(artist.isEmpty()&&metadata.albumArtist!=null)artist=metadata.albumArtist.toString().trim();
        if(title.isEmpty())title=artist.isEmpty()?jp.virtualcd.player.LanguageStrings.text("停止中","Stopped"):jp.virtualcd.player.LanguageStrings.text("曲名不明","Unknown title");
        return title+(artist.isEmpty()?"":(wide?" · ":"\n")+artist);
    }
    private static String clock(long ms){long s=Math.max(0,ms/1000);return String.format(Locale.ROOT,"%d:%02d",s/60,s%60);}
    static int playingIndex(List<MediaItem> displayed,MediaItem current){
        if(current==null)return -1;for(int i=0;i<displayed.size();i++)if(displayed.get(i).mediaId.equals(current.mediaId))return i;return -1;
    }
    private void syncTrackFocus(){
        if(libraryVisible||player==null)return;
        MediaItem current=player.getCurrentMediaItem();String id=current==null?"":current.mediaId;
        if(java.util.Objects.equals(id,highlightedId))return;highlightedId=id;
        int index=playingIndex(playlist,current);tracks.clearChoices();
        if(index>=0){tracks.setItemChecked(index,true);
            if(index<tracks.getFirstVisiblePosition()||index>tracks.getLastVisiblePosition())tracks.smoothScrollToPosition(index);
        }
        tracks.invalidateViews();
    }
    private void refreshAlbumFavorite(){if(selectedAlbum!=null){albumTitle.setText(selectedAlbumTitle);FavoriteButton.bind(albumStar,listening.contains("favoriteAlbums",selectedAlbum.toString()),selectedAlbumTitle);}}
    private void toggleAlbumFavorite(){
        if(selectedAlbum==null||selectedAlbumTitle.isEmpty())return;
        try{boolean added=listening.toggle("favoriteAlbums",new org.json.JSONObject().put("id",selectedAlbum.toString()).put("source",selectedAlbum.toString()).put("title",selectedAlbumTitle));
            refreshAlbumFavorite();Toast.makeText(this,added?jp.virtualcd.player.LanguageStrings.text("アルバムをお気に入りに登録しました","Album added to favorites"):jp.virtualcd.player.LanguageStrings.text("アルバムのお気に入りを解除しました","Album removed from favorites"),Toast.LENGTH_SHORT).show();
        }catch(org.json.JSONException e){status.setText(jp.virtualcd.player.LanguageStrings.text("お気に入りを保存できません","Unable to save favorites"));}
    }
    private void toggleTrackFavorite(MediaItem item){try{
        boolean added=listening.toggle("favoriteTracks",ListeningState.encode(item));refreshTrackLabels();
        Toast.makeText(this,added?jp.virtualcd.player.LanguageStrings.text("曲をお気に入りに登録しました","Track added to favorites"):jp.virtualcd.player.LanguageStrings.text("曲のお気に入りを解除しました","Track removed from favorites"),Toast.LENGTH_SHORT).show();
    }catch(org.json.JSONException e){status.setText(jp.virtualcd.player.LanguageStrings.text("お気に入りを保存できません","Unable to save favorites"));}}
    private void refreshTrackLabels(){
        int first=tracks.getFirstVisiblePosition();View firstRow=tracks.getChildAt(0);int top=firstRow==null?0:firstRow.getTop();
        tracks.setAdapter(new TrackAdapter(this,playlist,listening,item->toggleTrackFavorite(item),item->{if(propertiesDialog!=null)propertiesDialog.dismiss();propertiesDialog=TrackProperties.show(this,item);}));tracks.setSelectionFromTop(first,top);
        MediaItem current=player==null?null:player.getCurrentMediaItem();highlightedId=current==null?"":current.mediaId;tracks.clearChoices();
        int active=playingIndex(playlist,current);if(active>=0)tracks.setItemChecked(active,true);
    }
    private void showSaved(String key){
        var entries=listening.entries(key);if(entries.isEmpty()){Toast.makeText(this,jp.virtualcd.player.LanguageStrings.text("まだ登録がありません","No items yet"),Toast.LENGTH_SHORT).show();return;}
        if(savedDialog!=null)savedDialog.dismiss();
        String current=player!=null&&player.getCurrentMediaItem()!=null?player.getCurrentMediaItem().mediaId:"";
        savedDialog=SavedListDialog.show(this,entries,key,current,e->{String source=e.optString("source");
                if(key.equals("favoriteAlbums")){loadAlbum(Uri.parse(source));return;}
                if(player==null){status.setText(jp.virtualcd.player.LanguageStrings.text("再生機能へ接続中です","Connecting to playback"));return;}
                if(!source.isEmpty())loadAlbum(Uri.parse(source),e.optString("id"));
                else try{player.setMediaItem(ListeningState.decode(e));player.prepare();player.play();}catch(Exception error){status.setText(jp.virtualcd.player.LanguageStrings.text("保存した曲を開けません","Unable to open the saved track"));}
            },key.equals("history")?null:e->{listening.toggle(key,e);refreshAlbumFavorite();refreshTrackLabels();albumAdapter.notifyDataSetChanged();});
    }
    @Override protected void onStart(){super.onStart();handler.post(tick);}
    @Override protected void onStop(){handler.removeCallbacks(tick);super.onStop();}
    @Override protected void onDestroy(){generation++;scanGeneration++;cancelAlbumLoad();syncWorker.shutdownNow();cacheWorker.shutdownNow();coverWorker.shutdownNow();if(propertiesDialog!=null)propertiesDialog.dismiss();if(soundDialog!=null)soundDialog.dismiss();if(savedDialog!=null)savedDialog.dismiss();if(gallery!=null)gallery.close();scanner.shutdownNow();worker.shutdownNow();if(albumAdapter!=null)albumAdapter.close();handler.removeCallbacksAndMessages(null);if(connection!=null)MediaController.releaseFuture(connection);player=null;super.onDestroy();}
}
