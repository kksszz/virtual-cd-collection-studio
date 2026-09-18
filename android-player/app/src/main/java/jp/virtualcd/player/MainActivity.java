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
    private final java.util.concurrent.ExecutorService worker=Executors.newSingleThreadExecutor();
    private final java.util.concurrent.ExecutorService coverWorker=Executors.newSingleThreadExecutor();
    private java.util.concurrent.Future<?> albumJob,coverJob;
    private MediaController player;
    private ListenableFuture<MediaController> connection;
    private TextView status, now, time;
    private Button play, choose;
    private Button returnToPlaying;
    private SeekBar seek;
    private ListView tracks;
    private List<MediaItem> playlist=Collections.emptyList();
    private int generation;
    private boolean dragging;
    private AlbumAdapter albumAdapter;
    private ListView albums;
    private EditText search;
    private Button folder,refresh;
    private Uri libraryTree;
    private List<AlbumLibrary.Album> library=Collections.emptyList();
    private final java.util.concurrent.ExecutorService scanner=Executors.newSingleThreadExecutor();
    private java.util.concurrent.Future<?> scanJob;
    private int scanGeneration;
    private boolean libraryVisible=true,scanning;
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
        listening=new ListeningState(this);
        var layout=new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL);
        int padding=(int)(16*getResources().getDisplayMetrics().density); layout.setPadding(padding,padding,padding,padding);
        layout.setBackgroundColor(0xff15191f);
        layout.setOnApplyWindowInsetsListener((v,insets)->{
            v.setPadding(padding,padding+insets.getSystemWindowInsetTop(),padding,padding+insets.getSystemWindowInsetBottom());return insets;});
        var heading=new LinearLayout(this);heading.setBaselineAligned(false);heading.setGravity(android.view.Gravity.CENTER_VERTICAL);layout.addView(heading,new LinearLayout.LayoutParams(-1,PlayerStyle.dp(this,48)));
        backToAlbums=headerButton(heading,"back","アルバム一覧へ戻る",()->showLibrary());
        screenTitle=new TextView(this);screenTitle.setText("アルバム");screenTitle.setTextSize(18);screenTitle.setTextColor(0xffe8edf5);
        screenTitle.setIncludeFontPadding(false);screenTitle.setGravity(android.view.Gravity.CENTER_VERTICAL);screenTitle.setSingleLine(true);screenTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        screenTitle.setPadding(PlayerStyle.dp(this,12),0,PlayerStyle.dp(this,8),0);
        var titleSlot=new FrameLayout(this);titleSlot.addView(screenTitle,new FrameLayout.LayoutParams(-1,-1));heading.addView(titleSlot,new LinearLayout.LayoutParams(0,-1,1));
        historyButton=headerButton(heading,"history","再生履歴",()->showSaved("history"));
        favoritesButton=headerButton(heading,"star","お気に入り一覧",()->showFavorites());
        refresh=headerButton(heading,"refresh","このアルバムを再読込",()->{if(selectedAlbum!=null)loadAlbum(selectedAlbum,null,true);});
        headerButton(heading,"sound","音の設定",this::showSoundSettings);
        Button settings=headerButton(heading,"settings","設定",()->{});settings.setOnClickListener(v->showSettings(settings));
        backToAlbums.setVisibility(View.GONE);refresh.setVisibility(View.GONE);
        // The file/folder actions live in Settings; keep their busy-state controls off the main screen.
        var libraryControls=new LinearLayout(this);
        choose=button(libraryControls,"ファイル",()->{});
        choose.setContentDescription("音楽ファイル・ZIPを選択");choose.setTooltipText("音楽ファイル・ZIPを選択");
        choose.setOnClickListener(v->{Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE);
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);startActivityForResult(i,1);});
        folder=button(libraryControls,"フォルダー",()->{
            Intent i=new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION|Intent.FLAG_GRANT_PREFIX_URI_PERMISSION);
            if(libraryTree!=null)i.putExtra(android.provider.DocumentsContract.EXTRA_INITIAL_URI,libraryTree);
            startActivityForResult(i,2);
        });
        status=label("設定からSDカードの音楽フォルダーを選択してください。",12);layout.addView(status);
        search=new EditText(this);search.setSingleLine(true);search.setTextColor(0xffeef4fa);search.setHintTextColor(0xff99b5c9);search.setHint("アルバム名で検索");layout.addView(search);
        albumAdapter=new AlbumAdapter(this);albums=new ListView(this);albums.setAdapter(albumAdapter);layout.addView(albums,new LinearLayout.LayoutParams(-1,0,1));
        albums.setOnItemClickListener((p,v,index,id)->loadAlbum(albumAdapter.getItem(index).uri));
        albums.setOnItemLongClickListener((p,v,index,id)->{albumAdapter.chooseThumbnail(index);return true;});
        search.addTextChangedListener(new android.text.TextWatcher(){
            public void beforeTextChanged(CharSequence s,int start,int count,int after){}
            public void onTextChanged(CharSequence s,int start,int before,int count){albumAdapter.filter(s.toString());}
            public void afterTextChanged(android.text.Editable e){}
        });
        albumHeader=new LinearLayout(this);albumHeader.setOrientation(LinearLayout.VERTICAL);
        albumHeader.setGravity(android.view.Gravity.CENTER_HORIZONTAL);albumHeader.setVisibility(View.GONE);
        albumCover=new ImageView(this);albumCover.setScaleType(ImageView.ScaleType.FIT_CENTER);
        albumCover.setContentDescription("アルバムのジャケット");
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
        case3d.setLayoutParams(new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44)));ControlIcon.button(case3d,"case3d","このアルバムの3Dケース");
        Button jacket=button(artworkSide,"ジャケット",this::openJacketGallery);
        var jacketParams=new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44));jacketParams.setMargins(0,PlayerStyle.dp(this,6),0,PlayerStyle.dp(this,6));jacket.setLayoutParams(jacketParams);ControlIcon.button(jacket,"booklet","ジャケット・ブックレットを読む");
        artworkSide.addView(albumStar,new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,48)));albumStar.setOnClickListener(v->toggleAlbumFavorite());
        albumHeader.addView(albumTitle,new LinearLayout.LayoutParams(-1,-2));albumHeader.addView(albumInfo,new LinearLayout.LayoutParams(-1,-2));
        layout.addView(albumHeader,new LinearLayout.LayoutParams(-1,0,1));
        tracks=new ListView(this); layout.addView(tracks,new LinearLayout.LayoutParams(-1,0,1));
        tracks.setChoiceMode(ListView.CHOICE_MODE_SINGLE);
        tracks.setVisibility(View.GONE);
        tracks.setOnItemClickListener((p,v,index,id)->{if(player!=null&&!playlist.isEmpty()){
            player.setMediaItems(playlist,index,0);player.prepare();player.play();}});
        var nowRow=new LinearLayout(this);nowRow.setBaselineAligned(false);nowRow.setGravity(android.view.Gravity.CENTER_VERTICAL);layout.addView(nowRow);
        now=label("停止中",16);now.setMaxLines(2);now.setEllipsize(android.text.TextUtils.TruncateAt.END);nowRow.addView(now,new LinearLayout.LayoutParams(0,-2,1));
        returnToPlaying=headerButton(nowRow,"current","再生中のアルバム・曲へ戻る",()->showNowPlaying());returnToPlaying.setEnabled(false);
        now.setOnClickListener(v->showNowPlaying());now.setContentDescription("再生中の曲名。タップしてアルバム・曲へ戻る");
        seek=new SeekBar(this);seek.setMax(1000);layout.addView(seek);
        time=label("0:00 / —",12);layout.addView(time);
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
            public void onStartTrackingTouch(SeekBar s){dragging=true;}
            public void onProgressChanged(SeekBar s,int value,boolean user){if(user&&player!=null&&player.getDuration()>0)time.setText(clock(player.getDuration()*value/1000)+" / "+clock(player.getDuration()));}
            public void onStopTrackingTouch(SeekBar s){if(player!=null&&player.getDuration()>0)player.seekTo(player.getDuration()*s.getProgress()/1000);dragging=false;}
        });
        var controls=new LinearLayout(this);layout.addView(controls);
        shuffle=button(controls,"",()->{if(player!=null)player.setShuffleModeEnabled(!player.getShuffleModeEnabled());});ControlIcon.button(shuffle,"shuffle","シャッフル OFF");
        ControlIcon.button(button(controls,"前へ",()->{if(player!=null)player.seekToPreviousMediaItem();}),"previous","前の曲");
        play=button(controls,"再生",()->{if(player!=null){if(player.isPlaying())player.pause();else {if(player.getPlaybackState()==Player.STATE_IDLE)player.prepare();player.play();}}});
        ControlIcon.button(play,"play","再生");
        ControlIcon.button(button(controls,"停止",()->{if(player!=null)player.stop();}),"stop","停止");
        ControlIcon.button(button(controls,"次へ",()->{if(player!=null)player.seekToNextMediaItem();}),"next","次の曲");
        repeat=button(controls,"",()->{if(player!=null)player.setRepeatMode((player.getRepeatMode()+1)%3);});ControlIcon.button(repeat,"repeat","リピート OFF");
        setContentView(layout);
        connection=new MediaController.Builder(this,new SessionToken(this,new ComponentName(this,PlaybackService.class))).buildAsync();
        connection.addListener(()->{if(isDestroyed())return;try{player=connection.get(); player.addListener(new Player.Listener(){
            @Override public void onPlayerError(PlaybackException error){status.setText("再生エラー: "+error.getMessage());}
            @Override public void onMediaItemTransition(MediaItem item,int reason){updatePlayer();}
        }); updatePlayer();
            MediaItem current=player.getCurrentMediaItem();
            if(resumeFocusAllowed&&current!=null&&current.mediaMetadata.extras!=null){String source=current.mediaMetadata.extras.getString(ListeningState.ALBUM_URI,"");if(!source.isEmpty())loadAlbum(Uri.parse(source));}
        }catch(Exception ex){status.setText("再生機能へ接続できません: "+ex.getMessage());}},getMainExecutor());
        String savedTree=getPreferences(MODE_PRIVATE).getString("tree",null);
        if(savedTree!=null){libraryTree=Uri.parse(savedTree);restoreLibrary();}
        else {String previous=getPreferences(MODE_PRIVATE).getString("document",null);if(previous!=null)loadAlbum(Uri.parse(previous));}
    }
    private TextView label(String text,int size){var v=new TextView(this);v.setText(text);v.setTextSize(size);v.setTextColor(0xffe8edf5);v.setPadding(0,8,0,8);return v;}
    private Button button(LinearLayout parent,String text,Runnable action){var b=new Button(this);b.setText(text);PlayerStyle.button(b);
        var params=new LinearLayout.LayoutParams(0,PlayerStyle.dp(this,48),1);params.setMargins(PlayerStyle.dp(this,3),PlayerStyle.dp(this,3),PlayerStyle.dp(this,3),PlayerStyle.dp(this,3));
        parent.addView(b,params);b.setOnClickListener(v->action.run());return b;}
    private Button headerButton(LinearLayout row,String icon,String label,Runnable action){Button b=button(row,"",action);b.setLayoutParams(new LinearLayout.LayoutParams(PlayerStyle.dp(this,48),PlayerStyle.dp(this,44)));ControlIcon.button(b,icon,label);return b;}
    private void updateNavigation(){backToAlbums.setVisibility(libraryVisible?View.GONE:View.VISIBLE);refresh.setVisibility(libraryVisible?View.GONE:View.VISIBLE);
        historyButton.setVisibility(libraryVisible?View.VISIBLE:View.GONE);favoritesButton.setVisibility(libraryVisible?View.VISIBLE:View.GONE);screenTitle.setText(libraryVisible?"アルバム":"曲一覧");}
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
        if(source==null){Toast.makeText(this,"再生する曲がまだ選択されていません",Toast.LENGTH_SHORT).show();return;}
        resumeFocusAllowed=false;
        int index=playingIndex(playlist,current);
        if(!libraryVisible&&source.equals(selectedAlbum)&&index>=0){
            tracks.clearChoices();tracks.setItemChecked(index,true);tracks.setSelectionFromTop(index,0);highlightedId=current.mediaId;return;
        }
        // Only navigate. Never pass playId, replace the queue, seek, prepare, or resume playback.
        loadAlbum(source);
    }
    private void showFavorites(){new android.app.AlertDialog.Builder(this).setTitle("お気に入り").setItems(new String[]{"アルバム","曲"},(d,index)->showSaved(index==0?"favoriteAlbums":"favoriteTracks")).show();}
    private void showSoundSettings(){if(soundDialog!=null)soundDialog.dismiss();soundDialog=SoundSettingsDialog.show(this);}
    private void showSettings(View anchor){PopupMenu menu=new PopupMenu(this,anchor);
        menu.getMenu().add(0,2,1,"音楽フォルダーを選択").setEnabled(folder.isEnabled());menu.getMenu().add(0,3,2,"音楽ファイル・ZIPを選択").setEnabled(choose.isEnabled());
        menu.getMenu().add(0,4,3,"ライブラリ全体を再読込").setEnabled(!scanning&&libraryTree!=null);
        if(!libraryVisible){menu.getMenu().add(0,5,4,"再生履歴");menu.getMenu().add(0,6,5,"お気に入り一覧");}
        menu.getMenu().add(0,7,6,"Virtual CD Player 0.7.1").setEnabled(false);
        menu.setOnMenuItemClickListener(item->{switch(item.getItemId()){
            case 2:folder.performClick();break;case 3:choose.performClick();break;case 4:scanLibrary();break;case 5:showSaved("history");break;case 6:showFavorites();break;}return true;});menu.show();}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);
        if((request==1||request==2)&&result==RESULT_OK&&data!=null&&data.getData()!=null){Uri uri=data.getData();
            try{getContentResolver().takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION);}
            catch(SecurityException ex){status.setText("読み取り許可を保存できません。別のファイルを選択してください。");return;}
            if(request==2){libraryTree=uri;scanLibrary();}
            else{getPreferences(MODE_PRIVATE).edit().putString("document",uri.toString()).apply();loadAlbum(uri);}}}
    private void showLibrary(){
        resumeFocusAllowed=false;
        generation++;cancelAlbumLoad();choose.setEnabled(true);libraryVisible=true;
        updateNavigation();
        tracks.setVisibility(View.GONE);albums.setVisibility(View.VISIBLE);search.setVisibility(View.VISIBLE);
        albumHeader.setVisibility(View.GONE);
        albumAdapter.setAlbums(library,search.getText().toString());
        status.setText(scanning?"アルバムを検索中…":library.size()+"アルバム · ☆でお気に入り");
    }
    private void restoreLibrary(){
        final Uri tree=libraryTree;final int job=++scanGeneration;
        scanner.execute(()->{try{var saved=AlbumLibrary.load(this,tree);handler.post(()->{
            if(isDestroyed()||job!=scanGeneration)return;library=saved;
            if(libraryVisible){boolean allow=resumeFocusAllowed;showLibrary();resumeFocusAllowed=allow;}
            if(saved.isEmpty()&&libraryVisible)scanLibrary();
        });}catch(Exception e){handler.post(()->{if(!isDestroyed()&&job==scanGeneration)scanLibrary();});}});
    }
    private void scanLibrary(){
        if(scanJob!=null)scanJob.cancel(true);
        final Uri tree=libraryTree;final int job=++scanGeneration;
        scanning=true;folder.setEnabled(false);refresh.setEnabled(false);showLibrary();
        scanJob=scanner.submit(()->{try{
            final long[] last={0};var found=AlbumLibrary.scan(this,tree,count->{
                long now=android.os.SystemClock.elapsedRealtime();if(now-last[0]<250)return;last[0]=now;
                handler.post(()->{if(!isDestroyed()&&job==scanGeneration&&libraryVisible)status.setText("検索中… "+count+"アルバム");});
            });
            if(Thread.currentThread().isInterrupted())return;
            AlbumLibrary.save(this,tree,found);
            handler.post(()->{if(isDestroyed()||job!=scanGeneration)return;
                library=found;scanning=false;folder.setEnabled(true);refresh.setEnabled(true);
                getPreferences(MODE_PRIVATE).edit().putString("tree",tree.toString()).apply();
                albumAdapter.setAlbums(library,search.getText().toString());
                if(libraryVisible)status.setText(found.size()+"アルバム · ジャケットをタップして曲一覧へ");
            });
        }catch(Exception e){handler.post(()->{if(isDestroyed()||job!=scanGeneration)return;
            scanning=false;folder.setEnabled(true);refresh.setEnabled(true);
            status.setText("一覧の更新に失敗しました: "+e.getMessage()+"\n前回の一覧は保持しています。SDカード・読み取り許可をご確認ください。");
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
    private void cancelAlbumLoad(){if(albumJob!=null)albumJob.cancel(true);if(coverJob!=null)coverJob.cancel(true);}
    private void loadAlbum(Uri document,String playId,boolean force){
        cancelAlbumLoad();
        resumeFocusAllowed=false;selectedAlbum=document;selectedAlbumTitle="";
        libraryVisible=false;albums.setVisibility(View.GONE);search.setVisibility(View.GONE);tracks.setVisibility(View.VISIBLE);
        updateNavigation();
        albumHeader.setVisibility(View.VISIBLE);albumCover.setImageResource(R.drawable.ic_album);albumTitle.setText("読み込み中…");albumInfo.setText("");
        albumCover.setOnClickListener(v->openJacketGallery());
        albumCover.setContentDescription("アルバムのジャケット。タップして他の画像を表示");
        ((android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(search.getWindowToken(),0);
        int job=++generation;choose.setEnabled(false);status.setText("曲一覧とタグを読み込んでいます…");
        playlist=Collections.emptyList();tracks.setAdapter(null);tracks.setEnabled(false);
        albumJob=worker.submit(()->{try{
            var source=AlbumLibrary.describe(this,document);
            if(Thread.currentThread().isInterrupted())return;
            handler.post(()->{if(isDestroyed()||job!=generation)return;
                albumTitle.setText(source.title());
                coverJob=coverWorker.submit(()->{try{
                    String crop=getSharedPreferences("thumbnail-layout",MODE_PRIVATE).getString(document.toString(),"auto");
                    var cover=ArtworkLoader.load(this,source,crop);
                    handler.post(()->{if(!isDestroyed()&&job==generation&&cover!=null)albumCover.setImageBitmap(cover);});
                }catch(Exception ignored){/* Artwork failure must not prevent playback. */}});
            });
            var loaded=AlbumTracks.load(this,source,force,new AlbumTracks.Progress(){
                public void preview(AlbumTracks preview){
                    var labels=new ArrayList<String>();int n=0;for(var t:preview.tracks)labels.add(String.format(Locale.ROOT,"%02d  %s",++n,t.title));
                    handler.post(()->{if(isDestroyed()||job!=generation)return;
                        tracks.setAdapter(new ArrayAdapter<String>(MainActivity.this,android.R.layout.simple_list_item_1,labels));
                        albumInfo.setText(labels.size()+"曲");status.setText("タグを読込中… 0 / "+labels.size()+"曲（一覧はファイル名の仮表示）");
                    });
                }
                public void update(int complete,int total){handler.post(()->{if(!isDestroyed()&&job==generation)status.setText("タグを読込中… "+complete+" / "+total+"曲");});}
            });
            var items=new ArrayList<MediaItem>();var names=new ArrayList<String>();
            for(var track:loaded.tracks){
                var item=track.item();var extra=item.mediaMetadata.extras==null?new Bundle():new Bundle(item.mediaMetadata.extras);extra.putString(ListeningState.ALBUM_URI,document.toString());
                items.add(item.buildUpon().setMediaMetadata(item.mediaMetadata.buildUpon().setExtras(extra).build()).build());names.add(track.title);
            }
            handler.post(()->{if(isDestroyed()||job!=generation)return;playlist=items;
                selectedAlbumTitle=loaded.title;refreshTrackLabels();refreshAlbumFavorite();albumInfo.setText(loaded.artist+" · "+items.size()+"曲");
                status.setText("曲をタップして再生 · ☆でお気に入り");choose.setEnabled(true);tracks.setEnabled(true);
                String focus=playId!=null?playId:player!=null&&player.getCurrentMediaItem()!=null?player.getCurrentMediaItem().mediaId:null;
                boolean found=false;for(int i=0;i<items.size();i++)if(items.get(i).mediaId.equals(focus)){found=true;tracks.setItemChecked(i,true);tracks.setSelection(i);
                    if(playId!=null&&player!=null){player.setMediaItems(items,i,0);player.prepare();player.play();}break;}
                if(playId!=null&&!found)status.setText("保存した曲が見つかりません。移動・削除されていないか確認してください。");
            });
        }catch(Exception ex){handler.post(()->{if(isDestroyed()||job!=generation)return;albumTitle.setText("読み込めません");status.setText("読込エラー: "+ex.getMessage()+"\nSDカードと読み取り許可をご確認ください。");choose.setEnabled(true);});}});
    }
    private void updatePlayer(){if(player==null)return;if(playIconPlaying!=player.isPlaying()){playIconPlaying=player.isPlaying();ControlIcon.button(play,playIconPlaying?"pause":"play",playIconPlaying?"一時停止":"再生");}
        returnToPlaying.setEnabled(playingAlbum(player.getCurrentMediaItem())!=null);
        syncTrackFocus();
        play.setSelected(true);shuffle.setSelected(player.getShuffleModeEnabled());repeat.setSelected(player.getRepeatMode()!=Player.REPEAT_MODE_OFF);
        String shuffleLabel=player.getShuffleModeEnabled()?"シャッフル ON":"シャッフル OFF";shuffle.setContentDescription(shuffleLabel);shuffle.setTooltipText(shuffleLabel);
        if(repeatIconMode!=player.getRepeatMode()){repeatIconMode=player.getRepeatMode();ControlIcon.button(repeat,repeatIconMode==Player.REPEAT_MODE_ONE?"repeat-one":"repeat",repeatIconMode==Player.REPEAT_MODE_ONE?"リピート 1曲":repeatIconMode==Player.REPEAT_MODE_ALL?"リピート 全曲":"リピート OFF");}
        now.setText(player.getMediaMetadata().title!=null?player.getMediaMetadata().title:"停止中");
        long duration=player.getDuration(),position=player.getCurrentPosition();seek.setEnabled(duration>0&&player.isCurrentMediaItemSeekable());
        if(!dragging){seek.setProgress(duration>0?(int)Math.min(1000,position*1000/duration):0);
            var tuning=player.getPlaybackParameters();String suffix=tuning.equals(PlaybackParameters.DEFAULT)?"":String.format(Locale.ROOT,"  · %.2f× / %+d半音",tuning.speed,Math.round(12*Math.log(tuning.pitch)/Math.log(2)));
            time.setText(clock(position)+" / "+(duration>0?clock(duration):"—")+suffix);}}
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
            refreshAlbumFavorite();Toast.makeText(this,added?"アルバムをお気に入りに登録しました":"アルバムのお気に入りを解除しました",Toast.LENGTH_SHORT).show();
        }catch(org.json.JSONException e){status.setText("お気に入りを保存できません");}
    }
    private void toggleTrackFavorite(MediaItem item){try{
        boolean added=listening.toggle("favoriteTracks",ListeningState.encode(item));refreshTrackLabels();
        Toast.makeText(this,added?"曲をお気に入りに登録しました":"曲のお気に入りを解除しました",Toast.LENGTH_SHORT).show();
    }catch(org.json.JSONException e){status.setText("お気に入りを保存できません");}}
    private void refreshTrackLabels(){
        int first=tracks.getFirstVisiblePosition();View firstRow=tracks.getChildAt(0);int top=firstRow==null?0:firstRow.getTop();
        tracks.setAdapter(new TrackAdapter(this,playlist,listening,item->toggleTrackFavorite(item),item->{if(propertiesDialog!=null)propertiesDialog.dismiss();propertiesDialog=TrackProperties.show(this,item);}));tracks.setSelectionFromTop(first,top);
        MediaItem current=player==null?null:player.getCurrentMediaItem();highlightedId=current==null?"":current.mediaId;tracks.clearChoices();
        int active=playingIndex(playlist,current);if(active>=0)tracks.setItemChecked(active,true);
    }
    private void showSaved(String key){
        var entries=listening.entries(key);if(entries.isEmpty()){Toast.makeText(this,"まだ登録がありません",Toast.LENGTH_SHORT).show();return;}
        if(savedDialog!=null)savedDialog.dismiss();
        String current=player!=null&&player.getCurrentMediaItem()!=null?player.getCurrentMediaItem().mediaId:"";
        savedDialog=SavedListDialog.show(this,entries,key,current,e->{String source=e.optString("source");
                if(key.equals("favoriteAlbums")){loadAlbum(Uri.parse(source));return;}
                if(player==null){status.setText("再生機能へ接続中です");return;}
                if(!source.isEmpty())loadAlbum(Uri.parse(source),e.optString("id"));
                else try{player.setMediaItem(ListeningState.decode(e));player.prepare();player.play();}catch(Exception error){status.setText("保存した曲を開けません");}
            },key.equals("history")?null:e->{listening.toggle(key,e);refreshAlbumFavorite();refreshTrackLabels();albumAdapter.notifyDataSetChanged();});
    }
    @Override protected void onStart(){super.onStart();handler.post(tick);}
    @Override protected void onStop(){handler.removeCallbacks(tick);super.onStop();}
    @Override protected void onDestroy(){generation++;scanGeneration++;cancelAlbumLoad();coverWorker.shutdownNow();if(propertiesDialog!=null)propertiesDialog.dismiss();if(soundDialog!=null)soundDialog.dismiss();if(savedDialog!=null)savedDialog.dismiss();if(gallery!=null)gallery.close();scanner.shutdownNow();worker.shutdownNow();if(albumAdapter!=null)albumAdapter.close();handler.removeCallbacksAndMessages(null);if(connection!=null)MediaController.releaseFuture(connection);player=null;super.onDestroy();}
}
