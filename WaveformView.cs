using System.Windows;
using System.Windows.Media;

namespace ZipMp3Player;

public sealed class WaveformView : FrameworkElement
{
    private float[] _samples = [];
    private static readonly Pen WavePen = new(new SolidColorBrush(Color.FromRgb(60, 180, 230)), 1.4);
    private static readonly Pen CenterPen = new(new SolidColorBrush(Color.FromArgb(80, 130, 145, 160)), 1);

    public void SetSamples(float[]? samples)
    {
        _samples = samples ?? [];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth; var height = ActualHeight; var center = height / 2;
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(19, 23, 29)), null, new Rect(0, 0, width, height));
        drawingContext.DrawLine(CenterPen, new Point(0, center), new Point(width, center));
        if (_samples.Length < 2 || width <= 1 || height <= 1) return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < _samples.Length; i++)
            {
                var x = i * width / (_samples.Length - 1);
                var amplitude = Math.Min(1, _samples[i]) * (center - 3);
                context.BeginFigure(new Point(x, center - amplitude), false, false);
                context.LineTo(new Point(x, center + amplitude), true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, WavePen, geometry);
    }
}
