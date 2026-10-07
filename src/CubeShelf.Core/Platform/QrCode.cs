using System.Text;

namespace CubeShelf.Core.Platform;

/// <summary>
/// A QR code (ISO/IEC 18004), byte mode, for short text such as the phone link.
///
/// Written here rather than taken from a package because CubeShelf ships no third-party code and
/// this needs very little of the standard: one mode, one error correction level at a time, and
/// the usual mask selection. The structure follows the specification step by step -- data
/// codewords, Reed-Solomon over GF(256) with the 0x11D polynomial, interleaving, function
/// patterns, zigzag placement, masking, format and version information.
/// </summary>
public sealed class QrCode
{
    public enum ErrorCorrection
    {
        Low = 0,
        Medium = 1,
        Quartile = 2,
        High = 3
    }

    // Per level (L, M, Q, H) and version (index 1..40): EC codewords per block, and number of blocks.
    private static readonly int[][] EccCodewordsPerBlock =
    {
        new[] { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        new[] { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
        new[] { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        new[] { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 }
    };

    private static readonly int[][] ErrorCorrectionBlocks =
    {
        new[] { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
        new[] { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
        new[] { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
        new[] { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 }
    };

    // The two format bits the standard assigns to each level, in the order of ErrorCorrection.
    private static readonly int[] FormatBitsOf = { 1, 0, 3, 2 };

    private readonly bool[,] _modules;
    private readonly bool[,] _isFunction;

    public int Version { get; }
    public int Size { get; }
    public ErrorCorrection Level { get; }
    public int Mask { get; private set; }

    /// <summary>Dark or light at column <paramref name="x"/>, row <paramref name="y"/>.</summary>
    public bool this[int x, int y] => _modules[y, x];

    private QrCode(int version, ErrorCorrection level, byte[] dataCodewords)
    {
        Version = version;
        Level = level;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        DrawCodewords(AddEccAndInterleave(dataCodewords));

        // The mask with the lowest penalty, as the standard asks: a reader copes with any, but
        // the chosen one avoids patterns that look like finders or long runs.
        var best = 0;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            var penalty = Penalty();
            if (penalty < bestPenalty)
            {
                best = mask;
                bestPenalty = penalty;
            }
            ApplyMask(mask); // XOR again: undone
        }

        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    /// <summary>Encodes <paramref name="text"/> (UTF-8, byte mode) in the smallest version that fits.</summary>
    public static QrCode Encode(string text, ErrorCorrection level = ErrorCorrection.Medium)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = Encoding.UTF8.GetBytes(text);

        for (var version = 1; version <= 40; version++)
        {
            var capacityBits = DataCodewords(version, level) * 8;
            var countBits = version <= 9 ? 8 : 16;
            var needed = 4 + countBits + data.Length * 8;
            if (needed > capacityBits) continue;

            var bits = new List<bool>(capacityBits);
            Append(bits, 0b0100, 4);              // byte mode
            Append(bits, data.Length, countBits);
            foreach (var b in data) Append(bits, b, 8);
            Append(bits, 0, Math.Min(4, capacityBits - bits.Count)); // terminator
            Append(bits, 0, (8 - bits.Count % 8) % 8);                 // to a byte boundary
            for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);

            var codewords = new byte[bits.Count / 8];
            for (var index = 0; index < bits.Count; index++)
                if (bits[index]) codewords[index >> 3] |= (byte)(1 << (7 - (index & 7)));
            return new QrCode(version, level, codewords);
        }

        throw new ArgumentException("Texte trop long pour un QR code.", nameof(text));
    }

    private static void Append(List<bool> bits, int value, int count)
    {
        for (var index = count - 1; index >= 0; index--) bits.Add(((value >> index) & 1) != 0);
    }

    // ------------------------------------------------------------------ capacity

    private static int RawDataModules(int version)
    {
        var result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            var alignments = version / 7 + 2;
            result -= (25 * alignments - 10) * alignments - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int version, ErrorCorrection level) =>
        RawDataModules(version) / 8 -
        EccCodewordsPerBlock[(int)level][version] * ErrorCorrectionBlocks[(int)level][version];

    // ------------------------------------------------------------------ error correction

    private byte[] AddEccAndInterleave(byte[] data)
    {
        var blocks = ErrorCorrectionBlocks[(int)Level][Version];
        var eccLength = EccCodewordsPerBlock[(int)Level][Version];
        var rawCodewords = RawDataModules(Version) / 8;
        var shortBlocks = blocks - rawCodewords % blocks;
        var shortLength = rawCodewords / blocks;

        var divisor = ReedSolomonDivisor(eccLength);
        var built = new byte[blocks][];
        for (int index = 0, offset = 0; index < blocks; index++)
        {
            var dataLength = shortLength - eccLength + (index < shortBlocks ? 0 : 1);
            var chunk = data.AsSpan(offset, dataLength).ToArray();
            offset += dataLength;

            var block = new byte[shortLength + 1];
            chunk.CopyTo(block, 0);
            var ecc = ReedSolomonRemainder(chunk, divisor);
            ecc.CopyTo(block, block.Length - eccLength);
            built[index] = block;
        }

        var result = new byte[rawCodewords];
        for (int position = 0, written = 0; position < built[0].Length; position++)
        {
            for (var block = 0; block < built.Length; block++)
            {
                // Short blocks have one data codeword fewer; that slot is skipped, not filled.
                if (position != shortLength - eccLength || block >= shortBlocks)
                    result[written++] = built[block][position];
            }
        }
        return result;
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var step = 0; step < degree; step++)
        {
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = (byte)Multiply(result[index], root);
                if (index + 1 < result.Length) result[index] ^= result[index + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var value in data)
        {
            var factor = value ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var index = 0; index < result.Length; index++)
                result[index] ^= (byte)Multiply(divisor[index], factor);
        }
        return result;
    }

    private static int Multiply(int x, int y)
    {
        var z = 0;
        for (var bit = 7; bit >= 0; bit--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> bit) & 1) * x;
        }
        return z;
    }

    // ------------------------------------------------------------------ drawing

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    private void DrawFunctionPatterns()
    {
        for (var index = 0; index < Size; index++)
        {
            SetFunction(6, index, index % 2 == 0);
            SetFunction(index, 6, index % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions();
        for (var i = 0; i < positions.Length; i++)
        {
            for (var j = 0; j < positions.Length; j++)
            {
                // Not where a finder already is.
                if ((i == 0 && j == 0) || (i == 0 && j == positions.Length - 1) || (i == positions.Length - 1 && j == 0))
                    continue;
                DrawAlignment(positions[i], positions[j]);
            }
        }

        DrawFormatBits(0); // reserved now, written for real once the mask is chosen
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                    SetFunction(xx, yy, distance != 2 && distance != 4);
            }
        }
    }

    private void DrawAlignment(int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1) return Array.Empty<int>();
        var count = Version / 7 + 2;
        var step = Version == 32 ? 26 : (Version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int index = count - 1, position = Size - 7; index >= 1; index--, position -= step)
            result[index] = position;
        return result;
    }

