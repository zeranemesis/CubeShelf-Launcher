using System.Net;
using System.Runtime.CompilerServices;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>The operation codes of the network, every layer's in one place so none collide.</summary>
public static class MeshOps
{
    public const byte Ping = 0x01;
    public const byte FindNode = 0x02;
    public const byte FindValue = 0x03;
    public const byte Store = 0x04;

    public const byte DialBack = 0x05;

    public const byte Reserve = 0x06;
    public const byte Connect = 0x07;
    public const byte RelayData = 0x08;
    public const byte Incoming = 0x09;

    /// <summary>A node's flags and addresses changed: said on every open session, so long-lived ones do not keep stale claims.</summary>
    public const byte Announce = 0x0A;

    public const byte FriendHello = 0x10;
    public const byte FriendDocument = 0x11;
}

public sealed record MeshDhtOptions
{
    /// <summary>Bucket size, and how many closest nodes a lookup converges on.</summary>
    public int K { get; init; } = 16;

    /// <summary>Requests in flight during a lookup.</summary>
    public int Alpha { get; init; } = 3;

    /// <summary>How many nodes hold each record.</summary>
    public int Replication { get; init; } = 8;

    public int ProofDifficulty { get; init; } = NodeProof.Difficulty;
    public TimeSpan LookupTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Stores a single session may make per minute: a node publishing for itself needs a few dozen.</summary>
    public double StoresPerMinute { get; init; } = 120;
}

/// <summary>
/// The distributed table: Kademlia lookups over the encrypted sessions of <see cref="MeshTransport"/>.
///
/// Every node that can be reached from the Internet holds a share of the records, the ones whose
/// locators fall near its id, and answers lookups for them. A lookup walks towards a position by
/// asking the closest nodes it knows for closer ones, so it takes a number of steps that grows with
/// the logarithm of the network's size -- and every node it meets has proven its id in a handshake
/// before its answer counts.
/// </summary>
public sealed class MeshDht
{
    private readonly MeshTransport _transport;
    private readonly MeshDhtOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConditionalWeakTable<MeshSession, StoreBudget> _budgets = new();
    private readonly HashSet<NodeId> _verifying = new();

    /// <summary>Addresses that did not answer lately: not worth another handshake timeout for a while.</summary>
    private readonly Dictionary<IPEndPoint, long> _unreachable = new();

    /// <summary>
    /// How the contacts each node handed out turned out. A node whose referrals keep leading
    /// nowhere is lying, or hopelessly out of date; either way it stops being asked.
    /// </summary>
    private readonly Dictionary<NodeId, (int Good, int Bad)> _referrals = new();
    private readonly Dictionary<NodeId, long> _distrusted = new();

    /// <summary>
    /// Stores across all sessions: each one costs a signature check, and a thousand sessions at
    /// their own allowance each would be a thousand checks a second.
    /// </summary>
    private readonly RateBucket _allStores = new(200);

