using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CubeShelf.Desktop.Controls;

public enum GameCaseFormat { GameCube, Wii, Switch }

/// <summary>
/// Portable console case renderer with a real projected cuboid, textured
/// front/back/spine faces and time-based, compositor-paced interaction. The renderer stays
/// framework-native (no WPF Viewport3D and no Windows-only graphics API).
/// </summary>
public sealed partial class GameCaseControl : Control, IDisposable
{
    private double CaseWidth => CaseFormat == GameCaseFormat.Switch ? 1.79 : 2.08;
    private const double CaseHeight = 2.92;
    private double CaseDepth => CaseFormat == GameCaseFormat.Switch ? .174 : .215;
    private const double CameraDistance = 5.75;
    private const double FieldOfViewDegrees = 32;
    private const double MaxHoverYaw = 19;
    private const double MaxHoverPitch = 8;

    /// <summary>How much the case grows under the pointer, so it reads as lifting off the page.</summary>
    private const double HoverLift = .04;

    /// <summary>
    /// Each mesh cell is clipped to its own triangle, and two clips that share an edge leave a
    /// hairline of background between them -- a visible grid over the artwork. Growing the clip
    /// (not the texture transform) makes neighbours overlap by well under a pixel instead.
    /// </summary>
    private const double CellClipBleed = .75;

