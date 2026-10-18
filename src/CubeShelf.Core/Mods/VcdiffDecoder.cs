namespace CubeShelf.Core.Mods;

/// <summary>
/// Applies a VCDIFF delta (RFC 3284) as written by xdelta3: the default code table,
/// VCD_SOURCE and VCD_TARGET windows and xdelta3's per-window Adler-32 checksum.
/// Deltas with secondary compression (xdelta3 -S djw/lzma) or a custom code table are
/// refused. Super Mario Eclipse ships as one, against the North American disc.
/// </summary>
public static class VcdiffDecoder
{
    private const byte Noop = 0, Add = 1, Run = 2, Copy = 3;
    private const int SNear = 4, SSame = 3;

    private readonly record struct Instruction(byte Type1, byte Size1, byte Mode1, byte Type2, byte Size2, byte Mode2);

    private static readonly Instruction[] CodeTable = BuildCodeTable();

    // RFC 3284 section 5.6
    private static Instruction[] BuildCodeTable()
    {
        var table = new List<Instruction> { new(Run, 0, 0, Noop, 0, 0) };
        for (var size = 0; size < 18; size++) table.Add(new(Add, (byte)size, 0, Noop, 0, 0));
        for (var mode = 0; mode < 9; mode++)
        {
            table.Add(new(Copy, 0, (byte)mode, Noop, 0, 0));
            for (var size = 4; size < 19; size++) table.Add(new(Copy, (byte)size, (byte)mode, Noop, 0, 0));
        }
        for (var mode = 0; mode < 6; mode++)
            for (var add = 1; add < 5; add++)
                for (var copy = 4; copy < 7; copy++)
                    table.Add(new(Add, (byte)add, 0, Copy, (byte)copy, (byte)mode));
        for (var mode = 6; mode < 9; mode++)
            for (var add = 1; add < 5; add++)
                table.Add(new(Add, (byte)add, 0, Copy, 4, (byte)mode));
        for (var mode = 0; mode < 9; mode++) table.Add(new(Copy, 4, (byte)mode, Add, 1, 0));
        if (table.Count != 256) throw new InvalidOperationException("VCDIFF code table");
        return table.ToArray();
    }

