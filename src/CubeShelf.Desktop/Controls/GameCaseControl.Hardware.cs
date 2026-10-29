using Avalonia;
using Avalonia.Media;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameCaseControl
{
    public string? HardwareConsole { get; set; }
    private readonly record struct HardwareVertex(double X, double Y, double Z);
    private sealed record HardwareFace(HardwareVertex[] Points, Color Color, int Part);
    private static readonly Dictionary<string, HardwareFace[]> HardwareModels = new();
    private (Size Size, double Yaw, double Pitch, string? Id) _hardwareKey;
    private (Geometry Geometry, IBrush Brush)[] _hardwareDrawing = [];

    private void RenderHardware(DrawingContext context)
    {
        var key = (Bounds.Size, _yaw, _pitch, HardwareConsole);
        if (key != _hardwareKey)
        {
            if (!HardwareModels.TryGetValue(HardwareConsole!, out var faces))
                HardwareModels[HardwareConsole!] = faces = BuildHardware(HardwareConsole!);
            var yaw = (_yaw - 8) * Math.PI / 180;
            var pitch = (_pitch + 22) * Math.PI / 180;
            HardwareVertex RotateHardware(HardwareVertex v)
            {
                var x = v.X * Math.Cos(yaw) + v.Z * Math.Sin(yaw);
                var z = -v.X * Math.Sin(yaw) + v.Z * Math.Cos(yaw);
                return new(x, v.Y * Math.Cos(pitch) - z * Math.Sin(pitch), v.Y * Math.Sin(pitch) + z * Math.Cos(pitch));
            }
            var fit = Math.Min(Bounds.Width / 3.9, Bounds.Height / 4.1);
            _hardwareDrawing = faces.Select(face => (Points: face.Points.Select(RotateHardware).ToArray(), face.Color, face.Part))
                .GroupBy(face=>face.Part).OrderBy(part=>part.SelectMany(face=>face.Points).Average(p=>p.Z))
                .SelectMany(part=>part.OrderBy(face=>face.Points.Average(p=>p.Z)))
                .Where(face => (face.Points[1].X-face.Points[0].X)*(face.Points[2].Y-face.Points[0].Y)
                    -(face.Points[1].Y-face.Points[0].Y)*(face.Points[2].X-face.Points[0].X)>0)
                .Select(face =>
                {
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                    {
                        Point Project(HardwareVertex v) => new(Bounds.Width / 2 + v.X * fit * 6 / (6 - v.Z), Bounds.Height / 2 - v.Y * fit * 6 / (6 - v.Z));
                        path.BeginFigure(Project(face.Points[0]), true);
                        foreach (var point in face.Points.Skip(1)) path.LineTo(Project(point));
                        path.EndFigure(true);
                    }
                    return ((Geometry)geometry, (IBrush)new SolidColorBrush(face.Color));
                }).ToArray();
            _hardwareKey = key;
        }
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)), null,
            new Point(Bounds.Width / 2, Bounds.Height * .79), Bounds.Width * .36, Bounds.Height * .035);
        foreach (var face in _hardwareDrawing) context.DrawGeometry(face.Brush, null, face.Geometry);
    }

    // Original low-poly meshes, deliberately stylised rather than scans of the hardware.
    private static HardwareFace[] BuildHardware(string id)
    {
        var faces = new List<HardwareFace>();
        var part=0;
        Color Shade(Color c, double factor) => Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
        void Box(double x, double y, double z, double w, double h, double d, string hex)
        {
            var c = Color.Parse(hex);
            part++;
            HardwareVertex V(int a, int b, int e) => new(x + (a - .5) * w, y + (b - .5) * h, z + (e - .5) * d);
            faces.Add(new([V(0,1,0),V(1,1,0),V(1,0,0),V(0,0,0)], Shade(c,.58),part));
            faces.Add(new([V(0,0,1),V(1,0,1),V(1,1,1),V(0,1,1)], Shade(c,.82),part));
            faces.Add(new([V(0,0,0),V(0,0,1),V(0,1,1),V(0,1,0)], Shade(c,.66),part));
            faces.Add(new([V(1,1,0),V(1,1,1),V(1,0,1),V(1,0,0)], Shade(c,.73),part));
            faces.Add(new([V(0,0,0),V(1,0,0),V(1,0,1),V(0,0,1)], Shade(c,.5),part));
            faces.Add(new([V(0,1,1),V(1,1,1),V(1,1,0),V(0,1,0)], c,part));
        }
        void Disc(double x, double y, double z, double radius, string hex)
        {
            faces.Add(new(Enumerable.Range(0,32).Select(i => new HardwareVertex(x + Math.Cos(-i*Math.PI/16)*radius,y,z+Math.Sin(-i*Math.PI/16)*radius)).ToArray(),Color.Parse(hex),++part));
        }
        void Ports(int count, double y, double z, double spacing=.5)
        { for(var i=0;i<count;i++) Box((i-(count-1)/2d)*spacing,y,z,.25,.17,.08,"#11151C"); }
        void Handheld(bool upright, string color)
        {
            var w=upright?1.5:3.1;var h=upright?2.45:1.45;
            Box(0,0,0,w,h,.3,color);
            Box(0,upright?.43:.07,.17,upright?1.2:1.65,upright?1.1:1,.06,"#252C36");
            Box(0,upright?.43:.07,.21,upright?.95:1.4,upright?.8:.76,.02,"#677C79");
            var dx=upright?-.42:-1.15;var dy=upright?-.55:0;
            Box(dx,dy,.2,.4,.12,.08,"#20252C");Box(dx,dy,.2,.12,.4,.08,"#20252C");
            Box(-dx,dy+.12,.2,.15,.15,.08,"#AA3969");Box(-dx+.19,dy-.1,.2,.15,.15,.08,"#AA3969");
        }
        switch(id)
        {
            case "gb": Handheld(true,"#BFC4AD");break;
            case "gbc": Handheld(true,"#7954A0");break;
            case "gba": Handheld(false,"#6956A3");break;
            case "psp": Handheld(false,"#303541");break;
            case "gamegear": Handheld(false,"#353C46");break;
            case "nds": case "3ds":
                Handheld(false,id=="3ds"?"#B64249":"#D8DCE0");
                Box(0,1.15,-.2,3.1,1.15,.18,id=="3ds"?"#B64249":"#D8DCE0");
                Box(0,1.15,-.09,1.8,.85,.04,"#25343E");break;
            case "switch":
                Box(0,0,0,2.5,1.6,.24,"#292F37");Box(0,0,.14,2.2,1.35,.03,"#435765");
                Box(-1.42,0,0,.42,1.6,.3,"#36BBD9");Box(1.42,0,0,.42,1.6,.3,"#E85A57");
                Ports(2,0,.22,2.85);break;
            case "gamecube":
                Box(0,0,0,2.1,1.7,2,"#7565B3");Disc(0,.86,0,.82,"#454151");Ports(4,-.38,1.04,.43);
                Box(-.72,.9,.7,.22,.1,.25,"#BBBBBE");break;
            case "wii":
                Box(0,-1,0,1.4,.16,1.8,"#8B9299");Box(0,.05,0,.72,2.3,1.7,"#EEEFEA");
                Box(.1,.2,.87,.045,1.3,.03,"#389DC4");Box(-.17,.8,.88,.12,.12,.04,"#A8AFB1");break;
            case "n64":
                Box(0,-.2,0,2.9,.65,1.9,"#414750");Box(0,.19,-.15,1.8,.18,1.1,"#414750");
                Box(0,.31,-.2,1.35,.08,.17,"#151A22");Ports(4,-.2,1,.58);
                Box(-1,.2,.45,.3,.1,.4,"#8C9198");break;
            case "nes":
                Box(0,-.17,0,2.7,.55,2,"#747B82");Box(0,.2,0,2.7,.33,2,"#C6C9C6");
                Box(.25,.2,1.03,1.7,.24,.04,"#363B42");Ports(2,-.2,1.04,.45);break;
            case "snes":
                Box(0,0,0,2.7,.65,2,"#BFC2C4");Box(0,.38,-.2,1.5,.14,.25,"#353A43");
                Box(-.65,.38,.45,.4,.16,.3,"#8F7FB0");Box(.65,.38,.45,.4,.16,.3,"#8F7FB0");Ports(2,-.08,1.04,.65);break;
            case "virtualboy":
                Box(0,.5,0,2.6,.95,1.2,"#BF303A");Box(0,.5,.65,2.1,.65,.2,"#252A31");
                Box(-.55,-.5,0,.15,1.1,.18,"#434A50");Box(.55,-.5,0,.15,1.1,.18,"#434A50");break;
            case "arcade":
                Box(0,-.2,0,1.9,2.7,1.6,"#3D506C");Box(0,.65,.84,1.5,1.1,.08,"#171D27");
                Box(0,-.15,1.05,2.1,.22,.65,"#C78D46");Box(0,1.4,.85,1.8,.35,.1,"#CF994B");break;
            default:
                var white=id is "ps1" or "dreamcast" or "xbox360" or "wiiu";
                var color=white?"#D2D5D6":"#303841";
                var width=id=="ps4"?3.2:2.8;var depth=id=="psp"?1.2:2;
                Box(0,0,0,width,id=="ps3"?.85:.6,depth,color);
                if(id is "ps1" or "dreamcast" or "saturn" or "megadrive" or "segacd" or "32x") Disc(0,.32,0,.75,id=="ps1"?"#A9AFB2":"#424A52");
                if(id is "mastersystem" or "megadrive" or "32x" or "neogeo") Box(0,.38,-.1,1.5,.15,.18,"#161B21");
                if(id=="xbox") {Box(0,.34,0,2.4,.09,.35,"#4E6950");Box(0,.34,0,.35,.09,1.7,"#4E6950");Disc(0,.4,0,.3,"#7CB342");}
                if(id is "ps2" or "ps4") Box(0,-.15,.1,width+.12,.13,depth+.12,"#20252F");
                if(id=="mastersystem") Box(0,.31,.65,2.65,.03,.5,"#B83744");
                Ports(id=="neogeo"?2:2,-.06,depth/2+.035,.85);
                if(id is "ps2" or "ps3" or "ps4" or "xbox360" or "cdi" or "wiiu") Box(.25,.1,depth/2+.04,1.7,.045,.04,"#101720");
                if(id=="wiiu") { Box(0,-.7,1.2,2.4,1.1,.3,"#40474F");Box(0,-.7,1.37,1.65,.8,.03,"#607681"); }
                break;
        }
        return faces.ToArray();
    }
}
