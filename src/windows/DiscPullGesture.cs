using System.Windows;
namespace ZipMp3Player;

internal sealed class DiscPullGesture
{
    private int hub,edge;
    private Point hubStart,edgeStart;
    private bool finished;
    public bool Captured{get;private set;}
    public bool Begin(int a,Point ap,double ar,int b,Point bp,double br){
        Reset();
        if(ar>=0&&ar<=.28&&br>=.70&&br<=1.15){hub=a;edge=b;hubStart=ap;edgeStart=bp;}
        else if(br>=0&&br<=.28&&ar>=.70&&ar<=1.15){hub=b;edge=a;hubStart=bp;edgeStart=ap;}
        else return false;
        return Captured=true;
    }
    public bool Move(int a,Point ap,int b,Point bp){
        if(!Captured||finished)return false;
        if(a==edge&&b==hub)return Move(b,bp,a,ap);
        if(a!=hub||b!=edge||(ap-hubStart).Length>20){Cancel();return false;}
        if((bp-edgeStart).Length<28)return false;
        finished=true;return true;
    }
    public void Cancel()=>finished=true;
    public void Reset(){Captured=false;finished=false;}
}
