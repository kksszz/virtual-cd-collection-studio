package jp.virtualcd.player.case3d;

import java.nio.*;
import java.util.*;

/** Independently authored, centimetre-scale jewel case. No desktop STL or third-party mesh. */
final class CaseGeometry {
    static final int BASE=0,LID=1,DISC=2,OBI=3,FILM_TOP=4,FILM_BOTTOM=5,TAPE=6;
    static final float[] WHITE={1,1,1,1},PAPER={.96f,.95f,.91f,1},GLASS={.72f,.85f,.90f,.22f},EDGE={.65f,.79f,.83f,.55f},SILVER={.77f,.81f,.85f,1};
    static final class Mesh {
        final FloatBuffer vertices;
        final int count,part,finish;
        final String texture;
        final float[] color;
        final float x,y,z;
        Mesh(float[] data,String texture,float[] color,int part,int finish){
            vertices=ByteBuffer.allocateDirect(data.length*4).order(ByteOrder.nativeOrder()).asFloatBuffer();vertices.put(data).position(0);
            count=data.length/8;this.texture=texture;this.color=color;this.part=part;this.finish=finish;
            float sx=0,sy=0,sz=0;for(int i=0;i<data.length;i+=8){sx+=data[i];sy+=data[i+1];sz+=data[i+2];}x=sx/count;y=sy/count;z=sz/count;
        }
    }
    private final List<Mesh> meshes=new ArrayList<>();
    private int part,finish;
    static List<Mesh> build(CasePackage data){CaseGeometry g=new CaseGeometry();g.buildCase(data);return g.compact();}
    private List<Mesh> compact(){
        List<Mesh> result=new ArrayList<>();Map<String,List<Mesh>> groups=new LinkedHashMap<>();
        for(Mesh mesh:meshes){if(mesh.color[3]<1||mesh.part>=OBI){result.add(mesh);continue;}
            String key=mesh.part+":"+mesh.finish+":"+mesh.texture+":"+Arrays.toString(mesh.color);groups.computeIfAbsent(key,k->new ArrayList<>()).add(mesh);}
        for(List<Mesh> group:groups.values()){int size=0;for(Mesh m:group)size+=m.count*8;float[] data=new float[size];int offset=0;for(Mesh m:group){FloatBuffer buffer=m.vertices.duplicate();buffer.position(0);buffer.get(data,offset,m.count*8);offset+=m.count*8;}Mesh first=group.get(0);result.add(new Mesh(data,first.texture,first.color,first.part,first.finish));}
        return result;
    }
    private void buildCase(CasePackage data){
        float[] tray=data.tray.equals("White")?new float[]{.84f,.82f,.75f,1}:data.tray.equals("Gray")?new float[]{.37f,.40f,.43f,1}:new float[]{.13f,.14f,.16f,1};
        boolean clear=data.tray.equals("Clear");
        part=BASE;finish=1;
        // Rear shell, printed insert, and tray floor. Clear tray leaves the inlay visible.
        box(-.71f,.71f,-.625f,.625f,-.05f,-.041f,GLASS);
        finish=0;face(-.69f,.69f,-.59f,.59f,-.051f,true,"back",WHITE);
        face(-.69f,.69f,-.59f,.59f,-.036f,false,"inlay",WHITE);
        finish=1;detailedTray(tray,clear);
        // The base's edge channels and two lid pivot sockets.
        for(float y:new float[]{-.625f,.606f})box(-.71f,.71f,y,y+.019f,-.04f,.029f,EDGE);
        box(.693f,.713f,-.605f,.605f,-.035f,.027f,EDGE);
        for(float y:new float[]{-.57f,.51f})box(-.724f,-.681f,y,y+.06f,-.022f,.043f,EDGE);
        finish=0;
        quad(new float[]{.714f,-.59f,.024f,.714f,-.59f,-.038f,.714f,.59f,-.038f,.714f,.59f,.024f},"spine",WHITE);
        quad(new float[]{-.714f,-.59f,-.038f,-.714f,-.59f,.024f,-.714f,.59f,.024f,-.714f,.59f,-.038f},"rightSpine",WHITE);
        part=LID;
        // Booklet has paper thickness and separately printed inside/outside.
        // Only paper edges: coincident box faces would z-fight with the printed panels.
        quad(new float[]{-.53f,-.60f,.031f,-.53f,-.60f,.043f,-.53f,.60f,.043f,-.53f,.60f,.031f},"",PAPER);
        quad(new float[]{.67f,-.60f,.043f,.67f,-.60f,.031f,.67f,.60f,.031f,.67f,.60f,.043f},"",PAPER);
        quad(new float[]{-.53f,.60f,.043f,.67f,.60f,.043f,.67f,.60f,.031f,-.53f,.60f,.031f},"",PAPER);
        quad(new float[]{-.53f,-.60f,.031f,.67f,-.60f,.031f,.67f,-.60f,.043f,-.53f,-.60f,.043f},"",PAPER);
        face(-.53f,.67f,-.60f,.60f,.0435f,false,"front",WHITE);
        face(-.53f,.67f,-.60f,.60f,.0305f,true,"insideFront",WHITE);
        for(int i=0;i<4;i++)box(.669f,.672f,-.60f,.60f,.032f+i*.0025f,.0326f+i*.0025f,new float[]{.64f,.64f,.62f,1});
        finish=1;
        box(-.71f,.71f,.60f,.625f,.025f,.058f,EDGE);box(-.71f,.71f,-.625f,-.60f,.025f,.058f,EDGE);
        box(.683f,.715f,-.60f,.60f,.024f,.058f,EDGE);box(-.711f,-.688f,-.60f,.60f,.024f,.058f,EDGE);
        face(-.687f,.683f,-.60f,.60f,.056f,false,"",new float[]{.83f,.92f,.97f,.065f});
        for(float x:new float[]{-.38f,.43f})for(float y:new float[]{-.593f,.575f})box(x-.035f,x+.035f,y,y+.018f,.022f,.038f,EDGE);
        for(float y:new float[]{-.57f,.51f})box(-.716f,-.66f,y+.01f,y+.05f,.033f,.052f,EDGE);
        // Latch tabs on the opening edge.
        for(float y:new float[]{-.39f,.35f})box(.674f,.722f,y,y+.035f,.01f,.044f,EDGE);
        disc(data);obi(data);wrapping(data);
    }
    private void detailedTray(float[] opaque,boolean clear){
        float[] body=clear?new float[]{.72f,.83f,.88f,.16f}:opaque;
        float[] rim=clear?new float[]{.77f,.87f,.92f,.40f}:new float[]{opaque[0]*1.13f,opaque[1]*1.13f,opaque[2]*1.13f,1};
        // Recess floor, not a solid slab: the centre aperture remains open.
        ring(.06f,0,.027f,.605f,-.027f,false,"",body,128);
        wall(.06f,0,.027f,-.033f,-.023f,true,rim,64);
        // Four raised corner fields outside the circular well. Top/bottom finger
        // access notches interrupt the rim, so the edge of the CD can be grasped.
        int start=meshes.size();
        for(int i=0;i<128;i++){
            double a=i*Math.PI*2/128,b=(i+1)*Math.PI*2/128;
            float mid=(float)((a+b)/2);if(Math.abs(Math.cos(mid))<.14f)continue;
            float ca=(float)Math.cos(a),sa=(float)Math.sin(a),cb=(float)Math.cos(b),sb=(float)Math.sin(b);
            float ra=Math.min(.612f/Math.max(.0001f,Math.abs(sa)),(ca<0?.608f:.631f)/Math.max(.0001f,Math.abs(ca)));
            float rb=Math.min(.612f/Math.max(.0001f,Math.abs(sb)),(cb<0?.608f:.631f)/Math.max(.0001f,Math.abs(cb)));
            quad(new float[]{.06f+ca*.607f,sa*.607f,.009f,.06f+ca*ra,sa*ra,.009f,.06f+cb*rb,sb*rb,.009f,.06f+cb*.607f,sb*.607f,.009f},"",body);
        }
        batch(start);
        // Bevel profile catches highlights instead of a thin, flat circle.
        for(int half=0;half<2;half++){
            float a=(float)(-.5*Math.PI+.15+half*Math.PI),b=(float)(.5*Math.PI-.15+half*Math.PI);
            profile(new float[]{.597f,-.027f,.601f,-.023f,.604f,.006f,.607f,.009f},a,b,64,rim);
        }
        // Disc rests only on the clear inner stacking area, not its recorded face.
        profile(new float[]{.142f,-.026f,.150f,-.004f,.162f,-.004f,.170f,-.026f},0,(float)(Math.PI*2),96,rim);
        // Twelve separate cantilever fingers with slots, tapered shoulders and
        // a small retaining lip at the 15 mm centre hole (not square pegs).
        for(int i=0;i<12;i++){
            float a=(float)(i*Math.PI/6+.045),b=(float)((i+1)*Math.PI/6-.045);
            float[] section={.029f,-.026f,.034f,.010f,.047f,.023f,.067f,.023f,.077f,.014f,.070f,.009f,.079f,-.022f,.108f,-.026f};
            profile(section,a,b,5,rim);
            int caps=meshes.size();
            for(float angle:new float[]{a,b})for(int j=0;j<section.length-2;j+=2){
                float c=(float)Math.cos(angle),s=(float)Math.sin(angle),r0=section[j],r1=section[j+2];
                quad(new float[]{.06f+c*r0,s*r0,-.030f,.06f+c*r1,s*r1,-.030f,.06f+c*r1,s*r1,section[j+3],.06f+c*r0,s*r0,section[j+1]},"",body);
            }
            batch(caps);
        }
        // Narrow perimeter rails and fixing tabs; the hinge strip belongs to tray.
        box(-.704f,-.550f,-.612f,.612f,-.032f,.025f,body);
        // Clear trays use a smooth hinge-side skin, matching the desktop variant.
        // Opaque moulding has subtle same-material ribs, not bright ladder lines.
        if(!clear){int ribs=meshes.size();
            for(int i=0;i<39;i++){float y=-.59f+i*.031f;box(-.692f,-.566f,y,y+.003f,.025f,.027f,body);}batch(ribs);
        }
        for(float y:new float[]{-.612f,.599f})box(-.548f,.69f,y,y+.010f,-.025f,.012f,rim);
        box(.682f,.695f,-.60f,.60f,-.026f,.010f,rim);
        for(float y:new float[]{-.46f,.43f})box(.676f,.704f,y,y+.032f,-.015f,.016f,rim);
    }
    /** Revolved mould cross-section; radial/Z normals are calculated per bevel. */
    private void profile(float[] rz,float a,float b,int steps,float[] color){
        int start=meshes.size();
        for(int i=0;i<steps;i++){float u=a+(b-a)*i/steps,v=a+(b-a)*(i+1)/steps;
            float cu=(float)Math.cos(u),su=(float)Math.sin(u),cv=(float)Math.cos(v),sv=(float)Math.sin(v);
            for(int j=0;j<rz.length-2;j+=2){float r=rz[j],z=rz[j+1],r2=rz[j+2],z2=rz[j+3];
                quad(new float[]{.06f+cu*r,su*r,z,.06f+cu*r2,su*r2,z2,.06f+cv*r2,sv*r2,z2,.06f+cv*r,sv*r,z},"",color);
            }
        }batch(start);
    }
    /** Keep transparent mould sections bounded in draw calls as well as vertices. */
    private void batch(int start){if(meshes.size()==start)return;int size=0;for(int i=start;i<meshes.size();i++)size+=meshes.get(i).count*8;
        float[] values=new float[size];int at=0;Mesh first=meshes.get(start);
        for(int i=start;i<meshes.size();i++){Mesh m=meshes.get(i);FloatBuffer src=m.vertices.duplicate();src.position(0);src.get(values,at,m.count*8);at+=m.count*8;}
        meshes.subList(start,meshes.size()).clear();meshes.add(new Mesh(values,first.texture,first.color,first.part,first.finish));
    }
    private void disc(CasePackage data){part=DISC;finish=0;
        ring(.06f,0,.075f,.60f,.012f,false,"disc",data.images.containsKey("disc")?WHITE:SILVER,128);
        finish=2;ring(.06f,0,.17f,.60f,0,true,"",SILVER,128);
        // Physical 1.2 mm rim and center bore, plus raised stacking and clear hub rings.
        wall(.06f,0,.60f,0,.012f,false,SILVER,128);wall(.06f,0,.075f,0,.012f,true,EDGE,96);
        finish=1;ring(.06f,0,.075f,.17f,-.0005f,true,"",new float[]{.78f,.86f,.88f,.38f},96);
        ring(.06f,0,.15f,.158f,-.002f,true,"",EDGE,96);wall(.06f,0,.158f,-.002f,0,false,EDGE,96);
        ring(.06f,0,.075f,.081f,.0125f,false,"",EDGE,96);
    }
    private void obi(CasePackage data){if(!data.hasObi)return;part=OBI;finish=0;
        float x=-.735f,z=.073f,back=-.064f;
        face(x,x+data.obiFrontWidth,-.60f,.60f,z,false,"obiFront",WHITE);
        face(x,x+data.obiFrontWidth,-.60f,.60f,z-.002f,true,"",PAPER);
        face(x,x+data.obiBackWidth,-.60f,.60f,back,true,"obiBack",WHITE);
        face(x,x+data.obiBackWidth,-.60f,.60f,back+.002f,false,"",PAPER);
        quad(new float[]{x,-.60f,back,x,-.60f,z,x,.60f,z,x,.60f,back},"obiSpine",WHITE);
        quad(new float[]{x+.002f,-.60f,z,x+.002f,-.60f,back,x+.002f,.60f,back,x+.002f,.60f,z},"",PAPER);
        // Fold edges are paper, not a transparent seam.
        box(x+.001f,x+.003f,-.60f,.60f,back+.002f,z-.002f,PAPER);
    }
    private void wrapping(CasePackage data){finish=3;
        float[] film={.96f,.98f,1,.018f},seam={.95f,.97f,1,.085f};
        float l=data.hasObi?-.738f:-.727f,r=.726f,z0=data.hasObi?-.067f:-.054f,z1=data.hasObi?.076f:.061f;
        // Zero-thickness film, close to the case. No horizontal box caps at the
        // tear line: those caps previously looked like a thick acrylic shelf.
        part=FILM_TOP;filmSides(l,r,-.514f,.628f,z0,z1,film);
        quad(new float[]{l,.628f,z1,r,.628f,z1,r,.628f,z0,l,.628f,z0},"",film);
        part=FILM_BOTTOM;filmSides(l,r,-.628f,-.514f,z0,z1,film);
        quad(new float[]{l,-.628f,z0,r,-.628f,z0,r,-.628f,z1,l,-.628f,z1},"",film);
        for(int s:new int[]{-1,1}){part=s==1?FILM_TOP:FILM_BOTTOM;float y=s*.6285f,mid=(z0+z1)/2,fold=(z1-z0)*.5f;
            ribbon(new float[]{l,y,z0},new float[]{l+fold,y,mid},.00045f,seam);
            ribbon(new float[]{l+fold,y,mid},new float[]{l,y,z1},.00045f,seam);
            ribbon(new float[]{r,y,z0},new float[]{r-fold,y,mid},.00045f,seam);
            ribbon(new float[]{r-fold,y,mid},new float[]{r,y,z1},.00045f,seam);
            ribbon(new float[]{l+fold,y,mid},new float[]{r-fold,y,mid},.0006f,seam);
        }
        part=FILM_TOP;face(.510f,.513f,-.513f,.626f,z0-.0006f,true,"",seam);
        part=TAPE;filmSides(l-.0006f,r+.0006f,-.516f,-.512f,z0-.0006f,z1+.0006f,new float[]{.97f,.97f,.94f,.10f});
        face(r,r+.018f,-.518f,-.509f,z1+.0008f,false,"",new float[]{.97f,.97f,.93f,.18f});
    }
    private void filmSides(float l,float r,float b,float t,float z0,float z1,float[] color){
        face(l,r,b,t,z1,false,"",color);face(l,r,b,t,z0,true,"",color);
        quad(new float[]{l,b,z0,l,b,z1,l,t,z1,l,t,z0},"",color);
        quad(new float[]{r,b,z1,r,b,z0,r,t,z0,r,t,z1},"",color);
    }
    private void ribbon(float[] a,float[] b,float width,float[] color){float dx=b[0]-a[0],dz=b[2]-a[2],len=(float)Math.sqrt(dx*dx+dz*dz),ox=-dz/len*width,oz=dx/len*width;float[] p={a[0]+ox,a[1],a[2]+oz,b[0]+ox,b[1],b[2]+oz,b[0]-ox,b[1],b[2]-oz,a[0]-ox,a[1],a[2]-oz};quad(p,"",color);quad(new float[]{p[9],p[10],p[11],p[6],p[7],p[8],p[3],p[4],p[5],p[0],p[1],p[2]},"",color);}
    private void ring(float cx,float cy,float inner,float outer,float z,boolean reverse,String role,float[] color,int segments){
        float[] data=new float[segments*48];int offset=0;
        for(int i=0;i<segments;i++){float a=(float)(i*Math.PI*2/segments),b=(float)((i+1)*Math.PI*2/segments);float[] angles=reverse?new float[]{b,b,a,b,a,a}:new float[]{a,a,b,a,b,b};float[] radii={inner,outer,outer,inner,outer,inner};
            for(int j=0;j<6;j++){float x=(float)Math.cos(angles[j])*radii[j],y=(float)Math.sin(angles[j])*radii[j];data[offset++]=cx+x;data[offset++]=cy+y;data[offset++]=z;data[offset++]=.5f+x/1.2f;data[offset++]=.5f-y/1.2f;data[offset++]=0;data[offset++]=0;data[offset++]=reverse?-1:1;}
        }meshes.add(new Mesh(data,role,color,part,finish));
    }
    private void wall(float cx,float cy,float radius,float bottom,float top,boolean inward,float[] color,int segments){
        float[] values=new float[segments*48];int out=0;int[] order=inward?new int[]{3,2,1,3,1,0}:new int[]{0,1,2,0,2,3};
        for(int i=0;i<segments;i++){float a=(float)(i*Math.PI*2/segments),b=(float)((i+1)*Math.PI*2/segments);float[] angles={a,b,b,a},zs={bottom,bottom,top,top};for(int v:order){float c=(float)Math.cos(angles[v]),s=(float)Math.sin(angles[v]),sign=inward?-1:1;values[out++]=cx+c*radius;values[out++]=cy+s*radius;values[out++]=zs[v];values[out++]=0;values[out++]=0;values[out++]=c*sign;values[out++]=s*sign;values[out++]=0;}}
        meshes.add(new Mesh(values,"",color,part,finish));
    }
    private void face(float l,float r,float b,float t,float z,boolean reverse,String role,float[] color){quad(reverse?new float[]{r,b,z,l,b,z,l,t,z,r,t,z}:new float[]{l,b,z,r,b,z,r,t,z,l,t,z},role,color);}
    private void box(float l,float r,float b,float t,float z0,float z1,float[] color){face(l,r,b,t,z1,false,"",color);face(l,r,b,t,z0,true,"",color);quad(new float[]{l,b,z0,l,b,z1,l,t,z1,l,t,z0},"",color);quad(new float[]{r,b,z1,r,b,z0,r,t,z0,r,t,z1},"",color);quad(new float[]{l,t,z1,r,t,z1,r,t,z0,l,t,z0},"",color);quad(new float[]{l,b,z0,r,b,z0,r,b,z1,l,b,z1},"",color);}
    private void quad(float[] p,String role,float[] color){float ax=p[3]-p[0],ay=p[4]-p[1],az=p[5]-p[2],bx=p[6]-p[0],by=p[7]-p[1],bz=p[8]-p[2];float nx=ay*bz-az*by,ny=az*bx-ax*bz,nz=ax*by-ay*bx,len=(float)Math.sqrt(nx*nx+ny*ny+nz*nz);int[] order={0,1,2,0,2,3};float[] coords={0,1,1,1,1,0,0,0},values=new float[48];int out=0;for(int v:order){values[out++]=p[v*3];values[out++]=p[v*3+1];values[out++]=p[v*3+2];values[out++]=coords[v*2];values[out++]=coords[v*2+1];values[out++]=nx/len;values[out++]=ny/len;values[out++]=nz/len;}meshes.add(new Mesh(values,role,color,part,finish));}
}
