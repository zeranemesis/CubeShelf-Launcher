using System.Net;
using System.Net.Http.Headers;

namespace CubeShelf.Core.Social;

public enum PresenceFetchStatus
{
    /// <summary>A newer document was read and opened.</summary>
    Updated = 0,

    /// <summary>The address answered 304: what we already hold is current.</summary>
    NotModified = 1,

    /// <summary>Read, but not usable: not addressed to us any more, or a replay.</summary>
    Rejected = 2,

    /// <summary>The address could not be reached or answered an error.</summary>
    Unreachable = 3,

    /// <summary>Nothing published there yet, or no longer.</summary>
    NotPublished = 4,

    /// <summary>Still backing off from earlier failures.</summary>
    Skipped = 5
}

public sealed record PresenceFetchOutcome(
    string FriendPublicKey,
    PresenceFetchStatus Status,
    PresenceSnapshot? Snapshot = null,
    string? Error = null,
    bool IsConnectivityFailure = false);

/// <summary>
/// Reads friends' published documents.
///
/// Every address here came from a code the user pasted, so it is attacker-chosen text: the
/// response is capped, timed out, and never trusted to be the size it claims. One friend's
/// broken address must never cost the others their poll.
/// </summary>
public sealed class PresenceFetcher : IDisposable
{
    private readonly PeerIdentity _identity;
    private readonly FriendStore _friends;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Func<DateTimeOffset> _clock;
    private bool _disposed;

    public PresenceFetcher(
        PeerIdentity identity,
        FriendStore friends,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? clock = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _friends = friends ?? throw new ArgumentNullException(nameof(friends));
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-avalonia");
    }

    /// <summary>
    /// Polls every friend that is neither paused nor backing off. Each is isolated: an address
    /// that hangs, 404s or throws costs only its own outcome.
    /// </summary>
    public async Task<IReadOnlyList<PresenceFetchOutcome>> PollAsync(
        CancellationToken cancellationToken = default)
    {
        var due = _friends.Load().Where(friend => !friend.Paused).ToArray();
        if (due.Length == 0) return Array.Empty<PresenceFetchOutcome>();

        using var gate = new SemaphoreSlim(PresencePolicy.PollConcurrency);
        var tasks = due.Select(async friend =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await FetchAsync(friend, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<PresenceFetchOutcome> FetchAsync(Friend friend, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(friend);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var now = _clock();
        if (friend.NextAttemptAt is { } next && next > now)
            return new(friend.PublicKey, PresenceFetchStatus.Skipped);

        // friends.json is a plain file a user can edit, so the scheme is checked again here and
        // not only when the friend code was decoded.
        if (!Uri.TryCreate(friend.PresenceUrl, UriKind.Absolute, out var address) ||
            !address.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return Fail(friend, PresenceFetchStatus.Unreachable,
                "L’adresse de présence de cet ami n’est pas une adresse https.", connectivity: false);

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(friend.PublicKey);
            PeerIdentity.ValidatePublicKey(publicKey);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return Fail(friend, PresenceFetchStatus.Unreachable, "Clé publique illisible.", connectivity: false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PresencePolicy.FetchTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);

            // A hand-edited ETag would make ParseAdd throw. Omitting the header costs one full
            // download; failing the fetch would cost the friend entirely.
            if (!string.IsNullOrWhiteSpace(friend.LastETag) &&
                EntityTagHeaderValue.TryParse(friend.LastETag, out var tag))
                request.Headers.IfNoneMatch.Add(tag);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            // Before any success check: 304 is not 2xx, so EnsureSuccessStatusCode would treat
            // the cheapest possible answer as a failure.
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                Succeed(friend, friend.LastETag, now, advanceSequence: null);
                return new(friend.PublicKey, PresenceFetchStatus.NotModified);
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return Fail(friend, PresenceFetchStatus.NotPublished,
                    "Cet ami n’a pas encore publié sa présence.", connectivity: false);

            if (!response.IsSuccessStatusCode)
                return Fail(friend, PresenceFetchStatus.Unreachable,
                    $"L’adresse a répondu {(int)response.StatusCode}.",
                    connectivity: (int)response.StatusCode >= 500);

            if (response.Content.Headers.ContentLength > PresencePolicy.MaximumDocumentBytes)
                return Fail(friend, PresenceFetchStatus.Unreachable,
                    "Le document de présence est trop volumineux.", connectivity: false);

            var json = await ReadCappedAsync(response, timeout.Token).ConfigureAwait(false);
            if (json is null)
                return Fail(friend, PresenceFetchStatus.Unreachable,
                    "Le document de présence est trop volumineux.", connectivity: false);

            var etag = response.Headers.ETag?.ToString();

            if (!SealedPresence.TryOpen(_identity, publicKey, SealedPresence.FromJson(json), out var snapshot) ||
                snapshot is null)
            {
                // Indistinguishable from tampering by design, but by far the likeliest cause is
                // benign and worth saying: they stopped addressing their document to us.
                Succeed(friend, etag, now, advanceSequence: null);
                return new(friend.PublicKey, PresenceFetchStatus.Rejected,
                    Error: "Cet ami ne partage plus sa présence avec toi.");
            }

            // The ETag is stored either way. Without that, a replayed document would be
            // downloaded in full on every single poll, forever.
            if (!_friends.TryAcceptSequence(friend.PublicKey, snapshot.Sequence, now))
            {
                Succeed(friend, etag, now, advanceSequence: null);
                return new(friend.PublicKey, PresenceFetchStatus.Rejected,
                    Error: "Document de présence déjà vu.");
            }

            Succeed(friend, etag, now, advanceSequence: snapshot.Sequence);
            return new(friend.PublicKey, PresenceFetchStatus.Updated, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fail(friend, PresenceFetchStatus.Unreachable, "Délai dépassé.", connectivity: true);
        }
        catch (HttpRequestException exception)
        {
            return Fail(friend, PresenceFetchStatus.Unreachable, exception.Message, connectivity: true);
        }
    }

    /// <summary>
    /// Reads the body while counting, because Content-Length may be absent or simply untrue.
    /// Returns null once the cap is passed.
    /// </summary>
    private static async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > PresencePolicy.MaximumDocumentBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void Succeed(Friend friend, string? etag, DateTimeOffset now, long? advanceSequence)
    {
        _friends.Update(friend.PublicKey, entry =>
        {
            entry.LastETag = etag;
            entry.ConsecutiveFailures = 0;
            entry.NextAttemptAt = null;
            if (advanceSequence is not null) entry.LastSeenAt = now;
        });
    }

    private PresenceFetchOutcome Fail(
        Friend friend,
        PresenceFetchStatus status,
        string error,
        bool connectivity)
    {
        var now = _clock();
        _friends.Update(friend.PublicKey, entry =>
        {
            entry.ConsecutiveFailures = Math.Min(entry.ConsecutiveFailures + 1, 16);
            entry.NextAttemptAt = now + BackoffFor(entry.ConsecutiveFailures);
        });

        return new(friend.PublicKey, status, Error: error, IsConnectivityFailure: connectivity);
    }

    private static TimeSpan BackoffFor(int consecutiveFailures)
    {
        var doubled = PresencePolicy.PollInterval * Math.Pow(2, Math.Min(consecutiveFailures, 10));
        var capped = doubled > PresencePolicy.MaximumBackoff ? PresencePolicy.MaximumBackoff : doubled;

        // Jitter keeps a shared outage from turning every client into a synchronised retry.
        var jitter = 1 + (Random.Shared.NextDouble() - .5) * .4;
        return capped * jitter;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _http.Dispose();
    }
}
