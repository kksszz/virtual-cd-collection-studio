package jp.virtualcd.player.library;

import android.content.Context;
import android.graphics.Bitmap;
import android.os.*;
import android.util.LruCache;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
import jp.virtualcd.player.R;

/** Recycled rows with bounded thumbnails, a dedicated worker and stale-row protection. */
public final class AlbumAdapter extends BaseAdapter implements AutoCloseable {
    private final Context context;
    private final ListeningState listening;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ThreadPoolExecutor worker=new ThreadPoolExecutor(2,2,0,TimeUnit.SECONDS,new LinkedBlockingQueue<>());
    private final LruCache<String,Bitmap> cache=new LruCache<String,Bitmap>(8*1024*1024){protected int sizeOf(String key,Bitmap b){return b.getAllocationByteCount();}};
    private final Set<String> missing=Collections.synchronizedSet(new HashSet<>());
    private final Map<String,String> artists=new ConcurrentHashMap<>();
    private final Map<String,AlbumIndicators.Info> formats=new ConcurrentHashMap<>();
    private final ExecutorService sortWorker=Executors.newSingleThreadExecutor();
    private final android.content.SharedPreferences sortPrefs,artistPrefs;
    private Future<?> sortJob;
    private int sortGeneration;
    private String filterText="";
    private boolean artistOrder;
    private List<AlbumLibrary.Album> all=Collections.emptyList(),shown=Collections.emptyList();
    private boolean closed;
    public AlbumAdapter(Context c){context=c;listening=new ListeningState(c);sortPrefs=c.getSharedPreferences("album-order",0);artistPrefs=c.getSharedPreferences("album-artist-index",0);artistOrder=sortPrefs.getBoolean("artist",false);}
    public boolean isArtistOrder(){return artistOrder;}
    public void setArtistOrder(boolean value){artistOrder=value;sortPrefs.edit().putBoolean("artist",value).apply();filter(filterText);indexArtists();}
    public void setAlbums(List<AlbumLibrary.Album> albums,String filter){all=new ArrayList<>(albums);formats.clear();for(var album:all){String cached=artistPrefs.getString(album.key(),null);if(cached!=null)artists.put(album.key(),cached);}filter(filter);indexArtists();}
    public void filter(String text){filterText=text;String q=text.trim().toLowerCase(Locale.ROOT);var list=new ArrayList<AlbumLibrary.Album>();for(var a:all)if(a.name.toLowerCase(Locale.ROOT).contains(q))list.add(a);
        var collator=java.text.Collator.getInstance(Locale.JAPANESE);collator.setStrength(java.text.Collator.SECONDARY);
        // Freeze keys during a sort: background metadata reads must not change comparator results.
        var keys=new HashMap<>(artists);
        list.sort((a,b)->{int order=0;if(artistOrder){String aa=artistKey(keys.get(a.key())),bb=artistKey(keys.get(b.key()));order=Boolean.compare(aa.isEmpty(),bb.isEmpty());if(order==0)order=collator.compare(aa,bb);}
            if(order==0)order=collator.compare(AudioFormats.natural(a.title()),AudioFormats.natural(b.title()));return order==0?a.uri.toString().compareTo(b.uri.toString()):order;});
        shown=list;notifyDataSetChanged();}
    private static String artistKey(String value){return value==null||value.startsWith("アーティスト情報")?"":java.text.Normalizer.normalize(value.trim(),java.text.Normalizer.Form.NFKC);}
    private void indexArtists(){
        int generation=++sortGeneration;if(sortJob!=null)sortJob.cancel(true);
        if(!artistOrder||closed)return;var snapshot=new ArrayList<>(all);
        sortJob=sortWorker.submit(()->{
            boolean changed=false;var save=artistPrefs.edit();
            for(var album:snapshot){if(Thread.currentThread().isInterrupted())return;if(artists.containsKey(album.key()))continue;
                try{var cached=AlbumTagCache.read(context,album.uri);String artist=cached!=null?cached.album.artist:AlbumTracks.readArtist(context,album);if(artist==null)artist="";artists.put(album.key(),artist);save.putString(album.key(),artist);changed=true;}
                catch(Exception ignored){if(Thread.currentThread().isInterrupted())return;artists.put(album.key(),"");}
            }
            if(changed)save.apply();handler.post(()->{if(!closed&&generation==sortGeneration&&artistOrder)filter(filterText);});
        });
    }
    public int getCount(){return shown.size();}
    public AlbumLibrary.Album getItem(int position){return shown.get(position);}
    public long getItemId(int position){return position;}
    public void chooseThumbnail(int position){
        var album=getItem(position);var prefs=context.getSharedPreferences("thumbnail-layout",Context.MODE_PRIVATE);
        String[] modes={"auto","right","left","full"};String current=prefs.getString(album.uri.toString(),"auto");
        new android.app.AlertDialog.Builder(context).setTitle("表紙の表示範囲")
            .setSingleChoiceItems(new String[]{"自動（見開きは右側）","右側を表紙にする","左側を表紙にする","画像全体"},Arrays.asList(modes).indexOf(current),(dialog,index)->{
                prefs.edit().putString(album.uri.toString(),modes[index]).apply();notifyDataSetChanged();dialog.dismiss();
            }).setNegativeButton("キャンセル",null).show();
    }
    private static class Row { ImageView image;TextView name,artist,detail,badge;Button star;String key;Future<?> job; }
    public View getView(int position,View recycled,ViewGroup parent){
        Row row;
        if(recycled==null){
            var layout=new LinearLayout(context);layout.setGravity(Gravity.CENTER_VERTICAL);int pad=dp(8);layout.setPadding(pad,pad,pad,pad);
            row=new Row();row.image=new ImageView(context);row.image.setScaleType(ImageView.ScaleType.FIT_CENTER);row.image.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
            layout.addView(row.image,new LinearLayout.LayoutParams(dp(82),dp(82)));
            var labels=new LinearLayout(context);labels.setOrientation(LinearLayout.VERTICAL);labels.setPadding(dp(12),0,0,0);
            row.name=new TextView(context);row.name.setTextColor(0xffeef4fa);row.name.setTextSize(16);row.name.setMaxLines(2);labels.addView(row.name);
            row.artist=new TextView(context);row.artist.setTextColor(0xffc4d3df);row.artist.setTextSize(13);row.artist.setMaxLines(2);row.artist.setEllipsize(android.text.TextUtils.TruncateAt.END);labels.addView(row.artist);
            var infoLine=new LinearLayout(context);infoLine.setGravity(Gravity.CENTER_VERTICAL);labels.addView(infoLine);
            row.detail=new TextView(context);row.detail.setTextColor(0xff99b5c9);row.detail.setTextSize(12);infoLine.addView(row.detail,new LinearLayout.LayoutParams(0,-2,1));
            row.badge=new TextView(context);row.badge.setText("3D");row.badge.setTextSize(11);row.badge.setTextColor(0xff72d9ca);row.badge.setPadding(dp(4),0,dp(4),0);row.badge.setContentDescription("3Dデータ取込済み");infoLine.addView(row.badge);
            layout.addView(labels,new LinearLayout.LayoutParams(0,-2,1));row.star=FavoriteButton.create(context);layout.addView(row.star,new LinearLayout.LayoutParams(dp(48),dp(48)));layout.setTag(row);recycled=layout;
        }else row=(Row)recycled.getTag();
        if(row.job!=null){row.job.cancel(false);worker.purge();}
        var album=getItem(position);String mode=context.getSharedPreferences("thumbnail-layout",Context.MODE_PRIVATE).getString(album.uri.toString(),"auto");
        String key=album.key()+"|"+mode+"|"+ArtworkLoader.frontStamp(context,album);row.key=key;row.name.setText(album.title());setDetail(row,album,formats.get(album.key()));
        row.badge.setVisibility(AlbumIndicators.has3d(context,album)?View.VISIBLE:View.GONE);
        String artist=artists.get(album.key());row.artist.setText(artist==null?"アーティスト読込中…":artist.isEmpty()?"アーティスト情報なし":artist);
        FavoriteButton.bind(row.star,listening.contains("favoriteAlbums",album.uri.toString()),album.title());
        row.star.setOnClickListener(v->{try{listening.toggle("favoriteAlbums",new org.json.JSONObject().put("id",album.uri.toString()).put("source",album.uri.toString()).put("title",album.title()));notifyDataSetChanged();}
            catch(org.json.JSONException error){Toast.makeText(context,"お気に入りを保存できません",Toast.LENGTH_SHORT).show();}});
        Bitmap bitmap=cache.get(key);row.image.setImageResource(R.drawable.ic_album);
        if(bitmap!=null)row.image.setImageBitmap(bitmap);
        if(!closed&&(formats.get(album.key())==null||artist==null||(bitmap==null&&!missing.contains(key)))){final Row target=row;row.job=worker.submit(()->{
            AlbumIndicators.Info detected=formats.get(album.key());if(detected==null){try{detected=AlbumIndicators.read(context,album);}catch(Exception ignored){detected=new AlbumIndicators.Info("",-1);}formats.put(album.key(),detected);}
            final AlbumIndicators.Info formatLabel=detected;handler.post(()->{if(!closed&&key.equals(target.key))setDetail(target,album,formatLabel);});
            String loadedArtist=artists.get(album.key());
            if(loadedArtist==null){
                try{var saved=AlbumTagCache.read(context,album.uri);loadedArtist=saved!=null?saved.album.artist:AlbumTracks.readArtist(context,album);if(loadedArtist==null)loadedArtist="";artists.put(album.key(),loadedArtist);artistPrefs.edit().putString(album.key(),loadedArtist).apply();}
                catch(Exception error){loadedArtist="アーティスト情報を取得できません";}
            }
            final String label=loadedArtist;
            handler.post(()->{if(!closed&&key.equals(target.key))target.artist.setText(label);});
            if(cache.get(key)==null&&!missing.contains(key)){
                Bitmap loaded=ArtworkLoader.load(context,album,mode);
                if(loaded!=null)cache.put(key,loaded);else missing.add(key);
                handler.post(()->{if(!closed&&key.equals(target.key)&&loaded!=null)target.image.setImageBitmap(loaded);});
            }
        });}
        return recycled;
    }
    private int dp(int value){return (int)(value*context.getResources().getDisplayMetrics().density);}
    private void setDetail(Row row,AlbumLibrary.Album album,AlbumIndicators.Info info){String container=album.directory?"DIR":AudioFormats.archive(album.name)?"ZIP":"";String format=info==null?null:info.formats;
        String label=container+(format==null||format.isEmpty()?"":(container.isEmpty()?"":" · ")+format);
        row.detail.setText(String.format(Locale.ROOT,"%s · %s曲\n%.1f MB",label,info==null||info.count<0?"—":String.valueOf(info.count),album.size/1048576.0));}
    public void close(){closed=true;sortGeneration++;sortWorker.shutdownNow();worker.shutdownNow();handler.removeCallbacksAndMessages(null);cache.evictAll();}
}
