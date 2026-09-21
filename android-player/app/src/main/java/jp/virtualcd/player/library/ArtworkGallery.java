package jp.virtualcd.player.library;

import android.app.*;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
import jp.virtualcd.player.archive.StoredZipIndex;
import jp.virtualcd.player.ControlIcon;

/** One bounded image at a time; closing the viewer does not affect audio playback. */
public final class ArtworkGallery {
    private final Activity activity;
    private final Uri document;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final Dialog dialog;
    private final ZoomArtworkView image;
    private final TextView caption;
    private final Button previous,next;
    private final int originalOrientation;
    private boolean loading;
    private List<ArtworkLoader.ImageRef> images=Collections.emptyList();
    private int index;
    private boolean closed;
    public static ArtworkGallery show(Activity activity,Uri document){var gallery=new ArtworkGallery(activity,document);gallery.open();return gallery;}
    public void close(){dialog.dismiss();}
    private ArtworkGallery(Activity activity,Uri document){
        this.activity=activity;this.document=document;originalOrientation=activity.getRequestedOrientation();
        dialog=new Dialog(activity,android.R.style.Theme_Material_NoActionBar_Fullscreen);
        var root=new FrameLayout(activity);root.setBackgroundColor(0xff000000);
        image=new ZoomArtworkView(activity);root.addView(image,new FrameLayout.LayoutParams(-1,-1));
        caption=new TextView(activity);caption.setTextColor(0xffeef4fa);caption.setTextSize(14);caption.setGravity(Gravity.CENTER);caption.setMaxLines(2);caption.setBackgroundColor(0xbb15191f);
        caption.setPadding(12,8,12,8);root.addView(caption,new FrameLayout.LayoutParams(-1,-2,Gravity.TOP));
        var controls=new LinearLayout(activity);controls.setBackgroundColor(0xbb15191f);root.addView(controls,new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM));
        previous=button(controls,"page-previous",jp.virtualcd.player.LanguageStrings.text("前の画像","Previous image"),()->turn(-1));next=button(controls,"page-next",jp.virtualcd.player.LanguageStrings.text("次の画像","Next image"),()->turn(1));
        button(controls,"orientation",jp.virtualcd.player.LanguageStrings.text("画面の横向き・縦向きを切り替え","Switch landscape / portrait"),()->activity.setRequestedOrientation(activity.getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE
            ?android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT:android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE));
        button(controls,"close",jp.virtualcd.player.LanguageStrings.text("ジャケットビューアーを閉じる","Close artwork viewer"),()->dialog.dismiss());
        image.setActions(()->{int visibility=controls.getVisibility()==View.VISIBLE?View.GONE:View.VISIBLE;controls.setVisibility(visibility);caption.setVisibility(visibility);},this::turn);
        dialog.setContentView(root);dialog.setOnDismissListener(d->{closed=true;worker.shutdownNow();handler.removeCallbacksAndMessages(null);image.setImageDrawable(null);activity.setRequestedOrientation(originalOrientation);});
    }
    private Button button(LinearLayout row,String icon,String label,Runnable action){var b=new Button(activity);row.addView(b,new LinearLayout.LayoutParams(0,-2,1));ControlIcon.button(b,icon,label);b.setOnClickListener(v->action.run());return b;}
    private void open(){
        dialog.show();dialog.getWindow().setLayout(-1,-1);
        dialog.getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN|View.SYSTEM_UI_FLAG_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY|View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN|View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        caption.setText(jp.virtualcd.player.LanguageStrings.text("画像一覧を読み込み中…","Loading image list…"));previous.setEnabled(false);next.setEnabled(false);
        worker.execute(()->{try{
            var found=ArtworkLoader.listImages(activity,AlbumLibrary.describe(activity,document));
            handler.post(()->{if(closed||activity.isDestroyed())return;images=found;
                if(images.isEmpty()){caption.setText(jp.virtualcd.player.LanguageStrings.text("画像ファイルはありません（埋め込み画像は一覧対象外です）。","No image files (embedded artwork is not listed)."));return;}load();});
        }catch(Exception e){handler.post(()->{if(!closed)caption.setText(jp.virtualcd.player.LanguageStrings.text("画像一覧を読み込めません: ","Unable to load image list: ")+e.getMessage());});}});
    }
    private void load(){
        loading=true;
        var entry=images.get(index);caption.setText((index+1)+" / "+images.size()+" · "+entry.name+jp.virtualcd.player.LanguageStrings.text("\n読み込み中…","\nLoading…"));
        image.setImageDrawable(null);previous.setEnabled(false);next.setEnabled(false);
        worker.execute(()->{try{
            var bitmap=ArtworkLoader.galleryImage(activity,entry);
            handler.post(()->{if(closed||activity.isDestroyed())return;image.setImageBitmap(bitmap);
                caption.setText((index+1)+" / "+images.size()+" · "+entry.name+(bitmap==null?jp.virtualcd.player.LanguageStrings.text("\n表示できない画像です","\nThis image cannot be displayed"):""));enableNavigation();});
        }catch(Exception e){handler.post(()->{if(!closed){caption.setText((index+1)+" / "+images.size()+" · "+entry.name+"\n"+e.getMessage());enableNavigation();}});}});
    }
    private void turn(int direction){if(closed||loading||index+direction<0||index+direction>=images.size())return;index+=direction;load();}
    private void enableNavigation(){loading=false;previous.setEnabled(index>0);next.setEnabled(index+1<images.size());}
}
