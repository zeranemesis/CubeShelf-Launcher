namespace CubeShelf.Core.Social;

/// <summary>
/// A standing offer to play, carried inside the presence document.
///
/// Not a notification. The transport is pull-based, so a friend sees this at their next poll --
/// up to a couple of minutes later, plus whatever the sync client adds. It is an invitation left
/// on the table for a quarter of an hour, not something that interrupts anyone.
///
/// <see cref="JoinPayload"/> is opaque to CubeShelf on purpose. PartyBoard's own invitation
/// string already carries a public address, a LAN address and a token, and its companion tries
/// the local network before the internet. Re-deriving any of that here would be reimplementing,
/// worse, something the runtime already does -- so CubeShelf carries it and does not read it.
///
/// What CubeShelf adds over pasting that string into a chat window: it is encrypted to the
/// friends it is meant for, it expires on its own, and it shows up in the launcher.
/// </summary>
public sealed record PresenceInvite(
    string GameId,
    string GameTitle,
    string JoinPayload,
    DateTimeOffset ExpiresAt,

    /// <summary>Base64 public key of the one friend it is meant for, or empty for all of them.</summary>
    string ForFriend = "")
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Generous, because the payload is a runtime's own blob and its shape is not ours to
    /// predict; small enough that a malformed document cannot bloat every publish.
    /// </summary>
    public const int MaximumPayloadLength = 2048;

    public bool IsLive(DateTimeOffset now) => now < ExpiresAt;

    public bool IsFor(string friendPublicKeyBase64) =>
        ForFriend.Length == 0 ||
        string.Equals(ForFriend, friendPublicKeyBase64, StringComparison.Ordinal);

    /// <summary>
    /// An invitation worth publishing: it names a game, carries something to join with, and has
    /// not already lapsed.
    /// </summary>
    public bool IsPublishable(DateTimeOffset now) =>
        IsLive(now) &&
        !string.IsNullOrWhiteSpace(GameId) &&
        !string.IsNullOrWhiteSpace(JoinPayload) &&
        JoinPayload.Length <= MaximumPayloadLength;
}
