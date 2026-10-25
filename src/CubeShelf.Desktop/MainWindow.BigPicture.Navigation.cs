using Avalonia.Controls;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly Stack<(Control View, Control? Focus)> _bigPictureBackStack = new();
    private Control? _bigPictureNavigationView;
    private Control? _bigPictureLastFocusedControl;
    private bool _bigPictureNavigatingBack;

    private void RecordBigPictureNavigation(Control view)
    {
        if (!_bigPictureActive)
        {
            _bigPictureBackStack.Clear();
            _bigPictureNavigationView = null;
            return;
        }
        if (ReferenceEquals(view, _bigPictureNavigationView)) return;
        if (view == BigPictureView || view == BigPictureHomeView)
            _bigPictureBackStack.Clear();
        else if (!_bigPictureNavigatingBack && _bigPictureNavigationView is { } previous)
        {
            // Revisiting a page trims the existing route rather than growing a cycle.
            if (_bigPictureBackStack.Any(entry => ReferenceEquals(entry.View, view)))
            {
                while (_bigPictureBackStack.TryPop(out var entry))
                    if (ReferenceEquals(entry.View, view)) break;
            }
            else _bigPictureBackStack.Push((previous, _bigPictureLastFocusedControl));
        }
        _bigPictureNavigationView = view;
        _bigPictureLastInput = -1;
    }

    private bool TryReturnBigPicturePage()
    {
        if (!_bigPictureBackStack.TryPop(out var previous)) return false;
        _bigPictureNavigatingBack = true;
        try
        {
            if (previous.View == BigPictureView) RefreshBigPictureGame();
            ShowParityView(previous.View);
            if (previous.Focus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Focusable: true } focus)
            {
                focus.Focus();
                focus.BringIntoView();
            }
            else if (previous.View == BigPictureView) FocusBigPictureAction(0);
            else
            {
                var first = BigPicturePageControls().FirstOrDefault();
                first?.Focus();
                first?.BringIntoView();
            }
        }
        finally { _bigPictureNavigatingBack = false; }
        return true;
    }
}
