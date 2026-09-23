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
    IReadOnlyList<SharedMod> Mods)
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
