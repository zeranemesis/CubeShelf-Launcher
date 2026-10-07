namespace CubeShelf.Core.Social;

public enum PresenceAddressHealth
{
    /// <summary>Not checked yet, or nothing to check: no public address configured.</summary>
    Unknown = 0,

    /// <summary>The address serves a recent document of ours.</summary>
    Healthy = 1,

    /// <summary>
    /// The address serves one of our documents, but an old one: the sync client is not getting
    /// what we write out to the service quickly enough, and friends see us late or offline.
    /// </summary>
    Lagging = 2,

    /// <summary>The address answers, with something that is not our document: a page, a login wall.</summary>
    NotOurs = 3,

    /// <summary>Nothing is served there any more: the file or its share link is gone.</summary>
    NotFound = 4,

    /// <summary>The address could not be reached at all. Usually the network, rarely the link.</summary>
    Unreachable = 5,

    /// <summary>
    /// Our address serves a document numbered past anything this installation issued: a copy of
    /// this profile on another machine is publishing as us.
    /// </summary>
    SomeoneElsePublishes = 6
}

/// <param name="Lag">For <see cref="PresenceAddressHealth.Lagging"/>: how far behind the served document is.</param>
/// <param name="Detail">The underlying error, in French like the rest of Core, when there is one.</param>
public sealed record PresenceAddressReport(
    PresenceAddressHealth Health,
    DateTimeOffset CheckedAt,
    TimeSpan? Lag = null,
    string? Detail = null)
{
    /// <summary>Whether friends are, right now, unlikely to see us as we are.</summary>
    public bool NeedsAttention => Health is PresenceAddressHealth.Lagging or PresenceAddressHealth.NotOurs
        or PresenceAddressHealth.NotFound or PresenceAddressHealth.SomeoneElsePublishes;
}
