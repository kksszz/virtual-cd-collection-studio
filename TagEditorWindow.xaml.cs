using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ZipMp3Player;

public partial class TagEditorWindow : Window
{
    private readonly ObservableCollection<TagEditRow> _rows;
    internal IReadOnlyList<TrackTagUpdate> EditedTracks { get; private set; } = [];

    public TagEditorWindow(ZipAlbum album, string? selectedFileName = null)
    {
        InitializeComponent();
        _rows = new ObservableCollection<TagEditRow>(album.Tracks.Select(track => new TagEditRow(track)));
        TagsGrid.ItemsSource = _rows;
        TrackCountText.Text = LocalizationService.Select($"{_rows.Count}曲", $"{_rows.Count} tracks");
        var isArchive = album.Tracks.FirstOrDefault()?.IsArchiveEntry == true;
        SourceTypeText.Text = isArchive
            ? "ZIP.MP3：全曲の変更をまとめて1回だけ再構築"
            : "通常フォルダ：変更した各音楽ファイルへ実タグを書き込み";
        SourcePathText.Text = album.Path;
        LocalizationService.Apply(this);
        Loaded += (_, _) =>
        {
            var selected = _rows.FirstOrDefault(row => string.Equals(row.FileName, selectedFileName, StringComparison.Ordinal));
            if (selected is not null) { TagsGrid.SelectedItem = selected; TagsGrid.ScrollIntoView(selected); }
            TagsGrid.Focus();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var updates = new List<TrackTagUpdate>();
        foreach (var row in _rows)
        {
            if (!TryNumber(row.Year, 9999, "年", row.DisplayFileName, out var year)
                || !TryNumber(row.TrackNumber, uint.MaxValue, "曲番号", row.DisplayFileName, out var track)
                || !TryNumber(row.DiscNumber, uint.MaxValue, "ディスク番号", row.DisplayFileName, out var disc)
                || !TryNumber(row.DiscCount, uint.MaxValue, "ディスク総数", row.DisplayFileName, out var discCount)) return;
            if (discCount > 0 && disc > discCount)
            {
                ValidationText.Text = LocalizationService.Select($"{row.DisplayFileName}: ディスク番号は総数以下にしてください。", $"{row.DisplayFileName}: Disc number must not exceed disc count.");
                return;
            }
            var values = new TrackTagValues(row.Title.Trim(), row.Artist.Trim(), row.Album.Trim(), year,
                row.Genre.Trim(), track, disc, discCount);
            if (row.IsChanged(values)) updates.Add(new TrackTagUpdate(row.FileName, row.SourcePath, values));
        }
        if (updates.Count == 0)
        {
            ValidationText.Text = LocalizationService.Select("変更された項目はありません。", "No fields were changed.");
            return;
        }
        EditedTracks = updates;
        DialogResult = true;
    }

    private void ApplySelectedCellToAll_Click(object sender, RoutedEventArgs e)
    {
        TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (TagsGrid.CurrentItem is not TagEditRow source || GetColumnProperty(TagsGrid.CurrentCell.Column) is not { } property
            || !TryGetValue(source, property, out var value))
        {
            BatchStatusText.Text = LocalizationService.Select("適用する編集可能なセルを選択してください", "Select an editable cell to apply");
            return;
        }
        foreach (var row in _rows) SetValue(row, property, value);
        TagsGrid.Items.Refresh();
        BatchStatusText.Text = LocalizationService.Select($"「{value}」を{_rows.Count}曲へ一括適用しました（未保存）",
            $"Applied “{value}” to {_rows.Count} tracks (not saved yet)");
    }

    private void TagsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        if (!Clipboard.ContainsText() || TagsGrid.CurrentItem is not TagEditRow startRow) return;
        TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var text = Clipboard.GetText().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n');
        var lines = text.Split('\n');
        var matrix = lines.Select(line => line.TrimEnd('\r').Split('\t')).ToArray();
        var pasted = 0;

        if (matrix.Length == 1 && matrix[0].Length == 1 && TagsGrid.SelectedCells.Count > 1)
        {
            foreach (var cell in TagsGrid.SelectedCells)
                if (cell.Item is TagEditRow row && GetColumnProperty(cell.Column) is { } property
                    && SetValue(row, property, matrix[0][0])) pasted++;
        }
        else
        {
            var startRowIndex = _rows.IndexOf(startRow);
            var visibleColumns = TagsGrid.Columns.OrderBy(column => column.DisplayIndex).ToList();
            var startColumnIndex = visibleColumns.IndexOf(TagsGrid.CurrentCell.Column);
            for (var rowOffset = 0; rowOffset < matrix.Length && startRowIndex + rowOffset < _rows.Count; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < matrix[rowOffset].Length
                    && startColumnIndex + columnOffset < visibleColumns.Count; columnOffset++)
                {
                    var column = visibleColumns[startColumnIndex + columnOffset];
                    if (GetColumnProperty(column) is { } property
                        && SetValue(_rows[startRowIndex + rowOffset], property, matrix[rowOffset][columnOffset])) pasted++;
                }
            }
        }
        TagsGrid.Items.Refresh();
        BatchStatusText.Text = LocalizationService.Select($"クリップボードから{pasted}セルを貼り付けました（未保存）",
            $"Pasted {pasted} cells from the clipboard (not saved yet)");
    }

    private static string? GetColumnProperty(DataGridColumn? column)
    {
        if (column?.IsReadOnly == true || column is not DataGridBoundColumn bound || bound.Binding is not Binding binding) return null;
        return binding.Path?.Path;
    }

    private static bool TryGetValue(TagEditRow row, string property, out string value)
    {
        value = property switch
        {
            nameof(TagEditRow.Title) => row.Title,
            nameof(TagEditRow.Artist) => row.Artist,
            nameof(TagEditRow.Album) => row.Album,
            nameof(TagEditRow.Year) => row.Year,
            nameof(TagEditRow.Genre) => row.Genre,
            nameof(TagEditRow.TrackNumber) => row.TrackNumber,
            nameof(TagEditRow.DiscNumber) => row.DiscNumber,
            nameof(TagEditRow.DiscCount) => row.DiscCount,
            _ => ""
        };
        return property is nameof(TagEditRow.Title) or nameof(TagEditRow.Artist) or nameof(TagEditRow.Album)
            or nameof(TagEditRow.Year) or nameof(TagEditRow.Genre) or nameof(TagEditRow.TrackNumber)
            or nameof(TagEditRow.DiscNumber) or nameof(TagEditRow.DiscCount);
    }

    private static bool SetValue(TagEditRow row, string property, string value)
    {
        switch (property)
        {
            case nameof(TagEditRow.Title): row.Title = value; return true;
            case nameof(TagEditRow.Artist): row.Artist = value; return true;
            case nameof(TagEditRow.Album): row.Album = value; return true;
            case nameof(TagEditRow.Year): row.Year = value; return true;
            case nameof(TagEditRow.Genre): row.Genre = value; return true;
            case nameof(TagEditRow.TrackNumber): row.TrackNumber = value; return true;
            case nameof(TagEditRow.DiscNumber): row.DiscNumber = value; return true;
            case nameof(TagEditRow.DiscCount): row.DiscCount = value; return true;
            default: return false;
        }
    }

    private bool TryNumber(string text, uint maximum, string label, string file, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (uint.TryParse(text.Trim(), out value) && value <= maximum) return true;
        ValidationText.Text = LocalizationService.Select($"{file}: {label}は0以上{maximum}以下の整数で入力してください。", $"{file}: Enter {label} as an integer from 0 to {maximum}.");
        return false;
    }

    private sealed class TagEditRow
    {
        private readonly TrackTagValues _original;
        public string FileName { get; }
        public string SourcePath { get; }
        public string DisplayFileName => Path.GetFileName(FileName);
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Year { get; set; }
        public string Genre { get; set; }
        public string TrackNumber { get; set; }
        public string DiscNumber { get; set; }
        public string DiscCount { get; set; }

        public TagEditRow(ZipTrack track)
        {
            FileName = track.FileName;
            SourcePath = track.SourcePath;
            Title = track.Title; Artist = track.Artist; Album = track.Album; Year = track.Year; Genre = track.Genre;
            TrackNumber = track.TrackNumber > 0 ? track.TrackNumber.ToString() : "";
            DiscNumber = track.DiscNumber > 0 ? track.DiscNumber.ToString() : "";
            DiscCount = track.DiscCount > 0 ? track.DiscCount.ToString() : "";
            _original = new TrackTagValues(Title.Trim(), Artist.Trim(), Album.Trim(), Parse(Year), Genre.Trim(),
                Parse(TrackNumber), Parse(DiscNumber), Parse(DiscCount));
        }

        public bool IsChanged(TrackTagValues values) => values != _original;
        private static uint Parse(string value) => uint.TryParse(value, out var parsed) ? parsed : 0;
    }
}
