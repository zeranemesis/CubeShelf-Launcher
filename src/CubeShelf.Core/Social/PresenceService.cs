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
    PresenceInvite? Invite = null,
    string? Address = null);

/// <summary>Timings, injectable so the loop can be tested in milliseconds rather than minutes.</summary>
public sealed record PresenceServiceOptions(
    TimeSpan Debounce,
    TimeSpan MinimumGap,
    TimeSpan Heartbeat,
    TimeSpan PollInterval,
    TimeSpan? AddressCheckInterval = null,
    TimeSpan? AddressFirstCheck = null,
    TimeSpan? AddressLagTolerance = null,
    TimeSpan? ActivePollInterval = null)
{
    /// <summary>Never longer than the idle interval: "active" can only mean sooner.</summary>
    public TimeSpan EffectiveActivePollInterval =>
        ActivePollInterval is { } active && active < PollInterval ? active
        : PresencePolicy.ActivePollInterval < PollInterval ? PresencePolicy.ActivePollInterval
        : PollInterval;

    public TimeSpan EffectiveAddressCheckInterval => AddressCheckInterval ?? PresencePolicy.AddressCheckInterval;
    public TimeSpan EffectiveAddressFirstCheck => AddressFirstCheck ?? PresencePolicy.AddressFirstCheck;
    public TimeSpan EffectiveAddressLagTolerance => AddressLagTolerance ?? PresencePolicy.AddressLagTolerance;

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

    /// <summary>The address the last capture stated, so the farewell can state it too without asking again.</summary>
    private string? _lastAddress;
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

    /// <summary>
    /// Every document that reached the transport, sealed, with its sequence -- the farewell
    /// included. The local network serves exactly this to friends who pull it.
    /// </summary>
    public event Action<string, long>? DocumentPublished;

    /// <summary>Raised after each read-back of our own address, on whatever thread did it.</summary>
    public event Action<PresenceAddressReport>? AddressChecked;

    /// <summary>The last read-back, or Unknown before the first.</summary>
    public PresenceAddressReport LastAddressReport { get; private set; } =
        new(PresenceAddressHealth.Unknown, DateTimeOffset.MinValue);

    /// <summary>What this session published and when, newest last, for judging how late the address is.</summary>
    private readonly List<(long Sequence, DateTimeOffset At)> _publishedHistory = new();
    private readonly object _historyGate = new();
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _lastAddressCheck;
    private int _checkingAddress;

    /// <summary>How many publishes actually reached the transport. For tests and diagnostics.</summary>
    public int PublishCount { get; private set; }

    public void Start(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startedAt = _clock();
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
        var now = _clock();
        foreach (var outcome in outcomes) _lastPolled[outcome.FriendPublicKey] = now;
        Remember(outcomes);
        if (outcomes.Count > 0) FriendsRefreshed?.Invoke(outcomes);
        return outcomes;
    }

    /// <summary>The latest document of each friend, from any source, for deciding how often to read them.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PresenceSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _lastPolled = new(StringComparer.Ordinal);
    private DateTimeOffset _eagerUntil = DateTimeOffset.MinValue;

    /// <summary>How many times each friend was read over HTTP. For tests and diagnostics.</summary>
    public int PollsOf(string friendPublicKey) => _pollCounts.TryGetValue(friendPublicKey, out var count) ? count : 0;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _pollCounts = new(StringComparer.Ordinal);

    private void Remember(IEnumerable<PresenceFetchOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _pollCounts.AddOrUpdate(outcome.FriendPublicKey, 1, (_, count) => count + 1);
            if (outcome.Snapshot is not null) _latest[outcome.FriendPublicKey] = outcome.Snapshot;
        }
    }

    /// <summary>
    /// A friend's document that arrived another way -- the local network. Counts toward deciding
    /// how often to read them, like one read here would.
    /// </summary>
    public void NoteSnapshot(string friendPublicKey, PresenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latest[friendPublicKey] = snapshot;
    }

    /// <summary>
    /// Reads everyone actively for a while: just after sending an invitation, whoever answers
    /// should be seen within seconds, not minutes; just after adding someone, so is whether they
    /// add us back.
    /// </summary>
    public void PollEagerly(TimeSpan? span = null) => _eagerUntil = _clock() + (span ?? PresencePolicy.EagerPollingSpan);

    /// <summary>
    /// Whether a friend deserves the short interval. Someone who is around -- online or playing --
    /// or who is inviting us, is about to do something worth seeing soon; someone offline for
    /// hours can wait the long one.
    /// </summary>
    private bool IsActive(string friendPublicKey, DateTimeOffset now)
    {
        if (now < _eagerUntil) return true;
        if (!_latest.TryGetValue(friendPublicKey, out var latest)) return true;   // never read: find out
        if (latest.EffectiveStatus(PresencePolicy.FreshnessWindow, now) is PresenceStatus.Online or PresenceStatus.InGame) return true;
        return latest.Invite is { } invite && invite.IsLive(now) &&
               invite.IsFor(Convert.ToBase64String(_identity.PublicKey));
    }

    private async Task PollDueAsync(CancellationToken cancellationToken)
    {
        var now = _clock();
        var active = _options.EffectiveActivePollInterval;
        var idle = _options.PollInterval;

        var outcomes = await _fetcher.PollAsync(friend =>
        {
            var interval = IsActive(friend.PublicKey, now) ? active : idle;
            // A little early rather than a whole tick late: the loop ticks at the active interval.
            return !_lastPolled.TryGetValue(friend.PublicKey, out var last) || now - last >= interval - TimeSpan.FromMilliseconds(active.TotalMilliseconds / 4);
        }, cancellationToken).ConfigureAwait(false);

        foreach (var outcome in outcomes) _lastPolled[outcome.FriendPublicKey] = now;
        Remember(outcomes);
        if (outcomes.Count > 0) FriendsRefreshed?.Invoke(outcomes);
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

            var snapshot = PresenceComposer.Offline(name, _sequence.Next(_clock()), _clock(), _lastAddress);
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
                // Ticks at the short interval; each tick reads only those due, so a friend away
                // for hours is still read every couple of minutes, not every tick.
                await PollDueAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.EffectiveActivePollInterval, cancellationToken).ConfigureAwait(false);
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
            _lastAddress = inputs.Address;
            var candidate = _composer.Compose(
                inputs.DisplayName, inputs.Games, inputs.RunningGameIds, inputs.Sharing,
                sequence: 0, _clock(), inputs.Profile, inputs.Invite, inputs.Address);

            var fingerprint = PresenceComposer.ContentFingerprint(candidate);
            var heartbeatDue = _lastPublishedAt is not { } previous ||
                _clock() - previous >= _options.Heartbeat;

            // Nothing a friend would see has changed and the document is still fresh: writing it
            // again would only churn the sync client.
            if (fingerprint == _lastFingerprint && !heartbeatDue) return;

            var snapshot = candidate with { Sequence = _sequence.Next(_clock()) };
            var result = await PublishSnapshotAsync(reason, snapshot, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                _lastFingerprint = fingerprint;
                lock (_historyGate)
                {
                    _publishedHistory.Add((snapshot.Sequence, _clock()));
                    if (_publishedHistory.Count > 64) _publishedHistory.RemoveRange(0, _publishedHistory.Count - 64);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _publishGate.Release();
        }

        // Outside the gate: a slow read of our own address must never delay the next publish.
        if (AddressCheckDue()) _ = CheckAddressInBackgroundAsync(cancellationToken);
    }

    private bool AddressCheckDue()
    {
        if (string.IsNullOrEmpty(_publisher.PresenceUrl)) return false;
        var now = _clock();
        if (now - _startedAt < _options.EffectiveAddressFirstCheck) return false;
        return _lastAddressCheck is not { } last || now - last >= _options.EffectiveAddressCheckInterval;
    }

    private async Task CheckAddressInBackgroundAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _checkingAddress, 1) == 1) return;
        try
        {
            await CheckAddressNowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _checkingAddress, 0);
        }
    }

    /// <summary>
    /// Reads our own address back and says what friends are getting from it. Public so the
    /// profile page can ask on demand; the service also runs it on its own while publishing.
    /// </summary>
    public async Task<PresenceAddressReport> CheckAddressNowAsync(CancellationToken cancellationToken = default)
    {
        var url = _publisher.PresenceUrl;
        var now = _clock();
        _lastAddressCheck = now;

        PresenceAddressReport report;
        if (string.IsNullOrEmpty(url))
        {
            report = new(PresenceAddressHealth.Unknown, now);
        }
        else
        {
            var (read, opened) = await _fetcher.ReadOwnAsync(url, cancellationToken).ConfigureAwait(false);
            report = Judge(read, opened, _clock());
        }

        LastAddressReport = report;
        AddressChecked?.Invoke(report);
        return report;
    }

    private PresenceAddressReport Judge(PresenceReadResult read, PresenceSnapshot? opened, DateTimeOffset now)
    {
        if (read.NotPublished) return new(PresenceAddressHealth.NotFound, now, Detail: read.Error);
        if (!read.Ok) return new(PresenceAddressHealth.Unreachable, now, Detail: read.Error);
        if (opened is null) return new(PresenceAddressHealth.NotOurs, now);

        if (_sequence.Reconcile(opened.Sequence) == PresenceSequenceReconciliation.RemoteAhead)
            return new(PresenceAddressHealth.SomeoneElsePublishes, now);

        lock (_historyGate)
        {
            // The newest document that has had plenty of time to arrive. Serving anything older
            // than it means the sync client is behind by at least the tolerance.
            var tolerance = _options.EffectiveAddressLagTolerance;
            var due = _publishedHistory.Where(entry => now - entry.At >= tolerance).ToArray();
            if (due.Length > 0 && opened.Sequence < due[^1].Sequence)
            {
                var firstMissed = _publishedHistory.First(entry => entry.Sequence > opened.Sequence);
                return new(PresenceAddressHealth.Lagging, now, Lag: now - firstMissed.At);
            }
        }

        return new(PresenceAddressHealth.Healthy, now);
    }

    private async Task<PresencePublishResult> PublishSnapshotAsync(
        PresencePublishReason reason,
        PresenceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var envelope = SealedPresence.Seal(
            _identity, snapshot, PresenceRecipients.ForPublication(_identity, _friends));
        var json = SealedPresence.ToJson(envelope);
        var result = await _publisher
            .PublishAsync(json, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            _lastPublishedAt = _clock();
            PublishCount++;
            DocumentPublished?.Invoke(json, snapshot.Sequence);
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
