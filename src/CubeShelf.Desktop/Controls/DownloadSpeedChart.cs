using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

public sealed class DownloadSpeedChart : Control
{
    public static readonly StyledProperty<IBrush?> NetworkBrushProperty =
        AvaloniaProperty.Register<DownloadSpeedChart, IBrush?>(nameof(NetworkBrush));
    public static readonly StyledProperty<IBrush?> DiskBrushProperty =
        AvaloniaProperty.Register<DownloadSpeedChart, IBrush?>(nameof(DiskBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<DownloadSpeedChart, IBrush?>(nameof(GridBrush));
    public IBrush? NetworkBrush { get => GetValue(NetworkBrushProperty); set => SetValue(NetworkBrushProperty, value); }
    public IBrush? DiskBrush { get => GetValue(DiskBrushProperty); set => SetValue(DiskBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    private readonly Queue<(double Network, double Disk)> _samples = new(Enumerable.Repeat((0d, 0d), 60));
    public double Ceiling => Math.Max(1, _samples.Max(s => Math.Max(s.Network, s.Disk)));

    static DownloadSpeedChart() => AffectsRender<DownloadSpeedChart>(NetworkBrushProperty, DiskBrushProperty, GridBrushProperty);

    public void AddSample(double network, double disk)
    {
        _samples.Dequeue();
        _samples.Enqueue((Math.Max(0, network), Math.Max(0, disk)));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var samples = _samples.ToArray();
        var max = Ceiling * 1.12;
        Point PointAt(int i, double value) => new(i * Bounds.Width / 59, Bounds.Height - value / max * (Bounds.Height - 4));
        using var clip = context.PushClip(new Rect(Bounds.Size));
        var grid = new Pen(GridBrush, 1);
        foreach (var fraction in new[] { .1, .5, .9 })
            context.DrawLine(grid, new Point(0, Bounds.Height * fraction), new Point(Bounds.Width, Bounds.Height * fraction));
        var fill = new StreamGeometry();
        using (var path = fill.Open())
        {
            path.BeginFigure(new Point(0, Bounds.Height), true);
            for (var i = 0; i < samples.Length; i++) path.LineTo(PointAt(i, samples[i].Network));
            path.LineTo(new Point(Bounds.Width, Bounds.Height));
            path.EndFigure(true);
        }
        using (context.PushOpacity(.12)) context.DrawGeometry(NetworkBrush, null, fill);
        var network = new Pen(NetworkBrush, 2);
        var disk = new Pen(DiskBrush, 1.5, DashStyle.Dash);
        for (var i = 1; i < samples.Length; i++)
        {
            context.DrawLine(network, PointAt(i - 1, samples[i - 1].Network), PointAt(i, samples[i].Network));
            context.DrawLine(disk, PointAt(i - 1, samples[i - 1].Disk), PointAt(i, samples[i].Disk));
        }
    }
}
