package jp.virtualcd.player.library;

/** Thumbnail-only crop, never changes original artwork. */
public final class ThumbnailLayout {
    public static int[] region(int width,int height,String mode){
        if(width<1||height<1)throw new IllegalArgumentException("Invalid image dimensions");
        boolean spread=width/(double)height>=1.65&&width/(double)height<=2.35;
        if("full".equals(mode)||("auto".equals(mode)&&!spread))return new int[]{0,0,width,height};
        int side=Math.max(1,Math.min(width/2,height));
        return new int[]{"left".equals(mode)?0:width-side,Math.max(0,(height-side)/2),side,side};
    }
}
