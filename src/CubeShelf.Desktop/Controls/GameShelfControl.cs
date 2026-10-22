using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CubeShelf.Desktop.Controls;

public sealed record GameShelfItem(string Title, string Front, string Back, string Spine);

/// <summary>A bounded shelf of actual projected, textured cases, with no external renderer.</summary>
public sealed class GameShelfControl : Panel, IDisposable
{
    private IReadOnlyList<GameShelfItem> _items = [];
    private readonly Dictionary<int, GameCaseControl> _cases = new();
    private readonly Dictionary<int, Rect> _positions = new();
    private readonly DispatcherTimer _timer;
    private int _selected;
    private bool _ribbon = true;
    private bool _animate;
    private Size _lastLayoutSize;
    public event Action<int>? SelectionRequested;

    public GameShelfControl()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => InvalidateArrange();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PointerWheelChanged += (_, args) =>
        {
            if (_items.Count == 0) return;
            var delta = Math.Abs(args.Delta.X) > Math.Abs(args.Delta.Y) ? args.Delta.X : args.Delta.Y;
            if (Math.Abs(delta) < .01) return;
            SelectionRequested?.Invoke((_selected + (delta > 0 ? -1 : 1) + _items.Count) % _items.Count);
            args.Handled = true;
        };
    }

    public void SetGames(IReadOnlyList<GameShelfItem> items)
    {
        Clear();
        _items = items;
    }

    public void SetSelection(int selected, bool ribbon, bool animate = true)
    {
        if (_items.Count == 0) return;
        _selected = Math.Clamp(selected, 0, _items.Count - 1);
        _ribbon = ribbon;
        _animate = animate;
        var offsets = VisibleOffsets().ToArray();
        var visible = offsets.Select(IndexAt).ToHashSet();
        foreach (var key in _cases.Keys.Where(key => !visible.Contains(key)).ToArray())
        {
            var obsolete = _cases[key];
            Children.Remove(obsolete);
            obsolete.Dispose();
            _cases.Remove(key);
            _positions.Remove(key);
        }
        foreach (var offset in offsets)
        {
            var index = IndexAt(offset);
            var created = !_cases.TryGetValue(index, out var cover);
            if (created)
            {
                var item = _items[index];
                cover = new GameCaseControl { MinWidth = 0, MinHeight = 0, ShadowOpacity = .35 };
                cover.SetCover(item.Front, item.Back, item.Spine);
                AutomationProperties.SetName(cover, item.Title);
                cover.PointerPressed += (_, args) =>
                {
                    if (index != _selected) SelectionRequested?.Invoke(index);
                    args.Handled = true;
                };
                _cases[index] = cover;
                Children.Add(cover);
            }
            cover!.ZIndex = 20 - Math.Abs(offset);
            cover.Opacity = offset == 0 ? 1 : .82;
            cover.SetShelfPose(offset == 0 ? -8 : ribbon ? Math.Sign(offset) * -66 : -12,
                offset == 0, animate && !created);
        }
        InvalidateMeasure();
        if (animate) _timer.Start();
    }

    public void FlipSelected() { if (_cases.TryGetValue(_selected, out var cover)) cover.Flip(); }

    private int IndexAt(int offset) => (_selected + offset + _items.Count) % _items.Count;
    private IEnumerable<int> VisibleOffsets()
    {
        var count = Math.Min(7, _items.Count);
        for (var offset = -(count / 2); offset < count - count / 2; offset++) yield return offset;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var cover in _cases.Values) cover.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 800,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 600);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _lastLayoutSize)
        {
            _positions.Clear();
            _lastLayoutSize = finalSize;
        }
        var moving = false;
        var height = Math.Min(finalSize.Height * .94, finalSize.Width * .76);
        var width = height * .76;
        var center = finalSize.Width * .5;
        foreach (var offset in VisibleOffsets())
        {
            var index = IndexAt(offset);
            // A native resize may arrange between SetGames and SetSelection,
            // or while selection is realizing a different set of neighbors.
            if (!_cases.TryGetValue(index, out var cover)) continue;
            var scale = offset == 0 ? 1 : .79 - Math.Abs(offset) * .025;
            var spacing = _ribbon ? width * .39 : width * .77;
            var target = new Rect(center + offset * spacing - width * scale / 2,
                (finalSize.Height - height * scale) / 2, width * scale, height * scale);
            var current = _positions.GetValueOrDefault(index, target);
            var distance = Math.Abs(current.X - target.X) + Math.Abs(current.Width - target.Width) + Math.Abs(current.Y - target.Y);
            var next = !_animate || distance < .8 ? target : new Rect(
                current.X + (target.X - current.X) * .22, current.Y + (target.Y - current.Y) * .22,
                current.Width + (target.Width - current.Width) * .22, current.Height + (target.Height - current.Height) * .22);
            moving |= next != target;
            _positions[index] = next;
            cover.Arrange(next);
        }
        if (!moving) _timer.Stop();
        else if (!_timer.IsEnabled) _timer.Start();
        return finalSize;
    }

    public void Clear()
    {
        _timer.Stop();
        _items = [];
        foreach (var cover in _cases.Values) cover.Dispose();
        _cases.Clear();
        _positions.Clear();
        Children.Clear();
    }
    public void Dispose() => Clear();
}

/// <summary>Quiet contour lines drawn at the current resolution.</summary>
public sealed class ShelfContourBackground : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#DDD5D8")), 1.1);
        for (var line = -8; line < 32; line++)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(line * 72, -60), false);
                path.CubicBezierTo(new Point(line * 72 + 360, Bounds.Height * .32),
                    new Point(line * 72 - 380, Bounds.Height * .63), new Point(line * 72 + 100, Bounds.Height + 60));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }
}
