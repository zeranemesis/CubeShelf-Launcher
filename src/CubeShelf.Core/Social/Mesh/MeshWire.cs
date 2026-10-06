using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// Builds a binary message. Everything on the wire is big-endian and length-prefixed; there is no
/// text format to parse, which keeps a hostile packet's room to surprise the reader small.
/// </summary>
public sealed class MeshWriter
{
    private byte[] _buffer;
    private int _length;

    public MeshWriter(int capacity = 256) => _buffer = new byte[Math.Max(16, capacity)];

    public int Length => _length;

    public MeshWriter U8(byte value)
    {
        Ensure(1)[0] = value;
        _length += 1;
        return this;
    }

    public MeshWriter U16(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(Ensure(2), value);
        _length += 2;
        return this;
    }

    public MeshWriter U32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(Ensure(4), value);
        _length += 4;
        return this;
    }

    public MeshWriter U64(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(Ensure(8), value);
        _length += 8;
        return this;
    }

    /// <summary>Bytes whose length both sides know, such as a key or a hash.</summary>
    public MeshWriter Fixed(ReadOnlySpan<byte> value)
    {
        value.CopyTo(Ensure(value.Length));
        _length += value.Length;
        return this;
    }

    /// <summary>Up to 65535 bytes, preceded by their length.</summary>
    public MeshWriter Blob(ReadOnlySpan<byte> value)
    {
        if (value.Length > ushort.MaxValue) throw new ArgumentException("Bloc trop long pour le réseau.", nameof(value));
        U16((ushort)value.Length);
        return Fixed(value);
    }

    /// <summary>Up to 16 MiB, preceded by a 32-bit length. Only for record payloads, which are capped well below.</summary>
    public MeshWriter LargeBlob(ReadOnlySpan<byte> value)
    {
        U32((uint)value.Length);
        return Fixed(value);
    }

    public MeshWriter Text(string value) => Blob(Encoding.UTF8.GetBytes(value));

    public MeshWriter Id(NodeId id)
    {
        id.WriteTo(Ensure(NodeId.Length));
        _length += NodeId.Length;
        return this;
    }

    /// <summary>Family (4 or 6), the address, the port. IPv4-mapped IPv6 is written as IPv4.</summary>
    public MeshWriter Endpoint(IPEndPoint endpoint)
    {
        var address = endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            U8(4);
            Span<byte> bytes = stackalloc byte[4];
            address.TryWriteBytes(bytes, out _);
            Fixed(bytes);
        }
        else
        {
            U8(6);
            Span<byte> bytes = stackalloc byte[16];
            address.TryWriteBytes(bytes, out _);
            Fixed(bytes);
        }
        return U16((ushort)endpoint.Port);
    }

    /// <summary>An endpoint, or a single zero byte for none.</summary>
    public MeshWriter OptionalEndpoint(IPEndPoint? endpoint) => endpoint is null ? U8(0) : Endpoint(endpoint);

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    private Span<byte> Ensure(int count)
    {
        if (_length + count > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        return _buffer.AsSpan(_length, count);
    }
}

