using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CubeShelf.Desktop.Controls;

/// <summary>Cached GameCube artwork and resolution-independent console catalogue marks.</summary>
public sealed class ConsoleBrandMark : Control
{
    public static readonly StyledProperty<string> PlatformProperty = AvaloniaProperty.Register<ConsoleBrandMark,string>(nameof(Platform),"gamecube");
    public string Platform { get=>GetValue(PlatformProperty);set=>SetValue(PlatformProperty,value); }
    private static readonly Lazy<Bitmap> Cube = new(()=>
    {
        using var stream=AssetLoader.Open(new Uri($"avares://{typeof(ConsoleBrandMark).Assembly.GetName().Name}/Assets/Brand/gamecube_logo.png"));
        return new Bitmap(stream);
    });
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E60012"));
    private static readonly IBrush WiiBackground = new SolidColorBrush(Color.Parse("#F2F3F1"));
    private static readonly FormattedText Wii = new("Wii",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
        new Typeface("Arial",FontStyle.Normal,FontWeight.Normal),58,new SolidColorBrush(Color.Parse("#929797")));
    private static readonly Geometry LeftJoyCon = Geometry.Parse("M44,19 L32,19 C22,19 17,26 17,36 L17,64 C17,75 23,81 33,81 L44,81 Z");
    private static readonly Geometry RightJoyCon = Geometry.Parse("M56,19 L68,19 C78,19 83,26 83,36 L83,64 C83,75 77,81 67,81 L56,81 Z");
    private static readonly IPen WhiteOutline = new Pen(Brushes.White,4.5);
    private static readonly IBrush N64GreenBrush = new SolidColorBrush(Color.Parse("#00A650"));
    private static readonly IBrush N64BlueBrush = new SolidColorBrush(Color.Parse("#244CA1"));
    private static readonly IBrush N64YellowBrush = new SolidColorBrush(Color.Parse("#FFC526"));
    private static readonly Geometry N64Green = Geometry.Parse("M12,29 L31,20 L54,56 L54,10 L73,19 L73,83 L54,93 L31,56 L31,82 L12,73 Z");
    private static readonly Geometry N64Blue = Geometry.Parse("M31,20 L48,29 L70,65 L70,27 L88,35 L88,73 L73,83 L54,52 L54,93 L37,85 L37,47 Z");
    private static readonly Geometry N64Yellow = Geometry.Parse("M12,29 L31,20 L48,29 L31,38 Z M54,10 L73,19 L88,35 L70,27 Z");
    private static readonly Geometry N64Red = Geometry.Parse("M31,38 L37,47 L37,85 L31,82 Z M54,10 L54,52 L48,43 L48,29 Z");
    static ConsoleBrandMark()
    {
        AffectsRender<ConsoleBrandMark>(PlatformProperty);
        PlatformProperty.Changed.AddClassHandler<ConsoleBrandMark>((mark,_)=>mark.UpdateAccessibleName());
    }
    public ConsoleBrandMark() { IsHitTestVisible=false;UpdateAccessibleName(); }
    private void UpdateAccessibleName()=>AutomationProperties.SetName(this,"Nintendo "+(Platform switch {"wii"=>"Wii","switch"=>"Switch","n64"=>"64",_=>"GameCube"}));
    public override void Render(DrawingContext context)
    {
        var size=Math.Min(Bounds.Width,Bounds.Height);
        if(size<=0)return;
        using(context.PushTransform(Matrix.CreateScale(size/100,size/100)*Matrix.CreateTranslation((Bounds.Width-size)/2,(Bounds.Height-size)/2)))
        {
            if(Platform=="n64")
            {
                context.DrawGeometry(N64GreenBrush,null,N64Green);
                context.DrawGeometry(N64BlueBrush,null,N64Blue);
                context.DrawGeometry(N64YellowBrush,null,N64Yellow);
                context.DrawGeometry(Red,null,N64Red);
            }
            else if(Platform=="wii")
            {
                context.DrawRectangle(WiiBackground,null,new Rect(0,0,100,100),18,18);
                context.DrawText(Wii,new Point((100-Wii.Width)/2,(100-Wii.Height)/2));
            }
            else if(Platform=="switch")
            {
                context.DrawRectangle(Red,null,new Rect(0,0,100,100),18,18);
                context.DrawGeometry(null,WhiteOutline,LeftJoyCon);
                context.DrawGeometry(Brushes.White,null,RightJoyCon);
                context.DrawEllipse(Brushes.White,null,new Point(31,39),5,5);
                context.DrawEllipse(Red,null,new Point(69,61),5,5);
            }
            else
            {
                var cube=Cube.Value;
                context.DrawImage(cube,new Rect(cube.Size),new Rect(0,0,100,100));
            }
        }
    }
}
