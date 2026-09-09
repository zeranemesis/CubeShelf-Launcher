using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CubeShelf.Launcher.Controls;

public sealed class GameCase3D : UserControl
{
    private readonly Viewport3D _viewport = new();
    private readonly Model3DGroup _root = new();
    private readonly ModelVisual3D _visual = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);

    private double _baseYaw;
    private bool _flipped;

    public GameCase3D()
    {
        Background = Brushes.Transparent;
        Content = _viewport;

        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 0, 5.6),
            LookDirection = new Vector3D(0, 0, -5.6),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 34
        };

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(110, 110, 125)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.5, -1)));
        scene.Children.Add(_root);

        var transforms = new Transform3DGroup();
        transforms.Children.Add(new RotateTransform3D(_pitch));
        transforms.Children.Add(new RotateTransform3D(_yaw));
        _root.Transform = transforms;

        _visual.Content = scene;
        _viewport.Children.Add(_visual);

        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => ResetTilt();
        MouseLeftButtonDown += (_, _) => Flip();
    }

    public void SetCover(CoverVariant? cover)
    {
        _root.Children.Clear();

        var shell = Material(Color.FromRgb(15, 18, 25));
        AddBox(_root, 2.08, 2.92, 0.18, shell);

        if (cover is null)
            return;

        AddPlaneFront(_root, cover.FrontFullPath, 2.00, 2.80, 0.095);
        AddPlaneBack(_root, cover.BackFullPath, 2.00, 2.80, -0.095);
        AddPlaneSpine(_root, cover.SpineFullPath, -1.045, 2.80, 0.16);

        ResetOrientation();
    }

    public void Flip()
    {
        _flipped = !_flipped;
        _baseYaw = _flipped ? 180 : 0;
        ResetTilt();
    }

    public void ShowFront()
    {
        _flipped = false;
        _baseYaw = 0;
        ResetTilt();
    }

    private void ResetOrientation()
    {
        _flipped = false;
        _baseYaw = 0;
        ResetTilt();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var nx = Math.Clamp((p.X / ActualWidth - 0.5) * 2.0, -1.0, 1.0);
        var ny = Math.Clamp((p.Y / ActualHeight - 0.5) * 2.0, -1.0, 1.0);

        _yaw.Angle = _baseYaw + nx * 19;
        _pitch.Angle = -ny * 8;
    }

    private void ResetTilt()
    {
        _yaw.Angle = _baseYaw;
        _pitch.Angle = 0;
    }

    private static Material Material(Color color)
        => new DiffuseMaterial(new SolidColorBrush(color));

    private static Material ImageMaterial(string path)
    {
        if (!File.Exists(path))
            return Material(Color.FromRgb(38, 44, 59));

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        brush.Freeze();

        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(brush));
        group.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)), 48));
        return group;
    }

    private static void AddBox(Model3DGroup target, double w, double h, double d, Material material)
    {
        var x = w / 2;
        var y = h / 2;
        var z = d / 2;

        target.Children.Add(Quad(
            new Point3D(-x, -y, z), new Point3D(x, -y, z),
            new Point3D(x, y, z), new Point3D(-x, y, z), material));

        target.Children.Add(Quad(
            new Point3D(x, -y, -z), new Point3D(-x, -y, -z),
            new Point3D(-x, y, -z), new Point3D(x, y, -z), material));

        target.Children.Add(Quad(
            new Point3D(-x, -y, -z), new Point3D(-x, -y, z),
            new Point3D(-x, y, z), new Point3D(-x, y, -z), material));

        target.Children.Add(Quad(
            new Point3D(x, -y, z), new Point3D(x, -y, -z),
            new Point3D(x, y, -z), new Point3D(x, y, z), material));

        target.Children.Add(Quad(
            new Point3D(-x, y, z), new Point3D(x, y, z),
            new Point3D(x, y, -z), new Point3D(-x, y, -z), material));

        target.Children.Add(Quad(
            new Point3D(-x, -y, -z), new Point3D(x, -y, -z),
            new Point3D(x, -y, z), new Point3D(-x, -y, z), material));
    }

    private static void AddPlaneFront(Model3DGroup target, string path, double w, double h, double z)
    {
        var x = w / 2;
        var y = h / 2;
        target.Children.Add(TexturedQuad(
            new Point3D(-x, -y, z), new Point3D(x, -y, z),
            new Point3D(x, y, z), new Point3D(-x, y, z), ImageMaterial(path)));
    }

    private static void AddPlaneBack(Model3DGroup target, string path, double w, double h, double z)
    {
        var x = w / 2;
        var y = h / 2;
        target.Children.Add(TexturedQuad(
            new Point3D(x, -y, z), new Point3D(-x, -y, z),
            new Point3D(-x, y, z), new Point3D(x, y, z), ImageMaterial(path)));
    }

    private static void AddPlaneSpine(Model3DGroup target, string path, double x, double h, double d)
    {
        var y = h / 2;
        var z = d / 2;
        target.Children.Add(TexturedQuad(
            new Point3D(x, -y, -z), new Point3D(x, -y, z),
            new Point3D(x, y, z), new Point3D(x, y, -z), ImageMaterial(path)));
    }

    private static GeometryModel3D Quad(
        Point3D p0, Point3D p1, Point3D p2, Point3D p3, Material material)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { p0, p1, p2, p3 },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };

        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D TexturedQuad(
        Point3D p0, Point3D p1, Point3D p2, Point3D p3, Material material)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection { p0, p1, p2, p3 },
            TextureCoordinates = new PointCollection
            {
                new Point(0, 1), new Point(1, 1),
                new Point(1, 0), new Point(0, 0)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };

        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
