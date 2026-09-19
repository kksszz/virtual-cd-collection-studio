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
    private final List<Draw> opaque=new ArrayList<>(),transparent=new ArrayList<>(),draws=new ArrayList<>();
    private final ScaleGestureDetector pinch;
    private final GestureDetector taps;
    private boolean resetGesture;
    private volatile boolean sideFacing;
    private boolean hingeGesture,hingeTriggered;
    private float hingeSpan;
    private final DiscPullGesture discPull=new DiscPullGesture();
    private volatile float[] discHitInverse;
    private float panX,panY,lastFocusX,lastFocusY;
    private int lastPointerCount;
    private float yaw=-20,pitch=12,zoom=1,open,targetOpen,discRemoved,targetDisc,obiRemoved,targetObi,wrapRemoved,targetWrap,aspect=1,lastX,lastY;
    private long lastFrame;
    private int program,position,uv,normal,mvpUniform,modelUniform,colorUniform,textureUniform,finishUniform;
    private final float[] projection=new float[16],view=new float[16],root=new float[16],vp=new float[16],mvp=new float[16],tint=new float[4];
    private static final class Draw {final CaseGeometry.Mesh mesh;final float[] model=new float[16];float alpha,depth;Draw(CaseGeometry.Mesh mesh){this.mesh=mesh;}}
    public CaseSurface(Context context,CasePackage data){super(context);this.data=data;obiRemoved=targetObi=data.hasObi?0:1;wrapRemoved=targetWrap=data.wrapped?0:1;
        setEGLContextClientVersion(2);setEGLConfigChooser(8,8,8,0,24,0);setRenderer(this);setRenderMode(RENDERMODE_WHEN_DIRTY);
        setContentDescription("3D CDケース。1本指で回転、2本指のスライドで移動。開いたCDの中心を押さえ、もう1本の指で外周を引くとCDを取り出します。ピンチまたはマウスホイールで拡大縮小、ダブルタップで初期表示");
        taps=new GestureDetector(context,new GestureDetector.SimpleOnGestureListener(){
            @Override public boolean onDown(MotionEvent event){return true;}
            @Override public boolean onDoubleTap(MotionEvent event){resetGesture=true;reset();return true;}
        });taps.setIsLongpressEnabled(false);
        pinch=new ScaleGestureDetector(context,new ScaleGestureDetector.SimpleOnScaleGestureListener(){@Override public boolean onScale(ScaleGestureDetector detector){if(hingeGesture)return true;float factor=detector.getScaleFactor();queueEvent(()->{zoom=Math.max(.5f,Math.min(3f,zoom*factor));requestRender();});return true;}});
    }
    public void toggleOpen(){queueEvent(()->{if(targetOpen>0){targetOpen=0;targetDisc=0;}else{targetOpen=1;targetObi=1;targetWrap=1;}wake();});}
    public void toggleDisc(){queueEvent(()->{targetDisc=targetDisc==0?1:0;if(targetDisc>0){targetOpen=1;targetObi=1;targetWrap=1;}wake();});}
    public void toggleObi(){if(!data.hasObi)return;queueEvent(()->{targetObi=targetObi==0?1:0;targetWrap=1;if(targetObi==0){targetOpen=0;targetDisc=0;}wake();});}
    public void toggleWrapping(){queueEvent(()->{targetWrap=targetWrap==0?1:0;if(targetWrap==0){targetOpen=0;targetDisc=0;targetObi=data.hasObi?0:1;}wake();});}
    public void reset(){queueEvent(()->{yaw=-20;pitch=12;zoom=1;panX=panY=0;targetOpen=0;targetDisc=0;targetObi=data.hasObi?0:1;targetWrap=data.wrapped?0:1;wake();});}
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
        if(action==MotionEvent.ACTION_DOWN){discPull.reset();hingeGesture=false;hingeTriggered=false;}
        if(action==MotionEvent.ACTION_POINTER_DOWN&&event.getPointerCount()==2&&!discPull.captured()){
            float[] inverse=discHitInverse;
            if(inverse!=null&&discPull.begin(event.getPointerId(0),event.getX(0),event.getY(0),discRadius(inverse,event.getX(0),event.getY(0)),event.getPointerId(1),event.getX(1),event.getY(1),discRadius(inverse,event.getX(1),event.getY(1)))){
                MotionEvent cancel=MotionEvent.obtain(event);cancel.setAction(MotionEvent.ACTION_CANCEL);taps.onTouchEvent(cancel);pinch.onTouchEvent(cancel);cancel.recycle();
            }
        }
        if(discPull.captured()){
            if(action==MotionEvent.ACTION_MOVE&&event.getPointerCount()==2&&discHitInverse!=null){
                if(discPull.move(event.getPointerId(0),event.getX(0),event.getY(0),event.getPointerId(1),event.getX(1),event.getY(1),getResources().getDisplayMetrics().density))queueEvent(()->{if(targetOpen==1&&open>.98f&&targetDisc==0){targetDisc=1;wake();}});
            }else if(event.getPointerCount()!=2||action==MotionEvent.ACTION_POINTER_UP||discHitInverse==null)discPull.cancel();
            if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){discPull.reset();lastPointerCount=0;hingeGesture=false;}
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
                    if(Math.abs(delta)>48*getResources().getDisplayMetrics().density&&Math.abs(event.getY(1)-event.getY(0))<span){hingeTriggered=true;boolean opening=delta>0;queueEvent(()->{targetOpen=opening?1:0;if(opening){targetObi=targetWrap=1;}else targetDisc=0;wake();});}
                }
                float dx=(fx-lastFocusX)/Math.max(1,getWidth()),dy=(fy-lastFocusY)/Math.max(1,getHeight());
                queueEvent(()->{panX=Math.max(-1,Math.min(1,panX+dx));panY=Math.max(-1,Math.min(1,panY+dy));requestRender();});
            }else if(count==1&&!pinch.isInProgress()){float dx=x-lastX,dy=y-lastY;queueEvent(()->{yaw+=dx*.3f;pitch=Math.max(-85,Math.min(85,pitch+dy*.3f));requestRender();});}
        }
        lastX=x;lastY=y;lastFocusX=fx;lastFocusY=fy;lastPointerCount=count;
        if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){lastPointerCount=0;hingeGesture=false;hingeTriggered=false;}
        return true;
    }
    private float discRadius(float[] inverse,float x,float y){
        float nx=x/Math.max(1,getWidth())*2-1,ny=1-y/Math.max(1,getHeight())*2;
        float[] a=new float[4],b=new float[4];Matrix.multiplyMV(a,0,inverse,0,new float[]{nx,ny,-1,1},0);Matrix.multiplyMV(b,0,inverse,0,new float[]{nx,ny,1,1},0);
        if(Math.abs(a[3])<.00001f||Math.abs(b[3])<.00001f)return Float.NaN;
        for(int i=0;i<3;i++){a[i]/=a[3];b[i]/=b[3];}
        float dz=b[2]-a[2];if(dz>=-.0001f)return Float.NaN;
        float t=(.012f-a[2])/dz;if(t<0||t>1)return Float.NaN;
        return (float)Math.hypot(a[0]+t*(b[0]-a[0])-.06f,a[1]+t*(b[1]-a[1]))/.60f;
    }
    private static float approach(float value,float target,float step){return value+Math.signum(target-value)*Math.min(Math.abs(target-value),step);}
    private boolean advance(float seconds){float step=seconds*2.4f;
        // Clearance sequence: film -> obi -> lid -> disc, reversed when closing.
        float wrapGoal=(targetOpen>0||open>0||discRemoved>0||(data.hasObi&&obiRemoved!=targetObi))?1:targetWrap;
        wrapRemoved=approach(wrapRemoved,wrapGoal,step);
        if(wrapRemoved==1&&(targetObi>obiRemoved||open==0))obiRemoved=approach(obiRemoved,targetObi,step);
        if(targetDisc<discRemoved||open==1)discRemoved=approach(discRemoved,targetDisc,step);
        if((targetOpen>open&&wrapRemoved==1&&obiRemoved==1)||(targetOpen<open&&discRemoved==0))open=approach(open,targetOpen,step);
        return open!=targetOpen||discRemoved!=targetDisc||obiRemoved!=targetObi||wrapRemoved!=targetWrap;
    }
    @Override public void onSurfaceCreated(GL10 unused,EGLConfig config){
        program=glCreateProgram();int vertex=shader(GL_VERTEX_SHADER,"uniform mat4 mvp;uniform mat4 model;attribute vec3 pos;attribute vec2 uv;attribute vec3 normal;varying vec2 tex;varying vec3 n;varying vec3 p;varying vec3 local;void main(){gl_Position=mvp*vec4(pos,1.0);tex=uv;n=normalize(mat3(model)*normal);p=(model*vec4(pos,1.0)).xyz;local=pos;}");
        int fragment=shader(GL_FRAGMENT_SHADER,"precision mediump float;uniform sampler2D image;uniform vec4 color;uniform float finish;varying vec2 tex;varying vec3 n;varying vec3 p;varying vec3 local;void main(){vec4 c=texture2D(image,tex)*color;if(c.a<0.004)discard;vec3 N=normalize(n);vec3 L=normalize(vec3(-0.5,0.9,2.0));vec3 V=normalize(vec3(0.0,0.0,5.0)-p);float light=0.76+0.24*max(dot(N,L),0.0);float spec=pow(max(dot(N,normalize(L+V)),0.0),finish>2.5?90.0:52.0);vec3 rgb=c.rgb*light;float alpha=c.a;if(finish>0.5){rgb+=vec3(spec*(finish>1.5?0.7:0.25));}if(finish>1.5&&finish<2.5){float a=atan(local.y,local.x-0.06);vec3 rainbow=0.5+0.5*cos(vec3(0.0,2.1,4.2)+a*2.0+dot(N,V)*8.0);rgb=mix(rgb,rainbow,0.16+0.15*spec);}if(finish>2.5){alpha=min(0.22,c.a*(1.0+spec*2.0+pow(1.0-abs(dot(N,V)),5.0)*2.5));}gl_FragColor=vec4(rgb,alpha);}");
        glAttachShader(program,vertex);glAttachShader(program,fragment);glLinkProgram(program);int[] ok=new int[1];glGetProgramiv(program,GL_LINK_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(glGetProgramInfoLog(program));glDeleteShader(vertex);glDeleteShader(fragment);
        position=glGetAttribLocation(program,"pos");uv=glGetAttribLocation(program,"uv");normal=glGetAttribLocation(program,"normal");mvpUniform=glGetUniformLocation(program,"mvp");modelUniform=glGetUniformLocation(program,"model");colorUniform=glGetUniformLocation(program,"color");textureUniform=glGetUniformLocation(program,"image");finishUniform=glGetUniformLocation(program,"finish");
        textures.clear();int[] ids=new int[data.images.size()+1];glGenTextures(ids.length,ids,0);int index=0;
        android.graphics.Bitmap white=android.graphics.Bitmap.createBitmap(1,1,android.graphics.Bitmap.Config.ARGB_8888);white.eraseColor(android.graphics.Color.WHITE);upload(ids[index],white);textures.put("",ids[index++]);white.recycle();
        for(var entry:data.images.entrySet()){upload(ids[index],entry.getValue());textures.put(entry.getKey(),ids[index++]);}
        draws.clear();for(CaseGeometry.Mesh mesh:CaseGeometry.build(data))draws.add(new Draw(mesh));glEnable(GL_DEPTH_TEST);glEnable(GL_CULL_FACE);glBlendFunc(GL_SRC_ALPHA,GL_ONE_MINUS_SRC_ALPHA);lastFrame=0;
    }
    private static int shader(int type,String source){int id=glCreateShader(type);glShaderSource(id,source);glCompileShader(id);int[] ok=new int[1];glGetShaderiv(id,GL_COMPILE_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(glGetShaderInfoLog(id));return id;}
    private static void upload(int id,android.graphics.Bitmap image){glBindTexture(GL_TEXTURE_2D,id);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);GLUtils.texImage2D(GL_TEXTURE_2D,0,image,0);}
    @Override public void onSurfaceChanged(GL10 unused,int width,int height){glViewport(0,0,width,height);aspect=(float)width/Math.max(1,height);}
    @Override public void onDrawFrame(GL10 unused){long now=android.os.SystemClock.uptimeMillis();float step=lastFrame==0?.016f:Math.min(.05f,(now-lastFrame)/1000f);lastFrame=now;boolean animating=advance(step);
        glClearColor(.055f,.075f,.095f,1);glClear(GL_COLOR_BUFFER_BIT|GL_DEPTH_BUFFER_BIT);glUseProgram(program);
        Matrix.perspectiveM(projection,0,42,aspect,.5f,30);float distance=(3.0f+2.5f*open+.35f*discRemoved)*Math.max(1,.8f/aspect);
        Matrix.setLookAtM(view,0,0,0,distance,0,0,0,0,1,0);Matrix.multiplyMM(vp,0,projection,0,view,0);
        float visibleHeight=2*distance*(float)Math.tan(Math.toRadians(21));
        sideFacing=Math.abs(Math.cos(Math.toRadians(yaw)))<.55&&Math.abs(pitch)<55;
        Matrix.setIdentityM(root,0);Matrix.translateM(root,0,panX*visibleHeight*aspect,-panY*visibleHeight,0);Matrix.scaleM(root,0,zoom,zoom,zoom);Matrix.rotateM(root,0,pitch,1,0,0);Matrix.rotateM(root,0,yaw,0,1,0);Matrix.translateM(root,0,.55f*open,0,0);
        if(open>.98f&&targetOpen==1&&discRemoved<.01f&&targetDisc==0){float[] transform=new float[16],inverse=new float[16];Matrix.multiplyMM(transform,0,vp,0,root,0);discHitInverse=Matrix.invertM(inverse,0,transform,0)?inverse:null;}else discHitInverse=null;
        glUniform1i(textureUniform,0);glActiveTexture(GL_TEXTURE0);glEnableVertexAttribArray(position);glEnableVertexAttribArray(uv);glEnableVertexAttribArray(normal);
        opaque.clear();transparent.clear();for(Draw draw:draws){CaseGeometry.Mesh mesh=draw.mesh;float visibility=mesh.part==CaseGeometry.OBI?1-obiRemoved:mesh.part>=CaseGeometry.FILM_TOP?1-wrapRemoved:1;if(visibility<=.001f)continue;draw.alpha=mesh.color[3]*visibility;
            System.arraycopy(root,0,draw.model,0,16);float[] model=draw.model;
            if(mesh.part==CaseGeometry.LID){Matrix.translateM(model,0,-.69f,0,.045f);Matrix.rotateM(model,0,-155*open,0,1,0);Matrix.translateM(model,0,.69f,0,-.045f);}
            if(mesh.part==CaseGeometry.DISC){Matrix.translateM(model,0,.30f*discRemoved,.08f*discRemoved,.65f*discRemoved);Matrix.translateM(model,0,.06f,0,0);Matrix.rotateM(model,0,-25*discRemoved,1,0,0);Matrix.translateM(model,0,-.06f,0,0);}
            if(mesh.part==CaseGeometry.OBI)Matrix.translateM(model,0,-1.2f*obiRemoved,0,.06f*obiRemoved);
            if(mesh.part==CaseGeometry.FILM_TOP)Matrix.translateM(model,0,0,.8f*wrapRemoved,.15f*wrapRemoved);
            if(mesh.part==CaseGeometry.FILM_BOTTOM)Matrix.translateM(model,0,0,-.3f*wrapRemoved,.08f*wrapRemoved);
            if(mesh.part==CaseGeometry.TAPE)Matrix.translateM(model,0,1.3f*wrapRemoved,0,.2f*wrapRemoved);
            draw.depth=model[2]*mesh.x+model[6]*mesh.y+model[10]*mesh.z+model[14];
            (draw.alpha<1?transparent:opaque).add(draw);
        }
        glDisable(GL_BLEND);glDepthMask(true);for(Draw draw:opaque)draw(draw);
        transparent.sort(Comparator.comparingDouble(d->d.depth));glEnable(GL_BLEND);glDepthMask(false);for(Draw draw:transparent)draw(draw);glDepthMask(true);
        if(animating)requestRender();
    }
    private void draw(Draw draw){CaseGeometry.Mesh mesh=draw.mesh;Matrix.multiplyMM(mvp,0,vp,0,draw.model,0);glUniformMatrix4fv(mvpUniform,1,false,mvp,0);glUniformMatrix4fv(modelUniform,1,false,draw.model,0);System.arraycopy(mesh.color,0,tint,0,4);tint[3]=draw.alpha;glUniform4fv(colorUniform,1,tint,0);glUniform1f(finishUniform,mesh.finish);
        glBindTexture(GL_TEXTURE_2D,textures.getOrDefault(mesh.texture,textures.get("")));mesh.vertices.position(0);glVertexAttribPointer(position,3,GL_FLOAT,false,32,mesh.vertices);mesh.vertices.position(3);glVertexAttribPointer(uv,2,GL_FLOAT,false,32,mesh.vertices);mesh.vertices.position(5);glVertexAttribPointer(normal,3,GL_FLOAT,false,32,mesh.vertices);glDrawArrays(GL_TRIANGLES,0,mesh.count);
    }
}
