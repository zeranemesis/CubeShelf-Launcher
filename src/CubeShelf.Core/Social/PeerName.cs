using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social;

/// <summary>
/// A pseudo and the four digits that go with it: <c>Zera#4821</c>.
///
/// The digits are not chosen and not assigned by anyone. They are read off the public key, so
/// the same person has the same tag on every machine, and two people who both call themselves
/// Zera can be told apart at a glance.
///
/// What the tag is not: a way to find someone, or a proof of who they are. With no directory
/// there is nothing to look <c>Zera#4821</c> up in -- the friend code is still what carries the
/// key and the address. And four digits are ten thousand possibilities: anyone can generate a
/// key landing on a chosen tag in seconds. It tells friends apart; the code itself is what
/// cannot be forged.
/// </summary>
public static class PeerName
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 32;

    private static readonly char[] Reserved = { '<', '>' };

    /// <summary>
    /// Checks a pseudo the user is choosing for themselves. Whitespace runs collapse to one
    /// space. The hash sign is refused because it separates the pseudo from its tag, and
    /// <c>A#1#2345</c> would read two ways.
    /// </summary>
    public static bool TryNormalize(string? input, out string name, out string error)
    {
        name = "";
        error = "";

        var collapsed = string.Join(' ',
            (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Any(char.IsControl))
        {
            error = "Le pseudo contient un caractère invisible.";
            return false;
        }
        if (collapsed.Contains('#'))
        {
            error = "Le pseudo ne peut pas contenir #, qui sépare le pseudo de son numéro.";
            return false;
        }
        // The game's menu is RmlUi, and some of its surfaces -- the toasts, the modals -- read a
        // string that starts with '<' as markup. A pseudo is shown there too.
        if (collapsed.IndexOfAny(Reserved) >= 0)
        {
            error = "Le pseudo ne peut pas contenir < ni >.";
            return false;
        }
        if (collapsed.Length < MinimumLength)
        {
            error = $"Le pseudo doit faire au moins {MinimumLength} caractères.";
            return false;
        }
        if (collapsed.Length > MaximumLength)
        {
            error = $"Le pseudo ne peut pas dépasser {MaximumLength} caractères.";
            return false;
        }

        name = collapsed;
        return true;
    }

    /// <summary>
    /// Cleans a pseudo that someone else chose, to show it rather than to judge it: invisible
    /// characters and hash signs go, the rest is capped. May come back empty.
    /// </summary>
    public static string Sanitize(string? untrusted)
    {
        var kept = new string((untrusted ?? "")
            .Where(character => !char.IsControl(character) && character != '#' && Array.IndexOf(Reserved, character) < 0)
            .ToArray());
        var collapsed = string.Join(' ', kept.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= MaximumLength) return collapsed;

        // Never cut between the two halves of an emoji: a lone surrogate turns into a
        // replacement character everywhere it is shown.
        var cut = MaximumLength;
        if (char.IsHighSurrogate(collapsed[cut - 1])) cut--;
        return collapsed[..cut].TrimEnd();
    }

    /// <summary>The four digits read off a public key.</summary>
    public static string Tag(ReadOnlySpan<byte> publicKey)
    {
        var digest = Digest(publicKey);
        var value = ((uint)digest[0] << 24) | ((uint)digest[1] << 16) | ((uint)digest[2] << 8) | digest[3];
        return (value % 10000).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Six digits, the first four being <see cref="Tag"/>. Shown only when two people in the same
    /// list would otherwise read identically -- one chance in ten thousand for two players who
    /// share a pseudo -- so the number everyone knows you by stays the one in front.
    /// </summary>
    public static string LongTag(ReadOnlySpan<byte> publicKey)
    {
        var digest = Digest(publicKey);
        var extra = (((uint)digest[4] << 8) | digest[5]) % 100;
        return Tag(publicKey) + extra.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static byte[] Digest(ReadOnlySpan<byte> publicKey)
    {
        PeerIdentity.ValidatePublicKey(publicKey);

        // Domain-separated, so these digits can never coincide by construction with any other
        // hash CubeShelf takes of the same key.
        var label = "CubeShelf/tag/v1\0"u8;
        var input = new byte[label.Length + publicKey.Length];
        label.CopyTo(input);
        publicKey.CopyTo(input.AsSpan(label.Length));
        return SHA256.HashData(input);
    }

    /// <summary><c>Zera#4821</c>, or just <c>#4821</c> when there is no pseudo to put in front.</summary>
    public static string Handle(string? name, ReadOnlySpan<byte> publicKey, bool longTag = false)
    {
        var shown = Sanitize(name);
        return (shown.Length == 0 ? "" : shown) + "#" + (longTag ? LongTag(publicKey) : Tag(publicKey));
    }

    /// <summary><see cref="Handle"/> from the base64 key friends.json stores, or empty if unreadable.</summary>
    public static string Handle(string? name, string publicKeyBase64, bool longTag = false)
    {
        try
        {
            return Handle(name, Convert.FromBase64String(publicKeyBase64), longTag);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return Sanitize(name);
        }
    }

    /// <summary>
    /// The pseudo as it travels inside a friend code. Thirty-two UTF-16 units never exceed
    /// ninety-six UTF-8 bytes, so it always fits the single length byte in front of it.
    /// </summary>
    internal static byte[] Encode(string? name) => Encoding.UTF8.GetBytes(Sanitize(name));
}
