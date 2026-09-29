using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZipMp3Player;

internal sealed partial class CdImportWindow
{
    private void BuildImportForm()
    {
        foreach(var input in new Control[]{drives,candidates,album,artist,year,discNumber,discCount,format,bitrate,destination})
        {
            input.Height=30;input.VerticalContentAlignment=VerticalAlignment.Center;
            input.HorizontalContentAlignment=HorizontalAlignment.Left;
        }
        album.Width=artist.Width=candidates.Width=destination.Width=double.NaN;
        destination.MinWidth=0;drives.Width=format.Width=120;year.Width=80;
        bitrateUnused.Height=30;

        var driveActions=new StackPanel{Orientation=Orientation.Horizontal};
        driveActions.Children.Add(drives);AddButton(driveActions,"CDを読み込む",ReadDisc);
        driveActions.Children.Add(eject);eject.Height=30;eject.Click+=(_,_)=>EjectDisc();
        AddButton(driveActions,"曲情報を取得（MusicBrainz）",Lookup);driveActions.Children.Add(readOnOpen);
        FormRow("CDドライブ",driveActions);

        var candidateRow=new DockPanel();
        var candidateActions=new StackPanel{Orientation=Orientation.Horizontal};
        AddButton(candidateActions,"候補を反映",ApplyCandidate);candidateActions.Children.Add(autoApply);
        DockPanel.SetDock(candidateActions,Dock.Right);candidateRow.Children.Add(candidateActions);candidateRow.Children.Add(candidates);
        FormRow("曲情報の候補",candidateRow);

        // The two main metadata fields share both their left and right edges.
        var albumRow=new Grid();albumRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        albumRow.ColumnDefinitions.Add(new(){Width=new GridLength(44)});albumRow.ColumnDefinitions.Add(new(){Width=new GridLength(80)});
        albumRow.Children.Add(album);var yearLabel=Label("年");yearLabel.Margin=new(12,0,4,0);
        Grid.SetColumn(yearLabel,1);albumRow.Children.Add(yearLabel);Grid.SetColumn(year,2);albumRow.Children.Add(year);
        FormRow("アルバム",albumRow);
        var artistRow=new Grid();artistRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        artistRow.ColumnDefinitions.Add(new(){Width=new GridLength(124)});artistRow.Children.Add(artist);
        FormRow("アルバムアーティスト",artistRow);

        var options=new StackPanel{Orientation=Orientation.Horizontal};options.Children.Add(format);
        bitrateLabel.Margin=new(16,0,6,0);options.Children.Add(bitrateLabel);
        var bitrateArea=new Grid{Width=85,Height=30};bitrateArea.Children.Add(bitrate);bitrateArea.Children.Add(bitrateUnused);options.Children.Add(bitrateArea);
        var discLabel=Label("Disc番号 / 総数");discLabel.Margin=new(24,0,8,0);options.Children.Add(discLabel);
        options.Children.Add(discNumber);var separator=Label("/");separator.Margin=new(8,0,8,0);options.Children.Add(separator);options.Children.Add(discCount);
        FormRow("保存形式",options);

        var folderRow=new DockPanel();var browse=new Button{Content="参照…",Width=96,Height=30,Margin=new(8,0,0,0),Padding=new(8,4,8,4)};
        browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFolderDialog{Title="CD取り込み先"};if(picker.ShowDialog(this)==true)destination.Text=picker.FolderName;};
        DockPanel.SetDock(browse,Dock.Right);folderRow.Children.Add(browse);folderRow.Children.Add(destination);
        FormRow("保存先",folderRow);
        var verification=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};verification.Children.Add(accurateRip);verification.Children.Add(offsetEnabled);verification.Children.Add(readOffset);
        var units=Label("サンプル");units.Margin=new(8,0,16,0);verification.Children.Add(units);verification.Children.Add(lookupOffset);
        FormRow("読み取り検証",verification);
        var offsetRow=new DockPanel();DockPanel.SetDock(autoOffset,Dock.Left);offsetRow.Children.Add(autoOffset);
        offsetDrive.TextWrapping=TextWrapping.NoWrap;offsetDrive.TextTrimming=TextTrimming.CharacterEllipsis;offsetDrive.VerticalAlignment=VerticalAlignment.Center;offsetRow.Children.Add(offsetDrive);
        FormRow("ドライブ補正",offsetRow);
    }

    private static TextBlock Label(string text)=>new(){Text=text,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(166,175,187))};
    private void FormRow(string label,FrameworkElement content)
    {
        var row=new Grid{Margin=new(0,0,0,8)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(136)});
        row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        row.Children.Add(Label(label));Grid.SetColumn(content,1);row.Children.Add(content);controls.Children.Add(row);
    }
}
