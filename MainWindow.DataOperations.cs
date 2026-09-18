using System.Windows;

namespace ZipMp3Player;

public partial class MainWindow
{
    private readonly DataOperationGate _dataOperations = new();
    private void InitializeDataOperationProtection()
    {
        _dataOperations.Drained += () => Dispatcher.BeginInvoke(new Action(() => { _forceClose = true; Close(); }));
        Application.Current.SessionEnding += (_, e) =>
        {
            if (_dataOperations.ActiveCount > 0)
            { e.Cancel = true; DeferCloseForDataOperations(); }
        };
    }
    private void DeferCloseForDataOperations()
    {
        _dataOperations.RequestClose();
        _automaticArtworkTimer?.Stop();
        _automaticArtworkCancellation?.Cancel();
        IsEnabled = false;
        Title = LocalizationService.Select("データ処理の完了後に終了します", "Closing after data processing completes");
        StatusText.Text = LocalizationService.Select("保存・検証・後処理の完了を待っています。強制終了しないでください。",
            "Waiting for save, verification and cleanup. Please do not force quit.");
    }
}
