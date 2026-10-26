using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CubeShelf.Desktop.Controls;

/// <summary>One outgoing snapshot, compositor-paced motion, and immediate catalogue changes.</summary>
public sealed class ShelfTransitionControl : Control, IDisposable
{
    private readonly DemandAnimation _animation;
    private RenderTargetBitmap? _snapshot;
    private Control? _content;
    private ITransform? _previousTransform;
    private readonly TranslateTransform _translation = new();
    private FormattedText? _label;
    private IBrush _accent = Brushes.White;
    private double _elapsed, _direction, _previousOpacity;
    private static readonly IBrush Badge = new SolidColorBrush(Color.FromArgb(245,18,23,36));
    internal bool IsRunning => _content is not null;
    internal const double Duration = .38;

    public ShelfTransitionControl()
    {
        IsHitTestVisible = false; IsVisible = false;
        _animation = new DemandAnimation(this, Advance);
        DetachedFromVisualTree += (_,_) => Stop();
    }

    public void Capture(Control content, string console, int direction, bool reduceMotion)
    {
        Stop();
        if (reduceMotion || !content.IsEffectivelyVisible || content.Bounds.Width<2 || content.Bounds.Height<2) return;
        // Keep the snapshot bounded on high DPI/4K screens; the live incoming view stays native.
        var scale = Math.Min(1, Math.Min(1600d/content.Bounds.Width,1200d/content.Bounds.Height));
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(content.Bounds.Width*scale),
            (int)Math.Ceiling(content.Bounds.Height*scale)),new Vector(96*scale,96*scale));
        try { bitmap.Render(content); }
        catch { bitmap.Dispose(); return; }
        _snapshot = bitmap;
        _content = content; _previousTransform = content.RenderTransform; _previousOpacity = content.Opacity;
        _direction = direction; _elapsed=0;
        _label = new FormattedText(console,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default,FontStyle.Normal,FontWeight.SemiBold),22,Brushes.White);
        _accent = new SolidColorBrush(Color.Parse(console switch { "Wii"=>"#79CDE7","Switch"=>"#F65B64",_=>"#AC94FF" }));
    }

    public void Play()
    {
        if (_content is null) return;
        // Move the visible page inside its fixed clipping host, keeping the sidebar untouched.
        if (_content is Panel panel && panel.Children.FirstOrDefault(child => child.Classes.Contains("view") && child.IsVisible) is { } page)
        {
            _content=page; _previousTransform=page.RenderTransform; _previousOpacity=page.Opacity;
        }
        _content.RenderTransform = _translation;
        _content.Opacity=0; _translation.X=32*_direction;
        IsVisible=true; InvalidateVisual(); _animation.Start();
    }

    internal bool Advance(double seconds)
    {
        if (_content is null) return false;
        _elapsed=Math.Min(Duration,_elapsed+seconds);
        var p=_elapsed/Duration;
        var reveal=1-Math.Pow(1-p,3);
        _translation.X=32*_direction*(1-reveal);
        _content.Opacity=_previousOpacity*reveal;
        InvalidateVisual();
        if(p<1) return true;
        Stop(); return false;
    }

    public override void Render(DrawingContext context)
    {
        if(_snapshot is null || _label is null) return;
        var p=_elapsed/Duration;
        using(context.PushClip(new Rect(Bounds.Size)))
        using(context.PushOpacity(Math.Pow(Math.Max(0,1-p/.45),2)))
        using(context.PushTransform(Matrix.CreateTranslation(-24*_direction*p,0)))
            context.DrawImage(_snapshot,new Rect(_snapshot.Size),new Rect(Bounds.Size));
        var opacity=Math.Sin(Math.PI*p)*.95;
        using(context.PushOpacity(opacity))
        {
            var pill=new Rect((Bounds.Width-_label.Width-56)/2,Math.Max(20,Bounds.Height*.18),_label.Width+56,54);
            context.DrawRectangle(Badge,new Pen(_accent,1),pill,27,27);
            context.DrawEllipse(_accent,null,new Point(pill.X+18,pill.Center.Y),3,3);
            context.DrawText(_label,new Point(pill.X+32,pill.Center.Y-_label.Height/2));
        }
    }

    public void Stop()
    {
        _animation.Stop();
        if(_content is { } content) { content.RenderTransform=_previousTransform; content.Opacity=_previousOpacity; }
        _content=null; _previousTransform=null; _label=null;
        _snapshot?.Dispose(); _snapshot=null; IsVisible=false; _translation.X=0;
    }
    public void Dispose() => Stop();
}
