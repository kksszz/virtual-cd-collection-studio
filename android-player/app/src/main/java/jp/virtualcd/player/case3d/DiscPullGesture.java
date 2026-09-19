package jp.virtualcd.player.case3d;

/** Two-finger hub/edge grab. Capture lasts until all fingers lift, even if cancelled. */
public final class DiscPullGesture {
    private int hub=-1,edge=-1;
    private float hx,hy,ex,ey;
    private boolean captured,finished;
    public boolean begin(int a,float ax,float ay,float ar,int b,float bx,float by,float br){
        reset();
        if(ar<=.28f&&ar>=0&&br>=.70f&&br<=1.15f){hub=a;edge=b;hx=ax;hy=ay;ex=bx;ey=by;}
        else if(br<=.28f&&br>=0&&ar>=.70f&&ar<=1.15f){hub=b;edge=a;hx=bx;hy=by;ex=ax;ey=ay;}
        else return false;
        captured=true;return true;
    }
    public boolean move(int a,float ax,float ay,int b,float bx,float by,float density){
        if(!captured||finished)return false;
        if(a==edge&&b==hub)return move(b,bx,by,a,ax,ay,density);
        if(a!=hub||b!=edge){cancel();return false;}
        if(Math.hypot(ax-hx,ay-hy)>20*density){cancel();return false;}
        if(Math.hypot(bx-ex,by-ey)<28*density)return false;
        finished=true;return true;
    }
    public boolean captured(){return captured;}
    public void cancel(){finished=true;}
    public void reset(){captured=finished=false;hub=edge=-1;}
}
