using System.Text;
using System.Threading;
using System.IO;
using System.Windows;

namespace ZipMp3Player;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dataDirectory = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipMp3Player");
        LocalizationService.InitializeFromSettings(Path.Combine(dataDirectory, "settings.json"));
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\ZipMp3Player.SingleInstance", out var firstInstance);
        _ownsSingleInstanceMutex = firstInstance;
        if (!firstInstance)
        {
            MessageBox.Show(LocalizationService.IsEnglish
                    ? "Virtual CD Collection Studio is already running. Please use the existing window."
                    : "Virtual CD Collection Studioはすでに起動しています。先に開いている画面をご利用ください。",
                LocalizationService.IsEnglish ? "Already Running" : "すでに起動しています",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsSingleInstanceMutex = false;
        base.OnExit(e);
    }
}
