using System.Buffers.Binary;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CubeShelf.Desktop.Controls;

public sealed partial class GameCaseControl
{
    // This is a reader for the bundled, known model, not an arbitrary GLTF importer.
    // Geometry and material colors are shared; every case owns only its game textures.
    private static readonly Lazy<ImportedCase?> ImportedModel = new(LoadImportedCase);
    private ModelDraw[]? _modelCommands;
    private readonly List<ModelDraw> _commandBuffer = new();
    private readonly Dictionary<ModelMesh, ModelDraw[]> _meshDraws = new();
    private int _nextDrawOrder;
    private int _modelCommandCount;
    private readonly Dictionary<ModelMesh, ProjectedVertex[]> _projectedMeshes = new();
    private Rect _modelShadowBounds;
    private (double Width, double Height, double Yaw, double Pitch, double Lift, Bitmap? Front, Bitmap? Back, Bitmap? Spine, bool Detail) _modelDrawingKey;

    private bool RenderImportedModel(DrawingContext context)
    {
        if (ImportedModel.Value is not { } model) return false;
        // Pointer glare is drawn over the mesh and does not change its projection.
        var key = (Bounds.Width, Bounds.Height, _yaw, _pitch, _lift,
            _front, _back, _spine, IsInteractive);
        if (_modelCommands is null || key != _modelDrawingKey)
        {
            BuildImportedCommands(model);
            _modelDrawingKey = key;
        }
        try { DrawImportedGeometry(context); }
        finally { DiscardGpuDraws(); }
        return true;
    }

