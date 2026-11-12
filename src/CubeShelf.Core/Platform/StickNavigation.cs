namespace CubeShelf.Core.Platform;

public enum StickDirection { Neutral, Left, Right, Up, Down }

public static class StickNavigation
{
    public const int RepeatDelayMilliseconds = 400;
    public const int RepeatIntervalMilliseconds = 165;

    /// <summary>Classifies normalized axes. Positive Y points down.</summary>
    public static StickDirection Resolve(float x, float y, StickDirection held)
    {
        const float engage = .50f;
        const float release = .30f;
        if (!float.IsFinite(x) || !float.IsFinite(y)) return StickDirection.Neutral;
        var horizontal = Math.Abs(x);
        var vertical = Math.Abs(y);
        var horizontalHeld = held is StickDirection.Left or StickDirection.Right;
        var alongHeld = held switch
        {
            StickDirection.Left => -x, StickDirection.Right => x,
            StickDirection.Up => -y, StickDirection.Down => y,
            _ => 0
        };
        var crossAxis = horizontalHeld ? vertical : horizontal;
        if (alongHeld >= release && (crossAxis < engage || crossAxis < alongHeld + .23f))
            return held;
        if (Math.Max(horizontal, vertical) < engage || Math.Abs(horizontal - vertical) < .12f)
            return StickDirection.Neutral;
        if (horizontal > vertical) return x < 0 ? StickDirection.Left : StickDirection.Right;
        return y < 0 ? StickDirection.Up : StickDirection.Down;
    }
}
