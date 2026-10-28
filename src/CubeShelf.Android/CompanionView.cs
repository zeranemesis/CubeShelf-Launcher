using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using CubeShelf.Assistance;

namespace CubeShelf.Android;

internal sealed class CompanionView : View
{
    private Bitmap? _atlas;
    private readonly Paint _paint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    private readonly Rect _source = new();
    private readonly RectF _target = new();
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Java.Lang.Runnable _frame;
    private long _started;
    private bool _reduceMotion;
    private CompanionState _state;
    public CompanionState State { get => _state; set { _state = value; _started = SystemClock.UptimeMillis(); Invalidate(); AnimateIfNeeded(); } }
    public bool ReduceMotion { get => _reduceMotion; set { _reduceMotion = value; _handler.RemoveCallbacks(_frame); Invalidate(); if (!value) AnimateIfNeeded(); } }

    internal CompanionView(Context context) : base(context)
    {
        _reduceMotion = !global::Android.Animation.ValueAnimator.AreAnimatorsEnabled();
        _frame = new Java.Lang.Runnable(() => { Invalidate(); AnimateIfNeeded(); });
    }
    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        using var source = Companion.OpenArtwork();
        _atlas = BitmapFactory.DecodeStream(source, null, new BitmapFactory.Options { InSampleSize = 2 });
        _started = SystemClock.UptimeMillis(); AnimateIfNeeded();
    }
    protected override void OnDetachedFromWindow()
    {
        _handler.RemoveCallbacks(_frame); _atlas?.Dispose(); _atlas = null;
        base.OnDetachedFromWindow();
    }
    protected override void OnWindowVisibilityChanged(ViewStates visibility)
    {
        base.OnWindowVisibilityChanged(visibility);
        if (_frame is null) return;
        _handler.RemoveCallbacks(_frame);
        if (visibility == ViewStates.Visible) AnimateIfNeeded();
    }
    private double Elapsed => Math.Max(0, (SystemClock.UptimeMillis() - _started) / 1000d);
    private void AnimateIfNeeded()
    {
        _handler.RemoveCallbacks(_frame);
        if (IsAttachedToWindow && IsShown && Companion.Moves(State, Elapsed, ReduceMotion)) _handler.PostDelayed(_frame, 33);
    }
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_atlas is not { } atlas) return;
        var pose = Companion.Pose(State, Elapsed, ReduceMotion);
        var cell = atlas.Width / 4;
        var scale = Math.Min((float)Width / cell, (float)(Height - 6) / atlas.Height);
        var density = Resources?.DisplayMetrics?.Density ?? 1;
        var left = (Width - cell * scale) / 2;
        var top = (Height - atlas.Height * scale) / 2 + (float)pose.Lift * density;
        _source.Set(pose.Frame * cell, 0, (pose.Frame + 1) * cell, atlas.Height);
        _target.Set(left, top, left + cell * scale, top + atlas.Height * scale);
        _paint.Color = Color.White;
        canvas.DrawBitmap(atlas, _source, _target, _paint);
        if (State is CompanionState.Listening or CompanionState.Thinking)
        {
            _paint.Color = Color.Rgb(132, 237, 206);
            for (var i = 0; i < 3; i++) canvas.DrawCircle(Width / 2f + (i - 1) * 6 * density, Height - 3 * density,
                (i == (int)(Elapsed * 3) % 3 ? 2 : 1.4f) * density, _paint);
        }
    }
}
