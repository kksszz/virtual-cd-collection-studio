package jp.virtualcd.player.case3d;

import android.content.Context;
import android.opengl.*;
import android.view.*;
import java.util.*;
import javax.microedition.khronos.egl.EGLConfig;
import javax.microedition.khronos.opengles.GL10;
import static android.opengl.GLES20.*;

/** Read-only animated display; all pose changes are serialized on the GL thread. */
public final class CaseSurface extends GLSurfaceView implements GLSurfaceView.Renderer {
    private final CasePackage data;
    private final Map<String,Integer> textures=new HashMap<>();
    private final Map<CaseGeometry.Mesh,float[]> foldBinds=new IdentityHashMap<>();
    private final List<Draw> opaque=new ArrayList<>(),transparent=new ArrayList<>(),draws=new ArrayList<>();
    private final ScaleGestureDetector pinch;
    private final GestureDetector taps;
    private boolean resetGesture;
    private volatile boolean sideFacing;
    private boolean hingeGesture,hingeTriggered;
    private float hingeSpan;
    private final DiscPullGesture discPull=new DiscPullGesture();
    private volatile float[] discHitInverse;
    private volatile int digipakHitMask;
    private final int digipakPresentMask;
    private int gestureDisc=-1;
    private float panX,panY,lastFocusX,lastFocusY;
    private int lastPointerCount;
    private float yaw=-20,pitch=12,zoom=1,open,targetOpen,discRemoved,targetDisc,obiRemoved,targetObi,wrapRemoved,targetWrap,aspect=1,lastX,lastY;
    private long lastFrame;
    private float bookletRemoved,targetBooklet;
    private float multiTurn,targetMultiTurn;
    private final float[] multiRemoved=new float[4],targetMultiRemoved=new float[4];
    private final float[] digipakRemoved=new float[3],targetDigipakRemoved=new float[3];
    private int program,position,uv,normal,mvpUniform,modelUniform,colorUniform,textureUniform,finishUniform;
    private final float[] projection=new float[16],view=new float[16],root=new float[16],vp=new float[16],mvp=new float[16],tint=new float[4];
    private static final class Draw {final CaseGeometry.Mesh mesh;final float[] model=new float[16],bind;float alpha,depth,foldPose=Float.NaN;Draw(CaseGeometry.Mesh mesh,float[] bind){this.mesh=mesh;this.bind=bind;}}
    public CaseSurface(Context context,CasePackage data){super(context);this.data=data;obiRemoved=targetObi=data.hasObi?0:1;wrapRemoved=targetWrap=data.wrapped?0:1;
        int present=0;if(data.digipak&&data.geometry!=null)for(var mesh:data.geometry){
            if(mesh.part==DigipakMotion.DISC1)present|=1;
            else if(mesh.part==DigipakMotion.DISC2)present|=2;
            else if(mesh.part==DigipakMotion.DISC3)present|=4;
        }digipakPresentMask=present;
        setEGLContextClientVersion(2);setEGLConfigChooser(8,8,8,0,24,0);setRenderer(this);setRenderMode(RENDERMODE_WHEN_DIRTY);
        setContentDescription(jp.virtualcd.player.LanguageStrings.text("3D CDケース。1本指で回転、2本指のスライドで移動。開いたCDの中心を押さえ、もう1本の指で外周を引くとCDを取り出します。ピンチまたはマウスホイールで拡大縮小、ダブルタップで初期表示","3D CD case. One finger rotates, two fingers pan. Hold the CD center and pull its edge with another finger to remove it. Pinch or mouse wheel to zoom; double tap to reset."));
        taps=new GestureDetector(context,new GestureDetector.SimpleOnGestureListener(){
            @Override public boolean onDown(MotionEvent event){return true;}
            @Override public boolean onDoubleTap(MotionEvent event){resetGesture=true;reset();return true;}
        });taps.setIsLongpressEnabled(false);
        pinch=new ScaleGestureDetector(context,new ScaleGestureDetector.SimpleOnScaleGestureListener(){@Override public boolean onScale(ScaleGestureDetector detector){if(hingeGesture)return true;float factor=detector.getScaleFactor();queueEvent(()->{zoom=Math.max(.5f,Math.min(3f,zoom*factor));requestRender();});return true;}});
    }
    public void toggleOpen(){queueEvent(()->{if(targetOpen>0){targetOpen=0;targetDisc=0;targetBooklet=0;targetMultiTurn=0;Arrays.fill(targetMultiRemoved,0);Arrays.fill(targetDigipakRemoved,0);if(data.multiCase)targetObi=data.hasObi?0:1;}else{targetOpen=1;targetObi=1;targetWrap=1;}wake();});}
    public void toggleMultiTurn(){if(!data.multiCase)return;queueEvent(()->{targetOpen=1;targetObi=1;Arrays.fill(targetMultiRemoved,0);targetMultiTurn=targetMultiTurn==0?1:0;wake();});}
    public void toggleMultiDisc(int number){if(!data.multiCase||number<1||number>4)return;queueEvent(()->{targetOpen=1;targetObi=1;targetMultiTurn=number>=3?1:0;
        for(int i=0;i<4;i++)if((i>=2)!=(number>=3))targetMultiRemoved[i]=0;
        targetMultiRemoved[number-1]=targetMultiRemoved[number-1]==0?1:0;wake();});}
    public void toggleBooklet(){if(!data.digipak)return;queueEvent(()->{targetBooklet=targetBooklet==0?1:0;if(targetBooklet>0){targetOpen=1;targetObi=targetWrap=1;}wake();});}
    public void toggleDisc(){queueEvent(()->{targetDisc=targetDisc==0?1:0;if(targetDisc>0){targetOpen=1;targetObi=1;targetWrap=1;}wake();});}
    public void toggleDigipakDisc(int number){if(!data.digipak||number<1||number>(data.threeDiscs?3:2))return;
        int part=number==1?DigipakMotion.DISC1:number==2?DigipakMotion.DISC2:DigipakMotion.DISC3;
        if(data.geometry==null||data.geometry.stream().noneMatch(mesh->mesh.part==part))return;
        queueEvent(()->{int i=number-1;targetDigipakRemoved[i]=targetDigipakRemoved[i]==0?1:0;
            if(targetDigipakRemoved[i]>0){targetOpen=1;targetObi=targetWrap=1;}wake();});}
    public void toggleObi(){if(!data.hasObi)return;queueEvent(()->{targetObi=targetObi==0?1:0;targetWrap=1;if(targetObi==0){targetOpen=0;targetDisc=0;targetMultiTurn=0;Arrays.fill(targetMultiRemoved,0);}wake();});}
    public void toggleWrapping(){if(data.digipak||data.multiCase)return;queueEvent(()->{targetWrap=targetWrap==0?1:0;if(targetWrap==0){targetOpen=0;targetDisc=0;targetObi=data.hasObi?0:1;}wake();});}
    public void reset(){queueEvent(()->{yaw=-20;pitch=12;zoom=1;panX=panY=0;targetOpen=0;targetDisc=0;targetBooklet=0;targetMultiTurn=0;Arrays.fill(targetMultiRemoved,0);Arrays.fill(targetDigipakRemoved,0);targetObi=data.hasObi?0:1;targetWrap=data.wrapped?0:1;wake();});}
    private void wake(){lastFrame=0;requestRender();}
    @Override public boolean onGenericMotionEvent(MotionEvent event){
        if(event.getActionMasked()==MotionEvent.ACTION_SCROLL&&event.isFromSource(InputDevice.SOURCE_CLASS_POINTER)){
            float scroll=event.getAxisValue(MotionEvent.AXIS_VSCROLL);
            if(Float.isFinite(scroll)&&scroll!=0){float factor=(float)Math.exp(Math.max(-20,Math.min(20,scroll))*.14f);
                queueEvent(()->{zoom=Math.max(.5f,Math.min(3f,zoom*factor));requestRender();});return true;}
        }return super.onGenericMotionEvent(event);
    }
    @Override public boolean onTouchEvent(MotionEvent event){
        int action=event.getActionMasked();
        if(action==MotionEvent.ACTION_DOWN){discPull.reset();gestureDisc=-1;hingeGesture=false;hingeTriggered=false;}
        if(action==MotionEvent.ACTION_POINTER_DOWN&&event.getPointerCount()==2&&!discPull.captured()){
            float[] inverse=discHitInverse;
            boolean captured=false;
            if(inverse!=null){
                if(data.digipak){for(int i=0;i<(data.threeDiscs?3:2);i++)if((digipakHitMask&(1<<i))!=0&&beginDisc(event,inverse,i)){gestureDisc=i;captured=true;break;}}
                else if(beginDisc(event,inverse,0)){gestureDisc=0;captured=true;}
            }
            if(captured){
                MotionEvent cancel=MotionEvent.obtain(event);cancel.setAction(MotionEvent.ACTION_CANCEL);taps.onTouchEvent(cancel);pinch.onTouchEvent(cancel);cancel.recycle();
            }
        }
        if(discPull.captured()){
            if(action==MotionEvent.ACTION_MOVE&&event.getPointerCount()==2&&discHitInverse!=null){
                if(discPull.move(event.getPointerId(0),event.getX(0),event.getY(0),event.getPointerId(1),event.getX(1),event.getY(1),getResources().getDisplayMetrics().density)){
                    int selected=gestureDisc;queueEvent(()->{if(targetOpen==1&&open>.98f){
                        if(data.digipak&&selected>=0&&selected<3&&targetDigipakRemoved[selected]==0)targetDigipakRemoved[selected]=1;
                        else if(!data.digipak&&targetDisc==0)targetDisc=1;wake();}});
                }
            }else if(event.getPointerCount()!=2||action==MotionEvent.ACTION_POINTER_UP||discHitInverse==null)discPull.cancel();
            if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){discPull.reset();gestureDisc=-1;lastPointerCount=0;hingeGesture=false;}
            return true;
        }
        if(action==MotionEvent.ACTION_POINTER_DOWN&&event.getPointerCount()==2){hingeGesture=sideFacing;hingeTriggered=false;hingeSpan=Math.abs(event.getX(1)-event.getX(0));}
        if(action==MotionEvent.ACTION_DOWN){resetGesture=false;lastPointerCount=0;}
        taps.onTouchEvent(event);pinch.onTouchEvent(event);
        // Rebase whenever fingers enter/leave, excluding the lifted pointer. This avoids
        // jumps when pointer zero lifts or a pinch becomes a one-finger rotation.
        int skip=action==MotionEvent.ACTION_POINTER_UP?event.getActionIndex():-1;
        int count=event.getPointerCount()-(skip>=0?1:0),first=skip==0?1:0;
        float x=event.getX(first),y=event.getY(first),fx=0,fy=0;
        for(int i=0;i<event.getPointerCount();i++)if(i!=skip){fx+=event.getX(i);fy+=event.getY(i);}
        fx/=count;fy/=count;
        if(action==MotionEvent.ACTION_MOVE&&count==lastPointerCount&&!resetGesture){
            if(count==2){
                if(hingeGesture&&!hingeTriggered){float span=Math.abs(event.getX(1)-event.getX(0));float delta=span-hingeSpan;
                    if(Math.abs(delta)>48*getResources().getDisplayMetrics().density&&Math.abs(event.getY(1)-event.getY(0))<span){hingeTriggered=true;boolean opening=delta>0;queueEvent(()->{targetOpen=opening?1:0;if(opening){targetObi=targetWrap=1;}else {targetDisc=0;targetBooklet=0;Arrays.fill(targetDigipakRemoved,0);}wake();});}
                }
                float dx=(fx-lastFocusX)/Math.max(1,getWidth()),dy=(fy-lastFocusY)/Math.max(1,getHeight());
                queueEvent(()->{panX=Math.max(-1,Math.min(1,panX+dx));panY=Math.max(-1,Math.min(1,panY+dy));requestRender();});
            }else if(count==1&&!pinch.isInProgress()){float dx=x-lastX,dy=y-lastY;queueEvent(()->{yaw+=dx*.3f;pitch=Math.max(-85,Math.min(85,pitch+dy*.3f));requestRender();});}
        }
        lastX=x;lastY=y;lastFocusX=fx;lastFocusY=fy;lastPointerCount=count;
        if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){lastPointerCount=0;hingeGesture=false;hingeTriggered=false;}
        return true;
    }
    private boolean beginDisc(MotionEvent event,float[] inverse,int disc){return discPull.begin(event.getPointerId(0),event.getX(0),event.getY(0),discRadius(inverse,event.getX(0),event.getY(0),disc),event.getPointerId(1),event.getX(1),event.getY(1),discRadius(inverse,event.getX(1),event.getY(1),disc));}
    private float discRadius(float[] inverse,float x,float y,int disc){
        float nx=x/Math.max(1,getWidth())*2-1,ny=1-y/Math.max(1,getHeight())*2;
        float[] a=new float[4],b=new float[4];Matrix.multiplyMV(a,0,inverse,0,new float[]{nx,ny,-1,1},0);Matrix.multiplyMV(b,0,inverse,0,new float[]{nx,ny,1,1},0);
        if(Math.abs(a[3])<.00001f||Math.abs(b[3])<.00001f)return Float.NaN;
        for(int i=0;i<3;i++){a[i]/=a[3];b[i]/=b[3];}
        float dz=b[2]-a[2];if(dz>=-.0001f)return Float.NaN;
        float scale=1.42f/2.42f;
        float z=data.digipak?.028f:data.desktopGeometry?(-.177f*.136f)*scale:.012f;
        float t=(z-a[2])/dz;if(t<0||t>1)return Float.NaN;
        float cx=data.digipak?(disc==2?3.025f:disc==1?(data.threeDiscs?1.535f:1.47f):-.01f):data.desktopGeometry?.044f*scale:.06f,cy=data.digipak?.005f:data.desktopGeometry?.004f*scale:0,r=data.digipak?.60f:data.desktopGeometry?1.018f*scale:.60f;
        return (float)Math.hypot(a[0]+t*(b[0]-a[0])-cx,a[1]+t*(b[1]-a[1])-cy)/r;
    }
    private static float approach(float value,float target,float step){return value+Math.signum(target-value)*Math.min(Math.abs(target-value),step);}
    private boolean advance(float seconds){float step=seconds*2.4f;
        if(data.multiCase){
            obiRemoved=approach(obiRemoved,targetOpen==0&&open>0?1:targetObi,step);
            if(targetOpen>open&&obiRemoved==1)open=approach(open,targetOpen,step*.7f);
            boolean discsMoving=false;
            for(int i=0;i<4;i++){float goal=targetMultiRemoved[i];if(goal>0&&(open<1||i>=2&&multiTurn<1||i<2&&multiTurn>0))goal=0;
                multiRemoved[i]=approach(multiRemoved[i],goal,step);discsMoving|=multiRemoved[i]!=targetMultiRemoved[i];}
            boolean occupied=false;for(float removed:multiRemoved)occupied|=removed>.001f;
            if(!occupied&&targetMultiTurn<multiTurn)multiTurn=approach(multiTurn,targetMultiTurn,step*.7f);
            if(!occupied&&targetMultiTurn>multiTurn&&open==1)multiTurn=approach(multiTurn,targetMultiTurn,step*.7f);
            if(targetOpen<open&&multiTurn==0&&!occupied)open=approach(open,targetOpen,step*.7f);
            return open!=targetOpen||multiTurn!=targetMultiTurn||discsMoving||obiRemoved!=targetObi;
        }
        // Clearance sequence: film -> obi -> lid -> disc, reversed when closing.
        boolean digipakDiscsMoving=false,anyDigipakRemoved=false;
        if(data.digipak)for(int i=0;i<3;i++){
            if(targetDigipakRemoved[i]<digipakRemoved[i]||open==1)digipakRemoved[i]=approach(digipakRemoved[i],targetDigipakRemoved[i],step);
            digipakDiscsMoving|=digipakRemoved[i]!=targetDigipakRemoved[i];anyDigipakRemoved|=digipakRemoved[i]>0;
        }
        float wrapGoal=(targetOpen>0||open>0||discRemoved>0||anyDigipakRemoved||(data.hasObi&&obiRemoved!=targetObi))?1:targetWrap;
        wrapRemoved=approach(wrapRemoved,wrapGoal,step);
        if(wrapRemoved==1&&(targetObi>obiRemoved||open==0))obiRemoved=approach(obiRemoved,targetObi,step);
        if(targetDisc<discRemoved||open==1)discRemoved=approach(discRemoved,targetDisc,step);
        if(targetBooklet<bookletRemoved||open==1)bookletRemoved=approach(bookletRemoved,targetBooklet,step);
        if((targetOpen>open&&wrapRemoved==1&&obiRemoved==1)||(targetOpen<open&&discRemoved==0&&!anyDigipakRemoved&&bookletRemoved==0))open=approach(open,targetOpen,step*(data.digipak?.4f:1));
        return open!=targetOpen||discRemoved!=targetDisc||digipakDiscsMoving||bookletRemoved!=targetBooklet||obiRemoved!=targetObi||wrapRemoved!=targetWrap;
    }
    @Override public void onSurfaceCreated(GL10 unused,EGLConfig config){
        program=glCreateProgram();int vertex=shader(GL_VERTEX_SHADER,"uniform mat4 mvp;uniform mat4 model;attribute vec3 pos;attribute vec2 uv;attribute vec3 normal;varying vec2 tex;varying vec3 n;varying vec3 p;varying vec3 local;void main(){gl_Position=mvp*vec4(pos,1.0);tex=uv;n=normalize(mat3(model)*normal);p=(model*vec4(pos,1.0)).xyz;local=pos;}");
        int fragment=shader(GL_FRAGMENT_SHADER,"precision mediump float;uniform sampler2D image;uniform vec4 color;uniform float finish;varying vec2 tex;varying vec3 n;varying vec3 p;varying vec3 local;void main(){vec4 c=texture2D(image,tex)*color;if(c.a<0.004)discard;vec3 N=normalize(n);vec3 L=normalize(vec3(-0.5,0.9,2.0));vec3 V=normalize(vec3(0.0,0.0,5.0)-p);float light=0.76+0.24*max(dot(N,L),0.0);float spec=pow(max(dot(N,normalize(L+V)),0.0),finish>2.5?90.0:52.0);vec3 rgb=c.rgb*light;float alpha=c.a;if(finish>0.5){rgb+=vec3(spec*(finish>1.5?0.7:0.25));}if(finish>1.5&&finish<2.5){float a=atan(local.y,local.x-0.06);vec3 rainbow=0.5+0.5*cos(vec3(0.0,2.1,4.2)+a*2.0+dot(N,V)*8.0);rgb=mix(rgb,rainbow,0.16+0.15*spec);}if(finish>2.5){alpha=min(0.22,c.a*(1.0+spec*2.0+pow(1.0-abs(dot(N,V)),5.0)*2.5));}gl_FragColor=vec4(rgb,alpha);}");
        glAttachShader(program,vertex);glAttachShader(program,fragment);glLinkProgram(program);int[] ok=new int[1];glGetProgramiv(program,GL_LINK_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(glGetProgramInfoLog(program));glDeleteShader(vertex);glDeleteShader(fragment);
        position=glGetAttribLocation(program,"pos");uv=glGetAttribLocation(program,"uv");normal=glGetAttribLocation(program,"normal");mvpUniform=glGetUniformLocation(program,"mvp");modelUniform=glGetUniformLocation(program,"model");colorUniform=glGetUniformLocation(program,"color");textureUniform=glGetUniformLocation(program,"image");finishUniform=glGetUniformLocation(program,"finish");
        textures.clear();int[] ids=new int[data.images.size()+1];glGenTextures(ids.length,ids,0);int index=0;
        android.graphics.Bitmap white=android.graphics.Bitmap.createBitmap(1,1,android.graphics.Bitmap.Config.ARGB_8888);white.eraseColor(android.graphics.Color.WHITE);upload(ids[index],white);textures.put("",ids[index++]);white.recycle();
        for(var entry:data.images.entrySet()){upload(ids[index],entry.getValue());textures.put(entry.getKey(),ids[index++]);}
        draws.clear();for(CaseGeometry.Mesh mesh:CaseGeometry.build(data)){
            float[] bind=data.digipak&&(mesh.part==DigipakMotion.FOLD_LEFT||mesh.part==DigipakMotion.FOLD_RIGHT||mesh.part==DigipakMotion.FOLD_FAR)?foldBinds.computeIfAbsent(mesh,m->{float[] values=new float[m.count*8];var buffer=m.vertices.duplicate();buffer.position(0);buffer.get(values);return values;}):new float[0];
            draws.add(new Draw(mesh,bind));
        }glEnable(GL_DEPTH_TEST);glEnable(GL_CULL_FACE);glBlendFunc(GL_SRC_ALPHA,GL_ONE_MINUS_SRC_ALPHA);lastFrame=0;
    }
    private static int shader(int type,String source){int id=glCreateShader(type);glShaderSource(id,source);glCompileShader(id);int[] ok=new int[1];glGetShaderiv(id,GL_COMPILE_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(glGetShaderInfoLog(id));return id;}
    private static void upload(int id,android.graphics.Bitmap image){glBindTexture(GL_TEXTURE_2D,id);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);GLUtils.texImage2D(GL_TEXTURE_2D,0,image,0);}
    @Override public void onSurfaceChanged(GL10 unused,int width,int height){glViewport(0,0,width,height);aspect=(float)width/Math.max(1,height);}
    @Override public void onDrawFrame(GL10 unused){long now=android.os.SystemClock.uptimeMillis();float step=lastFrame==0?.016f:Math.min(.05f,(now-lastFrame)/1000f);lastFrame=now;boolean animating=advance(step);
        glClearColor(.055f,.075f,.095f,1);glClear(GL_COLOR_BUFFER_BIT|GL_DEPTH_BUFFER_BIT);glUseProgram(program);
        float furthestDisc=discRemoved;if(data.digipak)for(float removed:digipakRemoved)furthestDisc=Math.max(furthestDisc,removed);
        Matrix.perspectiveM(projection,0,42,aspect,.5f,30);float distance=(3.0f+(data.threeDiscs?4.5f:data.digipak?3.5f:data.multiCase?3.5f:2.5f)*open+.35f*furthestDisc+.5f*bookletRemoved)*Math.max(1,(data.digipak||data.multiCase?1.0f:.8f)/aspect);
        Matrix.setLookAtM(view,0,0,0,distance,0,0,0,0,1,0);Matrix.multiplyMM(vp,0,projection,0,view,0);
        float visibleHeight=2*distance*(float)Math.tan(Math.toRadians(21));
        sideFacing=Math.abs(Math.cos(Math.toRadians(yaw)))<.55&&Math.abs(pitch)<55;
        Matrix.setIdentityM(root,0);Matrix.translateM(root,0,panX*visibleHeight*aspect,-panY*visibleHeight,0);Matrix.scaleM(root,0,zoom,zoom,zoom);Matrix.rotateM(root,0,pitch,1,0,0);Matrix.rotateM(root,0,yaw,0,1,0);Matrix.translateM(root,0,data.digipak?0:.55f*open,0,0);
        if(!data.multiCase&&open>.98f&&targetOpen==1&&(data.digipak||discRemoved<.01f&&targetDisc==0)){
            float[] transform=new float[16],inverse=new float[16];Matrix.multiplyMM(transform,0,vp,0,root,0);discHitInverse=Matrix.invertM(inverse,0,transform,0)?inverse:null;
        }else discHitInverse=null;
        digipakHitMask=0;if(data.digipak&&discHitInverse!=null)for(int i=0;i<(data.threeDiscs?3:2);i++)
            if((digipakPresentMask&(1<<i))!=0&&digipakRemoved[i]<.01f&&targetDigipakRemoved[i]==0)digipakHitMask|=1<<i;
        glUniform1i(textureUniform,0);glActiveTexture(GL_TEXTURE0);glEnableVertexAttribArray(position);glEnableVertexAttribArray(uv);glEnableVertexAttribArray(normal);
        opaque.clear();transparent.clear();for(Draw draw:draws){CaseGeometry.Mesh mesh=draw.mesh;float visibility=data.multiCase?1:data.digipak?1:mesh.part==CaseGeometry.OBI?1-obiRemoved:mesh.part>=CaseGeometry.FILM_TOP?1-wrapRemoved:1;if(visibility<=.001f)continue;draw.alpha=mesh.color[3]*visibility;
            System.arraycopy(root,0,draw.model,0,16);float[] model=draw.model;
            if(data.multiCase){multiCasePose(draw);}else if(data.digipak){digipakPose(draw);}else {
            if(mesh.part==CaseGeometry.LID){float hx=data.desktopGeometry?-1.12660f*(1.42f/2.42f):-.69f,hz=data.desktopGeometry?0:.045f;Matrix.translateM(model,0,hx,0,hz);Matrix.rotateM(model,0,-155*open,0,1,0);Matrix.translateM(model,0,-hx,0,-hz);}
            if(mesh.part==CaseGeometry.DISC){float cx=data.desktopGeometry?.044f*(1.42f/2.42f):.06f,cy=data.desktopGeometry?.004f*(1.42f/2.42f):0;Matrix.translateM(model,0,.30f*discRemoved,.08f*discRemoved,.65f*discRemoved);Matrix.translateM(model,0,cx,cy,0);Matrix.rotateM(model,0,-25*discRemoved,1,0,0);Matrix.translateM(model,0,-cx,-cy,0);}
            if(mesh.part==CaseGeometry.OBI)Matrix.translateM(model,0,-1.2f*obiRemoved,0,.06f*obiRemoved);
            if(mesh.part==CaseGeometry.FILM_TOP)Matrix.translateM(model,0,0,.8f*wrapRemoved,.15f*wrapRemoved);
            if(mesh.part==CaseGeometry.FILM_BOTTOM)Matrix.translateM(model,0,0,-.3f*wrapRemoved,.08f*wrapRemoved);
            if(mesh.part==CaseGeometry.TAPE)Matrix.translateM(model,0,1.3f*wrapRemoved,0,.2f*wrapRemoved);
            }
            draw.depth=model[2]*mesh.x+model[6]*mesh.y+model[10]*mesh.z+model[14];
            (draw.alpha<1?transparent:opaque).add(draw);
        }
        glDisable(GL_BLEND);glDepthMask(true);for(Draw draw:opaque)draw(draw);
        transparent.sort(Comparator.comparingDouble(d->d.depth));glEnable(GL_BLEND);glDepthMask(false);for(Draw draw:transparent)draw(draw);glDepthMask(true);
        if(animating)requestRender();
    }
    private void multiCasePose(Draw draw){
        int part=draw.mesh.part;float[] model=draw.model;
        float unit=(2.42f/142f)*(1.42f/2.42f),hinge=-65.75f*unit;
        boolean center=part==1||part==2||part==3||part==4||part==5;
        boolean front=part==2||part==3;
        if(center){Matrix.translateM(model,0,hinge,0,-7*unit);Matrix.rotateM(model,0,-180*multiTurn,0,1,0);Matrix.translateM(model,0,-hinge,0,7*unit);}
        if(front){Matrix.translateM(model,0,hinge,0,7*unit);Matrix.rotateM(model,0,-180*open+180*multiTurn,0,1,0);Matrix.translateM(model,0,-hinge,0,-7*unit);}
        if(part>=3&&part<=6){float removed=multiRemoved[part-3];Matrix.translateM(model,0,.28f*removed,.08f*removed,.55f*removed);}
        if(part==7)Matrix.translateM(model,0,-1.1f*obiRemoved,0,.05f*obiRemoved);
    }
    private void digipakPose(Draw draw){
        int part=draw.mesh.part;float[] model=draw.model;
        if(part==DigipakMotion.FOLD_LEFT||part==DigipakMotion.FOLD_RIGHT||part==DigipakMotion.FOLD_FAR){
            if(draw.foldPose==open)return;draw.foldPose=open;boolean right=part==DigipakMotion.FOLD_RIGHT;
            for(int i=0;i<draw.bind.length;i+=8){float[] p=data.threeDiscs?DigipakMotion.fold3(part,open,draw.bind[i],draw.bind[i+2]):DigipakMotion.fold(right,open,draw.bind[i],draw.bind[i+2]);draw.mesh.vertices.put(i,p[0]);draw.mesh.vertices.put(i+2,p[1]);
                float nz=draw.bind[i+7];draw.mesh.vertices.put(i+5,-p[2]*nz);draw.mesh.vertices.put(i+7,-p[3]*nz);}
            return;
        }
        boolean left=part==DigipakMotion.LEFT||part==DigipakMotion.BOOKLET,right=part==DigipakMotion.RIGHT||part==DigipakMotion.DISC2;
        boolean far=data.threeDiscs&&(part==DigipakMotion.FAR_RIGHT||part==DigipakMotion.DISC3);
        if(right||far){float x=data.threeDiscs ? .7725f : .74f,z=data.threeDiscs ? .09f : .045f;Matrix.translateM(model,0,x,0,z);Matrix.rotateM(model,0,data.threeDiscs?DigipakMotion.rightAngle3(open):DigipakMotion.rightAngle(open),0,1,0);Matrix.translateM(model,0,-x,0,-z);}
        if(far){Matrix.translateM(model,0,2.29f,0,.045f);Matrix.rotateM(model,0,DigipakMotion.farAngle3(open),0,1,0);Matrix.translateM(model,0,-2.29f,0,-.045f);}
        if(left){float x=data.threeDiscs?-.78f:-.75f,z=data.threeDiscs ? .105f : .05f;Matrix.translateM(model,0,x,0,z);Matrix.rotateM(model,0,data.threeDiscs?DigipakMotion.leftAngle3(open):DigipakMotion.leftAngle(open),0,1,0);Matrix.translateM(model,0,-x,0,-z);}
        if(part==DigipakMotion.DISC1||part==DigipakMotion.DISC2||part==DigipakMotion.DISC3){int index=part==DigipakMotion.DISC1?0:part==DigipakMotion.DISC2?1:2;float removed=digipakRemoved[index];float cx=index==0?-.01f:index==1?(data.threeDiscs?1.535f:1.47f):3.025f;Matrix.translateM(model,0,.30f*removed,.08f*removed,.65f*removed);Matrix.translateM(model,0,cx,.005f,0);Matrix.rotateM(model,0,-25*removed,1,0,0);Matrix.translateM(model,0,-cx,-.005f,0);}
        if(part==DigipakMotion.BOOKLET)Matrix.translateM(model,0,data.sideBooklet?-1.32f*bookletRemoved:0,data.sideBooklet?0:1.25f*bookletRemoved,.06f*bookletRemoved);
    }
    private void draw(Draw draw){CaseGeometry.Mesh mesh=draw.mesh;Matrix.multiplyMM(mvp,0,vp,0,draw.model,0);glUniformMatrix4fv(mvpUniform,1,false,mvp,0);glUniformMatrix4fv(modelUniform,1,false,draw.model,0);System.arraycopy(mesh.color,0,tint,0,4);tint[3]=draw.alpha;glUniform4fv(colorUniform,1,tint,0);glUniform1f(finishUniform,mesh.finish);
        glBindTexture(GL_TEXTURE_2D,textures.getOrDefault(mesh.texture,textures.get("")));mesh.vertices.position(0);glVertexAttribPointer(position,3,GL_FLOAT,false,32,mesh.vertices);mesh.vertices.position(3);glVertexAttribPointer(uv,2,GL_FLOAT,false,32,mesh.vertices);mesh.vertices.position(5);glVertexAttribPointer(normal,3,GL_FLOAT,false,32,mesh.vertices);glDrawArrays(GL_TRIANGLES,0,mesh.count);
    }
}
