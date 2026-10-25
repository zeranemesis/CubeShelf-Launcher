using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

/// <summary>A finite compositor-paced transition, shared by desktop and Android.</summary>
public sealed class GameLaunchTransition : UserControl, IDisposable
{
    private GameCaseControl _case = new() { Width = 210, Height = 250,
        RealisticShell = true, UseImportedModel = true, IsInteractive = false };
    private readonly GameTitleControl _title = new() { FontSize = 26, MaxHeight = 72, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 18, TextAlignment = TextAlignment.Center, Foreground = Brushes.LightGray };
    private readonly ScaleTransform _scale = new(.82, .82);
    private readonly TranslateTransform _offset = new(0, 36);
    private readonly StackPanel _content;
    private readonly Border _shade;
    private readonly DemandAnimation _animation;
    private double _elapsed;
    public event Action? DismissRequested;

    public GameLaunchTransition()
    {
        IsVisible = false;
        _content = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 620, Margin = new Thickness(24),
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new TransformGroup { Children = { _scale, _offset } } };
        _content.Children.Add(_case); _content.Children.Add(_title); _content.Children.Add(_status);
        var back = new Button { Content = "Retour à la bibliothèque", HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14, Padding = new Thickness(16, 10) };
        back.Click += (_, _) => { DismissRequested?.Invoke(); Stop(); };
        _content.Children.Add(back);
        _shade = new Border { Background = new SolidColorBrush(Color.Parse("#F20B101B")), Child = _content };
        Content = _shade;
        _animation = new DemandAnimation(this, elapsed =>
        {
            _elapsed += elapsed;
            var progress = Math.Clamp(_elapsed / .5, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            _shade.Opacity = .25 + .75 * eased;
            _content.Opacity = eased;
            _scale.ScaleX = _scale.ScaleY = .82 + .22 * eased;
            _offset.Y = 36 * (1 - eased);
            return progress < 1;
        });
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Start(string title, string front, string back, string spine, string status, string? gameId = null)
    {
        Stop();
        _case = new GameCaseControl { Width = 210, Height = Math.Clamp(Bounds.Height * .48, 90, 250),
            RealisticShell = true, UseImportedModel = true, IsInteractive = false };
        _content.Children[0] = _case;
        _title.SetGame(gameId, title); _status.Text = status;
        _case.SetCover(front, back, spine);
        _case.SetShelfPose(28, false, false);
        _elapsed = 0;
        _shade.Opacity = .25; _content.Opacity = 0;
        _scale.ScaleX = _scale.ScaleY = .82; _offset.Y = 36;
        IsVisible = true;
        _animation.Start();
    }

    public void Stop()
    {
        _animation.Stop(); IsVisible = false;
        _case.Dispose();
    }

    public void Dispose() => Stop();
}
