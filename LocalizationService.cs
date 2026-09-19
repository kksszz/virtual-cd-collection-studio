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
            ["◷ 履歴"] = "◷ History",
            ["操作 ▾"] = "Actions ▾",
            ["画像・歌詞の操作ボタンを表示／非表示"] = "Show or hide artwork and lyrics actions",
            ["画像の操作ボタンを表示／非表示"] = "Show or hide artwork actions",
            ["歌詞の操作ボタンを表示／非表示"] = "Show or hide lyrics actions",
            ["お気に入り"] = "Favorites", ["★ お気に入り"] = "★ Favorites",
            ["ライナーノーツ"] = "Liner Notes", ["Spine Card（帯）"] = "Spine Card (Obi)",
            ["PAGE（冊子ページ）"] = "PAGE (Booklet page)",
            ["ギャップレス再生"] = "Gapless playback",
            ["次の曲を先読みし、表示順で次のアルバムまで連続再生します。原音忠実モードの形式変更時は通常切り替えになります。"] = "Preload tracks and continue across albums in displayed order. Format changes in Source-Faithful Mode use standard transitions.",
            ["★ お気に入りの曲"] = "★ Favorite tracks", ["★ お気に入りアルバムの全曲"] = "★ Tracks from favorite albums",
            ["お気に入りの曲・アルバムを一覧から連続再生"] = "Browse and play favorite tracks and albums",
            ["曲名・アーティスト・アルバムを検索"] = "Search title, artist or album",
            ["選択した曲から再生"] = "Play from selection", ["▶ 一覧を再生"] = "▶ Play list",
            ["表示順に連続再生します"] = "Play continuously in displayed order",
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
            ["原音忠実モード"] = "Source-Faithful Mode", ["カバーフロー"] = "Cover Flow",
            ["用途"] = "Role", ["自動"] = "Auto", ["Spine付きBack"] = "Back with Spines",
            ["Front見開き（左＝裏／右＝表）"] = "Front Spread (Left Inside / Right Front)",
            ["Front縦見開き（上＝表／下＝逆さの裏）"] = "Vertical Front Spread (Top Front / Bottom Rotated Inside)",
            ["Front裏面"] = "Inside Front",
            ["トレイ"] = "Tray", ["白"] = "White", ["黒"] = "Black", ["グレー"] = "Gray", ["透明"] = "Clear",
            ["左Spine"] = "Left Spine", ["右Spine"] = "Right Spine",
            ["アルバム画像はありません"] = "No album artwork",
            ["音質加工とソフト音量を迂回し、WASAPIで原音を直接再生します"] = "Bypass sound processing and software volume; play directly through WASAPI",
            ["OFF：音質向上・EQ・音量調整を使用します"] = "OFF: Sound enhancement, EQ and volume controls are enabled",
            ["OFF（原音）"] = "OFF (Original)", ["軽め"] = "Light", ["標準"] = "Standard",
            ["強め"] = "Strong", ["劇的（強調）"] = "Dramatic", ["HDR風（実験）"] = "HDR-like (Experimental)",
            ["小音量クリア"] = "Low-Volume Clarity",
            ["音量ノーマライズ"] = "Volume Normalize",
            ["容量を再計算"] = "Refresh size",
            ["圧縮ZIPを一括で無圧縮ZIP.MP3へ変換…"] = "Convert all compressed ZIPs to uncompressed ZIP.MP3…",
            ["再生中の平均音量をゆっくり揃えます。補正には数秒かかります。元ファイルは変更しません。原音忠実モードでは無効です。"] = "Gradually matches average playback levels over several seconds. Original files are unchanged. Disabled in Source-Faithful Mode.",
            ["元ファイルを変更せず、再生音だけを補正します"] = "Enhances playback without modifying the source file",
            ["音量を上げにくい環境向け。低音・声・アタックを聴き取りやすくし、急な大音量を抑えます"] = "Improves bass, voices and attack at low volume while controlling sudden peaks",
            ["高域・輪郭・音量感を補正します。HDR風は細部・アタック・広がりも適応調整します。"] = "Enhances treble, definition and loudness. HDR-like mode also adapts detail, attack and width.",
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
            ["Wikipediaでアーティストを検索"] = "Search Artist on Wikipedia",
            ["Wikipediaでアルバムを検索"] = "Search Album on Wikipedia",
            ["Wikipediaでアーティストを検索（日本語）"] = "Search Artist on Wikipedia (Japanese)",
            ["Wikipediaでアーティストを検索（英語）"] = "Search Artist on Wikipedia (English)",
            ["Wikipediaでアルバムを検索（日本語）"] = "Search Album on Wikipedia (Japanese)",
            ["Wikipediaでアルバムを検索（英語）"] = "Search Album on Wikipedia (English)",
            ["3Dケースをフルスクリーン表示"] = "View 3D Case Full Screen",
            ["インターネットからアルバム画像を取得"] = "Download Album Artwork", ["リストから削除"] = "Remove from List",
            ["アルバムのお気に入りを登録／解除"] = "Toggle album favorite", ["曲のお気に入りを登録／解除"] = "Toggle track favorite",
            ["アーティスト"] = "Artist", ["タイトル"] = "Title", ["時間"] = "Time", ["音質"] = "Audio",
            ["年"] = "Year", ["ジャンル"] = "Genre", ["歌詞"] = "Lyrics", ["この曲には登録済みの歌詞があります"] = "Lyrics are saved for this track",
            ["ファイル"] = "File", ["ファイル名"] = "File Name", ["曲番号"] = "Track Number", ["ディスク番号"] = "Disc Number", ["ディスク総数"] = "Disc Count",
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
            ["全曲の英数字・記号・空白を半角に"] = "Half-width Letters/Digits/Symbols/Spaces in All Tracks",
            ["選択範囲の全大文字を先頭大文字に"] = "Title Case Selected All-Caps Values",
            ["選択したタイトル・アーティスト・アルバム・ジャンルのうち、すべて大文字の値を Soul Doctor のような表記へ変換します。ファイル名と数値は変更しません。"] = "Convert selected all-uppercase title, artist, album, and genre values to forms such as Soul Doctor. File names and numbers are unchanged.",
            ["タグ異常をチェック"] = "Check Tag Anomalies",
            ["曲番号の未設定・重複・欠番・突出値・ファイル名との不一致や、主要タグの欠落を確認します。ファイルは変更しません。"] = "Check for missing, duplicate, skipped, outlying, or file-name-mismatched track numbers and missing key tags. Files are not changed.",
            ["このアルバム全曲のファイル名と編集欄にある全角英数字・記号・全角スペースを半角へ変換します。日本語・半角カナ・囲み文字・ローマ数字・スペースの数は変更しません。ファイル名で使用できない半角記号は安全のため全角のまま保持します。保存するまで元ファイルは変わりません。"] = "Convert full-width Latin letters, digits, symbols, and spaces in file names and editable fields for every track. Japanese text, half-width kana, enclosed characters, Roman numerals, and the number of spaces are preserved. Symbols that would be invalid in a file name remain full-width. Files change only when saved.",
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
            ["保存"] = "Save",
            ["表示中のアルバム画像をOCR"] = "OCR Displayed Artwork", ["画像ファイルをOCR"] = "OCR Image File",
            ["表示画像・Snipping Tool・画像ファイルから日本語歌詞を認識"] = "Recognize lyrics from displayed, Snipping Tool or image files",
            ["Google歌詞"] = "Google Lyrics", ["アーティスト名・曲名・アルバム名を使ってGoogleで歌詞検索"] = "Search Google using artist, title and album",
            ["歌詞を保存"] = "Save Lyrics",
            ["LRCは時刻同期、通常歌詞は再生位置から推定して追従"] = "LRC follows timestamps; plain lyrics follow an estimated position",
            ["ドラッグしてアルバム画像と歌詞の幅を変更"] = "Drag to resize artwork and lyrics",
            ["再生速度"] = "Playback Speed", ["音程を維持"] = "Preserve Pitch",
            ["速度を変えても声や楽器の音程を保ちます"] = "Keep voices and instruments at the same pitch when changing speed",
            ["再生速度と音程維持を変更"] = "Change playback speed and pitch preservation",
            ["音量"] = "Volume", ["システム"] = "System", ["⤨ OFF"] = "⤨ OFF", ["↻ OFF"] = "↻ OFF",
            ["再生中のアルバムと曲を一覧で選択して表示"] = "Select and reveal the currently playing album and track",
            ["プロパティ"] = "Properties", ["アルバムのプロパティ"] = "Album Properties",
            ["曲のプロパティ"] = "Track Properties",
            ["場所を開く"] = "Open Location", ["パスをコピー"] = "Copy Path",
            ["情報を読み込んでいます…"] = "Loading information…",
            ["音楽ライブラリ"] = "Music Library", ["アプリの動作"] = "Application Behavior",
            ["標準：画像75%／歌詞25%"] = "Default: artwork 75% / lyrics 25%",
            ["画像重視：画像80%／歌詞20%"] = "Artwork first: artwork 80% / lyrics 20%",
            ["歌詞重視：画像60%／歌詞40%"] = "Lyrics first: artwork 60% / lyrics 40%",
            ["ドラッグで幅を調整／右クリックで割合テンプレート"] = "Drag to resize / right-click for proportion presets",
            ["画像のないアルバムのジャケットを自動取得する"] = "Automatically fetch covers for albums with no images",
            ["自動取得を一時停止（保存して反映）"] = "Pause automatic artwork (save to apply)",
            ["作品名・アーティスト名をMusicBrainzとTheAudioDBへ送信します。高解像度を優先し、600px未満は保存しません。音楽・ZIPは変更しません。"] = "Sends album and artist names to MusicBrainz and TheAudioDB. Prefers high resolution; rejects images below 600px. Music and ZIP files are never modified.",
            ["作品名・アーティスト名をMusicBrainzへ送信します。1200px版を優先し、600px未満は保存しません。音楽・ZIPは変更しません。"] = "Sends album and artist names to MusicBrainz. Requests 1200px covers; rejects images below 600px. Music and ZIP files are never modified.",
            ["データのバックアップと移行"] = "Backup and Migration", ["バックアップ作成"] = "Create Backup",
            ["バックアップから復元"] = "Restore Backup", ["＋ フォルダを追加"] = "+ Add Folder",
            ["－ 選択を登録解除"] = "- Unregister Selected", ["⇄ 選択フォルダの場所を変更"] = "⇄ Relocate Selected Folder",
            ["右上の×で終了せず、タスクバーへ最小化する"] = "Minimize to the taskbar instead of exiting with the close button",
            ["再生を続けたまま最小化します。終了は設定画面の［アプリを終了］を使用します。"] = "Keeps playback running while minimized. Use Exit Application in Settings to quit.",
            ["チェックを外したフォルダは登録を残したままアルバム一覧から非表示になります。登録解除しても元のファイルは削除されません。"] = "Unchecked folders remain registered but are hidden from the album list. Unregistering never deletes source files.",
            ["設定・ライブラリ情報・お気に入り・再生履歴・取得画像・保存歌詞を対象にします。音楽ファイルは含みません。"] = "Includes settings, library data, favorites, history, downloaded artwork and saved lyrics. Music files are excluded.",
            ["アプリを終了"] = "Exit Application", ["キャンセル"] = "Cancel", ["保存して閉じる"] = "Save and Close",
            ["保存して再スキャン"] = "Save and Rescan", ["表示言語"] = "Display Language", ["日本語"] = "Japanese", ["英語"] = "English",
            ["再生履歴・Usage"] = "Playback History and Usage", ["再生履歴ダッシュボード"] = "Playback Dashboard",
            ["これまでの再生記録を集計して表示します"] = "A summary of your listening history",
            ["ダッシュボード"] = "Dashboard", ["すべての履歴"] = "All History", ["ライブラリ変更"] = "Library Changes",
            ["累計再生時間"] = "Total Play Time", ["総再生回数"] = "Total Plays", ["記録された曲"] = "Tracked Songs",
            ["いちばん聴いた曲"] = "Most-Played Song", ["よく聴くアーティスト"] = "Top Artists",
            ["よく聴くアルバム"] = "Top Albums", ["再生時間ランキング"] = "Top Listening Time",
            ["最近聴いた曲"] = "Recently Played", ["再生回数"] = "Plays",
            ["日別・月別の推移は、今後の再生から日時別ログを記録すると表示できます。"] = "Daily and monthly trends will be available after dated listening logs are recorded.",
            ["詳細履歴の曲をダブルクリックすると再生できます。"] = "Double-click a song in All History to play it.",
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
            ["画像を開けません"] = "Cannot Open Image", ["画像を追加できません"] = "Cannot Add Image",
            ["画像を削除できません"] = "Cannot Delete Image", ["画像を保存できません"] = "Cannot Save Image",
            ["歌詞を保存できません"] = "Cannot Save Lyrics", ["歌詞OCRを実行できませんでした"] = "Lyrics OCR failed",
            ["OCRで文字を認識できませんでした"] = "OCR could not recognize any text",
            ["タスクバーへ最小化しました（設定画面から終了できます）"] = "Minimized to the taskbar (exit from Settings)"
            , ["音楽ファイルを開けません"] = "Cannot Open Music File", ["スキャンエラー"] = "Scan Error",
            ["場所を開けません"] = "Cannot Open Location", ["エクスプローラーを開けません"] = "Cannot Open Explorer",
            ["再生エラー"] = "Playback Error", ["曲を選択してください"] = "Select a Track",
            ["歌詞を保存"] = "Save Lyrics",
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
        result = Regex.Replace(result, @"^(\d+)/(\d+)件$", "$1/$2 items");
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
        result = Regex.Replace(result, @"^(\d+)/(\d+) items$", "$1/$2件");
        result = Regex.Replace(result, @"^(\d+) candidates$", "候補 $1件");
        result = Regex.Replace(result, @"^(\d+) tracks$", "$1曲");
        result = Regex.Replace(result, @"^Visible (\d+) / Registered (\d+) folders$", "表示 $1 / 登録 $2フォルダ");
        result = Regex.Replace(result, @"^Images (\d+)$", "画像 $1");
        result = Regex.Replace(result, @"^Playable (\d+)/(\d+) tracks$", "再生可能 $1/$2曲");
        result = Regex.Replace(result, @"^Playable (\d+) tracks$", "再生可能 $1曲");
        return result;
    }
}
