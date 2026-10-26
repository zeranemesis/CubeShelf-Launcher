using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CubeShelf.Desktop.Controls;

/// <summary>Lightweight, stationary case for library cards; shares the decoded thumbnail.</summary>
public sealed class GameCoverControl : Control
{
    public static readonly StyledProperty<Bitmap?> CoverProperty = AvaloniaProperty.Register<GameCoverControl, Bitmap?>(nameof(Cover));
    public static readonly StyledProperty<GameCaseFormat> FormatProperty = AvaloniaProperty.Register<GameCoverControl, GameCaseFormat>(nameof(Format));
    public Bitmap? Cover { get => GetValue(CoverProperty); set => SetValue(CoverProperty, value); }
    public GameCaseFormat Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0));
    private static readonly IBrush DarkShell = new SolidColorBrush(Color.Parse("#151518"));
    private static readonly IBrush WhiteShell = new SolidColorBrush(Color.Parse("#F5F5F2"));
    private static readonly IBrush ClearShell = new SolidColorBrush(Color.Parse("#C1C6CD"));
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)), .65);
    private static readonly IPen Seam = new Pen(new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), .8);
    private static readonly IBrush Sleeve = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .4, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(Color.FromArgb(14,255,255,255),0),new(Colors.Transparent,.28),new(Color.FromArgb(9,255,255,255),.5),new(Colors.Transparent,.7) }
    };
    static GameCoverControl() => AffectsRender<GameCoverControl>(CoverProperty, FormatProperty);
    public GameCoverControl() => RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.HighQuality);
    public override void Render(DrawingContext context)
    {
        var box = new Rect(3, 2, Math.Max(0, Bounds.Width - 6), Math.Max(0, Bounds.Height - 8));
        if (box.Width < 2 || box.Height < 2) return;
        context.DrawRectangle(Shadow, null, box.Translate(new Vector(0, 4)).Inflate(1), 3, 3);
        var shell = Format switch { GameCaseFormat.Wii => WhiteShell, GameCaseFormat.Switch => ClearShell, _ => DarkShell };
        context.DrawRectangle(shell, Edge, box, 2.5, 2.5);
        var hinge = Format == GameCaseFormat.Switch ? 3 : 5;
        var artwork = new Rect(box.X + hinge + 2, box.Y + 3, Math.Max(1,box.Width-hinge-5), Math.Max(1,box.Height-6));
        if(Cover is { } cover)
        {
            // Uniform fit preserves the complete printed insert, including platform/rating marks.
            var scale = Math.Min(artwork.Width / cover.Size.Width, artwork.Height / cover.Size.Height);
            var size = cover.Size * scale;
            var target = new Rect(artwork.Center.X-size.Width/2,artwork.Center.Y-size.Height/2,size.Width,size.Height);
            context.DrawImage(cover,new Rect(cover.Size),target);
            context.DrawRectangle(Sleeve,null,target);
        }
        context.DrawLine(Seam,new Point(box.X+hinge,box.Y+3),new Point(box.X+hinge,box.Bottom-3));
        context.DrawLine(Edge,new Point(box.X+hinge+1,box.Y+3),new Point(box.X+hinge+1,box.Bottom-3));
        context.DrawLine(Edge,new Point(box.X+2,box.Y+1),new Point(box.Right-2,box.Y+1));
    }
}
