package jp.virtualcd.player;

import android.app.Instrumentation;
import android.os.*;
import android.net.Uri;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import androidx.media3.datasource.DefaultDataSource;
import jp.virtualcd.player.archive.ZipTrackDataSource;
import jp.virtualcd.player.library.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;

/** On-device, read-only smoke checks against the user's already-authorized Music tree.
 * Test playback is muted. No source or persistent library data is changed. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class DeviceSmokeTest extends Instrumentation {
    private boolean stateOnly;
    private boolean caseOnly;
    private boolean loadingOnly;
    private String testMode;
    private String syncAddress;
    @Override public void onCreate(Bundle args){super.onCreate(args);syncAddress=args.getString("address","");testMode=args.getString("mode","");stateOnly="state".equals(testMode);caseOnly="case3d".equals(testMode);loadingOnly="loading".equals(testMode);start();}
    @Override public void onStart(){
        Bundle result=new Bundle();
        try{
            if(testMode.equals("languageUi")){LanguageDeviceChecks.run(this);result.putString("languageUi","PASS settings selection, activity recreation, Japanese/English UI, saved setting");finish(-1,result);return;}
            if(testMode.equals("language")){
                String original=LanguageStrings.code(),prefix="language-test-"+System.nanoTime();
                var c=new android.content.ContextWrapper(getTargetContext()){
                    @Override public android.content.SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(prefix+name,mode);}
                };
                try{
                    if(!c.getSharedPreferences("display-language",0).getString("code","ja").equals("ja"))throw new AssertionError("Default");
                    for(String language:new String[]{"en","ja","en","ja"}){
                        PlayerApplication.saveLanguage(c,language);
                        if(!language.equals(c.getSharedPreferences("display-language",0).getString("code","ja")))throw new AssertionError("Persistence");
                        LanguageStrings.setCode(c.getSharedPreferences("display-language",0).getString("code","ja"));
                        if(!LanguageStrings.text("日本語","English").equals(language.equals("en")?"English":"日本語"))throw new AssertionError("Language");
                        if(!FavoriteSync.labels()[0].equals(language.equals("en")?"First sync only (default)":"初回のみ引き継ぐ（標準）"))throw new AssertionError("Live labels");
                        if(!ArtworkLoader.imageFolder("ジャケット")||!ArtworkLoader.imageFolder("歌詞")||!ArtworkLoader.imageFolder("画像"))throw new AssertionError("Japanese folder detection");
                    }
                }finally{LanguageStrings.setCode(original);c.getSharedPreferences("display-language",0).edit().clear().commit();}
                result.putString("language","PASS default, persistence, repeated switching, labels, Japanese folder names");finish(-1,result);return;
            }
            if(testMode.equals("recentOrder")){
                String prefix="recent-test-"+System.nanoTime();
                var c=new android.content.ContextWrapper(getTargetContext()){
                    @Override public android.content.SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences(prefix+name,mode);}
                };
                var old=new AlbumLibrary.Album(Uri.parse("content://test/old"),"old",1,1);
                var fresh=new AlbumLibrary.Album(Uri.parse("content://test/new"),"new",1,1);
                var revision=new AlbumLibrary.Album(Uri.parse("content://test/revision"),"new",2,2);
                try{
                    AlbumAddedOrder.observe(c,List.of(old),true);AlbumAddedOrder.observe(c,List.of(fresh),false);
                    long first=AlbumAddedOrder.get(c,fresh);
                    AlbumAddedOrder.observe(c,List.of(old,fresh),false);
                    if(AlbumAddedOrder.get(c,old)!=0||first<=0||AlbumAddedOrder.get(c,fresh)!=first)throw new AssertionError("First seen");
                    AlbumAddedOrder.bindSync(c,"id",fresh);AlbumAddedOrder.bindSync(c,"id",revision);
                    if(AlbumAddedOrder.get(c,revision)!=first)throw new AssertionError("Revision identity");
                }finally{c.getSharedPreferences("album-added-v1",0).edit().clear().commit();}
                result.putString("recentOrder","PASS baseline, repeat scan, sync revision identity");finish(-1,result);return;
            }
            if(testMode.equals("favoriteSync")){FavoriteSyncTest.run(getTargetContext());result.putString("favoriteSync","PASS first/always/never/one-shot, Android edits, revision binding, legacy, CUE");finish(-1,result);return;}
            if(testMode.equals("lyrics")){
                var entries=new org.json.JSONArray().put(new org.json.JSONObject().put("file","disc.flac · Track 2").put("title","Song").put("number",2).put("disc",1).put("text","[ar:Artist]\n[00:01.00]一行目\n[00:02.00][00:03.00]二行目"));
                byte[] data=new org.json.JSONObject().put("format","virtual-cd-lyrics").put("version",1).put("tracks",entries).toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
                var extras=new Bundle();extras.putString(TrackProperties.FILE,"disc.flac · Track 2");
                var item=new MediaItem.Builder().setMediaMetadata(new MediaMetadata.Builder().setTitle("Song").setTrackNumber(2).setDiscNumber(1).setExtras(extras).build()).build();
                if(!LyricsStore.find(data,item).equals("一行目\n二行目"))throw new AssertionError("CUE/LRC lyrics lookup");
                var restored=item.buildUpon().setMediaMetadata(item.mediaMetadata.buildUpon().setExtras(null).build()).build();
                if(!LyricsStore.find(data,restored).equals("一行目\n二行目"))throw new AssertionError("Restored queue fallback");
                if(!LyricsStore.find(data,item.buildUpon().setMediaMetadata(new MediaMetadata.Builder().setTitle("Other").build()).build()).isEmpty())throw new AssertionError("Wrong song lyrics");
                result.putString("lyrics","PASS CUE filename binding, LRC text, restored queue and unmatched song");finish(-1,result);return;
            }
            if(testMode.equals("syncStatusLayout")){
                runOnMainSync(()->{
                    for(int width:new int[]{320,640}){
                        var view=new SyncStatusView(getTargetContext());view.setLayoutParams(new android.widget.LinearLayout.LayoutParams(width,-2));int height=-1;
                        for(String message:new String[]{"PCに接続しています…","PCから転送中 · 38%\n受信 168 MiB · 56 / 100ファイル\n"+"長いファイル名".repeat(40),"PCからの転送完了","同期を中断しました。\n詳細".repeat(5)}){
                            view.showProgress(message);
                            view.measure(android.view.View.MeasureSpec.makeMeasureSpec(width,android.view.View.MeasureSpec.EXACTLY),android.view.View.MeasureSpec.makeMeasureSpec(2000,android.view.View.MeasureSpec.AT_MOST));
                            if(height<0)height=view.getMeasuredHeight();
                            if(height!=view.getMeasuredHeight())throw new AssertionError("Status height changed");
                            int expected=message.startsWith("PCからの転送完了")?0xffa5d6a7:0xffffb74d;
                            if(((android.graphics.drawable.ColorDrawable)view.getBackground()).getColor()!=expected)throw new AssertionError("Status color");
                        }
                    }
                });
                result.putString("syncStatusLayout","PASS fixed height at two widths: connecting, long progress, completion, error; orange/green backgrounds");finish(-1,result);return;
            }
            if(testMode.equals("artworkLimit")){
                jp.virtualcd.player.library.ArtworkLimitTest.run(getTargetContext());
                result.putString("artworkLimit","PASS 9MB document/STORED/DEFLATED, 32MB boundary, oversize/truncated/expanded-size rejection and cache cleanup");finish(-1,result);return;
            }
            if(testMode.equals("imageFolder")){
                if(!ArtworkLoader.imageFolder("Images")||!ArtworkLoader.imageFolder("IMAGE")||ArtworkLoader.imageFolder("Music"))throw new AssertionError("Folder rules");
                var c=getTargetContext();var root=Uri.parse("content://com.android.externalstorage.documents/tree/6264-6230%3AMusic/document/6264-6230%3AMusic");
                var album=MobileSync.cached(c,root).stream().filter(a->a.name.contains("Pimp Your Past")).findFirst().orElseThrow();
                var pictures=ArtworkLoader.listImages(c,album);if(pictures.size()!=20)throw new AssertionError("Expected 20 images, got "+pictures.size());
                var cover=ArtworkLoader.load(c,album,"auto");if(cover==null)throw new AssertionError("Cover decode");
                var file=new java.io.File(new java.io.File(c.getFilesDir(),"cases3d"),jp.virtualcd.player.case3d.CasePackage.hash(album.uri.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8))+".vcd3d");
                byte[] bytes;try(var input=new java.io.FileInputStream(file)){bytes=jp.virtualcd.player.case3d.CasePackage.readBytes(input,jp.virtualcd.player.case3d.CasePackage.MAX_BYTES);}
                var front=jp.virtualcd.player.case3d.CasePackage.frontImage(bytes);if(front==null)throw new AssertionError("Windows front absent");
                var thumbnail=ArtworkLoader.class.getDeclaredMethod("thumbnail",byte[].class,String.class);thumbnail.setAccessible(true);
                var expected=(android.graphics.Bitmap)thumbnail.invoke(null,front,"full");if(!cover.sameAs(expected))throw new AssertionError("Cover did not prioritize Windows Front");expected.recycle();cover.recycle();
                var page=ArtworkLoader.galleryImage(c,pictures.get(4));if(page==null)throw new AssertionError("Gallery decode");page.recycle();
                result.putString("imageFolder","PASS Pimp Your Past: exact Windows Front thumbnail, 20 images and booklet decoded");finish(-1,result);return;
            }
            if(testMode.equals("discPull")){
                var g=new jp.virtualcd.player.case3d.DiscPullGesture();
                if(!g.begin(7,100,100,0,9,200,100,1))throw new AssertionError("Hub/edge grab");
                if(g.move(9,210,100,7,102,100,1))throw new AssertionError("Jitter triggered");
                if(!g.move(9,232,100,7,102,100,1)||g.move(9,250,100,7,102,100,1))throw new AssertionError("One extraction per grab");
                if(!g.begin(9,200,100,1,7,100,100,0))throw new AssertionError("Reverse finger order");
                if(g.move(7,125,100,9,240,100,1)||g.move(7,100,100,9,240,100,1))throw new AssertionError("Moving hub cancels");
                if(g.begin(7,100,100,.5f,9,200,100,1)||g.begin(7,100,100,Float.NaN,9,200,100,1))throw new AssertionError("Non-disc pinch captured");
                g.begin(7,100,100,0,9,200,100,1);g.cancel();if(g.move(7,100,100,9,240,100,1))throw new AssertionError("Cancelled touch");g.reset();if(g.captured())throw new AssertionError("Reset");
                result.putString("discPull","PASS hub/edge, either order, jitter, one-shot, cancellation and ordinary pinch exclusion");finish(-1,result);return;
            }
            if(testMode.equals("cueFlac")){
                String cue="PERFORMER \"Frozen Crown\"\nTITLE \"The Fallen King\"\nFILE \"test.flac\" WAVE\nTRACK 01 AUDIO\nTITLE \"Fail No More\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"To Infinity\"\nINDEX 01 04:09:70\n";
                var album=CueTracks.parse(cue);if(album.tracks.size()!=2||!album.tracks.get(1).title.equals("To Infinity"))throw new AssertionError("Cue tags");
                var t=album.tracks.get(1);t.uri=Uri.parse("content://test/album.flac");t.properties.putString(CueTracks.END,"500000");t.properties.putString(CueTracks.URI,t.uri.toString());
                var item=t.item();var restored=ListeningState.decode(ListeningState.encode(item));
                if(item.clippingConfiguration.startPositionMs!=249933||restored.clippingConfiguration.startPositionMs!=249933||restored.clippingConfiguration.endPositionMs!=500000||!restored.localConfiguration.uri.equals(t.uri))throw new AssertionError("Clip restoration");
                for(String invalid:new String[]{cue.replace("test.flac","../test.flac"),cue.replace("04:09:70","00:00:00"),cue.replace("INDEX 01 04:09:70","")}){boolean rejected=false;try{CueTracks.parse(invalid);}catch(Exception e){rejected=true;}if(!rejected)throw new AssertionError("Invalid CUE accepted");}
                result.putString("cueFlac","PASS metadata, 75-fps boundaries, clipping persistence and invalid path/boundary rejection");finish(-1,result);return;
            }
            if(testMode.equals("syncProgress")){
                var meter=new SyncProgress(4*1048576L,2,s->{});meter.reused(1048576L);meter.received(1048576L,"test.mp3");
                String half=meter.text("転送中","");if(!half.contains("50%")||!half.contains("2.0 MiB / 4.0 MiB")||!half.contains("受信 1.0 MiB")||!half.contains("再利用 1.0 MiB"))throw new AssertionError(half);
                meter.received(2*1048576L,"test.mp3");meter.completed();String full=meter.text("転送完了","");if(!full.contains("100%")||!full.contains("2 / 2ファイル"))throw new AssertionError(full);
                if(!new SyncProgress(0,0,s->{}).text("完了","").contains("100%"))throw new AssertionError("Zero byte total");
                if(!SyncProgress.size(3L*1024*1024*1024).equals("3.00 GiB"))throw new AssertionError("Large size");
                result.putString("syncProgress","PASS bytes, percentage, reused vs received, file count, zero total and GiB");finish(-1,result);return;
            }
            if(testMode.equals("syncRace")){
                var empty=new ArrayList<String>();var restored=new ArrayList<String>();
                for(int i=0;i<122;i++)restored.add("album-"+i);
                if(MainActivity.canApplyLibrarySync(empty,empty,true,false))throw new AssertionError("Sync during restore");
                if(MainActivity.canApplyLibrarySync(empty,restored,false,false))throw new AssertionError("Stale empty snapshot accepted");
                if(MainActivity.canApplyLibrarySync(restored,new ArrayList<>(restored),false,false))throw new AssertionError("Replaced snapshot accepted");
                if(MainActivity.canApplyLibrarySync(restored,restored,false,true))throw new AssertionError("Sync during scan");
                if(!MainActivity.canApplyLibrarySync(restored,restored,false,false))throw new AssertionError("Current snapshot rejected");
                result.putString("syncRace","PASS restore/scan gates and stale snapshot rejection, including 122-album restore");finish(-1,result);return;
            }
            if(testMode.equals("qr")){
                android.graphics.Bitmap bitmap;try(var stream=getContext().getAssets().open("sync-qr.png")){bitmap=android.graphics.BitmapFactory.decodeStream(stream);}
                int width=bitmap.getWidth(),height=bitmap.getHeight();int[] pixels=new int[width*height];bitmap.getPixels(pixels,0,width,0,0,width,height);bitmap.recycle();
                var image=new com.google.zxing.BinaryBitmap(new com.google.zxing.common.HybridBinarizer(new com.google.zxing.RGBLuminanceSource(width,height,pixels)));
                String decoded=new com.google.zxing.qrcode.QRCodeReader().decode(image).getText();
                String expected="http://192.168.11.63:50703/0123456789abcdef0123456789abcdef0123456789abcdef/";
                if(!decoded.equals(expected)||!SyncDownload.validateAddress(decoded).equals(expected))throw new AssertionError("QR round trip");
                for(String bad:new String[]{"https://example.com/","http://8.8.8.8/"+"a".repeat(48)+"/","javascript:alert(1)","http://192.168.1.1/invalid"}){boolean rejected=false;try{SyncDownload.validateAddress(bad);}catch(Exception ex){rejected=true;}if(!rejected)throw new AssertionError("Unsafe QR accepted");}
                result.putString("qr","PASS Windows QRCoder -> Android ZXing exact URL, LAN validation and non-sync QR rejection (camera optics not tested)");finish(-1,result);return;
            }
            if(testMode.equals("sync")){SyncDeviceChecks.run(getTargetContext(),syncAddress);result.putString("sync","PASS LAN download, checksum, unchanged skip, automatic GLB binding and incomplete-transfer rejection");finish(-1,result);return;}
            if(testMode.equals("desktopGlb")){CaseDeviceChecks.runDesktop(this);result.putString("desktopGlb","PASS desktop geometry GPU poses, controls, lifecycle and malformed rejection");finish(-1,result);return;}
            if(testMode.equals("glb")||testMode.equals("glbReal")){CaseDeviceChecks.runGlb(this,testMode.equals("glbReal"));result.putString("glb","PASS legacy/GLB image comparison closed/open/disc, controls, reset, context restore and malformed GLB rejection");finish(-1,result);return;}
            if(testMode.equals("albumOrder")){testAlbumOrder();result.putString("albumOrder","PASS album/artist order, artist ties, unknown last, filter and persisted choice");finish(-1,result);return;}
            if(testMode.equals("nowPlayingLabel")){
                var m=new MediaMetadata.Builder().setTitle("Erotomania").setArtist("Dream Theater").setAlbumArtist("Various Artists").build();
                if(!MainActivity.nowPlayingLabel(m,false).equals("Erotomania\nDream Theater")||!MainActivity.nowPlayingLabel(m,true).equals("Erotomania · Dream Theater"))throw new AssertionError("Artist and orientation");
                m=m.buildUpon().setArtist(" ").build();if(!MainActivity.nowPlayingLabel(m,false).equals("Erotomania\nVarious Artists"))throw new AssertionError("Album artist fallback");
                m=m.buildUpon().setAlbumArtist(null).build();if(!MainActivity.nowPlayingLabel(m,false).equals("Erotomania"))throw new AssertionError("Missing artist");
                if(!MainActivity.nowPlayingLabel(MediaMetadata.EMPTY,false).equals("停止中"))throw new AssertionError("Empty metadata");
                result.putString("nowPlayingLabel","PASS artist, album artist fallback, portrait/landscape, empty metadata");finish(-1,result);return;
            }
            if(testMode.equals("caseLayout")){testCaseLayout();result.putString("caseLayout","PASS landscape full-height viewport and portrait restoration");finish(-1,result);return;}
            if(testMode.equals("layout")){testLayout(result);finish(-1,result);return;}
            if(testMode.equals("timer")){testTimer(result);finish(-1,result);return;}
            if(testMode.equals("cacheWrite")||testMode.equals("cacheRead")){TagCacheChecks.run(getTargetContext(),testMode.equals("cacheWrite"));result.putString("cache","PASS "+testMode+" pid="+android.os.Process.myPid());finish(-1,result);return;}
            if(testMode.equals("resumeUi")){testResumeUi(result);finish(-1,result);return;}
            if(loadingOnly){testAlbumLoading(result);finish(-1,result);return;}
            if(caseOnly){CaseDeviceChecks.run(this);result.putString("case3d","PASS Windows export round-trip, schema/hash/path/size rejection, GPU closed/open/rotation/landscape/context recreation");finish(-1,result);return;}
            SoundDeviceChecks.run(getTargetContext());result.putString("sound","PASS settings persistence, PCM bypass/live switch, limiter, EOS, format change");
            testListeningState();result.putString("listeningState","PASS queue/position/shuffle/repeat restore, favorites, newest-first history");
            if(stateOnly){result.putString("stream","PASS listening state tests (isolated test preferences)\n");finish(-1,result);return;}
            var context=getTargetContext();String tree=context.getSharedPreferences("MainActivity",0).getString("tree",null);
            if(tree==null)throw new AssertionError("No user-authorized tree");
            var albums=AlbumLibrary.scan(context,Uri.parse(tree),count->{});
            long dirs=albums.stream().filter(a->a.directory).count();result.putLong("directoryAlbums",dirs);result.putInt("allAlbums",albums.size());
            AlbumLibrary.Album flac=null,mp3=null,zip=null;
            for(var album:albums){
                if(!album.directory){if(zip==null)zip=album;continue;}
                for(var file:AlbumLibrary.children(context,album.uri)){
                    if(file.name.toLowerCase(Locale.ROOT).endsWith(".flac")&&flac==null)flac=file;
                    if(AudioFormats.audio(file.name)&&file.name.toLowerCase(Locale.ROOT).endsWith(".mp3")&&mp3==null)mp3=file;
                }
                if(flac!=null&&mp3!=null&&zip!=null)break;
            }
            if(flac==null||mp3==null||zip==null)throw new AssertionError("Need FLAC, regular MP3 and ZIP samples");
            for(var file:new AlbumLibrary.Album[]{flac,mp3,zip}){
                var tracks=AlbumTracks.load(context,file);if(tracks.tracks.isEmpty())throw new AssertionError("Empty playlist");
                var properties=tracks.tracks.get(0).item().mediaMetadata.extras;
                if(properties==null||properties.getLong(TrackProperties.SIZE)<=0||properties.getString(TrackProperties.FILE)==null||Long.parseLong(properties.getString(TrackProperties.DURATION,"0"))<=0)throw new AssertionError("Real-file properties missing");
                testPlayback(tracks.tracks.get(0).item());result.putString(file==flac?"FLAC":file==mp3?"MP3":"ZIP","PASS DSP decode, position advances, seek");
            }
            var sample=AlbumTracks.load(context,mp3).tracks.get(0).item();testBoundaries(sample,true,Player.REPEAT_MODE_OFF);testBoundaries(sample,false,Player.REPEAT_MODE_OFF);
            testBoundaries(sample,false,Player.REPEAT_MODE_ONE);testBoundaries(sample,false,Player.REPEAT_MODE_ALL);result.putString("boundaries","PASS gapless ON/OFF, last track, repeat one/all");
            result.putString("stream","PASS: folder scan + FLAC/MP3/ZIP decode and seek; muted; source files unchanged\n");finish(-1,result);
        }catch(Throwable e){result.putString("stream","FAIL: "+e+"\n");finish(0,result);}
    }
    private void testAlbumOrder(){runOnMainSync(()->{
        var context=getTargetContext();var pref=context.getSharedPreferences("album-order",0);boolean had=pref.contains("artist"),old=pref.getBoolean("artist",false);
        var index=context.getSharedPreferences("album-artist-index",0);
        var a=new AlbumLibrary.Album(Uri.parse("content://sort-test/a"),"Beta.zip.mp3",1,1);
        var b=new AlbumLibrary.Album(Uri.parse("content://sort-test/b"),"Alpha.zip.mp3",1,1);
        var c=new AlbumLibrary.Album(Uri.parse("content://sort-test/c"),"Gamma.zip.mp3",1,1);
        var d=new AlbumLibrary.Album(Uri.parse("content://sort-test/d"),"Delta.zip.mp3",1,1);
        index.edit().putString(a.key(),"Artist A").putString(b.key(),"Artist Z").putString(c.key(),"Artist A").putString(d.key(),"アーティスト情報なし").commit();
        AlbumAdapter adapter=new AlbumAdapter(context);
        try{
            adapter.setAlbums(java.util.Arrays.asList(d,c,b,a),"");adapter.setArtistOrder(false);
            if(adapter.getItem(0)!=b||adapter.getItem(1)!=a)throw new AssertionError("Album order");
            adapter.setArtistOrder(true);
            if(adapter.getItem(0)!=a||adapter.getItem(1)!=c||adapter.getItem(2)!=b||adapter.getItem(3)!=d)throw new AssertionError("Artist order/ties/unknown");
            adapter.filter("alpha");if(adapter.getCount()!=1||adapter.getItem(0)!=b)throw new AssertionError("Sort filter");
            adapter.filter("");if(adapter.getItem(0)!=a)throw new AssertionError("Filter reset");
            try(AlbumAdapter restored=new AlbumAdapter(context)){if(!restored.isArtistOrder())throw new AssertionError("Saved order");}
            adapter.setArtistOrder(false);if(adapter.getItem(0)!=b)throw new AssertionError("Switch back");
        }finally{adapter.close();var edit=pref.edit();if(had)edit.putBoolean("artist",old);else edit.remove("artist");edit.commit();index.edit().remove(a.key()).remove(b.key()).remove(c.key()).remove(d.key()).commit();}
    });}
    private void testCaseLayout()throws Exception{
        var intent=new android.content.Intent(getTargetContext(),jp.virtualcd.player.case3d.CaseActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("album","content://case-layout-test/album").putExtra("title","Test album");
        var activity=startActivitySync(intent);
        try{for(int orientation:new int[]{0,1,0}){
            runOnMainSync(()->activity.setRequestedOrientation(orientation));Thread.sleep(800);waitForIdleSync();
            runOnMainSync(()->{try{
                var type=activity.getClass();var cf=type.getDeclaredField("content");cf.setAccessible(true);var sf=type.getDeclaredField("shell");sf.setAccessible(true);var wf=type.getDeclaredField("wide");wf.setAccessible(true);
                var content=(android.view.View)cf.get(activity);var shell=(android.view.View)sf.get(activity);boolean wide=(Boolean)wf.get(activity);
                if(wide!=(orientation==0))throw new AssertionError("3D orientation");
                int usable=shell.getHeight()-shell.getPaddingTop()-shell.getPaddingBottom();
                if(wide&&content.getHeight()<usable*.95f)throw new AssertionError("3D viewport not full height");
                if(content.getHeight()<100||content.getWidth()<100)throw new AssertionError("3D viewport collapsed");
            }catch(ReflectiveOperationException ex){throw new AssertionError(ex);}});
        }}finally{runOnMainSync(activity::finish);}
    }
    private void testAlbumLoading(Bundle output)throws Exception{
        var context=getTargetContext();String tree=context.getSharedPreferences("MainActivity",0).getString("tree",null);
        if(tree==null)throw new AssertionError("No authorized library");
        var albums=AlbumLibrary.scan(context,Uri.parse(tree),count->{});
        AlbumLibrary.Album dir=null,zip=null;
        for(var album:albums){if(album.directory&&dir==null)dir=album;if(!album.directory&&zip==null)zip=album;}
        if(dir==null||zip==null)throw new AssertionError("Need directory and ZIP albums");
        for(var selected:new AlbumLibrary.Album[]{dir,zip}){
            var source=AlbumLibrary.describe(context,selected.uri);int[] state={0,0};long[] previewAt={0};long start=SystemClock.elapsedRealtime();
            AlbumTracks.Progress progress=new AlbumTracks.Progress(){
                public void preview(AlbumTracks preview){state[0]=preview.tracks.size();previewAt[0]=SystemClock.elapsedRealtime()-start;if(state[0]==0)throw new AssertionError("Empty preview");}
                public void update(int count,int total){if(total!=state[0]||count<=state[1])throw new AssertionError("Invalid progress");state[1]=count;}
            };
            var cold=AlbumTracks.load(context,source,true,progress);long coldMs=SystemClock.elapsedRealtime()-start;
            if(state[0]!=cold.tracks.size()||state[1]!=cold.tracks.size())throw new AssertionError("Preview/progress mismatch");
            long warmStart=SystemClock.elapsedRealtime();var warm=AlbumTracks.load(context,source,false,null);long warmMs=SystemClock.elapsedRealtime()-warmStart;
            if(cold!=warm)throw new AssertionError("Cache missed unchanged source");
            var forced=AlbumTracks.load(context,source,true,null);if(forced==warm)throw new AssertionError("Refresh did not bypass cache");
            var changed=new AlbumLibrary.Album(source.uri,source.name,source.size,source.modified+1,source.directory);
            if(AlbumTracks.load(context,changed,false,null)==forced)throw new AssertionError("Changed signature reused cache");
            Thread.currentThread().interrupt();boolean cancelled=false;
            try{AlbumTracks.load(context,source,true,null);}catch(java.io.InterruptedIOException expected){cancelled=true;}finally{Thread.interrupted();}
            if(!cancelled)throw new AssertionError("Cancelled load continued");
            output.putString(source.directory?"directoryLoading":"zipLoading","PASS preview="+previewAt[0]+"ms cold="+coldMs+"ms warm="+warmMs+"ms tracks="+cold.tracks.size()+"; refresh, invalidation, cancellation");
        }
    }
    private void testResumeUi(Bundle output)throws Exception{
        var files=new java.io.File(getTargetContext().getFilesDir(),"album-tags-v1").listFiles((d,n)->n.endsWith(".json"));
        if(files==null||files.length==0)throw new AssertionError("Run loading test first");
        var root=new org.json.JSONObject(new String(java.nio.file.Files.readAllBytes(files[0].toPath()),java.nio.charset.StandardCharsets.UTF_8));
        var source=Uri.parse(root.getString("source"));var saved=AlbumTagCache.read(getTargetContext(),source);
        if(saved==null)throw new AssertionError("Missing snapshot");
        var intent=new android.content.Intent(getTargetContext(),MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
        MainActivity activity=(MainActivity)startActivitySync(intent);
        var cancel=MainActivity.class.getDeclaredMethod("cancelAlbumLoad");cancel.setAccessible(true);
        var load=MainActivity.class.getDeclaredMethod("loadAlbum",Uri.class,String.class,boolean.class);load.setAccessible(true);
        var workerField=MainActivity.class.getDeclaredField("worker");workerField.setAccessible(true);
        var tracksField=MainActivity.class.getDeclaredField("tracks");tracksField.setAccessible(true);
        var titleField=MainActivity.class.getDeclaredField("selectedAlbumTitle");titleField.setAccessible(true);
        var entered=new CountDownLatch(1);var release=new CountDownLatch(1);
        try{
            runOnMainSync(()->{try{cancel.invoke(activity);}catch(Exception e){throw new RuntimeException(e);}});
            ((ExecutorService)workerField.get(activity)).submit(()->{entered.countDown();try{release.await(15,TimeUnit.SECONDS);}catch(InterruptedException ignored){}});
            if(!entered.await(15,TimeUnit.SECONDS))throw new AssertionError("Worker did not become idle");
            long start=SystemClock.elapsedRealtime();
            runOnMainSync(()->{try{load.invoke(activity,source,null,false);}catch(Exception e){throw new RuntimeException(e);}});
            boolean[] ready={false};
            while(!ready[0]&&SystemClock.elapsedRealtime()-start<3000){
                runOnMainSync(()->{try{var list=(android.widget.ListView)tracksField.get(activity);ready[0]=list.isEnabled()&&list.getCount()==saved.album.tracks.size()&&saved.album.title.equals(titleField.get(activity));}catch(Exception e){throw new RuntimeException(e);}});
                if(!ready[0])Thread.sleep(20);
            }
            if(!ready[0])throw new AssertionError("Cached playable list waited for blocked metadata worker");
            output.putString("resumeUi","PASS cached list enabled in "+(SystemClock.elapsedRealtime()-start)+"ms while metadata worker blocked");
        }finally{runOnMainSync(()->{try{cancel.invoke(activity);}catch(Exception ignored){}activity.finish();});release.countDown();}
    }
    private void testTimer(Bundle result)throws Exception{
        var context=getTargetContext();var prefs=AutoStopSettings.preferences(context);
        var previous=new java.util.HashMap<String,Object>(prefs.getAll());
        var disconnected=new CountDownLatch(1);var failure=new AtomicReference<Throwable>();
        var controller=new AtomicReference<androidx.media3.session.MediaController>();
        var future=new AtomicReference<com.google.common.util.concurrent.ListenableFuture<androidx.media3.session.MediaController>>();
        var info=AlbumIndicators.summarize(Arrays.asList("01.FLAC","02.mp3","cover.jpg","archive.zip.mp3","notes.txt"));
        if(info.count!=2||!info.formats.equals("FLAC/MP3"))throw new AssertionError("Album indicator counts non-audio files");
        var isolated=context.getSharedPreferences("timer-default-test",0);isolated.edit().clear().commit();
        if(!AutoStopSettings.enabled(isolated)||AutoStopSettings.minutes(isolated)!=180)throw new AssertionError("Timer defaults");
        try{
            // Simulate nearly three hours already played, without changing the configured duration.
            prefs.edit().putBoolean("enabled",true).putInt("minutes",180).putLong("used",180*60000L-2000).commit();
            runOnMainSync(()->future.set(new androidx.media3.session.MediaController.Builder(context,new androidx.media3.session.SessionToken(context,new android.content.ComponentName(context,PlaybackService.class)))
                .setListener(new androidx.media3.session.MediaController.Listener(){@Override public void onDisconnected(androidx.media3.session.MediaController c){disconnected.countDown();}}).buildAsync()));
            controller.set(future.get().get(10,TimeUnit.SECONDS));
            runOnMainSync(()->{try{var c=controller.get();if(c.getMediaItemCount()==0)throw new AssertionError("No saved playback queue");c.setVolume(0);c.prepare();c.play();}catch(Throwable e){failure.set(e);}});
            if(failure.get()!=null)throw new AssertionError(failure.get());
            if(!disconnected.await(30,TimeUnit.SECONDS))throw new AssertionError("Timer did not release playback session");
            if(prefs.getLong("used",-1)!=0)throw new AssertionError("Timer budget did not reset");
            if(!"timer".equals(prefs.getString("lastStopReason",""))||prefs.getLong("lastStoppedAt",0)<=0||prefs.getLong("lastStopLimit",0)!=180*60000L||prefs.getLong("lastStopUsed",0)<180*60000L||!prefs.getBoolean("stopNoticePending",false))throw new AssertionError("Timer completion evidence missing");
            if(!TimerNotice.summary(prefs).contains("180分"))throw new AssertionError("Timer summary");
            var notifications=context.getSystemService(android.app.NotificationManager.class);
            // Session release precedes announcement; notification posting also crosses a process boundary.
            runOnMainSync(()->{});
            if(notifications.areNotificationsEnabled()){
                long deadline=android.os.SystemClock.elapsedRealtime()+5000;
                while(java.util.Arrays.stream(notifications.getActiveNotifications()).noneMatch(n->n.getId()==TimerNotice.ID)&&android.os.SystemClock.elapsedRealtime()<deadline)Thread.sleep(50);
                if(java.util.Arrays.stream(notifications.getActiveNotifications()).noneMatch(n->n.getId()==TimerNotice.ID))throw new AssertionError("Timer notification missing");
            }
            result.putString("timer","PASS default 3h ON, live expiry, session release, budget reset, durable reason/time, next-launch pending and posted notification");
        }finally{
            runOnMainSync(()->{if(controller.get()!=null)controller.get().release();else if(future.get()!=null)androidx.media3.session.MediaController.releaseFuture(future.get());});
            context.stopService(new android.content.Intent(context,PlaybackService.class));
            var edit=prefs.edit().clear();for(var entry:previous.entrySet()){Object value=entry.getValue();if(value instanceof Boolean)edit.putBoolean(entry.getKey(),(Boolean)value);else if(value instanceof Integer)edit.putInt(entry.getKey(),(Integer)value);else if(value instanceof Long)edit.putLong(entry.getKey(),(Long)value);else if(value instanceof String)edit.putString(entry.getKey(),(String)value);}edit.commit();
            context.getSystemService(android.app.NotificationManager.class).cancel(TimerNotice.ID);
            isolated.edit().clear().commit();
        }
    }
    private Object field(Object target,String name)throws Exception{var f=target.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(target);}
    private void testLayout(Bundle output)throws Exception{
        MainActivity activity=(MainActivity)startActivitySync(new android.content.Intent(getTargetContext(),MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
        var show=MainActivity.class.getDeclaredMethod("showLibrary");show.setAccessible(true);
        var failure=new AtomicReference<Throwable>();
        try{
            for(boolean wide:new boolean[]{true,false,true}){
                runOnMainSync(()->activity.setRequestedOrientation(wide?android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE:android.content.pm.ActivityInfo.SCREEN_ORIENTATION_PORTRAIT));
                boolean[] ready={false};long deadline=SystemClock.elapsedRealtime()+8000;
                while(!ready[0]&&SystemClock.elapsedRealtime()<deadline){runOnMainSync(()->{try{var root=(android.view.View)field(activity,"rootLayout");ready[0]=root.getWidth()>0&&(root.getWidth()>root.getHeight())==wide;}catch(Exception e){failure.set(e);}});Thread.sleep(30);}
                if(failure.get()!=null)throw new AssertionError(failure.get());if(!ready[0])throw new AssertionError("Rotation did not settle");
                runOnMainSync(()->{try{show.invoke(activity);}catch(Exception e){failure.set(e);}});waitForIdleSync();
                runOnMainSync(()->{try{
                    var grid=(android.widget.GridView)field(activity,"albums");if(grid.getNumColumns()!=(wide?2:1)||grid.getHeight()<=0)throw new AssertionError("Album columns/height");
                    var search=(android.view.View)field(activity,"search");var slot=(android.view.View)field(activity,"headerTitleSlot");
                    if(wide&&search.getParent()!=slot)throw new AssertionError("Landscape search not inside header");
                    if(!wide&&search.getParent()!=field(activity,"rootLayout"))throw new AssertionError("Portrait search not restored");
                    int[] slotAt=new int[2],gridAt=new int[2];slot.getLocationOnScreen(slotAt);grid.getLocationOnScreen(gridAt);
                    if(wide&&gridAt[1]>slotAt[1]+slot.getHeight()+40*getTargetContext().getResources().getDisplayMetrics().density)throw new AssertionError("Header still wastes vertical space");
                    if(wide&&grid.getChildCount()>1&&grid.getChildAt(1).getLeft()<=grid.getChildAt(0).getLeft())throw new AssertionError("Columns overlap");
                    // Reuse existing views; no album load, player mutation or metadata refresh on rotation.
                    var visible=MainActivity.class.getDeclaredField("libraryVisible");visible.setAccessible(true);visible.setBoolean(activity,false);
                    var navigation=MainActivity.class.getDeclaredMethod("updateNavigation");navigation.setAccessible(true);navigation.invoke(activity);
                    grid.setVisibility(android.view.View.GONE);((android.view.View)field(activity,"search")).setVisibility(android.view.View.GONE);
                    ((android.view.View)field(activity,"trackPane")).setVisibility(android.view.View.VISIBLE);
                    ((android.view.View)field(activity,"albumHeader")).setVisibility(android.view.View.VISIBLE);
                    ((android.view.View)field(activity,"tracks")).setVisibility(android.view.View.VISIBLE);
                }catch(Throwable e){failure.set(e);}});waitForIdleSync();Thread.sleep(100);
                runOnMainSync(()->{try{
                    var cover=(android.view.View)field(activity,"albumHeader");var tracks=(android.view.View)field(activity,"tracks");
                    var controls=(android.view.View)field(activity,wide?"sideRail":"playbackControls");var root=(android.view.View)field(activity,"screenLayout");
                    if(wide&&((android.view.View)field(activity,"playbackControls")).getParent()!=field(activity,"sideColumns"))throw new AssertionError("Playback buttons not in sidebar");
                    if(wide&&((android.view.View)field(activity,"headingRow")).getVisibility()!=android.view.View.GONE)throw new AssertionError("Track header still occupies height");
                    if(!wide&&((android.view.View)field(activity,"playbackControls")).getParent()!=field(activity,"playbackPane"))throw new AssertionError("Portrait controls not restored");
                    if(cover.getHeight()<40||tracks.getHeight()<40||controls.getHeight()<40)throw new AssertionError("Content collapsed");
                    if(wide?tracks.getLeft()<cover.getRight():tracks.getTop()<cover.getBottom())throw new AssertionError("Album and tracks overlap");
                    int[] controlAt=new int[2],rootAt=new int[2];controls.getLocationOnScreen(controlAt);root.getLocationOnScreen(rootAt);
                    if(controlAt[0]<rootAt[0]||controlAt[0]+controls.getWidth()>rootAt[0]+root.getWidth()||controlAt[1]+controls.getHeight()>rootAt[1]+root.getHeight())throw new AssertionError("Playback controls clipped");
                }catch(Throwable e){failure.set(e);}});
                if(failure.get()!=null)throw new AssertionError(failure.get());
            }
            output.putString("layout","PASS landscape 2 columns, side-by-side track pane, portrait restore, controls within screen");
        }finally{runOnMainSync(()->{activity.setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);activity.finish();});}
    }
    private void testListeningState()throws Exception{
        var context=getTargetContext();String namespace="listening-smoke-"+System.nanoTime();
        var failure=new AtomicReference<Throwable>();
        try{runOnMainSync(()->{
            ExoPlayer a=null,b=null;
            try{
                for(String icon:new String[]{"current","settings","back","history","star","refresh","play","pause","previous","next","stop","shuffle","repeat","repeat-one"}){
                    var button=new android.widget.Button(context);PlayerStyle.button(button);int height=PlayerStyle.dp(context,44);button.setLayoutParams(new android.widget.LinearLayout.LayoutParams(144,height));ControlIcon.button(button,icon,icon);
                    button.measure(android.view.View.MeasureSpec.makeMeasureSpec(144,android.view.View.MeasureSpec.EXACTLY),android.view.View.MeasureSpec.makeMeasureSpec(height,android.view.View.MeasureSpec.EXACTLY));button.layout(0,0,144,height);
                    var bitmap=android.graphics.Bitmap.createBitmap(144,height,android.graphics.Bitmap.Config.ARGB_8888);button.draw(new android.graphics.Canvas(bitmap));int lines=0,minY=height,maxY=0,minX=144,maxX=0;
                    for(int y=0;y<height;y++)for(int x=0;x<144;x++){int color=bitmap.getPixel(x,y);if(android.graphics.Color.red(color)>150&&android.graphics.Color.green(color)>150){lines++;minY=Math.min(minY,y);maxY=Math.max(maxY,y);minX=Math.min(minX,x);maxX=Math.max(maxX,x);}}
                    bitmap.recycle();if(lines<20)throw new AssertionError("Invisible icon: "+icon);
                    if(Math.abs((minY+maxY)/2.0-height/2.0)>5||Math.abs((minX+maxX)/2.0-72)>8)throw new AssertionError("Uncentered icon: "+icon);
                }
                var source=new Bundle();source.putString(ListeningState.ALBUM_URI,"content://test/album");
                MediaItem first=new MediaItem.Builder().setUri("content://test/one.flac").setMediaId("content://test/one.flac")
                    .setMediaMetadata(new MediaMetadata.Builder().setTitle("曲名1").setExtras(source).build()).build();
                MediaItem second=new MediaItem.Builder().setUri("content://test/two.mp3").setMediaId("content://test/two.mp3")
                    .setMediaMetadata(new MediaMetadata.Builder().setTitle("曲名2").setExtras(source).build()).build();
                var store=new ListeningState(context,namespace);a=new ExoPlayer.Builder(context).build();
                a.setMediaItems(Arrays.asList(first,second),1,65000);a.setShuffleModeEnabled(true);a.setRepeatMode(Player.REPEAT_MODE_ALL);store.save(a);
                b=new ExoPlayer.Builder(context).build();new ListeningState(context,namespace).restore(b);
                if(b.getMediaItemCount()!=2||b.getCurrentMediaItemIndex()!=1||b.getCurrentPosition()!=65000||b.getPlayWhenReady()||!b.getShuffleModeEnabled()||b.getRepeatMode()!=Player.REPEAT_MODE_ALL)throw new AssertionError("Restore mismatch");
                if(!"content://test/album".equals(b.getCurrentMediaItem().mediaMetadata.extras.getString(ListeningState.ALBUM_URI)))throw new AssertionError("Lost album identity");
                if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=1)throw new AssertionError("Restored focus mismatch");
                if(MainActivity.playingIndex(Arrays.asList(second,first),b.getCurrentMediaItem())!=0)throw new AssertionError("Focus must match identity, not queue index");
                b.seekToPreviousMediaItem();if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=0)throw new AssertionError("Previous focus mismatch");
                b.seekToNextMediaItem();if(MainActivity.playingIndex(Arrays.asList(first,second),b.getCurrentMediaItem())!=1)throw new AssertionError("Next focus mismatch");
                if(MainActivity.playingIndex(Arrays.asList(first),second)!=-1)throw new AssertionError("Other album must not highlight");
                if(MainActivity.playingAlbum(null)!=null)throw new AssertionError("Empty now-playing destination");
                if(!Uri.parse("content://test/album").equals(MainActivity.playingAlbum(second)))throw new AssertionError("Now-playing album identity");
                var zipUri=ZipTrackDataSource.trackUri(Uri.parse("content://test/music.zip.mp3"),"02 Song.mp3");
                if(!Uri.parse("content://test/music.zip.mp3").equals(MainActivity.playingAlbum(new MediaItem.Builder().setMediaId(zipUri.toString()).build())))throw new AssertionError("Legacy ZIP destination");
                if(!Uri.parse("content://test/single.flac").equals(MainActivity.playingAlbum(new MediaItem.Builder().setUri("content://test/single.flac").build())))throw new AssertionError("Single-file destination");
                if(MainActivity.playingAlbum(new MediaItem.Builder().setMediaId("zipmp3:invalid").build())!=null)throw new AssertionError("Malformed destination");
                if(!"02".equals(TrackAdapter.number(second,1)))throw new AssertionError("Fallback track number");
                MediaItem numbered=first.buildUpon().setMediaMetadata(first.mediaMetadata.buildUpon().setTrackNumber(7).setDiscNumber(2).build()).build();
                if(!"2-07".equals(TrackAdapter.number(numbered,0))||ListeningState.decode(ListeningState.encode(numbered)).mediaMetadata.trackNumber!=7)throw new AssertionError("Tagged track number");
                var clicked=new AtomicReference<MediaItem>();var details=new AtomicReference<MediaItem>();var adapter=new TrackAdapter(context,Arrays.asList(numbered),store,clicked::set,details::set);
                android.view.ViewGroup row=(android.view.ViewGroup)adapter.getView(0,null,new android.widget.ListView(context));
                ((android.widget.Button)row.getChildAt(2)).performClick();if(clicked.get()!=numbered)throw new AssertionError("Star click target");
                clicked.set(null);row.getChildAt(3).performClick();if(details.get()!=numbered||clicked.get()!=null)throw new AssertionError("Properties must not toggle favorite");
                var properties=new Bundle();properties.putString(TrackProperties.FILE,"song.flac");properties.putString(TrackProperties.DURATION,"254000");properties.putString(TrackProperties.BITRATE,"900000");properties.putString(TrackProperties.RATE,"44100");properties.putLong(TrackProperties.SIZE,1048576);
                var propertyItem=numbered.buildUpon().setMediaMetadata(numbered.mediaMetadata.buildUpon().setExtras(properties).build()).build();String description=TrackProperties.describe(propertyItem);
                if(!description.contains("4:14")||!description.contains("900 kbps")||!description.contains("44.1 kHz")||!description.contains("song.flac")||!description.contains("1.00 MiB"))throw new AssertionError("Property formatting");
                if(!TrackProperties.describe(first).contains("不明／未設定"))throw new AssertionError("Unknown properties must not be invented");
                store.played(first);store.played(second);store.played(first);
                var history=new ListeningState(context,namespace).entries("history");
                if(history.size()!=2||!first.mediaId.equals(history.get(0).getString("id"))||history.get(0).getInt("plays")!=2)throw new AssertionError("History ordering/deduplication");
                store.toggle("favoriteTracks",ListeningState.encode(second));
                if(!new ListeningState(context,namespace).contains("favoriteTracks",second.mediaId))throw new AssertionError("Favorite not persisted");
                store.toggle("favoriteTracks",ListeningState.encode(second));if(store.contains("favoriteTracks",second.mediaId))throw new AssertionError("Favorite not removed");
                for(int i=0;i<205;i++)store.played(first.buildUpon().setMediaId("content://test/track"+i).build());
                if(store.entries("history").size()!=200)throw new AssertionError("History not bounded");
            }catch(Throwable e){failure.set(e);}finally{if(a!=null)a.release();if(b!=null)b.release();}
        });if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{context.getSharedPreferences(namespace,0).edit().clear().commit();}
    }
    private void testBoundaries(MediaItem item,boolean gapless,@Player.RepeatMode int repeat)throws Exception{
        CountDownLatch done=new CountDownLatch(1);AtomicReference<Throwable> failure=new AtomicReference<>();ExoPlayer[] player={null};Handler main=new Handler(Looper.getMainLooper());
        runOnMainSync(()->{var c=getTargetContext();var config=new jp.virtualcd.player.audio.SoundConfig(true,0,false,false,false,false,0,new int[10]);
            player[0]=new ExoPlayer.Builder(c,new jp.virtualcd.player.audio.SoundRenderers(c,new jp.virtualcd.player.audio.SoundProcessor(config)))
                .setMediaSourceFactory(new DefaultMediaSourceFactory(()->new DefaultDataSource(c,new ZipTrackDataSource(c)))).build();
            ExoPlayer p=player[0];p.setVolume(0);p.setPauseAtEndOfMediaItems(!gapless);new jp.virtualcd.player.audio.BoundaryPlayback(p);p.setRepeatMode(repeat);
            p.addListener(new Player.Listener(){int jumps;
                @Override public void onPlayerError(PlaybackException error){failure.set(error);done.countDown();}
                @Override public void onPositionDiscontinuity(Player.PositionInfo old,Player.PositionInfo current,int reason){
                    if(reason==Player.DISCONTINUITY_REASON_AUTO_TRANSITION||reason==Player.DISCONTINUITY_REASON_SEEK)jumps++;
                    if(repeat!=Player.REPEAT_MODE_OFF&&jumps>=3){if(repeat==Player.REPEAT_MODE_ONE&&p.getCurrentMediaItemIndex()!=0)failure.set(new AssertionError("Repeat one advanced"));
                        p.pause();main.postDelayed(()->{if(p.getPlayWhenReady())failure.set(new AssertionError("Explicit pause resumed"));done.countDown();},150);}
                }
                @Override public void onPlaybackStateChanged(int state){if(state==Player.STATE_ENDED&&repeat==Player.REPEAT_MODE_OFF){
                    if(p.getCurrentMediaItemIndex()!=1)failure.set(new AssertionError("Last track not reached"));done.countDown();}}
            });
            var clipped=item.buildUpon().setClippingConfiguration(new MediaItem.ClippingConfiguration.Builder().setStartPositionMs(1000).setEndPositionMs(1600).build()).build();
            p.setMediaItems(Arrays.asList(clipped,clipped.buildUpon().setMediaId(item.mediaId+"#test-next").build()));p.prepare();p.play();
        });
        try{if(!done.await(12,TimeUnit.SECONDS))throw new AssertionError("Boundary timeout gapless="+gapless+" repeat="+repeat);if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{runOnMainSync(()->{main.removeCallbacksAndMessages(null);player[0].release();});}
    }
    private void testPlayback(MediaItem item)throws Exception{
        CountDownLatch done=new CountDownLatch(1);AtomicReference<Throwable> failure=new AtomicReference<>();ExoPlayer[] player={null};
        Handler main=new Handler(Looper.getMainLooper());
        runOnMainSync(()->{
            var context=getTargetContext();var config=new jp.virtualcd.player.audio.SoundConfig(false,5,true,true,true,true,83,new int[]{1,2,3,0,-1,0,1,2,3,4});
            player[0]=new ExoPlayer.Builder(context,new jp.virtualcd.player.audio.SoundRenderers(context,new jp.virtualcd.player.audio.SoundProcessor(config))).setMediaSourceFactory(new DefaultMediaSourceFactory(()->new DefaultDataSource(context,new ZipTrackDataSource(context)))).build();
            player[0].setVolume(0);player[0].addListener(new Player.Listener(){boolean started;
                @Override public void onPlayerError(PlaybackException error){failure.set(error);done.countDown();}
                @Override public void onPlaybackStateChanged(int state){if(state==Player.STATE_READY&&!started){started=true;
                    main.postDelayed(()->{try{
                        if(player[0].getCurrentPosition()<=0)throw new AssertionError("Playback did not advance");
                        if(!player[0].isCurrentMediaItemSeekable()||player[0].getDuration()<=0)throw new AssertionError("Not seekable");
                        if(Math.abs(player[0].getPlaybackParameters().speed-1.25f)>.001)throw new AssertionError("Initial speed");
                        player[0].setPlaybackParameters(new PlaybackParameters(.75f,.5f));
                        long target=player[0].getDuration()/2;player[0].seekTo(target);
                        main.postDelayed(()->{try{if(player[0].getPlaybackState()!=Player.STATE_READY||Math.abs(player[0].getCurrentPosition()-target)>6000)throw new AssertionError("Seek failed");
                            if(!player[0].getPlaybackParameters().equals(new PlaybackParameters(.75f,.5f)))throw new AssertionError("Live speed/pitch lost after seek");}
                            catch(Throwable e){failure.set(e);}finally{done.countDown();}},3000);
                    }catch(Throwable e){failure.set(e);done.countDown();}},1500);
                }}
            });player[0].setPlaybackParameters(new PlaybackParameters(1.25f,(float)Math.pow(2,2/12.0)));player[0].setMediaItem(item);player[0].prepare();player[0].play();
        });
        try{if(!done.await(25,TimeUnit.SECONDS))throw new AssertionError("Playback timed out");if(failure.get()!=null)throw new AssertionError(failure.get());}
        finally{runOnMainSync(()->{main.removeCallbacksAndMessages(null);player[0].release();});}
    }
}
