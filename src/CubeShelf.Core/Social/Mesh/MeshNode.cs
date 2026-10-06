using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace CubeShelf.Core.Social.Mesh;

public enum MeshReachability
{
    /// <summary>Still finding out.</summary>
    Starting = 0,

    /// <summary>Reachable from the Internet: holds a share of the table and relays for others.</summary>
    Public = 1,

    /// <summary>Behind a router that lets nothing in, but reachable through relays.</summary>
    Relayed = 2,

    /// <summary>No other node answered: nothing to join yet.</summary>
    Isolated = 3
}

public sealed record MeshNodeOptions
{
    public MeshTransportOptions Transport { get; init; } = new();
    public MeshDhtOptions Dht { get; init; } = new();

    /// <summary>Ask the router to open the port (PCP, NAT-PMP, UPnP).</summary>
    public bool MapPort { get; init; } = true;

    public TimeSpan PortMappingLifetime { get; init; } = TimeSpan.FromHours(2);
    public int RelaysWanted { get; init; } = 2;
    public int MaximumReservations { get; init; } = 64;
    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a hole punch is given before falling back to the relay.</summary>
    public TimeSpan PunchTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Where the addresses of good nodes are kept between runs, so rejoining needs no code. Null keeps nothing.</summary>
    public string? NodesFile { get; init; }

    /// <summary>Lowered only by tests, which run on simulated addresses.</summary>
    public Func<IPAddress, bool> IsRoutable { get; init; } = MeshAddresses.IsPublic;

    /// <summary>For tests: skip looking at this machine's network interfaces for IPv6 addresses.</summary>
    public bool UseInterfaceAddresses { get; init; } = true;
}

/// <summary>
/// One CubeShelf on the network: the transport, its share of the table, and everything needed to
/// be reachable -- or to be reached anyway.
///
/// On start it asks the router to open its port, joins through whatever addresses it knows, and
/// then has other nodes dial it back from addresses it never talked to: only an answer that gets
/// through proves the Internet can reach it. A reachable node takes its share of records and
/// relays for others; one that is not keeps sessions with two relays and is reached through them.
///
/// The node id is new on every run: nothing ties this run's position in the network to the last
/// one's, or to the person -- who someone is travels only inside friends' encrypted sessions.
/// </summary>
public sealed class MeshNode : IAsyncDisposable
{
    private readonly IMeshSocket _socket;
    private readonly MeshNodeOptions _options;
    private readonly IPortMapper? _portMapper;
    private readonly Func<IMeshSocket>? _probeSockets;
    private readonly RelayService _relay;
    private readonly object _gate = new();
    private readonly List<HeldReservation> _held = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _probes = new(StringComparer.Ordinal);
    private readonly RateBucket _dialBacks = new(20);
    private readonly HashSet<IPEndPoint> _seeds = new();

    /// <summary>Circuits we opened as the caller, by relay session and circuit: their packets are ours to read.</summary>
    private readonly Dictionary<(uint Relay, uint Circuit), long> _callerCircuits = new();

    private volatile MeshNodeFlags _flags = MeshNodeFlags.None;
    private IReadOnlyList<IPEndPoint> _publicEndpoints = Array.Empty<IPEndPoint>();
    private PortMapping? _mapping;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private int _checking;
    private long _lastReachabilityCheck;
    private bool _disposed;

    private MeshNode(IMeshSocket socket, ECDiffieHellman key, ulong nonce, MeshNodeOptions options, IPortMapper? portMapper, Func<IMeshSocket>? probeSockets)
    {
        _socket = socket;
        _options = options;
        _portMapper = portMapper;
        _probeSockets = probeSockets;
        Transport = new MeshTransport(socket, key, nonce, () => _flags, () => _publicEndpoints, options.Transport);
        Table = new RoutingTable(Transport.LocalId, options.IsRoutable, options.Dht.K);
        Records = new RecordStore();
        Dht = new MeshDht(Transport, Table, Records, options.Dht);
        _relay = new RelayService(Transport, options.MaximumReservations);

        Transport.RequestHandler = HandleRequestAsync;
        Transport.NotificationReceived += OnNotification;
        Transport.SessionEstablished += OnSessionEstablished;
        Transport.SessionClosed += OnSessionClosed;
        Transport.ProbeReceived += OnProbe;
        Transport.RelaySender = (route, packet) =>
        {
            var body = new byte[4 + packet.Length];
            BinaryPrimitives.WriteUInt32BigEndian(body, route.Circuit);
            packet.CopyTo(body, 4);
            Transport.Notify(route.Relay!, MeshOps.RelayData, body);
        };
    }

