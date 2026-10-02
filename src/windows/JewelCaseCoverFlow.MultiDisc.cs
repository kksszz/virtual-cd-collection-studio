using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZipMp3Player;

public sealed partial class JewelCaseCoverFlow
{
    private readonly Button _multiDiscButton=new() {
        Width=150,Height=25,
        HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
        Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(220,24,32,41)),
        Visibility=Visibility.Collapsed
    };
    private void UpdateMultiDiscButton()
    {
        bool multi=_dxScene?.IsMultiCase==true&&!_collectionPresentation;
        bool digipak=_dxScene?.IsDigipak==true&&!_collectionPresentation;
        _multiDiscButton.Visibility=multi||digipak?Visibility.Visible:Visibility.Collapsed;
        _multiDiscButton.Margin=digipak?new Thickness(134,0,0,0):new Thickness(0);
        _multiDiscButton.IsEnabled=(multi&&_dxScene!.CanOperateSelectedMultiDisc
            ||digipak&&_dxScene!.CanOperateSelectedDigipakDisc)&&_isCaseOpen&&!_isCaseTransitioning;
        var number=multi?_dxScene?.SelectedMultiDisc:_dxScene?.SelectedDigipakDisc;
        bool removed=multi?_dxScene?.SelectedMultiDiscRemoved==true:_dxScene?.SelectedDigipakDiscRemoved==true;
        _multiDiscButton.Content=number is null?"CDをクリックして選択":
            $"Disc{number}を"+(removed?"戻す":"取り出す");
        _multiDiscButton.ToolTip="CDをクリックして選択。取り出したCDはドラッグで移動、ダブルクリックで再生／停止。";
    }
}
