using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CubeShelf.Mobile;

/// <summary>Redraws only for real samples, without an idle animation timer.</summary>
internal sealed class DownloadRateChart : Control
{
    private IReadOnlyList<DownloadRateSample> _samples = Array.Empty<DownloadRateSample>();
    private DateTimeOffset _lastAt;
    private int _count;
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#A18AFF"));
    private static readonly Pen Line = new(Accent, 2);
    private static readonly Pen GridLine = new(new SolidColorBrush(Color.Parse("#2D394E")), 1);
    public void SetSamples(IReadOnlyList<DownloadRateSample> samples)
    {
        var last = samples.Count == 0 ? default : samples[^1].At;
        if (last == _lastAt && samples.Count == _count) return;
        _samples = samples; _lastAt = last; _count = samples.Count; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width; var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;
        for (var row = 1; row <= 3; row++)
            context.DrawLine(GridLine, new Point(0, row * height / 4), new Point(width, row * height / 4));
        if (_samples.Count < 2) return;
        var end = _samples[^1].At;
        var start = end - TimeSpan.FromSeconds(60);
        var visible = _samples.Where(sample => sample.At >= start).ToArray();
        if (visible.Length < 2) return;
        var maximum = Math.Max(1048576, visible.Max(sample => sample.BytesPerSecond) * 1.15);
        Point At(DownloadRateSample sample) => new(Math.Clamp((sample.At - start).TotalSeconds / 60, 0, 1) * width,
            height - Math.Clamp(sample.BytesPerSecond / maximum, 0, 1) * (height - 4));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(At(visible[0]).X, height), true);
            foreach (var sample in visible) path.LineTo(At(sample));
            path.LineTo(new Point(width, height)); path.EndFigure(true);
        }
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using (context.PushOpacity(.14)) context.DrawGeometry(Accent, null, geometry);
        for (var index = 1; index < visible.Length; index++) context.DrawLine(Line, At(visible[index - 1]), At(visible[index]));
    }
}
