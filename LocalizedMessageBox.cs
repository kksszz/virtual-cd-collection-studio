using System.Windows;

namespace ZipMp3Player;

internal static class LocalizedMessageBox
{
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption,
        MessageBoxButton button, MessageBoxImage icon) => System.Windows.MessageBox.Show(owner,
            TranslateMessage(messageBoxText), LocalizationService.T(caption), button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption,
        MessageBoxButton button, MessageBoxImage icon) => System.Windows.MessageBox.Show(
            TranslateMessage(messageBoxText), LocalizationService.T(caption), button, icon);

    private static string TranslateMessage(string message)
    {
        if (!LocalizationService.IsEnglish || string.IsNullOrWhiteSpace(message)) return message;
        var exact = message switch
        {
            "元のファイルまたはフォルダが見つかりません。" => "The source file or folder could not be found.",
            "プレビューを作る曲を曲目リストで選択してください。" => "Select a track from the track list to create a preview.",
            "カラオケを作る曲を曲目リストで選択してください。" => "Select a track from the track list to create a karaoke preview.",
            "先に「AI機能を準備」を実行してください。" => "Run Prepare AI first.",
            "先に曲を選択してください。" => "Select a track first.",
            "先にOCRするアルバム画像を表示してください。" => "Display the album image to scan with OCR first.",
            "歌詞を登録する曲を曲目リストで選択してください。" => "Select a track from the track list before adding lyrics.",
            "歌詞を検索する曲を選択してください。" => "Select a track before searching for lyrics.",
            "先にアルバムを選択してください。" => "Select an album first.",
            "貼り付け先のアルバムを選択してください。" => "Select the album that will receive the pasted image.",
            "AIデータを削除しますか？" => "Delete the AI data?",
            "AI実行環境、分離モデル、作成済みプレビューを削除します。\n必要になった時は再取得できます。" =>
                "The AI runtime, separation models and generated previews will be deleted.\nThey can be downloaded again when needed.",
            "クリップボードに画像がありません。\nSnipping Toolで範囲を切り取ってからお試しください。" =>
                "There is no image on the clipboard.\nCapture an area with Snipping Tool and try again.",
            "クリップボードに画像がありません。\nSnipping Toolで範囲を切り取ってから、もう一度お試しください。" =>
                "There is no image on the clipboard.\nCapture an area with Snipping Tool and try again.",
            "文字を認識できませんでした。\n文字部分だけを大きく切り取り、傾きや文字方向を確認してください。" =>
                "No text was recognized.\nCrop closely around the text and check its angle and orientation.",
            _ => ""
        };
        if (exact.Length > 0) return exact;

        var translated = message
            .Replace("選択した画像を削除しますか？", "Delete the selected image?", StringComparison.Ordinal)
            .Replace("ファイルはごみ箱へ移動します。", "The file will be moved to the Recycle Bin.", StringComparison.Ordinal)
            .Replace("は再生できません。", " cannot be played.", StringComparison.Ordinal)
            .Replace("バックアップを復元しました。アプリを終了しますので、もう一度起動してください。", "The backup was restored. The application will now close; start it again.", StringComparison.Ordinal)
            .Replace("復元前の安全バックアップ:", "Safety backup created before restore:", StringComparison.Ordinal);
        return translated;
    }
}
