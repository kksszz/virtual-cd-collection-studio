package jp.virtualcd.player.library;

import android.content.Context;
import android.graphics.*;
import android.view.*;
import android.widget.ImageView;

/** Fit-to-screen, pinch/double-tap zoom and bounded pan. Swipes change pages only at fit scale. */
public final class ZoomArtworkView extends ImageView {
    private final Matrix transform=new Matrix();
    private final GestureDetector gestures;
    private final ScaleGestureDetector pinch;
    private float zoom=1,base=1,x,y;
    private boolean multiTouch;
    private float startX,startY;
    private boolean startedAtFit;
    private Runnable toggle=()->{};
    private java.util.function.IntConsumer page=direction->{};
    public ZoomArtworkView(Context context){
        super(context);setScaleType(ScaleType.MATRIX);setContentDescription(jp.virtualcd.player.LanguageStrings.text("アルバム画像。左右スワイプでページ切替、ピンチまたはダブルタップで拡大、タップで操作表示","Album artwork. Swipe for pages, pinch or double tap to zoom, tap for controls."));
        pinch=new ScaleGestureDetector(context,new ScaleGestureDetector.SimpleOnScaleGestureListener(){
            @Override public boolean onScale(ScaleGestureDetector detector){zoomTo(zoom*detector.getScaleFactor(),detector.getFocusX(),detector.getFocusY());return true;}
        });
        gestures=new GestureDetector(context,new GestureDetector.SimpleOnGestureListener(){
            @Override public boolean onDown(MotionEvent e){return true;}
            @Override public boolean onSingleTapConfirmed(MotionEvent e){performClick();toggle.run();return true;}
            @Override public boolean onDoubleTap(MotionEvent e){zoomTo(zoom>1.05f?1:2.5f,e.getX(),e.getY());return true;}
            @Override public boolean onScroll(MotionEvent first,MotionEvent last,float dx,float dy){
                if(!pinch.isInProgress()&&zoom>1.05f){x-=dx;y-=dy;apply();}return true;
            }
        });
    }
    public void setActions(Runnable toggle,java.util.function.IntConsumer page){this.toggle=toggle;this.page=page;}
    @Override public boolean performClick(){super.performClick();return true;}
    @Override public boolean onGenericMotionEvent(MotionEvent event){
        if(event.getActionMasked()==MotionEvent.ACTION_SCROLL&&event.isFromSource(InputDevice.SOURCE_CLASS_POINTER)){
            float scroll=event.getAxisValue(MotionEvent.AXIS_VSCROLL);
            if(Float.isFinite(scroll)&&scroll!=0){zoomTo(zoom*(float)Math.exp(Math.max(-20,Math.min(20,scroll))*.14f),event.getX(),event.getY());return true;}
        }return super.onGenericMotionEvent(event);
    }
    @Override public boolean onTouchEvent(MotionEvent event){
        if(event.getActionMasked()==MotionEvent.ACTION_DOWN){multiTouch=false;startX=event.getX();startY=event.getY();startedAtFit=zoom<=1.05f;}
        if(event.getPointerCount()>1)multiTouch=true;
        pinch.onTouchEvent(event);gestures.onTouchEvent(event);
        if(event.getActionMasked()==MotionEvent.ACTION_UP&&!multiTouch&&startedAtFit&&zoom<=1.05f){
            float dx=event.getX()-startX,dy=event.getY()-startY;
            if(Math.abs(dx)>getResources().getDisplayMetrics().density*48&&Math.abs(dx)>Math.abs(dy)*1.5f)page.accept(dx<0?1:-1);
        }
        return true;
    }
    @Override public void setImageBitmap(Bitmap bitmap){super.setImageBitmap(bitmap);fit();}
    @Override protected void onSizeChanged(int w,int h,int oldw,int oldh){super.onSizeChanged(w,h,oldw,oldh);fit();}
    private void fit(){
        zoom=1;if(getDrawable()==null||getWidth()==0||getHeight()==0)return;
        base=Math.min(getWidth()/(float)getDrawable().getIntrinsicWidth(),getHeight()/(float)getDrawable().getIntrinsicHeight());x=0;y=0;apply();
    }
    private void zoomTo(float next,float fx,float fy){
        next=Math.max(1,Math.min(5,next));float ratio=next/zoom;
        x=fx-(fx-x)*ratio;y=fy-(fy-y)*ratio;zoom=next;apply();
    }
    private void apply(){
        if(getDrawable()==null)return;
        float scale=base*zoom,w=getDrawable().getIntrinsicWidth()*scale,h=getDrawable().getIntrinsicHeight()*scale;
        x=w<=getWidth()?(getWidth()-w)/2:Math.max(getWidth()-w,Math.min(0,x));
        y=h<=getHeight()?(getHeight()-h)/2:Math.max(getHeight()-h,Math.min(0,y));
        transform.setScale(scale,scale);transform.postTranslate(x,y);setImageMatrix(transform);
    }
}
