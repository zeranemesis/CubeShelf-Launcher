using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

/// <summary>A compositor-paced transition; Big Picture spins its disc until launch takes focus.</summary>
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
    private bool _openCase;
    private TaskCompletionSource<bool>? _openingFinished;
    internal const double OpeningDuration = 2.1;
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
        _animation = new DemandAnimation(this, Advance);
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Start(string title, string front, string back, string spine, string status, string? gameId = null,
        string console = "gamecube", bool openCase = false, string? media = null)
    {
        Stop();
        _openCase=openCase;
        _openingFinished=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _case = new GameCaseControl { Width = openCase ? 540 : 210,
            Height = openCase ? Math.Clamp((TopLevel.GetTopLevel(this)?.Bounds.Height??720)*.48,200,430) : Math.Clamp(Bounds.Height * .48, 90, 250),
            CaseFormat=GameCaseControl.FormatForConsole(console),RealisticShell = true, UseImportedModel = true, IsInteractive = false,
            UseGpuTextures = openCase };
        _content.Children[0] = _case;
        _title.SetGame(gameId, title); _status.Text = status;
        _case.SetCover(front, back, spine);
        _case.SetShelfPose(28, false, false);
        if(openCase) { _case.SetOpeningMedia(media);_case.SetOpeningProgress(0,0); }
        _elapsed = 0;
        _shade.Opacity = .25; _content.Opacity = 0;
        _scale.ScaleX = _scale.ScaleY = .82; _offset.Y = 36;
        IsVisible = true;
        _animation.Start();
    }

    internal bool Advance(double elapsed)
    {
        _elapsed+=elapsed;
        var progress=Math.Clamp(_elapsed/.5,0,1);
        var eased=1-Math.Pow(1-progress,3);
        _shade.Opacity=.25+.75*eased;_content.Opacity=eased;
        _scale.ScaleX=_scale.ScaleY=.82+.22*eased;_offset.Y=36*(1-eased);
        if(!_openCase) return progress<1;
        static double Ease(double value) { var p=Math.Clamp(value,0,1);return p*p*(3-2*p); }
        var spinTime=Math.Max(0,_elapsed-1.05);
        var accelerating=Math.Min(spinTime,OpeningDuration-1.05);
        var rotation=720*accelerating*accelerating+1440*(OpeningDuration-1.05)*Math.Max(0,_elapsed-OpeningDuration);
        _case.SetOpeningProgress(Ease((_elapsed-.18)/.85),Ease((_elapsed-1)/.55),rotation);
        if(_elapsed>=OpeningDuration)
        {
            _openingFinished?.TrySetResult(true);
            if(_case.CaseFormat==GameCaseFormat.Switch)return false;
        }
        return IsVisible;
    }

    public Task<bool> WaitForOpeningAsync(CancellationToken cancellationToken) =>
        _openingFinished?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(false);

    public void Stop()
    {
        _animation.Stop(); IsVisible = false;
        _openingFinished?.TrySetResult(false);
        _case.Dispose();
    }

    public void Dispose() => Stop();
}
