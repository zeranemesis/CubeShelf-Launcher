using System.Globalization;

namespace CubeShelf.Android;

/// <summary>Opt-in measurements, emitted every 240 active callbacks, never per frame.</summary>
internal sealed class RenderProfiler
{
    private readonly object _gate = new();
    private int _frames, _lateFrames, _frontCases, _backCalls;
    private double _lateMax, _tickMax, _frontMs, _frontMax, _backMs, _backMax;
    private long _bytes;

    public void Frontend(double milliseconds, long bytes)
    {
        lock (_gate)
        {
            _frontCases++; _frontMs += milliseconds; _bytes += bytes;
            _frontMax = Math.Max(_frontMax, milliseconds);
        }
    }

    public void Backend(double milliseconds)
    {
        lock (_gate)
        {
            _backCalls++; _backMs += milliseconds;
            _backMax = Math.Max(_backMax, milliseconds);
        }
    }

    public void Vsync(long frameTime, long started, long ended)
    {
        string? report = null;
        lock (_gate)
        {
            var late = Math.Max(0, (started - frameTime) / 1_000_000d);
            _frames++;
            if (late >= 8.333) _lateFrames++;
            _lateMax = Math.Max(_lateMax, late);
            _tickMax = Math.Max(_tickMax, (ended - started) / 1_000_000d);
            if (_frames >= 240)
            {
                report = string.Create(CultureInfo.InvariantCulture,
                    $"frames={_frames} lateFrames={_lateFrames} lateMaxMs={_lateMax:F3} tickMaxMs={_tickMax:F3} frontCases={_frontCases} frontMsPerFrame={_frontMs / _frames:F3} frontMaxMs={_frontMax:F3} bytesPerFrame={_bytes / _frames} backCalls={_backCalls} backMsPerFrame={_backMs / _frames:F3} backMaxMs={_backMax:F3}");
                _frames = _lateFrames = _frontCases = _backCalls = 0;
                _lateMax = _tickMax = _frontMs = _frontMax = _backMs = _backMax = 0;
                _bytes = 0;
            }
        }
        if (report is not null) global::Android.Util.Log.Info("CubeShelf.Perf", report);
    }
}
