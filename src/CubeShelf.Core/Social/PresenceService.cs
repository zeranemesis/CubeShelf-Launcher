namespace CubeShelf.Core.Social;

/// <summary>Why a publish was asked for. Only used for reporting; every reason publishes alike.</summary>
public enum PresencePublishReason
{
    Startup = 0,
    Heartbeat = 1,
    GameChanged = 2,
    FriendsChanged = 3,
    Manual = 4,
    Shutdown = 5,

    /// <summary>The profile or an invitation changed: same document, different contents.</summary>
    ProfileChanged = 6
}

/// <summary>
/// The state of the shelf at one instant, captured by the caller.
///
/// The catalog is mutable and lives on the UI thread, so the service never reads it directly:
/// it asks for this, and the desktop layer marshals.
/// </summary>
public sealed record PresenceInputs(
    string DisplayName,
    IReadOnlyList<PresenceGame> Games,
    IReadOnlyCollection<string> RunningGameIds,
    PresenceSharingOptions Sharing,
    ProfileInputs? Profile = null,
    PresenceInvite? Invite = null);

/// <summary>Timings, injectable so the loop can be tested in milliseconds rather than minutes.</summary>
public sealed record PresenceServiceOptions(
    TimeSpan Debounce,
    TimeSpan MinimumGap,
    TimeSpan Heartbeat,
    TimeSpan PollInterval)
{
    public static PresenceServiceOptions Default => new(
        PresencePolicy.PublishDebounce,
        PresencePolicy.MinimumPublishGap,
        PresencePolicy.Heartbeat,
        PresencePolicy.PollInterval);
}

/// <summary>
/// Keeps our document published and friends' documents read.
///
/// Two loops. The publish loop waits for a reason or for the heartbeat, whichever comes first,
/// and coalesces a burst -- starting a game changes several things at once and should still
/// produce one document. The poll loop reads friends on its own cadence.
/// </summary>
public sealed class PresenceService : IAsyncDisposable
{
    private readonly PeerIdentity _identity;
    private readonly FriendStore _friends;
    private readonly PresenceComposer _composer;
    private readonly PresenceSequence _sequence;
    private readonly IPresencePublisher _publisher;
    private readonly PresenceFetcher _fetcher;
    private readonly Func<CancellationToken, Task<PresenceInputs>> _capture;
    private readonly PresenceServiceOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    private CancellationTokenSource? _lifetime;
    private Task? _publishLoop;
    private Task? _pollLoop;

    private string _lastFingerprint = "";
    private DateTimeOffset? _lastPublishedAt;
    private int _shutdownPublished;
    private bool _disposed;

