using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// The few primitives the network is built from, all from the BCL: P-256 for agreement and
/// signatures (the same curve the friend identities already use), AES-256-GCM, SHA-256, HKDF.
///
/// Nothing here is novel cryptography. The point of gathering it is that every key the network
/// derives says what it is for -- a domain string goes into every hash and every derivation -- so a
/// value computed for one purpose can never be replayed as another.
/// </summary>
public static class MeshCrypto
{
    /// <summary>Uncompressed P-256 point: 0x04 || X(32) || Y(32).</summary>
    public const int PublicKeyLength = 65;

    /// <summary>r || s, the IEEE P1363 form .NET produces by default.</summary>
    public const int SignatureLength = 64;

    public const int KeyLength = 32;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    private const int CoordinateLength = 32;

    /// <summary>The order of the P-256 group, for reducing a derived scalar into range.</summary>
    private static readonly BigInteger CurveOrder = BigInteger.Parse(
        "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        System.Globalization.NumberStyles.HexNumber);

    public static ECDiffieHellman NewAgreementKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] PublicKeyOf(ECDiffieHellman key) => Encode(key.ExportParameters(false).Q);

    public static byte[] PublicKeyOf(ECDsa key) => Encode(key.ExportParameters(false).Q);

    /// <summary>Shape only: the right length and the uncompressed marker. Whether it is on the curve is checked on import.</summary>
    public static bool HasPublicKeyShape(ReadOnlySpan<byte> publicKey) =>
        publicKey.Length == PublicKeyLength && publicKey[0] == 0x04;

    private static readonly BigInteger FieldPrime = BigInteger.Parse(
        "0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF",
        System.Globalization.NumberStyles.HexNumber);

    private static readonly BigInteger CurveB = BigInteger.Parse(
        "05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B",
        System.Globalization.NumberStyles.HexNumber);

    /// <summary>
    /// Whether the bytes are a point of P-256: y² = x³ − 3x + b (mod p), both coordinates reduced.
    /// Checked here, by arithmetic, rather than left to whatever the platform's import does: the
    /// answer is then the same on every OS, and a key off the curve is refused where it enters --
    /// a pasted code, a file, the network -- instead of failing later, deep in a publish, in a way
    /// each platform reports differently. P-256 has cofactor 1, so this is the whole check.
    /// </summary>
    public static bool IsOnCurve(ReadOnlySpan<byte> publicKey)
    {
        if (!HasPublicKeyShape(publicKey)) return false;
        var x = new BigInteger(publicKey.Slice(1, CoordinateLength), isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(publicKey.Slice(1 + CoordinateLength, CoordinateLength), isUnsigned: true, isBigEndian: true);
        if (x >= FieldPrime || y >= FieldPrime) return false;
        var left = y * y % FieldPrime;
        var right = ((x * x % FieldPrime * x - 3 * x + CurveB) % FieldPrime + FieldPrime) % FieldPrime;
        return left == right;
    }

    /// <summary>
    /// ECDH with a peer's public key, hashed to 32 bytes. The import rejects a point that is not on
    /// the curve, which is what stops a crafted key from steering the agreement (invalid-curve
    /// attacks); that surfaces here as a <see cref="CryptographicException"/>.
    /// </summary>
    public static byte[] Agree(ECDiffieHellman own, ReadOnlySpan<byte> peerPublicKey)
    {
        using var peer = ImportAgreementKey(peerPublicKey);
        return own.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256);
    }

    public static ECDiffieHellman ImportAgreementKey(ReadOnlySpan<byte> publicKey)
    {
        if (!IsOnCurve(publicKey)) throw new CryptographicException("Clé publique de nœud invalide.");
        try
        {
            return ECDiffieHellman.Create(PublicParameters(publicKey));
        }
        catch (Exception exception) when (exception is ArgumentException or PlatformNotSupportedException)
        {
            // Windows reports a point off the curve as "parameters not valid for this platform".
            throw new CryptographicException("Clé publique de nœud invalide.", exception);
        }
    }

