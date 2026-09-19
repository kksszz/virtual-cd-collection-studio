package jp.virtualcd.player;

import android.app.Instrumentation;
import android.graphics.Bitmap;
import android.opengl.*;
import jp.virtualcd.player.case3d.*;
import java.io.*;
import java.nio.*;
import java.util.*;
import java.util.zip.*;
import org.json.JSONObject;
import static android.opengl.EGL14.*;
import static android.opengl.GLES20.*;

final class CaseDeviceChecks {
    static void runGlb(Instrumentation test)throws Exception {
        runGlb(test,false);
    }
    static void runGlb(Instrumentation test,boolean real)throws Exception {
        byte[] legacy,glb;try(var input=real?new FileInputStream(new File(test.getTargetContext().getCacheDir(),"glb-check.vcd3d")):test.getContext().getAssets().open("case3d-standard.vcd3d")){legacy=CasePackage.readBytes(input,CasePackage.MAX_BYTES);}
        try(var input=real?new FileInputStream(new File(test.getTargetContext().getCacheDir(),"glb-check.glb")):test.getContext().getAssets().open("case3d-standard.glb")){glb=CasePackage.readBytes(input,CasePackage.MAX_BYTES);}
        try(CasePackage old=CasePackage.parse(legacy);CasePackage standard=CasePackage.parse(glb)){
            if(!old.title.equals(standard.title)||!old.artist.equals(standard.artist)||old.images.size()!=standard.images.size())throw new AssertionError("GLB metadata/images");
            int[][] before=render(test,old),after=render(test,standard);
            for(int pose=0;pose<before.length;pose++){long delta=0;for(int i=0;i<before[pose].length;i++)for(int shift:new int[]{0,8,16})delta+=Math.abs(((before[pose][i]>>shift)&255)-((after[pose][i]>>shift)&255));
                double mean=delta/(before[pose].length*3.0);if(mean>1.0)throw new AssertionError("GLB visual difference pose="+pose+" mean="+mean);
            }
        }
        byte[] broken=glb.clone();broken[8]=0;reject(broken);reject(Arrays.copyOf(glb,glb.length-4));
        for(String mode:new String[]{"range","count","uri","profile","required"})reject(breakGlb(glb,mode));
        if(!real)try(var input=test.getContext().getAssets().open("case3d-standard.empty.glb");CasePackage empty=CasePackage.parse(CasePackage.readBytes(input,CasePackage.MAX_BYTES))){if(!empty.images.isEmpty()||empty.hasObi||empty.wrapped)throw new AssertionError("Empty artwork GLB");render(test,empty);}
    }
    private static byte[] breakGlb(byte[] glb,String mode)throws Exception {
        ByteBuffer input=ByteBuffer.wrap(glb).order(ByteOrder.LITTLE_ENDIAN);int size=input.getInt(12);
        JSONObject root=new JSONObject(new String(glb,20,size,java.nio.charset.StandardCharsets.UTF_8));
        if(mode.equals("range"))root.getJSONArray("bufferViews").getJSONObject(0).put("byteOffset",Integer.MAX_VALUE);
        if(mode.equals("count"))root.getJSONArray("accessors").getJSONObject(0).put("count",Integer.MAX_VALUE);
        if(mode.equals("uri"))root.getJSONArray("images").getJSONObject(0).put("uri","https://invalid.example/should-not-fetch.png");
        if(mode.equals("profile"))root.getJSONObject("extras").getJSONObject("virtualCd").put("profile","unknown");
        if(mode.equals("required"))root.put("extensionsRequired",new org.json.JSONArray().put("UNSUPPORTED_test"));
        byte[] json=root.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);int padded=(json.length+3)&~3;int length=20+padded+glb.length-20-size;
        ByteBuffer output=ByteBuffer.allocate(length).order(ByteOrder.LITTLE_ENDIAN);output.putInt(0x46546c67).putInt(2).putInt(length).putInt(padded).putInt(0x4e4f534a).put(json);while(output.position()<20+padded)output.put((byte)32);output.put(glb,20+size,glb.length-20-size);return output.array();
    }
    static void run(Instrumentation test)throws Exception {
        runPackage(test,"case3d-test.vcd3d",false);
        runPackage(test,"case3d-v2-test.vcd3d",true);
    }
    private static void runPackage(Instrumentation test,String asset,boolean obi)throws Exception {
        byte[] bytes;try(InputStream input=test.getContext().getAssets().open(asset)){bytes=CasePackage.readBytes(input,CasePackage.MAX_BYTES);}
        try(CasePackage data=CasePackage.parse(bytes)){
            if(!data.title.equals("3D TEST")||data.images.size()!=(obi?7:4)||data.images.get("front").getWidth()!=1024||data.hasObi!=obi||data.wrapped!=obi)throw new AssertionError("Desktop export round trip");
            if(obi&&(Math.abs(data.obiFrontWidth-.4f)>.002f||Math.abs(data.obiBackWidth-.3f)>.002f))throw new AssertionError("Obi fold dimensions");
            reject(rewrite(bytes,"version"));reject(rewrite(bytes,"hash"));reject(rewrite(bytes,"path"));reject(rewrite(bytes,"unknown"));
            if(obi){reject(rewrite(bytes,"obiWidth"));reject(rewrite(bytes,"obiMissing"));}
            try{CasePackage.readBytes(new ByteArrayInputStream(new byte[11]),10);throw new AssertionError("Read bound");}catch(IOException expected){}
            render(test,data);
        }
        if(obi)try(CasePackage clear=CasePackage.parse(rewrite(bytes,"clearTray"))){render(test,clear);}
    }
    private static void reject(byte[] data)throws Exception {try(CasePackage ignored=CasePackage.parse(data)){throw new AssertionError("Malformed package accepted");}catch(IOException|org.json.JSONException expected){}}
    private static byte[] rewrite(byte[] data,String mode)throws Exception {
        ByteArrayOutputStream output=new ByteArrayOutputStream();
        try(ZipInputStream input=new ZipInputStream(new ByteArrayInputStream(data));ZipOutputStream zip=new ZipOutputStream(output)){
            ZipEntry entry;while((entry=input.getNextEntry())!=null){byte[] content=CasePackage.readBytes(input,CasePackage.MAX_BYTES);String name=entry.getName();
                if(name.equals("manifest.json")){JSONObject json=new JSONObject(new String(content,java.nio.charset.StandardCharsets.UTF_8));
                    if(mode.equals("version"))json.put("version",99);
                    if(mode.equals("clearTray"))json.put("tray","Clear");
                    if(mode.equals("hash"))json.getJSONObject("textures").getJSONObject("front").put("sha256","bad");
                    if(mode.equals("path"))json.getJSONObject("textures").getJSONObject("front").put("file","../front.png");
                    if(mode.equals("obiWidth"))json.getJSONObject("obi").put("frontWidthMm",9999);
                    if(mode.equals("obiMissing"))json.getJSONObject("textures").remove("obiSpine");
                    content=json.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
                }zip.putNextEntry(new ZipEntry(name));zip.write(content);zip.closeEntry();
            }
            if(mode.equals("unknown")){zip.putNextEntry(new ZipEntry("../unexpected"));zip.write(0);zip.closeEntry();}
        }return output.toByteArray();
    }
    private static int[][] render(Instrumentation test,CasePackage data)throws Exception {
        EGLDisplay display=eglGetDisplay(EGL_DEFAULT_DISPLAY);int[] version=new int[2];if(!eglInitialize(display,version,0,version,1))throw new AssertionError("EGL init");
        EGLConfig[] configs=new EGLConfig[1];int[] count=new int[1];eglChooseConfig(display,new int[]{EGL_RENDERABLE_TYPE,EGL_OPENGL_ES2_BIT,EGL_SURFACE_TYPE,EGL_PBUFFER_BIT,EGL_RED_SIZE,8,EGL_GREEN_SIZE,8,EGL_BLUE_SIZE,8,EGL_DEPTH_SIZE,16,EGL_NONE},0,configs,0,1,count,0);
        EGLContext context=eglCreateContext(display,configs[0],EGL_NO_CONTEXT,new int[]{EGL_CONTEXT_CLIENT_VERSION,2,EGL_NONE},0);EGLSurface surface=eglCreatePbufferSurface(display,configs[0],new int[]{EGL_WIDTH,512,EGL_HEIGHT,512,EGL_NONE},0);
        if(!eglMakeCurrent(display,surface,surface,context))throw new AssertionError("EGL current");CaseSurface[] renderer=new CaseSurface[1];
        try{
            test.runOnMainSync(()->renderer[0]=new CaseSurface(test.getTargetContext(),data));CaseSurface view=renderer[0];view.onSurfaceCreated(null,null);view.onSurfaceChanged(null,512,512);view.onDrawFrame(null);int[] closed=capture();save(test,closed,"case3d-closed.png");
            java.lang.reflect.Field open=CaseSurface.class.getDeclaredField("open"),target=CaseSurface.class.getDeclaredField("targetOpen");open.setAccessible(true);target.setAccessible(true);
            view.toggleOpen();barrier(view);settle(view);view.onDrawFrame(null);int[] opened=capture();save(test,opened,"case3d-open.png");
            if(open.getFloat(view)!=1||value(view,"wrapRemoved")!=1||value(view,"obiRemoved")!=1)throw new AssertionError("Opening clearance");
            view.toggleDisc();barrier(view);settle(view);view.onDrawFrame(null);int[] removed=capture();save(test,removed,"case3d-disc.png");if(value(view,"discRemoved")!=1||Arrays.equals(removed,opened))throw new AssertionError("Disc extraction");
            captureTray(test,view);
            view.toggleWrapping();barrier(view);settle(view);if(value(view,"discRemoved")!=0||open.getFloat(view)!=0||value(view,"wrapRemoved")!=0||(data.hasObi&&value(view,"obiRemoved")!=0))throw new AssertionError("Repack sequence");
            view.toggleDisc();barrier(view);tick(view);view.reset();barrier(view);settle(view);
            if(value(view,"wrapRemoved")!=(data.wrapped?0:1)||value(view,"obiRemoved")!=(data.hasObi?0:1)||open.getFloat(view)!=0)throw new AssertionError("Interrupted reset");
            if(data.hasObi){view.toggleObi();barrier(view);settle(view);view.onDrawFrame(null);save(test,capture(),"case3d-no-obi.png");if(value(view,"obiRemoved")!=1)throw new AssertionError("Obi detach");view.toggleObi();barrier(view);settle(view);if(value(view,"obiRemoved")!=0)throw new AssertionError("Obi attach");}
            if(Arrays.equals(closed,opened))throw new AssertionError("Hinge unchanged");
            java.lang.reflect.Field yaw=CaseSurface.class.getDeclaredField("yaw");yaw.setAccessible(true);yaw.setFloat(view,160);view.onDrawFrame(null);int[] rotated=capture();if(Arrays.equals(rotated,opened))throw new AssertionError("Rotation unchanged");
            java.lang.reflect.Field pitch=CaseSurface.class.getDeclaredField("pitch"),zoom=CaseSurface.class.getDeclaredField("zoom");pitch.setAccessible(true);zoom.setAccessible(true);pitch.setFloat(view,55);zoom.setFloat(view,2);
            long time=android.os.SystemClock.uptimeMillis();
            test.runOnMainSync(()->{touch(view,time,time,0);touch(view,time,time+30,1);});barrier(view);
            if(yaw.getFloat(view)!=160||zoom.getFloat(view)!=2)throw new AssertionError("Single tap must not reset");
            test.runOnMainSync(()->{touch(view,time+100,time+100,0);touch(view,time+100,time+130,1);});barrier(view);
            if(yaw.getFloat(view)!=-20||pitch.getFloat(view)!=12||zoom.getFloat(view)!=1||target.getFloat(view)!=0)throw new AssertionError("Double tap reset");
            settle(view);if(open.getFloat(view)!=0||value(view,"discRemoved")!=0)throw new AssertionError("Reset closure");
            test.runOnMainSync(()->wheel(view,1));barrier(view);if(value(view,"zoom")<=1)throw new AssertionError("Mouse wheel zoom in");
            test.runOnMainSync(()->wheel(view,-1));barrier(view);if(Math.abs(value(view,"zoom")-1)>.001f)throw new AssertionError("Mouse wheel zoom out");
            test.runOnMainSync(()->wheel(view,100));barrier(view);if(value(view,"zoom")!=3)throw new AssertionError("Mouse wheel upper bound");
            test.runOnMainSync(()->wheel(view,-100));barrier(view);if(value(view,"zoom")!=.5f)throw new AssertionError("Mouse wheel lower bound");
            view.reset();barrier(view);settle(view);
            test.runOnMainSync(()->{
                view.layout(0,0,512,512);long t=android.os.SystemClock.uptimeMillis()+1000;
                fingers(view,t,0,new float[]{100},new float[]{100});
                fingers(view,t+20,261,new float[]{100,300},new float[]{100,100});
                fingers(view,t+40,2,new float[]{150,350},new float[]{130,130});
            });barrier(view);
            if(Math.abs(value(view,"panX")-50f/512)>.001f||Math.abs(value(view,"panY")-30f/512)>.001f||value(view,"yaw")!=-20||value(view,"zoom")!=1)throw new AssertionError("Two finger pan without rotation/scale");
            test.runOnMainSync(()->{long t=android.os.SystemClock.uptimeMillis()+1100;
                fingers(view,t,6,new float[]{150,350},new float[]{130,130});
                fingers(view,t+20,2,new float[]{350},new float[]{130});
                fingers(view,t+40,1,new float[]{350},new float[]{130});
            });barrier(view);
            if(value(view,"yaw")!=-20||value(view,"pitch")!=12)throw new AssertionError("Pointer zero lift must not jump");
            view.reset();barrier(view);if(value(view,"panX")!=0||value(view,"panY")!=0)throw new AssertionError("Pan reset");
            test.runOnMainSync(()->{
                var art=new jp.virtualcd.player.library.ZoomArtworkView(test.getTargetContext());art.layout(0,0,512,512);art.setImageBitmap(data.images.get("front"));
                try{var z=art.getClass().getDeclaredField("zoom");z.setAccessible(true);wheel(art,1);if(z.getFloat(art)<=1)throw new AssertionError("Artwork wheel in");wheel(art,-1);if(Math.abs(z.getFloat(art)-1)>.001f)throw new AssertionError("Artwork wheel out");wheel(art,100);if(z.getFloat(art)!=5)throw new AssertionError("Artwork upper bound");wheel(art,-100);if(z.getFloat(art)!=1)throw new AssertionError("Artwork lower bound");}catch(ReflectiveOperationException error){throw new AssertionError(error);}
            });
            // Every interruption point must converge, including partially removed wrapping/obi.
            for(int i=1;i<=28;i+=3){view.toggleDisc();barrier(view);for(int j=0;j<i;j++)tick(view);view.reset();barrier(view);settle(view);}
            view.onSurfaceChanged(null,512,256);view.onDrawFrame(null);if(glGetError()!=GL_NO_ERROR)throw new AssertionError("Landscape GL error");
            // Simulate context recreation: textures must be rebuilt from retained bitmaps.
            view.onSurfaceCreated(null,null);view.onSurfaceChanged(null,256,512);view.onDrawFrame(null);if(glGetError()!=GL_NO_ERROR)throw new AssertionError("Context restore");
            return new int[][]{closed,opened,removed};
        }finally{if(renderer[0]!=null)test.runOnMainSync(()->renderer[0].onPause());eglMakeCurrent(display,EGL_NO_SURFACE,EGL_NO_SURFACE,EGL_NO_CONTEXT);eglDestroySurface(display,surface);eglDestroyContext(display,context);eglTerminate(display);}
    }
    private static float value(CaseSurface view,String name)throws Exception {var field=CaseSurface.class.getDeclaredField(name);field.setAccessible(true);return field.getFloat(view);}
    private static void fingers(CaseSurface view,long time,int action,float[] xs,float[] ys){
        var props=new android.view.MotionEvent.PointerProperties[xs.length];var coords=new android.view.MotionEvent.PointerCoords[xs.length];
        for(int i=0;i<xs.length;i++){props[i]=new android.view.MotionEvent.PointerProperties();props[i].id=i;props[i].toolType=1;coords[i]=new android.view.MotionEvent.PointerCoords();coords[i].x=xs[i];coords[i].y=ys[i];coords[i].pressure=1;coords[i].size=1;}
        var event=android.view.MotionEvent.obtain(time-100,time,action,xs.length,props,coords,0,0,1,1,0,0,android.view.InputDevice.SOURCE_TOUCHSCREEN,0);
        try{view.onTouchEvent(event);}finally{event.recycle();}
    }
    private static void wheel(android.view.View view,float scroll){
        var props=new android.view.MotionEvent.PointerProperties();props.id=0;props.toolType=android.view.MotionEvent.TOOL_TYPE_MOUSE;
        var coords=new android.view.MotionEvent.PointerCoords();coords.x=256;coords.y=256;coords.setAxisValue(android.view.MotionEvent.AXIS_VSCROLL,scroll);
        long now=android.os.SystemClock.uptimeMillis();var event=android.view.MotionEvent.obtain(now,now,android.view.MotionEvent.ACTION_SCROLL,1,new android.view.MotionEvent.PointerProperties[]{props},new android.view.MotionEvent.PointerCoords[]{coords},0,0,1,1,0,0,android.view.InputDevice.SOURCE_MOUSE,0);
        try{if(!view.onGenericMotionEvent(event))throw new AssertionError("Wheel unhandled");}finally{event.recycle();}
    }
    private static void captureTray(Instrumentation test,CaseSurface view)throws Exception {
        var field=CaseSurface.class.getDeclaredField("draws");field.setAccessible(true);
        @SuppressWarnings("unchecked") List<Object> draws=(List<Object>)field.get(view);
        var saved=new ArrayList<>(draws);var states=new LinkedHashMap<String,Float>();
        try{
            for(Object draw:saved){var mf=draw.getClass().getDeclaredField("mesh");mf.setAccessible(true);Object mesh=mf.get(draw);
                var vf=mesh.getClass().getDeclaredField("vertices");vf.setAccessible(true);FloatBuffer vertices=((FloatBuffer)vf.get(mesh)).duplicate();vertices.position(0);
                while(vertices.hasRemaining())if(!Float.isFinite(vertices.get()))throw new AssertionError("Invalid tray vertex/normal");
                var pf=mesh.getClass().getDeclaredField("part");pf.setAccessible(true);if(pf.getInt(mesh)!=0)draws.remove(draw);
            }
            for(String name:new String[]{"open","targetOpen","discRemoved","targetDisc","yaw","pitch","zoom"}){var f=CaseSurface.class.getDeclaredField(name);f.setAccessible(true);states.put(name,f.getFloat(view));f.setFloat(view,name.equals("zoom")?1.55f:name.equals("pitch")?30:name.equals("yaw")?-15:0);}
            view.onDrawFrame(null);save(test,capture(),"case3d-tray-detail.png");
        }finally{draws.clear();draws.addAll(saved);for(var entry:states.entrySet()){var f=CaseSurface.class.getDeclaredField(entry.getKey());f.setAccessible(true);f.setFloat(view,entry.getValue());}}
    }
    private static boolean tick(CaseSurface view)throws Exception {var advance=CaseSurface.class.getDeclaredMethod("advance",float.class);advance.setAccessible(true);return (boolean)advance.invoke(view,.05f);}
    private static void settle(CaseSurface view)throws Exception {for(int i=0;i<180;i++)if(!tick(view))return;throw new AssertionError("Animation deadlock");}
    private static void touch(CaseSurface view,long down,long time,int action){android.view.MotionEvent event=android.view.MotionEvent.obtain(down,time,action,120,120,0);try{view.onTouchEvent(event);}finally{event.recycle();}}
    private static void barrier(CaseSurface view)throws Exception {var latch=new java.util.concurrent.CountDownLatch(1);view.queueEvent(latch::countDown);if(!latch.await(3,java.util.concurrent.TimeUnit.SECONDS))throw new AssertionError("GL event timeout");}
    private static int[] capture(){ByteBuffer pixels=ByteBuffer.allocateDirect(512*512*4);glReadPixels(0,0,512,512,GL_RGBA,GL_UNSIGNED_BYTE,pixels);if(glGetError()!=GL_NO_ERROR)throw new AssertionError("GL draw error");int[] colors=new int[512*512];int colored=0;for(int y=0;y<512;y++)for(int x=0;x<512;x++){int offset=(y*512+x)*4,r=pixels.get(offset)&255,g=pixels.get(offset+1)&255,b=pixels.get(offset+2)&255;colors[(511-y)*512+x]=0xff000000|(r<<16)|(g<<8)|b;if(r>80||g>80||b>80)colored++;}if(colored<3000)throw new AssertionError("Empty render: "+colored);return colors;}
    private static void save(Instrumentation test,int[] colors,String name)throws Exception {Bitmap bitmap=Bitmap.createBitmap(colors,512,512,Bitmap.Config.ARGB_8888);try(FileOutputStream output=new FileOutputStream(new File(test.getTargetContext().getCacheDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,output);}finally{bitmap.recycle();}}
}