    public PresenceService(
        PeerIdentity identity,
        FriendStore friends,
        PresenceComposer composer,
        PresenceSequence sequence,
        IPresencePublisher publisher,
        PresenceFetcher fetcher,
        Func<CancellationToken, Task<PresenceInputs>> captureInputs,
        PresenceServiceOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _friends = friends ?? throw new ArgumentNullException(nameof(friends));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _capture = captureInputs ?? throw new ArgumentNullException(nameof(captureInputs));
        _options = options ?? PresenceServiceOptions.Default;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action<PresencePublishReason, PresencePublishResult>? Published;
    public event Action<IReadOnlyList<PresenceFetchOutcome>>? FriendsRefreshed;

    /// <summary>How many publishes actually reached the transport. For tests and diagnostics.</summary>
    public int PublishCount { get; private set; }

    public void Start(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RequestPublish(PresencePublishReason.Startup);
        _publishLoop = PublishLoopAsync(_lifetime.Token);
        _pollLoop = PollLoopAsync(_lifetime.Token);
    }

    /// <summary>
    /// Asks for a publish. Several requests before the loop wakes collapse into one, which is
    /// the point: starting a game raises more than one of them.
    /// </summary>
    public void RequestPublish(PresencePublishReason reason)
    {
        _pendingReason = reason;
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One is already pending, and one publish covers both.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private volatile PresencePublishReason _pendingReason = PresencePublishReason.Startup;

    public async Task<IReadOnlyList<PresenceFetchOutcome>> RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        var outcomes = await _fetcher.PollAsync(cancellationToken).ConfigureAwait(false);
        if (outcomes.Count > 0) FriendsRefreshed?.Invoke(outcomes);
        return outcomes;
    }

    /// <summary>
    /// Publishes one last document saying we are gone.
    ///
    /// Best-effort by nature: a crash, a power cut or a closed lid all skip it, so staleness
    /// remains the mechanism that is always right. This only spares friends the wait -- without
    /// it, someone who quits mid-game reads as still playing for the whole freshness window,
    /// which is the most visible thing a presence system can get wrong.
    /// </summary>
    /// <param name="displayName">
    /// The name to say goodbye with. A caller closing a window should pass it: without it the
    /// service asks the capture, the capture runs on the UI thread, and the UI thread is exactly
    /// what a closing window is blocking. That wait is what kept CubeShelf 0.9.0 alive after its
    /// window closed whenever presence was on.
    /// </param>
    public async Task ShutdownAsync(TimeSpan budget, string? displayName = null)
    {
        if (Interlocked.Exchange(ref _shutdownPublished, 1) == 1) return;
        _lifetime?.Cancel();

        using var deadline = new CancellationTokenSource(budget);
        var gated = false;
        try
        {
            var name = displayName ??
                (await CaptureAsync(deadline.Token).ConfigureAwait(false)).DisplayName;

            // Behind any publish still in flight, so a document written late cannot land on top
            // of the farewell and leave friends reading "online" for someone who has left. The
            // lifetime was cancelled above, so whatever holds the gate is already on its way out.
            await _publishGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            gated = true;

            var snapshot = PresenceComposer.Offline(name, _sequence.Next(_clock()), _clock());
            await PublishSnapshotAsync(PresencePublishReason.Shutdown, snapshot, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Closing the window must stay instant; a farewell that does not fit the budget is
            // simply not sent.
        }
        finally
        {
            if (gated) _publishGate.Release();
        }
    }

    /// <summary>
    /// The capture belongs to the caller and may never answer -- a UI thread that is itself
    /// blocked waiting on this service is the obvious case. So cancellation is enforced here
    /// rather than trusted to it: a stuck capture costs one publish, never the service's
    /// ability to stop.
    /// </summary>
    private Task<PresenceInputs> CaptureAsync(CancellationToken cancellationToken) =>
        _capture(cancellationToken).WaitAsync(cancellationToken);

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var heartbeat = _options.Heartbeat + RandomJitter(PresencePolicy.HeartbeatJitter);
                var signalled = await _signal.WaitAsync(heartbeat, cancellationToken).ConfigureAwait(false);
                var reason = signalled ? _pendingReason : PresencePublishReason.Heartbeat;

                // Let the rest of a burst arrive before composing anything.
                if (signalled && _options.Debounce > TimeSpan.Zero)
                    await Task.Delay(_options.Debounce, cancellationToken).ConfigureAwait(false);

                await PublishOnceAsync(reason, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishOnceAsync(PresencePublishReason reason, CancellationToken cancellationToken)
    {
        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The gap is waited out rather than skipped, so the last state always reaches the
            // wire. Dropping the publish instead would leave friends looking at the state
            // before a rapid start-stop.
            if (_lastPublishedAt is { } last)
            {
                var since = _clock() - last;
                if (since < _options.MinimumGap)
                    await Task.Delay(_options.MinimumGap - since, cancellationToken).ConfigureAwait(false);
            }

            var inputs = await CaptureAsync(cancellationToken).ConfigureAwait(false);
            var candidate = _composer.Compose(
                inputs.DisplayName, inputs.Games, inputs.RunningGameIds, inputs.Sharing,
                sequence: 0, _clock(), inputs.Profile, inputs.Invite);

            var fingerprint = PresenceComposer.ContentFingerprint(candidate);
            var heartbeatDue = _lastPublishedAt is not { } previous ||
                _clock() - previous >= _options.Heartbeat;

            // Nothing a friend would see has changed and the document is still fresh: writing it
            // again would only churn the sync client.
            if (fingerprint == _lastFingerprint && !heartbeatDue) return;

            var snapshot = candidate with { Sequence = _sequence.Next(_clock()) };
            var result = await PublishSnapshotAsync(reason, snapshot, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) _lastFingerprint = fingerprint;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private async Task<PresencePublishResult> PublishSnapshotAsync(
        PresencePublishReason reason,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var envelope = SealedPresence.Seal(
            _identity, snapshot, PresenceRecipients.ForPublication(_identity, _friends));
        var result = await _publisher
            .PublishAsync(SealedPresence.ToJson(envelope), cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            _lastPublishedAt = _clock();
            PublishCount++;
        }

        Published?.Invoke(reason, result);
        return result;
    }

    private static TimeSpan RandomJitter(TimeSpan span) =>
        span <= TimeSpan.Zero ? TimeSpan.Zero : span * Random.Shared.NextDouble();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _lifetime?.Cancel();
        foreach (var loop in new[] { _publishLoop, _pollLoop })
        {
            if (loop is null) continue;
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime?.Dispose();
        _signal.Dispose();
        _publishGate.Dispose();
        _fetcher.Dispose();
        _publisher.Dispose();
    }
}
