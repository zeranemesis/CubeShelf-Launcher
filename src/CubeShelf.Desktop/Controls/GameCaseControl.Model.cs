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

    private bool RenderImportedModel(DrawingContext context)
    {
        if (ImportedModel.Value is not { } model) return false;
        var commands = new List<ModelDraw>();
        var boundsPoints = new List<Point>();
        foreach (var mesh in model.Meshes)
        {
            var projected = mesh.Positions.Select(ProjectVertex).ToArray();
            boundsPoints.AddRange(projected.Select(vertex => vertex.Screen));
            // All film strips and latches in this supplied model are on the
            // front. The opaque shell occludes them when viewing the back.
            if (mesh.Overlay && Rotate(new Vec3(0, 0, 1), _yaw, _pitch).Z <= 0) continue;
            if (mesh.TextureQuad is { } quad)
            {
                var normal = Rotate(mesh.Triangles[0].Normal, _yaw, _pitch);
                if (normal.Z <= .00001) continue;
                var vertices = quad.Select(index => projected[index]).ToArray();
                commands.Add(new ModelDraw(1, vertices.Average(vertex => vertex.World.Z),
                    vertices.Select(vertex => vertex.Screen).ToArray(), mesh.Palette[48], mesh.TextureSlot));
                continue;
            }
            foreach (var triangle in mesh.Triangles)
            {
                var normal = Rotate(triangle.Normal, _yaw, _pitch);
                if (normal.Z <= .00001) continue;
                var vertices = new[] { projected[triangle.A], projected[triangle.B], projected[triangle.C] };
                var light = Math.Clamp(.38 + .55 * Math.Max(0, -.4 * normal.X + .6 * normal.Y + .7 * normal.Z)
                    + Math.Pow(Math.Max(0, normal.Z), 24) * .2 * (1 - mesh.Roughness), 0, 1.2);
                var shade = Math.Clamp((int)Math.Round(light / 1.2 * 63), 0, 63);
                commands.Add(new ModelDraw(mesh.Overlay ? 2 : 0, vertices.Average(vertex => vertex.World.Z),
                    vertices.Select(vertex => vertex.Screen).ToArray(), mesh.Palette[shade], null));
            }
        }
        DrawContactShadow(context, ComputeBounds(boundsPoints));
        // Inserts lie outside the shell; latches and clear film lie outside the
        // inserts. Respect those model layers before painter sorting. Sorting a
        // whole insert against individual shell triangles by average Z alone
        // would incorrectly cover portions of the insert at oblique angles.
        foreach (var command in commands.OrderBy(command => command.Layer).ThenBy(command => command.Depth))
        {
            if (command.TextureSlot is { } slot)
            {
                Bitmap? texture = slot switch { "front" => _front, "back" => _back, _ => _spine };
                if (texture is null)
                {
                    context.DrawGeometry(command.Brush, null, BuildGeometry(command.Points));
                    continue;
                }
                var outline = BuildGeometry(command.Points);
                using (context.PushGeometryClip(outline))
                    DrawTexturedQuadMesh(context, texture, command.Points, slot == "spine" ? 2 : 8, slot == "spine" ? 10 : 12);
                DrawPointerGlare(context, outline, command.Points, _pointerPosition, _glare * .45);
            }
            else context.DrawGeometry(command.Brush, null, BuildGeometry(command.Points));
        }
        return true;
    }

    private static ImportedCase? LoadImportedCase()
    {
        try
        {
            using var resource = AssetLoader.Open(new Uri("avares://CubeShelf/Assets/Models/Jaquette_GameCube_3D_vierge.glb"));
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
    private sealed record ModelDraw(int Layer, double Depth, Point[] Points, IBrush Brush, string? TextureSlot);
}
