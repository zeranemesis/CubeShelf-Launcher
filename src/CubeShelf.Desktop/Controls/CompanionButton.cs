using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CubeShelf.Desktop.Controls;

internal sealed class CompanionButton : Button
{
    private static readonly Cursor Hand = new(StandardCursorType.Hand), Move = new(StandardCursorType.SizeAll);
    private PixelPoint _press, _origin;
    private bool _pressed, _dragged;
    public event Action<PixelPoint>? Dragged;
    public event Action? HideRequested;

    public CompanionButton()
    {
        Cursor = Hand;
        var hide = new MenuItem { Header = "Masquer Bip" };
        hide.Click += (_, _) => HideRequested?.Invoke();
        ContextMenu = new ContextMenu { ItemsSource = new[] { hide } };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Type != PointerType.Touch)
        { base.OnPointerPressed(e); return; }
        _press = this.PointToScreen(e.GetPosition(this));
        _origin = (TopLevel.GetTopLevel(this) as Window)?.Position ?? default;
        _pressed = true; _dragged = false;
        e.Pointer.Capture(this); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_pressed) { base.OnPointerMoved(e); return; }
        var point = this.PointToScreen(e.GetPosition(this));
        var x = point.X - _press.X; var y = point.Y - _press.Y;
        var threshold = 6 * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (!_dragged && x * (double)x + y * (double)y < threshold * threshold) return;
        _dragged = true; Cursor = Move;
        Dragged?.Invoke(new PixelPoint(_origin.X + x, _origin.Y + y));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_pressed || e.Pointer.Type != PointerType.Touch && e.InitialPressMouseButton != MouseButton.Left)
        { base.OnPointerReleased(e); return; }
        var click = !_dragged && new Rect(Bounds.Size).Contains(e.GetPosition(this));
        _pressed = false; e.Pointer.Capture(null); Cursor = Hand;
        e.Handled = true;
        if (click) OnClick();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _pressed = false; Cursor = Hand;
        base.OnPointerCaptureLost(e);
    }
}
