namespace CubeShelf.Core.Social;

public enum PresenceStatus
{
    Offline = 0,
    Online = 1,
    InGame = 2
}

/// <summary>One entry of a shared library. Deliberately small: this is published repeatedly.</summary>
public sealed record SharedGame(
    string Id,
    string Title,
    int PlayCount,
    long TotalPlaySeconds,
    bool IsFavorite,
    DateTimeOffset? LastPlayedAt);

/// <summary>A mod the peer has installed, so two friends can line up before playing together.</summary>
public sealed record SharedMod(
    string GameId,
    string ModId,
    string Name,
    bool Enabled);

/// <summary>
/// The part of a peer that barely ever changes: who they are rather than what they are doing.
///
/// It travels inside the presence document rather than in one of its own. A separate file would
/// save rewriting the avatar on every heartbeat, but a share link points at a single file, so it
/// would also mean a second address in every friend code and a second round trip to verify --
/// more cost than the bytes it saves. The avatar is capped small instead.
/// </summary>
public sealed record PeerProfile(
    string StatusLine = "",
    string? AvatarPng = null,
    string? PinnedGameId = null,
    string? PinnedGameTitle = null,
    int TotalGames = 0,
    long TotalPlaySeconds = 0,
    DateTimeOffset? FirstSeenAt = null)
{
    /// <summary>
    /// 8 KiB is generous for the 96x96 the launcher stores, and it is what keeps a heartbeat
    /// every five minutes from turning into megabytes of sync traffic a day.
    /// </summary>
    public const int MaximumAvatarBytes = 8 * 1024;

    public const int MaximumStatusLength = 140;
}

/// <summary>
/// What a peer publishes for its friends to read.
///
/// <paramref name="Sequence"/> only ever increases. A reader that has seen a higher sequence
/// rejects the document, which is what stops someone who kept an old copy of the published file
/// from serving it back to make a friend look like they are still in a game.
/// </summary>
public sealed record PresenceSnapshot(
    int Version,
    string DisplayName,
    DateTimeOffset PublishedAt,
    long Sequence,
    PresenceStatus Status,
    string? CurrentGameId,
    string? CurrentGameTitle,
    IReadOnlyList<SharedGame> Library,
    IReadOnlyList<SharedMod> Mods,
    PeerProfile? Profile = null,
    PresenceInvite? Invite = null)
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Publishing is periodic, so "online" is really "published recently". Past the window a peer
    /// is shown offline rather than frozen on whatever it was last doing.
    /// </summary>
    public bool IsFresh(TimeSpan window, DateTimeOffset now) =>
        PublishedAt <= now + TimeSpan.FromMinutes(5) && now - PublishedAt <= window;

    public PresenceStatus EffectiveStatus(TimeSpan window, DateTimeOffset now) =>
        IsFresh(window, now) ? Status : PresenceStatus.Offline;
}
