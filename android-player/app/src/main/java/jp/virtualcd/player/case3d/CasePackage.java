package jp.virtualcd.player.case3d;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import org.json.JSONObject;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.zip.*;

/** Strict, bounded, non-extracting parser for the Windows artwork snapshot. */
public final class CasePackage implements AutoCloseable {
    public static final int MAX_BYTES=32*1024*1024;
    public static final List<String> ROLES=Arrays.asList("front","insideFront","back","spine","rightSpine","inlay","inlayLeft","inlayRight","disc","obiFront","obiSpine","obiBack","obiFrontInside","obiSpineInside","obiBackInside");
    public final String title,artist,tray;
    public final boolean hasObi,wrapped,desktopGeometry;
    public final float obiFrontWidth,obiBackWidth;
    public final Map<String,Bitmap> images=new HashMap<>();
    List<CaseGeometry.Mesh> geometry;
    CasePackage(JSONObject manifest)throws Exception {
        title=manifest.optString("title","");artist=manifest.optString("artist","");tray=manifest.optString("tray","Black");
        desktopGeometry="jewel-case-glb-2".equals(manifest.optString("profile"));
        JSONObject obi=manifest.optJSONObject("obi");hasObi=obi!=null;wrapped=manifest.optBoolean("wrapped",false);
        obiFrontWidth=hasObi?width(obi,"frontWidthMm"):0;obiBackWidth=hasObi?width(obi,"backWidthMm"):0;
    }
    private static float width(JSONObject obi,String key)throws Exception {double mm=obi.getDouble(key);if(!Double.isFinite(mm)||mm<1||mm>140)throw new IOException(jp.virtualcd.player.LanguageStrings.text("帯の寸法が不正です","Invalid obi dimensions"));return (float)(mm/100);}
    public static byte[] readBytes(InputStream input,int limit)throws IOException {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] buffer=new byte[8192];int n;
        while((n=input.read(buffer))!=-1){if(bytes.size()>limit-n)throw new IOException(jp.virtualcd.player.LanguageStrings.text("3Dデータがサイズ上限を超えています","3D data exceeds the size limit"));bytes.write(buffer,0,n);}return bytes.toByteArray();
    }
    public static CasePackage parse(byte[] data)throws Exception {
        if(data.length>MAX_BYTES)throw new IOException(jp.virtualcd.player.LanguageStrings.text("パッケージが大きすぎます","Package is too large"));
        if(data.length>=4&&data[0]=='g'&&data[1]=='l'&&data[2]=='T'&&data[3]=='F')return GlbCaseReader.read(data);
        Map<String,byte[]> entries=new HashMap<>();int total=0;
        try(ZipInputStream zip=new ZipInputStream(new ByteArrayInputStream(data))){ZipEntry entry;
            while((entry=zip.getNextEntry())!=null){String name=entry.getName();
                boolean allowed=name.equals("manifest.json")||ROLES.stream().anyMatch(r->name.equals(r+".png"));
                if(!allowed||entries.containsKey(name)||entry.isDirectory())throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応・重複した項目: ","Unsupported or duplicate entry: ")+name);
                byte[] bytes=readBytes(zip,name.equals("manifest.json")?16384:5*1024*1024);total+=bytes.length;
                if(total>MAX_BYTES)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像の合計サイズが大きすぎます","Total image size is too large"));entries.put(name,bytes);
            }
        }
        if(!entries.containsKey("manifest.json"))throw new IOException(jp.virtualcd.player.LanguageStrings.text("manifest.json がありません","manifest.json is missing"));
        JSONObject manifest=new JSONObject(new String(entries.get("manifest.json"),java.nio.charset.StandardCharsets.UTF_8));
        int version=manifest.optInt("version");
        if(!manifest.optString("format").equals("virtual-cd-case")||(version<1||version>3)||!manifest.optString("model").equals("jewel-case-v"+version))throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応の3D形式・バージョンです","Unsupported 3D format or version"));
        if(!Arrays.asList("Black","White","Gray","Clear").contains(manifest.optString("tray")))throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応のトレイです","Unsupported tray"));
        JSONObject textures=manifest.getJSONObject("textures");CasePackage result=new CasePackage(manifest);
        try{
            if(version==1&&(result.hasObi||result.wrapped))throw new IOException(jp.virtualcd.player.LanguageStrings.text("帯・包装にはv2データが必要です","Obi and wrapping require v2 data"));
            for(String role:Arrays.asList("obiFront","obiSpine","obiBack"))if(textures.has(role)!=result.hasObi)throw new IOException(jp.virtualcd.player.LanguageStrings.text("帯の画像・寸法が不足しています","Missing obi image or dimensions"));
            Iterator<String> keys=textures.keys();while(keys.hasNext())if(!ROLES.contains(keys.next()))throw new IOException(jp.virtualcd.player.LanguageStrings.text("未対応の画像用途です","Unsupported image role"));
            for(String role:ROLES){if(!textures.has(role)){if(entries.containsKey(role+".png"))throw new IOException(jp.virtualcd.player.LanguageStrings.text("未定義の画像です","Undefined image"));continue;}
                JSONObject texture=textures.getJSONObject(role);String name=role+".png";
                if(!name.equals(texture.getString("file")))throw new IOException(jp.virtualcd.player.LanguageStrings.text("不正な画像パスです","Invalid image path"));
                byte[] bytes=entries.get(name);if(bytes==null||!hash(bytes).equalsIgnoreCase(texture.getString("sha256")))throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像が破損しています: ","Corrupt image: ")+role);
                BitmapFactory.Options options=new BitmapFactory.Options();options.inJustDecodeBounds=true;BitmapFactory.decodeByteArray(bytes,0,bytes.length,options);
                if(options.outWidth<1||options.outHeight<1||options.outWidth>1024||options.outHeight>1024||!"image/png".equals(options.outMimeType))throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像形式・解像度が上限外です","Image format or resolution exceeds the limits"));
                options.inJustDecodeBounds=false;options.inScaled=false;Bitmap bitmap=BitmapFactory.decodeByteArray(bytes,0,bytes.length,options);
                if(bitmap==null)throw new IOException(jp.virtualcd.player.LanguageStrings.text("画像を読み込めません","Unable to load image"));result.images.put(role,bitmap);
            }
            return result;
        }catch(Exception ex){result.close();throw ex;}
    }
    public static byte[] frontImage(byte[] data)throws Exception {
        if(data.length>=4&&data[0]=='g'&&data[1]=='l'&&data[2]=='T'&&data[3]=='F')return GlbCaseReader.frontImage(data);
        try(var parsed=parse(data)){Bitmap front=parsed.images.get("front");if(front==null)return null;var out=new ByteArrayOutputStream();front.compress(Bitmap.CompressFormat.PNG,100,out);return out.toByteArray();}
    }
    public static String hash(byte[] bytes)throws Exception {byte[] digest=MessageDigest.getInstance("SHA-256").digest(bytes);StringBuilder s=new StringBuilder();for(byte b:digest)s.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return s.toString();}
    @Override public void close(){for(Bitmap image:images.values())image.recycle();images.clear();}
}
