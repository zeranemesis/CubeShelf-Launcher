using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeShelf.Core.Social;

/// <summary>One recipient's copy of the content key.</summary>
public sealed class PresenceLockbox
{
    /// <summary>
    /// Lets a reader find its own box without trying every one. Derived from the pairwise key, so
    /// only the two peers can compute it -- an onlooker cannot use it to tell friends apart.
    /// </summary>
    [JsonPropertyName("h")] public string Hint { get; set; } = "";
    [JsonPropertyName("n")] public string Nonce { get; set; } = "";
    [JsonPropertyName("k")] public string Key { get; set; } = "";
    [JsonPropertyName("t")] public string Tag { get; set; } = "";
}

/// <summary>The document as it is published: opaque to anyone who is not a friend.</summary>
public sealed class SealedPresenceEnvelope
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("n")] public string Nonce { get; set; } = "";
    [JsonPropertyName("p")] public string Payload { get; set; } = "";
    [JsonPropertyName("t")] public string Tag { get; set; } = "";
    [JsonPropertyName("b")] public List<PresenceLockbox> Boxes { get; set; } = new();
}

/// <summary>
/// Seals a presence snapshot for a set of friends and opens one addressed to us.
///
/// The payload is encrypted once under a fresh random content key; that key is then wrapped once
/// per friend. Removing a friend means leaving their box out of the next publish, with no need to
/// re-key anything else.
///
/// There is no signature. AES-GCM is authenticated and the pairwise key is known only to the two
/// peers, so a document that opens under it came from that peer. The cost of that simplification
/// is that either side could forge the other's document -- acceptable for presence between people
/// who have already chosen to trust each other, and worth stating plainly.
/// </summary>
public static class SealedPresence
{
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HintLength = 8;

    /// <summary>Boxes are padded up to a multiple of this so the count does not reveal how many friends a peer has.</summary>
    private const int BoxPadding = 8;

    /// <summary>Refuse absurd documents before allocating for them.</summary>
    private const int MaximumBoxes = 4096;
    private const int MaximumPayloadBytes = 4 * 1024 * 1024;

    private static readonly byte[] HintContext = "cubeshelf-presence-hint-v1"u8.ToArray();
    private static readonly byte[] AadContext = "cubeshelf-presence-v1"u8.ToArray();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SealedPresenceEnvelope Seal(
        PeerIdentity identity,
        PresenceSnapshot snapshot,
        IReadOnlyCollection<byte[]> recipientPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(recipientPublicKeys);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
        var contentKey = RandomNumberGenerator.GetBytes(KeyLength);
        try
        {
            var aad = AssociatedData(identity.PublicKey);
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagLength];

            using (var gcm = new AesGcm(contentKey, TagLength))
                gcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);

            var envelope = new SealedPresenceEnvelope
            {
                Version = 1,
                Nonce = Convert.ToBase64String(nonce),
                Payload = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            };

            foreach (var recipient in recipientPublicKeys)
                envelope.Boxes.Add(WrapFor(identity, recipient, contentKey, aad));

