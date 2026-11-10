using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CubeShelf.Assistance;

namespace CubeShelf.Desktop.Controls;

internal sealed class CompanionControl : Control, IDisposable
{
    private readonly Bitmap _atlas;
    private readonly DemandAnimation _animation;
    private double _elapsed;
    private CompanionState _state;
    private bool _reduceMotion;
    private double _lastFrame;
    private bool _disposed;
    public CompanionState State
    {
        get => _state;
        set { _state = value; _elapsed = _lastFrame = 0; InvalidateVisual(); StartMotion(); }
    }
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set { _reduceMotion = value; if (value) _animation.Stop(); else StartMotion(); InvalidateVisual(); }
    }
    public CompanionControl()
    {
        using var source = Companion.OpenArtwork();
        _atlas = Bitmap.DecodeToHeight(source, 256);
        _animation = new DemandAnimation(this, delta =>
        {
            _elapsed += delta;
            if (_elapsed - _lastFrame >= 1d / 30) { _lastFrame = _elapsed; InvalidateVisual(); }
            return Companion.Moves(State, _elapsed, ReduceMotion);
        });
        AttachedToVisualTree += (_, _) => StartMotion();
        DetachedFromVisualTree += (_, _) => _animation.Stop();
        Unloaded += (_, _) => _animation.Stop();
    }
    private void StartMotion() { if (!_disposed && !ReduceMotion && IsEffectivelyVisible) _animation.Start(); }
    public void Dispose() { if (_disposed) return; _disposed = true; _animation.Stop(); _atlas.Dispose(); }
    public override void Render(DrawingContext context)
    {
        if (_disposed) return;
        var pose = Companion.Pose(State, _elapsed, ReduceMotion);
        var width = _atlas.Size.Width / 4;
        var scale = Math.Min(Bounds.Width / width, (Bounds.Height - 6) / _atlas.Size.Height);
        if (scale <= 0) return;
        var target = new Rect((Bounds.Width - width * scale) / 2,
            (Bounds.Height - _atlas.Size.Height * scale) / 2 + pose.Lift, width * scale, _atlas.Size.Height * scale);
        if (State is CompanionState.Listening or CompanionState.Thinking)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(90 + pose.Pulse * 90), 132, 237, 206));
            for (var i = 0; i < 3; i++) context.DrawEllipse(brush, null,
                new Point(Bounds.Width * .5 + (i - 1) * 7, Bounds.Height - 3), 1.5 + (i == (int)(_elapsed * 3) % 3 ? 1 : 0), 1.5);
        }
        context.DrawImage(_atlas, new Rect(pose.Frame * width, 0, width, _atlas.Size.Height), target);
    }
}
