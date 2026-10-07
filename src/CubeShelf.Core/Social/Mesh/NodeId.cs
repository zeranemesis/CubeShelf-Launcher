using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// A 256-bit position in the network. Nodes, and the records they hold, live at these positions;
/// "close" means a small XOR distance, as in Kademlia.
///
/// Stored as four big-endian words so comparing two distances is four integer comparisons, not a
/// loop over bytes -- a lookup sorts candidates by distance constantly.
/// </summary>
public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
{
    public const int Length = 32;

    private readonly ulong _a, _b, _c, _d;

    private NodeId(ulong a, ulong b, ulong c, ulong d)
    {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
    }

    public NodeId(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length) throw new ArgumentException("Identifiant de nœud invalide.", nameof(bytes));
        _a = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        _b = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        _c = BinaryPrimitives.ReadUInt64BigEndian(bytes[16..]);
        _d = BinaryPrimitives.ReadUInt64BigEndian(bytes[24..]);
    }

    public static NodeId Random() => new(RandomNumberGenerator.GetBytes(Length));

    /// <summary>The position a 32-byte hash names, for locating records.</summary>
    public static NodeId FromHash(ReadOnlySpan<byte> hash) => new(hash);

    public NodeId Xor(NodeId other) => new(_a ^ other._a, _b ^ other._b, _c ^ other._c, _d ^ other._d);

    /// <summary>How many leading bits are zero: 256 for the zero id. Shared prefix length, once XORed.</summary>
    public int LeadingZeroBits()
    {
        if (_a != 0) return BitOperations.LeadingZeroCount(_a);
        if (_b != 0) return 64 + BitOperations.LeadingZeroCount(_b);
        if (_c != 0) return 128 + BitOperations.LeadingZeroCount(_c);
        if (_d != 0) return 192 + BitOperations.LeadingZeroCount(_d);
        return 256;
    }

    /// <summary>A copy of this id with bit <paramref name="index"/> (0 = most significant) flipped.</summary>
    public NodeId FlipBit(int index)
    {
        if (index is < 0 or >= 256) throw new ArgumentOutOfRangeException(nameof(index));
        var mask = 1UL << (63 - index % 64);
        return (index / 64) switch
        {
            0 => new(_a ^ mask, _b, _c, _d),
            1 => new(_a, _b ^ mask, _c, _d),
            2 => new(_a, _b, _c ^ mask, _d),
            _ => new(_a, _b, _c, _d ^ mask)
        };
    }

    /// <summary>
    /// A random id that shares exactly <paramref name="prefixLength"/> leading bits with this one,
    /// for refreshing one bucket of the routing table.
    /// </summary>
    public NodeId RandomWithPrefix(int prefixLength)
    {
        if (prefixLength is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(prefixLength));
        Span<byte> mine = stackalloc byte[Length];
        WriteTo(mine);
        Span<byte> random = stackalloc byte[Length];
        RandomNumberGenerator.Fill(random);
        for (var bit = 0; bit < 256; bit++)
        {
            var mask = (byte)(0x80 >> (bit % 8));
            if (bit < prefixLength)
                random[bit / 8] = (byte)((random[bit / 8] & ~mask) | (mine[bit / 8] & mask));
            else if (bit == prefixLength)
                random[bit / 8] = (byte)((random[bit / 8] & ~mask) | (~mine[bit / 8] & mask));
        }
        return new NodeId(random);
    }

    public void WriteTo(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, _a);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _b);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..], _c);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..], _d);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        WriteTo(bytes);
        return bytes;
    }

    public int CompareTo(NodeId other)
    {
        var compared = _a.CompareTo(other._a);
        if (compared != 0) return compared;
        compared = _b.CompareTo(other._b);
        if (compared != 0) return compared;
        compared = _c.CompareTo(other._c);
        return compared != 0 ? compared : _d.CompareTo(other._d);
    }

    public bool Equals(NodeId other) => _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    public override bool Equals(object? obj) => obj is NodeId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
    public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);
    public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);

    /// <summary>Short hex, for logs and tests. Never shown to a user.</summary>
    public override string ToString() => _a.ToString("x16");

    /// <summary>Orders candidates by their distance to <paramref name="target"/>, closest first.</summary>
    public static IComparer<NodeId> ByDistanceTo(NodeId target) =>
        Comparer<NodeId>.Create((x, y) => x.Xor(target).CompareTo(y.Xor(target)));
}