    private void BuildImportedCommands(ImportedCase model)
    {
        var commands = _commandBuffer;
        commands.Clear();
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        // The pose and camera are constant across the entire case for this frame.
        var (sinX, cosX) = Math.SinCos(_pitch * Math.PI / 180);
        var (sinY, cosY) = Math.SinCos(_yaw * Math.PI / 180);
        var focal = 1 / Math.Tan(FieldOfViewDegrees * Math.PI / 360);
        var fit = Math.Min(Bounds.Width / 2.67, Bounds.Height / 3.48) * focal * 1.72 * (1 + HoverLift * _lift);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Vec3 RotateFrame(Vec3 point)
        {
            var y = point.Y * cosX - point.Z * sinX;
            var z = point.Y * sinX + point.Z * cosX;
            return new Vec3(point.X * cosY + z * sinY, y, -point.X * sinY + z * cosY);
        }
        foreach (var mesh in model.Meshes)
        {
            if (!_meshDraws.TryGetValue(mesh, out var draws))
            {
                draws = new ModelDraw[mesh.TextureQuad is null ? mesh.Triangles.Length : 1];
                for (var index = 0; index < draws.Length; index++)
                    draws[index] = new ModelDraw(mesh.TextureQuad is null ? (mesh.Overlay ? 2 : 0) : 1,
                        0, new Point[mesh.TextureQuad is null ? 3 : 4], mesh.Palette[48], mesh.TextureSlot,
                        _nextDrawOrder++);
                _meshDraws[mesh] = draws;
            }
            if (!_projectedMeshes.TryGetValue(mesh, out var projected))
                _projectedMeshes[mesh] = projected = new ProjectedVertex[mesh.Positions.Length];
            for (var index = 0; index < projected.Length; index++)
            {
                var rotated = RotateFrame(mesh.Positions[index]);
                var scale = fit / Math.Max(.28, CameraDistance - rotated.Z);
                var screen = new Point(center.X + rotated.X * scale, center.Y - rotated.Y * scale);
                projected[index] = new ProjectedVertex(rotated, screen);
                minX = Math.Min(minX, screen.X); minY = Math.Min(minY, screen.Y);
                maxX = Math.Max(maxX, screen.X); maxY = Math.Max(maxY, screen.Y);
            }
            // All film strips and latches in this supplied model are on the
            // front. The opaque shell occludes them when viewing the back.
            if (mesh.Overlay && RotateFrame(new Vec3(0, 0, 1)).Z <= 0) continue;
            if (mesh.TextureQuad is { } quad)
            {
                // The supplied spine insert is double-sided and its triangles
                // wind inward. Use its physical outward normal for visibility.
                var normal = RotateFrame(mesh.TextureSlot == "spine" ? new Vec3(-1, 0, 0) : mesh.Triangles[0].Normal);
                if (normal.Z <= .00001) continue;
                var command = draws[0];
                var depth = 0d;
                for (var index = 0; index < quad.Length; index++)
                {
                    var vertex = projected[quad[index]];
                    command.Points[index] = vertex.Screen;
                    depth += vertex.World.Z;
                }
                command.Update(depth / quad.Length, mesh.Palette[48]);
                commands.Add(command);
                continue;
            }
            for (var triangleIndex = 0; triangleIndex < mesh.Triangles.Length; triangleIndex++)
            {
                var triangle = mesh.Triangles[triangleIndex];
                var normal = RotateFrame(triangle.Normal);
                if (normal.Z <= .00001) continue;
                var a = projected[triangle.A];
                var b = projected[triangle.B];
                var c = projected[triangle.C];
                var z = Math.Max(0, normal.Z);
                var z2 = z * z;
                var z4 = z2 * z2;
                var z8 = z4 * z4;
                var light = Math.Clamp(.38 + .55 * Math.Max(0, -.4 * normal.X + .6 * normal.Y + .7 * normal.Z)
                    + z8 * z8 * z8 * .2 * (1 - mesh.Roughness), 0, 1.2);
                var shade = Math.Clamp((int)Math.Round(light / 1.2 * 63), 0, 63);
                var command = draws[triangleIndex];
                command.Points[0] = a.Screen; command.Points[1] = b.Screen; command.Points[2] = c.Screen;
                command.Update((a.World.Z + b.World.Z + c.World.Z) / 3, mesh.Palette[shade]);
                commands.Add(command);
            }
        }
        _modelShadowBounds = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        // Inserts lie outside the shell; latches and clear film lie outside the
        // inserts. Respect those model layers before painter sorting. Sorting a
        // whole insert against individual shell triangles by average Z alone
        // would incorrectly cover portions of the insert at oblique angles.
        commands.Sort(static (a, b) =>
        {
            var comparison = a.Layer.CompareTo(b.Layer);
            if (comparison == 0) comparison = a.Depth.CompareTo(b.Depth);
            return comparison == 0 ? a.Order.CompareTo(b.Order) : comparison;
        });
        if (UsesGpuTextures)
        {
            if (_modelCommands is null || _modelCommands.Length < commands.Count)
                _modelCommands = new ModelDraw[Math.Max(commands.Count, _nextDrawOrder)];
            commands.CopyTo(_modelCommands);
            if (_modelCommandCount > commands.Count)
                Array.Clear(_modelCommands, commands.Count, _modelCommandCount - commands.Count);
            _modelCommandCount = commands.Count;
            return;
        }
        var ordered = commands.ToArray();
        var batches = new List<ModelDraw>(ordered.Length);
        for (var start = 0; start < ordered.Length;)
        {
            var first = ordered[start];
            var end = start + 1;
            if (first.TextureSlot is null && first.Brush is ISolidColorBrush { Color: { A: 255 } })
                while (end < ordered.Length && ordered[end].TextureSlot is null
                    && ordered[end].Layer == first.Layer && ReferenceEquals(ordered[end].Brush, first.Brush)) end++;
            if (end > start + 1)
            {
                // Batch adjacent equally shaded triangles without changing painter order.
                // NonZero keeps shared triangle edges inside a single filled path.
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.SetFillRule(FillRule.NonZero);
                    for (var index = start; index < end; index++)
                    {
                        var points = ordered[index].Points;
                        path.BeginFigure(points[0], true);
                        for (var vertex = 1; vertex < points.Length; vertex++) path.LineTo(points[vertex]);
                        path.EndFigure(true);
                    }
                }
                first.Outline = geometry;
            }
            batches.Add(first);
            start = end;
        }
        _modelCommands = batches.ToArray();
        _modelCommandCount = _modelCommands.Length;
    }

    private void DrawImportedGeometry(DrawingContext context)
    {
        DrawContactShadow(context, _modelShadowBounds);
        for (var index = 0; index < _modelCommandCount; index++)
        {
            var command = _modelCommands![index];
            if (UsesGpuTextures && command.TextureSlot is null)
            {
                var end = index + 1;
                while (end < _modelCommandCount && _modelCommands[end].TextureSlot is null
                    && _modelCommands[end].Layer == command.Layer) end++;
                DrawGpuShell(context, _modelCommands, index, end);
                index = end - 1;
                continue;
            }
            if (command.TextureSlot is { } slot)
            {
                Bitmap? texture = slot switch { "front" => _front, "back" => _back, _ => _spine };
                if (texture is null)
                {
                    FlushGpuDraws(context);
                    context.DrawGeometry(command.Brush, null, command.Outline);
                    continue;
                }
                if (UsesGpuTextures && DrawGpuTexture(context, texture, command.Points,
                    slot == "spine" ? 2 : 8, slot == "spine" ? 6 : 12))
                {
                    if (_glare * .45 > .01)
                    {
                        FlushGpuDraws(context);
                        DrawPointerGlare(context, command.Outline, command.Points, _pointerPosition, _glare * .45);
                    }
                    continue;
                }
                var outline = command.Outline;
                FlushGpuDraws(context);
                command.TextureMesh ??= PrepareTexturedQuadMesh(texture, command.Points,
                    slot == "spine" ? 2 : IsInteractive ? (UseCompactTextureMesh ? 4 : 8) : 3,
                    slot == "spine" ? 6 : IsInteractive ? (UseCompactTextureMesh ? 6 : 12) : 4);
                using (context.PushGeometryClip(outline))
                    DrawPreparedTextureMesh(context, texture, command.TextureMesh);
                DrawPointerGlare(context, outline, command.Points, _pointerPosition, _glare * .45);
            }
            else
            {
                FlushGpuDraws(context);
                context.DrawGeometry(command.Brush, null, command.Outline);
            }
        }
        FlushGpuDraws(context);
    }

    private static ImportedCase? LoadImportedCase()
    {
        try
        {
            var assembly = typeof(GameCaseControl).Assembly.GetName().Name;
            using var resource = AssetLoader.Open(new Uri($"avares://{assembly}/Assets/Models/Jaquette_GameCube_3D_vierge.glb"));
            using var memory = new MemoryStream();
            resource.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length < 28 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546C67
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != bytes.Length)
                throw new InvalidDataException("Invalid bundled case GLB header.");
            var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) != 0x4E4F534A)
                throw new InvalidDataException("Missing case JSON chunk.");
            using var document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
            var binHeader = checked(20 + jsonLength);
            var binLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binHeader)));
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binHeader + 4)) != 0x004E4942
                || binHeader + 8 + binLength != bytes.Length)
                throw new InvalidDataException("Invalid bundled case BIN chunk.");
            var binary = bytes.AsMemory(binHeader + 8, binLength);
            var root = document.RootElement;
            // The supplied export has identity nodes, positions baked into its meshes,
            // float positions/UVs, uint indices and uncompressed triangle primitives.
            foreach (var node in root.GetProperty("nodes").EnumerateArray())
                if (new[] { "matrix", "translation", "rotation", "scale" }.Any(name => node.TryGetProperty(name, out _)))
                    throw new InvalidDataException("Unexpected transformed case node.");
            var meshes = new List<ModelMesh>();
            foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
            {
                var name = mesh.GetProperty("name").GetString() ?? "";
                foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
                {
                    if (primitive.GetProperty("mode").GetInt32() != 4)
                        throw new InvalidDataException("The case requires triangle primitives.");
                    var attributes = primitive.GetProperty("attributes");
                    var coordinates = ReadFloats(attributes.GetProperty("POSITION").GetInt32(), "VEC3", 3);
                    // Export is Z-up with the front at negative Y. Reorient to the
                    // existing renderer's Y-up, front-positive-Z camera; preserve scale.
                    var positions = Enumerable.Range(0, coordinates.Length / 3).Select(index => new Vec3(
                        coordinates[index * 3] * (CaseHeight / .195),
                        (coordinates[index * 3 + 2] - .0975) * (CaseHeight / .195),
                        -coordinates[index * 3 + 1] * (CaseHeight / .195))).ToArray();
                    var indices = ReadIndices(primitive.GetProperty("indices").GetInt32());
                    if (indices.Length % 3 != 0 || indices.Any(index => index < 0 || index >= positions.Length))
                        throw new InvalidDataException("Invalid case triangle indices.");
                    var triangles = new List<ModelTriangle>();
                    for (var index = 0; index < indices.Length; index += 3)
                    {
                        var a = indices[index]; var b = indices[index + 1]; var c = indices[index + 2];
                        var normal = TriangleNormal(positions[a], positions[b], positions[c]);
                        triangles.Add(new ModelTriangle(a, b, c, normal));
                    }
                    var pbr = root.GetProperty("materials")[primitive.GetProperty("material").GetInt32()].GetProperty("pbrMetallicRoughness");
                    var factor = pbr.TryGetProperty("baseColorFactor", out var color)
                        ? color.EnumerateArray().Select(value => value.GetDouble()).ToArray() : new[] { 1d, 1d, 1d, 1d };
                    var palette = new IBrush[64];
                    // Apply shade to material RGB once rather than allocating brushes per frame.
                    for (var shade = 0; shade < palette.Length; shade++)
                    {
                        var light = shade * 1.2 / 63;
                        palette[shade] = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(factor[3] * 255, 0, 255),
                            (byte)Math.Clamp(factor[0] * light * 255, 0, 255),
                            (byte)Math.Clamp(factor[1] * light * 255, 0, 255),
                            (byte)Math.Clamp(factor[2] * light * 255, 0, 255)));
                    }
                    var slot = name.StartsWith("10_Jaquette_", StringComparison.Ordinal) ? "front"
                        : name.StartsWith("11_Jaquette_", StringComparison.Ordinal) ? "back"
                        : name.StartsWith("12_Jaquette_", StringComparison.Ordinal) ? "spine" : null;
                    int[]? textureQuad = null;
                    if (slot is not null)
                    {
                        var uv = ReadFloats(attributes.GetProperty("TEXCOORD_0").GetInt32(), "VEC2", 2);
                        if (positions.Length != 4 || uv.Length != 8) throw new InvalidDataException("Unexpected case insert UVs.");
                        // Trimesh export puts v=1 at the top. Avalonia's image origin is top-left.
                        textureQuad = new[] { Corner(0, 1), Corner(1, 1), Corner(1, 0), Corner(0, 0) };
                        int Corner(float u, float v) => Enumerable.Range(0, 4).Single(index =>
                            Math.Abs(uv[index * 2] - u) < .001 && Math.Abs(uv[index * 2 + 1] - v) < .001);
                    }
                    meshes.Add(new ModelMesh(positions, triangles.ToArray(), palette,
                        pbr.TryGetProperty("roughnessFactor", out var roughness) ? roughness.GetDouble() : 1,
                        slot, textureQuad, name.StartsWith("21_", StringComparison.Ordinal)
                            || name.StartsWith("30_", StringComparison.Ordinal)
                            || name.StartsWith("31_", StringComparison.Ordinal)
                            || name.StartsWith("32_", StringComparison.Ordinal)));
                }
            }
            if (meshes.Count == 0) throw new InvalidDataException("Empty case model.");
            return new ImportedCase(meshes.ToArray());

            float[] ReadFloats(int accessorIndex, string type, int components)
            {
                var accessor = root.GetProperty("accessors")[accessorIndex];
                if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != type)
                    throw new InvalidDataException("Unsupported case vertex accessor.");
                var count = accessor.GetProperty("count").GetInt32();
                var (offset, stride, viewEnd) = AccessorRange(accessor, components * 4);
                var result = new float[checked(count * components)];
                for (var index = 0; index < count; index++)
                    for (var component = 0; component < components; component++)
                    {
                        var location = checked(offset + index * stride + component * 4);
                        if (location + 4 > viewEnd) throw new InvalidDataException("Case vertex outside buffer view.");
                        var value = BinaryPrimitives.ReadSingleLittleEndian(binary.Span.Slice(location, 4));
                        if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite case vertex.");
                        result[index * components + component] = value;
                    }
                return result;
            }

            int[] ReadIndices(int accessorIndex)
            {
                var accessor = root.GetProperty("accessors")[accessorIndex];
                if (accessor.GetProperty("componentType").GetInt32() != 5125 || accessor.GetProperty("type").GetString() != "SCALAR")
                    throw new InvalidDataException("Unsupported case index accessor.");
                var count = accessor.GetProperty("count").GetInt32();
                var (offset, stride, viewEnd) = AccessorRange(accessor, 4);
                var result = new int[count];
                for (var index = 0; index < count; index++)
                {
                    var location = checked(offset + index * stride);
                    if (location + 4 > viewEnd) throw new InvalidDataException("Case index outside buffer view.");
                    result[index] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(binary.Span.Slice(location, 4)));
                }
                return result;
            }

            (int Offset, int Stride, int End) AccessorRange(JsonElement accessor, int elementSize)
            {
                var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
                if (view.GetProperty("buffer").GetInt32() != 0) throw new InvalidDataException("External case buffer.");
                var start = view.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0;
                var end = checked(start + view.GetProperty("byteLength").GetInt32());
                var offset = checked(start + (accessor.TryGetProperty("byteOffset", out var relative) ? relative.GetInt32() : 0));
                var stride = view.TryGetProperty("byteStride", out var step) ? step.GetInt32() : elementSize;
                if (start < 0 || offset < start || end > binary.Length || stride < elementSize)
                    throw new InvalidDataException("Invalid case accessor range.");
                return (offset, stride, end);
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException
            or ArgumentException or OverflowException or KeyNotFoundException)
        {
            // Keep the existing molded case if the packaged model is unavailable.
            System.Diagnostics.Trace.WriteLine($"Case GLB unavailable: {exception.Message}");
            return null;
        }
    }

    private static Vec3 TriangleNormal(Vec3 a, Vec3 b, Vec3 c)
    {
        var u = new Vec3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        var v = new Vec3(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        var n = new Vec3(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
        var length = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
        return length < 1e-12 ? new Vec3(0, 0, 0) : new Vec3(n.X / length, n.Y / length, n.Z / length);
    }

    private sealed record ImportedCase(ModelMesh[] Meshes);
    private sealed record ModelMesh(Vec3[] Positions, ModelTriangle[] Triangles, IBrush[] Palette,
        double Roughness, string? TextureSlot, int[]? TextureQuad, bool Overlay);
    private readonly record struct ModelTriangle(int A, int B, int C, Vec3 Normal);
    private sealed class ModelDraw(int layer, double depth, Point[] points, IBrush brush, string? textureSlot, int order)
    {
        public int Layer { get; } = layer;
        public double Depth { get; private set; } = depth;
        public Point[] Points { get; } = points;
        public IBrush Brush { get; private set; } = brush;
        public string? TextureSlot { get; } = textureSlot;
        public int Order { get; } = order;
        private StreamGeometry? _outline;
        public StreamGeometry Outline { get => _outline ??= BuildGeometry(Points); set => _outline = value; }
        public TexturedTriangle[]? TextureMesh { get; set; }
        public void Update(double depth, IBrush brush)
        {
            Depth = depth; Brush = brush;
            _outline = null; TextureMesh = null;
        }
    }
}
