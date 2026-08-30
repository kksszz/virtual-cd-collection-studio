using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ZipMp3Player;

internal static class LocalizationService
{
    public const string Japanese = "ja";
    public const string English = "en";
    public static string CurrentLanguage { get; private set; } = Japanese;
    public static bool IsEnglish => CurrentLanguage == English;

    private static readonly IReadOnlyDictionary<string, string> JapaneseToEnglish =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ファイルを開く"] = "Open File", ["履歴"] = "History", ["設定"] = "Settings",
            ["再生"] = "Play", ["▶ 再生"] = "▶ Play", ["■ 停止"] = "■ Stop", ["停止"] = "Stopped",
            ["再生中"] = "Playing", ["▶ 再生中"] = "▶ Playing", ["  ▶ 再生中"] = "  ▶ Playing",
            ["↪ 再生中へ"] = "↪ Go to Playing", ["◀ 前へ"] = "◀ Previous", ["次へ ▶"] = "Next ▶",
            ["再生していません"] = "Not playing", ["曲を再生するとタグ情報を表示します"] = "Track details appear during playback",
            ["固定ビットレートMP3を直接再生します"] = "Plays fixed-bitrate MP3 files directly",
            ["再生履歴・Usageを表示"] = "Show playback history and usage",
            ["設定（フォルダ管理・バックアップ・移行）"] = "Settings (folders, backup and migration)",
            ["右側の拡張機能を折りたたむ"] = "Collapse the extension panel",
            ["右側の拡張機能を開く"] = "Open the extension panel",
            ["音質向上（リアルタイム）"] = "Sound Enhancement (Real-time)",
            ["原音忠実モード"] = "Source-Faithful Mode",
            ["音質加工とソフト音量を迂回し、WASAPIで原音を直接再生します"] = "Bypass sound processing and software volume; play directly through WASAPI",
            ["OFF：音質向上・EQ・音量調整を使用します"] = "OFF: Sound enhancement, EQ and volume controls are enabled",
            ["OFF（原音）"] = "OFF (Original)", ["軽め"] = "Light", ["標準"] = "Standard",
            ["強め"] = "Strong", ["劇的（強調）"] = "Dramatic", ["HDR風（実験）"] = "HDR-like (Experimental)",
            ["小音量クリア"] = "Low-Volume Clarity",
            ["元ファイルを変更せず、再生音だけを補正します"] = "Enhances playback without modifying the source file",
            ["音量を上げにくい環境向け。低音・声・アタックを聴き取りやすくし、急な大音量を抑えます"] = "Improves bass, voices and attack at low volume while controlling sudden peaks",
            ["高域・輪郭・音量感を補正します。HDR風は細部・アタック・広がりも適応調整します。"] = "Enhances treble, definition and loudness. HDR-like mode also adapts detail, attack and width.",
            ["AIギター・リメイク（実験）"] = "AI Guitar Remake (Experimental)",
            ["選択曲の再生位置から60秒をAIで楽器分離し、ギター・ドラム・ベースを現代的な音へ再構築します。音質向上とEQも試聴時に統合されます。"] = "Separates 60 seconds from the selected position and rebuilds guitar, drums and bass with a modern sound. Enhancement and EQ are included in the preview.",
            ["スタイル"] = "Style", ["モダンメタル"] = "Modern Metal", ["現代的ハードロック"] = "Modern Hard Rock",
            ["タイトなスラッシュ"] = "Tight Thrash", ["劇的AIリメイク（最大）"] = "Dramatic AI Remake (Maximum)",
            ["変化量"] = "Amount", ["AI機能を準備"] = "Prepare AI", ["60秒を作成"] = "Create 60 sec",
            ["AI版を試聴"] = "Preview AI Version", ["AIデータ削除"] = "Delete AI Data",
            ["未準備（初回のみ追加データを取得します）"] = "Not prepared (downloads additional data once)", ["準備完了"] = "Ready",
            ["AIカラオケ（ボーカルレス）"] = "AI Karaoke (Vocal-free)",
            ["AIでボーカルだけを分離して伴奏を再構成します。AI環境はリメイク機能と共通です。"] = "Separates vocals and rebuilds the accompaniment. Uses the same AI environment as Remake.",
            ["除去率"] = "Removal", ["カラオケ版を試聴"] = "Preview Karaoke", ["未作成"] = "Not created",
            ["ビジュアライザー"] = "Visualizer", ["表示"] = "Display", ["波形"] = "Waveform", ["スペクトラムバー"] = "Spectrum Bars",
            ["10バンド イコライザー"] = "10-band Equalizer", ["デフォルト"] = "Default", ["カスタム"] = "Custom",
            ["ハードロック"] = "Hard Rock", ["ヘヴィメタル"] = "Heavy Metal", ["スラッシュメタル"] = "Thrash Metal",
            ["デスメタル"] = "Death Metal", ["パワーメタル"] = "Power Metal", ["低音強調"] = "Bass Boost",
            ["ボーカル"] = "Vocal", ["高音強調"] = "Treble Boost", ["リセット"] = "Reset",
            ["EXTRA BASS風"] = "EXTRA BASS-style",
            ["29Hz未満の不要な振動を除去し、音楽成分がある時だけ低域を強調します"] = "Removes sub-29 Hz rumble and boosts bass only when musical content is present",
            ["アルバム"] = "Album", ["アルバム一覧の並び順"] = "Album list order", ["アーティスト順"] = "Artist",
            ["アルバム名順"] = "Album Title", ["アーティストツリー"] = "Artist Tree", ["検索"] = "Search",
            ["アルバム名・アーティスト名・ファイル名・曲名で絞り込み"] = "Filter by album, artist, file or track title",
            ["検索をクリア"] = "Clear search", ["エクスプローラーで場所を開く"] = "Open Location in Explorer",
            ["インターネットからアルバム画像を取得"] = "Download Album Artwork", ["リストから削除"] = "Remove from List",
            ["アルバムのお気に入りを登録／解除"] = "Toggle album favorite", ["曲のお気に入りを登録／解除"] = "Toggle track favorite",
            ["アーティスト"] = "Artist", ["タイトル"] = "Title", ["時間"] = "Time", ["音質"] = "Audio",
            ["年"] = "Year", ["ジャンル"] = "Genre", ["歌詞"] = "Lyrics", ["この曲には登録済みの歌詞があります"] = "Lyrics are saved for this track",
            ["ファイル"] = "File", ["曲番号"] = "Track Number", ["ディスク番号"] = "Disc Number", ["ディスク総数"] = "Disc Count",
            ["Disc総数"] = "Disc Count", ["アルバムのタグを編集…"] = "Edit Album Tags…",
            ["アルバムのタグを編集"] = "Edit Album Tags", ["アルバムのタグを一括編集"] = "Edit Album Tags",
            ["まとめて保存"] = "Save All", ["タグ編集"] = "Tag Editor", ["タグ編集完了"] = "Tag Edit Complete",
            ["タグ編集エラー"] = "Tag Edit Error", ["変更された項目はありません。"] = "No fields were changed.",
            ["保存時に元ファイルの隣へ日時付きバックアップを作成します。音声は再エンコードしません。"] = "A timestamped backup is created beside the source when saving. Audio is not re-encoded.",
            ["音声は再エンコードしません。タグ編集バックアップの有無と保存先は設定画面で変更できます。"] = "Audio is not re-encoded. Tag-edit backup creation and its folder can be changed in Settings.",
            ["タグ編集時に元ファイルのバックアップを保存する"] = "Save source-file backups when editing tags",
            ["デフォルトは無効です。有効時はアルバムフォルダではなく、下の専用フォルダへ保存します。"] = "Disabled by default. When enabled, backups are saved to the dedicated folder below instead of album folders.",
            ["保存先を選択"] = "Choose Folder", ["タグ編集バックアップの保存先フォルダ"] = "Folder for tag-edit backups",
            ["バックアップ保存先"] = "Backup Folder",
            ["ZIP.MP3：全曲をまとめて1回だけ再構築・バックアップ"] = "ZIP.MP3: rebuild all edited tracks once and create one backup",
            ["通常フォルダ：変更した各音楽ファイルへ実タグを書き込み・バックアップ"] = "Folder: write real tags to each edited audio file and create backups",
            ["ZIP.MP3：全曲の変更をまとめて1回だけ再構築"] = "ZIP.MP3: rebuild once for all edited tracks",
            ["通常フォルダ：変更した各音楽ファイルへ実タグを書き込み"] = "Folder: write real tags to each edited audio file",
            ["選択セルの値を全曲へ適用"] = "Apply Selected Cell to All Tracks",
            ["アーティストやアルバムなど、現在のセルと同じ列へ一括適用"] = "Apply the current artist, album or other cell value to the entire column",
            ["複数セルのコピー・貼り付け: Ctrl+C / Ctrl+V"] = "Copy and paste multiple cells: Ctrl+C / Ctrl+V",
            ["無圧縮ZIP.MP3へ変換…"] = "Convert to Uncompressed ZIP.MP3…",
            ["無圧縮ZIP.MP3へ変換"] = "Convert to Uncompressed ZIP.MP3",
            ["音声を再エンコードせず、ZIP内の全収録物をStore方式へ変換"] = "Convert every ZIP entry to Store without re-encoding audio",
            ["通常フォルダのアルバムは変換対象外です"] = "Folder albums do not require conversion",
            ["このZIP.MP3はすでに無圧縮です"] = "This ZIP.MP3 is already stored without compression",
            ["変換完了"] = "Conversion Complete", ["ZIP変換エラー"] = "ZIP Conversion Error",
            ["無圧縮ZIP.MP3へ変換・検証しています…"] = "Converting and verifying uncompressed ZIP.MP3…",
            ["無圧縮ZIP.MP3への変換が完了しました"] = "Conversion to uncompressed ZIP.MP3 completed",
            ["無圧縮ZIP.MP3へ変換できませんでした"] = "Could not convert to uncompressed ZIP.MP3",
            ["圧縮ZIP・変換可能"] = "Compressed ZIP · Convertible",
            ["圧縮ZIP.MP3です。アルバムを右クリックすると無圧縮へ変換できます"] = "Compressed ZIP.MP3: right-click the album to convert it to Store",
            ["アルバム画像"] = "Album Artwork", ["Google画像"] = "Google Images", ["画像を追加"] = "Add Image",
            ["画像を検索"] = "Find Artwork", ["画像を削除"] = "Delete Image", ["画像はありません"] = "No images",
            ["選択アルバムをGoogle画像検索で開く"] = "Search the selected album with Google Images",
            ["MusicBrainzからアルバム画像を検索"] = "Search MusicBrainz for album artwork",
            ["保存したJPGまたはPNG画像をこのアルバムへ追加"] = "Add a saved JPG or PNG image to this album",
            ["選択した貼り付け・追加・取得画像を削除"] = "Delete the selected pasted, added or downloaded image",
            ["自動スクロール"] = "Auto-scroll", ["歌詞は未読込です"] = "Lyrics have not been loaded",
            ["画像からOCR ▼"] = "OCR from Image ▼", ["クリップボード画像をOCR"] = "OCR Clipboard Image",
            ["表示中のアルバム画像をOCR"] = "OCR Displayed Artwork", ["画像ファイルをOCR"] = "OCR Image File",
            ["表示画像・Snipping Tool・画像ファイルから日本語歌詞を認識"] = "Recognize lyrics from displayed, Snipping Tool or image files",
            ["Google歌詞"] = "Google Lyrics", ["アーティスト名・曲名・アルバム名を使ってGoogleで歌詞検索"] = "Search Google using artist, title and album",
            ["再読込"] = "Reload", ["歌詞を保存"] = "Save Lyrics",
            ["LRCは時刻同期、通常歌詞は再生位置から推定して追従"] = "LRC follows timestamps; plain lyrics follow an estimated position",
            ["ドラッグしてアルバム画像と歌詞の幅を変更"] = "Drag to resize artwork and lyrics",
            ["再生速度"] = "Playback Speed", ["音程を維持"] = "Preserve Pitch",
            ["速度を変えても声や楽器の音程を保ちます"] = "Keep voices and instruments at the same pitch when changing speed",
            ["再生速度と音程維持を変更"] = "Change playback speed and pitch preservation",
            ["音量"] = "Volume", ["システム"] = "System", ["⤨ OFF"] = "⤨ OFF", ["↻ OFF"] = "↻ OFF",
            ["再生中のアルバムと曲を一覧で選択して表示"] = "Select and reveal the currently playing album and track",
            ["音楽ライブラリ"] = "Music Library", ["アプリの動作"] = "Application Behavior",
            ["データのバックアップと移行"] = "Backup and Migration", ["バックアップ作成"] = "Create Backup",
            ["バックアップから復元"] = "Restore Backup", ["＋ フォルダを追加"] = "+ Add Folder",
            ["－ 選択を登録解除"] = "- Unregister Selected", ["⇄ 選択フォルダの場所を変更"] = "⇄ Relocate Selected Folder",
            ["右上の×で終了せず、タスクバーへ最小化する"] = "Minimize to the taskbar instead of exiting with the close button",
            ["再生を続けたまま最小化します。終了は設定画面の［アプリを終了］を使用します。"] = "Keeps playback running while minimized. Use Exit Application in Settings to quit.",
            ["チェックを外したフォルダは登録を残したままアルバム一覧から非表示になります。登録解除しても元のファイルは削除されません。"] = "Unchecked folders remain registered but are hidden from the album list. Unregistering never deletes source files.",
            ["設定・ライブラリ情報・お気に入り・再生履歴・取得画像・保存歌詞を対象にします。音楽ファイルとAIモデルは含みません。"] = "Includes settings, library data, favorites, history, downloaded artwork and saved lyrics. Music files and AI models are excluded.",
            ["アプリを終了"] = "Exit Application", ["キャンセル"] = "Cancel", ["保存して閉じる"] = "Save and Close",
            ["保存して再スキャン"] = "Save and Rescan", ["表示言語"] = "Display Language", ["日本語"] = "Japanese", ["英語"] = "English",
            ["再生履歴・Usage"] = "Playback History and Usage", ["再生回数"] = "Plays", ["累計再生時間"] = "Total Play Time",
            ["曲の長さ"] = "Track Length", ["最終再生"] = "Last Played", ["閉じる"] = "Close",
            ["曲をダブルクリックすると再生します。各列の見出しをクリックすると並べ替えられます。"] = "Double-click a track to play it. Click a column header to sort.",
            ["アルバム画像を検索"] = "Search Album Artwork", ["アルバム名"] = "Album Title",
            ["検索すると、利用できるアルバム画像の候補を表示します"] = "Search to show available artwork candidates",
            ["提供: MusicBrainz / Cover Art Archive"] = "Provided by MusicBrainz / Cover Art Archive", ["この画像を使用"] = "Use This Image",
            ["未対応の形式"] = "Unsupported Format", ["再生可能"] = "Playable", ["形式未判定"] = "Unknown Format",
            ["暗号化"] = "Encrypted", ["ZIP読込エラー"] = "ZIP Read Error", ["VBR/未判定"] = "VBR/Unknown",
            ["アルバムを解析しています…"] = "Analyzing album…", ["読み込みに失敗しました"] = "Load failed",
            ["未対応形式の曲があります"] = "Some tracks use unsupported formats", ["設定を保存しました"] = "Settings saved",
            ["フォルダの表示設定を保存しました"] = "Folder visibility settings saved",
            ["ライブラリをスキャン中"] = "Scanning library", ["音楽フォルダを登録してください"] = "Register a music folder",
            ["登録フォルダからZIP.MP3・MP3・WAV・FLAC・M4Aを検索しています…"] = "Searching registered folders for ZIP.MP3, MP3, WAV, FLAC and M4A…",
            ["前回のライブラリを復元しました"] = "Previous library restored",
            ["前回終了時までに解析できたライブラリを復元しました"] = "Restored the library scanned before the previous exit",
            ["設定ファイルを読み込めませんでした"] = "Could not load the settings file",
            ["保存済みライブラリを読み込めませんでした"] = "Could not load the saved library",
            ["⤨ ON"] = "⤨ ON", ["↻ 全曲"] = "↻ ALL", ["↻ 1曲"] = "↻ ONE",
            ["シャッフル再生を切り替え"] = "Toggle shuffle", ["リピート範囲を切り替え"] = "Change repeat mode",
            ["一時停止"] = "Paused", ["⏸ 一時停止"] = "⏸ Pause", ["固定"] = "Fixed",
            ["AI環境は準備済みです"] = "AI environment is ready", ["AI機能は未準備です"] = "AI is not prepared",
            ["AI機能は準備済みです。再準備は必要ありません。"] = "AI is ready; no preparation is needed.",
            ["AI機能の準備を中止しました"] = "AI preparation was canceled", ["AI追加機能の準備が完了しました"] = "AI preparation completed",
            ["AIデータを削除しました"] = "AI data deleted", ["AI追加データを削除しました"] = "Additional AI data deleted",
            ["AIプレビューの再生が終了しました"] = "AI preview playback finished", ["AIカラオケが完成しました"] = "AI karaoke is ready",
            ["画像を開けません"] = "Cannot Open Image", ["画像を追加できません"] = "Cannot Add Image",
            ["画像を削除できません"] = "Cannot Delete Image", ["画像を保存できません"] = "Cannot Save Image",
            ["歌詞を保存できません"] = "Cannot Save Lyrics", ["歌詞OCRを実行できませんでした"] = "Lyrics OCR failed",
            ["OCRで文字を認識できませんでした"] = "OCR could not recognize any text",
            ["タスクバーへ最小化しました（設定画面から終了できます）"] = "Minimized to the taskbar (exit from Settings)"
            , ["音楽ファイルを開けません"] = "Cannot Open Music File", ["スキャンエラー"] = "Scan Error",
            ["場所を開けません"] = "Cannot Open Location", ["エクスプローラーを開けません"] = "Cannot Open Explorer",
            ["再生エラー"] = "Playback Error", ["AI準備エラー"] = "AI Preparation Error",
            ["曲を選択してください"] = "Select a Track", ["AI処理エラー"] = "AI Processing Error",
            ["AIカラオケ処理エラー"] = "AI Karaoke Processing Error", ["AIプレビュー再生エラー"] = "AI Preview Playback Error",
            ["AIデータ削除エラー"] = "AI Data Deletion Error", ["歌詞を保存"] = "Save Lyrics",
            ["歌詞OCR"] = "Lyrics OCR", ["歌詞OCRエラー"] = "Lyrics OCR Error",
            ["OCR結果の取り込み方法"] = "How to Import OCR Results", ["Google歌詞検索"] = "Google Lyrics Search",
            ["Google歌詞検索を開けません"] = "Cannot Open Google Lyrics Search", ["Google画像検索"] = "Google Images Search",
            ["画像検索を開けません"] = "Cannot Open Image Search", ["画像を追加"] = "Add Image",
            ["画像を貼り付け"] = "Paste Image", ["画像を貼り付けできません"] = "Cannot Paste Image",
            ["アルバム画像を削除"] = "Delete Album Artwork", ["復元完了"] = "Restore Complete"
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishToJapanese = JapaneseToEnglish
        .GroupBy(pair => pair.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    public static void SetLanguage(string? language) => CurrentLanguage =
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Japanese;

    public static void InitializeFromSettings(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.TryGetProperty("DisplayLanguage", out var value)) SetLanguage(value.GetString());
        }
        catch { }
    }

    public static string T(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var dictionary = IsEnglish ? JapaneseToEnglish : EnglishToJapanese;
        if (dictionary.TryGetValue(text, out var translated)) return translated;
        return IsEnglish ? TranslateDynamicToEnglish(text) : TranslateDynamicToJapanese(text);
    }

    public static string Select(string japanese, string english) => IsEnglish ? english : japanese;

    public static void Apply(Window window)
    {
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(IsEnglish ? "en-US" : "ja-JP");
        TranslateProperty(window, Window.TitleProperty);
        var visited = new HashSet<DependencyObject>();
        Visit(window, visited);
    }

    private static void Visit(DependencyObject current, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(current)) return;
        var preservesMediaText = current is FrameworkElement named
            && named.Name is "AlbumTitleText" or "NowPlayingTitleText" or "NowPlayingTagText";
        switch (current)
        {
            case HeaderedContentControl header when !BindingOperations.IsDataBound(header, HeaderedContentControl.HeaderProperty):
                TranslateProperty(header, HeaderedContentControl.HeaderProperty);
                if (!BindingOperations.IsDataBound(header, ContentControl.ContentProperty))
                    TranslateProperty(header, ContentControl.ContentProperty);
                break;
            case TextBlock text when !preservesMediaText && !BindingOperations.IsDataBound(text, TextBlock.TextProperty):
                TranslateProperty(text, TextBlock.TextProperty);
                break;
            case ContentControl content when !BindingOperations.IsDataBound(content, ContentControl.ContentProperty):
                TranslateProperty(content, ContentControl.ContentProperty);
                break;
        }
        if (current is FrameworkElement element && !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty))
        {
            TranslateProperty(element, FrameworkElement.ToolTipProperty);
            if (element.ContextMenu is not null) Visit(element.ContextMenu, visited);
        }
        if (current is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string header) column.Header = T(header);

        var visualCount = current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(current) : 0;
        for (var index = 0; index < visualCount; index++) Visit(VisualTreeHelper.GetChild(current, index), visited);
        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>()) Visit(child, visited);
    }

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is string value)
        {
            var translated = T(value);
            if (!string.Equals(value, translated, StringComparison.Ordinal)) target.SetCurrentValue(property, translated);
        }
    }

    private static string TranslateDynamicToEnglish(string text)
    {
        var result = Regex.Replace(text, @"^(\d+)件$", "$1 items");
        result = Regex.Replace(result, @"^候補 (\d+)件$", "$1 candidates");
        result = Regex.Replace(result, @"^(\d+)曲$", "$1 tracks");
        result = Regex.Replace(result, @"^表示 (\d+) / 登録 (\d+)フォルダ$", "Visible $1 / Registered $2 folders");
        result = Regex.Replace(result, @"^画像 (\d+)$", "Images $1");
        result = Regex.Replace(result, @"^再生可能 (\d+)/(\d+)曲$", "Playable $1/$2 tracks");
        result = Regex.Replace(result, @"^再生可能 (\d+)曲$", "Playable $1 tracks");
        result = Regex.Replace(result, @"(\d+)曲", "$1 tracks");
        result = Regex.Replace(result, @"(\d+)アルバム", "$1 albums");
        result = result.Replace("再生中: ", "Playing: ", StringComparison.Ordinal)
            .Replace("解析中 ", "Analyzing ", StringComparison.Ordinal)
            .Replace("完了 ", "Completed ", StringComparison.Ordinal)
            .Replace("スキャン完了: ", "Scan complete: ", StringComparison.Ordinal)
            .Replace("履歴から再生しました: ", "Playing from history: ", StringComparison.Ordinal)
            .Replace("アルバムをお気に入りに登録しました: ", "Album added to favorites: ", StringComparison.Ordinal)
            .Replace("アルバムのお気に入りを解除しました: ", "Album removed from favorites: ", StringComparison.Ordinal)
            .Replace("曲をお気に入りに登録しました: ", "Track added to favorites: ", StringComparison.Ordinal)
            .Replace("曲のお気に入りを解除しました: ", "Track removed from favorites: ", StringComparison.Ordinal)
            .Replace("歌詞を保存しました（元ファイルは変更していません）", "Lyrics saved (source file unchanged)", StringComparison.Ordinal)
            .Replace("歌詞を空欄として保存しました（元ファイルは変更していません）", "Blank lyrics saved (source file unchanged)", StringComparison.Ordinal)
            .Replace("画像をごみ箱へ移動しました: ", "Image moved to Recycle Bin: ", StringComparison.Ordinal)
            .Replace("Google画像検索を開きました: ", "Opened Google Images search: ", StringComparison.Ordinal)
            .Replace("Google歌詞検索を開きました: ", "Opened Google lyrics search: ", StringComparison.Ordinal);
        return result;
    }

    private static string TranslateDynamicToJapanese(string text)
    {
        var result = Regex.Replace(text, @"^(\d+) items$", "$1件");
        result = Regex.Replace(result, @"^(\d+) candidates$", "候補 $1件");
        result = Regex.Replace(result, @"^(\d+) tracks$", "$1曲");
        result = Regex.Replace(result, @"^Visible (\d+) / Registered (\d+) folders$", "表示 $1 / 登録 $2フォルダ");
        result = Regex.Replace(result, @"^Images (\d+)$", "画像 $1");
        result = Regex.Replace(result, @"^Playable (\d+)/(\d+) tracks$", "再生可能 $1/$2曲");
        result = Regex.Replace(result, @"^Playable (\d+) tracks$", "再生可能 $1曲");
        return result;
    }
}
