using Avalonia.Controls;

namespace CubeShelf.Desktop.Controls;

/// <summary>Runs only while moving, on compositor frames, capped at 120 updates/sec.</summary>
internal sealed class DemandAnimation(Control owner, Func<double, bool> advance)
{
    // Android supplies its UI-thread Choreographer to avoid adding another
    // composition acknowledgement round trip between custom animation frames.
    public static Action<Action<TimeSpan>>? FrameScheduler { get; set; }
    private bool _running;
    private int _generation;
    private TimeSpan? _last;
    private TimeSpan? _nextDue;
    private Action<TimeSpan>? _frameCallback;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _last = null;
        _nextDue = null;
        var generation = ++_generation;
        _frameCallback = time => Tick(generation, time);
        Schedule();
    }

    public void Stop() { _running = false; _generation++; _last = null; _frameCallback = null; }

    private void Schedule()
    {
        if (TopLevel.GetTopLevel(owner) is not { } top) { Stop(); return; }
        Action<Action<TimeSpan>> request = FrameScheduler ?? top.RequestAnimationFrame;
        if (_frameCallback is { } callback) request(callback);
    }

    private void Tick(int generation, TimeSpan time)
    {
        if (!_running || generation != _generation) return;
        var top = TopLevel.GetTopLevel(owner);
        if (top is null || !owner.IsEffectivelyVisible || top is Window { WindowState: WindowState.Minimized }) { Stop(); return; }
        var delta = _last is { } last ? (time - last).TotalSeconds : 1d / 120;
        _nextDue ??= time;
        if (time + TimeSpan.FromTicks(2500) >= _nextDue.Value)
        {
            var period = TimeSpan.FromSeconds(1d / 120);
            _nextDue += period;
            if (time - _nextDue.Value > period) _nextDue = time + period;
            _last = time;
            if (!advance(Math.Clamp(delta, 1d / 240, .05))) { Stop(); return; }
        }
        if (_running && generation == _generation) Schedule();
    }

    public static double Response(double rateAt60Hz, double elapsed) =>
        1 - Math.Pow(1 - rateAt60Hz, elapsed * 60);
}
