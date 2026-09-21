using System.Windows;
using System.Windows.Markup;

namespace ZipMp3Player;

public static class MobileSyncAppearance
{
    public static System.Windows.Media.Brush Brush(string color) => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!;
    public static System.Windows.Controls.ListView CreateTransferTable(){
        var list=new System.Windows.Controls.ListView{SelectionMode=System.Windows.Controls.SelectionMode.Multiple,Background=Brush("#1F2329"),Foreground=Brush("#F2F3F5"),FontSize=13,BorderThickness=new Thickness(1),BorderBrush=Brush("#454C58")};
        list.ItemContainerStyle=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
          <Setter Property="Height" Value="30"/><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="Background" Value="#1F2329"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListViewItem">
            <Border BorderBrush="#353D48" BorderThickness="0,0,0,1" Background="{TemplateBinding Background}">
              <GridViewRowPresenter Margin="2,0,0,0" Content="{TemplateBinding Content}" Columns="{Binding View.Columns,RelativeSource={RelativeSource AncestorType=ListView}}" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate></Setter.Value></Setter>
          <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#2C3845"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#285B80"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.65"/></Trigger></Style.Triggers>
        </Style>
        """);
        var view=new System.Windows.Controls.GridView{AllowsColumnReorder=true};list.View=view;
        view.Columns.Add(new System.Windows.Controls.GridViewColumn{Header="選択",Width=44,CellTemplate=(DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><CheckBox IsChecked="{Binding IsSelected,RelativeSource={RelativeSource AncestorType=ListViewItem},Mode=OneWay}" IsHitTestVisible="False" Focusable="False" HorizontalAlignment="Center"/></DataTemplate>
        """)});
        foreach(var (header,path,width) in new[]{("アルバム","Album.Title",260d),("アーティスト","Album.Artist",165d),("曲数","Tracks",48d),("ファイル容量","Size",105d),("形式","Format",120d),("3D","CaseStatus",76d),("画像","Images",76d),("歌詞","Lyrics",64d)}){
            var template=(DataTemplate)XamlReader.Parse($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Border Margin="-6,0,-6,0" BorderBrush="#353D48" BorderThickness="0,0,1,0" Padding="10,3"><TextBlock Text="{Binding {{path}}}" ToolTip="{Binding {{path}}}" TextTrimming="CharacterEllipsis"/></Border></DataTemplate>
            """);
            view.Columns.Add(new System.Windows.Controls.GridViewColumn{Header=header,Width=width,CellTemplate=template});
        }
        return list;
    }
    public static void Apply(Window window){
        window.Foreground=Brush("#F2F3F5");
        window.FontFamily=new System.Windows.Media.FontFamily("Yu Gothic UI");window.FontSize=14;
        window.Background=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#15171B")!;
        window.Resources=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="#2C313A"/><Setter Property="Foreground" Value="#F2F3F5"/>
            <Setter Property="Padding" Value="14,9"/><Setter Property="MinHeight" Value="36"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="surface" Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.85"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#16859B"/><Setter TargetName="surface" Property="BorderThickness" Value="2"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="TextBox"><Setter Property="Padding" Value="10,7"/><Setter Property="Background" Value="#1F2329"/><Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="BorderBrush" Value="#454C58"/></Style>
          <Style TargetType="ComboBox"><Setter Property="Padding" Value="8,6"/><Setter Property="MinHeight" Value="34"/><Setter Property="Background" Value="#F3F4F6"/><Setter Property="Foreground" Value="#18202A"/></Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Background" Value="#F3F4F6"/><Setter Property="Foreground" Value="#18202A"/><Setter Property="Padding" Value="7,4"/></Style>
          <Style TargetType="Expander"><Setter Property="Foreground" Value="#F2F3F5"/></Style>
          <Style TargetType="GridViewColumnHeader">
            <Setter Property="Foreground" Value="#F2F3F5"/><Setter Property="Background" Value="#2C313A"/>
            <Setter Property="Padding" Value="6,4"/><Setter Property="BorderBrush" Value="#454C58"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="GridViewColumnHeader">
              <Grid><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="0,0,1,1" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border><Thumb x:Name="PART_HeaderGripper" Width="6" HorizontalAlignment="Right" Background="Transparent">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate></Thumb.Template>
              </Thumb></Grid>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#36495B"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """);
    }
    public static DataTemplate CreateAlbumTemplate() => (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="36"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <CheckBox IsChecked="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}, Mode=OneWay}" IsHitTestVisible="False" Focusable="False" VerticalAlignment="Center"/>
            <StackPanel Grid.Column="1">
              <TextBlock Text="{Binding Album.Title}" FontSize="15" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" ToolTip="{Binding Album.Title}"/>
              <TextBlock Text="{Binding Album.Artist}" FontSize="12" Foreground="#B3C1D1" Margin="0,3,0,0" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
          </Grid>
        </DataTemplate>
        """);
    // The application-wide ListBoxItem style is intended for dark windows.
    // Keep selection readable even when the dialog loses focus.
    public static Style CreateItemStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
          <Setter Property="Foreground" Value="#F2F3F5"/>
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="BorderBrush" Value="Transparent"/>
          <Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="12,9"/>
          <Setter Property="Margin" Value="3,2"/>
          <Setter Property="MinHeight" Value="38"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ListBoxItem">
                <Border CornerRadius="7" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="Center"/>
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#2C3845"/></Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter Property="Background" Value="#285B80"/>
              <Setter Property="Foreground" Value="#FFFFFF"/>
              <Setter Property="BorderBrush" Value="#3979A8"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="BorderBrush" Value="#24689B"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.65"/></Trigger>
          </Style.Triggers>
        </Style>
        """);
}
