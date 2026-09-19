using System.Windows;
using System.Windows.Markup;

namespace ZipMp3Player;

public static class MobileSyncAppearance
{
    public static System.Windows.Controls.ListView CreateTransferTable(){
        var list=new System.Windows.Controls.ListView{SelectionMode=System.Windows.Controls.SelectionMode.Multiple,Background=System.Windows.Media.Brushes.White,Foreground=System.Windows.Media.Brushes.Black,FontSize=13,BorderThickness=new Thickness(1),BorderBrush=System.Windows.Media.Brushes.LightGray};
        list.ItemContainerStyle=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
          <Setter Property="Height" Value="30"/><Setter Property="Foreground" Value="#203747"/><Setter Property="Background" Value="White"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListViewItem">
            <Border BorderBrush="#E0E7ED" BorderThickness="0,0,0,1" Background="{TemplateBinding Background}">
              <GridViewRowPresenter Margin="2,0,0,0" Content="{TemplateBinding Content}" Columns="{Binding View.Columns,RelativeSource={RelativeSource AncestorType=ListView}}" VerticalAlignment="Center"/>
            </Border>
          </ControlTemplate></Setter.Value></Setter>
          <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#F0F6FA"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#CCE5FA"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.65"/></Trigger></Style.Triggers>
        </Style>
        """);
        var view=new System.Windows.Controls.GridView{AllowsColumnReorder=true};list.View=view;
        view.Columns.Add(new System.Windows.Controls.GridViewColumn{Header="選択",Width=44,CellTemplate=(DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><CheckBox IsChecked="{Binding IsSelected,RelativeSource={RelativeSource AncestorType=ListViewItem},Mode=OneWay}" IsHitTestVisible="False" Focusable="False" HorizontalAlignment="Center"/></DataTemplate>
        """)});
        foreach(var (header,path,width) in new[]{("アルバム","Album.Title",260d),("アーティスト","Album.Artist",165d),("曲数","Tracks",48d),("ファイル容量","Size",105d),("形式","Format",120d),("3D","CaseStatus",76d),("画像","Images",76d)}){
            var template=(DataTemplate)XamlReader.Parse($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Border Margin="-6,0,-6,0" BorderBrush="#E0E7ED" BorderThickness="0,0,1,0" Padding="10,3"><TextBlock Text="{Binding {{path}}}" ToolTip="{Binding {{path}}}" TextTrimming="CharacterEllipsis"/></Border></DataTemplate>
            """);
            view.Columns.Add(new System.Windows.Controls.GridViewColumn{Header=header,Width=width,CellTemplate=template});
        }
        return list;
    }
    public static void Apply(Window window){
        window.FontFamily=new System.Windows.Media.FontFamily("Yu Gothic UI");window.FontSize=14;
        window.Background=(System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F4F7FA")!;
        window.Resources=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="#E5EDF3"/><Setter Property="Foreground" Value="#203747"/>
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
          <Style TargetType="TextBox"><Setter Property="Padding" Value="10,7"/><Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#203747"/><Setter Property="BorderBrush" Value="#CAD7E0"/></Style>
          <Style TargetType="ComboBox"><Setter Property="Padding" Value="8,6"/><Setter Property="MinHeight" Value="34"/><Setter Property="Foreground" Value="#203747"/></Style>
        </ResourceDictionary>
        """);
    }
    public static DataTemplate CreateAlbumTemplate() => (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="36"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <CheckBox IsChecked="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}, Mode=OneWay}" IsHitTestVisible="False" Focusable="False" VerticalAlignment="Center"/>
            <StackPanel Grid.Column="1">
              <TextBlock Text="{Binding Album.Title}" FontSize="15" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" ToolTip="{Binding Album.Title}"/>
              <TextBlock Text="{Binding Album.Artist}" FontSize="12" Foreground="#526D7D" Margin="0,3,0,0" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
          </Grid>
        </DataTemplate>
        """);
    // The application-wide ListBoxItem style is intended for dark windows.
    // Keep this light dialog self-contained, including inactive selection colours.
    public static Style CreateItemStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
          <Setter Property="Foreground" Value="#17212B"/>
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
            <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#EDF4FA"/></Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter Property="Background" Value="#CCE5FA"/>
              <Setter Property="Foreground" Value="#102A43"/>
              <Setter Property="BorderBrush" Value="#3979A8"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="BorderBrush" Value="#24689B"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.65"/></Trigger>
          </Style.Triggers>
        </Style>
        """);
}
