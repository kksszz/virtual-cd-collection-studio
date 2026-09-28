using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.Wave;

namespace ZipMp3Player;

internal sealed partial class CdImportWindow
{
    private readonly Action? beforePreview;
    private readonly Button previewSelected = new() { Content = "▶ 選択曲を試聴", Padding = new(10, 5, 10, 5) };
    private readonly Button previewStop = new() { Content = "■ 試聴停止", Padding = new(10, 5, 10, 5) };
    private readonly TextBlock previewStatus = new() { Text = "CD読込後、各曲の▶ボタンで試聴できます。", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel previewToolbar = new() { Orientation = Orientation.Horizontal, Margin = new(0, 4, 0, 6) };
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Slider previewPosition = new() { Minimum = 0, Maximum = 1, SmallChange = 1, LargeChange = 10, IsMoveToPointEnabled = true, IsEnabled = false, Margin = new(0, 5, 0, 5), ToolTip = "ドラッグして試聴の再生位置を変更" };
    private CdTrackPreviewPlayer? previewPlayer;
    private CdImportTrack? previewTrack;
    private string previewTitle = "";
    private bool previewSeeking;
    private bool previewTransition, previewCloseRequested, previewAllowClose;

    private void AddPreviewControls(Panel footer)
    {
        previewToolbar.Children.Add(previewSelected);previewToolbar.Children.Add(previewStop);
        footer.Children.Add(previewToolbar);footer.Children.Add(previewStatus);footer.Children.Add(previewPosition);
        previewSelected.Click += async (_, _) => { if (tracks.SelectedItem is CdImportTrack track) await PreviewTrack(track); };
        previewStop.Click += async (_, _) => await StopPreview();
        tracks.SelectionChanged += (_, _) => UpdatePreviewControls();
        previewPosition.PreviewMouseLeftButtonDown += (_, _) => previewSeeking = previewPosition.IsEnabled;
        previewPosition.PreviewMouseLeftButtonUp += async (_, _) => { if (previewSeeking) await SeekPreview(); };
        previewPosition.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => previewSeeking = previewPosition.IsEnabled));
        previewPosition.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(async (_, _) => { if (previewSeeking) await SeekPreview(); }));
        previewPosition.PreviewKeyDown += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) previewSeeking = previewPosition.IsEnabled; };
        previewPosition.PreviewKeyUp += async (_, _) => { if (previewSeeking) await SeekPreview(); };
        previewPosition.ValueChanged += (_, _) => { if (previewSeeking && previewPlayer is not null) previewStatus.Text = $"再生位置：{TimeSpan.FromSeconds(previewPosition.Value):mm\\:ss} / {previewPlayer.Duration:mm\\:ss}"; };
        previewTimer.Tick += (_, _) => {
            if (previewPlayer is not null && !previewSeeking) try { var position = previewPlayer.Position;previewPosition.Value = position.TotalSeconds;previewStatus.Text = $"試聴中：{previewTitle} — {position:mm\\:ss} / {previewPlayer.Duration:mm\\:ss}"; }
            catch { /* PlaybackStopped supplies the read/output error and releases the player. */ }
        };
        UpdatePreviewControls();
    }

    private void AddPreviewColumn()
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(Button.ContentProperty, "▶ 試聴");button.SetValue(Button.ToolTipProperty, "この曲をCDから試聴");button.SetValue(Button.PaddingProperty, new Thickness(6, 2, 6, 2));
        button.SetValue(FrameworkElement.MinWidthProperty, 58d);
        button.SetBinding(Button.IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = previewToolbar });
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (sender, e) => {
            e.Handled = true;
            if (sender is Button { DataContext: CdImportTrack track }) await PreviewTrack(track);
        }));
        tracks.Columns.Insert(2, new DataGridTemplateColumn { Header = "試聴", Width = 80, MinWidth = 80, IsReadOnly = true, CellTemplate = new DataTemplate { VisualTree = button } });
    }

    private void UpdatePreviewControls()
    {
        bool ready = !busy && !previewTransition && !previewCloseRequested;
        controls.IsEnabled = ready;
        eject.IsEnabled = ready && drives.SelectedItem is string;
        start.IsEnabled = ready && disc is not null;
        previewToolbar.IsEnabled = ready && disc is not null;
        previewSelected.IsEnabled = ready && disc is not null && tracks.SelectedItem is CdImportTrack;
        previewStop.IsEnabled = ready && previewPlayer is not null;
        previewPosition.IsEnabled = ready && previewPlayer is not null;
    }

    private async Task PreviewTrack(CdImportTrack track, TimeSpan position = default)
    {
        if (busy || previewTransition || previewCloseRequested || disc is null) return;
        previewTransition = true;UpdatePreviewControls();
        try
        {
            await StopPreviewCore();
            beforePreview?.Invoke();
            previewStatus.Text = $"曲 {track.Number} を読み込んでいます…";
            var expected = disc!;
            previewPlayer = await Task.Run(() => CdTrackPreviewPlayer.Open(loadedDrive, expected, track.Number, position));
            if (previewCloseRequested) { await StopPreviewCore();return; }
            previewTitle = $"{track.Number}. {track.Title}";
            previewTrack = track;previewPosition.Maximum = previewPlayer.Duration.TotalSeconds;previewPosition.Value = previewPlayer.Position.TotalSeconds;
            previewPlayer.Stopped += PreviewStopped;
            previewPlayer.Play();previewTimer.Start();
            previewStatus.Text = $"試聴中：{previewTitle}";
        }
        catch (Exception ex) { try { await StopPreviewCore(); } catch { } previewStatus.Text = "試聴できませんでした：" + ex.Message; }
        finally { EndPreviewTransition(); }
    }

    private void PreviewStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(async () => {
            if (!ReferenceEquals(sender, previewPlayer) || busy || previewTransition) return;
            previewTransition = true;UpdatePreviewControls();
            try { await StopPreviewCore();previewStatus.Text = e.Exception is null ? "試聴が終了しました：" + previewTitle : "試聴を中断しました：" + e.Exception.Message; }
            catch (Exception ex) { previewStatus.Text = "試聴を停止しました：" + ex.Message; }
            finally { EndPreviewTransition(); }
        }));
    }

    private async Task StopPreviewCore()
    {
        previewTimer.Stop();
        previewSeeking = false;previewTrack = null;previewPosition.Value = 0;
        var player = previewPlayer;previewPlayer = null;
        if (player is not null) { player.Stopped -= PreviewStopped;await Task.Run(player.Dispose); }
        previewStatus.Text = "試聴停止";
    }

    private async Task SeekPreview()
    {
        previewSeeking = false;
        if (previewPlayer is null || previewTrack is null || previewTransition || busy) return;
        var track = previewTrack;var position = TimeSpan.FromSeconds(previewPosition.Value);
        await PreviewTrack(track, position);
    }

    private async Task StopPreview()
    {
        if (busy || previewTransition) return;
        previewTransition = true;UpdatePreviewControls();
        try { await StopPreviewCore(); }
        catch (Exception ex) { previewStatus.Text = "試聴を停止しました：" + ex.Message; }
        finally { EndPreviewTransition(); }
    }

    private void EndPreviewTransition()
    {
        previewTransition = false;UpdatePreviewControls();
        if (previewCloseRequested) { previewAllowClose = true;Close(); }
    }

    private async void PreviewWindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true;cancellation?.Cancel();status.Text = "処理の終了を待っています。";return; }
        if (previewAllowClose) return;
        if (previewTransition) { e.Cancel = true;previewCloseRequested = true;previewStatus.Text = "試聴の停止を待っています…";return; }
        if (previewPlayer is not null) { e.Cancel = true;previewCloseRequested = true;await StopPreview(); }
    }
}