            // Shuffle before padding so a friend's position in the list carries no meaning, and
            // pad so the length only ever reveals a range.
            Shuffle(envelope.Boxes);
            while (envelope.Boxes.Count % BoxPadding != 0 || envelope.Boxes.Count == 0)
                envelope.Boxes.Add(DecoyBox());

            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Opens a document published by <paramref name="authorPublicKey"/>. Returns false for anything
    /// we cannot read, without distinguishing "not addressed to us" from "tampered with" -- the
    /// caller has nothing useful to do with that difference, and neither has an attacker.
    /// </summary>
    public static bool TryOpen(
        PeerIdentity identity,
        ReadOnlySpan<byte> authorPublicKey,
        SealedPresenceEnvelope? envelope,
        out PresenceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(identity);
        snapshot = null;

        if (envelope is null || envelope.Version != 1) return false;
        if (envelope.Boxes.Count is 0 or > MaximumBoxes) return false;

        byte[] pairwise;
        try
        {
            pairwise = identity.DeriveSharedKey(authorPublicKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return false;
        }

        try
        {
            if (!TryDecodeAll(envelope, out var nonce, out var ciphertext, out var tag)) return false;

            var hint = Hint(pairwise);
            var aad = AssociatedData(authorPublicKey);

            foreach (var box in envelope.Boxes)
            {
                if (!TryFromBase64(box.Hint, HintLength, out var boxHint) ||
                    !CryptographicOperations.FixedTimeEquals(hint, boxHint))
                    continue;

                if (!TryFromBase64(box.Nonce, NonceLength, out var boxNonce) ||
                    !TryFromBase64(box.Key, KeyLength, out var wrapped) ||
                    !TryFromBase64(box.Tag, TagLength, out var boxTag))
                    continue;

                var contentKey = new byte[KeyLength];
                try
                {
                    using (var unwrap = new AesGcm(pairwise, TagLength))
                        unwrap.Decrypt(boxNonce, wrapped, boxTag, contentKey, aad);

                    var plaintext = new byte[ciphertext.Length];
                    try
                    {
                        using (var open = new AesGcm(contentKey, TagLength))
                            open.Decrypt(nonce, ciphertext, tag, plaintext, aad);

                        snapshot = JsonSerializer.Deserialize<PresenceSnapshot>(plaintext, Json);
                        return snapshot is not null && snapshot.Version == PresenceSnapshot.CurrentVersion;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
                catch (Exception exception) when (exception is CryptographicException or JsonException)
                {
                    return false;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(contentKey);
                }
            }

            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairwise);
        }
    }

    private static PresenceLockbox WrapFor(
        PeerIdentity identity,
        byte[] recipientPublicKey,
        byte[] contentKey,
        byte[] aad)
    {
        var pairwise = identity.DeriveSharedKey(recipientPublicKey);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var wrapped = new byte[contentKey.Length];
            var tag = new byte[TagLength];
            using (var gcm = new AesGcm(pairwise, TagLength))
                gcm.Encrypt(nonce, contentKey, wrapped, tag, aad);

            return new PresenceLockbox
            {
                Hint = Convert.ToBase64String(Hint(pairwise)),
                Nonce = Convert.ToBase64String(nonce),
                Key = Convert.ToBase64String(wrapped),
                Tag = Convert.ToBase64String(tag)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pairwise);
        }
    }

    /// <summary>
    /// A decoy is pure randomness, which is exactly what a real box's fields look like from
    /// outside: ciphertext, a nonce and a MAC. Nothing distinguishes the two without a key.
    /// </summary>
    private static PresenceLockbox DecoyBox() => new()
    {
        Hint = Convert.ToBase64String(RandomNumberGenerator.GetBytes(HintLength)),
        Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceLength)),
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyLength)),
        Tag = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TagLength))
    };

    private static byte[] Hint(byte[] pairwiseKey) =>
        HMACSHA256.HashData(pairwiseKey, HintContext).AsSpan(0, HintLength).ToArray();

    private static byte[] AssociatedData(ReadOnlySpan<byte> authorPublicKey)
    {
        var aad = new byte[AadContext.Length + authorPublicKey.Length];
        AadContext.CopyTo(aad, 0);
        authorPublicKey.CopyTo(aad.AsSpan(AadContext.Length));
        return aad;
    }

    private static bool TryDecodeAll(
        SealedPresenceEnvelope envelope,
        out byte[] nonce,
        out byte[] ciphertext,
        out byte[] tag)
    {
        nonce = Array.Empty<byte>();
        ciphertext = Array.Empty<byte>();
        tag = Array.Empty<byte>();

        if (!TryFromBase64(envelope.Nonce, NonceLength, out nonce) ||
            !TryFromBase64(envelope.Tag, TagLength, out tag))
            return false;

        try
        {
            ciphertext = Convert.FromBase64String(envelope.Payload);
        }
        catch (FormatException)
        {
            return false;
        }

        return ciphertext.Length is > 0 and <= MaximumPayloadBytes;
    }

    private static bool TryFromBase64(string value, int expectedLength, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }
        return decoded.Length == expectedLength;
    }

    private static void Shuffle(List<PresenceLockbox> boxes)
    {
        for (var index = boxes.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (boxes[index], boxes[swap]) = (boxes[swap], boxes[index]);
        }
    }

    public static string ToJson(SealedPresenceEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, Json);

    public static SealedPresenceEnvelope? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SealedPresenceEnvelope>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Describe(SealedPresenceEnvelope envelope) =>
        $"v{envelope.Version}, {envelope.Boxes.Count} boxes, " +
        $"{Encoding.UTF8.GetByteCount(envelope.Payload)} B payload";
}
