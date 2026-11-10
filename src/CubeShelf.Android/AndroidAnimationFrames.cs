using Android.Views;

namespace CubeShelf.Android;

/// <summary>One native, UI-thread vsync callback for all active shelf animations.</summary>
internal sealed class AndroidAnimationFrames : Java.Lang.Object, Choreographer.IFrameCallback
{
    private readonly Choreographer _choreographer = Choreographer.Instance!;
    private List<Action<TimeSpan>> _pending = [];
    private List<Action<TimeSpan>> _current = [];
    private bool _scheduled;
    private bool _stopped;
    public Action<long, long, long>? FrameTiming { get; set; }

    public void Request(Action<TimeSpan> callback)
    {
        if (_stopped) return;
        _pending.Add(callback);
        if (_scheduled) return;
        _scheduled = true;
        _choreographer.PostFrameCallback(this);
    }

    public void DoFrame(long frameTimeNanos)
    {
        _scheduled = false;
        if (_stopped) return;
        (_pending, _current) = (_current, _pending);
        var time = TimeSpan.FromTicks(frameTimeNanos / 100);
        var observer = FrameTiming;
        var started = observer is null ? 0 : Java.Lang.JavaSystem.NanoTime();
        try
        {
            foreach (var callback in _current) callback(time);
        }
        finally
        {
            _current.Clear();
            observer?.Invoke(frameTimeNanos, started, Java.Lang.JavaSystem.NanoTime());
        }
    }

    public void Stop()
    {
        _stopped = true;
        _choreographer.RemoveFrameCallback(this);
        _pending.Clear();
    }
}
