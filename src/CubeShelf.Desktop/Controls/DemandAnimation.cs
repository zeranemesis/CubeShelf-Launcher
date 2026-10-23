using Avalonia.Controls;

namespace CubeShelf.Desktop.Controls;

/// <summary>Runs only while moving, on compositor frames, capped at 120 updates/sec.</summary>
internal sealed class DemandAnimation(Control owner, Func<double, bool> advance)
{
    private bool _running;
    private int _generation;
    private TimeSpan? _last;
    private TimeSpan? _nextDue;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _last = null;
        _nextDue = null;
        Schedule(++_generation);
    }

    public void Stop() { _running = false; _generation++; _last = null; }

    private void Schedule(int generation)
    {
        if (TopLevel.GetTopLevel(owner) is not { } top) { Stop(); return; }
        top.RequestAnimationFrame(time =>
        {
            if (!_running || generation != _generation) return;
            if (!owner.IsEffectivelyVisible || top is Window { WindowState: WindowState.Minimized }) { Stop(); return; }
            var delta = _last is { } last ? (time - last).TotalSeconds : 1d / 120;
            _nextDue ??= time;
            if (time >= _nextDue.Value)
            {
                var period = TimeSpan.FromSeconds(1d / 120);
                _nextDue += period;
                if (time - _nextDue.Value > period) _nextDue = time + period;
                _last = time;
                if (!advance(Math.Clamp(delta, 1d / 240, .05))) { Stop(); return; }
            }
            Schedule(generation);
        });
    }

    public static double Response(double rateAt60Hz, double elapsed) =>
        1 - Math.Pow(1 - rateAt60Hz, elapsed * 60);
}