    /// <summary>Verifies a signature without letting a malformed key or signature escape as an exception.</summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (!IsOnCurve(publicKey) || signature.Length != SignatureLength) return false;
        try
        {
            using var key = ECDsa.Create(PublicParameters(publicKey));
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static byte[] Sign(ECDsa key, ReadOnlySpan<byte> data) => key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>
    /// A signing key that is a pure function of <paramref name="secret"/> and <paramref name="label"/>.
    /// Whoever knows the secret can recompute it, and nobody else can. This is what lets a record's
    /// address rotate every day while staying writable only by its owner (and, for a pointer, by
    /// the one friend it is for), with nothing to store.
    /// </summary>
    public static ECDsa DeriveSigningKey(ReadOnlySpan<byte> secret, string label)
    {
        var material = Hkdf(secret, "cubeshelf-mesh-signing-key-v1|" + label, 48);
        try
        {
            // 384 bits reduced modulo n-1, plus one: the bias is below 2^-128 and zero is impossible.
            var value = new BigInteger(material, isUnsigned: true, isBigEndian: true);
            var scalar = value % (CurveOrder - 1) + 1;
            var d = new byte[CoordinateLength];
            var raw = scalar.ToByteArray(isUnsigned: true, isBigEndian: true);
            raw.CopyTo(d, CoordinateLength - raw.Length);
            try
            {
                try
                {
                    return ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = d });
                }
                catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException or ArgumentException)
                {
                    // A provider that will not compute the public point from the scalar alone:
                    // compute it here and hand over both.
                    var q = MultiplyBase(scalar);
                    return ECDsa.Create(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        D = d,
                        Q = new ECPoint { X = Coordinate(q.X), Y = Coordinate(q.Y) }
                    });
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(d);
                CryptographicOperations.ZeroMemory(raw);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static readonly BigInteger GeneratorX = BigInteger.Parse(
        "06B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296", System.Globalization.NumberStyles.HexNumber);

    private static readonly BigInteger GeneratorY = BigInteger.Parse(
        "04FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5", System.Globalization.NumberStyles.HexNumber);

    /// <summary>
    /// <paramref name="scalar"/>·G on P-256, in plain affine arithmetic. Only the fallback of
    /// <see cref="DeriveSigningKey"/> uses it, for a scalar that is not a long-term secret of
    /// anyone's identity, so the timing of BigInteger arithmetic is not a concern here.
    /// </summary>
    internal static (BigInteger X, BigInteger Y) MultiplyBase(BigInteger scalar)
    {
        (BigInteger X, BigInteger Y)? result = null;
        (BigInteger X, BigInteger Y) addend = (GeneratorX, GeneratorY);
        while (scalar > 0)
        {
            if (!scalar.IsEven) result = result is { } current ? Add(current, addend) : addend;
            addend = Add(addend, addend);
            scalar >>= 1;
        }
        return result ?? throw new CryptographicException("Scalaire nul.");

        static (BigInteger, BigInteger) Add((BigInteger X, BigInteger Y) p, (BigInteger X, BigInteger Y) q)
        {
            BigInteger slope;
            if (p.X == q.X && p.Y == q.Y)
                slope = (3 * p.X * p.X - 3) * Inverse(2 * p.Y) % FieldPrime;
            else
                slope = (q.Y - p.Y) * Inverse(q.X - p.X) % FieldPrime;
            var x = Mod(slope * slope - p.X - q.X);
            var y = Mod(slope * (p.X - x) - p.Y);
            return (x, y);
        }

        static BigInteger Mod(BigInteger value) => (value % FieldPrime + FieldPrime) % FieldPrime;

        static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), FieldPrime - 2, FieldPrime);
    }

    private static byte[] Coordinate(BigInteger value)
    {
        var bytes = new byte[CoordinateLength];
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        raw.CopyTo(bytes, CoordinateLength - raw.Length);
        return bytes;
    }

    public static byte[] Hkdf(ReadOnlySpan<byte> secret, string info, int length, ReadOnlySpan<byte> salt = default) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret.ToArray(), length, salt.ToArray(), Encoding.ASCII.GetBytes(info));

    /// <summary>SHA-256 over a domain string and the parts, each length-prefixed so no two inputs collide.</summary>
    public static byte[] Hash(string domain, params byte[][] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        var domainBytes = Encoding.ASCII.GetBytes(domain);
        BinaryPrimitives.WriteInt32BigEndian(length, domainBytes.Length);
        hash.AppendData(length);
        hash.AppendData(domainBytes);
        foreach (var part in parts)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
            hash.AppendData(length);
            hash.AppendData(part);
        }
        return hash.GetHashAndReset();
    }

    /// <summary>AES-256-GCM with a random nonce: nonce || ciphertext || tag.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var output = new byte[NonceLength + plaintext.Length + TagLength];
        var nonce = output.AsSpan(0, NonceLength);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(key, TagLength);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(NonceLength, plaintext.Length), output.AsSpan(NonceLength + plaintext.Length), associatedData);
        return output;
    }

    /// <summary>The inverse of <see cref="Seal"/>; null for anything that does not authenticate.</summary>
    public static byte[]? Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        if (sealedData.Length < NonceLength + TagLength) return null;
        var plaintext = new byte[sealedData.Length - NonceLength - TagLength];
        try
        {
            using var gcm = new AesGcm(key, TagLength);
            gcm.Decrypt(
                sealedData[..NonceLength],
                sealedData.Slice(NonceLength, plaintext.Length),
                sealedData[^TagLength..],
                plaintext,
                associatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    internal static ECParameters PublicParameters(ReadOnlySpan<byte> publicKey) => new()
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint
        {
            X = publicKey.Slice(1, CoordinateLength).ToArray(),
            Y = publicKey.Slice(1 + CoordinateLength, CoordinateLength).ToArray()
        }
    };

    private static byte[] Encode(ECPoint q)
    {
        if (q.X is not { } x || q.Y is not { } y)
            throw new CryptographicException("La clé publique générée est incomplète.");

        var encoded = new byte[PublicKeyLength];
        encoded[0] = 0x04;
        // Left-pad: a coordinate with leading zero bytes can export shorter than the field size.
        x.CopyTo(encoded, 1 + CoordinateLength - x.Length);
        y.CopyTo(encoded, 1 + CoordinateLength + CoordinateLength - y.Length);
        return encoded;
    }
}

