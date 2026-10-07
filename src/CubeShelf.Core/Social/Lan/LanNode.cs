using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CubeShelf.Core.Social.Lan;

/// <summary>Someone on the local network who asked to be found, and is not already a friend.</summary>
public sealed record LanPeer(string PublicKey, string DisplayName, IPEndPoint Endpoint, DateTimeOffset SeenAt)
{
    public string Handle => PeerName.Handle(DisplayName, PublicKey);
}

/// <summary>Someone on the local network who asked to become a friend, proved who they are, and waits for an answer.</summary>
public sealed record LanFriendRequest(
    string PublicKey,
    string DisplayName,
    string PresenceUrl,
    IPEndPoint Endpoint,
    DateTimeOffset ReceivedAt)
{
    public string Handle => PeerName.Handle(DisplayName, PublicKey);
}

public enum LanIntroductionResult
{
    /// <summary>Delivered; the other side decides.</summary>
    Sent = 0,

    /// <summary>They already had us as a friend: we added them too, and that is the whole story.</summary>
    AlreadyFriends = 1,

    /// <summary>An answer to their request, delivered: they add us as we added them.</summary>
    Accepted = 2,

    /// <summary>They are not taking introductions right now, or not from us.</summary>
    Refused = 3,

    /// <summary>No answer at all.</summary>
    Unreachable = 4,

    /// <summary>
    /// Whoever answered could not prove they hold the key they announced. Someone else on the
    /// network is impersonating them; nothing was exchanged.
    /// </summary>
    NotGenuine = 5
}

/// <param name="TcpPort">0 for a port of the system's choosing, which the announcements carry.</param>
public sealed record LanNodeOptions(
    int TcpPort = 0,
    TimeSpan? AnnouncementInterval = null,
    TimeSpan? PeerTimeout = null,
    TimeSpan? MinimumPullGap = null);

/// <summary>
/// CubeShelf on the local network: friends found and read directly, without the sync service in
/// between, and strangers befriended in person, like pairing two Bluetooth devices.
///
/// Three things travel. Announcements, broadcast a few times a minute, carry tags only friends
/// recognise (<see cref="LanAnnouncement"/>). A friend who recognises us connects and pulls our
/// presence document -- the very same sealed document the sync folder carries, opened and checked
/// exactly the same way, so the network adds speed and nothing else. And introductions, where
/// each side proves it holds the private key behind the public one it shows, so a stranger on the
/// network cannot pass for someone the user meant to add.
///
/// Nothing here trusts the network: every document is authenticated by the key a pair shares,
/// every sequence is checked against what was already seen, every message is size-capped and
/// timed out, and an introduction is only listened to while the user has asked to be found.
/// </summary>
public sealed class LanNode : IAsyncDisposable
{
    private readonly PeerIdentity _identity;
    private readonly FriendStore _friends;
    private readonly ILanAnnouncer _announcer;
    private readonly Func<string> _displayName;
    private readonly Func<string> _ownUrl;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _peerTimeout;
    private readonly TimeSpan _minimumPullGap;
    private readonly int _requestedPort;
    private readonly string _ownKey;
    private readonly string _instance = LanProtocol.RandomNonce();

    private readonly ConcurrentDictionary<string, LanPeer> _strangers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (IPEndPoint Endpoint, DateTimeOffset SeenAt)> _friendEndpoints = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanFriendRequest> _incoming = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _outgoing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPull = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _pairKeys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connections = new(8, 8);

    private volatile string? _document;
    private long _sequence;
    private DateTimeOffset? _discoverableUntil;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private (long Epoch, DateTimeOffset BuiltAt, Dictionary<string, string> TagToFriend)? _tagIndex;
    private bool _disposed;

    public LanNode(
        PeerIdentity identity,
        FriendStore friends,
        ILanAnnouncer announcer,
        Func<string> displayName,
        Func<string> ownUrl,
        LanNodeOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _friends = friends ?? throw new ArgumentNullException(nameof(friends));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _ownUrl = ownUrl ?? throw new ArgumentNullException(nameof(ownUrl));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        options ??= new LanNodeOptions();
        _requestedPort = options.TcpPort;
        _interval = options.AnnouncementInterval ?? LanProtocol.AnnouncementInterval;
        _peerTimeout = options.PeerTimeout ?? LanProtocol.PeerTimeout;
        _minimumPullGap = options.MinimumPullGap ?? TimeSpan.FromSeconds(2);
        _ownKey = Convert.ToBase64String(identity.PublicKey);
    }

