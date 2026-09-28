package jp.virtualcd.player.case3d;

/** Centimetre-space pose contract. Pure math so sequencing and pivots can be tested without GL. */
public final class DigipakMotion {
    public static final int CENTER=0,LEFT=1,DISC1=2,RIGHT=3,DISC2=4,BOOKLET=5,FOLD_LEFT=6,FOLD_RIGHT=7;
    private DigipakMotion(){}
    public static float leftAngle(float open){return 180*(1-clamp(open*2));}
    public static float rightAngle(float open){return -180*(1-clamp(open*2-1));}
    private static float clamp(float n){return Math.max(0,Math.min(1,n));}
    /** x/z rotation around the panel hinge (positive Y axis). */
    public static float[] panelPoint(boolean right,float open,float x,float z){
        float hx=right?.74f:-.75f,hz=right?.045f:.05f;
        double angle=Math.toRadians(right?rightAngle(open):leftAngle(open)),c=Math.cos(angle),s=Math.sin(angle);
        return new float[]{hx+(float)(c*(x-hx)+s*(z-hz)),hz+(float)(-s*(x-hx)+c*(z-hz))};
    }
    /** Fold position + outward normal, matching the desktop quadratic fold strip. */
    public static float[] fold(boolean right,float open,float flatX,float flatZ){
        float start=right?.69f:-.69f,moving=right?.79f:-.81f,direction=right?1:-1;
        float t=clamp((flatX-start)/(moving-start)),u=1-t;
        float[] end=panelPoint(right,open,moving,0);
        float cx=(start+end[0])/2+direction*.015f*Math.abs(end[1])/.1f,cz=end[1]/2;
        float tx=2*u*(cx-start)+2*t*(end[0]-cx),tz=2*u*cz+2*t*(end[1]-cz);
        float length=(float)Math.hypot(tx,tz),nx=direction*tz/Math.max(length,.000001f),nz=-direction*tx/Math.max(length,.000001f);
        return new float[]{u*u*start+2*u*t*cx+t*t*end[0]-nx*flatZ,2*u*t*cz+t*t*end[1]-nz*flatZ,nx,nz};
    }
}
