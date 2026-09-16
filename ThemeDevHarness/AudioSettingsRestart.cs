using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZipMp3Player;

internal static partial class Program
{
    private static void VerifyAudioSettingsRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ZipMp3Player-AudioSettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR", directory);
        var path = Path.Combine(directory, "settings.json");
        // Do not run App.OnStartup: its production single-instance mutex belongs to the user's player.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var gains = new double[] { 1, -2, 3, -4, 5, -6, 7, -8, 9, -10 };
        try
        {
            foreach (var mode in new[] { "Off", "Light", "Standard", "Strong", "Dramatic", "AudioHdr" })
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    RemasterMode = mode, EqGains = gains, EqEnabled = false,
                    BassBoostEnabled = true, BassBoostAmount = 83, LowVolumeClarityEnabled = true,
                    Volume = 0.43, PlaybackSpeed = 1.25, PreservePitch = false,
                    MusicFolders = Array.Empty<string>(), AutomaticArtworkEnabled = false
                }));
                for (var restart = 0; restart < 2; restart++)
                {
                    var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                    try
                    {
                        window.Show();
                        var frame = new DispatcherFrame();
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        timer.Start(); Dispatcher.PushFrame(frame);
                        if (!window.IsLoaded) throw new Exception("Window did not load");
                        var selected = ((ComboBox)window.FindName("RemasterModeCombo")).SelectedItem as ComboBoxItem;
                        if ((string?)selected?.Tag != mode) throw new Exception("Mode reset: " + mode);
                        for (var i = 0; i < gains.Length; i++)
                            if (((Slider)window.FindName("Eq" + i)).Value != gains[i]) throw new Exception("EQ reset");
                    }
                    finally { window.Close(); }
                    using var saved = JsonDocument.Parse(File.ReadAllText(path));
                    var root = saved.RootElement;
                    if (root.GetProperty("RemasterMode").GetString() != mode
                        || root.GetProperty("EqEnabled").GetBoolean()
                        || !root.GetProperty("BassBoostEnabled").GetBoolean()
                        || root.GetProperty("BassBoostAmount").GetDouble() != 83
                        || !root.GetProperty("LowVolumeClarityEnabled").GetBoolean()
                        || root.GetProperty("Volume").GetDouble() != 0.43
                        || root.GetProperty("PlaybackSpeed").GetDouble() != 1.25
                        || root.GetProperty("PreservePitch").GetBoolean()
                        || !root.GetProperty("EqGains").EnumerateArray().Select(x => x.GetDouble()).SequenceEqual(gains))
                        throw new Exception("Saved settings changed during restore: " + mode);
                    Console.WriteLine($"PASS {mode}: startup/shutdown cycle {restart + 1}");
                }
            }
            Console.WriteLine("PASS isolated audio settings restart regression. Test data: " + directory);
        }
        finally { app.Shutdown(); }
    }
}
