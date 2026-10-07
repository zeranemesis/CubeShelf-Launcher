using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social;

/// <summary>What one CubeShelf user hands another so they can become friends.</summary>
/// <param name="PublicKey">The peer's identity, 65 bytes.</param>
/// <param name="PresenceUrl">Where that peer publishes its presence document.</param>
/// <param name="DisplayName">
/// The pseudo they chose, so the person adding them sees <c>Zera#4821</c> before confirming
/// instead of having to type a name. Empty for a version-1 code, which never carried one.
/// </param>
/// <param name="EntryPoints">
/// Version 3: a few addresses of nodes on the CubeShelf network -- the author's own when it can
/// be reached, otherwise relays it uses -- so someone with no other way in can join through them.
/// Never private addresses.
/// </param>
public sealed record FriendCodePayload(byte[] PublicKey, string PresenceUrl, string DisplayName = "", IReadOnlyList<System.Net.IPEndPoint>? EntryPoints = null)
{
    /// <summary><c>Zera#4821</c>, or <c>#4821</c> for a code that carried no pseudo.</summary>
    public string Handle => PeerName.Handle(DisplayName, PublicKey);

    public IReadOnlyList<System.Net.IPEndPoint> Seeds => EntryPoints ?? Array.Empty<System.Net.IPEndPoint>();
}

/// <summary>
/// Encodes and decodes friend codes.
///
/// The URL has to travel inside the code: with no directory and no central server, a public key
/// alone gives a friend no way to find where you publish. That makes the code long -- it is
/// pasted once, like a WireGuard peer or a Syncthing device id, not typed.
/// </summary>
public static class FriendCode
{
    // Version 2 appends the pseudo. Version 1 is still read, so a code handed out before the
    // change keeps working; it just arrives without a name. Version 3 makes the address optional
    // -- the network needs none -- and carries entry points into the network instead.
    private const string Prefix = "CSF2-";
    private const string LegacyPrefix = "CSF1-";
    private const string NetworkPrefix = "CSF3-";
    private static readonly byte[] Magic = "CSF2"u8.ToArray();
    private static readonly byte[] LegacyMagic = "CSF1"u8.ToArray();
    private static readonly byte[] NetworkMagic = "CSF3"u8.ToArray();
    public const int MaximumEntryPoints = 4;

    /// <summary>
    /// A version-3 code: the identity, the pseudo, and ways into the network. The https address
    /// is optional, kept only for someone who still publishes to a synced folder.
    /// </summary>
    public static string EncodeForNetwork(ReadOnlySpan<byte> publicKey, string? displayName, IEnumerable<System.Net.IPEndPoint> entryPoints, string? presenceUrl = null)
    {
        PeerIdentity.ValidatePublicKey(publicKey);
        var url = string.IsNullOrWhiteSpace(presenceUrl) ? "" : NormalizeUrl(presenceUrl);
        var urlBytes = Encoding.UTF8.GetBytes(url);
        if (urlBytes.Length > MaximumUrlLength)
            throw new ArgumentException("L’URL de présence est trop longue.", nameof(presenceUrl));
        var entries = entryPoints.Where(endpoint => Mesh.MeshAddresses.IsPublic(endpoint.Address)).Take(MaximumEntryPoints).ToArray();

        var writer = new Mesh.MeshWriter(160).Fixed(NetworkMagic).Fixed(publicKey);
        var nameBytes = PeerName.Encode(displayName);
        writer.U8((byte)nameBytes.Length).Fixed(nameBytes).Blob(urlBytes).U8((byte)entries.Length);
        foreach (var entry in entries) writer.Endpoint(entry);
        var body = writer.ToArray();

        var framed = new byte[body.Length + ChecksumLength];
        body.CopyTo(framed, 0);
        SHA256.HashData(body).AsSpan(0, ChecksumLength).CopyTo(framed.AsSpan(body.Length));
        return NetworkPrefix + Base64Url(framed);
    }

    private static bool TryDecodeNetwork(ReadOnlySpan<byte> body, out FriendCodePayload? payload, out string error)
    {
        payload = null;
        error = "Code ami incohérent : la longueur annoncée ne correspond pas.";
        var reader = new Mesh.MeshReader(body);
        reader.Fixed(NetworkMagic.Length);
        var publicKey = reader.Fixed(PeerIdentity.PublicKeyLength).ToArray();
        int nameLength = reader.U8();
        var nameBytes = reader.Fixed(nameLength).ToArray();
        var urlBytes = reader.Blob(MaximumUrlLength).ToArray();
        int count = reader.U8();
        if (count > MaximumEntryPoints) return false;
        var entries = new List<System.Net.IPEndPoint>();
        for (var index = 0; index < count; index++)
        {
            // Someone else wrote these: an address outside the public Internet is dropped, so a
            // code cannot aim this CubeShelf at a machine on its own network.
            if (reader.Endpoint() is { } endpoint && Mesh.MeshAddresses.IsPublic(endpoint.Address))
                entries.Add(Mesh.MeshAddresses.Normalize(endpoint));
        }
        if (!reader.Done) return false;

        string url;
        try
        {
            PeerIdentity.ValidatePublicKey(publicKey);
            url = urlBytes.Length == 0 ? "" : NormalizeUrl(Encoding.UTF8.GetString(urlBytes));
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        payload = new FriendCodePayload(publicKey, url, PeerName.Sanitize(Encoding.UTF8.GetString(nameBytes)), entries);
        error = "";
        return true;
    }
    private const int ChecksumLength = 4;
    private const int MaximumUrlLength = 2048;

    public static string Encode(ReadOnlySpan<byte> publicKey, string presenceUrl, string? displayName = null)
    {
        PeerIdentity.ValidatePublicKey(publicKey);
        var url = NormalizeUrl(presenceUrl);
        var urlBytes = Encoding.UTF8.GetBytes(url);
        if (urlBytes.Length > MaximumUrlLength)
            throw new ArgumentException("L’URL de présence est trop longue.", nameof(presenceUrl));
        var nameBytes = PeerName.Encode(displayName);

        var body = new byte[Magic.Length + PeerIdentity.PublicKeyLength + 2 + urlBytes.Length + 1 + nameBytes.Length];
        var offset = 0;
        Magic.CopyTo(body, offset);
        offset += Magic.Length;
        publicKey.CopyTo(body.AsSpan(offset));
        offset += PeerIdentity.PublicKeyLength;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset), (ushort)urlBytes.Length);
        offset += 2;
        urlBytes.CopyTo(body, offset);
        offset += urlBytes.Length;
        body[offset++] = (byte)nameBytes.Length;
        nameBytes.CopyTo(body, offset);

