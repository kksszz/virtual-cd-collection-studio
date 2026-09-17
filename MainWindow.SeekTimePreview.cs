using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZipMp3Player;

public partial class MainWindow
{
    private string FormatPlaybackWindowTitle(ZipTrack track) =>
        string.IsNullOrWhiteSpace(track.Artist)
            ? $"{track.Title} — {_applicationTitle}"
            : $"{track.Artist} — {track.Title} — {_applicationTitle}";

    private void InitializeSeekTimePreview()
    {
        var preview = new ToolTip
        {
            PlacementTarget = PositionSlider, Placement = PlacementMode.Relative,
            VerticalOffset = -34, StaysOpen = true,
            Background = new SolidColorBrush(Color.FromRgb(30, 39, 48)),
            Foreground = Brushes.White, Padding = new Thickness(8, 4, 8, 4)
        };
        var dragging = false;
        void Update()
        {
            if (!dragging) return;
            var time = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
            preview.Content = time;
            ElapsedText.Text = time;
            var track = PositionSlider.Template.FindName("PART_Track", PositionSlider) as Track;
            var thumb = track?.Thumb;
            var point = thumb is not null ? thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, 0), PositionSlider) : new Point();
            preview.HorizontalOffset = Math.Clamp(point.X - 24, 0, Math.Max(0, PositionSlider.ActualWidth - 48));
        }
        PositionSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            dragging = true;
            Update();
            preview.IsOpen = true;
        }), true);
        PositionSlider.ValueChanged += (_, _) => Update();
        PositionSlider.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler((_, _) => Update()), true);
        PositionSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            dragging = false;
            preview.IsOpen = false;
        }), true);
        Deactivated += (_, _) => { dragging = false; preview.IsOpen = false; };
        Closed += (_, _) => { dragging = false; preview.IsOpen = false; };
        PositionSlider.Unloaded += (_, _) => { dragging = false; preview.IsOpen = false; };
    }
}
