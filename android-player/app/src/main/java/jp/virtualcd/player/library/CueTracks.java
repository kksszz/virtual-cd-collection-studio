package jp.virtualcd.player.library;

import android.content.Context;
import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;

/** Single-file FLAC/WAVE CUE sheets. Never follows paths outside the album. */
public final class CueTracks {
    public static final String START="cueStartMs",END="cueEndMs",URI="cueSourceUri";
    public static AlbumTracks read(Context c,AlbumLibrary.Album album,List<AlbumLibrary.Album> children)throws IOException{
        var sheets=new ArrayList<AlbumLibrary.Album>();for(var f:children)if(f.name.toLowerCase(Locale.ROOT).endsWith(".cue"))sheets.add(f);
        if(sheets.isEmpty())return null;
        if(sheets.size()!=1)throw new IOException("複数のCUEは未対応です。アルバムごとにフォルダーを分けてください");
        byte[] bytes;try(var in=c.getContentResolver().openInputStream(sheets.get(0).uri)){if(in==null)throw new IOException("CUEを開けません");bytes=jp.virtualcd.player.case3d.CasePackage.readBytes(in,1024*1024);}
        String text;try{text=StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();}catch(CharacterCodingException ex){text=new String(bytes,Charset.forName("windows-31j"));}
        var result=parse(text);String filename=result.tracks.get(0).file;
        AlbumLibrary.Album audio=null;for(var f:children)if(f.name.equals(filename)&&!f.directory)audio=f;
        if(audio==null)throw new IOException("CUEの参照FLACが見つかりません: "+filename);
        long duration;try(var fd=c.getContentResolver().openFileDescriptor(audio.uri,"r");var tags=new android.media.MediaMetadataRetriever()){
            if(fd==null)throw new IOException("FLACを開けません");tags.setDataSource(fd.getFileDescriptor());duration=Long.parseLong(tags.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_DURATION));
        }catch(Exception ex){throw new IOException("FLACの長さを確認できません",ex);}
        for(int i=0;i<result.tracks.size();i++){var t=result.tracks.get(i);long start=Long.parseLong(t.properties.getString(START));long end=i+1<result.tracks.size()?Long.parseLong(result.tracks.get(i+1).properties.getString(START)):duration;
            if(end<=start||end>duration)throw new IOException("CUEの曲境界がFLACの範囲外です");
            t.uri=audio.uri;t.properties.putString(URI,audio.uri.toString());t.properties.putString(END,Long.toString(end));t.properties.putString(TrackProperties.DURATION,Long.toString(end-start));t.properties.putString(TrackProperties.FORMAT,"FLAC / CUE");t.properties.putString(TrackProperties.FILE,filename+" · Track "+t.number);
        }return result;
    }
    public static AlbumTracks parse(String text)throws IOException{
        var result=new AlbumTracks();result.title="アルバム";String file=null;AlbumTracks.Track current=null;long previous=-1;
        for(String raw:text.replace("\uFEFF","").split("\n")){String line=raw.trim();
            if(line.matches("(?i)^(PREGAP|POSTGAP|FLAGS).*"))throw new IOException("追加ギャップ・FLAGS指定は未対応です");
            Matcher m=Pattern.compile("(?i)^FILE\\s+\"([^\"]+)\"\\s+WAVE$").matcher(line);
            if(line.toUpperCase(Locale.ROOT).startsWith("FILE ")){if(file!=null||!m.matches())throw new IOException("1つのFLACを参照するCUEのみ対応しています");file=m.group(1);if(!file.toLowerCase(Locale.ROOT).endsWith(".flac")||file.contains("/")||file.contains("\\")||file.contains(":"))throw new IOException("参照FLACは同じフォルダーに置いてください");continue;}
            m=Pattern.compile("(?i)^TRACK\\s+(\\d+)\\s+AUDIO$").matcher(line);
            if(m.matches()){int number=Integer.parseInt(m.group(1));if(file==null||number!=result.tracks.size()+1||number>99)throw new IOException("不正な曲番号です");current=new AlbumTracks.Track();current.file=file;current.number=number;current.title=String.format(Locale.ROOT,"Track %02d",number);current.artist=result.artist;current.album=result.title;result.tracks.add(current);continue;}
            m=Pattern.compile("(?i)^INDEX\\s+0?1\\s+(\\d+):(\\d+):(\\d+)$").matcher(line);
            if(m.matches()){int sec=Integer.parseInt(m.group(2)),frame=Integer.parseInt(m.group(3));long ms=(Long.parseLong(m.group(1))*4500+sec*75+frame)*1000/75;if(current==null||sec>=60||frame>=75||ms<=previous||current.properties.containsKey(START))throw new IOException("不正な曲境界です");current.properties.putString(START,Long.toString(ms));previous=ms;continue;}
            m=Pattern.compile("(?i)^(TITLE|PERFORMER)\\s+\"(.*)\"$").matcher(line);
            if(m.matches()){boolean title=m.group(1).equalsIgnoreCase("TITLE");if(current==null){if(title)result.title=m.group(2);else result.artist=m.group(2);}else if(title)current.title=m.group(2);else current.artist=m.group(2);}
        }
        if(result.tracks.isEmpty())throw new IOException("CUEに曲がありません");for(var t:result.tracks)if(!t.properties.containsKey(START))throw new IOException("INDEX 01がありません");return result;
    }
}
