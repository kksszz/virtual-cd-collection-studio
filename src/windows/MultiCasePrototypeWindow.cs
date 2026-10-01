using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed class MultiCasePrototypeWindow : Window
{
    internal static BitmapSource DiscLabel(int number)
    {
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen()) {
            if(number is 1 or 3) drawing.PushTransform(new RotateTransform(180,256,256));
            var colors=new[]{Colors.SteelBlue,Colors.SeaGreen,Colors.DarkOrange,Colors.MediumPurple};
            drawing.DrawRectangle(new SolidColorBrush(colors[number-1]),null,new Rect(0,0,512,512));
            var text=new FormattedText("DISC "+number,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),48,Brushes.White,1);
            drawing.DrawText(text,new Point((512-text.Width)/2,90));
            if(number is 1 or 3) drawing.Pop();
        }
        var bitmap=new RenderTargetBitmap(512,512,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    private readonly DxJewelCaseScene scene = new();
    internal MultiCasePrototypeWindow(IReadOnlyList<MultiCaseImageChoice>? images=null,string? albumTitle=null)
    {
        images??=Array.Empty<MultiCaseImageChoice>();
        Title="24mmマルチケース — 画像マッピング試作"+(albumTitle is null?"":" — "+albumTitle);
        Width=1280; Height=860; MinWidth=1020; MinHeight=700;
        Background=new SolidColorBrush(Color.FromRgb(20,23,28)); Foreground=Brushes.White;
        var layout=new DockPanel { Margin=new Thickness(14) }; Content=layout;
        var controls=new StackPanel(); DockPanel.SetDock(controls,Dock.Bottom); layout.Children.Add(controls);
        controls.Children.Add(new TextBlock { Text="試作：実測した中央部品＋既存の前後トレー。アルバム設定・画像・転送データは変更しません。", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,8) });
        var state=new TextBlock { Text="閉じた状態", Margin=new Thickness(0,4,0,4) }; controls.Children.Add(state);
        var progress=new Slider { Minimum=0,Maximum=2,Value=0,TickFrequency=1,IsSnapToTickEnabled=false,Margin=new Thickness(0,5,0,10) };
        controls.Children.Add(progress);
        progress.ValueChanged+=(_,_)=> { scene.SetMultiCaseProgress(progress.Value); state.Text=progress.Value<.001?"閉じた状態":progress.Value<1?"① 前側のケースを開く":progress.Value<1.999?"② 中央トレーを反対側へめくる":"中央トレーの裏面と後ろ側のトレー"; };
        var presets=new WrapPanel(); controls.Children.Add(presets);
        foreach(var (label,value) in new[]{("閉じる",0d),("前側を開く",1d),("中央をめくる",2d)})
        {
            var button=new Button { Content=label, Padding=new Thickness(12,5,12,5), Margin=new Thickness(0,0,8,8) };
            button.Click+=(_,_)=>progress.Value=value; presets.Children.Add(button);
        }
        var discs=new CheckBox { Content="ディスクを表示", IsChecked=true, Margin=new Thickness(8,5,8,5), Foreground=Brushes.White };
        presets.Children.Add(discs); discs.Checked+=(_,_)=>scene.SetMultiCaseDiscsVisible(true); discs.Unchecked+=(_,_)=>scene.SetMultiCaseDiscsVisible(false);
        void RotationSlider(string name,double min,double max,double initial,Action<double> changed)
        {
            var row=new DockPanel(); controls.Children.Add(row);
            row.Children.Add(new TextBlock {Text=name,Width=75,VerticalAlignment=VerticalAlignment.Center});
            var slider=new Slider {Minimum=min,Maximum=max,Value=initial,Margin=new Thickness(0,5,0,5)};
            row.Children.Add(slider); slider.ValueChanged+=(_,_)=>changed(slider.Value);
        }
        double yaw=-22,pitch=-18,zoom=1;
        RotationSlider("左右回転",-180,180,yaw,value=> {yaw=value;scene.SetRotation(yaw,pitch);});
        RotationSlider("上下回転",-85,85,pitch,value=> {pitch=value;scene.SetRotation(yaw,pitch);});
        RotationSlider("拡大",.6,2.5,1,value=>{zoom=value;scene.SetViewZoom(value);});
        var mapping=new StackPanel {Margin=new Thickness(12,0,0,0)};
        var scroll=new ScrollViewer {Content=mapping,Width=325,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        DockPanel.SetDock(scroll,Dock.Right);layout.Children.Add(scroll);
        mapping.Children.Add(new TextBlock {Text="既存画像の割り当て（保存しません）",FontSize=16,Margin=new Thickness(0,0,0,8)});
        mapping.Children.Add(new TextBlock {Text="選択中アルバムの画像を使用します。変更後に「画像を適用」を押してください。未指定の背表紙はジャケットから切り出します。Disc未指定は番号表示です。",TextWrapping=TextWrapping.Wrap});
        var selectors=new Dictionary<string,ComboBox>();var turns=new Dictionary<string,int>();
        var modes=new Dictionary<string,ComboBox>();
        foreach(var (key,label,role) in new[]{("Front","表ジャケット","Front"),("Back","裏ジャケット","Back"),
            ("FrontLeft","前側・画像左の背表紙",""),("FrontRight","前側・画像右の背表紙",""),
            ("BackLeft","後側・画像左の背表紙","LeftSpine"),("BackRight","後側・画像右の背表紙","RightSpine"),
            ("Disc1","Disc 1（前側トレー）","Disc1"),("Disc2","Disc 2（中央表）","Disc2"),
            ("Disc3","Disc 3（中央裏）","Disc3"),("Disc4","Disc 4（後側トレー）","Disc4")}) {
            var heading=new DockPanel {Margin=new Thickness(0,10,0,3)};
            var rotate=new Button {Content="回転 0°",Padding=new Thickness(5,1,5,1)};
            DockPanel.SetDock(rotate,Dock.Right);heading.Children.Add(rotate);
            heading.Children.Add(new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center});mapping.Children.Add(heading);
            turns[key]=0;rotate.Click+=(_,_)=>{turns[key]=(turns[key]+90)%360;rotate.Content=$"回転 {turns[key]}°";};
            var select=new ComboBox {Foreground=Brushes.Black,MaxDropDownHeight=300};select.Items.Add("自動／未指定");
            foreach(var choice in images)select.Items.Add(choice.Name);
            int initial=images.ToList().FindIndex(choice=>role.Length>0&&choice.Role==role);
            select.SelectedIndex=initial+1;selectors[key]=select;mapping.Children.Add(select);
            if(key is "Front" or "Back") {
                var mode=new ComboBox {Foreground=Brushes.Black,Margin=new Thickness(0,3,0,0)};
                foreach(var name in new[]{"背表紙の切り出し：自動","パネル画像のみ（切り出さない）","背込み画像（6＋138＋6mm）"})mode.Items.Add(name);
                mode.SelectedIndex=0;modes[key]=mode;mapping.Children.Add(mode);
            }
        }
        var apply=new Button {Content="画像を適用",Margin=new Thickness(0,14,0,8),Padding=new Thickness(10,6,10,6)};
        mapping.Children.Add(apply);
        var result=new TextBlock {TextWrapping=TextWrapping.Wrap};mapping.Children.Add(result);
        void ApplyImages() {
            try {
                var loaded=new Dictionary<int,BitmapSource>();
                BitmapSource? Picture(string key) {
                    int index=selectors[key].SelectedIndex-1;if(index<0)return null;
                    if(!loaded.TryGetValue(index,out var image)) {
                        try {image=images[index].Load();loaded[index]=image;}
                        catch(Exception ex){throw new InvalidOperationException($"{images[index].Name}: {ex.Message}",ex);}
                    }
                    return MultiCaseArtwork.Rotate(image,turns[key]);
                }
                var front=MultiCaseArtwork.Split(Picture("Front"),modes["Front"].SelectedIndex);
                var back=MultiCaseArtwork.Split(Picture("Back"),modes["Back"].SelectedIndex);
                var artwork=new MultiCaseArtwork {Front=front.Panel,Back=back.Panel,
                    FrontLeft=Picture("FrontLeft")??MultiCaseArtwork.Rotate(front.Left,turns["FrontLeft"]),
                    FrontRight=Picture("FrontRight")??MultiCaseArtwork.Rotate(front.Right,turns["FrontRight"]),
                    BackLeft=Picture("BackLeft")??MultiCaseArtwork.Rotate(back.Left,turns["BackLeft"]),
                    BackRight=Picture("BackRight")??MultiCaseArtwork.Rotate(back.Right,turns["BackRight"]),
                    Disc1=Picture("Disc1"),Disc2=Picture("Disc2"),Disc3=Picture("Disc3"),Disc4=Picture("Disc4")};
                scene.BuildMultiCasePrototype(artwork);scene.SetMultiCaseProgress(progress.Value);
                scene.SetMultiCaseDiscsVisible(discs.IsChecked==true);scene.SetRotation(yaw,pitch);scene.SetViewZoom(zoom);
                result.Text=images.Count==0?"アルバム画像がありません。メイン画面でアルバムを選択し、この試作を開き直してください。":$"{loaded.Count}種類の元画像を適用しました。元画像・用途設定は変更していません。";
            } catch(Exception ex) {result.Text="適用できませんでした："+ex.Message;}
        }
        apply.Click+=(_,_)=>ApplyImages();
        layout.Children.Add(scene.Viewport);
        Loaded+=(_,_)=>ApplyImages();
        Closed+=(_,_)=>scene.Dispose();
    }
}

public partial class MainWindow
{
    private void MultiCasePrototype_Click(object sender,RoutedEventArgs e)
    {
        try {
            var choices=new List<MultiCaseImageChoice>();
            if(_album is {} album) {
                var roles=LoadArtworkRoles(album.Path);
                foreach(var source in GetCaseArtworkSources(album,GetDownloadedArtworkDirectory(album.Path))) {
                    var role=GetEffectiveArtworkRole(source,roles);
                    // Existing role inference predates Disc 4. Respect explicit
                    // assignments; recognize Disc4 filenames only in this preview.
                    if((!roles.TryGetValue(source.RoleKey,out var stored)||stored=="Auto")&&
                        System.Text.RegularExpressions.Regex.IsMatch(source.DisplayName,@"(?:disc|disk)[\s_-]*0?4(?:\D|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))role="Disc4";
                    choices.Add(new MultiCaseImageChoice(source.DisplayName,role,()=>LoadBitmap(source,2400)));
                }
            }
            new MultiCasePrototypeWindow(choices,_album?.Path) { Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner }.Show();
        } catch(Exception ex) {MessageBox.Show(this,ex.Message,"試作画像を準備できません",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
}
