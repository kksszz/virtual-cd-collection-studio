package jp.virtualcd.player.case3d;

import android.graphics.*;
import org.json.*;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Bounded reader for the documented jewel-case GLB profile, not an arbitrary glTF engine.
 * All geometry and textures come from standard glTF data. No URI/network resolution. */
final class GlbCaseReader {
    private final JSONObject root;
    private final byte[] bin;
    private int vertices;
    private GlbCaseReader(byte[] bytes)throws Exception {
        if(bytes.length<28||bytes.length>CasePackage.MAX_BYTES)throw invalid(jp.virtualcd.player.LanguageStrings.text("サイズ","size"));
        ByteBuffer b=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        if(b.getInt()!=0x46546c67||b.getInt()!=2||b.getInt()!=bytes.length)throw invalid(jp.virtualcd.player.LanguageStrings.text("ヘッダー","header"));
        int json=b.getInt();if(json<2||json>2*1024*1024||json%4!=0||json>b.remaining()-12||b.getInt()!=0x4e4f534a)throw invalid(jp.virtualcd.player.LanguageStrings.text("JSONチャンク","JSON chunk"));
        byte[] text=new byte[json];b.get(text);root=new JSONObject(new String(text,StandardCharsets.UTF_8));
        int length=b.getInt();if(b.getInt()!=0x004e4942||length!=b.remaining()||length%4!=0)throw invalid(jp.virtualcd.player.LanguageStrings.text("BINチャンク","BIN chunk"));bin=new byte[length];b.get(bin);
        if(!"2.0".equals(root.getJSONObject("asset").getString("version"))||root.optJSONArray("extensionsRequired")!=null&&root.getJSONArray("extensionsRequired").length()>0)throw invalid(jp.virtualcd.player.LanguageStrings.text("未対応の拡張","unsupported extension"));
        JSONArray buffers=root.getJSONArray("buffers");if(buffers.length()!=1||buffers.getJSONObject(0).has("uri"))throw invalid(jp.virtualcd.player.LanguageStrings.text("外部バッファ","external buffer"));
        int declared=buffers.getJSONObject(0).getInt("byteLength");if(declared<0||declared>bin.length||bin.length-declared>3)throw invalid(jp.virtualcd.player.LanguageStrings.text("バッファ長","buffer length"));
    }
    static CasePackage read(byte[] bytes)throws Exception {return new GlbCaseReader(bytes).decode();}
    static byte[] frontImage(byte[] bytes)throws Exception {
        var reader=new GlbCaseReader(bytes);var images=reader.root.optJSONArray("images");if(images==null)return null;
        for(int i=0;i<images.length();i++){var image=images.getJSONObject(i);if(!"front".equals(image.optString("name")))continue;
            if(image.has("uri")||!"image/png".equals(image.optString("mimeType")))throw invalid(jp.virtualcd.player.LanguageStrings.text("表紙画像","cover image"));
            return reader.view(image.getInt("bufferView"),5*1024*1024);
        }return null;
    }
    private CasePackage decode()throws Exception {
        JSONObject metadata=root.getJSONObject("extras").getJSONObject("virtualCd");
        if(!Arrays.asList("jewel-case-glb-1","jewel-case-glb-2").contains(metadata.optString("profile")))throw invalid(jp.virtualcd.player.LanguageStrings.text("未対応のケースプロファイル","unsupported case profile"));
        if(!Arrays.asList("Black","White","Gray","Clear").contains(metadata.optString("tray")))throw invalid(jp.virtualcd.player.LanguageStrings.text("トレイ","tray"));
        CasePackage data=new CasePackage(metadata);
        try{
            JSONArray images=root.optJSONArray("images");List<String> roles=new ArrayList<>();
            if(images!=null){if(images.length()>(data.desktopGeometry?32:CasePackage.ROLES.size()))throw invalid(jp.virtualcd.player.LanguageStrings.text("画像数","image count"));
                for(int i=0;i<images.length();i++){JSONObject image=images.getJSONObject(i);String role=image.getString("name");
                    if((!CasePackage.ROLES.contains(role)&&!(data.desktopGeometry&&role.matches("detail[0-9]{1,2}")))||data.images.containsKey(role)||image.has("uri")||!"image/png".equals(image.getString("mimeType")))throw invalid(jp.virtualcd.player.LanguageStrings.text("画像用途","image role"));
                    byte[] png=view(image.getInt("bufferView"),5*1024*1024);BitmapFactory.Options options=new BitmapFactory.Options();options.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(png,0,png.length,options);
                    if(options.outWidth<1||options.outHeight<1||options.outWidth>1024||options.outHeight>1024||!"image/png".equals(options.outMimeType))throw invalid(jp.virtualcd.player.LanguageStrings.text("画像解像度","image resolution"));
                    options.inJustDecodeBounds=false;options.inScaled=false;Bitmap bitmap=BitmapFactory.decodeByteArray(png,0,png.length,options);if(bitmap==null)throw invalid(jp.virtualcd.player.LanguageStrings.text("画像デコード","image decoding"));data.images.put(role,bitmap);roles.add(role);
                }
            }
            for(String role:Arrays.asList("obiFront","obiSpine","obiBack"))if(data.hasObi!=data.images.containsKey(role))throw invalid(jp.virtualcd.player.LanguageStrings.text("帯画像","obi image"));
            JSONArray nodes=root.getJSONArray("nodes"),meshes=root.getJSONArray("meshes"),materials=root.getJSONArray("materials");
            if(nodes.length()>4096||meshes.length()>2048||materials.length()>2048)throw invalid(jp.virtualcd.player.LanguageStrings.text("モデル数","model count"));
            JSONObject scene=root.getJSONArray("scenes").getJSONObject(root.optInt("scene",0));JSONArray roots=scene.getJSONArray("nodes");
            if(roots.length()!=1||roots.getInt(0)!=0)throw invalid(jp.virtualcd.player.LanguageStrings.text("シーン","scene"));
            JSONObject sceneRoot=nodes.getJSONObject(0);JSONArray scale=sceneRoot.getJSONArray("scale");
            if(scale.length()!=3)throw invalid(jp.virtualcd.player.LanguageStrings.text("単位","units"));for(int i=0;i<3;i++)if(Math.abs(scale.getDouble(i)-.1)>1e-6)throw invalid(jp.virtualcd.player.LanguageStrings.text("単位","units"));
            if(sceneRoot.has("matrix")||sceneRoot.has("rotation")||sceneRoot.has("translation"))throw invalid(jp.virtualcd.player.LanguageStrings.text("ルート変換","root transform"));
            JSONArray parts=sceneRoot.getJSONArray("children");if(parts.length()!=7)throw invalid(jp.virtualcd.player.LanguageStrings.text("部品数","part count"));
            Set<Integer> used=new HashSet<>();List<CaseGeometry.Mesh> geometry=new ArrayList<>();
            for(int p=0;p<7;p++){
                if(parts.getInt(p)!=p+1)throw invalid(jp.virtualcd.player.LanguageStrings.text("部品順","part order"));JSONObject node=nodes.getJSONObject(p+1);
                if(node.getJSONObject("extras").getInt("virtualCdPart")!=p||node.has("matrix")||node.has("rotation")||node.has("translation")||node.has("mesh"))throw invalid(jp.virtualcd.player.LanguageStrings.text("部品定義","part definition"));
                JSONArray initial=node.getJSONArray("scale");float expected=p>=4&&!data.wrapped?0:1;
                if(initial.length()!=3)throw invalid(jp.virtualcd.player.LanguageStrings.text("初期状態","initial state"));for(int k=0;k<3;k++)if(initial.getDouble(k)!=expected)throw invalid(jp.virtualcd.player.LanguageStrings.text("初期状態","initial state"));
                JSONArray children=node.optJSONArray("children");if(children==null)continue;
                for(int j=0;j<children.length();j++){
                    int n=children.getInt(j);if(n<8||!used.add(n))throw invalid(jp.virtualcd.player.LanguageStrings.text("ノード重複","duplicate node"));JSONObject child=nodes.getJSONObject(n);
                    for(String field:Arrays.asList("matrix","translation","rotation","scale","children","skin","weights"))if(child.has(field))throw invalid(jp.virtualcd.player.LanguageStrings.text("未対応のメッシュ変換","unsupported mesh transform"));
                    JSONObject mesh=meshes.getJSONObject(child.getInt("mesh"));JSONArray primitives=mesh.getJSONArray("primitives");
                    for(int k=0;k<primitives.length();k++)geometry.add(primitive(primitives.getJSONObject(k),materials,roles,p));
                }
            }
            if(geometry.isEmpty())throw invalid(jp.virtualcd.player.LanguageStrings.text("空モデル","empty model"));data.geometry=geometry;return data;
        }catch(Exception error){data.close();throw error;}
    }
    private CaseGeometry.Mesh primitive(JSONObject primitive,JSONArray materials,List<String> roles,int part)throws Exception {
        if(primitive.optInt("mode",4)!=4||primitive.has("indices")||primitive.has("targets")||primitive.has("extensions"))throw invalid(jp.virtualcd.player.LanguageStrings.text("未対応のプリミティブ","unsupported primitive"));
        JSONObject attributes=primitive.getJSONObject("attributes");float[] positions=accessor(attributes.getInt("POSITION"),3),normals=accessor(attributes.getInt("NORMAL"),3),uv=accessor(attributes.getInt("TEXCOORD_0"),2);
        int count=positions.length/3;vertices+=count;if(count%3!=0||vertices>600000||normals.length!=positions.length||uv.length!=count*2)throw invalid(jp.virtualcd.player.LanguageStrings.text("頂点数","vertex count"));
        float[] interleaved=new float[count*8];for(int i=0;i<count;i++){System.arraycopy(positions,i*3,interleaved,i*8,3);System.arraycopy(uv,i*2,interleaved,i*8+3,2);System.arraycopy(normals,i*3,interleaved,i*8+5,3);}
        JSONObject material=materials.getJSONObject(primitive.getInt("material")),pbr=material.getJSONObject("pbrMetallicRoughness");JSONArray factor=pbr.getJSONArray("baseColorFactor");if(factor.length()!=4)throw invalid(jp.virtualcd.player.LanguageStrings.text("色","color"));
        float[] color=new float[4];for(int i=0;i<4;i++){color[i]=(float)factor.getDouble(i);if(!Float.isFinite(color[i])||color[i]<0||color[i]>1)throw invalid(jp.virtualcd.player.LanguageStrings.text("色","color"));}
        String role="";if(pbr.has("baseColorTexture")){JSONObject texture=root.getJSONArray("textures").getJSONObject(pbr.getJSONObject("baseColorTexture").getInt("index"));role=roles.get(texture.getInt("source"));}
        int finish=material.optJSONObject("extras")==null?0:material.getJSONObject("extras").optInt("virtualCdFinish",0);if(finish<0||finish>3)throw invalid(jp.virtualcd.player.LanguageStrings.text("材質","material"));
        return new CaseGeometry.Mesh(interleaved,role,color,part,finish);
    }
    private float[] accessor(int index,int width)throws Exception {
        JSONObject a=root.getJSONArray("accessors").getJSONObject(index);int count=a.getInt("count");
        if(a.getInt("componentType")!=5126||!a.getString("type").equals("VEC"+width)||count<1||count>600000||a.has("sparse")||a.optBoolean("normalized",false))throw invalid(jp.virtualcd.player.LanguageStrings.text("アクセサー","accessor"));
        JSONObject v=root.getJSONArray("bufferViews").getJSONObject(a.getInt("bufferView"));byte[] bytes=view(a.getInt("bufferView"),24*1024*1024);
        int offset=a.optInt("byteOffset",0),stride=v.optInt("byteStride",width*4);if(offset<0||offset%4!=0||stride<width*4||stride>252||stride%4!=0||(long)offset+(long)(count-1)*stride+width*4>bytes.length)throw invalid(jp.virtualcd.player.LanguageStrings.text("頂点範囲","vertex range"));
        ByteBuffer input=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);float[] values=new float[count*width];
        for(int i=0;i<count;i++)for(int j=0;j<width;j++){float value=input.getFloat(offset+i*stride+j*4);if(!Float.isFinite(value)||Math.abs(value)>100)throw invalid(jp.virtualcd.player.LanguageStrings.text("頂点値","vertex value"));values[i*width+j]=value;}return values;
    }
    private byte[] view(int index,int limit)throws Exception {JSONObject v=root.getJSONArray("bufferViews").getJSONObject(index);int offset=v.optInt("byteOffset",0),length=v.getInt("byteLength");if(v.getInt("buffer")!=0||offset<0||length<1||length>limit||(long)offset+length>bin.length)throw invalid(jp.virtualcd.player.LanguageStrings.text("バッファ範囲","buffer range"));return Arrays.copyOfRange(bin,offset,offset+length);}
    private static IOException invalid(String what){return new IOException(jp.virtualcd.player.LanguageStrings.text("GLBの","GLB ")+what+jp.virtualcd.player.LanguageStrings.text("が不正、または未対応です"," is invalid or unsupported"));}
}
