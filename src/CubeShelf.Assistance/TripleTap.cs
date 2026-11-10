namespace CubeShelf.Assistance;

/// <summary>A monotonic clock avoids wall-clock changes and requires three distinct taps.</summary>
public sealed class TripleTap
{
    private long _first = -1, _last = -1;
    private int _count;
    public bool Tap(long milliseconds)
    {
        if (_first < 0 || milliseconds < _last || milliseconds - _first > 900)
        { _first = milliseconds; _count = 0; }
        if (_last >= 0 && milliseconds - _last is >= 0 and < 40) return false;
        _last = milliseconds;
        if (++_count < 3) return false;
        _first = _last = -1;
        _count = 0;
        return true;
    }
}