    public MeshDht(MeshTransport transport, RoutingTable table, RecordStore records, MeshDhtOptions? options = null, Func<DateTimeOffset>? clock = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Records = records ?? throw new ArgumentNullException(nameof(records));
        _options = options ?? new MeshDhtOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public RoutingTable Table { get; }
    public RecordStore Records { get; }

    /// <summary>Whether this node serves its share of the table; only reachable nodes do.</summary>
    public bool Serving { get; set; }

    public NodeId LocalId => _transport.LocalId;

    // ------------------------------------------------------------------ answering

    /// <summary>Answers a request if it is one of the table's; null otherwise, so another layer can.</summary>
    public Task<byte[]?> HandleAsync(MeshSession session, MeshMessage message)
    {
        switch (message.Op)
        {
            case MeshOps.Ping:
                return Task.FromResult<byte[]?>(Array.Empty<byte>());

            case MeshOps.FindNode:
            {
                var reader = new MeshReader(message.Body);
                var target = reader.Id();
                if (!reader.Done) return Task.FromResult<byte[]?>(null);
                return Task.FromResult<byte[]?>(Contacts(target, session.RemoteId));
            }

            case MeshOps.FindValue:
            {
                var reader = new MeshReader(message.Body);
                var locator = reader.Id();
                if (!reader.Done) return Task.FromResult<byte[]?>(null);
                var records = Records.Get(locator, _clock());
                if (records.Count == 0)
                {
                    var contacts = Contacts(locator, session.RemoteId);
                    var withFlag = new byte[contacts.Length + 1];
                    contacts.CopyTo(withFlag, 1);
                    return Task.FromResult<byte[]?>(withFlag);
                }

                var writer = new MeshWriter(records.Sum(record => record.Size) + 16).U8(1).U8((byte)Math.Min(records.Count, 255));
                foreach (var record in records.Take(255)) record.WriteTo(writer);
                return Task.FromResult<byte[]?>(writer.ToArray());
            }

            case MeshOps.Store:
            {
                var reader = new MeshReader(message.Body);
                var record = MeshRecord.Read(ref reader);
                if (record is null || !reader.Done) return Task.FromResult<byte[]?>(new[] { (byte)StoreResult.Invalid });
                if (!Serving || !IsResponsibleFor(record.Locator)) return Task.FromResult<byte[]?>(new[] { (byte)StoreResult.NotResponsible });
                if (!_budgets.GetOrCreateValue(session).TryTake(_options.StoresPerMinute))
                    return Task.FromResult<byte[]?>(new[] { (byte)StoreResult.Full });
                lock (_allStores)
                    if (!_allStores.TryTake(Environment.TickCount64, 100, 200))
                        return Task.FromResult<byte[]?>(new[] { (byte)StoreResult.Full });
                var result = Records.Put(record, SourceOf(session), _clock());
                return Task.FromResult<byte[]?>(new[] { (byte)result });
            }

            default:
                return Task.FromResult<byte[]?>(null);
        }
    }

    /// <summary>
    /// Whether we would be among the K closest nodes to a locator, as far as we know. Refusing the
    /// rest keeps anyone from using our disk as free storage for records that belong elsewhere.
    /// </summary>
    public bool IsResponsibleFor(NodeId locator)
    {
        var closest = Table.Closest(locator, _options.K);
        return closest.Count < _options.K || LocalId.Xor(locator).CompareTo(closest[^1].Id.Xor(locator)) < 0;
    }

    private byte[] Contacts(NodeId target, NodeId requester)
    {
        var contacts = Table.Closest(target, _options.K + 1).Where(contact => contact.Id != requester).Take(_options.K).ToArray();
        var writer = new MeshWriter(contacts.Length * 96 + 1).U8((byte)contacts.Length);
        foreach (var contact in contacts) contact.WriteTo(writer);
        return writer.ToArray();
    }

    private static string SourceOf(MeshSession session) =>
        session.Route.IsDirect ? MeshAddresses.Neighbourhood(session.Route.Endpoint!.Address) : "relayed:" + session.RemoteId;

    /// <summary>
    /// A session just opened. A reachable node we dialled ourselves has proven its address and goes
    /// in the table at once. One that dialled us is checked first: what it says is its listening
    /// address is only a claim until we reach it there.
    /// </summary>
    public void OnSessionEstablished(MeshSession session)
    {
        if (!session.RemoteFlags.HasFlag(MeshNodeFlags.Server) || !session.Route.IsDirect) return;
        var source = session.Route.Endpoint!;
        if (session.IsInitiator)
        {
            Table.Observe(NodeContact.Of(session, source));
            return;
        }

        var claimed = session.RemoteAdvertised.FirstOrDefault(endpoint => endpoint.Address.Equals(source.Address)) ?? source;
        if (claimed.Equals(source))
        {
            Table.Observe(NodeContact.Of(session, source));
            return;
        }

        lock (_verifying)
        {
            if (_verifying.Count >= 8 || !_verifying.Add(session.RemoteId)) return;
        }
        _ = VerifyAsync(session.RemoteId, claimed);
    }

    private async Task VerifyAsync(NodeId id, IPEndPoint endpoint)
    {
        try
        {
            var session = await _transport.ConnectAsync(MeshRoute.Direct(endpoint)).ConfigureAwait(false);
            if (session is not null && session.RemoteId == id) Table.Observe(NodeContact.Of(session, endpoint));
        }
        finally
        {
            lock (_verifying) _verifying.Remove(id);
        }
    }

    // ------------------------------------------------------------------ lookups

    /// <summary>The closest nodes to <paramref name="target"/> that answered, closest first.</summary>
    public async Task<IReadOnlyList<NodeContact>> FindClosestAsync(NodeId target, CancellationToken cancellationToken = default) =>
        (await LookupAsync(target, wantValue: false, cancellationToken).ConfigureAwait(false)).Closest;

    /// <summary>
    /// The records at a locator. For a signed record, the one with the highest sequence any holder
    /// had -- a holder serving an old copy is outvoted by the others, it cannot win by answering
    /// first. For a mailbox, every entry, newest per writer.
    /// </summary>
    public async Task<IReadOnlyList<MeshRecord>> FindValueAsync(NodeId locator, CancellationToken cancellationToken = default)
    {
        var found = new List<MeshRecord>(Records.Get(locator, _clock()));
        found.AddRange((await LookupAsync(locator, wantValue: true, cancellationToken).ConfigureAwait(false)).Records);
        if (found.Count == 0) return found;

        if (found[0].Kind == MeshRecordKind.Signed)
            return new[] { found.Where(record => record.Kind == MeshRecordKind.Signed).MaxBy(record => record.Sequence)! };

        return found
            .Where(record => record.Kind == MeshRecordKind.Mailbox)
            .GroupBy(record => Convert.ToBase64String(record.SignerKey))
            .Select(group => group.MaxBy(record => record.ExpiresAt)!)
            .ToArray();
    }

    /// <summary>Stores a record on the nodes closest to its locator. Returns how many accepted it.</summary>
    public async Task<int> StoreAsync(MeshRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var closest = await FindClosestAsync(record.Locator, cancellationToken).ConfigureAwait(false);
        var accepted = 0;

        // We are never in our own lookup's answer, so we decide for ourselves.
        if (Serving && IsResponsibleFor(record.Locator) &&
            Records.Put(record, "self", _clock()) is StoreResult.Stored or StoreResult.Unchanged)
            accepted++;

        var writer = new MeshWriter(record.Size + 16);
        record.WriteTo(writer);
        var body = writer.ToArray();
        var results = await Task.WhenAll(closest.Take(_options.Replication).Select(async contact =>
        {
            var session = await _transport.ConnectAsync(MeshRoute.Direct(contact.Endpoint), cancellationToken).ConfigureAwait(false);
            if (session is null || session.RemoteId != contact.Id) return false;
            var reply = await _transport.RequestAsync(session, MeshOps.Store, body, cancellationToken, _options.RequestTimeout * 2).ConfigureAwait(false);
            return reply is [var status] && (StoreResult)status is StoreResult.Stored or StoreResult.Unchanged;
        })).ConfigureAwait(false);
        return accepted + results.Count(ok => ok);
    }

    /// <summary>
    /// Joins the network through known addresses -- a friend code, the local network, last
    /// session's nodes -- then looks ourselves up, which is what fills the table near our own id.
    /// Returns how many nodes the table knows afterwards.
    /// </summary>
    public async Task<int> BootstrapAsync(IEnumerable<IPEndPoint> seeds, CancellationToken cancellationToken = default)
    {
        var attempts = seeds.Distinct().Take(32).Select(seed => _transport.ConnectAsync(MeshRoute.Direct(seed), cancellationToken));
        var sessions = await Task.WhenAll(attempts).ConfigureAwait(false);
        foreach (var session in sessions.OfType<MeshSession>())
            if (session.RemoteFlags.HasFlag(MeshNodeFlags.Server) && session.Route.IsDirect)
                Table.Observe(NodeContact.Of(session, session.Route.Endpoint!));

        if (Table.Count == 0) return 0;
        await FindClosestAsync(LocalId, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        return Table.Count;
    }

    /// <summary>Looks up a random id in each bucket nobody looked into lately, keeping the whole table fresh.</summary>
    public async Task RefreshAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        foreach (var bucket in Table.BucketsToRefresh(olderThan))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Table.MarkRefreshed(bucket);
            await FindClosestAsync(LocalId.RandomWithPrefix(bucket), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Pings the least recently heard node of each bucket; those that do not answer make room.</summary>
    public async Task CheckStaleAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(Table.Stalest(olderThan).Select(async contact =>
        {
            var session = await _transport.ConnectAsync(MeshRoute.Direct(contact.Endpoint), cancellationToken).ConfigureAwait(false);
            var alive = session is not null && session.RemoteId == contact.Id &&
                        await _transport.RequestAsync(session, MeshOps.Ping, Array.Empty<byte>(), cancellationToken, _options.RequestTimeout).ConfigureAwait(false) is not null;
            if (alive) Table.Observe(contact);
            else Table.Failed(contact.Id);
        })).ConfigureAwait(false);
    }

    /// <summary>
    /// Passes on records whose owner has gone quiet, so they outlive the holders that came and went
    /// since -- the part of "the bigger the network, the steadier" that happens without the owner.
    /// </summary>
    public async Task<int> RepublishAsync(TimeSpan quietFor, CancellationToken cancellationToken = default)
    {
        var now = _clock();
        var count = 0;
        foreach (var record in Records.NotRefreshedSince(quietFor, now).Where(record => record.ExpiresAt - now.ToUnixTimeSeconds() > 3600).Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await StoreAsync(record, cancellationToken).ConfigureAwait(false) > 0) count++;
        }
        return count;
    }

    private sealed record LookupResult(IReadOnlyList<NodeContact> Closest, IReadOnlyList<MeshRecord> Records);

    private sealed class Candidate
    {
        public Candidate(NodeContact contact, NodeId? referredBy)
        {
            Contact = contact;
            ReferredBy = referredBy;
        }

        public NodeContact Contact { get; }
        public NodeId? ReferredBy { get; }
        public int State { get; set; }   // 0 waiting, 1 asked, 2 answered, 3 failed
    }

    private bool IsUnreachable(IPEndPoint endpoint)
    {
        lock (_unreachable)
        {
            if (!_unreachable.TryGetValue(endpoint, out var until)) return false;
            if (until > Environment.TickCount64) return true;
            _unreachable.Remove(endpoint);
            return false;
        }
    }

    private void MarkUnreachable(IPEndPoint endpoint)
    {
        lock (_unreachable)
        {
            if (_unreachable.Count > 2048) _unreachable.Clear();
            _unreachable[endpoint] = Environment.TickCount64 + 10 * 60_000;
        }
    }

    private bool IsDistrusted(NodeId id)
    {
        lock (_referrals)
        {
            if (!_distrusted.TryGetValue(id, out var until)) return false;
            if (until > Environment.TickCount64) return true;
            _distrusted.Remove(id);
            _referrals.Remove(id);
            return false;
        }
    }

    private void Referral(NodeId? referrer, bool good)
    {
        if (referrer is not { } id) return;
        lock (_referrals)
        {
            var (goodCount, badCount) = _referrals.GetValueOrDefault(id);
            if (good) goodCount++;
            else badCount++;
            _referrals[id] = (goodCount, badCount);
            if (_referrals.Count > 4096) _referrals.Clear();

            // Churn makes honest referrals go stale too, so it takes many failures and few
            // successes before a node is set aside -- and then only for an hour.
            if (badCount >= 12 && goodCount * 4 < badCount)
            {
                _distrusted[id] = Environment.TickCount64 + 3600_000;
                Table.Remove(id);
            }
        }
    }

    private async Task<LookupResult> LookupAsync(NodeId target, bool wantValue, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.LookupTimeout);
        var token = deadline.Token;

        var candidates = new Dictionary<NodeId, Candidate>();
        foreach (var contact in Table.Closest(target, _options.K)) candidates[contact.Id] = new Candidate(contact, null);
        var records = new List<MeshRecord>();
        var inFlight = new Dictionary<Task<QueryResult>, Candidate>();
        var comparer = NodeId.ByDistanceTo(target);

        List<Candidate> Window() => candidates.Values
            .Where(candidate => candidate.State != 3)
            .OrderBy(candidate => candidate.Contact.Id, comparer)
            .Take(_options.K)
            .ToList();

        try
        {
            while (true)
            {
                var window = Window();
                if (window.Count > 0 && window.All(candidate => candidate.State == 2)) break;

                foreach (var candidate in window.Where(candidate => candidate.State == 0))
                {
                    if (inFlight.Count >= _options.Alpha) break;
                    if (IsUnreachable(candidate.Contact.Endpoint) || IsDistrusted(candidate.Contact.Id))
                    {
                        candidate.State = 3;
                        continue;
                    }
                    candidate.State = 1;
                    inFlight[QueryAsync(candidate.Contact, target, wantValue, token)] = candidate;
                }
                if (inFlight.Count == 0) break;

                var done = await Task.WhenAny(inFlight.Keys).ConfigureAwait(false);
                var asked = inFlight[done];
                inFlight.Remove(done);
                var result = await done.ConfigureAwait(false);

                if (result.Failed)
                {
                    asked.State = 3;
                    Table.Failed(asked.Contact.Id);
                    MarkUnreachable(asked.Contact.Endpoint);
                    Referral(asked.ReferredBy, good: false);
                    continue;
                }

                asked.State = 2;
                Referral(asked.ReferredBy, good: true);
                records.AddRange(result.Records);
                if (IsDistrusted(asked.Contact.Id)) continue;
                foreach (var contact in result.Contacts)
                    if (contact.Id != LocalId && !candidates.ContainsKey(contact.Id))
                        candidates[contact.Id] = new Candidate(contact, asked.Contact.Id);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Out of time: what has answered so far is the answer.
        }

        var closest = candidates.Values.Where(candidate => candidate.State == 2)
            .OrderBy(candidate => candidate.Contact.Id, comparer)
            .Take(_options.K)
            .Select(candidate => candidate.Contact)
            .ToArray();
        return new LookupResult(closest, records);
    }

    private sealed record QueryResult(bool Failed, IReadOnlyList<NodeContact> Contacts, IReadOnlyList<MeshRecord> Records)
    {
        public static readonly QueryResult Failure = new(true, Array.Empty<NodeContact>(), Array.Empty<MeshRecord>());
    }

    private async Task<QueryResult> QueryAsync(NodeContact contact, NodeId target, bool wantValue, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _transport.ConnectAsync(MeshRoute.Direct(contact.Endpoint), cancellationToken).ConfigureAwait(false);
            // Whoever answers at that address has to be the node we were told about.
            if (session is null || session.RemoteId != contact.Id) return QueryResult.Failure;
            Table.Observe(contact);

            var reply = await _transport.RequestAsync(
                session, wantValue ? MeshOps.FindValue : MeshOps.FindNode, target.ToBytes(), cancellationToken, _options.RequestTimeout).ConfigureAwait(false);
            return reply is null ? QueryResult.Failure : ParseReply(reply, target, wantValue);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QueryResult.Failure;
        }
    }

