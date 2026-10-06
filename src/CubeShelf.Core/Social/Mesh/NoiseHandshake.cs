using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// The Noise XX handshake (noiseprotocol.org, revision 34), over P-256, AES-256-GCM and SHA-256.
///
/// XX because two nodes usually meet knowing only an address: each side learns the other's static
/// key inside the handshake, encrypted, and proves it holds the private half. Both directions get
/// forward-secret keys from fresh ephemeral keys, and the transcript hash at the end
/// (<see cref="HandshakeHash"/>) binds everything said -- which is what a friend's identity proof is
/// later tied to, so it cannot be lifted from one session into another.
///
/// Followed to the letter of the specification, with P-256 in place of the 25519 the
/// specification names: the DH output is SHA-256 of the shared X coordinate (32 bytes) and public
/// keys travel uncompressed (65 bytes). The protocol name says so, so this can never be confused
/// with a standard Noise_XX_25519 peer.
///
/// Any failure -- a point off the curve, a tag that does not verify, a message of the wrong
/// length -- makes the read return null and the handshake unusable. Nothing is retried inside.
/// </summary>
public sealed class NoiseHandshake : IDisposable
{
    public const string ProtocolName = "Noise_XX_P256_AESGCM_SHA256";
    private const int HashLength = 32;
    private const int TagLength = 16;
    private const int KeyLength = MeshCrypto.PublicKeyLength;

    private readonly bool _initiator;
    private readonly ECDiffieHellman _static;
    private readonly byte[] _staticPublic;
    private readonly ECDiffieHellman _ephemeral;
    private readonly byte[] _ephemeralPublic;

    private byte[] _ck;
    private byte[] _h;
    private byte[]? _k;
    private ulong _n;
    private int _step;
    private bool _failed;
    private bool _disposed;

    private byte[]? _remoteEphemeral;

    private NoiseHandshake(bool initiator, ECDiffieHellman staticKey, ReadOnlySpan<byte> prologue, ECDiffieHellman? ephemeral)
    {
        _initiator = initiator;
        _static = staticKey ?? throw new ArgumentNullException(nameof(staticKey));
        _staticPublic = MeshCrypto.PublicKeyOf(staticKey);
        _ephemeral = ephemeral ?? MeshCrypto.NewAgreementKey();
        _ephemeralPublic = MeshCrypto.PublicKeyOf(_ephemeral);

        var name = Encoding.ASCII.GetBytes(ProtocolName);
        _h = name.Length <= HashLength ? Pad(name) : SHA256.HashData(name);
        _ck = (byte[])_h.Clone();
        MixHash(prologue);
    }

    /// <summary>The side that sends the first message.</summary>
    /// <param name="ephemeral">For tests only: a fixed ephemeral key. Never pass one otherwise.</param>
    public static NoiseHandshake Initiator(ECDiffieHellman staticKey, ReadOnlySpan<byte> prologue, ECDiffieHellman? ephemeral = null) =>
        new(true, staticKey, prologue, ephemeral);

    public static NoiseHandshake Responder(ECDiffieHellman staticKey, ReadOnlySpan<byte> prologue, ECDiffieHellman? ephemeral = null) =>
        new(false, staticKey, prologue, ephemeral);

    /// <summary>The other side's static key, once the handshake has revealed it.</summary>
    public byte[]? RemoteStatic { get; private set; }

    /// <summary>The transcript hash once complete: identical on both sides, unique to this session.</summary>
    public byte[] HandshakeHash => IsComplete ? (byte[])_h.Clone() : throw new InvalidOperationException("Poignée de main inachevée.");

    public bool IsComplete => _step == 3 && !_failed;

    public bool IsInitiator => _initiator;

    // ------------------------------------------------------------------ initiator

    /// <summary>→ e. The payload travels in clear: nothing secret belongs in it.</summary>
    public byte[] WriteMessage1(ReadOnlySpan<byte> payload)
    {
        Expect(_initiator, 0);
        var message = new byte[KeyLength + payload.Length];
        _ephemeralPublic.CopyTo(message, 0);
        MixHash(_ephemeralPublic);
        EncryptAndHash(payload, message.AsSpan(KeyLength));
        _step = 1;
        return message;
    }

