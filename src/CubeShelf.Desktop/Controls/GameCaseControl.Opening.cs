using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameCaseControl
{
    private bool _openingEnabled;
    private double _opening, _mediaLift;
    private double _mediaSpinDegrees;
    internal double MediaVerticalOffset => (CaseFormat switch { GameCaseFormat.Switch => .82, GameCaseFormat.Nintendo64 => -2.2, _ => -1.05 })*_mediaLift;
    internal double MediaRotationDegrees => _mediaSpinDegrees;
    private Bitmap? _mediaArtwork;
    private static readonly IBrush DiscSilver = Material("#E8EDF3","#879CB3","#FCFCFF","#9DA5B6");
    private static readonly IBrush InteriorDark = new SolidColorBrush(Color.Parse("#202126"));
    private static readonly IBrush InteriorLight = new SolidColorBrush(Color.Parse("#E7E8E4"));
    private static readonly IBrush CartridgeBlack = new SolidColorBrush(Color.Parse("#24272C"));
    private static readonly IBrush NeutralCartridgeLabel = new SolidColorBrush(Color.Parse("#D84D50"));
    private static readonly Point[] CirclePoints = Enumerable.Range(0,64)
        .Select(index=>new Point(Math.Cos(index*Math.PI/32),Math.Sin(index*Math.PI/32))).ToArray();

    public void SetOpeningMedia(string? path)
    {
        CoverBitmapCache.Release(_mediaArtwork);_mediaArtwork=Load(path);
        if(UsesGpuTextures)CoverBitmapCache.PrepareTexture(_mediaArtwork);
    }
    public void SetOpeningProgress(double opening,double mediaLift,double spinDegrees = 0)
    {
        _openingEnabled=true;
        _opening=Math.Clamp(opening,0,1);_mediaLift=Math.Clamp(mediaLift,0,1);
        _mediaSpinDegrees=spinDegrees%360;
        InvalidateVisual();
    }

    private void RenderOpening(DrawingContext context)
    {
        if(CaseFormat==GameCaseFormat.Nintendo64) { RenderNintendo64Opening(context);return; }
        var half=CaseWidth/2;var tall=CaseHeight/2;
        var (sinYaw,cosYaw)=Math.SinCos(12*Math.PI/180);
        var (sinPitch,cosPitch)=Math.SinCos(-4*Math.PI/180);
        var shift=CaseWidth*.34*_opening;
        var isDisc=CaseFormat!=GameCaseFormat.Switch;
        var fit=Math.Min(Bounds.Width/(CaseWidth*2.4),Bounds.Height/(isDisc?4.5:3.8));
        var centerY=Bounds.Height*(isDisc?.43:.5);
        var projectionScale=fit*1.72/Math.Tan(FieldOfViewDegrees*Math.PI/360);
        Point Project(Vec3 point)
        {
            var x=point.X+shift;
            var y=point.Y*cosPitch-point.Z*sinPitch;
            var z=point.Y*sinPitch+point.Z*cosPitch;
            var scale=projectionScale/(CameraDistance-(-x*sinYaw+z*cosYaw));
            return new Point(Bounds.Width/2+(x*cosYaw+z*sinYaw)*scale,centerY-y*scale);
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
                    var unit=CirclePoints[n];
                    var point=Project(new(x+unit.X*r,y+(reverse?-unit.Y:unit.Y)*r,z));
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
            var x=isDisc?0:.38*_mediaLift;
            var y=MediaVerticalOffset;var z=.06+.65*_mediaLift;
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
            {
                var (sin,cos)=Math.SinCos(_mediaSpinDegrees*Math.PI/180);
                Point Rotated(double dx,double dy)=>Project(new(x+dx*cos-dy*sin,y+dx*sin+dy*cos,z));
                var printed=new[]{Rotated(-radius,radius),Rotated(radius,radius),Rotated(radius,-radius),Rotated(-radius,-radius)};
                using(context.PushGeometryClip(ring))
                    DrawTexturedQuadMesh(context,_mediaArtwork,printed,8,12);
            }
            else
            {
                var (sin,cos)=Math.SinCos(_mediaSpinDegrees*Math.PI/180);
                context.DrawLine(CaseHighlight,Project(new(x+cos*radius*.45,y+sin*radius*.45,z)),
                    Project(new(x+cos*radius*.92,y+sin*radius*.92,z)));
            }
            context.DrawGeometry(null,CaseHighlight,Circle(x,y,z,radius*.98));
            context.DrawGeometry(null,CaseSeam,Circle(x,y,z,.18));
        }
        if(_mediaLift==0)Media();

        var (sinLid,cosLid)=Math.SinCos(-140*_opening*Math.PI/180);
        Vec3 Lid(double x,double y)
        {
            return new Vec3((x+half)*cosLid-half,y,-(x+half)*sinLid+.07);
        }
        var lid=new[]{Project(Lid(-half,tall)),Project(Lid(half,tall)),Project(Lid(half,-tall)),Project(Lid(-half,-tall))};
        var lidShape=RoundedQuad(lid,.01);
        context.DrawGeometry(material,CaseOutline,lidShape);
        if(cosYaw*cosLid-sinYaw*sinLid>0)
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

    private static readonly IBrush CartridgeTrayPaper = new SolidColorBrush(Color.Parse("#EEEDE5"));
    private static readonly IBrush CartridgeTrayWell = new SolidColorBrush(Color.Parse("#B8B6AE"));
    internal bool Nintendo64OpensAtTop => PortraitNintendo64;

    private void RenderNintendo64Opening(DrawingContext context)
    {
        var half=CaseWidth/2;var tall=CaseHeight/2;var depth=CaseDepth/2;
        var portrait=Nintendo64OpensAtTop;
        var end=portrait?tall:half;var side=portrait?half:tall;
        var fit=Math.Min(Bounds.Width/6.2,Bounds.Height/(portrait?7.8:5.7));
        Point Project(double x,double y,double z) => new(Bounds.Width*(portrait?.5:.34)+(x*.951-z*.309)*fit,
            Bounds.Height*(portrait?.53:.33)-(y*.990-(x*.309+z*.951)*.139)*fit);
        Point Local(double u,double v,double z) => portrait?Project(-v,u,z):Project(u,v,z);
        Point[] Panel(double left,double top,double right,double bottom,double z) =>
            [Project(left,top,z),Project(right,top,z),Project(right,bottom,z),Project(left,bottom,z)];
        void DrawPanel(Point[] points,IBrush paper,Bitmap? print=null)
        {
            var outline=BuildGeometry(points);
            context.DrawGeometry(paper,null,outline);
            if(print is not null)using(context.PushGeometryClip(outline))DrawTexturedQuadMesh(context,print,points,4,4);
            context.DrawGeometry(null,CaseSeam,outline);
        }
        var front=Panel(-half,tall,half,-tall,depth);
        DrawContactShadow(context,ComputeBounds(front));
        DrawPanel(Panel(-half,tall,half,-tall,-depth),CartridgeTrayWell);
        if(!portrait)
            DrawPanel([Project(-half,tall,-depth),Project(half,tall,-depth),Project(half,tall,depth),Project(-half,tall,depth)],CartridgeTrayPaper,_top);
        else
            DrawPanel([Project(half,tall,depth),Project(half,tall,-depth),Project(half,-tall,-depth),Project(half,-tall,depth)],CartridgeTrayPaper,_right);

        // Western cartons open at the short end; Japanese portrait cartons open at the top.
        // Two dust flaps sit under the printed tuck flap, with a folded paper cartridge tray inside.
        foreach(var sign in new[]{-1,1})
        {
            var rim=side*sign;
            DrawPanel([Local(end,rim,-depth),Local(end,rim,depth),
                Local(end+.32*_opening,rim-sign*.28*(1-_opening),depth),
                Local(end+.32*_opening,rim-sign*.28*(1-_opening),-depth)],CartridgeTrayPaper);
        }
        var slide=Math.Clamp(_mediaLift/.6,0,1);
        var trayCenter=slide*2.65;
        var trayHalf=1.23;var traySide=side-.12;
        if(_mediaLift>0)
        {
            DrawPanel([Local(trayCenter-trayHalf,traySide,.025),Local(trayCenter+trayHalf,traySide,.025),
                Local(trayCenter+trayHalf,-traySide,.025),Local(trayCenter-trayHalf,-traySide,.025)],CartridgeTrayPaper);
            DrawPanel([Local(trayCenter-.9,traySide-.18,.03),Local(trayCenter+.9,traySide-.18,.03),
                Local(trayCenter+.9,-traySide+.18,.03),Local(trayCenter-.9,-traySide+.18,.03)],CartridgeTrayWell);
        }
        void Cartridge()
        {
            if(_mediaLift<=0)return;
            var release=Math.Clamp((_mediaLift-.55)/.45,0,1);release=release*release*(3-2*release);
            var x=portrait?0:trayCenter*(1-release);
            var y=(portrait?trayCenter:0)*(1-release)+MediaVerticalOffset*release;
            var width=portrait?1.75:1.9;
            var height=_mediaArtwork is null?width*.67:width*_mediaArtwork.PixelSize.Height/_mediaArtwork.PixelSize.Width;
            var card=Panel(x-width/2,y+height/2,x+width/2,y-height/2,.06+release*.8);
            if(_mediaArtwork is not null)DrawTexturedQuadMesh(context,_mediaArtwork,card,4,4);
            else context.DrawGeometry(InteriorLight,CaseOutline,RoundedQuad(card,.14));
        }
        if(_mediaLift<.6)Cartridge();
        DrawPanel(front,CaseMaterials[(int)CaseFormat],_front);
        var angle=_opening*Math.PI*.66;
        var freeU=end+CaseDepth*Math.Sin(angle);var freeZ=-depth+CaseDepth*Math.Cos(angle);
        Point[] flap=portrait
            ?[Local(end,side,-depth),Local(end,-side,-depth),Local(freeU,-side,freeZ),Local(freeU,side,freeZ)]
            :[Local(freeU,side,freeZ),Local(end,side,-depth),Local(end,-side,-depth),Local(freeU,-side,freeZ)];
        DrawPanel(flap,CartridgeTrayPaper,_opening<.55?(portrait?_top:_right):null);
        if(_opening>.1)
            DrawPanel([Local(freeU,side-.09,freeZ),Local(freeU,-side+.09,freeZ),
                Local(freeU+.16*Math.Sin(angle),-side+.09,freeZ+.16*Math.Cos(angle)),
                Local(freeU+.16*Math.Sin(angle),side-.09,freeZ+.16*Math.Cos(angle))],CartridgeTrayPaper);
        if(_mediaLift>=.6)Cartridge();
    }
}