    private QueryResult ParseReply(byte[] reply, NodeId target, bool wantValue)
    {
        var reader = new MeshReader(reply);
        if (wantValue && reader.U8() == 1)
        {
            // Each record costs a signature check: no answer gets to make us do more than a
            // full mailbox's worth, however many it claims to hold.
            int count = Math.Min((int)reader.U8(), Records.MailboxEntries);
            var records = new List<MeshRecord>();
            var now = _clock();
            for (var index = 0; index < count && reader.Ok; index++)
            {
                var record = MeshRecord.Read(ref reader);
                // Only what is genuinely at this locator and verifies counts; a holder cannot
                // slip in a record for somewhere else.
                if (record is not null && record.Locator == target && record.IsValid(now, Records.MailboxDifficulty)) records.Add(record);
            }
            return new QueryResult(false, Array.Empty<NodeContact>(), records);
        }

        return new QueryResult(false, ReadContacts(ref reader), Array.Empty<MeshRecord>());
    }

    private IReadOnlyList<NodeContact> ReadContacts(ref MeshReader reader)
    {
        int count = reader.U8();
        if (count > _options.K * 2) return Array.Empty<NodeContact>();
        var contacts = new List<NodeContact>(count);
        var neighbourhoods = new Dictionary<string, int>();
        for (var index = 0; index < count; index++)
        {
            var contact = NodeContact.Read(ref reader, _options.ProofDifficulty);
            if (contact is null) break;
            // A contact someone told us about is only a candidate to try, never an entry of the
            // table, and never an address outside the public Internet: an answer must not be able
            // to aim our handshakes at our own local network.
            if (contact.Id == LocalId || !Table.IsRoutable(contact.Endpoint.Address)) continue;

            // One answer cannot steer a lookup into a single neighbourhood of addresses.
            var neighbourhood = MeshAddresses.Neighbourhood(contact.Endpoint.Address);
            var seen = neighbourhoods.GetValueOrDefault(neighbourhood);
            if (seen >= 2) continue;
            neighbourhoods[neighbourhood] = seen + 1;
            contacts.Add(contact);
        }
        return contacts;
    }

    private sealed class StoreBudget
    {
        private double _tokens = -1;
        private long _updatedAt = Environment.TickCount64;

        public bool TryTake(double perMinute)
        {
            lock (this)
            {
                var now = Environment.TickCount64;
                if (_tokens < 0) _tokens = perMinute;
                _tokens = Math.Min(perMinute, _tokens + (now - _updatedAt) / 60000.0 * perMinute);
                _updatedAt = now;
                if (_tokens < 1) return false;
                _tokens -= 1;
                return true;
            }
        }
    }
}
