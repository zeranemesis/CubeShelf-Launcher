namespace CubeShelf.Core.Social.Lan;

/// <summary>
/// Publishing with no folder and no address: the document only exists to be served on the local
/// network. Two friends on the same network see each other with nothing set up at all, and the
/// day one of them adds a folder, the same service carries on with a real publisher instead.
/// </summary>
public sealed class LanOnlyPublisher : IPresencePublisher
{
    public bool IsConfigured => true;

    public string PresenceUrl => "";

    public Task<PresencePublishResult> PublishAsync(string envelopeJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeJson);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PresencePublishResult(true, ""));
    }

    public void Dispose()
    {
    }
}