/// <summary>
/// The proof of work that makes a node id cost something.
///
/// A node's id is a hash of its public key and a nonce, and only nonces whose work hash starts with
/// <see cref="Difficulty"/> zero bits count. Getting one id costs about four million hashes -- a
/// fraction of a second, once per run. Getting an id close to a chosen position, which is what an
/// attacker needs to sit next to a record and hide it, costs that many times the size of the
/// network. It does not make an attack impossible; it makes it expensive, and the rest of the
/// design (addresses only friends can compute, limits per IP) makes it pointless.
/// </summary>
public static class NodeProof
{
    public const int Difficulty = 22;

    public static NodeId IdOf(ReadOnlySpan<byte> publicKey, ulong nonce) =>
        NodeId.FromHash(Hash("cubeshelf-node-id-v1", publicKey, nonce));

    public static bool IsValid(ReadOnlySpan<byte> publicKey, ulong nonce, int difficulty = Difficulty) =>
        MeshCrypto.HasPublicKeyShape(publicKey) &&
        LeadingZeroBits(Hash("cubeshelf-node-work-v1", publicKey, nonce)) >= difficulty;

    /// <summary>Searches for a nonce from a random start. Checks for cancellation every few thousand tries.</summary>
    public static ulong Solve(ReadOnlySpan<byte> publicKey, int difficulty = Difficulty, CancellationToken cancellationToken = default)
    {
        var domain = Encoding.ASCII.GetBytes("cubeshelf-node-work-v1");
        var buffer = new byte[domain.Length + publicKey.Length + 8];
        domain.CopyTo(buffer, 0);
        publicKey.CopyTo(buffer.AsSpan(domain.Length));
        var nonceSlot = buffer.AsSpan(domain.Length + publicKey.Length);
        Span<byte> digest = stackalloc byte[32];

        var nonce = BinaryPrimitives.ReadUInt64BigEndian(RandomNumberGenerator.GetBytes(8));
        for (var tries = 0L; ; tries++, nonce++)
        {
            if ((tries & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteUInt64BigEndian(nonceSlot, nonce);
            SHA256.HashData(buffer, digest);
            if (LeadingZeroBits(digest) >= difficulty) return nonce;
        }
    }

    private static byte[] Hash(string domain, ReadOnlySpan<byte> publicKey, ulong nonce)
    {
        var domainBytes = Encoding.ASCII.GetBytes(domain);
        var buffer = new byte[domainBytes.Length + publicKey.Length + 8];
        domainBytes.CopyTo(buffer, 0);
        publicKey.CopyTo(buffer.AsSpan(domainBytes.Length));
        BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(domainBytes.Length + publicKey.Length), nonce);
        return SHA256.HashData(buffer);
    }

    internal static int LeadingZeroBits(ReadOnlySpan<byte> bytes)
    {
        var bits = 0;
        foreach (var value in bytes)
        {
            if (value == 0)
            {
                bits += 8;
                continue;
            }
            return bits + BitOperations.LeadingZeroCount((uint)value) - 24;
        }
        return bits;
    }
}
