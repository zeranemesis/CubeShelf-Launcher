using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

/// <summary>Quiet, static lighting shared by both HOME palettes.</summary>
internal sealed class HomeMenuBackdrop : Control
{
    public bool Banner { get; init; }
    private bool _light=true;
    public bool LightPalette { get=>_light;set{if(_light==value)return;_light=value;InvalidateVisual();} }
    public HomeMenuBackdrop(){IsHitTestVisible=false;}
    public override void Render(DrawingContext context)
    {
        if(Bounds.Width<=0||Bounds.Height<=0)return;
        var background=new LinearGradientBrush
        {
            StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),
            GradientStops=LightPalette
                ?[new GradientStop(Color.Parse(Banner?"#FFFFFFFF":"#F3F4F7"),0),new GradientStop(Color.Parse(Banner?"#F7F9FC":"#E9EDF3"),1)]
                :[new GradientStop(Color.Parse(Banner?"#263247":"#111823"),0),new GradientStop(Color.Parse(Banner?"#151E2D":"#0B1019"),1)]
        };
        context.DrawRectangle(background,null,new Rect(Bounds.Size));
        if(Banner)
        {
            var sheen=new LinearGradientBrush
            {
                StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(.8,1,RelativeUnit.Relative),
                GradientStops=[new GradientStop(Color.Parse(LightPalette?"#A0FFFFFF":"#14FFFFFF"),0),new GradientStop(Color.Parse("#00FFFFFF"),.65)]
            };
            context.DrawRectangle(sheen,null,new Rect(Bounds.Size));
        }
    }
}
