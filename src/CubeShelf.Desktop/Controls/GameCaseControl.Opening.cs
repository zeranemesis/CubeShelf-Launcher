using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameCaseControl
{
    private bool _openingEnabled;
    private double _opening, _mediaLift;
    private Bitmap? _mediaArtwork;
    private static readonly IBrush DiscSilver = Material("#E8EDF3","#879CB3","#FCFCFF","#9DA5B6");
    private static readonly IBrush InteriorDark = new SolidColorBrush(Color.Parse("#202126"));
    private static readonly IBrush InteriorLight = new SolidColorBrush(Color.Parse("#E7E8E4"));
    private static readonly IBrush CartridgeBlack = new SolidColorBrush(Color.Parse("#24272C"));
    private static readonly IBrush NeutralCartridgeLabel = new SolidColorBrush(Color.Parse("#D84D50"));

    public void SetOpeningMedia(string? path)
    {
        CoverBitmapCache.Release(_mediaArtwork);_mediaArtwork=Load(path);
    }
    public void SetOpeningProgress(double opening,double mediaLift)
    {
        _openingEnabled=true;
        _opening=Math.Clamp(opening,0,1);_mediaLift=Math.Clamp(mediaLift,0,1);
        InvalidateVisual();
    }

    private void RenderOpening(DrawingContext context)
    {
        var half=CaseWidth/2;var tall=CaseHeight/2;
        Point Project(Vec3 point)
        {
            point=point with { X=point.X+CaseWidth*.34*_opening };
            var rotated=Rotate(point,12,-4);
            var focal=1/Math.Tan(FieldOfViewDegrees*Math.PI/360);
            var fit=Math.Min(Bounds.Width/(CaseWidth*2.4),Bounds.Height/3.8);
            var scale=fit*focal*1.72/(CameraDistance-rotated.Z);
            return new Point(Bounds.Width/2+rotated.X*scale,Bounds.Height/2-rotated.Y*scale);
        }
        Point[] Quad(double left,double top,double right,double bottom,double z) =>
            [Project(new(left,top,z)),Project(new(right,top,z)),Project(new(right,bottom,z)),Project(new(left,bottom,z))];
        var body=Quad(-half,tall,half,-tall,0);
        DrawContactShadow(context,ComputeBounds(body));
        var material=CaseMaterials[(int)CaseFormat];
        var interior=CaseFormat==GameCaseFormat.GameCube?InteriorDark:InteriorLight;
        context.DrawGeometry(material,CaseHighlight,RoundedQuad(body,.012));
        var tray=Quad(-half+.06,tall-.06,half-.06,-tall+.06,.015);
        context.DrawGeometry(interior,CaseSeam,RoundedQuad(tray,.018));
        var edge=new[]{body[1],Project(new(half,tall,-CaseDepth)),Project(new(half,-tall,-CaseDepth)),body[2]};
        context.DrawGeometry(material,CaseOutline,RoundedQuad(edge,.02));

        StreamGeometry Circle(double x,double y,double z,double radius,double hole=0)
        {
            var geometry=new StreamGeometry();using var path=geometry.Open();
            void Loop(double r,bool reverse)
            {
                for(var n=0;n<64;n++)
                {
                    var angle=(reverse?-1:1)*n*Math.PI/32;
                    var point=Project(new(x+Math.Cos(angle)*r,y+Math.Sin(angle)*r,z));
                    if(n==0)path.BeginFigure(point,true);else path.LineTo(point);
                }
                path.EndFigure(true);
            }
            Loop(radius,false);if(hole>0)Loop(hole,true);
            return geometry;
        }
        var radius=CaseWidth*(CaseFormat==GameCaseFormat.GameCube?.295:.445);
        if(CaseFormat!=GameCaseFormat.Switch)
        {
            context.DrawGeometry(null,CaseSeam,Circle(0,0,.03,radius+.04));
            context.DrawGeometry(material,CaseHighlight,Circle(0,0,.04,.12));
        }
        else
            context.DrawGeometry(CartridgeBlack,CaseSeam,RoundedQuad(Quad(-.205,.3,.205,-.3,.04),.04));

        void Media()
        {
            var x=.38*_mediaLift;var y=.82*_mediaLift;var z=.06+.65*_mediaLift;
            if(CaseFormat==GameCaseFormat.Switch)
            {
                var card=Quad(x-.18,y+.27,x+.18,y-.27,z);
                context.DrawGeometry(CartridgeBlack,CaseHighlight,RoundedQuad(card,.055));
                if(_mediaArtwork is not null)
                    using(context.PushGeometryClip(RoundedQuad(card,.055)))DrawTexturedQuadMesh(context,_mediaArtwork,card,3,4);
                else
                {
                    var label=Quad(x-.15,y+.23,x+.15,y-.12,z+.005);
                    context.DrawGeometry(NeutralCartridgeLabel,null,RoundedQuad(label,.03));
                }
                return;
            }
            var ring=Circle(x,y,z,radius,.12);
            context.DrawGeometry(DiscSilver,CaseOutline,ring);
            if(_mediaArtwork is not null)
                using(context.PushGeometryClip(ring))
                    DrawTexturedQuadMesh(context,_mediaArtwork,Quad(x-radius,y+radius,x+radius,y-radius,z),8,12);
            context.DrawGeometry(null,CaseHighlight,Circle(x,y,z,radius*.98));
            context.DrawGeometry(null,CaseSeam,Circle(x,y,z,.18));
        }
        if(_mediaLift==0)Media();

        Vec3 Lid(double x,double y)
        {
            var rotated=Rotate(new(x+half,y,0),-140*_opening,0);
            return rotated with { X=rotated.X-half,Z=rotated.Z+.07 };
        }
        var lid=new[]{Project(Lid(-half,tall)),Project(Lid(half,tall)),Project(Lid(half,-tall)),Project(Lid(-half,-tall))};
        var lidShape=RoundedQuad(lid,.01);
        context.DrawGeometry(material,CaseOutline,lidShape);
        if(Math.Cos((12-140*_opening)*Math.PI/180)>0)
        {
            var printed=new[]{Bilinear(lid,.025,.014),Bilinear(lid,.985,.014),Bilinear(lid,.985,.986),Bilinear(lid,.025,.986)};
            if(_front is not null)using(context.PushGeometryClip(lidShape))DrawTexturedQuadMesh(context,_front,printed,8,12);
        }
        else
        {
            context.DrawGeometry(interior,SleeveEdge,lidShape);
            foreach(var v in new[]{.16,.84})
                context.DrawLine(CaseHighlight,Bilinear(lid,.92,v-.035),Bilinear(lid,.92,v+.035));
        }
        context.DrawLine(CaseHighlight,lid[0],lid[3]);
        if(_mediaLift>0)Media();
    }
}
