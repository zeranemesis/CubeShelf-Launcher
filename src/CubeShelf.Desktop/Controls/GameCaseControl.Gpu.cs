using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameCaseControl
{
    public static bool PreferGpuTextures { get; set; }
    internal static bool BatchGpuDraws { get; set; } = true;
    internal static Action<double, long>? FrontendTiming { get; set; }
    internal static Action<double>? BackendTiming { get; set; }
    private readonly List<MeshDraw> _gpuBatch = new(5);
    private readonly Dictionary<int, (SKPoint[] Positions, SKColor[] Colors)> _shellBuffers = new();
    private readonly Dictionary<(int Columns, int Rows), (SKPoint[] Positions, SKPoint[] Texture, ushort[] Indices, PixelSize TextureSize)> _textureBuffers = new();

    private void DrawGpuShell(DrawingContext context, ModelDraw[] commands, int start, int end)
    {
        var layer = commands[start].Layer;
        var length = (end - start) * 3;
        if (!_shellBuffers.TryGetValue(layer, out var buffers) || buffers.Positions.Length != length)
            _shellBuffers[layer] = buffers = (new SKPoint[length], new SKColor[length]);
        var (positions, colors) = buffers;
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        var next = 0;
        for (var index = start; index < end; index++)
        {
            var command = commands[index];
            var color = ((ISolidColorBrush)command.Brush).Color;
            var shade = new SKColor(color.R, color.G, color.B, color.A);
            foreach (var point in command.Points)
            {
                positions[next] = new SKPoint((float)point.X, (float)point.Y);
                colors[next++] = shade;
                minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
            }
        }
        var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, colors);
        QueueGpuDraw(context, new MeshDraw(new Rect(minX, minY, maxX - minX, maxY - minY),
            vertices, new SKPaint { IsAntialias = true }));
    }

    private bool DrawGpuTexture(DrawingContext context, Bitmap image, Point[] quad, int columns, int rows)
    {
        var paint = CoverBitmapCache.CreateTexturePaint(image);
        if (paint is null) return false;
        if (!_textureBuffers.TryGetValue((columns, rows), out var buffers))
        {
            var length = (columns + 1) * (rows + 1);
            buffers = (new SKPoint[length], new SKPoint[length], new ushort[columns * rows * 6], default);
            var nextIndex = 0;
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var a = (ushort)(row * (columns + 1) + column);
                    var b = (ushort)(a + 1);
                    var c = (ushort)(a + columns + 1);
                    var d = (ushort)(c + 1);
                    buffers.Indices[nextIndex++] = a; buffers.Indices[nextIndex++] = b; buffers.Indices[nextIndex++] = c;
                    buffers.Indices[nextIndex++] = b; buffers.Indices[nextIndex++] = d; buffers.Indices[nextIndex++] = c;
                }
            _textureBuffers[(columns, rows)] = buffers;
        }
        var size = image.PixelSize;
        if (buffers.TextureSize != size)
        {
            for (var row = 0; row <= rows; row++)
                for (var column = 0; column <= columns; column++)
                    buffers.Texture[row * (columns + 1) + column] = new SKPoint(
                        (float)((double)size.Width * column / columns), (float)((double)size.Height * row / rows));
            buffers.TextureSize = size;
            _textureBuffers[(columns, rows)] = buffers;
        }
        var positions = buffers.Positions;
        for (var row = 0; row <= rows; row++)
        {
            // A bilinear quad is linear across each row: calculate its edges
            // once rather than interpolating all four corners at every vertex.
            var v = (double)row / rows;
            var left = Lerp(quad[0], quad[3], v);
            var right = Lerp(quad[1], quad[2], v);
            var stepX = (right.X - left.X) / columns;
            var stepY = (right.Y - left.Y) / columns;
            for (var column = 0; column <= columns; column++)
            {
                var index = row * (columns + 1) + column;
                positions[index] = new SKPoint((float)(left.X + stepX * column), (float)(left.Y + stepY * column));
            }
        }
        var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, buffers.Texture, null, buffers.Indices);
        QueueGpuDraw(context, new MeshDraw(ComputeBounds(quad), vertices, paint));
        return true;
    }

    private void QueueGpuDraw(DrawingContext context, MeshDraw mesh)
    {
        _gpuBatch.Add(mesh);
        if (!BatchGpuDraws) FlushGpuDraws(context);
    }

    private void FlushGpuDraws(DrawingContext context)
    {
        if (_gpuBatch.Count == 0) return;
        var bounds = _gpuBatch[0].Bounds;
        for (var index = 1; index < _gpuBatch.Count; index++) bounds = bounds.Union(_gpuBatch[index].Bounds);
        var operation = new CaseDraw(bounds, _gpuBatch.ToArray());
        _gpuBatch.Clear();
        try { context.Custom(operation); }
        catch { operation.Dispose(); throw; }
    }

    private void DiscardGpuDraws()
    {
        foreach (var mesh in _gpuBatch) { mesh.Vertices.Dispose(); mesh.Paint.Dispose(); }
        _gpuBatch.Clear();
    }

    private readonly record struct MeshDraw(Rect Bounds, SKVertices Vertices, SKPaint Paint);

    private sealed class CaseDraw(Rect bounds, MeshDraw[] meshes) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Render(ImmediateDrawingContext context)
        {
            var timing = BackendTiming;
            var started = timing is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using (var lease = feature.Lease())
            {
                var opacity = (byte)Math.Clamp(Math.Round(lease.CurrentOpacity * 255), 0, 255);
                foreach (var mesh in meshes)
                {
                    mesh.Paint.Color = SKColors.White.WithAlpha(opacity);
                    lease.SkCanvas.DrawVertices(mesh.Vertices, SKBlendMode.Modulate, mesh.Paint);
                }
            }
            timing?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        public void Dispose()
        {
            foreach (var mesh in meshes) { mesh.Vertices.Dispose(); mesh.Paint.Dispose(); }
        }
    }
}