    /// <summary>A friend's document arrived over the network, opened and accepted.</summary>
    public event Action<PresenceFetchOutcome>? FriendDocumentReceived;

    /// <summary>Who is on the network, or who asked us, changed.</summary>
    public event Action? Changed;

    /// <summary>Someone we asked accepted, and is now in the friends list.</summary>
    public event Action<string>? FriendAdded;

    /// <summary>The TCP port the node listens on, 0 before <see cref="Start"/>.</summary>
    public int Port { get; private set; }

    public bool IsDiscoverable => _discoverableUntil is { } until && until > _clock();

    public DateTimeOffset? DiscoverableUntil => IsDiscoverable ? _discoverableUntil : null;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;

        _lifetime = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _requestedPort);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _announcer.Received += OnDatagram;
        _announcer.Start();

        _ = AcceptLoopAsync(_listener, _lifetime.Token);
        _ = AnnounceLoopAsync(_lifetime.Token);
    }

    /// <summary>The sealed document friends pull, as just published. Announced at once.</summary>
    public void SetDocument(string envelopeJson, long sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeJson);
        _document = envelopeJson;
        Interlocked.Exchange(ref _sequence, sequence);
        _ = AnnounceAsync(_lifetime?.Token ?? CancellationToken.None);
    }

    /// <summary>Makes us findable by strangers on the network, for a while, or stops it.</summary>
    public void SetDiscoverable(bool discoverable)
    {
        _discoverableUntil = discoverable ? _clock() + LanProtocol.DiscoverableFor : null;
        if (!discoverable)
        {
            // Requests are only listened to while findable; one left pending is answered
            // nowhere, so it goes too.
            _incoming.Clear();
        }
        Changed?.Invoke();
        _ = AnnounceAsync(_lifetime?.Token ?? CancellationToken.None);
    }

    /// <summary>Strangers who asked to be found and were heard recently.</summary>
    public IReadOnlyList<LanPeer> Strangers
    {
        get
        {
            var now = _clock();
            var listed = _friends.Load().Select(friend => friend.PublicKey).ToHashSet(StringComparer.Ordinal);
            return _strangers.Values
                .Where(peer => now - peer.SeenAt <= _peerTimeout && !listed.Contains(peer.PublicKey))
                .OrderBy(peer => peer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<LanFriendRequest> IncomingRequests =>
        _incoming.Values.Where(request => _clock() - request.ReceivedAt <= TimeSpan.FromMinutes(30))
            .OrderBy(request => request.ReceivedAt)
            .ToArray();

    /// <summary>Whether we asked this peer and are waiting for their answer.</summary>
    public bool IsAwaiting(string publicKey) =>
        _outgoing.TryGetValue(publicKey, out var at) && _clock() - at <= TimeSpan.FromMinutes(30);

    /// <summary>Whether this friend was heard on the local network recently.</summary>
    public bool IsOnNetwork(string friendPublicKey) =>
        _friendEndpoints.TryGetValue(friendPublicKey, out var seen) && _clock() - seen.SeenAt <= _peerTimeout;

    // ------------------------------------------------------------------ announcing

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await AnnounceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        if (Port == 0 || _disposed) return;

        try
        {
            foreach (var announcement in BuildAnnouncements())
                await _announcer.SendAsync(LanProtocol.Serialize(announcement), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException or IOException)
        {
        }
    }

    public IEnumerable<LanAnnouncement> BuildAnnouncements()
    {
        var epoch = LanProtocol.EpochOf(_clock());
        var tags = ActiveFriendKeys()
            .Select(key => LanProtocol.Tag(PairKey(key.Encoded, key.Raw), epoch))
            .ToList();

        var discoverable = IsDiscoverable;
        var chunks = tags.Chunk(LanProtocol.MaximumTagsPerAnnouncement).Select(chunk => chunk.ToList()).ToList();
        if (chunks.Count == 0) chunks.Add(new List<string>());

        foreach (var chunk in chunks)
        {
            // Padded and shuffled, like the lockboxes: the length says a range, not a count.
            while (chunk.Count % LanProtocol.TagPadding != 0 || chunk.Count == 0) chunk.Add(LanProtocol.RandomTag());
            for (var index = chunk.Count - 1; index > 0; index--)
            {
                var swap = System.Security.Cryptography.RandomNumberGenerator.GetInt32(index + 1);
                (chunk[index], chunk[swap]) = (chunk[swap], chunk[index]);
            }

            yield return new LanAnnouncement
            {
                Instance = _instance,
                Port = Port,
                Sequence = Interlocked.Read(ref _sequence),
                Tags = chunk,
                PublicKey = discoverable ? _ownKey : null,
                Name = discoverable ? PeerName.Sanitize(_displayName()) : null
            };
        }
    }

    private IEnumerable<(string Encoded, byte[] Raw)> ActiveFriendKeys() =>
        _friends.Load()
            .Where(friend => !friend.Paused && !friend.Blocked)
            .Select(friend => (friend.PublicKey, Raw: LanProtocol.DecodeKey(friend.PublicKey)))
            .Where(pair => pair.Raw is not null)
            .Select(pair => (pair.PublicKey, pair.Raw!));

    private byte[] PairKey(string encoded, byte[] raw) =>
        _pairKeys.GetOrAdd(encoded, _ => _identity.DeriveSharedKey(raw));

    // ------------------------------------------------------------------ hearing

    private void OnDatagram(byte[] datagram, IPAddress from)
    {
        if (_disposed) return;
        var announcement = LanProtocol.ParseAnnouncement(datagram);
        if (announcement is null || announcement.Instance == _instance) return;

        var endpoint = new IPEndPoint(from, announcement.Port);
        var now = _clock();
        var changed = false;

        // Someone asking to be found.
        if (LanProtocol.DecodeKey(announcement.PublicKey) is not null &&
            !string.Equals(announcement.PublicKey, _ownKey, StringComparison.Ordinal))
        {
            var name = PeerName.Sanitize(announcement.Name ?? "");
            var known = _strangers.TryGetValue(announcement.PublicKey!, out var before);
            _strangers[announcement.PublicKey!] = new LanPeer(announcement.PublicKey!, name.Length > 0 ? name : "?", endpoint, now);
            changed = !known || !Equals(before!.Endpoint, endpoint) || now - before.SeenAt > _peerTimeout;
        }

        // A friend, recognised by a tag only the two of us can compute.
        var index = TagIndex(now);
        foreach (var tag in announcement.Tags)
        {
            if (!index.TryGetValue(tag, out var friendKey)) continue;

            var wasOnNetwork = IsOnNetwork(friendKey);
            _friendEndpoints[friendKey] = (endpoint, now);
            changed |= !wasOnNetwork;

            var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == friendKey);
            if (friend is { Paused: false, Blocked: false } && announcement.Sequence > friend.LastSequence)
                _ = PullAsync(friendKey, endpoint, _lifetime?.Token ?? CancellationToken.None);
            break;
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Tag to friend, for the current window and its neighbours -- two clocks a few minutes
    /// apart still recognise each other. Rebuilt when the window turns or the list may have changed.
    /// </summary>
    private Dictionary<string, string> TagIndex(DateTimeOffset now)
    {
        var epoch = LanProtocol.EpochOf(now);
        if (_tagIndex is { } cached && cached.Epoch == epoch && now - cached.BuiltAt < TimeSpan.FromSeconds(20))
            return cached.TagToFriend;

        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (encoded, raw) in ActiveFriendKeys())
        {
            var pair = PairKey(encoded, raw);
            for (var delta = -1; delta <= 1; delta++) index[LanProtocol.Tag(pair, epoch + delta)] = encoded;
        }

        _tagIndex = (epoch, now, index);
        return index;
    }

    // ------------------------------------------------------------------ pulling a friend's document

    private async Task PullAsync(string friendKey, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var now = _clock();
        if (_lastPull.TryGetValue(friendKey, out var last) && now - last < _minimumPullGap) return;
        _lastPull[friendKey] = now;

        try
        {
            var reply = await ExchangeAsync(endpoint, new[] { new LanMessage { Op = "presence" } }, cancellationToken)
                .ConfigureAwait(false);
            if (reply.Count == 0 || reply[0] is not { Ok: true, Document: { Length: > 0 } document }) return;

            var outcome = AcceptDocument(friendKey, document);
            if (outcome is not null) FriendDocumentReceived?.Invoke(outcome);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// The same checks as a document read from the sync folder: it opens under the key we share
    /// with this friend, and it is newer than anything accepted from them.
    /// </summary>
    public PresenceFetchOutcome? AcceptDocument(string friendKey, string envelopeJson)
    {
        if (envelopeJson.Length > PresencePolicy.MaximumDocumentBytes) return null;
        var raw = LanProtocol.DecodeKey(friendKey);
        if (raw is null) return null;

        if (!SealedPresence.TryOpen(_identity, raw, SealedPresence.FromJson(envelopeJson), out var snapshot) || snapshot is null)
            return null;

        var now = _clock();
        if (!_friends.TryAcceptSequence(friendKey, snapshot.Sequence, now)) return null;

        _friends.Update(friendKey, entry => entry.SharesWithUs = true);
        _friends.AdoptAddress(friendKey, snapshot.Address);
        _friends.AdoptMeshAddress(friendKey, snapshot.Mesh);
        return new PresenceFetchOutcome(friendKey, PresenceFetchStatus.Updated, snapshot);
    }

    // ------------------------------------------------------------------ introductions, as the one asking

    /// <summary>Asks a stranger on the network to become a friend. They accept, or not, on their side.</summary>
    public async Task<LanIntroductionResult> RequestFriendshipAsync(LanPeer peer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peer);
        var result = await IntroduceAsync(peer.Endpoint, peer.PublicKey, "request", cancellationToken).ConfigureAwait(false);
        if (result == LanIntroductionResult.Sent) _outgoing[peer.PublicKey] = _clock();
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Accepts a request: they are added here at once -- the user just said yes -- and told, so
    /// they add us in turn. If they cannot be reached now, they stay added here, pending.
    /// </summary>
    public async Task<LanIntroductionResult> AcceptAsync(LanFriendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _incoming.TryRemove(request.PublicKey, out _);

        var raw = LanProtocol.DecodeKey(request.PublicKey);
        if (raw is null) return LanIntroductionResult.NotGenuine;

        AddFriend(raw, request.DisplayName, request.PresenceUrl);
        var result = await IntroduceAsync(request.Endpoint, request.PublicKey, "accept", cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
        return result == LanIntroductionResult.Sent ? LanIntroductionResult.Accepted : result;
    }

    public void Decline(LanFriendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _incoming.TryRemove(request.PublicKey, out _);
        Changed?.Invoke();
    }

    private async Task<LanIntroductionResult> IntroduceAsync(
        IPEndPoint endpoint,
        string expectedKey,
        string purpose,
        CancellationToken cancellationToken)
    {
        var raw = LanProtocol.DecodeKey(expectedKey);
        if (raw is null) return LanIntroductionResult.NotGenuine;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LanProtocol.ExchangeTimeout * 2);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();

            var ourNonce = LanProtocol.RandomNonce();
            await WriteLineAsync(stream, new LanMessage
            {
                Op = "introduce",
                Purpose = purpose,
                PublicKey = _ownKey,
                Name = PeerName.Sanitize(_displayName()),
                Nonce = ourNonce,
                Port = Port
            }, timeout.Token).ConfigureAwait(false);

            var challenge = await ReadLineAsync(stream, LanProtocol.MaximumRequestBytes, timeout.Token).ConfigureAwait(false);
            if (challenge is not { Ok: true }) return LanIntroductionResult.Refused;

            // The one who answered has to be the one we meant, and has to prove it.
            if (!string.Equals(challenge.PublicKey, expectedKey, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(challenge.Nonce))
                return LanIntroductionResult.NotGenuine;

            var pair = PairKey(expectedKey, raw);
            if (!LanProtocol.ProofMatches(pair, "S", ourNonce, challenge.Nonce, challenge.Proof))
                return LanIntroductionResult.NotGenuine;

            // Only now does anything more than a name and a public key leave this machine.
            await WriteLineAsync(stream, new LanMessage
            {
                Proof = LanProtocol.Proof(pair, "C", challenge.Nonce, ourNonce),
                Url = LanProtocol.SafeUrl(_ownUrl())
            }, timeout.Token).ConfigureAwait(false);

            var verdict = await ReadLineAsync(stream, LanProtocol.MaximumRequestBytes, timeout.Token).ConfigureAwait(false);
            if (verdict is not { Ok: true }) return LanIntroductionResult.Refused;

            switch (verdict.Result)
            {
                case "queued":
                    return LanIntroductionResult.Sent;
                case "friends":
                    // They already had us: adding them back completes it, nothing left to wait for.
                    AddFriend(raw, PeerName.Sanitize(challenge.Name ?? ""), LanProtocol.SafeUrl(verdict.Url));
                    return LanIntroductionResult.AlreadyFriends;
                case "added":
                    _friends.AdoptAddress(expectedKey, verdict.Url);
                    return LanIntroductionResult.Sent;
                default:
                    return LanIntroductionResult.Refused;
            }
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            return cancellationToken.IsCancellationRequested ? LanIntroductionResult.Refused : LanIntroductionResult.Unreachable;
        }
    }

    private void AddFriend(byte[] key, string name, string url)
    {
        var display = PeerName.TryNormalize(name, out var normalized, out _) ? normalized : "Ami";
        if (!_friends.TryAdd(new FriendCodePayload(key, LanProtocol.SafeUrl(url), display), display, _identity.PublicKey, out _))
        {
            // Already listed: at most, learn the address we did not have.
            _friends.AdoptAddress(Convert.ToBase64String(key), url);
        }
    }

    // ------------------------------------------------------------------ serving

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (cancellationToken.IsCancellationRequested) return;
                continue;
            }

            // A flood of connections costs those connections, never the node.
            if (!_connections.Wait(0))
            {
                client.Dispose();
                continue;
            }

            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(LanProtocol.ExchangeTimeout * 2);
                await using var stream = client.GetStream();
                var remote = client.Client.RemoteEndPoint as IPEndPoint;

                var first = await ReadLineAsync(stream, LanProtocol.MaximumRequestBytes, timeout.Token).ConfigureAwait(false);
                switch (first?.Op)
                {
                    case "presence":
                        var document = _document;
                        await WriteLineAsync(stream, document is null
                            ? new LanMessage { Ok = false, Why = "nothing" }
                            : new LanMessage { Ok = true, Document = document }, timeout.Token).ConfigureAwait(false);
                        break;
                    case "introduce" when remote is not null:
                        await ServeIntroductionAsync(stream, first, remote.Address, timeout.Token).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            _connections.Release();
        }
    }

    private async Task ServeIntroductionAsync(Stream stream, LanMessage opening, IPAddress from, CancellationToken cancellationToken)
    {
        var theirKey = opening.PublicKey ?? "";
        var raw = LanProtocol.DecodeKey(theirKey);
        if (raw is null || theirKey == _ownKey || string.IsNullOrEmpty(opening.Nonce) || opening.Nonce.Length > 64)
        {
            await WriteLineAsync(stream, new LanMessage { Ok = false, Why = "malformed" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = _friends.Load().FirstOrDefault(friend => friend.PublicKey == theirKey);
        var allowed = opening.Purpose switch
        {
            // From a stranger, only while the user asked to be found; from a friend, always --
            // it is how someone who lost us gets back in, and they are already trusted.
            "request" => existing is null ? IsDiscoverable : !existing.Blocked,
            // Only an answer to something we asked.
            "accept" => IsAwaiting(theirKey) && existing is not { Blocked: true },
            _ => false
        };
        if (!allowed)
        {
            await WriteLineAsync(stream, new LanMessage { Ok = false, Why = "not-listening" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var pair = PairKey(theirKey, raw);
        var ourNonce = LanProtocol.RandomNonce();
        await WriteLineAsync(stream, new LanMessage
        {
            Ok = true,
            PublicKey = _ownKey,
            Name = PeerName.Sanitize(_displayName()),
            Nonce = ourNonce,
            Proof = LanProtocol.Proof(pair, "S", opening.Nonce, ourNonce)
        }, cancellationToken).ConfigureAwait(false);

        var answer = await ReadLineAsync(stream, LanProtocol.MaximumRequestBytes, cancellationToken).ConfigureAwait(false);
        if (answer is null || !LanProtocol.ProofMatches(pair, "C", ourNonce, opening.Nonce, answer.Proof))
        {
            await WriteLineAsync(stream, new LanMessage { Ok = false, Why = "proof" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var name = PeerName.Sanitize(opening.Name ?? "");
        var url = LanProtocol.SafeUrl(answer.Url);

        if (opening.Purpose == "accept")
        {
            _outgoing.TryRemove(theirKey, out _);
            AddFriend(raw, name, url);
            await WriteLineAsync(stream, new LanMessage { Ok = true, Result = "added", Url = LanProtocol.SafeUrl(_ownUrl()) }, cancellationToken)
                .ConfigureAwait(false);
            FriendAdded?.Invoke(PeerName.Handle(name, theirKey));
            Changed?.Invoke();
            return;
        }

        if (existing is not null)
        {
            // Already our friend: they lost us, or never had us. Telling them so is enough for
            // them to add us back; nothing for the user to decide.
            _friends.AdoptAddress(theirKey, url);
            await WriteLineAsync(stream, new LanMessage { Ok = true, Result = "friends", Url = LanProtocol.SafeUrl(_ownUrl()) }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Bounded: a request is a line on someone's screen, and a network full of them is noise.
        if (_incoming.Count >= 8 && !_incoming.ContainsKey(theirKey))
        {
            await WriteLineAsync(stream, new LanMessage { Ok = false, Why = "busy" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Where to answer: the port they said they listen on, or the one their announcements carry.
        var port = opening.Port is > 0 and <= 65535
            ? opening.Port.Value
            : _strangers.TryGetValue(theirKey, out var peer) ? peer.Endpoint.Port : 0;
        _incoming[theirKey] = new LanFriendRequest(theirKey, name.Length > 0 ? name : "?", url, new IPEndPoint(from, port), _clock());
        await WriteLineAsync(stream, new LanMessage { Ok = true, Result = "queued" }, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ wire

    private async Task<IReadOnlyList<LanMessage>> ExchangeAsync(
        IPEndPoint endpoint,
        IReadOnlyList<LanMessage> requests,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LanProtocol.ExchangeTimeout);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var replies = new List<LanMessage>();
        foreach (var request in requests)
        {
            await WriteLineAsync(stream, request, timeout.Token).ConfigureAwait(false);
            var reply = await ReadLineAsync(stream, LanProtocol.MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            if (reply is null) break;
            replies.Add(reply);
        }

        return replies;
    }

    private static async Task WriteLineAsync(Stream stream, LanMessage message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(LanProtocol.Serialize(message) + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One line, capped: whatever is on the other end, it cannot make us buffer more.</summary>
    private static async Task<LanMessage?> ReadLineAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var one = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;

            var newline = Array.IndexOf(one, (byte)'\n', 0, read);
            var take = newline >= 0 ? newline : read;
            if (buffer.Length + take > maximumBytes) return null;
            buffer.Write(one, 0, take);
            if (newline >= 0) break;
        }

        return LanProtocol.ParseMessage(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>
    /// Announces the document just set -- the farewell, at shutdown -- and leaves friends a moment
    /// to pull it before the listener goes away.
    /// </summary>
    public async Task FarewellAsync(TimeSpan grace)
    {
        try
        {
            await AnnounceAsync(CancellationToken.None).WaitAsync(grace).ConfigureAwait(false);
            await Task.Delay(grace).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        _lifetime?.Cancel();
        _announcer.Received -= OnDatagram;
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }
        _announcer.Dispose();
        _lifetime?.Dispose();

        foreach (var key in _pairKeys.Values) System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        _pairKeys.Clear();
        return ValueTask.CompletedTask;
    }
}
