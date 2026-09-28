using System.Windows;
using System.Windows.Media;

namespace ZipMp3Player;

public sealed class SpectrumView : FrameworkElement
{
    private float[] _bands = new float[SpectrumCaptureSampleProvider.BandCount];
    private readonly float[] _peaks = new float[SpectrumCaptureSampleProvider.BandCount];
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(19, 23, 29));
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(210, 230, 239));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(39, 47, 57));
    private static readonly Pen PeakPen = new(new SolidColorBrush(Color.FromRgb(255, 151, 32)), 2);

    public void SetBands(float[]? bands)
    {
        _bands = bands is { Length: > 0 } ? bands : new float[SpectrumCaptureSampleProvider.BandCount];
        for (var index = 0; index < _peaks.Length; index++)
        {
            var value = index < _bands.Length ? _bands[index] : 0;
            _peaks[index] = value >= _peaks[index] ? value : Math.Max(value, _peaks[index] - 0.018f);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
        if (width <= 1 || height <= 1 || _bands.Length == 0) return;

        const int segmentCount = 28;
        const double gap = 1.5;
        var columnWidth = width / _bands.Length;
        var barWidth = Math.Max(2, columnWidth * 0.78);
        var segmentHeight = Math.Max(1, (height - (segmentCount - 1) * gap) / segmentCount);
        for (var band = 0; band < _bands.Length; band++)
        {
            var x = band * columnWidth + (columnWidth - barWidth) / 2;
            var activeSegments = (int)Math.Round(Math.Clamp(_bands[band], 0, 1) * segmentCount);
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var y = height - (segment + 1) * segmentHeight - segment * gap;
                drawingContext.DrawRectangle(segment < activeSegments ? ActiveBrush : InactiveBrush, null,
                    new Rect(x, y, barWidth, segmentHeight));
            }
            var peakY = Math.Clamp(height - _peaks[band] * height, 1, height - 1);
            drawingContext.DrawLine(PeakPen, new Point(x, peakY), new Point(x + barWidth, peakY));
        }
    }
}
