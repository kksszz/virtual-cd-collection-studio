package jp.virtualcd.player.library;

import java.nio.*;
import java.nio.channels.FileChannel;
import java.nio.charset.*;

/** Read-only compatibility for Japanese ID3v2.3 titles written as CP932 under encoding byte 0.
 * Standard Unicode frames and ambiguous Western text are left to Android's decoder. */
public final class LegacyId3Title {
    public static String read(FileChannel channel,long offset,long length){
        try{
            if(length<10)return null;
            ByteBuffer header=readBytes(channel,offset,10);
            if(header.get(0)!='I'||header.get(1)!='D'||header.get(2)!='3'||header.get(3)!=3||header.get(5)!=0)return null;
            int size=0;for(int i=6;i<10;i++){int value=header.get(i)&255;if(value>127)return null;size=(size<<7)|value;}
            if(size>1024*1024||size>length-10)return null;
            ByteBuffer tag=readBytes(channel,offset+10,size);
            for(int p=0;p+10<=size;){
                int n=tag.getInt(p+4);if(n<=0||n>size-p-10)return null;
                if(tag.get(p)=='T'&&tag.get(p+1)=='I'&&tag.get(p+2)=='T'&&tag.get(p+3)=='2'){
                    if(tag.getShort(p+8)!=0||tag.get(p+10)!=0)return null;
                    int end=p+10+n,start=p+11;while(end>start&&tag.get(end-1)==0)end--;
                    byte[] bytes=new byte[end-start];for(int i=0;i<bytes.length;i++)bytes[i]=tag.get(start+i);
                    return decode(bytes);
                }
                p+=10+n;
            }
        }catch(Exception ignored){ /* Unsupported/malformed tags fall back to the platform. */ }
        return null;
    }
    public static String decode(byte[] bytes){
        try{
            // C1 bytes are control characters in declared Latin-1, but common CP932 lead bytes.
            boolean suspicious=false;for(byte b:bytes){int v=b&255;if(v>=0x80&&v<=0x9f)suspicious=true;}if(!suspicious)return null;
            String value=Charset.forName("windows-31j").newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString().trim();
            boolean japanese=false;
            for(int i=0;i<value.length();i++){
                char c=value.charAt(i);if(Character.isISOControl(c))return null;
                if((c>='\u3040'&&c<='\u30ff')||(c>='\u3400'&&c<='\u9fff'))japanese=true;
            }
            return japanese?value:null;
        }catch(CharacterCodingException e){return null;}
    }
    private static ByteBuffer readBytes(FileChannel channel,long offset,int length)throws java.io.IOException{
        var result=ByteBuffer.allocate(length);while(result.hasRemaining()){
            int n=channel.read(result,offset+result.position());if(n<=0)throw new java.io.EOFException();
        }return result;
    }
}
