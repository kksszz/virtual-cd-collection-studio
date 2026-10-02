using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

internal sealed record ArtworkRenameImage(string SourcePath, string? ArchiveEntry);

internal sealed class ArtworkBatchRenameWindow : Window
{
    internal sealed class Row(ArtworkRenameImage image) : INotifyPropertyChanged
    {
        private string _baseName = Path.GetFileNameWithoutExtension((image.ArchiveEntry ?? image.SourcePath).Replace('/', Path.DirectorySeparatorChar));
        private string _error = "";
        internal ArtworkRenameImage Image { get; } = image;
        public string CurrentName { get; } = Path.GetFileName((image.ArchiveEntry ?? image.SourcePath).Replace('/', Path.DirectorySeparatorChar));
        public string BaseName
        {
            get => _baseName;
            set { if (_baseName == value) return; _baseName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaseName))); }
        }
        public string Extension { get; } = Path.GetExtension(image.ArchiveEntry ?? image.SourcePath);
        public string Location { get; } = image.ArchiveEntry is null ? "ファイル" : "ZIP内";
        public string Error
        {
            get => _error;
            set
            {
                if (_error == value) return;
                _error = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
            }
        }
        public bool HasError => Error.Length > 0;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<Row> _rows;
    private readonly DataGrid _grid;
    private readonly Image _previewImage;
    private readonly TextBlock _previewName;
    private readonly TextBlock _previewStatus;
    private readonly TextBlock _errorSummary;
    private int _previewRequest;
    internal IReadOnlyList<ArtworkRenameRequest> Requests { get; private set; } = [];

    internal ArtworkBatchRenameWindow(IReadOnlyList<ArtworkRenameImage> images)
    {
        Title = "アルバム画像のファイル名を一括編集";
        Width = 1120; Height = 620; MinWidth = 850; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 32, 38));
        Foreground = Brushes.White;
        _rows = new ObservableCollection<Row>(images.Select(image => new Row(image)));
        var layout = new DockPanel { Margin = new Thickness(16) };
        var description = new TextBlock
        {
            Text = "変更後の名前を編集してください。拡張子は固定です。空欄・重複名・既存ファイルとの衝突は保存できません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(description, Dock.Top); layout.Children.Add(description);
        _errorSummary = new TextBlock { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_errorSummary, Dock.Top); layout.Children.Add(_errorSummary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var cancel = new Button { Content = "キャンセル", Width = 105, Height = 31, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(cancel);
        var save = new Button { Content = "まとめて保存", Width = 125, Height = 31, IsDefault = true };
        save.Click += Save_Click;
        buttons.Children.Add(save);
        _grid = new DataGrid
        {
            ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            IsReadOnly = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "現在のファイル名", Binding = new Binding(nameof(Row.CurrentName)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "新しい名前（ここを編集）", Binding = new Binding(nameof(Row.BaseName)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "拡張子（固定）", Binding = new Binding(nameof(Row.Extension)), IsReadOnly = true, Width = 105 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "保存先", Binding = new Binding(nameof(Row.Location)), IsReadOnly = true, Width = 85 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "確認", Binding = new Binding(nameof(Row.Error)), IsReadOnly = true, Width = 150 });
        var errorStyle = new Style(typeof(DataGridRow));
        errorStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(Row.HasError)), Value = true,
            Setters = { new Setter(DataGridRow.BackgroundProperty, new SolidColorBrush(Color.FromRgb(85, 36, 39))) }
        });
        _grid.RowStyle = errorStyle;
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        Grid.SetColumn(_grid, 0);
        body.Children.Add(_grid);
        var preview = new DockPanel { Margin = new Thickness(14, 0, 0, 0) };
        Grid.SetColumn(preview, 1);
        body.Children.Add(preview);
        var previewHeading = new TextBlock { Text = "選択中の画像", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(previewHeading, Dock.Top);
        preview.Children.Add(previewHeading);
        _previewName = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2) };
        DockPanel.SetDock(_previewName, Dock.Bottom);
        preview.Children.Add(_previewName);
        _previewStatus = new TextBlock { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_previewStatus, Dock.Bottom);
        preview.Children.Add(_previewStatus);
        _previewImage = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        preview.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 73, 83)),
            BorderThickness = new Thickness(1), Child = _previewImage
        });
        _grid.SelectionChanged += (_, _) => _ = ShowPreviewAsync(_grid.SelectedItem as Row);
        foreach (var row in _rows)
            row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Row.BaseName)) ValidateRows(); };
        Loaded += (_, _) => { if (_rows.Count > 0) _grid.SelectedIndex = 0; };
        layout.Children.Add(body);
        Content = layout;
    }

    private async Task ShowPreviewAsync(Row? row)
    {
        var request = ++_previewRequest;
        _previewImage.Source = null;
        _previewName.Text = row?.CurrentName ?? "";
        _previewStatus.Text = row is null ? "画像を選択してください。" : "読み込み中…";
        if (row is null) return;
        try
        {
            var bitmap = await Task.Run(() => LoadPreview(row.Image));
            if (request != _previewRequest) return;
            _previewImage.Source = bitmap;
            _previewStatus.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px";
        }
        catch (Exception error)
        {
            if (request == _previewRequest) _previewStatus.Text = $"プレビューを表示できません: {error.Message}";
        }
    }

    private static BitmapImage LoadPreview(ArtworkRenameImage image)
    {
        using var file = File.OpenRead(image.SourcePath);
        if (image.ArchiveEntry is null) return Decode(file);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, false, Encoding.GetEncoding(932));
        var entry = archive.GetEntry(image.ArchiveEntry)
            ?? throw new FileNotFoundException("ZIP内の画像が見つかりません。", image.ArchiveEntry);
        using var stream = entry.Open();
        return Decode(stream);
    }

    private static BitmapImage Decode(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1000;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!ValidateRows())
        {
            var first = _rows.First(row => row.HasError);
            _grid.SelectedItem = first;
            _grid.ScrollIntoView(first);
            return;
        }
        try
        {
            var requests = _rows.Select(row => new ArtworkRenameRequest(row.Image.SourcePath,
                row.Image.ArchiveEntry, row.BaseName)).ToArray();
            ArtworkBatchRenameService.ValidateRequests(requests);
            Requests = requests;
            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            _errorSummary.Text = error.Message + " 該当する名前を修正してから再度保存してください。";
        }
    }

    private bool ValidateRows()
    {
        foreach (var row in _rows)
        {
            row.Error = "";
            try { ArtworkBatchRenameService.ValidateBaseName(row.BaseName); }
            catch (InvalidOperationException) { row.Error = "使用できない名前"; }
        }
        foreach (var group in _rows.Where(row => !row.HasError)
            .GroupBy(row => TargetKey(row), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var row in group) row.Error = "名前が重複";
        var count = _rows.Count(row => row.HasError);
        _errorSummary.Text = count == 0 ? "" : $"{count}行に問題があります。色の付いた行の名前だけ修正してください。入力内容は保持されます。";
        return count == 0;
    }

    private static string TargetKey(Row row)
    {
        var targetName = row.BaseName + row.Extension;
        if (row.Image.ArchiveEntry is { } entry)
            return "zip:" + Path.GetFullPath(row.Image.SourcePath) + "|" + entry[..^row.CurrentName.Length] + targetName;
        return "file:" + Path.Combine(Path.GetDirectoryName(Path.GetFullPath(row.Image.SourcePath))!, targetName);
    }
}