/// <summary>
/// Reads a binary message without ever throwing on bad input: a read past the end, or a value out
/// of range, turns <see cref="Ok"/> false and every later read returns a default. The caller reads
/// everything, then checks once -- one decision point, and no path where a hostile length makes
/// the reader allocate or index out of bounds.
/// </summary>
public ref struct MeshReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _offset;

    public MeshReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _offset = 0;
        Ok = true;
    }

    public bool Ok { get; private set; }

    public int Remaining => Ok ? _data.Length - _offset : 0;

    /// <summary>Everything was read and nothing went wrong: trailing bytes are a malformed message too.</summary>
    public bool Done => Ok && _offset == _data.Length;

    public void Fail() => Ok = false;

    public byte U8()
    {
        var bytes = Take(1);
        return bytes.Length == 1 ? bytes[0] : (byte)0;
    }

    public ushort U16()
    {
        var bytes = Take(2);
        return bytes.Length == 2 ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : (ushort)0;
    }

    public uint U32()
    {
        var bytes = Take(4);
        return bytes.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : 0;
    }

    public ulong U64()
    {
        var bytes = Take(8);
        return bytes.Length == 8 ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : 0;
    }

    public ReadOnlySpan<byte> Fixed(int length) => Take(length);

    /// <summary>A length-prefixed block no longer than <paramref name="maximum"/>.</summary>
    public ReadOnlySpan<byte> Blob(int maximum = ushort.MaxValue)
    {
        var length = U16();
        if (length > maximum)
        {
            Ok = false;
            return ReadOnlySpan<byte>.Empty;
        }
        return Take(length);
    }

    public ReadOnlySpan<byte> LargeBlob(int maximum)
    {
        var length = U32();
        if (length > (uint)maximum)
        {
            Ok = false;
            return ReadOnlySpan<byte>.Empty;
        }
        return Take((int)length);
    }

    public string Text(int maximumBytes)
    {
        var bytes = Blob(maximumBytes);
        if (!Ok) return "";
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Ok = false;
            return "";
        }
    }

    public NodeId Id()
    {
        var bytes = Take(NodeId.Length);
        return bytes.Length == NodeId.Length ? new NodeId(bytes) : default;
    }

    public IPEndPoint? Endpoint()
    {
        var family = U8();
        ReadOnlySpan<byte> address = family switch
        {
            4 => Take(4),
            6 => Take(16),
            _ => Invalid()
        };
        var port = U16();
        if (!Ok || port == 0) return Invalidated();
        return new IPEndPoint(new IPAddress(address), port);
    }

    /// <summary>The counterpart of <see cref="MeshWriter.OptionalEndpoint"/>: null for the zero byte, without failing.</summary>
    public IPEndPoint? OptionalEndpoint()
    {
        if (Remaining > 0 && _data[_offset] == 0)
        {
            _offset++;
            return null;
        }
        return Endpoint();
    }

    private ReadOnlySpan<byte> Invalid()
    {
        Ok = false;
        return ReadOnlySpan<byte>.Empty;
    }

    private IPEndPoint? Invalidated()
    {
        Ok = false;
        return null;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (!Ok || count < 0 || _offset + count > _data.Length)
        {
            Ok = false;
            return ReadOnlySpan<byte>.Empty;
        }
        var slice = _data.Slice(_offset, count);
        _offset += count;
        return slice;
    }
}

/// <summary>What the network will and will not treat as an address worth talking to.</summary>
public static class MeshAddresses
{
    /// <summary>
    /// <c>CUBESHELF_MESH_ALLOW_PRIVATE=1</c>: private IPv4 addresses (10/8, 172.16/12, 192.168/16)
    /// count as the Internet. For testing on one machine or one home network only -- that is the
    /// only way two nodes without a public address can form a network. Read once, at start-up.
    /// </summary>
    public static readonly bool AllowPrivateForTesting =
        Environment.GetEnvironmentVariable("CUBESHELF_MESH_ALLOW_PRIVATE") == "1";

    private static bool IsPrivateIPv4(ReadOnlySpan<byte> bytes) =>
        bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);

    /// <summary>
    /// A unicast address on the public Internet: not private, loopback, link-local, multicast,
    /// carrier-grade NAT or documentation space. Contacts with anything else are not kept or passed
    /// on -- they mean nothing to a stranger, and passing a private address around the world leaks
    /// a little of someone's home network and invites requests aimed at other people's LANs.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var written)) return false;
        if (AllowPrivateForTesting && written == 4 && IsPrivateIPv4(bytes)) return true;

        if (written == 4)
        {
            var a = bytes[0];
            var b = bytes[1];
            return !(a == 0 || a == 10 || a == 127 || a >= 224 ||
                     (a == 100 && b >= 64 && b <= 127) ||        // carrier-grade NAT
                     (a == 169 && b == 254) ||
                     (a == 172 && b >= 16 && b <= 31) ||
                     (a == 192 && b == 168) ||
                     (a == 192 && b == 0 && bytes[2] == 0) ||
                     (a == 192 && b == 0 && bytes[2] == 2) ||    // documentation
                     (a == 198 && (b == 18 || b == 19)) ||       // benchmarking
                     (a == 198 && b == 51 && bytes[2] == 100) ||
                     (a == 203 && b == 0 && bytes[2] == 113));
        }

        // IPv6: only global unicast (2000::/3), minus documentation and the transition ranges
        // that embed some other address.
        if ((bytes[0] & 0xE0) != 0x20) return false;
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return false;  // 2001:db8::/32
        if (bytes[0] == 0x20 && bytes[1] == 0x02) return false;                                          // 6to4
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) return false;        // Teredo
        return true;
    }

    /// <summary>
    /// The neighbourhood an address belongs to for diversity limits: the /24 of an IPv4 address,
    /// the /48 of an IPv6 one -- roughly "one household or one hosting customer".
    /// </summary>
    public static string Neighbourhood(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? $"{bytes[0]}.{bytes[1]}.{bytes[2]}"
            : Convert.ToHexString(bytes, 0, 6);
    }

    /// <summary>Compares endpoints the way the network sees them: an IPv4 address and its IPv6-mapped form are one.</summary>
    public static IPEndPoint Normalize(IPEndPoint endpoint) =>
        endpoint.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port) : endpoint;
}
