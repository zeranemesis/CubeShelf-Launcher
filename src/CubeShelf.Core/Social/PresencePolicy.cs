namespace CubeShelf.Core.Social;

/// <summary>
/// Every timing decision the friends feature makes, in one place so they can be reasoned about
/// together rather than discovered one constant at a time.
/// </summary>
public static class PresencePolicy
{
    /// <summary>
    /// How long a published document still counts as "now". It has to cover the whole worst-case
    /// age of a healthy peer's newest visible document:
    ///
    ///   heartbeat + heartbeat jitter + propagation lag + poll interval
    ///
    /// Sized to survive one missed heartbeat, otherwise a friend on flaky Wi-Fi flickers offline.
    /// With a synced folder the propagation term is the sync client's, which is unknown until
    /// measured -- measure it and revisit this number rather than trusting the arithmetic.
    /// </summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Spreads heartbeats out so several CubeShelf instances that started together do not keep
    /// publishing in lockstep forever.
    /// </summary>
    public static readonly TimeSpan HeartbeatJitter = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Starting a game fires several state changes at once. Waiting a moment turns a burst into
    /// one document.
    /// </summary>
    public static readonly TimeSpan PublishDebounce = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Floor between two publishes, so a rapid start-stop-start cannot hammer the sync client.
    /// A trailing publish is always allowed through afterwards, or the last state would be lost.
    /// </summary>
    public static readonly TimeSpan MinimumPublishGap = TimeSpan.FromSeconds(30);

    /// <summary>A friend whose address keeps failing is retried ever more slowly, up to this.</summary>
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How long shutdown may spend publishing a final "offline" document. Closing the window has
    /// to stay instant, so this is small and the publish is best-effort.
    /// </summary>
    public static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);

    /// <summary>Per-request ceiling when reading a friend's document.</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The largest document we will read from a friend. Their address comes from a pasted code,
    /// so it is attacker-chosen text and the response needs a hard bound.
    /// </summary>
    public const long MaximumDocumentBytes = 256 * 1024;

    /// <summary>A library longer than this is truncated before publishing, most-played first.</summary>
    public const int MaximumSharedGames = 250;

    public const int MaximumSharedMods = 300;

    /// <summary>How many friends are polled at once.</summary>
    public const int PollConcurrency = 4;
}
