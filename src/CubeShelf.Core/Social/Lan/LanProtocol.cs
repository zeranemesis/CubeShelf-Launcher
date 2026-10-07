using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeShelf.Core.Social.Lan;

/// <summary>
/// What one CubeShelf says on the local network, a few times a minute.
///
/// It names nobody. <see cref="Tags"/> holds one short tag per friend, derived from the key that
/// pair already shares and from the current ten-minute window, so a friend recognises us and a
/// stranger sees noise that changes every ten minutes -- nothing to follow from one café to the
/// next. Like the lockboxes of a presence document, the list is padded and shuffled, so its
/// length does not count our friends either.
///
/// Only while the user has asked to be found ("add from the local network", like pairing a
/// Bluetooth device) does it also carry <see cref="PublicKey"/> and <see cref="Name"/>.
/// </summary>
public sealed class LanAnnouncement
{
    [JsonPropertyName("cs")] public int Protocol { get; set; } = LanProtocol.Version;

    /// <summary>Random per run, so an instance ignores its own broadcasts.</summary>
    [JsonPropertyName("i")] public string Instance { get; set; } = "";

    /// <summary>Where to connect for the document and for introductions.</summary>
    [JsonPropertyName("p")] public int Port { get; set; }

    /// <summary>The sequence of the document on offer, so a friend only connects when it changed.</summary>
    [JsonPropertyName("s")] public long Sequence { get; set; }

    [JsonPropertyName("t")] public List<string> Tags { get; set; } = new();

    [JsonPropertyName("k")] public string? PublicKey { get; set; }
    [JsonPropertyName("n")] public string? Name { get; set; }
}

/// <summary>One line of the TCP exchange, in either direction. Unused fields stay null.</summary>
public sealed class LanMessage
{
    [JsonPropertyName("op")] public string? Op { get; set; }
    [JsonPropertyName("purpose")] public string? Purpose { get; set; }
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("why")] public string? Why { get; set; }
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("k")] public string? PublicKey { get; set; }
    [JsonPropertyName("n")] public string? Name { get; set; }
    [JsonPropertyName("u")] public string? Url { get; set; }
    [JsonPropertyName("nonce")] public string? Nonce { get; set; }
    [JsonPropertyName("proof")] public string? Proof { get; set; }
    [JsonPropertyName("doc")] public string? Document { get; set; }

    /// <summary>The port the speaker listens on, so the other side can come back with an answer.</summary>
    [JsonPropertyName("p")] public int? Port { get; set; }
}

/// <summary>Constants and the few pieces of cryptography the local network uses.</summary>
public static class LanProtocol
{
    public const int Version = 1;

    /// <summary>The UDP port announcements are broadcast on.</summary>
    public const int AnnouncementPort = 47913;

    /// <summary>Under a typical MTU, so an announcement is never fragmented.</summary>
    public const int MaximumAnnouncementBytes = 1400;

    /// <summary>Friend tags per announcement; more friends than this spread over several.</summary>
    public const int MaximumTagsPerAnnouncement = 72;

    /// <summary>The tag list is padded up to a multiple of this.</summary>
    public const int TagPadding = 8;

    public const int TagBytes = 8;
    public const int NonceBytes = 16;

    /// <summary>Request lines are small; the one large thing on the wire is a document coming back.</summary>
    public const int MaximumRequestBytes = 16 * 1024;
    public const int MaximumResponseBytes = (int)PresencePolicy.MaximumDocumentBytes + 64 * 1024;

    public static readonly TimeSpan AnnouncementInterval = TimeSpan.FromSeconds(15);

    /// <summary>A peer silent for this long is no longer listed as on the network.</summary>
    public static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(50);

    /// <summary>How long one asks to be found; long enough to walk to the other PC.</summary>
    public static readonly TimeSpan DiscoverableFor = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The window tags rotate on.</summary>
    public static readonly TimeSpan TagEpoch = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false
    };

    public static long EpochOf(DateTimeOffset now) => now.ToUnixTimeSeconds() / (long)TagEpoch.TotalSeconds;

    /// <summary>
    /// The tag a pair recognises each other by during <paramref name="epoch"/>. Computable only by
    /// the two of them, because only they can derive <paramref name="pairwiseKey"/>.
    /// </summary>
    public static string Tag(byte[] pairwiseKey, long epoch)
    {
        var input = Encoding.ASCII.GetBytes("cubeshelf-lan-tag-v1|" + epoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToBase64String(HMACSHA256.HashData(pairwiseKey, input).AsSpan(0, TagBytes));
    }

    public static string RandomTag() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TagBytes));

    public static string RandomNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

    /// <summary>
    /// Proof that the speaker holds the private half of the key it announced: an HMAC under the
    /// pairwise key over both nonces, in an order that differs by role so one side's proof can
    /// never be replayed as the other's.
    /// </summary>
    public static string Proof(byte[] pairwiseKey, string role, string firstNonce, string secondNonce)
    {
        var input = Encoding.ASCII.GetBytes("cubeshelf-lan-introduce-v1|" + role + "|" + firstNonce + "|" + secondNonce);
        return Convert.ToBase64String(HMACSHA256.HashData(pairwiseKey, input));
    }

    public static bool ProofMatches(byte[] pairwiseKey, string role, string firstNonce, string secondNonce, string? proof)
    {
        if (string.IsNullOrEmpty(proof)) return false;
        byte[] given;
        try
        {
            given = Convert.FromBase64String(proof);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromBase64String(Proof(pairwiseKey, role, firstNonce, secondNonce));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    public static byte[] Serialize(LanAnnouncement announcement) => JsonSerializer.SerializeToUtf8Bytes(announcement, Json);

    public static LanAnnouncement? ParseAnnouncement(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length is 0 or > MaximumAnnouncementBytes) return null;
        try
        {
            var announcement = JsonSerializer.Deserialize<LanAnnouncement>(datagram, Json);
            return announcement is { Protocol: Version } &&
                   announcement.Port is > 0 and <= 65535 &&
                   announcement.Instance.Length is > 0 and <= 64 &&
                   announcement.Tags.Count <= MaximumTagsPerAnnouncement + TagPadding
                ? announcement
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(LanMessage message) => JsonSerializer.Serialize(message, Json);

    public static LanMessage? ParseMessage(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<LanMessage>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A public key off the wire, or null for anything that is not one.</summary>
    public static byte[]? DecodeKey(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            var key = Convert.FromBase64String(encoded);
            PeerIdentity.ValidatePublicKey(key);
            return key;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// An address a peer offers for its document: https or nothing. Anything else would turn a
    /// LAN introduction into a way of pointing our poller at an arbitrary host.
    /// </summary>
    public static string SafeUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Length <= 2048 &&
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : "";
}