    /// <summary>Makes a node: a fresh key, and its proof of work, found off the calling thread.</summary>
    public static async Task<MeshNode> CreateAsync(
        IMeshSocket socket,
        MeshNodeOptions? options = null,
        IPortMapper? portMapper = null,
        Func<IMeshSocket>? probeSockets = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        options ??= new MeshNodeOptions();
        var key = MeshCrypto.NewAgreementKey();
        var publicKey = MeshCrypto.PublicKeyOf(key);
        var difficulty = options.Transport.ProofDifficulty;
        var nonce = await Task.Run(() => NodeProof.Solve(publicKey, difficulty, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new MeshNode(socket, key, nonce, options, portMapper, probeSockets);
    }

    public MeshTransport Transport { get; }
    public RoutingTable Table { get; }
    public RecordStore Records { get; }
    public MeshDht Dht { get; }

    public MeshReachability Reachability { get; private set; } = MeshReachability.Starting;

    /// <summary>Addresses proven to reach us from the Internet.</summary>
    public IReadOnlyList<IPEndPoint> PublicEndpoints => _publicEndpoints;

    public PortMapping? Mapping => _mapping;

    /// <summary>The relays that currently hold a reservation for us.</summary>
    public IReadOnlyList<MeshRelayContact> RelayContacts
    {
        get
        {
            lock (_gate) return _held.Where(held => !held.Session.Closed).Select(held => new MeshRelayContact(held.Endpoint, held.Token)).ToArray();
        }
    }

    /// <summary>What friends need to reach us, right now.</summary>
    public MeshPeerAddress Address => new(_publicEndpoints, Reachability == MeshReachability.Public ? Array.Empty<MeshRelayContact>() : RelayContacts);

    /// <summary>A few good public nodes, for a friend code or the next run: ourselves first when reachable.</summary>
    public IReadOnlyList<IPEndPoint> EntryPoints(int count = 3)
    {
        var entries = new List<IPEndPoint>(_publicEndpoints);
        lock (_gate) entries.AddRange(_held.Where(held => !held.Session.Closed).Select(held => held.Endpoint));
        entries.AddRange(Table.All().OrderBy(_ => Random.Shared.Next()).Select(contact => contact.Endpoint));
        return entries.Distinct().Take(count).ToArray();
    }

    /// <summary>Something about reachability, relays or the table changed.</summary>
    public event Action? Changed;

    /// <summary>Requests for the layers above (friends). Null replies nothing.</summary>
    public Func<MeshSession, MeshMessage, CancellationToken, Task<byte[]?>>? ApplicationRequests { get; set; }

    public event Action<MeshSession, MeshMessage>? ApplicationNotification;

    public void Start(IEnumerable<IPEndPoint>? seeds = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        lock (_gate)
        {
            foreach (var seed in LoadNodes().Concat(seeds ?? Array.Empty<IPEndPoint>())) _seeds.Add(MeshAddresses.Normalize(seed));
        }
        Transport.Start();
        _loop = RunAsync(_lifetime.Token);
    }

    /// <summary>More ways in: a friend code just pasted, a friend's document. Joins at once if the table is thin.</summary>
    public void AddSeeds(IEnumerable<IPEndPoint> seeds)
    {
        var added = false;
        lock (_gate)
        {
            foreach (var seed in seeds.Where(seed => _options.IsRoutable(seed.Address)).Take(16))
                added |= _seeds.Add(MeshAddresses.Normalize(seed));
        }
        if (added && Table.Count < _options.Dht.K / 2 && _lifetime is { } lifetime)
            _ = JoinAsync(lifetime.Token);
    }

    /// <summary>The network changed under us (another Wi-Fi, a cable): find out again how we are reached.</summary>
    public void NetworkChanged()
    {
        _lastReachabilityCheck = 0;
        if (_lifetime is { } lifetime) _ = CheckReachabilityAsync(lifetime.Token);
    }

    // ------------------------------------------------------------------ reaching another node

    /// <summary>
    /// A session with whoever is at <paramref name="address"/>: straight to a listening address
    /// when there is one; otherwise through each relay in turn, trying first to punch a direct
    /// path through both routers and only then settling for the relayed one. The caller proves who
    /// answered (a friend's identity proof); this only finds a path.
    /// </summary>
    public async Task<MeshSession?> ConnectAsync(MeshPeerAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        foreach (var endpoint in address.Direct)
        {
            var session = await Transport.ConnectAsync(MeshRoute.Direct(endpoint), cancellationToken).ConfigureAwait(false);
            if (session is not null) return session;
        }

        foreach (var relay in address.Relays)
        {
            var session = await ConnectThroughRelayAsync(relay, cancellationToken).ConfigureAwait(false);
            if (session is not null) return session;
        }
        return null;
    }

    private async Task<MeshSession?> ConnectThroughRelayAsync(MeshRelayContact contact, CancellationToken cancellationToken)
    {
        var relay = await Transport.ConnectAsync(MeshRoute.Direct(contact.Relay), cancellationToken).ConfigureAwait(false);
        if (relay is null) return null;
        var reply = await Transport.RequestAsync(relay, MeshOps.Connect, contact.Token, cancellationToken).ConfigureAwait(false);
        if (reply is null || reply.Length < 5 || reply[0] != 0) return null;
        var (circuit, target) = ParseCircuit(reply);
        if (circuit == 0) return null;

        // Both sides send out towards the other at once; whichever handshake gets through first wins.
        lock (_gate) _callerCircuits[(relay.LocalIndex, circuit)] = Environment.TickCount64;
        if (target is not null && Transport.SessionAt(target) is { } existing) return existing;
        if (target is not null)
        {
            var arrived = new TaskCompletionSource<MeshSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Watch(MeshSession session)
            {
                if (session.Route.IsDirect && session.Route.Endpoint!.Equals(target)) arrived.TrySetResult(session);
            }
            Transport.SessionEstablished += Watch;
            try
            {
                Punch(target);
                var dialled = Transport.ConnectAsync(MeshRoute.Direct(target), cancellationToken);
                var deadline = Task.Delay(_options.PunchTimeout, cancellationToken);
                while (true)
                {
                    var first = await Task.WhenAny(arrived.Task, dialled, deadline).ConfigureAwait(false);
                    if (first == arrived.Task) return arrived.Task.Result;
                    if (first == dialled && dialled.Result is { } direct) return direct;
                    if (first == deadline) break;
                    if (first == dialled) dialled = new TaskCompletionSource<MeshSession?>().Task;   // failed: keep waiting for theirs
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                Transport.SessionEstablished -= Watch;
            }
        }

        // The routers would not let a direct path through: the relay carries the session.
        return await Transport.ConnectAsync(MeshRoute.Via(relay, circuit), cancellationToken).ConfigureAwait(false);
    }

    private static (uint Circuit, IPEndPoint? Endpoint) ParseCircuit(byte[] reply)
    {
        var reader = new MeshReader(reply);
        reader.U8();
        var circuit = reader.U32();
        var endpoint = reader.OptionalEndpoint();
        return reader.Done ? (circuit, endpoint) : (0, null);
    }

    /// <summary>A few empty datagrams towards a peer: our router then lets its answers in.</summary>
    private void Punch(IPEndPoint target)
    {
        var punch = new byte[10];
        punch[0] = MeshPackets.Magic;
        punch[1] = (byte)MeshPacketType.Punch;
        RandomNumberGenerator.Fill(punch.AsSpan(2));
        for (var i = 0; i < 3; i++) Transport.SendRaw(punch, target);
    }

    // ------------------------------------------------------------------ answering

    private async Task<byte[]?> HandleRequestAsync(MeshSession session, MeshMessage message, CancellationToken cancellationToken)
    {
        switch (message.Op)
        {
            case MeshOps.Ping or MeshOps.FindNode or MeshOps.FindValue or MeshOps.Store:
                return await Dht.HandleAsync(session, message).ConfigureAwait(false);
            case MeshOps.Reserve:
                var granted = _relay.HandleReserve(session, message.Body);
                if (granted is [0, ..]) Changed?.Invoke();
                return granted;
            case MeshOps.Connect:
                return _relay.HandleConnect(session, message.Body);
            case MeshOps.DialBack:
                return HandleDialBack(session, message.Body);
            default:
                return ApplicationRequests is { } application
                    ? await application(session, message, cancellationToken).ConfigureAwait(false)
                    : null;
        }
    }

    private void OnNotification(MeshSession session, MeshMessage message)
    {
        switch (message.Op)
        {
            case MeshOps.RelayData when message.Body.Length > 4:
            {
                var circuit = BinaryPrimitives.ReadUInt32BigEndian(message.Body);
                // A packet for us -- through a relay that holds our reservation, or on a circuit we
                // opened as the caller -- or, if we are the relay, one to pass along.
                if (IsHeldRelay(session) || IsCallerCircuit(session, circuit))
                    Transport.ProcessRelayed(message.Body.AsSpan(4), MeshRoute.Via(session, circuit));
                else if (_relay.Enabled)
                    _relay.OnRelayData(session, message.Body);
                break;
            }
            case MeshOps.Incoming when IsHeldRelay(session):
                OnIncoming(message.Body);
                break;
            default:
                ApplicationNotification?.Invoke(session, message);
                break;
        }
    }

    /// <summary>Someone is calling through one of our relays: send out towards them so their packets get in.</summary>
    private void OnIncoming(byte[] body)
    {
        var reader = new MeshReader(body);
        reader.U32();
        var caller = reader.OptionalEndpoint();
        if (!reader.Done || caller is null || !_options.IsRoutable(caller.Address)) return;
        Punch(caller);
        _ = Transport.ConnectAsync(MeshRoute.Direct(caller), _lifetime?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// DialBack: [nonce][port] — a probe from a fresh socket to the requester's own address at that
    /// port. Never to any other address: a dial-back cannot be pointed at a third party.
    /// </summary>
    private byte[]? HandleDialBack(MeshSession session, byte[] body)
    {
        if (body.Length != MeshPackets.ProbeNonceLength + 2 || !session.Route.IsDirect || _probeSockets is null) return null;
        lock (_dialBacks)
        {
            if (!_dialBacks.TryTake(Environment.TickCount64, 0.5, 20)) return new byte[] { 1 };
        }
        var port = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(MeshPackets.ProbeNonceLength));
        if (port == 0) return new byte[] { 1 };
        var target = new IPEndPoint(session.Route.Endpoint!.Address, port);

        var probe = new byte[2 + MeshPackets.ProbeNonceLength];
        probe[0] = MeshPackets.Magic;
        probe[1] = (byte)MeshPacketType.Probe;
        body.AsSpan(0, MeshPackets.ProbeNonceLength).CopyTo(probe.AsSpan(2));
        try
        {
            using var socket = _probeSockets();
            socket.Send(probe, target);
            socket.Send(probe, target);
        }
        catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException)
        {
            return new byte[] { 1 };
        }
        return new byte[] { 0 };
    }

    private void OnProbe(byte[] nonce, IPEndPoint from)
    {
        lock (_probes)
        {
            if (_probes.TryGetValue(Convert.ToBase64String(nonce), out var waiting)) waiting.TrySetResult(true);
        }
    }

    private void OnSessionEstablished(MeshSession session) => Dht.OnSessionEstablished(session);

    private void OnSessionClosed(MeshSession session)
    {
        _relay.OnSessionClosed(session);
        bool lost;
        lock (_gate)
        {
            lost = _held.RemoveAll(held => ReferenceEquals(held.Session, session)) > 0;
            foreach (var key in _callerCircuits.Keys.Where(key => key.Relay == session.LocalIndex).ToArray()) _callerCircuits.Remove(key);
        }
        if (lost)
        {
            Changed?.Invoke();
            if (_lifetime is { } lifetime) _ = EnsureRelaysAsync(lifetime.Token);
        }
    }

    private bool IsCallerCircuit(MeshSession relay, uint circuit)
    {
        lock (_gate)
        {
            if (!_callerCircuits.ContainsKey((relay.LocalIndex, circuit))) return false;
            _callerCircuits[(relay.LocalIndex, circuit)] = Environment.TickCount64;
            return true;
        }
    }

    private bool IsHeldRelay(MeshSession session)
    {
        lock (_gate) return _held.Any(held => ReferenceEquals(held.Session, session));
    }

    // ------------------------------------------------------------------ running

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var tick = 0L;
        try
        {
            await MapPortAsync(cancellationToken).ConfigureAwait(false);
            await JoinAsync(cancellationToken).ConfigureAwait(false);
            await CheckReachabilityAsync(cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.MaintenanceInterval, cancellationToken).ConfigureAwait(false);
                tick++;
                try
                {
                    await MaintainAsync(tick, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // One failed round of upkeep is retried at the next.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MaintainAsync(long tick, CancellationToken cancellationToken)
    {
        Records.Prune(DateTimeOffset.UtcNow);
        if (Table.Count < _options.Dht.K / 2) await JoinAsync(cancellationToken).ConfigureAwait(false);
        if (_mapping?.RenewAt is { } renewAt && DateTimeOffset.UtcNow >= renewAt) await MapPortAsync(cancellationToken).ConfigureAwait(false);
        if (Environment.TickCount64 - _lastReachabilityCheck > TimeSpan.FromMinutes(30).TotalMilliseconds || Reachability is MeshReachability.Isolated)
            await CheckReachabilityAsync(cancellationToken).ConfigureAwait(false);
        await EnsureRelaysAsync(cancellationToken).ConfigureAwait(false);

        if (tick % 5 == 0) await Dht.CheckStaleAsync(TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        if (tick % 15 == 0) await Dht.RefreshAsync(TimeSpan.FromMinutes(15), cancellationToken).ConfigureAwait(false);
        if (tick % 30 == 0 && Dht.Serving) await Dht.RepublishAsync(TimeSpan.FromMinutes(40), cancellationToken).ConfigureAwait(false);
        if (tick % 10 == 0) SaveNodes();
    }

    private async Task JoinAsync(CancellationToken cancellationToken)
    {
        IPEndPoint[] seeds;
        lock (_gate) seeds = _seeds.ToArray();
        var before = Table.Count;
        await Dht.BootstrapAsync(seeds.Concat(Table.All().Select(contact => contact.Endpoint)).Distinct(), cancellationToken).ConfigureAwait(false);
        if (Table.Count != before) Changed?.Invoke();
    }

    private async Task MapPortAsync(CancellationToken cancellationToken)
    {
        if (!_options.MapPort || _portMapper is null) return;
        try
        {
            var mapping = _mapping is { } current
                ? await _portMapper.RenewAsync(current, cancellationToken).ConfigureAwait(false) ??
                  await _portMapper.MapUdpAsync(_socket.LocalEndPoint.Port, _options.PortMappingLifetime, cancellationToken).ConfigureAwait(false)
                : await _portMapper.MapUdpAsync(_socket.LocalEndPoint.Port, _options.PortMappingLifetime, cancellationToken).ConfigureAwait(false);
            if (!Equals(mapping?.ExternalPort, _mapping?.ExternalPort) || !Equals(mapping?.ExternalAddress, _mapping?.ExternalAddress))
                _lastReachabilityCheck = 0;
            _mapping = mapping;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
        }
    }

    /// <summary>
    /// Finds out whether the Internet can reach us, and where. Every candidate address -- the one
    /// the router mapped, the ones peers see us at, our own IPv6 addresses -- is only believed once
    /// another node has sent a probe to it from an address we never talked to, and it arrived.
    /// </summary>
    private async Task CheckReachabilityAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            _lastReachabilityCheck = Environment.TickCount64;
            // Any node can dial back -- it only needs to send one datagram from a fresh socket --
            // so any session will do, reachable or not: on a brand new network nobody is known to
            // be reachable yet, and somebody has to find out first.
            var helpers = Transport.Sessions
                .Where(session => session.Route.IsDirect && !IsHeldRelay(session))
                .OrderBy(_ => Random.Shared.Next())
                .Take(8)
                .ToList();
            if (helpers.Count == 0)
            {
                IPEndPoint[] seeds;
                lock (_gate) seeds = _seeds.ToArray();
                foreach (var endpoint in Table.Closest(Transport.LocalId, 4).Select(contact => contact.Endpoint).Concat(seeds).Distinct().Take(6))
                    if (await Transport.ConnectAsync(MeshRoute.Direct(endpoint), cancellationToken).ConfigureAwait(false) is { } session)
                        helpers.Add(session);
            }

            if (helpers.Count == 0)
            {
                SetReachability(MeshReachability.Isolated, Array.Empty<IPEndPoint>());
                return;
            }

            var candidates = new List<IPEndPoint>();
            if (_mapping is { ExternalAddress: { } external } mapping && _options.IsRoutable(external))
                candidates.Add(new IPEndPoint(external, mapping.ExternalPort));
            candidates.AddRange(helpers.Select(session => session.ObservedSelf).OfType<IPEndPoint>().Where(endpoint => _options.IsRoutable(endpoint.Address)));
            if (_options.UseInterfaceAddresses) candidates.AddRange(GlobalIPv6Addresses().Select(address => new IPEndPoint(address, _socket.LocalEndPoint.Port)));

            var proven = new List<IPEndPoint>();
            foreach (var candidate in candidates.Select(MeshAddresses.Normalize).Distinct().Take(4))
            {
                // A helper can only probe the address it sees us at; an address of the other family
                // needs a helper reached over that family.
                var helper = helpers.FirstOrDefault(session => session.ObservedSelf is { } seen && seen.Address.Equals(candidate.Address));
                if (helper is null) continue;
                if (await ProbeAsync(helper, candidate, cancellationToken).ConfigureAwait(false)) proven.Add(candidate);
            }

            SetReachability(proven.Count > 0 ? MeshReachability.Public : MeshReachability.Relayed, proven.Take(MeshPackets.MaximumAdvertised).ToArray());
            await EnsureRelaysAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private async Task<bool> ProbeAsync(MeshSession helper, IPEndPoint candidate, CancellationToken cancellationToken)
    {
        var nonce = RandomNumberGenerator.GetBytes(MeshPackets.ProbeNonceLength);
        var key = Convert.ToBase64String(nonce);
        var heard = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_probes) _probes[key] = heard;
        try
        {
            var body = new byte[MeshPackets.ProbeNonceLength + 2];
            nonce.CopyTo(body, 0);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(MeshPackets.ProbeNonceLength), (ushort)candidate.Port);
            var reply = await Transport.RequestAsync(helper, MeshOps.DialBack, body, cancellationToken).ConfigureAwait(false);
            if (reply is not [0]) return false;
            var done = await Task.WhenAny(heard.Task, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken)).ConfigureAwait(false);
            return done == heard.Task;
        }
        finally
        {
            lock (_probes) _probes.Remove(key);
        }
    }

    private void SetReachability(MeshReachability reachability, IReadOnlyList<IPEndPoint> endpoints)
    {
        var wasServing = Dht.Serving;
        var changed = reachability != Reachability || !endpoints.SequenceEqual(_publicEndpoints);
        Reachability = reachability;
        _publicEndpoints = endpoints;
        var serving = reachability == MeshReachability.Public;
        _flags = serving ? MeshNodeFlags.Server | MeshNodeFlags.Relay : MeshNodeFlags.None;
        Dht.Serving = serving;
        _relay.Enabled = serving;

        // Sessions opened before we knew told their peers we were not reachable. Opening them
        // again is how the network learns otherwise -- and how a node that stopped being
        // reachable stops being routed to.
        if (serving != wasServing && _lifetime is { } lifetime)
        {
            foreach (var session in Transport.Sessions.Where(session => !session.Pinned)) Transport.Close(session);
            _ = JoinAsync(lifetime.Token);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Keeps <see cref="MeshNodeOptions.RelaysWanted"/> reservations while unreachable, renewing them before they lapse.</summary>
    private async Task EnsureRelaysAsync(CancellationToken cancellationToken)
    {
        if (Reachability == MeshReachability.Public)
        {
            List<HeldReservation> released;
            lock (_gate)
            {
                released = _held.ToList();
                _held.Clear();
            }
            foreach (var held in released) held.Session.Pinned = false;
            if (released.Count > 0) Changed?.Invoke();
            return;
        }
        if (Reachability == MeshReachability.Starting) return;

        HeldReservation[] renew;
        int missing;
        lock (_gate)
        {
            _held.RemoveAll(held => held.Session.Closed);
            renew = _held.Where(held => Environment.TickCount64 >= held.RenewAt).ToArray();
            missing = _options.RelaysWanted - _held.Count;
        }

        foreach (var held in renew)
        {
            var reply = await Transport.RequestAsync(held.Session, MeshOps.Reserve, held.Token, cancellationToken).ConfigureAwait(false);
            if (reply is [0, ..]) held.RenewAt = Environment.TickCount64 + (long)(RelayService.ReservationLifetime.TotalMilliseconds * 0.4);
            else
            {
                lock (_gate) _held.Remove(held);
                held.Session.Pinned = false;
                missing++;
            }
        }

        if (missing <= 0) return;
        var changed = false;
        foreach (var contact in Table.All().OrderBy(_ => Random.Shared.Next()))
        {
            if (missing <= 0) break;
            lock (_gate)
                if (_held.Any(held => held.Endpoint.Equals(contact.Endpoint))) continue;

            var session = await Transport.ConnectAsync(MeshRoute.Direct(contact.Endpoint), cancellationToken).ConfigureAwait(false);
            if (session is null || session.RemoteId != contact.Id || !session.RemoteFlags.HasFlag(MeshNodeFlags.Relay)) continue;
            var reply = await Transport.RequestAsync(session, MeshOps.Reserve, new byte[MeshRelayContact.TokenLength], cancellationToken).ConfigureAwait(false);
            if (reply is not { Length: 1 + MeshRelayContact.TokenLength + 4 } || reply[0] != 0) continue;

            session.Pinned = true;
            var token = reply.AsSpan(1, MeshRelayContact.TokenLength).ToArray();
            lock (_gate)
                _held.Add(new HeldReservation(session, contact.Endpoint, token)
                {
                    RenewAt = Environment.TickCount64 + (long)(RelayService.ReservationLifetime.TotalMilliseconds * 0.4)
                });
            missing--;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    private static IEnumerable<IPAddress> GlobalIPv6Addresses()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        foreach (var networkInterface in interfaces)
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            foreach (var unicast in properties.UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 && MeshAddresses.IsPublic(unicast.Address))
                    yield return unicast.Address;
        }
    }

    // ------------------------------------------------------------------ remembering the way back in

    private IEnumerable<IPEndPoint> LoadNodes()
    {
        if (_options.NodesFile is not { } file || !File.Exists(file)) return Array.Empty<IPEndPoint>();
        try
        {
            var entries = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? new();
            return entries.Take(64)
                .Select(entry => IPEndPoint.TryParse(entry, out var endpoint) ? endpoint : null)
                .OfType<IPEndPoint>()
                .Where(endpoint => endpoint.Port > 0 && _options.IsRoutable(endpoint.Address))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<IPEndPoint>();
        }
    }

    /// <summary>Public nodes only -- the ones anybody could find -- and never our own addresses.</summary>
    public void SaveNodes()
    {
        if (_options.NodesFile is not { } file) return;
        try
        {
            var endpoints = Table.All().Select(contact => contact.Endpoint)
                .Where(endpoint => !_publicEndpoints.Contains(endpoint))
                .Take(64)
                .Select(endpoint => endpoint.ToString())
                .ToList();
            if (endpoints.Count == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(endpoints));
            File.Move(temporary, file, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime?.Cancel();
        SaveNodes();
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

        if (_mapping is { } mapping && _portMapper is not null)
        {
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _portMapper.RemoveAsync(mapping, budget.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }

        await Transport.DisposeAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
    }

    private sealed class HeldReservation
    {
        public HeldReservation(MeshSession session, IPEndPoint endpoint, byte[] token)
        {
            Session = session;
            Endpoint = endpoint;
            Token = token;
        }

        public MeshSession Session { get; }
        public IPEndPoint Endpoint { get; }
        public byte[] Token { get; }
        public long RenewAt { get; set; }
    }
}
