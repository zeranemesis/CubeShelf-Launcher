namespace CubeShelf.Core.Social;

public enum PresenceStatus
{
    Offline = 0,
    Online = 1,
    InGame = 2
}

/// <summary>
/// What the user says about being available, on top of what CubeShelf observes. Published beside
/// the status rather than as new status values, so an older CubeShelf still reads "online".
/// </summary>
public enum PresenceAvailability
{
    Available = 0,
    Away = 1,
    Busy = 2,

    /// <summary>Published as offline, while still reading friends and receiving their messages.</summary>
    Invisible = 3
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
/// <summary>
/// One game's RetroAchievements progress, as a friend publishes it. What their game reported --
/// not something the server vouched for -- so it is shown with the account name, which anyone can
/// look up on retroachievements.org.
/// </summary>
public sealed record SharedAchievements(
    string GameId,
    long RaGameId,
    string Title,
    string User,
    int Total,
    int Points,
    int TotalPoints,
    IReadOnlyList<int> UnlockedIds,
    DateTimeOffset UpdatedAt)
{
    public const int MaximumGames = 32;
    public const int MaximumIds = 2000;

    public int Unlocked => UnlockedIds.Count;

    /// <summary>
    /// A friend's entry made safe to show: counts in range, names cleaned, ids positive and
    /// distinct. Null for anything that does not hold together.
    /// </summary>
    public static SharedAchievements? Sanitized(SharedAchievements? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.GameId) || entry.GameId.Length > 128 || entry.RaGameId <= 0) return null;
        if (!CubeShelf.Core.Achievements.RetroAchievementsSession.IsUser(entry.User)) return null;
        var total = Math.Clamp(entry.Total, 0, MaximumIds);
        var ids = (entry.UnlockedIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().Take(MaximumIds).OrderBy(id => id).ToArray();
        if (total == 0 || ids.Length > total) return null;
        var title = ChatText.Clean(entry.Title);
        return entry with
        {
            Title = title.Length > 100 ? title[..100] : title,
            Total = total,
            Points = Math.Clamp(entry.Points, 0, 1_000_000),
            TotalPoints = Math.Clamp(entry.TotalPoints, 0, 1_000_000),
            UnlockedIds = ids
        };
    }
}

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
    PresenceInvite? Invite = null,

    /// <summary>
    /// Where this document is published, as its author states it. A friend who met us on the
    /// local network learns our address from here; one who still reads an old address learns the
    /// new one (<see cref="FriendStore.AdoptAddress"/>). Null says nothing, and changes nothing.
    /// </summary>
    string? Address = null,

    /// <summary>"away" or "busy" when the user said so; null otherwise.</summary>
    string? Availability = null,

    /// <summary>What is happening in the game -- the board, the turn -- when the game says.</summary>
    string? Activity = null,

    /// <summary>Messages and answers for single friends, each sealed for its one reader (<see cref="PairwiseNotes"/>).</summary>
    IReadOnlyList<SealedNote>? Notes = null,

    /// <summary>
    /// How to reach the author's CubeShelf on the network directly (<see cref="Mesh.MeshPeerAddress"/>,
    /// base64): its own addresses when it can listen, its relays otherwise. Inside the sealed
    /// document, so only friends ever learn it.
    /// </summary>
    string? Mesh = null,

    /// <summary>Where the author stands in each game's RetroAchievements set, as their games reported it.</summary>
    IReadOnlyList<SharedAchievements>? Achievements = null)
{
    public const string AvailabilityAway = "away";
    public const string AvailabilityBusy = "busy";

    public const int MaximumActivityLength = 120;

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
