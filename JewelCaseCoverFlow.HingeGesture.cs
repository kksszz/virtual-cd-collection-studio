using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace ZipMp3Player;
public partial class JewelCaseCoverFlow
{
    private readonly Dictionary<int,Point> _hingeTouches=new();
    private readonly DiscPullGesture _discPull=new();
    private bool _hingeMode,_hingeDone,_shiftHinge;
    private double _hingeSpan,_lastPinch;
    private Point _hingeCenter,_shiftStart;
    private bool HingeControl(object source){for(var e=source as DependencyObject;e!=null&&e!=this;e=e is Visual?VisualTreeHelper.GetParent(e):LogicalTreeHelper.GetParent(e))if(e is System.Windows.Controls.Primitives.ButtonBase)return true;return false;}
    private void InitializeHingeGestures(){
        PreviewTouchDown+=(_,e)=>{
            if(!_isFullScreen||HingeControl(e.OriginalSource))return;
            _hingeTouches[e.TouchDevice.Id]=e.GetTouchPoint(this).Position;
            if(_hingeTouches.Count==1)_discPull.Reset();
            if(_hingeTouches.Count>2&&_discPull.Captured){_discPull.Cancel();e.Handled=true;return;}
            if(_discPull.Captured){e.Handled=true;return;}
            if(_hingeTouches.Count==2&&_isCaseOpen&&!_isCaseTransitioning&&!_isDiscRemoved&&_dxScene is not null){
                var pair=_hingeTouches.ToArray();
                if(_discPull.Begin(pair[0].Key,pair[0].Value,_dxScene.SeatedDiscTouchRadius(TranslatePoint(pair[0].Value,_dxScene.Viewport)),pair[1].Key,pair[1].Value,_dxScene.SeatedDiscTouchRadius(TranslatePoint(pair[1].Value,_dxScene.Viewport)))){
                    EndPointerDrag();_hingeMode=false;e.Handled=true;return;
                }
            }
            if(_hingeTouches.Count==2){EndPointerDrag();var p=_hingeTouches.Values.ToArray();_hingeSpan=Math.Abs(p[1].X-p[0].X);_lastPinch=(p[1]-p[0]).Length;_hingeCenter=new Point((p[0].X+p[1].X)/2,(p[0].Y+p[1].Y)/2);_hingeMode=Math.Abs(Math.Cos(_caseYaw*Math.PI/180))<.55&&Math.Abs(_casePitch)<55;_hingeDone=false;e.Handled=true;}
        };
        PreviewTouchMove+=(_,e)=>{
            if(!_hingeTouches.ContainsKey(e.TouchDevice.Id))return;_hingeTouches[e.TouchDevice.Id]=e.GetTouchPoint(this).Position;
            if(_discPull.Captured){
                if(_hingeTouches.Count==2&&_isCaseOpen&&!_isCaseTransitioning&&!_isDiscRemoved){var pair=_hingeTouches.ToArray();if(_discPull.Move(pair[0].Key,pair[0].Value,pair[1].Key,pair[1].Value))SetDiscRemoved(true,true);}
                else _discPull.Cancel();
                e.Handled=true;return;
            }
            if(_hingeTouches.Count!=2)return;var p=_hingeTouches.Values.ToArray();double span=Math.Abs(p[1].X-p[0].X),distance=(p[1]-p[0]).Length;
            var center=new Point((p[0].X+p[1].X)/2,(p[0].Y+p[1].Y)/2);PanBy(center-_hingeCenter);_hingeCenter=center;
            if(_hingeMode){if(!_hingeDone&&Math.Abs(span-_hingeSpan)>48&&Math.Abs(p[1].Y-p[0].Y)<span){_hingeDone=true;_=SetCaseOpenAsync(span>_hingeSpan,true);}}
            else if(_lastPinch>1&&distance>1)AdjustZoom((int)(Math.Log(distance/_lastPinch)/Math.Log(1.12)*120));
            _lastPinch=distance;e.Handled=true;
        };
        PreviewTouchUp+=(_,e)=>{if(_hingeTouches.Remove(e.TouchDevice.Id)){if(_discPull.Captured){_discPull.Cancel();e.Handled=true;}else if(_hingeTouches.Count>0)e.Handled=true;if(_hingeTouches.Count==0)_discPull.Reset();}};
        LostTouchCapture+=(_,_)=>{_hingeTouches.Clear();_discPull.Reset();};
        PreviewMouseDown+=(_,e)=>{if(e.ChangedButton!=MouseButton.Left||(Keyboard.Modifiers&ModifierKeys.Shift)==0||HingeControl(e.OriginalSource))return;EndPointerDrag();_shiftStart=e.GetPosition(this);_shiftHinge=CaptureMouse();_hingeDone=false;e.Handled=true;};
        PreviewMouseMove+=(_,e)=>{if(!_shiftHinge)return;if(e.LeftButton!=MouseButtonState.Pressed){_shiftHinge=false;ReleaseMouseCapture();return;}double dx=e.GetPosition(this).X-_shiftStart.X;if(!_hingeDone&&Math.Abs(dx)>48){_hingeDone=true;_=SetCaseOpenAsync(dx>0,true);}e.Handled=true;};
        PreviewMouseUp+=(_,e)=>{if(_shiftHinge){_shiftHinge=false;ReleaseMouseCapture();e.Handled=true;}};
        Unloaded+=(_,_)=>{_hingeTouches.Clear();_discPull.Reset();_shiftHinge=false;};
    }
}
