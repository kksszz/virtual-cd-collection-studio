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
    private List<AlbumLibrary.Album> all=Collections.emptyList(),shown=Collections.emptyList();
    private boolean closed;
    public AlbumAdapter(Context c){context=c;listening=new ListeningState(c);}
    public void setAlbums(List<AlbumLibrary.Album> albums,String filter){all=albums;filter(filter);}
    public void filter(String text){String q=text.trim().toLowerCase(Locale.ROOT);var list=new ArrayList<AlbumLibrary.Album>();for(var a:all)if(a.name.toLowerCase(Locale.ROOT).contains(q))list.add(a);shown=list;notifyDataSetChanged();}
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
    private static class Row { ImageView image;TextView name,artist,detail;Button star;String key;Future<?> job; }
    public View getView(int position,View recycled,ViewGroup parent){
        Row row;
        if(recycled==null){
            var layout=new LinearLayout(context);layout.setGravity(Gravity.CENTER_VERTICAL);int pad=dp(8);layout.setPadding(pad,pad,pad,pad);
            row=new Row();row.image=new ImageView(context);row.image.setScaleType(ImageView.ScaleType.FIT_CENTER);row.image.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
            layout.addView(row.image,new LinearLayout.LayoutParams(dp(96),dp(96)));
            var labels=new LinearLayout(context);labels.setOrientation(LinearLayout.VERTICAL);labels.setPadding(dp(12),0,0,0);
            row.name=new TextView(context);row.name.setTextColor(0xffeef4fa);row.name.setTextSize(16);row.name.setMaxLines(2);labels.addView(row.name);
            row.artist=new TextView(context);row.artist.setTextColor(0xffc4d3df);row.artist.setTextSize(13);row.artist.setMaxLines(2);row.artist.setEllipsize(android.text.TextUtils.TruncateAt.END);labels.addView(row.artist);
            row.detail=new TextView(context);row.detail.setTextColor(0xff99b5c9);row.detail.setTextSize(12);labels.addView(row.detail);
            layout.addView(labels,new LinearLayout.LayoutParams(0,-2,1));row.star=FavoriteButton.create(context);layout.addView(row.star,new LinearLayout.LayoutParams(dp(48),dp(48)));layout.setTag(row);recycled=layout;
        }else row=(Row)recycled.getTag();
        if(row.job!=null){row.job.cancel(false);worker.purge();}
        var album=getItem(position);String mode=context.getSharedPreferences("thumbnail-layout",Context.MODE_PRIVATE).getString(album.uri.toString(),"auto");
        String key=album.key()+"|"+mode;row.key=key;row.name.setText(album.title());row.detail.setText(String.format(Locale.ROOT,"%s · %.1f MB",album.directory?"DIR":"ZIP",album.size/1048576.0));
        String artist=artists.get(album.key());row.artist.setText(artist==null?"アーティスト読込中…":artist);
        FavoriteButton.bind(row.star,listening.contains("favoriteAlbums",album.uri.toString()),album.title());
        row.star.setOnClickListener(v->{try{listening.toggle("favoriteAlbums",new org.json.JSONObject().put("id",album.uri.toString()).put("source",album.uri.toString()).put("title",album.title()));notifyDataSetChanged();}
            catch(org.json.JSONException error){Toast.makeText(context,"お気に入りを保存できません",Toast.LENGTH_SHORT).show();}});
        Bitmap bitmap=cache.get(key);row.image.setImageResource(R.drawable.ic_album);
        if(bitmap!=null)row.image.setImageBitmap(bitmap);
        if(!closed&&(artist==null||(bitmap==null&&!missing.contains(key)))){final Row target=row;row.job=worker.submit(()->{
            String loadedArtist=artists.get(album.key());
            if(loadedArtist==null){
                try{loadedArtist=AlbumTracks.readArtist(context,album);artists.put(album.key(),loadedArtist);}
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
    public void close(){closed=true;worker.shutdownNow();handler.removeCallbacksAndMessages(null);cache.evictAll();}
}