        var framed = new byte[body.Length + ChecksumLength];
        body.CopyTo(framed, 0);
        SHA256.HashData(body).AsSpan(0, ChecksumLength).CopyTo(framed.AsSpan(body.Length));
        return Prefix + Base64Url(framed);
    }

    public static bool TryDecode(string? code, out FriendCodePayload? payload, out string error)
    {
        payload = null;
        error = "";

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Code ami vide.";
            return false;
        }

        // Pasting out of a chat window routinely brings spaces and line breaks along.
        var cleaned = new string(code.Where(character => !char.IsWhiteSpace(character)).ToArray());
        var legacy = cleaned.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);
        var network = cleaned.StartsWith(NetworkPrefix, StringComparison.OrdinalIgnoreCase);
        if (!legacy && !network && !cleaned.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Ce n’est pas un code ami CubeShelf.";
            return false;
        }

        byte[] framed;
        try
        {
            framed = FromBase64Url(cleaned[Prefix.Length..]);
        }
        catch (FormatException)
        {
            error = "Code ami illisible : le texte a été modifié ou tronqué.";
            return false;
        }

        var minimum = Magic.Length + PeerIdentity.PublicKeyLength + 2 + ChecksumLength;
        if (framed.Length < minimum)
        {
            error = "Code ami incomplet : il manque des caractères à la fin.";
            return false;
        }

        var body = framed.AsSpan(0, framed.Length - ChecksumLength);
        var expected = SHA256.HashData(body.ToArray()).AsSpan(0, ChecksumLength);
        if (!CryptographicOperations.FixedTimeEquals(expected, framed.AsSpan(framed.Length - ChecksumLength)))
        {
            error = "Code ami corrompu : la somme de contrôle ne correspond pas.";
            return false;
        }

        // The prefix is a label a person can read; the magic inside the checksum is what counts.
        if (!body[..Magic.Length].SequenceEqual(legacy ? LegacyMagic : network ? NetworkMagic : Magic))
        {
            error = "Version de code ami non prise en charge.";
            return false;
        }

        if (network) return TryDecodeNetwork(body, out payload, out error);

        var offset = Magic.Length;
        var publicKey = body.Slice(offset, PeerIdentity.PublicKeyLength).ToArray();
        offset += PeerIdentity.PublicKeyLength;
        var urlLength = BinaryPrimitives.ReadUInt16BigEndian(body[offset..]);
        offset += 2;

        // Version 1 ends with the address; version 2 follows it with one length byte and the
        // pseudo. Either way every byte must be accounted for.
        var remaining = body.Length - offset;
        var nameLength = !legacy && remaining > urlLength ? body[offset + urlLength] : 0;
        var declared = legacy ? urlLength : urlLength + 1 + nameLength;
        if (remaining != declared)
        {
            error = "Code ami incohérent : la longueur annoncée ne correspond pas.";
            return false;
        }

        string url;
        try
        {
            PeerIdentity.ValidatePublicKey(publicKey);
            url = NormalizeUrl(Encoding.UTF8.GetString(body.Slice(offset, urlLength)));
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        // Chosen by someone else and about to be shown: cleaned, never trusted.
        var name = legacy
            ? ""
            : PeerName.Sanitize(Encoding.UTF8.GetString(body.Slice(offset + urlLength + 1, nameLength)));

        payload = new FriendCodePayload(publicKey, url, name);
        return true;
    }

    /// <summary>
    /// Finds a friend code inside whatever was pasted -- typically a whole chat message, the
    /// pseudo on one line and the code on the next. The first candidate that decodes wins, so a
    /// message quoting a broken code and then a good one still works.
    /// </summary>
    public static bool TryFind(string? text, out FriendCodePayload? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(text) || text.Length > 64 * 1024) return false;

        try
        {
            foreach (System.Text.RegularExpressions.Match match in CandidatePattern.Matches(text))
            {
                if (TryDecode(match.Value, out payload, out _)) return true;
            }
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
        }

        payload = null;
        return false;
    }

    // A version-1 code is never shorter than about a hundred characters, so eighty rules out
    // every stray "CSF1-" a message might contain without ruling out any real code.
    private static readonly System.Text.RegularExpressions.Regex CandidatePattern = new(
        "CSF[123]-[A-Za-z0-9_-]{80,}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Only https. A friend code is pasted from somewhere the user does not control, so a code
    /// naming http:// -- or file:// -- is a downgrade someone else chose for them.
    /// </summary>
    private static string NormalizeUrl(string presenceUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presenceUrl);
        var trimmed = presenceUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "L’URL de présence doit être une adresse https absolue.", nameof(presenceUrl));

        return uri.AbsoluteUri;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}
