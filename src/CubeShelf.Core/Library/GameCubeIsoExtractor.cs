using System.Buffers.Binary;
using System.Text;

namespace CubeShelf.Core.Library;

internal static class GameCubeIsoExtractor
{
    private sealed record FstEntry(bool IsDirectory, int NameOffset, uint OffsetOrParent, uint SizeOrNext);

    public static async Task ExtractFilesAsync(string isoPath, string destination, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        await using var stream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, true);
        if (stream.Length < 0x42C) throw new InvalidDataException("Image GameCube invalide : en-tête trop court.");
        var header = new byte[0x42C];
        await ReadExactlyAsync(stream, header, cancellationToken);
        var fstOffset = ReadBe32(header, 0x424);
        var fstSize = ReadBe32(header, 0x428);
        if (fstOffset == 0 || fstSize == 0 || (long)fstOffset + fstSize > stream.Length)
            throw new InvalidDataException("FST GameCube invalide.");
        stream.Position = fstOffset;
        var root = new byte[12];
        await ReadExactlyAsync(stream, root, cancellationToken);
        var entryCount = ReadBe32(root, 8);
        if ((ReadBe32(root, 0) & 0xFF000000) == 0 || entryCount == 0 || entryCount > 500_000)
            throw new InvalidDataException("Table de fichiers GameCube invalide.");
        var tableBytes = checked((int)entryCount * 12);
        var fst = new byte[tableBytes];
        stream.Position = fstOffset;
        await ReadExactlyAsync(stream, fst, cancellationToken);
        var entries = new FstEntry[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            var offset = index * 12;
            var word = ReadBe32(fst, offset);
            entries[index] = new((word & 0xFF000000) != 0, (int)(word & 0x00FFFFFF),
                ReadBe32(fst, offset + 4), ReadBe32(fst, offset + 8));
        }
        var stringsOffset = fstOffset + (uint)tableBytes;
        var stringsLength = (int)Math.Max(0, Math.Min(stream.Length - stringsOffset, (long)fstSize - tableBytes));
        var strings = new byte[stringsLength];
        stream.Position = stringsOffset;
        await ReadExactlyAsync(stream, strings, cancellationToken);
        string Name(int offset)
        {
            if (offset < 0 || offset >= strings.Length) return "_invalid";
            var end = offset;
            while (end < strings.Length && strings[end] != 0) end++;
            var name = Encoding.UTF8.GetString(strings, offset, end - offset);
            foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
            return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "_" : name;
        }
        var files = new List<(string Relative, uint Offset, uint Size)>();
        void Walk(int start, int end, string directory)
        {
            var index = start;
            while (index < end && index < entries.Length)
            {
                var entry = entries[index];
                var name = Name(entry.NameOffset);
                if (entry.IsDirectory)
                {
                    var next = checked((int)entry.SizeOrNext);
                    if (next <= index || next > entries.Length) throw new InvalidDataException("Arborescence FST invalide.");
                    Walk(index + 1, next, Path.Combine(directory, name));
                    index = next;
                }
                else { files.Add((Path.Combine(directory, name), entry.OffsetOrParent, entry.SizeOrNext)); index++; }
            }
        }
        Walk(1, checked((int)entryCount), "");
        long total = files.Sum(file => (long)file.Size), completed = 0;
        var buffer = new byte[1024 * 1024];
        var rootPath = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, file.Relative));
            if (!target.StartsWith(rootPath, comparison)) throw new InvalidDataException("Chemin FST non sûr.");
            if ((long)file.Offset + file.Size > stream.Length) throw new InvalidDataException("Entrée FST hors de l’image.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            stream.Position = file.Offset;
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true);
            long remaining = file.Size;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read <= 0) throw new EndOfStreamException();
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                completed += read;
                if (total > 0) progress?.Report((double)completed / total);
            }
        }
        progress?.Report(1);
    }

    private static uint ReadBe32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(buffer, offset, 4));

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
