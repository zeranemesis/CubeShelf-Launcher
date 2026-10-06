using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>Someone who asked, through the network, to become a friend.</summary>
public sealed record MeshFriendRequest(FriendCodePayload Code, string Note, DateTimeOffset ReceivedAt)
{
    public string PublicKey => Convert.ToBase64String(Code.PublicKey);
    public string Handle => Code.Handle;
}

/// <summary>
/// Friends over the network: where each one's presence is kept, how they find it, the direct
/// sessions between friends who are both online, and requests from people who are not friends yet.
///
/// Our sealed document lives in the table at a place that changes every day and that only we can
/// write -- its key is derived from our identity. Friends find it through a pointer each: a small
/// record at a place only that friend and we can compute (from the key we share), which says where
/// today's document is, encrypted for that friend. So the holders of these records learn nothing
/// about who they belong to, a stranger holding our friend code cannot find our presence at all,
/// and nobody can follow a place from one day to the next.
///
/// When a friend's CubeShelf is reachable, a direct session replaces the table: each side proves
/// it holds its identity -- an HMAC under the pair key, bound to that session's handshake, so it
/// cannot be replayed into another -- and documents are then pushed the moment they change.
/// </summary>
public sealed class MeshFriends : IAsyncDisposable
{
    public static readonly TimeSpan PointerRefresh = TimeSpan.FromHours(6);
    private static readonly TimeSpan RecordLifetime = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(5);
    private const int RequestProofLength = 32;
    private const int MaximumNoteBytes = 280;

    private readonly MeshNode _node;
    private readonly PeerIdentity _identity;
    private readonly FriendStore _friends;
    private readonly Func<string> _ownCode;
    private readonly Func<DateTimeOffset> _clock;
    private readonly byte[] _seed;
    private readonly string _ownKey;