    /// <summary>← e, ee, s, es. Returns the responder's payload, or null if anything is wrong.</summary>
    public byte[]? ReadMessage2(ReadOnlySpan<byte> message)
    {
        Expect(_initiator, 1);
        try
        {
            if (message.Length < KeyLength + KeyLength + TagLength + TagLength) return Failed();
            var remoteEphemeral = message[..KeyLength].ToArray();
            if (!MeshCrypto.HasPublicKeyShape(remoteEphemeral)) return Failed();
            _remoteEphemeral = remoteEphemeral;
            MixHash(remoteEphemeral);
            MixKey(Dh(_ephemeral, remoteEphemeral));

            var remoteStatic = DecryptAndHash(message.Slice(KeyLength, KeyLength + TagLength));
            if (remoteStatic is null || !MeshCrypto.HasPublicKeyShape(remoteStatic)) return Failed();
            RemoteStatic = remoteStatic;
            MixKey(Dh(_ephemeral, remoteStatic));

            var payload = DecryptAndHash(message[(KeyLength + KeyLength + TagLength)..]);
            if (payload is null) return Failed();
            _step = 2;
            return payload;
        }
        catch (CryptographicException)
        {
            return Failed();
        }
    }

    /// <summary>→ s, se. Completes the handshake on this side.</summary>
    public byte[] WriteMessage3(ReadOnlySpan<byte> payload)
    {
        Expect(_initiator, 2);
        var message = new byte[KeyLength + TagLength + payload.Length + TagLength];
        EncryptAndHash(_staticPublic, message.AsSpan(0, KeyLength + TagLength));
        MixKey(Dh(_static, _remoteEphemeral!));
        EncryptAndHash(payload, message.AsSpan(KeyLength + TagLength));
        _step = 3;
        return message;
    }

    // ------------------------------------------------------------------ responder

    public byte[]? ReadMessage1(ReadOnlySpan<byte> message)
    {
        Expect(!_initiator, 0);
        try
        {
            if (message.Length < KeyLength) return Failed();
            var remoteEphemeral = message[..KeyLength].ToArray();
            if (!MeshCrypto.HasPublicKeyShape(remoteEphemeral)) return Failed();
            _remoteEphemeral = remoteEphemeral;
            MixHash(remoteEphemeral);
            var payload = DecryptAndHash(message[KeyLength..]);
            if (payload is null) return Failed();
            _step = 1;
            return payload;
        }
        catch (CryptographicException)
        {
            return Failed();
        }
    }

    public byte[] WriteMessage2(ReadOnlySpan<byte> payload)
    {
        Expect(!_initiator, 1);
        var message = new byte[KeyLength + KeyLength + TagLength + payload.Length + TagLength];
        _ephemeralPublic.CopyTo(message, 0);
        MixHash(_ephemeralPublic);
        MixKey(Dh(_ephemeral, _remoteEphemeral!));
        EncryptAndHash(_staticPublic, message.AsSpan(KeyLength, KeyLength + TagLength));
        MixKey(Dh(_static, _remoteEphemeral!));
        EncryptAndHash(payload, message.AsSpan(KeyLength + KeyLength + TagLength));
        _step = 2;
        return message;
    }

    public byte[]? ReadMessage3(ReadOnlySpan<byte> message)
    {
        Expect(!_initiator, 2);
        try
        {
            if (message.Length < KeyLength + TagLength + TagLength) return Failed();
            var remoteStatic = DecryptAndHash(message[..(KeyLength + TagLength)]);
            if (remoteStatic is null || !MeshCrypto.HasPublicKeyShape(remoteStatic)) return Failed();
            RemoteStatic = remoteStatic;
            MixKey(Dh(_ephemeral, remoteStatic));
            var payload = DecryptAndHash(message[(KeyLength + TagLength)..]);
            if (payload is null) return Failed();
            _step = 3;
            return payload;
        }
        catch (CryptographicException)
        {
            return Failed();
        }
    }