    private static readonly IBrush ShellFrontBrush = new SolidColorBrush(Color.Parse("#151B28"));
    private static readonly IBrush ShellSideBrush = new SolidColorBrush(Color.Parse("#0D121D"));
    private static readonly IBrush ShellEdgeBrush = new SolidColorBrush(Color.Parse("#26334A"));
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromArgb(145, 120, 150, 200)), 1);
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(54, 0, 0, 0));
    private static readonly IBrush[] CaseMaterials =
    [
        Material("#45454B", "#111114", "#252529", "#08080A"),
        Material("#FFFFFF", "#E5E5E2", "#FAFAF7", "#C7CBCB"),
        Material("#E8EBEE", "#ADB6BF", "#D4DADE", "#8B969F")
    ];
    private static readonly IPen CaseOutline = new Pen(new SolidColorBrush(Color.FromArgb(110,65,65,72)), .7);
    private static readonly IPen CaseHighlight = new Pen(new SolidColorBrush(Color.FromArgb(105,245,247,250)), .7);
    private static readonly IPen CaseSeam = new Pen(new SolidColorBrush(Color.FromArgb(85,0,0,0)), .8);
    private static readonly IPen SleeveEdge = new Pen(new SolidColorBrush(Color.FromArgb(65,235,236,242)), .65);
    private static readonly IBrush SleeveReflection = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,.3,RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(Color.FromArgb(12,255,255,255),0), new(Colors.Transparent,.24),
            new(Color.FromArgb(14,255,255,255),.38),new(Colors.Transparent,.52), new(Color.FromArgb(5,0,0,0),1)
        }
    };
    private static IBrush Material(string a,string b,string c,string d) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,1,RelativeUnit.Relative),
        GradientStops = new GradientStops {new(Color.Parse(a),0),new(Color.Parse(b),.09),new(Color.Parse(c),.5),new(Color.Parse(d),1)}
    };

    private readonly DemandAnimation _animation;
    private Bitmap? _front;
    private Bitmap? _back;
    private Bitmap? _spine;
    private bool _flipped;
    private bool _pointerInside;
    private double _baseYaw;
    private double _restYaw;
    public bool IsInteractive { get; set; } = true;
    public double ShadowOpacity { get; set; } = 1;
    public bool RealisticShell { get; set; }
    public bool UseImportedModel { get; set; }
    public GameCaseFormat CaseFormat { get; set; } = GameCaseFormat.GameCube;
    public double CaseAspectRatio => CaseWidth / CaseHeight;
    public static GameCaseFormat FormatForConsole(string console) => console switch
    { "wii" => GameCaseFormat.Wii, "switch" => GameCaseFormat.Switch, _ => GameCaseFormat.GameCube };
    private bool UsesGameCubeModel => UseImportedModel && CaseFormat == GameCaseFormat.GameCube;
    public bool UseCompactTextureMesh { get; init; }
    private double _restPitch;
    private double _hoverYaw;
    private double _hoverPitch;
    private double _yaw;
    private double _pitch;
    private double _targetYaw;
    private double _targetPitch;
    private Point _pointerPosition;
    private double _glare;
    private double _targetGlare;
    private double _lift;
    private double _targetLift;
    private bool _disposed;
    private SolidColorBrush? _contactShadowBrush;

    public GameCaseControl()
    {
        MinWidth = 230;
        MinHeight = 300;
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Hand);

        _animation = new DemandAnimation(this, AnimateFrame);

        PointerEntered += (_, args) =>
        {
            if (args.Pointer.Type != PointerType.Touch) _pointerInside = IsInteractive;
        };
        PointerMoved += OnPointerMoved;
        PointerExited += (_, args) =>
        {
            if (args.Pointer.Type == PointerType.Touch) return;
            _pointerInside = false;
            _hoverYaw = 0;
            _hoverPitch = 0;
            UpdateAnimationTarget();
        };
        PointerPressed += (_, _) => { if (IsInteractive) Flip(); };
        DetachedFromVisualTree += (_, _) => _animation.Stop();
    }

    private (string Front,string Back,string Spine,GameCaseFormat Format)? _coverIdentity;

    private static string CoverIdentity(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks : path;
        }
        catch (Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException) { return path; }
    }

    public bool SetCover(string? front, string? back, string? spine)
    {
        var identity = (CoverIdentity(front),CoverIdentity(back),CoverIdentity(spine),CaseFormat);
        if (_coverIdentity == identity) return false;
        DisposeBitmaps();
        _coverIdentity = identity;
        _front = Load(front);
        _back = Load(back);
        _spine = Load(spine);
        if (PreferGpuTextures) CoverBitmapCache.PrepareTexture(_back);
        _flipped = false;
        _pointerInside = false;
        _baseYaw = 0;
        _restYaw = 0;
        _restPitch = 0;
        _hoverYaw = 0;
        _hoverPitch = 0;
        _yaw = 0;
        _pitch = 0;
        _targetYaw = 0;
        _targetPitch = 0;
        _glare = 0;
        _targetGlare = 0;
        _lift = 0;
        _targetLift = 0;
        _animation.Stop();
        InvalidateVisual();
        return true;
    }

    public void Flip()
    {
        _flipped = !_flipped;
        _baseYaw = UsesGameCubeModel ? (_flipped ? 180 - _restYaw : _restYaw) : _restYaw + (_flipped ? 180 : 0);
        UpdateAnimationTarget();
    }

    public void ShowFront()
    {
        _flipped = false;
        _baseYaw = _restYaw;
        _hoverYaw = 0;
        _hoverPitch = 0;
        UpdateAnimationTarget();
    }

    /// <summary>Places a case on a shelf, preserving the renderer used on game details.</summary>
    public void SetShelfPose(double yaw, bool selected, bool animate)
    {
        var restPitch = RealisticShell ? (selected ? 3.5 : 2) : 0;
        if (animate && _restYaw == yaw && _restPitch == restPitch && IsInteractive == selected && !_flipped
            && !_pointerInside && _hoverYaw == 0 && _hoverPitch == 0) return;
        IsInteractive = selected;
        _restYaw = yaw;
        _restPitch = restPitch;
        _baseYaw = yaw;
        _flipped = false;
        _pointerInside = false;
        _hoverYaw = _hoverPitch = 0;
        if (!animate) { _yaw = yaw; _pitch = _restPitch; }
        UpdateAnimationTarget();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 2 || Bounds.Height <= 2) return;
        var timing = FrontendTiming;
        if (timing is null) { RenderCase(context); return; }
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        RenderCase(context);
        timing(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private void RenderCase(DrawingContext context)
    {
        if (UsesGameCubeModel && RenderImportedModel(context)) return;

        var vertices = BuildProjectedVertices();
        var faces = BuildFaces(vertices)
            .Where(face => face.Visible)
            .OrderBy(face => face.AverageDepth)
            .ToArray();

        var shadowBounds = ComputeBounds(vertices.Select(v => v.Screen));
        if (RealisticShell)
            DrawContactShadow(context, shadowBounds);
        else
        {
            var yawRadians = _yaw * Math.PI / 180.0;
            var shadowOffset = new Vector(9 + Math.Sin(yawRadians) * 5, 14 + Math.Abs(_pitch) * .20);
            var shadow = shadowBounds.Translate(shadowOffset).Inflate(5 + 6 * _lift);
            using (context.PushOpacity(ShadowOpacity))
                context.DrawRectangle(ShadowBrush, null, shadow, 20, 20);
        }

        foreach (var face in faces)
        {
            if (RealisticShell) DrawRealisticFace(context, face);
            else DrawFace(context, face, _pointerPosition, _glare);
        }
    }

    private void DrawContactShadow(DrawingContext context, Rect bounds)
    {
        // Layered ellipses keep the shadow soft without a per-frame bitmap blur.
        var center = new Point(bounds.Center.X + 6, bounds.Bottom + 7 + _lift * 8);
        var alpha = (byte)(9 * ShadowOpacity);
        if (_contactShadowBrush is null || _contactShadowBrush.Color.A != alpha)
            _contactShadowBrush = new SolidColorBrush(Color.FromArgb(alpha, 24, 20, 28));
        var brush = _contactShadowBrush;
        for (var layer = 7; layer >= 0; layer--)
        {
            context.DrawEllipse(brush, null, center,
                Math.Max(12, bounds.Width * .42 + layer * 3), 3 + layer * 2.1);
        }
    }

    private void DrawRealisticFace(DrawingContext context, Face face)
    {
        var quad = face.Vertices.Select(vertex => vertex.Screen).ToArray();
        var broad = face.Name is "front" or "back";
        var outline = RoundedQuad(quad, broad ? .009 : .02);
        var shell = CaseMaterials[(int)CaseFormat];
        context.DrawGeometry(shell, CaseOutline, outline);

        // The printed insert sits inside a molded frame under a clear sleeve.
        // The asymmetrical gutter leaves room for the hinge beside the front/back.
        var gutter = CaseFormat == GameCaseFormat.Switch ? .018 : .028;
        var left = face.Name == "front" ? gutter : broad ? .012 : .06;
        var right = face.Name == "back" ? 1-gutter : broad ? .988 : .94;
        var top = broad ? .012 : .018;
        var artwork = new[] { Bilinear(quad, left, top), Bilinear(quad, right, top),
            Bilinear(quad, right, 1 - top), Bilinear(quad, left, 1 - top) };
        var sleeve = RoundedQuad(artwork, broad ? .005 : .01);
        using (context.PushGeometryClip(sleeve))
        {
            if (face.Texture is not null)
                DrawTexturedQuadMesh(context, face.Texture, artwork,
                    broad ? IsInteractive ? (UseCompactTextureMesh ? 4 : 8) : 3 : 2,
                    broad ? IsInteractive ? (UseCompactTextureMesh ? 6 : 12) : 4 : 6);
            if (broad)
            {
                context.DrawGeometry(SleeveReflection, null, sleeve);
                DrawPointerGlare(context, sleeve, artwork, _pointerPosition, _glare * .55);
            }
        }
        var highlight = CaseHighlight;
        var seam = CaseSeam;
        if (broad)
        {
            context.DrawGeometry(null, SleeveEdge, sleeve);
            var hinge = face.Name == "front" ? .026 : .973;
            context.DrawLine(seam, Bilinear(quad, hinge, .012), Bilinear(quad, hinge, .988));
            context.DrawLine(highlight, Bilinear(quad, hinge + .004, .014), Bilinear(quad, hinge + .004, .986));
            context.DrawLine(highlight, Bilinear(quad, .018, .006), Bilinear(quad, .985, .006));
        }
        else
        {
            // Two halves of the case meet along the exposed top/bottom edge.
            if (face.Name is "top" or "bottom")
                context.DrawLine(seam, Bilinear(quad, 0, .5), Bilinear(quad, 1, .5));
            if (face.Name == "right")
            {
                // The opening edge is bare molded plastic, with a latch recess, not a second spine.
                context.DrawLine(seam,Bilinear(quad,.5,.02),Bilinear(quad,.5,.98));
                context.DrawLine(highlight,Bilinear(quad,.22,.44),Bilinear(quad,.22,.56));
            }
            context.DrawLine(highlight, quad[0], quad[1]);
        }
    }

    private static StreamGeometry RoundedQuad(IReadOnlyList<Point> quad, double corner)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(Lerp(quad[0], quad[1], corner), true);
        for (var next = 1; next <= 4; next++)
        {
            var index = next % 4;
            path.LineTo(Lerp(quad[index], quad[(index + 3) % 4], corner));
            path.QuadraticBezierTo(quad[index], Lerp(quad[index], quad[(index + 1) % 4], corner));
        }
        path.EndFigure(true);
        return geometry;
    }

    private ProjectedVertex[] BuildProjectedVertices()
    {
        var x = CaseWidth / 2;
        var y = CaseHeight / 2;
        var z = CaseDepth / 2;
        var source = new[]
        {
            new Vec3(-x,  y,  z), new Vec3( x,  y,  z),
            new Vec3( x, -y,  z), new Vec3(-x, -y,  z),
            new Vec3(-x,  y, -z), new Vec3( x,  y, -z),
            new Vec3( x, -y, -z), new Vec3(-x, -y, -z)
        };

        return source.Select(ProjectVertex).ToArray();
    }

    private ProjectedVertex ProjectVertex(Vec3 point)
    {
        var rotated = Rotate(point, _yaw, _pitch);
        var fovRadians = FieldOfViewDegrees * Math.PI / 180.0;
        var focal = 1.0 / Math.Tan(fovRadians / 2.0);
        var fitScale = Math.Min(Bounds.Width / 2.67, Bounds.Height / 3.48);
        var perspective = focal / Math.Max(0.28, CameraDistance - rotated.Z);
        var scale = fitScale * perspective * 1.72 * (1 + HoverLift * _lift);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        return new ProjectedVertex(
            rotated,
            new Point(center.X + rotated.X * scale, center.Y - rotated.Y * scale));
    }

    private Face[] BuildFaces(ProjectedVertex[] v)
    {
        var frontTexture = _front ?? _back;
        var backTexture = RealisticShell ? _back : _back ?? _front;
        return new[]
        {
            MakeFace("front", v, new[] { 0, 1, 2, 3 }, new Vec3(0, 0, 1), frontTexture, ShellFrontBrush),
            MakeFace("back", v, new[] { 5, 4, 7, 6 }, new Vec3(0, 0, -1), backTexture, ShellFrontBrush),
            MakeFace("left", v, new[] { 4, 0, 3, 7 }, new Vec3(-1, 0, 0), _spine, ShellSideBrush),
            MakeFace("right", v, new[] { 1, 5, 6, 2 }, new Vec3(1, 0, 0), RealisticShell ? null : _spine, ShellSideBrush),
            MakeFace("top", v, new[] { 4, 5, 1, 0 }, new Vec3(0, 1, 0), null, ShellEdgeBrush),
            MakeFace("bottom", v, new[] { 3, 2, 6, 7 }, new Vec3(0, -1, 0), null, ShellEdgeBrush)
        };
    }

    private Face MakeFace(
        string name,
        ProjectedVertex[] vertices,
        int[] indices,
        Vec3 normal,
        Bitmap? texture,
        IBrush fallback)
    {
        var transformedNormal = Rotate(normal, _yaw, _pitch);
        var points = indices.Select(index => vertices[index]).ToArray();
        return new Face(
            name,
            points,
            transformedNormal.Z > 0.0001,
            points.Average(point => point.World.Z),
            texture,
            fallback);
    }

    private static void DrawFace(DrawingContext context, Face face, Point pointer, double glare)
    {
        var quad = face.Vertices.Select(vertex => vertex.Screen).ToArray();
        var outline = BuildGeometry(quad);
        context.DrawGeometry(face.Fallback, null, outline);

        if (face.Texture is not null)
        {
            // Subdivision removes the obvious diagonal/shear produced by mapping
            // one projected cover with only two affine triangles.  More cells on
            // the large front/back faces keep the artwork stable while tilting.
            var columns = face.Name is "front" or "back" ? 8 : 2;
            var rows = face.Name is "front" or "back" ? 12 : 10;
            DrawTexturedQuadMesh(context, face.Texture, quad, columns, rows);
        }

        var light = Math.Clamp(.14 + Math.Abs(face.Vertices.Average(v => v.World.Z)) * .04, .12, .24);
        var sheen = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb((byte)(255 * light), 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(10, 255, 255, 255), .38),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), .68),
                new GradientStop(Color.FromArgb(18, 120, 145, 205), 1)
            }
        };
        context.DrawGeometry(sheen, null, outline);

        DrawPointerGlare(context, outline, quad, pointer, glare);

        // One stroke, after every fill: drawing the outline under the texture and again over it
        // doubled the edge and made it look heavier than a case edge should.
        context.DrawGeometry(null, EdgePen, outline);
    }

    /// <summary>
    /// The specular highlight that follows the pointer. It is what turns a tilting box into
    /// something that reads as a glossy card: the tilt alone gives shape, the moving highlight
    /// gives the surface.
    /// </summary>
    private static void DrawPointerGlare(
        DrawingContext context,
        Geometry outline,
        IReadOnlyList<Point> quad,
        Point pointer,
        double glare)
    {
        if (glare <= .01) return;

        var bounds = ComputeBounds(quad);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // The brush is relative to the geometry it fills, so the pointer has to be expressed in
        // that same space. Letting it travel outside 0..1 keeps the highlight sliding off the
        // edge naturally instead of sticking to the border.
        var centerX = Math.Clamp((pointer.X - bounds.X) / bounds.Width, -.35, 1.35);
        var centerY = Math.Clamp((pointer.Y - bounds.Y) / bounds.Height, -.35, 1.35);
        var center = new RelativePoint(centerX, centerY, RelativeUnit.Relative);
        var peak = (byte)Math.Clamp(96 * glare, 0, 255);

        var highlight = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = new RelativeScalar(.62, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(.62, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(peak, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb((byte)(peak * .35), 214, 230, 255), .45),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
            }
        };
        context.DrawGeometry(highlight, null, outline);
    }

    private static void DrawTexturedQuadMesh(
        DrawingContext context,
        Bitmap image,
        IReadOnlyList<Point> quad,
        int columns,
        int rows)
    {
        if(Math.Abs(quad[0].Y-quad[1].Y)<.001 && Math.Abs(quad[1].X-quad[2].X)<.001
            && Math.Abs(quad[2].Y-quad[3].Y)<.001 && Math.Abs(quad[3].X-quad[0].X)<.001
            && quad[2].X>quad[0].X && quad[2].Y>quad[0].Y)
        {
            context.DrawImage(image,new Rect(image.Size),new Rect(quad[0],quad[2]));
            return;
        }
        DrawPreparedTextureMesh(context, image, PrepareTexturedQuadMesh(image, quad, columns, rows));
    }

    private static TexturedTriangle[] PrepareTexturedQuadMesh(
        Bitmap image, IReadOnlyList<Point> quad, int columns, int rows)
    {
        var width = Math.Max(1d, image.Size.Width);
        var height = Math.Max(1d, image.Size.Height);
        var mesh = new TexturedTriangle[columns * rows * 2];
        var next = 0;

        for (var row = 0; row < rows; row++)
        {
            var v0 = (double)row / rows;
            var v1 = (double)(row + 1) / rows;
            for (var column = 0; column < columns; column++)
            {
                var u0 = (double)column / columns;
                var u1 = (double)(column + 1) / columns;

                var d00 = Bilinear(quad, u0, v0);
                var d10 = Bilinear(quad, u1, v0);
                var d11 = Bilinear(quad, u1, v1);
                var d01 = Bilinear(quad, u0, v1);

                var s00 = new Point(width * u0, height * v0);
                var s10 = new Point(width * u1, height * v0);
                var s11 = new Point(width * u1, height * v1);
                var s01 = new Point(width * u0, height * v1);

                mesh[next++] = PrepareTexturedTriangle(image, s00, s10, s01, d00, d10, d01);
                mesh[next++] = PrepareTexturedTriangle(image, s10, s11, s01, d10, d11, d01);
            }
        }
        return mesh;
    }

    private static Point Bilinear(IReadOnlyList<Point> quad, double u, double v)
    {
        var top = Lerp(quad[0], quad[1], u);
        var bottom = Lerp(quad[3], quad[2], u);
        return Lerp(top, bottom, v);
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static TexturedTriangle PrepareTexturedTriangle(
        Bitmap image,
        Point source0,
        Point source1,
        Point source2,
        Point destination0,
        Point destination1,
        Point destination2)
    {
        var transform = AffineFromTriangles(
            source0, source1, source2,
            destination0, destination1, destination2);

        // The transform stays on the exact triangle; only the clip grows, so neighbouring cells
        // overlap instead of leaving a hairline seam. The sub-pixel overlap is redrawn by the
        // later cell with its own correct texture, which is invisible at this scale.
        var clip = BuildGeometry(Expand(destination0, destination1, destination2, CellClipBleed));
        return new TexturedTriangle(transform, clip, new Rect(0, 0, image.Size.Width, image.Size.Height));
    }

    private static void DrawPreparedTextureMesh(DrawingContext context, Bitmap image, TexturedTriangle[] mesh)
    {
        foreach (var triangle in mesh)
            using (context.PushGeometryClip(triangle.Clip))
            using (context.PushTransform(triangle.Transform))
                context.DrawImage(image, triangle.ImageRect, triangle.ImageRect);
    }

    private readonly record struct TexturedTriangle(Matrix Transform, StreamGeometry Clip, Rect ImageRect);

    private static Matrix AffineFromTriangles(
        Point s0, Point s1, Point s2,
        Point d0, Point d1, Point d2)
    {
        var ux = s1.X - s0.X;
        var uy = s1.Y - s0.Y;
        var vx = s2.X - s0.X;
        var vy = s2.Y - s0.Y;
        var det = ux * vy - vx * uy;
        if (Math.Abs(det) < 0.000001)
            return Matrix.Identity;

        var dx1 = d1.X - d0.X;
        var dy1 = d1.Y - d0.Y;
        var dx2 = d2.X - d0.X;
        var dy2 = d2.Y - d0.Y;
        var m11 = (dx1 * vy - dx2 * uy) / det;
        var m21 = (ux * dx2 - vx * dx1) / det;
        var m12 = (dy1 * vy - dy2 * uy) / det;
        var m22 = (ux * dy2 - vx * dy1) / det;
        var offsetX = d0.X - m11 * s0.X - m21 * s0.Y;
        var offsetY = d0.Y - m12 * s0.X - m22 * s0.Y;
        return new Matrix(m11, m12, m21, m22, offsetX, offsetY);
    }

    /// <summary>Pushes each corner away from the triangle's centroid by <paramref name="amount"/> pixels.</summary>
    private static Point[] Expand(Point a, Point b, Point c, double amount)
    {
        var centroid = new Point((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3);
        return new[] { Push(a), Push(b), Push(c) };

        Point Push(Point point)
        {
            var dx = point.X - centroid.X;
            var dy = point.Y - centroid.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < .0001) return point;
            return new Point(point.X + dx / length * amount, point.Y + dy / length * amount);
        }
    }

    private static StreamGeometry BuildGeometry(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0) return geometry;
        using var path = geometry.Open();
        path.BeginFigure(points[0], true);
        for (var index = 1; index < points.Count; index++)
            path.LineTo(points[index]);
        path.EndFigure(true);
        return geometry;
    }

    private static Rect ComputeBounds(IEnumerable<Point> points)
    {
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        var any = false;
        foreach (var point in points)
        {
            any = true;
            minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
        }
        if (!any) return default;
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsInteractive || e.Pointer.Type == PointerType.Touch) return;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        _pointerInside = true;
        _pointerPosition = e.GetPosition(this);
        var nx = Math.Clamp((_pointerPosition.X / Bounds.Width - .5) * 2, -1, 1);
        var ny = Math.Clamp((_pointerPosition.Y / Bounds.Height - .5) * 2, -1, 1);
        _hoverYaw = nx * MaxHoverYaw;
        _hoverPitch = -ny * MaxHoverPitch;
        UpdateAnimationTarget();
    }

    private void UpdateAnimationTarget()
    {
        _targetYaw = _baseYaw + (_pointerInside ? _hoverYaw : 0);
        if (UsesGameCubeModel) _targetYaw = Math.Clamp(_targetYaw, 12, 168);
        _targetPitch = _restPitch + (_pointerInside ? _hoverPitch : 0);
        _targetGlare = _pointerInside ? 1 : 0;
        _targetLift = _pointerInside ? 1 : 0;
        _animation.Start();
    }

    private bool AnimateFrame(double elapsed)
    {
        var positionResponse = DemandAnimation.Response(.17, elapsed);
        var pitchResponse = DemandAnimation.Response(.20, elapsed);
        var surfaceResponse = DemandAnimation.Response(.12, elapsed);

        var yawDelta = _targetYaw - _yaw;
        var pitchDelta = _targetPitch - _pitch;
        var glareDelta = _targetGlare - _glare;
        var liftDelta = _targetLift - _lift;

        _yaw += yawDelta * positionResponse;
        _pitch += pitchDelta * pitchResponse;
        _glare += glareDelta * surfaceResponse;
        _lift += liftDelta * surfaceResponse;

        // The highlight and the lift settle more slowly than the rotation, so stopping on the
        // rotation alone used to freeze them mid-fade. Every channel has to be at rest.
        if (Math.Abs(yawDelta) < .025 && Math.Abs(pitchDelta) < .025 &&
            Math.Abs(glareDelta) < .004 && Math.Abs(liftDelta) < .004)
        {
            _yaw = _targetYaw;
            _pitch = _targetPitch;
            _glare = _targetGlare;
            _lift = _targetLift;
            InvalidateVisual();
            return false;
        }
        InvalidateVisual();
        return true;
    }

    private static Vec3 Rotate(Vec3 point, double yawDegrees, double pitchDegrees)
    {
        var pitch = pitchDegrees * Math.PI / 180.0;
        var yaw = yawDegrees * Math.PI / 180.0;
        var cosX = Math.Cos(pitch);
        var sinX = Math.Sin(pitch);
        var afterPitch = new Vec3(
            point.X,
            point.Y * cosX - point.Z * sinX,
            point.Y * sinX + point.Z * cosX);
        var cosY = Math.Cos(yaw);
        var sinY = Math.Sin(yaw);
        return new Vec3(
            afterPitch.X * cosY + afterPitch.Z * sinY,
            afterPitch.Y,
            -afterPitch.X * sinY + afterPitch.Z * cosY);
    }

    private static Bitmap? Load(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return CoverBitmapCache.Acquire(path);
        }
        catch
        {
            return null;
        }
    }

    private void DisposeBitmaps()
    {
        _modelCommands = null;
        _modelDrawingKey = default;
        CoverBitmapCache.Release(_front);
        CoverBitmapCache.Release(_back);
        CoverBitmapCache.Release(_spine);
        _front = _back = _spine = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animation.Stop();
        DisposeBitmaps();
    }

    private readonly record struct Vec3(double X, double Y, double Z);
    private readonly record struct ProjectedVertex(Vec3 World, Point Screen);
    private sealed record Face(
        string Name,
        ProjectedVertex[] Vertices,
        bool Visible,
        double AverageDepth,
        Bitmap? Texture,
        IBrush Fallback);
}
