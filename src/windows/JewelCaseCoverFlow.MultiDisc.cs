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
        _multiDiscButton.Visibility=multi?Visibility.Visible:Visibility.Collapsed;
        _multiDiscButton.IsEnabled=multi&&_isCaseOpen&&_dxScene!.CanOperateSelectedMultiDisc&&!_isCaseTransitioning;
        var number=_dxScene?.SelectedMultiDisc;
        _multiDiscButton.Content=number is null?"CDをクリックして選択":
            $"Disc{number}を"+(_dxScene!.SelectedMultiDiscRemoved?"戻す":"取り出す");
        _multiDiscButton.ToolTip="CDをクリックして選択。取り出したCDはドラッグで移動、ダブルクリックで再生／停止。";
    }
}
