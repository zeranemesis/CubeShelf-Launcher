using System.Security.Cryptography;

namespace CubeShelf.Core.Social;

/// <summary>
/// This installation's long-term identity: a P-256 key pair whose public half <em>is</em> the
/// user's identity. There is no account and no server that issues it.
///
/// P-256 rather than the more fashionable X25519 because .NET 8 ships it in the BCL, and
/// CubeShelf.Core is worth keeping at one dependency. The security margin is not the weak
/// point of a friends list.
/// </summary>
public sealed class PeerIdentity : IDisposable
{
    /// <summary>Uncompressed P-256 point: 0x04 || X(32) || Y(32).</summary>
    public const int PublicKeyLength = 65;

    private const int CoordinateLength = 32;

    /// <summary>
    /// Mixed into every derived key so a shared secret from this scheme can never collide with
    /// one derived for some other purpose, now or in a later version.
    /// </summary>
    private static readonly byte[] DerivationContext = "cubeshelf-friend-v1"u8.ToArray();

    private readonly ECDiffieHellman _key;
    private bool _disposed;

    private PeerIdentity(ECDiffieHellman key)
    {
        _key = key;
        PublicKey = ExportPublicKey(key);
    }

    /// <summary>The identity itself, safe to publish. 65 bytes.</summary>
    public byte[] PublicKey { get; }

    public static PeerIdentity Create() =>
        new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>
    /// Loads the identity from <paramref name="path"/>, creating and persisting one on first run.
    /// A corrupt file is not silently replaced: losing the key means every friend has to re-add
    /// you, so the caller is told rather than quietly given a new identity.
    /// </summary>
    public static PeerIdentity LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);

        if (File.Exists(full))
        {
            byte[] pkcs8;
            try
            {
                pkcs8 = Convert.FromBase64String(File.ReadAllText(full).Trim());
            }
            catch (FormatException exception)
            {
                throw new CryptographicException(
                    $"L’identité CubeShelf ({full}) est illisible. La supprimer en créera une " +
                    "nouvelle, mais tes amis devront t’ajouter de nouveau.", exception);
            }

            var restored = ECDiffieHellman.Create();
            try
            {
                restored.ImportPkcs8PrivateKey(pkcs8, out _);
                return new PeerIdentity(restored);
            }
            catch
            {
                restored.Dispose();
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
        }

        var identity = Create();
        identity.Save(full);
        return identity;
    }

    public void Save(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var pkcs8 = _key.ExportPkcs8PrivateKey();
        try
        {
            var temporary = full + ".tmp";
            File.WriteAllText(temporary, Convert.ToBase64String(pkcs8));
            RestrictToOwner(temporary);
            File.Move(temporary, full, true);
            RestrictToOwner(full);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }

    /// <summary>
    /// The 32-byte key this installation shares with one friend, from ECDH plus a salt that both
    /// sides compute identically. Nobody else can derive it, which is what lets AES-GCM stand in
    /// for a signature later: a document that opens under this key came from that friend.
    /// </summary>
    public byte[] DeriveSharedKey(ReadOnlySpan<byte> peerPublicKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var peer = ImportPublicKey(peerPublicKey);
        return _key.DeriveKeyFromHash(
            peer.PublicKey,
            HashAlgorithmName.SHA256,
            PairSalt(PublicKey, peerPublicKey),
            null);
    }

    /// <summary>
    /// Salt for a pair of peers. Ordering the two public keys makes it symmetric, so both sides
    /// reach the same value without agreeing on who is "first".
    /// </summary>
    private static byte[] PairSalt(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var aFirst = a.SequenceCompareTo(b) <= 0;
        var first = aFirst ? a : b;
        var second = aFirst ? b : a;

        var buffer = new byte[DerivationContext.Length + first.Length + second.Length];
        DerivationContext.CopyTo(buffer, 0);
        first.CopyTo(buffer.AsSpan(DerivationContext.Length));
        second.CopyTo(buffer.AsSpan(DerivationContext.Length + first.Length));
        return SHA256.HashData(buffer);
    }

    public static void ValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != PublicKeyLength || publicKey[0] != 0x04)
            throw new ArgumentException("Clé publique CubeShelf invalide.", nameof(publicKey));
    }

    private static ECDiffieHellman ImportPublicKey(ReadOnlySpan<byte> publicKey)
    {
        ValidatePublicKey(publicKey);
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = publicKey.Slice(1, CoordinateLength).ToArray(),
                Y = publicKey.Slice(1 + CoordinateLength, CoordinateLength).ToArray()
            }
        };

        // Validate() rejects a point that is not on the curve, which is the check that stops a
        // crafted "friend code" from steering the key agreement.
        parameters.Validate();
        return ECDiffieHellman.Create(parameters);
    }

    private static byte[] ExportPublicKey(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        if (q.X is not { } x || q.Y is not { } y)
            throw new CryptographicException("La clé publique générée est incomplète.");

        var encoded = new byte[PublicKeyLength];
        encoded[0] = 0x04;
        // Left-pad: a coordinate with leading zero bytes can export shorter than the field size.
        x.CopyTo(encoded, 1 + CoordinateLength - x.Length);
        y.CopyTo(encoded, 1 + CoordinateLength + CoordinateLength - y.Length);
        return encoded;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
            // A filesystem that cannot express permissions is not a reason to refuse to run.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _key.Dispose();
    }
}
