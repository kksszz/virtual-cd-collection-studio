package jp.virtualcd.player;

import android.graphics.*;
import android.graphics.drawable.Drawable;
import android.widget.Button;
import jp.virtualcd.player.library.PlayerStyle;

/** Code-drawn 24 dp icons avoid emoji/font differences across devices. */
public final class ControlIcon extends Drawable {
    private final String kind;
    private final float density;
    private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
    private boolean selected,enabled=true;
    ControlIcon(String kind,float density){this.kind=kind;this.density=density;}
    public static void button(Button b,String kind,String label){
        b.setText("");b.setContentDescription(label);b.setTooltipText(label);
        ControlIcon icon=new ControlIcon(kind,b.getResources().getDisplayMetrics().density);int size=PlayerStyle.dp(b.getContext(),24);icon.setBounds(0,0,size,size);
        // Explicit top inset centers a 24 dp compound icon in a 44 dp button.
        // This path also renders reliably on the Xperia hardware-accelerated TextView.
        b.setForeground(null);b.setTextSize(0);b.setIncludeFontPadding(false);b.setGravity(android.view.Gravity.CENTER);b.setCompoundDrawablePadding(0);
        b.setPadding(0,PlayerStyle.dp(b.getContext(),10),0,0);b.setCompoundDrawables(null,icon,null,null);b.setCompoundDrawableTintList(null);
        var lp=b.getLayoutParams();if(lp!=null){lp.height=PlayerStyle.dp(b.getContext(),44);b.setLayoutParams(lp);}
    }
    @Override public boolean isStateful(){return true;}
    @Override protected boolean onStateChange(int[] states){selected=false;enabled=false;for(int s:states){if(s==android.R.attr.state_selected)selected=true;if(s==android.R.attr.state_enabled)enabled=true;}invalidateSelf();return true;}
    @Override public void draw(Canvas c){
        c.save();c.translate(getBounds().exactCenterX()-12*density,getBounds().exactCenterY()-12*density);c.scale(density,density);
        paint.setColor(!enabled?0xff657181:selected?0xff091820:0xffdce6ee);paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(1.7f);paint.setStrokeCap(Paint.Cap.ROUND);paint.setStrokeJoin(Paint.Join.ROUND);
        switch(kind){
            case "lyrics":c.drawRoundRect(5,3,19,21,1,1,paint);lines(c,8,8,16,8);lines(c,8,12,16,12);lines(c,8,16,14,16);break;
            case "page-previous":lines(c,15,4,7,12,15,20);break;
            case "page-next":lines(c,9,4,17,12,9,20);break;
            case "orientation":c.save();c.rotate(-35,12,12);c.drawRoundRect(7,4,17,20,1.5f,1.5f,paint);lines(c,11,17,13,17);c.restore();c.drawArc(1,1,23,23,190,65,false,paint);lines(c,5,2,9,1,8,5);c.drawArc(1,1,23,23,10,65,false,paint);lines(c,19,22,15,23,16,19);break;
            case "sort":lines(c,5,3,5,21);lines(c,2,17,5,21,8,17);lines(c,11,5,22,5);lines(c,11,11,19,11);lines(c,11,17,16,17);break;
            case "close":lines(c,5,5,19,19);lines(c,19,5,5,19);break;
            case "import":lines(c,12,2,12,15);lines(c,7,10,12,15,17,10);lines(c,3,15,3,21,21,21,21,15);break;
            case "case-open":lines(c,3,12,14,12,21,18,10,18,3,12,3,3,14,3,14,12);lines(c,10,18,10,21,21,21,21,18);break;
            case "disc-out":c.drawCircle(9,10,7,paint);c.drawCircle(9,10,2,paint);lines(c,13,19,22,19);lines(c,18,15,22,19,18,23);break;
            case "obi":c.drawRoundRect(3,3,21,21,1,1,paint);lines(c,7,3,7,21);lines(c,11,3,11,21);lines(c,3,7,7,7);lines(c,3,17,7,17);break;
            case "wrapping":lines(c,3,7,7,3,20,3,20,17,17,21,3,21,3,7,7,7,7,3);lines(c,20,17,17,17,17,21);lines(c,11,8,11,14);lines(c,8,11,14,11);break;
            case "case3d":lines(c,12,2,22,7,22,17,12,22,2,17,2,7,12,2);lines(c,2,7,12,12,22,7);lines(c,12,12,12,22);break;
            case "booklet":lines(c,12,6,8,4,2,4,2,19,8,19,12,21,16,19,22,19,22,4,16,4,12,6,12,21);lines(c,5,8,9,9);lines(c,5,12,9,13);lines(c,15,9,19,8);lines(c,15,13,19,12);break;
            case "current":c.drawCircle(12,12,6,paint);lines(c,12,2,12,5);lines(c,12,19,12,22);lines(c,2,12,5,12);lines(c,19,12,22,12);paint.setStyle(Paint.Style.FILL);c.drawCircle(12,12,2,paint);break;
            case "back":lines(c,13,4,5,12,13,20);lines(c,5,12,21,12);break;
            case "settings":c.drawCircle(12,12,6,paint);c.drawCircle(12,12,2,paint);for(int i=0;i<8;i++){c.save();c.rotate(i*45,12,12);lines(c,12,2,12,5);c.restore();}break;
            case "play":lines(c,7,3,20,12,7,21,7,3);break;
            case "pause":lines(c,8,4,8,20);lines(c,16,4,16,20);break;
            case "stop":c.drawRect(5,5,19,19,paint);break;
            case "next":lines(c,4,4,16,12,4,20,4,4);lines(c,20,4,20,20);break;
            case "previous":lines(c,20,4,8,12,20,20,20,4);lines(c,4,4,4,20);break;
            case "file":lines(c,7,3,15,3,20,8,20,21,4,21,4,3,7,3);lines(c,14,3,14,9,20,9);lines(c,13,12,13,17);c.drawOval(8,16,13,19,paint);break;
            case "folder":lines(c,2,6,9,6,11,8,22,8,22,20,2,20,2,6);break;
            case "albums":for(int x=3;x<20;x+=10)for(int y=3;y<20;y+=10)c.drawRoundRect(x,y,x+7,y+7,1,1,paint);break;
            case "refresh":c.drawArc(4,4,20,20,40,285,false,paint);lines(c,20,3,20,9,14,9);break;
            case "history":c.drawArc(4,4,20,20,205,310,false,paint);lines(c,2,4,2,10,8,10);lines(c,12,7,12,12,16,14);break;
            case "star":lines(c,12,2,15,8,22,9,17,14,18,21,12,18,6,21,7,14,2,9,9,8,12,2);break;
            case "shuffle":lines(c,3,6,6,6,17,18,21,18);lines(c,17,14,21,18,17,22);lines(c,3,18,6,18,10,14);lines(c,14,10,18,6,21,6);lines(c,17,2,21,6,17,10);break;
            case "repeat":case "repeat-one":lines(c,4,9,4,6,20,6,17,3);lines(c,20,15,20,18,4,18,7,21);lines(c,20,6,17,9);lines(c,4,18,7,15);
                if(kind.equals("repeat-one")){paint.setStyle(Paint.Style.FILL);paint.setTextSize(9);paint.setTypeface(Typeface.DEFAULT_BOLD);c.drawText("1",10,15.5f,paint);}break;
            case "sound":lines(c,5,3,5,21);lines(c,12,3,12,21);lines(c,19,3,19,21);paint.setStyle(Paint.Style.FILL);c.drawCircle(5,8,2.8f,paint);c.drawCircle(12,16,2.8f,paint);c.drawCircle(19,10,2.8f,paint);break;
        }
        c.restore();
    }
    private void lines(Canvas canvas,float... xy){Path path=new Path();path.moveTo(xy[0],xy[1]);for(int i=2;i<xy.length;i+=2)path.lineTo(xy[i],xy[i+1]);canvas.drawPath(path,paint);}
    @Override public void setAlpha(int alpha){paint.setAlpha(alpha);}
    @Override public void setColorFilter(ColorFilter filter){paint.setColorFilter(filter);}
    @Override public int getOpacity(){return PixelFormat.TRANSLUCENT;}
}
