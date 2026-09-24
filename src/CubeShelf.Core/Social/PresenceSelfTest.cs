namespace CubeShelf.Core.Social;

/// <param name="PresenceUrl">Proven to serve a document this identity can open.</param>
/// <param name="FriendCode">Empty unless the test succeeded: a code is a promise, not a guess.</param>
public sealed record PresenceSelfTestResult(
    bool Succeeded,
    string PresenceUrl,
    string FriendCode,
    string? Error = null);

/// <summary>
/// Publishes a document and then reads it back from the address that is about to go into a
/// friend code, opening it with our own key.
///
/// Publishing is not the same as being readable. The folder may not be the one the sync client
/// watches; the client may not have uploaded yet; the share link may serve an HTML preview page
/// rather than the file -- a Nextcloud share needs /download appended, and Dropbox has its own
/// rule. Every one of those looks like success from the writing side and fails silently on the
/// reading side, where nobody can diagnose it: the friend simply never sees you.
///
/// So the friend code is only offered once a document has genuinely made the round trip. That
/// is why presence documents are addressed to their author as well as to friends.
/// </summary>
public sealed class PresenceSelfTest : IDisposable
{
    private readonly PeerIdentity _identity;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _retryDelay;
    private bool _disposed;

    public PresenceSelfTest(
        PeerIdentity identity,
        HttpClient? httpClient = null,
        TimeSpan? retryDelay = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-avalonia");
    }

    /// <summary>
    /// Publishes <paramref name="snapshot"/> and waits up to <paramref name="patience"/> for it
    /// to become readable. <paramref name="recipients"/> must include this identity, which
    /// <see cref="PresenceRecipients.ForPublication"/> guarantees.
    /// </summary>
    public async Task<PresenceSelfTestResult> RunAsync(
        IPresencePublisher publisher,
        PresenceSnapshot snapshot,
        IReadOnlyList<byte[]> recipients,
        TimeSpan patience,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(recipients);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!publisher.IsConfigured)
            return Failed("La publication de présence n’est pas encore configurée.");

        var published = await publisher
            .PublishAsync(SealedPresence.ToJson(SealedPresence.Seal(_identity, snapshot, recipients)), cancellationToken)
            .ConfigureAwait(false);
        if (!published.Succeeded)
            return Failed(published.Error ?? "La publication a échoué.");

        if (!Uri.TryCreate(published.PresenceUrl, UriKind.Absolute, out var address))
            return Failed("L’adresse de lecture n’est pas une adresse valide.");

        var deadline = DateTimeOffset.UtcNow + patience;
        string? lastError = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // No conditional request: we want whatever is there now, not confirmation that
            // something we already hold is current.
            var read = await PresenceDocumentReader
                .ReadAsync(_http, address, null, cancellationToken)
                .ConfigureAwait(false);

            if (read.Ok &&
                SealedPresence.TryOpen(_identity, _identity.PublicKey, SealedPresence.FromJson(read.Json!), out var opened) &&
                opened is not null)
            {
                // A document we can open but that is not the one just published means the address
                // is serving something stale -- a cache, or a different file altogether.
                if (opened.Sequence == snapshot.Sequence)
                    return new(true, published.PresenceUrl,
                        FriendCode.Encode(_identity.PublicKey, published.PresenceUrl, snapshot.DisplayName));

                lastError = "L’adresse sert encore un document plus ancien.";
            }
            else if (read.Ok)
            {
                // Readable but not ours: almost always an HTML preview page instead of the file.
                lastError =
                    "L’adresse répond, mais pas avec ton document de présence. " +
                    "Vérifie que le lien pointe sur le fichier lui-même : un lien de partage " +
                    "Nextcloud demande /download à la fin.";
            }
            else
            {
                lastError = read.Error ?? "Lecture impossible.";
            }

            if (DateTimeOffset.UtcNow + _retryDelay >= deadline)
                return Failed(lastError);

            await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static PresenceSelfTestResult Failed(string? error) => new(false, "", "", error);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _http.Dispose();
    }
}