    /// <summary>
    /// Writes <paramref name="outputPath"/> = the delta applied to <paramref name="sourcePath"/>.
    /// Progress reports the fraction of the delta read. A failed window checksum throws, so a
    /// source other than the one the delta was made against cannot go unnoticed.
    /// </summary>
    public static async Task ApplyAsync(string sourcePath, string deltaPath, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        await using var delta = new FileStream(deltaPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20, true);

        var magic = new byte[5];
        await ReadExactlyAsync(delta, magic, cancellationToken);
        if (magic[0] != 0xD6 || magic[1] != 0xC3 || magic[2] != 0xC4 || magic[3] != 0x00)
            throw new InvalidDataException("Ce fichier n’est pas un patch xdelta (VCDIFF).");
        var header = magic[4];
        if ((header & 1) != 0) throw new InvalidDataException("Patch xdelta compressé (-S) : non pris en charge.");
        if ((header & 2) != 0) throw new InvalidDataException("Patch xdelta à table de codes personnalisée : non pris en charge.");
        if ((header & 4) != 0) await SkipAsync(delta, ReadVarint(delta), cancellationToken);

        byte[] segment = Array.Empty<byte>(), target = Array.Empty<byte>();
        byte[] data = Array.Empty<byte>(), instructions = Array.Empty<byte>(), addresses = Array.Empty<byte>();
        long written = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indicator = delta.ReadByte();
            if (indicator < 0) break;
            var segmentLength = 0;
            if ((indicator & 3) != 0)
            {
                segmentLength = checked((int)ReadVarint(delta));
                var segmentPosition = ReadVarint(delta);
                Grow(ref segment, segmentLength);
                var from = (indicator & 1) != 0 ? source : (Stream)output;
                if (segmentPosition + segmentLength > from.Length)
                    throw new InvalidDataException("Le patch lit au-delà de la fin de l’image source.");
                from.Position = segmentPosition;
                await ReadExactlyAsync(from, segment.AsMemory(0, segmentLength), cancellationToken);
                output.Position = written;
            }
            ReadVarint(delta); // length of the delta encoding
            var targetLength = checked((int)ReadVarint(delta));
            if (delta.ReadByte() != 0)
                throw new InvalidDataException("Fenêtres de patch compressées : non prises en charge.");
            var dataLength = checked((int)ReadVarint(delta));
            var instructionLength = checked((int)ReadVarint(delta));
            var addressLength = checked((int)ReadVarint(delta));
            uint? checksum = null;
            if ((indicator & 4) != 0)
            {
                var sum = new byte[4];
                await ReadExactlyAsync(delta, sum, cancellationToken);
                checksum = (uint)(sum[0] << 24 | sum[1] << 16 | sum[2] << 8 | sum[3]);
            }
            Grow(ref data, dataLength);
            Grow(ref instructions, instructionLength);
            Grow(ref addresses, addressLength);
            await ReadExactlyAsync(delta, data.AsMemory(0, dataLength), cancellationToken);
            await ReadExactlyAsync(delta, instructions.AsMemory(0, instructionLength), cancellationToken);
            await ReadExactlyAsync(delta, addresses.AsMemory(0, addressLength), cancellationToken);

            Grow(ref target, targetLength);
            DecodeWindow(target.AsSpan(0, targetLength), segment.AsSpan(0, segmentLength),
                data.AsSpan(0, dataLength), instructions.AsSpan(0, instructionLength), addresses.AsSpan(0, addressLength));
            if (checksum is { } expected && Adler32(target.AsSpan(0, targetLength)) != expected)
                throw new InvalidDataException(
                    $"Le patch ne correspond pas à cette image (somme de contrôle fausse à l’octet {written}).");
            await output.WriteAsync(target.AsMemory(0, targetLength), cancellationToken);
            written += targetLength;
            progress?.Report(delta.Length == 0 ? 1 : (double)delta.Position / delta.Length);
        }
        output.SetLength(written);
    }

    private static void DecodeWindow(Span<byte> target, ReadOnlySpan<byte> segment, ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> instructions, ReadOnlySpan<byte> addresses)
    {
        Span<long> near = stackalloc long[SNear];
        var same = new long[SSame * 256];
        int nextSlot = 0, targetPosition = 0, dataPosition = 0, instructionPosition = 0, addressPosition = 0;
        var segmentLength = segment.Length;
        while (instructionPosition < instructions.Length)
        {
            var entry = CodeTable[instructions[instructionPosition++]];
            for (var half = 0; half < 2; half++)
            {
                var (type, size, mode) = half == 0
                    ? (entry.Type1, (int)entry.Size1, entry.Mode1)
                    : (entry.Type2, (int)entry.Size2, entry.Mode2);
                if (type == Noop) continue;
                if (size == 0) size = checked((int)ReadVarint(instructions, ref instructionPosition));
                if (targetPosition + size > target.Length)
                    throw new InvalidDataException("Fenêtre de patch incohérente.");
                switch (type)
                {
                    case Add:
                        data.Slice(dataPosition, size).CopyTo(target.Slice(targetPosition, size));
                        dataPosition += size;
                        targetPosition += size;
                        break;
                    case Run:
                        target.Slice(targetPosition, size).Fill(data[dataPosition++]);
                        targetPosition += size;
                        break;
                    default:
                    {
                        long here = segmentLength + targetPosition, address;
                        if (mode == 0) address = ReadVarint(addresses, ref addressPosition);
                        else if (mode == 1) address = here - ReadVarint(addresses, ref addressPosition);
                        else if (mode < 2 + SNear) address = near[mode - 2] + ReadVarint(addresses, ref addressPosition);
                        else address = same[(mode - 2 - SNear) * 256 + addresses[addressPosition++]];
                        near[nextSlot] = address;
                        nextSlot = (nextSlot + 1) % SNear;
                        same[address % (SSame * 256)] = address;
                        if (address < 0 || address >= here)
                            throw new InvalidDataException("Copie au-delà des données déjà décodées.");
                        var remaining = size;
                        if (address < segmentLength)
                        {
                            var count = (int)Math.Min(remaining, segmentLength - address);
                            segment.Slice((int)address, count).CopyTo(target.Slice(targetPosition, count));
                            targetPosition += count;
                            remaining -= count;
                            address = segmentLength;
                        }
                        // From the target: may overlap what it writes, repeating the pattern.
                        var from = (int)(address - segmentLength);
                        while (remaining > 0)
                        {
                            var count = Math.Min(remaining, targetPosition - from);
                            target.Slice(from, count).CopyTo(target.Slice(targetPosition, count));
                            targetPosition += count;
                            from += count;
                            remaining -= count;
                        }
                        break;
                    }
                }
            }
        }
        if (targetPosition != target.Length || dataPosition != data.Length || addressPosition != addresses.Length)
            throw new InvalidDataException("Les sections d’une fenêtre du patch ne concordent pas.");
    }

    /// <summary>zlib's Adler-32, which xdelta3 stores per window.</summary>
    public static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        while (bytes.Length > 0)
        {
            var block = bytes[..Math.Min(bytes.Length, 5552)];
            foreach (var value in block)
            {
                a += value;
                b += a;
            }
            a %= Mod;
            b %= Mod;
            bytes = bytes[block.Length..];
        }
        return b << 16 | a;
    }

    private static long ReadVarint(Stream stream)
    {
        long value = 0;
        for (var count = 0; count < 10; count++)
        {
            var next = stream.ReadByte();
            if (next < 0) throw new InvalidDataException("Patch xdelta tronqué.");
            value = value << 7 | (uint)(next & 0x7F);
            if ((next & 0x80) == 0) return value;
        }
        throw new InvalidDataException("Entier VCDIFF trop long.");
    }

    private static long ReadVarint(ReadOnlySpan<byte> buffer, ref int position)
    {
        long value = 0;
        for (var count = 0; count < 10; count++)
        {
            if (position >= buffer.Length) throw new InvalidDataException("Fenêtre de patch tronquée.");
            var next = buffer[position++];
            value = value << 7 | (uint)(next & 0x7F);
            if ((next & 0x80) == 0) return value;
        }
        throw new InvalidDataException("Entier VCDIFF trop long.");
    }

    private static void Grow(ref byte[] buffer, int length)
    {
        if (buffer.Length < length) buffer = new byte[length];
    }

    private static async Task SkipAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        var scratch = new byte[(int)Math.Min(count, 1 << 16)];
        while (count > 0)
        {
            var read = await stream.ReadAsync(scratch.AsMemory(0, (int)Math.Min(count, scratch.Length)), cancellationToken);
            if (read == 0) throw new InvalidDataException("Patch xdelta tronqué.");
            count -= read;
        }
    }

    private static Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
        ReadExactlyAsync(stream, buffer.AsMemory(), cancellationToken);

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Patch ou image tronqué.");
        }
    }
}
