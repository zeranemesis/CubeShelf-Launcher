namespace CubeShelf.Core.Social;

/// <param name="Succeeded">Whether the document is now where friends will look for it.</param>
/// <param name="PresenceUrl">The https address friends poll, empty while still unknown.</param>
/// <param name="Error">User-facing, in French like the rest of Core.</param>
/// <param name="IsConnectivityFailure">
/// Lets the desktop tell a network problem, which deserves the global banner, from a
/// configuration problem, which belongs to the friends page alone.
/// </param>
public sealed record PresencePublishResult(
    bool Succeeded,
    string PresenceUrl,
    string? Error = null,
    bool IsConnectivityFailure = false);

/// <summary>
/// Puts the sealed document where friends can read it.
///
/// The interface exists so the transport is a choice rather than an assumption: publishing to a
/// folder something else already syncs needs no account and keeps no secret, while a Gist or a
/// WebDAV target would need both. Only the folder is implemented today; the seam is here so
/// adding another does not mean rewriting the layers above.
/// </summary>
public interface IPresencePublisher : IDisposable
{
    /// <summary>False while the user has not finished setting the transport up.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Where the document can be read from. This is what goes into the friend code, so it is
    /// only trustworthy once a self-test has actually read a document back from it.
    /// </summary>
    string PresenceUrl { get; }

    Task<PresencePublishResult> PublishAsync(string envelopeJson, CancellationToken cancellationToken = default);
}
