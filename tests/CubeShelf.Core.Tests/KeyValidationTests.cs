using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

static class KeyValidationTests
{
    /// <summary>
    /// A crafted friend code whose key is not a point of P-256 used to get in: the shape was all
    /// that was checked, and the failure only came later, as an exception Windows reports in a form
    /// no caller caught -- every publish sealing for that "friend" broke. It is now refused where it
    /// enters: the code, the friends file, the network.
    /// </summary>
    public static void KeysOffTheCurveAreRefusedAtTheDoor()
    {
        using var identity = PeerIdentity.Create();
        var good = identity.PublicKey;
        Check(MeshCrypto.IsOnCurve(good), "a real key is on the curve");

        var bad = (byte[])good.Clone();
        bad[^1] ^= 0x01;
        Check(!MeshCrypto.IsOnCurve(bad), "a flipped bit leaves the curve");
        var outOfField = (byte[])good.Clone();
        outOfField.AsSpan(1, 32).Fill(0xFF);
        Check(!MeshCrypto.IsOnCurve(outOfField), "a coordinate beyond the field");

        try
        {
            PeerIdentity.ValidatePublicKey(bad);
            throw new InvalidOperationException("Échec : off-curve key validated");
        }
        catch (ArgumentException)
        {
        }

        // A code carrying it, checksum and all, as an attacker would build it.
        Check(!FriendCode.TryDecode(HandBuilt(bad, "https://example.org/p.json", "Eve"), out _, out var error) && error.Length > 0,
            "the friend code is refused");
        Check(FriendCode.TryDecode(HandBuilt(good, "https://example.org/p.json", "Ann"), out var fine, out _) && fine is not null,
            "the same code with a real key decodes");

        // A friends file edited by hand, or written by an older version, with such a key in it.
        var directory = Path.Combine(Path.GetTempPath(), "cubeshelf-curve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FriendStore(directory);
            Check(store.TryAdd(new FriendCodePayload(PeerIdentity.Create().PublicKey, "https://example.org/a.json", "Ann"), "Ann", good, out _), "real friend added");
            var json = File.ReadAllText(store.FriendsFile);
            var injected = json.TrimEnd().TrimEnd(']') + $",{{\"PublicKey\":\"{Convert.ToBase64String(bad)}\",\"DisplayName\":\"Eve\",\"PresenceUrl\":\"https://example.org/e.json\"}}]";
            File.WriteAllText(store.FriendsFile, injected);

            Check(store.Load().Count == 1, "the bad entry is not loaded");
            Check(store.ActiveRecipients().Count == 1, "nor sealed for");
            var snapshot = new PresenceSnapshot(PresenceSnapshot.CurrentVersion, "Me", DateTimeOffset.UtcNow, 1, PresenceStatus.Online,
                null, null, Array.Empty<SharedGame>(), Array.Empty<SharedMod>());
            Check(SealedPresence.Seal(identity, snapshot, store.ActiveRecipients()).Boxes.Count > 0, "publishing still works");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The fallback that computes a public point from a derived scalar agrees with the platform's
    /// own arithmetic, so a provider that needs it produces the same keys -- and the same record
    /// locations -- as every other.
    /// </summary>
    public static void DerivedKeysAreTheSameEverywhere()
    {
        for (var trial = 0; trial < 6; trial++)
        {
            using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var parameters = key.ExportParameters(true);
            var scalar = new System.Numerics.BigInteger(parameters.D, isUnsigned: true, isBigEndian: true);
            var (x, y) = MeshCrypto.MultiplyBase(scalar);
            Check(x == new System.Numerics.BigInteger(parameters.Q.X, isUnsigned: true, isBigEndian: true) &&
                  y == new System.Numerics.BigInteger(parameters.Q.Y, isUnsigned: true, isBigEndian: true),
                "our point multiplication matches the platform's");
        }

        // Same secret, same label: same key, on any machine.
        using var first = MeshCrypto.DeriveSigningKey(new byte[32], "presence|1");
        using var second = MeshCrypto.DeriveSigningKey(new byte[32], "presence|1");
        using var other = MeshCrypto.DeriveSigningKey(new byte[32], "presence|2");
        Check(MeshCrypto.PublicKeyOf(first).AsSpan().SequenceEqual(MeshCrypto.PublicKeyOf(second)), "deterministic");
        Check(!MeshCrypto.PublicKeyOf(first).AsSpan().SequenceEqual(MeshCrypto.PublicKeyOf(other)), "a label is a different key");
        Check(Convert.ToHexString(MeshCrypto.PublicKeyOf(first))[..16] == ExpectedPresence1Prefix(), "pinned value: every platform derives this exact key");
    }

    private static string ExpectedPresence1Prefix() => "04F067F673ED9D6D";

    private static string HandBuilt(byte[] key, string url, string name)
    {
        var urlBytes = Encoding.UTF8.GetBytes(url);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var body = new byte[4 + key.Length + 2 + urlBytes.Length + 1 + nameBytes.Length];
        "CSF2"u8.CopyTo(body);
        key.CopyTo(body, 4);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(4 + key.Length), (ushort)urlBytes.Length);
        urlBytes.CopyTo(body, 4 + key.Length + 2);
        body[4 + key.Length + 2 + urlBytes.Length] = (byte)nameBytes.Length;
        nameBytes.CopyTo(body, 4 + key.Length + 2 + urlBytes.Length + 1);
        var framed = body.Concat(SHA256.HashData(body).Take(4)).ToArray();
        return "CSF2-" + Convert.ToBase64String(framed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
