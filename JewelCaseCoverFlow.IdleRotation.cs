using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZipMp3Player;

public sealed partial class JewelCaseCoverFlow
{
    private void InitializeIdleRotation(Panel toolbar)
    {
        var settingsPath = Path.Combine(Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipMp3Player"),
            "viewer-idle-rotation.json");
        var enabled = true;
        try { if (File.Exists(settingsPath)) enabled = JsonSerializer.Deserialize<bool>(File.ReadAllText(settingsPath)); }
        catch { /* A missing/unreadable preference must not prevent the viewer opening. */ }
        var toggle = new CheckBox
        {
            Content = LocalizationService.Select("自動回転", "Auto rotate"),
            ToolTip = LocalizationService.Select("30秒間操作しないとゆっくり回転。操作すると停止します。", "Rotate slowly after 30 seconds idle; input stops rotation."),
            IsChecked = enabled, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 4, 4, 4)
        };
        toolbar.Children.Add(toggle);
        var motion = new IdleRotationClock();
        var renderingSubscribed = false;
        void Reset() => motion.Reset(Environment.TickCount64);
        toggle.Click += (_, _) =>
        {
            enabled = toggle.IsChecked == true;
            Reset();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(enabled));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), LocalizationService.Select("自動回転の設定を保存できませんでした。\n", "Could not save auto-rotation preference.\n") + ex.Message);
            }
        };
        PreviewMouseDown += (_, _) => Reset();
        PreviewMouseUp += (_, _) => Reset();
        PreviewMouseMove += (_, _) => Reset();
        PreviewMouseWheel += (_, _) => Reset();
        PreviewKeyDown += (_, _) => Reset();
        PreviewKeyUp += (_, _) => Reset();
        PreviewTouchDown += (_, _) => Reset();
        PreviewTouchMove += (_, _) => Reset();
        void Render(object? sender, EventArgs args)
        {
            var eligible = enabled && !_backDiagnosticActive && IsVisible && !_collectionPresentation && _dxScene is not null
                && SelectedKey is not null && Window.GetWindow(this) is { IsActive: true, WindowState: not WindowState.Minimized }
                && Mouse.Captured is null && Keyboard.Modifiers == ModifierKeys.None
                && Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released
                && Mouse.MiddleButton == MouseButtonState.Released;
            var delta = motion.Step(Environment.TickCount64, eligible);
            if (delta == 0) return;
            _caseYaw = NormalizeAngle(_caseYaw + delta);
            // Only change orientation. Never rebuild the scene or reset its movable parts.
            _dxScene!.SetRotation(_caseYaw, _casePitch);
        }
        Loaded += (_, _) =>
        {
            Reset();
            if (renderingSubscribed) return;
            CompositionTarget.Rendering += Render;
            renderingSubscribed = true;
        };
        Unloaded += (_, _) =>
        {
            if (renderingSubscribed) CompositionTarget.Rendering -= Render;
            renderingSubscribed = false;
            Reset();
        };
    }
}

internal sealed class IdleRotationClock
{
    private long _lastInput;
    private long _lastFrame;
    internal void Reset(long now) { _lastInput = now; _lastFrame = now; }
    internal double Step(long now, bool eligible)
    {
        if (!eligible) { Reset(now); return 0; }
        var elapsed = Math.Clamp(now - Math.Max(_lastFrame, _lastInput + 30000), 0, 100);
        _lastFrame = now;
        return elapsed * 0.009; // Nine degrees/second: 1.5x speed, one revolution in 40 seconds.
    }
}
