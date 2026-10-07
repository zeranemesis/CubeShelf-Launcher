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
    public Task<PresenceSelfTestResult> RunAsync(
        IPresencePublisher publisher,
        PresenceSnapshot snapshot,
        IReadOnlyList<byte[]> recipients,
        TimeSpan patience,
        CancellationToken cancellationToken = default) =>
        RunAsync(publisher, snapshot, recipients, patience, candidates: null, cancellationToken);

    /// <summary>
    /// Same round trip, reading back from each of <paramref name="candidates"/> in turn -- the
    /// guesses <see cref="ShareLink"/> made from a pasted share link -- and keeping the first that
    /// serves the document just published. Without candidates, the publisher's own address is read.
    /// </summary>
    public async Task<PresenceSelfTestResult> RunAsync(
        IPresencePublisher publisher,
        PresenceSnapshot snapshot,
        IReadOnlyList<byte[]> recipients,
        TimeSpan patience,
        IReadOnlyList<string>? candidates,
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

        var addresses = (candidates is { Count: > 0 } ? candidates : new[] { published.PresenceUrl })
            .Select(candidate => Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? uri
                : null)
            .OfType<Uri>()
            .ToArray();
        if (addresses.Length == 0)
            return Failed("L’adresse de lecture n’est pas une adresse https valide.");

        var deadline = DateTimeOffset.UtcNow + patience;
        string? lastError = null;

        while (true)
        {
            var sawStale = false;
            foreach (var address in addresses)
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
                    // A document we can open but that is not the one just published means the
                    // address serves something stale -- a cache, or the sync client still uploading.
                    if (opened.Sequence == snapshot.Sequence)
                    {
                        var url = address.AbsoluteUri;
                        return new(true, url, FriendCode.Encode(_identity.PublicKey, url, snapshot.DisplayName));
                    }

                    sawStale = true;
                }
                else if (read.Ok)
                {
                    // Readable but not ours: almost always a preview page instead of the file.
                    lastError ??=
                        "L’adresse répond, mais pas avec ton document de présence : c’est sans doute la page " +
                        "d’aperçu du service. Vérifie que le lien désigne le fichier cubeshelf-presence.json " +
                        "et qu’il est ouvert à « toute personne disposant du lien ».";
                }
                else
                {
                    lastError ??= read.Error ?? "Lecture impossible.";
                }
            }

            // Of everything seen this round, the one that says the most wins: "it is there but
            // old" beats "this guess served a preview page", which beats a plain failure.
            if (sawStale)
                lastError = "L’adresse sert encore un document plus ancien : le service n’a pas fini de synchroniser.";

            if (DateTimeOffset.UtcNow + _retryDelay >= deadline)
                return Failed(lastError);

            lastError = null;
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