    private readonly ConcurrentDictionary<string, MeshSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Day, NodeId Locator)> _mainLocators = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Day, DateTimeOffset At)> _pointersPublished = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long NextAttempt, int Failures)> _attempts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MeshFriendRequest> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _dismissed = new(StringComparer.Ordinal);

    /// <summary>The day a request last reached each pending friend's mailbox: one a day keeps it there until they answer.</summary>
    private readonly ConcurrentDictionary<string, long> _requestDelivered = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _pointerGate = new(1, 1);

    /// <summary>
    /// A document that arrived on a session before its identity proof completed on our side --
    /// the other side proves first and pushes at once, and its push can overtake our reading of
    /// its answer. Kept, one per session, until the proof lands.
    /// </summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshSession, byte[]> _early = new();

    /// <summary>Sessions we are proving ourselves on right now: the only ones whose early document is kept.</summary>
    private readonly ConcurrentDictionary<uint, byte> _helloPending = new();

    private volatile string? _lastEnvelope;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private bool _disposed;

    public MeshFriends(MeshNode node, PeerIdentity identity, FriendStore friends, Func<string> ownCode, Func<DateTimeOffset>? clock = null)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _friends = friends ?? throw new ArgumentNullException(nameof(friends));
        _ownCode = ownCode ?? throw new ArgumentNullException(nameof(ownCode));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ownKey = Convert.ToBase64String(identity.PublicKey);

        var scalar = identity.ExportPrivateScalar();
        try
        {
            _seed = MeshCrypto.Hkdf(scalar, "cubeshelf-mesh-presence-seed-v1", 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }

        _node.ApplicationRequests = HandleRequestAsync;
        _node.ApplicationNotification += OnNotification;
        _node.Transport.SessionClosed += OnSessionClosed;
        // Joining the network, gaining a relay: what was waiting for it can go now.
        _node.Changed += Kick;
    }

    /// <summary>Looking offline: no direct session is offered or accepted, since one would give us away.</summary>
    public bool Invisible { get; set; }

    /// <summary>A friend's document arrived over a direct session, opened and accepted.</summary>
    public event Action<PresenceFetchOutcome>? DocumentReceived;

    /// <summary>Direct sessions or pending requests changed.</summary>
    public event Action? Changed;

    public IReadOnlyList<MeshFriendRequest> Requests => _requests.Values.OrderBy(request => request.ReceivedAt).ToArray();

    public bool IsConnected(string friendKey) => _sessions.TryGetValue(friendKey, out var session) && !session.Closed;

    public int ConnectedCount => _sessions.Values.Count(session => !session.Closed);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _loop = RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var tick = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Requests are read every minute: someone who just pasted our code is waiting.
                    if (tick % 2 == 0) await CheckMailboxAsync(cancellationToken).ConfigureAwait(false);
                    await DeliverRequestsAsync(cancellationToken).ConfigureAwait(false);
                    await ConnectFriendsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                }
                tick++;
                await _wake.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Runs the loop now rather than at its next turn: the friends list just changed.</summary>
    public void Kick()
    {
        try
        {
            _wake.Release();
        }
        catch (Exception exception) when (exception is SemaphoreFullException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// A request for every friend who has not added us back -- the one we just added from their
    /// code, someone added last week who has not answered -- once a day, for two weeks: a mailbox
    /// only keeps a day, and nobody should have to paste a code back. Retried at every turn until
    /// one actually lands, since the first try often comes before we have joined the network.
    /// </summary>
    private async Task DeliverRequestsAsync(CancellationToken cancellationToken)
    {
        if (_node.Table.Count == 0 && !_node.Dht.Serving) return;
        var now = _clock();
        var today = DayOf(now);
        var due = _friends.Load()
            .Where(friend => !friend.Paused && !friend.Blocked && friend.SharesWithUs != true && now - friend.AddedAt < TimeSpan.FromDays(14))
            .Where(friend => !_requestDelivered.TryGetValue(friend.PublicKey, out var day) || day != today)
            .Take(4)
            .ToArray();
        foreach (var friend in due)
        {
            if (await SendRequestAsync(Convert.FromBase64String(friend.PublicKey), null, cancellationToken).ConfigureAwait(false))
                _requestDelivered[friend.PublicKey] = today;
        }
    }

    // ------------------------------------------------------------------ where things live

    internal static long DayOf(DateTimeOffset time) => time.ToUnixTimeSeconds() / 86400;

    private ECDsa MainKey(long day) => MeshCrypto.DeriveSigningKey(_seed, "presence|" + day);

    /// <summary>The pointer <paramref name="owner"/> keeps for one friend on <paramref name="day"/>: both of them can derive it, nobody else.</summary>
    internal static ECDsa PointerKey(byte[] pairKey, byte[] owner, long day) =>
        MeshCrypto.DeriveSigningKey(pairKey, "pointer|" + Convert.ToHexString(SHA256.HashData(owner), 0, 16) + "|" + day);

    private static byte[] PointerSealKey(byte[] pairKey) => MeshCrypto.Hkdf(pairKey, "cubeshelf-mesh-pointer-v1", 32);

    /// <summary>Where requests for <paramref name="recipient"/> are dropped on <paramref name="day"/>.</summary>
    public static NodeId MailboxOf(byte[] recipient, long day)
    {
        var dayBytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(dayBytes, day);
        return NodeId.FromHash(MeshCrypto.Hash("cubeshelf-mailbox-v1", recipient, dayBytes));
    }

    // ------------------------------------------------------------------ publishing

    /// <summary>
    /// Publishes a sealed document: into the table under today's key, pointers for any friend
    /// whose pointer is due, and straight to every friend with a direct session.
    /// </summary>
    public async Task<PresencePublishResult> PublishAsync(string envelopeJson, long sequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(envelopeJson);
        _lastEnvelope = envelopeJson;
        var payload = Encoding.UTF8.GetBytes(envelopeJson);
        if (payload.Length > MeshRecord.MaximumPayload)
            return new PresencePublishResult(false, "", "Document de présence trop gros pour le réseau.");

        var pushed = Push(payload);
        var now = _clock();
        var day = DayOf(now);
        using var key = MainKey(day);
        var record = MeshRecord.CreateSigned(key, (ulong)Math.Max(sequence, 1), now + RecordLifetime, payload);

        var stored = 0;
        if (_node.Table.Count > 0 || _node.Dht.Serving)
        {
            stored = await _node.Dht.StoreAsync(record, cancellationToken).ConfigureAwait(false);
            await PublishPointersAsync(record.Locator, day, now, cancellationToken).ConfigureAwait(false);
        }

        return stored > 0 || pushed > 0
            ? new PresencePublishResult(true, "")
            : new PresencePublishResult(false, "", "Aucun nœud du réseau CubeShelf n’a pu être joint.");
    }

    private async Task PublishPointersAsync(NodeId mainLocator, long day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await _pointerGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var due = _friends.Load()
                .Where(friend => !friend.Paused && !friend.Blocked)
                .Where(friend => !_pointersPublished.TryGetValue(friend.PublicKey, out var published) ||
                                 published.Day != day || now - published.At >= PointerRefresh)
                .ToArray();

            foreach (var batch in due.Chunk(4))
            {
                await Task.WhenAll(batch.Select(async friend =>
                {
                    var friendKey = Convert.FromBase64String(friend.PublicKey);
                    var pair = _identity.DeriveSharedKey(friendKey);
                    try
                    {
                        using var signer = PointerKey(pair, _identity.PublicKey, day);
                        var locator = MeshRecord.LocatorOf(MeshCrypto.PublicKeyOf(signer));
                        var sealedLocator = MeshCrypto.Seal(PointerSealKey(pair), mainLocator.ToBytes(), locator.ToBytes());
                        var pointer = MeshRecord.CreateSigned(signer, (ulong)now.ToUnixTimeSeconds(), now + RecordLifetime, sealedLocator);
                        if (await _node.Dht.StoreAsync(pointer, cancellationToken).ConfigureAwait(false) > 0)
                            _pointersPublished[friend.PublicKey] = (day, now);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(pair);
                    }
                })).ConfigureAwait(false);
            }
        }
        finally
        {
            _pointerGate.Release();
        }
    }

    /// <summary>The friends list changed: pointers for newcomers are due at the next publish, requests now.</summary>
    public void FriendsChanged()
    {
        Kick();
        var current = _friends.Load().Select(friend => friend.PublicKey).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _pointersPublished.Keys.Where(key => !current.Contains(key)).ToArray()) _pointersPublished.TryRemove(key, out _);
        foreach (var (key, session) in _sessions.ToArray())
        {
            var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == key);
            if (friend is null || friend.Paused || friend.Blocked)
            {
                // Removed, paused or blocked: the direct session ends now, not at its next keepalive.
                _sessions.TryRemove(key, out _);
                session.FriendKey = null;
                _node.Transport.Close(session);
            }
        }
    }

    private int Push(byte[] payload)
    {
        var pushed = 0;
        foreach (var (key, session) in _sessions)
        {
            if (session.Closed) continue;
            _node.Transport.Notify(session, MeshOps.FriendDocument, payload);
            pushed++;
        }
        return pushed;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// A friend's document from the table: today's pointer, then the document it names; yesterday's
    /// if today has none yet. The record's sequence comes along, so an unchanged document is not
    /// opened again.
    /// </summary>
    public async Task<PresenceNetworkRead> ReadAsync(Friend friend, byte[] friendPublicKey, CancellationToken cancellationToken = default)
    {
        if (_node.Table.Count == 0 && !_node.Dht.Serving) return new PresenceNetworkRead(null, 0, Reachable: false);

        var pair = _identity.DeriveSharedKey(friendPublicKey);
        try
        {
            var today = DayOf(_clock());
            foreach (var day in new[] { today, today - 1 })
            {
                NodeId? locator = _mainLocators.TryGetValue(friend.PublicKey, out var cached) && cached.Day == day
                    ? cached.Locator
                    : await ReadPointerAsync(pair, friendPublicKey, day, cancellationToken).ConfigureAwait(false);
                if (locator is not { } main) continue;

                var records = await _node.Dht.FindValueAsync(main, cancellationToken).ConfigureAwait(false);
                if (records.Count == 0 || records[0].Kind != MeshRecordKind.Signed)
                {
                    _mainLocators.TryRemove(friend.PublicKey, out _);
                    continue;
                }

                _mainLocators[friend.PublicKey] = (day, main);
                return new PresenceNetworkRead(Encoding.UTF8.GetString(records[0].Payload), (long)records[0].Sequence);
            }
            return new PresenceNetworkRead(null, 0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pair);
        }
    }

    private async Task<NodeId?> ReadPointerAsync(byte[] pair, byte[] owner, long day, CancellationToken cancellationToken)
    {
        NodeId locator;
        using (var signer = PointerKey(pair, owner, day)) locator = MeshRecord.LocatorOf(MeshCrypto.PublicKeyOf(signer));
        var records = await _node.Dht.FindValueAsync(locator, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0) return null;
        var opened = MeshCrypto.Open(PointerSealKey(pair), records[0].Payload, locator.ToBytes());
        return opened is { Length: NodeId.Length } ? new NodeId(opened) : null;
    }

    // ------------------------------------------------------------------ direct sessions

    /// <summary>Tries the friends due now, without waiting for the loop. For tests.</summary>
    internal Task ConnectNowAsync(CancellationToken cancellationToken = default) => ConnectFriendsAsync(cancellationToken);

    private async Task ConnectFriendsAsync(CancellationToken cancellationToken)
    {
        if (Invisible) return;
        var now = Environment.TickCount64;
        var due = _friends.Load()
            .Where(friend => !friend.Paused && !friend.Blocked && !IsConnected(friend.PublicKey))
            .Where(friend => friend.LastSeenAt is { } seen && _clock() - seen < TimeSpan.FromHours(1))
            .Where(friend => !_attempts.TryGetValue(friend.PublicKey, out var attempt) || attempt.NextAttempt <= now)
            .Select(friend => (Friend: friend, Address: MeshPeerAddress.FromBase64(friend.MeshAddress)))
            .Where(entry => entry.Address is { IsEmpty: false })
            .Take(8)
            .ToArray();

        foreach (var batch in due.Chunk(4))
        {
            await Task.WhenAll(batch.Select(async entry =>
            {
                var session = await _node.ConnectAsync(entry.Address!, cancellationToken).ConfigureAwait(false);
                var proven = session is not null && await HelloAsync(session, entry.Friend, cancellationToken).ConfigureAwait(false);
                if (proven)
                {
                    _attempts.TryRemove(entry.Friend.PublicKey, out _);
                    return;
                }
                var failures = _attempts.TryGetValue(entry.Friend.PublicKey, out var previous) ? previous.Failures + 1 : 1;
                var wait = TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, failures)));
                _attempts[entry.Friend.PublicKey] = (Environment.TickCount64 + (long)wait.TotalMilliseconds, failures);
            })).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Proves to whoever is on <paramref name="session"/> that we are us, and has them prove they
    /// are <paramref name="friend"/>. We speak first, but our proof only means something to the
    /// one person who can compute the pair key -- so a stranger at that address learns nothing it
    /// can use, and gets no answer it can check.
    /// </summary>
    public async Task<bool> HelloAsync(MeshSession session, Friend friend, CancellationToken cancellationToken = default)
    {
        var friendKey = Convert.FromBase64String(friend.PublicKey);
        var pair = _identity.DeriveSharedKey(friendKey);
        _helloPending[session.LocalIndex] = 0;
        try
        {
            var body = new MeshWriter(128).Fixed(_identity.PublicKey).Fixed(Proof(pair, session.IsInitiator, session.HandshakeHash)).ToArray();
            var reply = await _node.Transport.RequestAsync(session, MeshOps.FriendHello, body, cancellationToken).ConfigureAwait(false);
            if (reply is not { Length: PeerIdentity.PublicKeyLength + 32 }) return false;
            if (!reply.AsSpan(0, PeerIdentity.PublicKeyLength).SequenceEqual(friendKey)) return false;
            var expected = Proof(pair, !session.IsInitiator, session.HandshakeHash);
            if (!CryptographicOperations.FixedTimeEquals(expected, reply.AsSpan(PeerIdentity.PublicKeyLength))) return false;

            Adopt(session, friend.PublicKey);
            return true;
        }
        finally
        {
            _helloPending.TryRemove(session.LocalIndex, out _);
            _early.Remove(session);
            CryptographicOperations.ZeroMemory(pair);
        }
    }

    private Task<byte[]?> HandleRequestAsync(MeshSession session, MeshMessage message, CancellationToken cancellationToken)
    {
        if (message.Op != MeshOps.FriendHello || Invisible || message.Body.Length != PeerIdentity.PublicKeyLength + 32)
            return Task.FromResult<byte[]?>(null);

        var theirKey = message.Body.AsSpan(0, PeerIdentity.PublicKeyLength).ToArray();
        var encoded = Convert.ToBase64String(theirKey);
        var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == encoded);
        // Not a friend, paused, blocked: no answer at all, which says nothing about who is here.
        if (friend is null || friend.Paused || friend.Blocked) return Task.FromResult<byte[]?>(null);

        byte[] pair;
        try
        {
            pair = _identity.DeriveSharedKey(theirKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return Task.FromResult<byte[]?>(null);
        }

        try
        {
            // Their role is the opposite of ours on this session.
            var expected = Proof(pair, !session.IsInitiator, session.HandshakeHash);
            if (!CryptographicOperations.FixedTimeEquals(expected, message.Body.AsSpan(PeerIdentity.PublicKeyLength)))
                return Task.FromResult<byte[]?>(null);

            Adopt(session, encoded);
            var reply = new MeshWriter(128).Fixed(_identity.PublicKey).Fixed(Proof(pair, session.IsInitiator, session.HandshakeHash)).ToArray();
            return Task.FromResult<byte[]?>(reply);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pair);
        }
    }

    private static byte[] Proof(byte[] pairKey, bool initiator, byte[] handshakeHash) =>
        HMACSHA256.HashData(pairKey, MeshCrypto.Hash("cubeshelf-mesh-friend-v1", new[] { initiator ? (byte)'I' : (byte)'R' }, handshakeHash));

    private void Adopt(MeshSession session, string friendKey)
    {
        session.FriendKey = friendKey;
        session.Pinned = true;
        if (_sessions.TryGetValue(friendKey, out var previous) && !ReferenceEquals(previous, session) && !previous.Closed)
        {
            // Two paths to the same friend (both dialled at once): keep the direct one.
            if (previous.Route.IsDirect && !session.Route.IsDirect) return;
            previous.Pinned = false;
        }
        _sessions[friendKey] = session;
        Changed?.Invoke();

        if (_early.TryGetValue(session, out var early))
        {
            _early.Remove(session);
            if (AcceptDocument(friendKey, Encoding.UTF8.GetString(early)) is { } outcome) DocumentReceived?.Invoke(outcome);
        }

        // They get our latest at once, without waiting for the next change.
        if (_lastEnvelope is { } envelope) _node.Transport.Notify(session, MeshOps.FriendDocument, Encoding.UTF8.GetBytes(envelope));
    }

    private void OnNotification(MeshSession session, MeshMessage message)
    {
        if (message.Op != MeshOps.FriendDocument || message.Body.Length > PresencePolicy.MaximumDocumentBytes) return;
        if (session.FriendKey is not { } friendKey)
        {
            // Not proven yet: kept for when it is, never read before -- and only on a session we
            // are proving ourselves on, so a stranger cannot make us hold documents for nothing.
            if (_helloPending.ContainsKey(session.LocalIndex)) _early.AddOrUpdate(session, message.Body);
            return;
        }
        var outcome = AcceptDocument(friendKey, Encoding.UTF8.GetString(message.Body));
        if (outcome is not null) DocumentReceived?.Invoke(outcome);
    }

    /// <summary>The same checks as any other way a document arrives: it opens under the pair key, and it is newer.</summary>
    private PresenceFetchOutcome? AcceptDocument(string friendKey, string envelopeJson)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(friendKey);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!SealedPresence.TryOpen(_identity, raw, SealedPresence.FromJson(envelopeJson), out var snapshot) || snapshot is null) return null;
        if (!_friends.TryAcceptSequence(friendKey, snapshot.Sequence, _clock())) return null;
        _friends.Update(friendKey, entry => entry.SharesWithUs = true);
        _friends.AdoptAddress(friendKey, snapshot.Address);
        _friends.AdoptMeshAddress(friendKey, snapshot.Mesh);
        return new PresenceFetchOutcome(friendKey, PresenceFetchStatus.Updated, snapshot);
    }

    private void OnSessionClosed(MeshSession session)
    {
        if (session.FriendKey is not { } key) return;
        if (_sessions.TryGetValue(key, out var current) && ReferenceEquals(current, session))
        {
            _sessions.TryRemove(key, out _);
            Changed?.Invoke();
        }
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// Leaves a friend request in <paramref name="recipient"/>'s mailbox: our code, a note, and a
    /// proof that the request comes from whoever holds our key -- so nobody can drop a request
    /// in someone else's name. Encrypted for the recipient; the holders see only that an entry exists.
    /// </summary>
    public async Task<bool> SendRequestAsync(byte[] recipient, string? note = null, CancellationToken cancellationToken = default)
    {
        PeerIdentity.ValidatePublicKey(recipient);
        var now = _clock();
        var mailbox = MailboxOf(recipient, DayOf(now));

        using var ephemeral = PeerIdentity.Create();
        var sealKey = ephemeral.DeriveSharedKey(recipient);
        var pair = _identity.DeriveSharedKey(recipient);
        try
        {
            var noteBytes = Encoding.UTF8.GetBytes(ChatText.Clean(note ?? ""));
            if (noteBytes.Length > MaximumNoteBytes) noteBytes = noteBytes[..MaximumNoteBytes];
            var plaintext = new MeshWriter(512)
                .U8(1)
                .Fixed(_identity.PublicKey)
                .Fixed(HMACSHA256.HashData(pair, MeshCrypto.Hash("cubeshelf-mesh-request-v1", ephemeral.PublicKey)))
                .Text(_ownCode())
                .Blob(noteBytes)
                .ToArray();
            var sealedRequest = MeshCrypto.Seal(sealKey, plaintext, mailbox.ToBytes());
            var payload = new byte[PeerIdentity.PublicKeyLength + sealedRequest.Length];
            ephemeral.PublicKey.CopyTo(payload, 0);
            sealedRequest.CopyTo(payload, PeerIdentity.PublicKeyLength);

            using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var entry = await Task.Run(() => MeshRecord.CreateMailboxEntry(signer, mailbox, now + RecordLifetime, payload, _node.Records.MailboxDifficulty), cancellationToken)
                .ConfigureAwait(false);
            return await _node.Dht.StoreAsync(entry, cancellationToken).ConfigureAwait(false) > 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sealKey);
            CryptographicOperations.ZeroMemory(pair);
        }
    }

    /// <summary>Reads our mailbox, today's and yesterday's. Requests from friends, the blocked and the dismissed are dropped.</summary>
    public async Task CheckMailboxAsync(CancellationToken cancellationToken = default)
    {
        if (_node.Table.Count == 0 && !_node.Dht.Serving) return;
        var today = DayOf(_clock());
        var changed = false;
        foreach (var day in new[] { today, today - 1 })
        {
            var mailbox = MailboxOf(_identity.PublicKey, day);
            foreach (var entry in await _node.Dht.FindValueAsync(mailbox, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Kind != MeshRecordKind.Mailbox || OpenRequest(entry, mailbox) is not { } request) continue;
                var key = request.PublicKey;
                if (_dismissed.ContainsKey(key) || _requests.ContainsKey(key)) continue;
                var existing = _friends.Load().FirstOrDefault(friend => friend.PublicKey == key);
                if (existing is not null) continue;
                _requests[key] = request;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private MeshFriendRequest? OpenRequest(MeshRecord entry, NodeId mailbox)
    {
        if (entry.Payload.Length <= PeerIdentity.PublicKeyLength) return null;
        var ephemeralKey = entry.Payload.AsSpan(0, PeerIdentity.PublicKeyLength).ToArray();
        byte[] sealKey;
        try
        {
            sealKey = _identity.DeriveSharedKey(ephemeralKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return null;
        }

        try
        {
            var plaintext = MeshCrypto.Open(sealKey, entry.Payload.AsSpan(PeerIdentity.PublicKeyLength), mailbox.ToBytes());
            if (plaintext is null) return null;
            var reader = new MeshReader(plaintext);
            if (reader.U8() != 1) return null;
            var sender = reader.Fixed(PeerIdentity.PublicKeyLength).ToArray();
            var proof = reader.Fixed(RequestProofLength).ToArray();
            var code = reader.Text(4096);
            var note = Encoding.UTF8.GetString(reader.Blob(MaximumNoteBytes));
            if (!reader.Done) return null;

            if (sender.AsSpan().SequenceEqual(_identity.PublicKey)) return null;
            if (!FriendCode.TryDecode(code, out var payload, out _) || payload is null || !payload.PublicKey.AsSpan().SequenceEqual(sender)) return null;

            // Only the holder of the sender's key can compute this: a request cannot be made in someone else's name.
            byte[] pair;
            try
            {
                pair = _identity.DeriveSharedKey(sender);
            }
            catch (Exception exception) when (exception is ArgumentException or CryptographicException)
            {
                return null;
            }
            var expected = HMACSHA256.HashData(pair, MeshCrypto.Hash("cubeshelf-mesh-request-v1", ephemeralKey));
            CryptographicOperations.ZeroMemory(pair);
            if (!CryptographicOperations.FixedTimeEquals(expected, proof)) return null;

            return new MeshFriendRequest(payload, ChatText.Clean(note), _clock());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sealKey);
        }
    }

    /// <summary>Forgets a request, accepted or declined, and does not show it again this run.</summary>
    public void Dismiss(MeshFriendRequest request)
    {
        _requests.TryRemove(request.PublicKey, out _);
        _dismissed[request.PublicKey] = true;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _node.Transport.SessionClosed -= OnSessionClosed;
        _node.ApplicationNotification -= OnNotification;
        _node.Changed -= Kick;
        _lifetime?.Dispose();
        _pointerGate.Dispose();
        _wake.Dispose();
        CryptographicOperations.ZeroMemory(_seed);
    }
}

/// <summary>The network as a place to publish presence, for <see cref="PresenceService"/>.</summary>
public sealed class MeshPresencePublisher : IPresencePublisher
{
    private readonly MeshFriends _friends;

    public MeshPresencePublisher(MeshFriends friends) => _friends = friends ?? throw new ArgumentNullException(nameof(friends));

    public bool IsConfigured => true;

    public string PresenceUrl => "";

    public Task<PresencePublishResult> PublishAsync(string envelopeJson, CancellationToken cancellationToken = default) =>
        PublishAsync(envelopeJson, 0, cancellationToken);

    public Task<PresencePublishResult> PublishAsync(string envelopeJson, long sequence, CancellationToken cancellationToken = default) =>
        _friends.PublishAsync(envelopeJson, sequence, cancellationToken);

    public void Dispose()
    {
    }
}
