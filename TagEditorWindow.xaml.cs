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
            ? LocalizationService.Select("ZIP.MP3：全曲の変更をまとめて1回だけ再構築", "ZIP.MP3: rebuild once for all edited tracks")
            : LocalizationService.Select("通常フォルダ：変更した各音楽ファイルへ実タグを書き込み", "Folder: write real tags to each edited audio file");
        SourcePathText.Text = album.Path;
        TagEditNoticeText.Text = LocalizationService.Select(
            "ファイル名も選択・コピー・編集できます（拡張子は変更不可）。音声は再エンコードしません。タグ編集バックアップの有無と保存先は設定画面で変更できます。",
            "File names can also be selected, copied, and edited (the extension cannot be changed). Audio is not re-encoded. Tag-edit backups and their destination can be changed in Settings.");
        LocalizationService.Apply(this);
        Loaded += (_, _) =>
        {
            var selected = _rows.FirstOrDefault(row => string.Equals(row.FileName, selectedFileName, StringComparison.Ordinal));
            if (selected is not null) { TagsGrid.SelectedItem = selected; TagsGrid.ScrollIntoView(selected); }
            TagsGrid.Focus();
            UpdateTagAnomalySummary();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var updates = new List<TrackTagUpdate>();
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            if (!TryValidateFileName(row, out var targetFileName)) return;
            if (!targetNames.Add(targetFileName))
            {
                ValidationText.Text = LocalizationService.Select($"同じファイル名が複数指定されています: {row.DisplayFileName}",
                    $"The same file name is specified more than once: {row.DisplayFileName}");
                return;
            }
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
            if (row.IsChanged(values, targetFileName))
                updates.Add(new TrackTagUpdate(row.FileName, row.SourcePath, values, targetFileName));
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

    private void NormalizeAlphaNumeric_Click(object sender, RoutedEventArgs e)
    {
        if (!TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !TagsGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            BatchStatusText.Text = LocalizationService.Select("編集中のセルを確定してから変換してください。", "Finish editing the current cell before converting.");
            return;
        }
        var tracks = 0;
        var cells = 0;
        foreach (var row in _rows)
        {
            var changed = false;
            foreach (var column in TagsGrid.Columns)
            {
                if (GetColumnProperty(column) is not { } property || !TryGetValue(row, property, out var value)) continue;
                var converted = TagTextNormalization.ToHalfWidthAsciiSymbols(value,
                    preserveInvalidFileNameCharacters: property == nameof(TagEditRow.DisplayFileName));
                if (converted == value) continue;
                SetValue(row, property, converted);
                cells++;
                changed = true;
            }
            if (changed) tracks++;
        }
        TagsGrid.Items.Refresh();
        BatchStatusText.Text = cells == 0
            ? LocalizationService.Select("変換対象の全角英数字・記号・全角スペースはありません。", "No full-width letters, digits, symbols, or spaces to convert.")
            : LocalizationService.Select($"{tracks}曲・{cells}項目の英数字・記号・空白を半角にしました（未保存）。確認後に「まとめて保存」を押してください。",
                $"Converted letters, digits, symbols, and spaces in {cells} fields across {tracks} tracks (not saved). Review, then choose Save All.");
    }

    private void NormalizeTitleCase_Click(object sender, RoutedEventArgs e)
    {
        if (!TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !TagsGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            BatchStatusText.Text = LocalizationService.Select("編集中のセルを確定してから変換してください。", "Finish editing the current cell before converting.");
            return;
        }

        var selectedTextCells = TagsGrid.SelectedCells
            .Where(cell => cell.Item is TagEditRow && GetColumnProperty(cell.Column) is { } property && IsTitleCaseProperty(property))
            .ToList();
        if (selectedTextCells.Count == 0)
        {
            BatchStatusText.Text = LocalizationService.Select(
                "タイトル・アーティスト・アルバム・ジャンルのセルを選択してください。",
                "Select title, artist, album, or genre cells.");
            return;
        }

        var changed = 0;
        foreach (var cell in selectedTextCells)
        {
            var row = (TagEditRow)cell.Item;
            var property = GetColumnProperty(cell.Column)!;
            if (!TryGetValue(row, property, out var value)) continue;
            var converted = TagTextNormalization.UpperCaseWordsToTitleCase(value);
            if (converted == value) continue;
            SetValue(row, property, converted);
            changed++;
        }
        TagsGrid.Items.Refresh();
        BatchStatusText.Text = changed == 0
            ? LocalizationService.Select("選択範囲に変換対象の全大文字データはありません。", "No all-uppercase values were found in the selection.")
            : LocalizationService.Select($"選択範囲の{changed}セルを先頭大文字へ変換しました（未保存）。",
                $"Converted {changed} selected cells to title case (not saved yet).");
    }

    private static bool IsTitleCaseProperty(string property) => property is nameof(TagEditRow.Title)
        or nameof(TagEditRow.Artist) or nameof(TagEditRow.Album) or nameof(TagEditRow.Genre);

    private void CheckTagAnomalies_Click(object sender, RoutedEventArgs e)
    {
        TagsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TagsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var anomalies = DetectTagAnomalies();
        UpdateTagAnomalySummary(anomalies);
        MessageBox.Show(this,
            anomalies.Count == 0
                ? LocalizationService.Select("明確なタグ異常は見つかりませんでした。", "No clear tag anomalies were found.")
                : string.Join(Environment.NewLine + Environment.NewLine,
                    anomalies.Select((anomaly, index) => $"{index + 1}. {anomaly.Message}")),
            LocalizationService.Select("タグ異常チェック", "Tag Anomaly Check"),
            MessageBoxButton.OK, anomalies.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private IReadOnlyList<TagAnomaly> DetectTagAnomalies() => TagAnomalyDetector.Analyze(_rows.Select(row =>
        new TagAnomalyInput(row.DisplayFileName, row.Title, row.Artist, row.Album, row.Year,
            row.Genre, row.TrackNumber, row.DiscNumber)).ToList());

    private void UpdateTagAnomalySummary(IReadOnlyList<TagAnomaly>? anomalies = null)
    {
        anomalies ??= DetectTagAnomalies();
        BatchStatusText.Text = anomalies.Count == 0
            ? LocalizationService.Select("タグ異常チェック: 明確な問題はありません。", "Tag anomaly check: no clear issues.")
            : LocalizationService.Select($"タグ異常チェック: {anomalies.Count}項目を検出しました。「タグ異常をチェック」で詳細を確認できます。",
                $"Tag anomaly check: {anomalies.Count} issues found. Choose Tag Anomaly Check for details.");
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
            nameof(TagEditRow.DisplayFileName) => row.DisplayFileName,
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
        return property is nameof(TagEditRow.DisplayFileName) or nameof(TagEditRow.Title) or nameof(TagEditRow.Artist) or nameof(TagEditRow.Album)
            or nameof(TagEditRow.Year) or nameof(TagEditRow.Genre) or nameof(TagEditRow.TrackNumber)
            or nameof(TagEditRow.DiscNumber) or nameof(TagEditRow.DiscCount);
    }

    private static bool SetValue(TagEditRow row, string property, string value)
    {
        switch (property)
        {
            case nameof(TagEditRow.DisplayFileName): row.DisplayFileName = value; return true;
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

    private bool TryValidateFileName(TagEditRow row, out string targetFileName)
    {
        targetFileName = row.TargetFileName;
        var name = row.DisplayFileName.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\'))
        {
            ValidationText.Text = LocalizationService.Select($"{row.DisplayFileName}: 使用できるファイル名を入力してください。",
                $"{row.DisplayFileName}: Enter a valid file name.");
            return false;
        }
        if (!string.Equals(Path.GetExtension(name), Path.GetExtension(row.OriginalDisplayFileName), StringComparison.OrdinalIgnoreCase))
        {
            ValidationText.Text = LocalizationService.Select($"{row.OriginalDisplayFileName}: 拡張子は変更できません。",
                $"{row.OriginalDisplayFileName}: The file extension cannot be changed.");
            return false;
        }
        targetFileName = row.BuildTargetFileName(name);
        return true;
    }

    private sealed class TagEditRow
    {
        private readonly TrackTagValues _original;
        public string FileName { get; }
        public string SourcePath { get; }
        public string OriginalDisplayFileName { get; }
        public string DisplayFileName { get; set; }
        public string TargetFileName => BuildTargetFileName(DisplayFileName.Trim());
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Year { get; set; }
        public string Genre { get; set; }
        public string TrackNumber { get; set; }
        public string DiscNumber { get; set; }
        public string DiscCount { get; set; }
        public long YearSort => ParseSortNumber(Year);
        public long TrackNumberSort => ParseSortNumber(TrackNumber);
        public long DiscNumberSort => ParseSortNumber(DiscNumber);
        public long DiscCountSort => ParseSortNumber(DiscCount);

        public TagEditRow(ZipTrack track)
        {
            FileName = track.FileName;
            SourcePath = track.SourcePath;
            OriginalDisplayFileName = Path.GetFileName(track.FileName);
            DisplayFileName = OriginalDisplayFileName;
            Title = track.Title; Artist = track.Artist; Album = track.Album; Year = track.Year; Genre = track.Genre;
            TrackNumber = track.TrackNumber > 0 ? track.TrackNumber.ToString() : "";
            DiscNumber = track.DiscNumber > 0 ? track.DiscNumber.ToString() : "";
            DiscCount = track.DiscCount > 0 ? track.DiscCount.ToString() : "";
            _original = new TrackTagValues(Title.Trim(), Artist.Trim(), Album.Trim(), Parse(Year), Genre.Trim(),
                Parse(TrackNumber), Parse(DiscNumber), Parse(DiscCount));
        }

        public string BuildTargetFileName(string displayFileName)
        {
            var slash = Math.Max(FileName.LastIndexOf('/'), FileName.LastIndexOf('\\'));
            return slash >= 0 ? FileName[..(slash + 1)] + displayFileName : displayFileName;
        }

        public bool IsChanged(TrackTagValues values, string targetFileName) => values != _original
            || !string.Equals(targetFileName, FileName, StringComparison.Ordinal);
        private static uint Parse(string value) => uint.TryParse(value, out var parsed) ? parsed : 0;
        private static long ParseSortNumber(string value) => uint.TryParse(value, out var parsed) ? parsed : long.MaxValue;
    }
}