    private void DrawFormatBits(int mask)
    {
        var data = FormatBitsOf[(int)Level] << 3 | mask;
        var remainder = data;
        for (var step = 0; step < 10; step++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        var bits = (data << 10 | remainder) ^ 0x5412;

        for (var i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (var i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

        for (var i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
        for (var i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
        SetFunction(8, Size - 8, true); // the module that is always dark
    }

    private void DrawVersion()
    {
        if (Version < 7) return;
        var remainder = Version;
        for (var step = 0; step < 12; step++) remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        var bits = Version << 12 | remainder;
        for (var i = 0; i < 18; i++)
        {
            var dark = Bit(bits, i);
            int a = Size - 11 + i % 3, b = i / 3;
            SetFunction(a, b, dark);
            SetFunction(b, a, dark);
        }
    }

    private void DrawCodewords(byte[] data)
    {
        var bit = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5; // the vertical timing pattern
            for (var vertical = 0; vertical < Size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vertical : vertical;
                    if (!_isFunction[y, x] && bit < data.Length * 8)
                    {
                        _modules[y, x] = Bit(data[bit >> 3], 7 - (bit & 7));
                        bit++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                };
                if (invert && !_isFunction[y, x]) _modules[y, x] = !_modules[y, x];
            }
        }
    }

    /// <summary>The four penalty rules of the standard (runs, blocks, finder-like patterns, balance).</summary>
    private int Penalty()
    {
        var result = 0;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var line = 0; line < Size; line++)
            {
                var run = 1;
                for (var index = 1; index < Size; index++)
                {
                    bool previous = At(pass, line, index - 1), current = At(pass, line, index);
                    if (current == previous)
                    {
                        run++;
                        if (run == 5) result += 3;
                        else if (run > 5) result++;
                    }
                    else
                    {
                        run = 1;
                    }
                }

                for (var index = 0; index + 10 < Size + 4; index++)
                {
                    if (FinderLike(pass, line, index)) result += 40;
                }
            }
        }

        for (var y = 0; y < Size - 1; y++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var color = _modules[y, x];
                if (color == _modules[y, x + 1] && color == _modules[y + 1, x] && color == _modules[y + 1, x + 1])
                    result += 3;
            }
        }

        var dark = 0;
        foreach (var module in _modules) if (module) dark++;
        var total = Size * Size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        result += k * 10;
        return result;
    }

    private bool At(int pass, int line, int index) => pass == 0 ? _modules[line, index] : _modules[index, line];

    /// <summary>1:1:3:1:1 with four light modules on one side; outside the symbol counts as light.</summary>
    private bool FinderLike(int pass, int line, int start)
    {
        static bool Matches(Func<int, bool> dark, int at, bool[] pattern)
        {
            for (var i = 0; i < pattern.Length; i++)
                if (dark(at + i) != pattern[i]) return false;
            return true;
        }

        bool Dark(int index) => index >= 0 && index < Size && At(pass, line, index);
        var core = new[] { true, false, true, true, true, false, true };
        var before = new[] { false, false, false, false }.Concat(core).ToArray();
        var after = core.Concat(new[] { false, false, false, false }).ToArray();
        return Matches(Dark, start - 4, before) || Matches(Dark, start, after);
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    /// <summary>A rough text rendering, for tests and logs: '#' dark, '.' light.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++) builder.Append(_modules[y, x] ? '#' : '.');
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
