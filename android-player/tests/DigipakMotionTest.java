import jp.virtualcd.player.case3d.DigipakMotion;

public class DigipakMotionTest {
    private static void near(float a,float b){if(Math.abs(a-b)>.00001f)throw new AssertionError(a+" != "+b);}
    public static void main(String[] args){
        near(DigipakMotion.leftAngle(0),180);near(DigipakMotion.rightAngle(0),-180);
        near(DigipakMotion.leftAngle(.5f),0);near(DigipakMotion.rightAngle(.5f),-180);
        near(DigipakMotion.leftAngle(1),0);near(DigipakMotion.rightAngle(1),0);
        for(boolean right:new boolean[]{false,true})for(int frame=0;frame<=100;frame++){
            float progress=frame/100f,start=right?.69f:-.69f,end=right?.79f:-.81f;
            float[] fixed=DigipakMotion.fold(right,progress,start,0),moving=DigipakMotion.fold(right,progress,end,0),panel=DigipakMotion.panelPoint(right,progress,end,0);
            near(fixed[0],start);near(fixed[1],0);near(moving[0],panel[0]);near(moving[1],panel[1]);
            for(int point=0;point<=16;point++){
                float[] fold=DigipakMotion.fold(right,progress,start+(end-start)*point/16,.005f);
                for(float value:fold)if(!Float.isFinite(value))throw new AssertionError("Invalid fold");
                near(fold[2]*fold[2]+fold[3]*fold[3],1);
            }
        }
        near(DigipakMotion.leftAngle3(0),180);near(DigipakMotion.rightAngle3(0),-180);near(DigipakMotion.farAngle3(0),180);
        near(DigipakMotion.leftAngle3(1f/3),0);near(DigipakMotion.rightAngle3(1f/3),-180);
        near(DigipakMotion.rightAngle3(2f/3),0);near(DigipakMotion.farAngle3(2f/3),180);
        near(DigipakMotion.farAngle3(1),0);
        for(int part:new int[]{DigipakMotion.FOLD_LEFT,DigipakMotion.FOLD_RIGHT,DigipakMotion.FOLD_FAR})for(int frame=0;frame<=100;frame++){
            float[] fold=DigipakMotion.fold3(part,frame/100f,part==DigipakMotion.FOLD_LEFT?-.78f:part==DigipakMotion.FOLD_RIGHT?.77f:2.29f,.005f);
            for(float value:fold)if(!Float.isFinite(value))throw new AssertionError("Invalid three-disc fold");
            near(fold[2]*fold[2]+fold[3]*fold[3],1);
        }
        System.out.println("PASS digipak: staged opening, fold endpoints, finite unit normals through 101 poses");
    }
}