    /// <summary>
    /// The two transport keys: what this side sends with, and what it receives with. The
    /// initiator's first key is the responder's second, as in the specification's Split().
    /// </summary>
    public (byte[] Send, byte[] Receive) Split()
    {
        if (!IsComplete) throw new InvalidOperationException("Poignée de main inachevée.");
        var (first, second) = Hkdf2(_ck, ReadOnlySpan<byte>.Empty);
        return _initiator ? (first, second) : (second, first);
    }

    // ------------------------------------------------------------------ symmetric state

    private void MixHash(ReadOnlySpan<byte> data)
    {
        var buffer = new byte[_h.Length + data.Length];
        _h.CopyTo(buffer, 0);
        data.CopyTo(buffer.AsSpan(_h.Length));
        _h = SHA256.HashData(buffer);
    }

    private void MixKey(byte[] inputKeyMaterial)
    {
        var (ck, k) = Hkdf2(_ck, inputKeyMaterial);
        CryptographicOperations.ZeroMemory(_ck);
        CryptographicOperations.ZeroMemory(inputKeyMaterial);
        if (_k is not null) CryptographicOperations.ZeroMemory(_k);
        _ck = ck;
        _k = k;
        _n = 0;
    }

    private void EncryptAndHash(ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        if (_k is null)
        {
            plaintext.CopyTo(destination);
            MixHash(destination[..plaintext.Length]);
            return;
        }

        using var gcm = new AesGcm(_k, TagLength);
        gcm.Encrypt(Nonce(_n++), plaintext, destination[..plaintext.Length], destination.Slice(plaintext.Length, TagLength), _h);
        MixHash(destination[..(plaintext.Length + TagLength)]);
    }

    private byte[]? DecryptAndHash(ReadOnlySpan<byte> ciphertext)
    {
        if (_k is null)
        {
            var clear = ciphertext.ToArray();
            MixHash(ciphertext);
            return clear;
        }

        if (ciphertext.Length < TagLength) return null;
        var plaintext = new byte[ciphertext.Length - TagLength];
        using (var gcm = new AesGcm(_k, TagLength))
        {
            try
            {
                gcm.Decrypt(Nonce(_n), ciphertext[..plaintext.Length], ciphertext[plaintext.Length..], plaintext, _h);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
        _n++;
        MixHash(ciphertext);
        return plaintext;
    }

    /// <summary>The specification's HKDF with two outputs, on HMAC-SHA256.</summary>
    private static (byte[] First, byte[] Second) Hkdf2(byte[] chainingKey, ReadOnlySpan<byte> inputKeyMaterial)
    {
        var temp = HMACSHA256.HashData(chainingKey, inputKeyMaterial);
        var first = HMACSHA256.HashData(temp, new byte[] { 0x01 });
        var input = new byte[first.Length + 1];
        first.CopyTo(input, 0);
        input[^1] = 0x02;
        var second = HMACSHA256.HashData(temp, input);
        CryptographicOperations.ZeroMemory(temp);
        return (first, second);
    }

    /// <summary>AESGCM nonces in Noise: 32 zero bits, then the counter big-endian.</summary>
    internal static byte[] Nonce(ulong counter)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    private static byte[] Dh(ECDiffieHellman own, byte[] remotePublic) => MeshCrypto.Agree(own, remotePublic);

    private static byte[] Pad(byte[] name)
    {
        var padded = new byte[HashLength];
        name.CopyTo(padded, 0);
        return padded;
    }

    private void Expect(bool role, int step)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new InvalidOperationException("Cette poignée de main a échoué.");
        if (!role || _step != step) throw new InvalidOperationException("Message de poignée de main hors séquence.");
    }

    private byte[]? Failed()
    {
        _failed = true;
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ephemeral.Dispose();
        CryptographicOperations.ZeroMemory(_ck);
        if (_k is not null) CryptographicOperations.ZeroMemory(_k);
    }
}
