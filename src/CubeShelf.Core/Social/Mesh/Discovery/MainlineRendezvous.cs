using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CubeShelf.Core.Social.Mesh.Discovery;

public sealed record MainlineRendezvousOptions
{
    /// <summary>
    /// Where a lookup starts when nothing is known yet: the public entry points of BitTorrent's DHT,
    /// run by different people. Any one answering is enough.
    /// </summary>
    public IReadOnlyList<(string Host, int Port)> Routers { get; init; } = new[]
    {
        ("router.bittorrent.com", 6881),
        ("dht.transmissionbt.com", 6881),
        ("router.utorrent.com", 6881),
        ("dht.libtorrent.org", 25401),
        ("dht.aelitis.com", 6881)
    };

    /// <summary>For tests: start from these instead of resolving <see cref="Routers"/>.</summary>
    public IReadOnlyList<IPEndPoint>? StartingNodes { get; init; }

    /// <summary>Which node addresses may be asked. Public ones only, outside tests.</summary>
    public Func<IPAddress, bool> IsRoutable { get; init; } = MeshAddresses.IsPublic;

    public int Parallelism { get; init; } = 8;
    public int MaximumQueries { get; init; } = 96;
    public int MaximumPeers { get; init; } = 64;
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan LookupBudget { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>What a lookup found: CubeShelf nodes, and the DHT nodes nearest the meeting point (with their tokens).</summary>
public sealed record MainlineLookup(IReadOnlyList<IPEndPoint> Peers, IReadOnlyList<(IPEndPoint Node, byte[] Token)> Closest, int Queries);

/// <summary>
/// How CubeShelf installations that do not know each other find each other, with no server of
/// our own: through BitTorrent's DHT, millions of computers that already keep such meeting points.
///
/// Every CubeShelf looks under one well-known key -- a hash of a fixed label, the same for all
/// (<see cref="MeetingPoint"/>) -- and gets back the addresses of CubeShelf nodes that announced
/// themselves there. Only a node others can reach directly announces itself; a node behind a
/// closed router only looks. What it finds are entry points into the CubeShelf network, nothing
/// more: every node met is then checked by the network's own handshake and proof of work, so an
/// address planted here costs one failed connection attempt.
///
/// This speaks the DHT's public protocol (BEP 5), as a read-only node (BEP 43): it asks, it is not
/// asked, and it never enters other nodes' tables. Its own socket and its own random id each time,
/// unrelated to anything in the CubeShelf network.
/// </summary>
public sealed class MainlineRendezvous : IDisposable
{
    /// <summary>The key CubeShelf nodes meet under. Changes only with the network's protocol.</summary>
    public static readonly byte[] MeetingPoint = SHA1.HashData(Encoding.ASCII.GetBytes("cubeshelf-mesh-rendezvous-v1"));

    private const int MaximumDatagram = Bencode.MaximumInput;

    private readonly MainlineRendezvousOptions _options;
    private readonly UdpClient _socket;
    private readonly byte[] _id = RandomNumberGenerator.GetBytes(20);
    private readonly object _gate = new();
    private readonly Dictionary<ushort, (IPEndPoint To, TaskCompletionSource<Dictionary<string, object>?> Answer)> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _receive;
    private bool _disposed;

    public MainlineRendezvous(MainlineRendezvousOptions? options = null)
    {
        _options = options ?? new MainlineRendezvousOptions();
        _socket = new UdpClient(AddressFamily.InterNetwork);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        _receive = ReceiveLoopAsync(_lifetime.Token);
    }

    public int LocalPort => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    /// <summary>Finds CubeShelf nodes that announced themselves.</summary>
    public async Task<MainlineLookup> LookupAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        budget.CancelAfter(_options.LookupBudget);

        IReadOnlyList<IPEndPoint> starting;
        try
        {
            starting = await StartingNodesAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Name resolution took the whole budget: nothing to ask this round.
            return new MainlineLookup(Array.Empty<IPEndPoint>(), Array.Empty<(IPEndPoint, byte[])>(), 0);
        }
        var candidates = new Dictionary<string, (IPEndPoint Endpoint, byte[] Distance)>(StringComparer.Ordinal);
        foreach (var node in starting) candidates.TryAdd(node.ToString(), (node, Array.Empty<byte>()));
        var asked = new HashSet<string>(StringComparer.Ordinal);
        var answered = new List<(IPEndPoint Node, byte[] Distance, byte[] Token)>();
        var peers = new Dictionary<string, IPEndPoint>(StringComparer.Ordinal);
        var queries = 0;

        try
        {
            while (queries < _options.MaximumQueries && !budget.IsCancellationRequested)
            {
                // The nearest not yet asked; the starting nodes, with no distance, first.
                var batch = candidates.Values
                    .Where(candidate => !asked.Contains(candidate.Endpoint.ToString()))
                    .OrderBy(candidate => candidate.Distance, DistanceOrder.Instance)
                    .Take(Math.Min(_options.Parallelism, _options.MaximumQueries - queries))
                    .ToArray();
                if (batch.Length == 0) break;

                // Stop once the closest answers are all nearer than anything left to ask.
                if (answered.Count >= 8 && batch[0].Distance.Length > 0 &&
                    DistanceOrder.Instance.Compare(batch[0].Distance, answered.OrderBy(entry => entry.Distance, DistanceOrder.Instance).ElementAt(7).Distance) > 0)
                    break;

                foreach (var candidate in batch) asked.Add(candidate.Endpoint.ToString());
                queries += batch.Length;
                var replies = await Task.WhenAll(batch.Select(candidate => QueryAsync(candidate.Endpoint, GetPeers(), budget.Token)))
                    .ConfigureAwait(false);

                for (var index = 0; index < batch.Length; index++)
                {
                    if (replies[index] is not { } reply || reply.TryGetValue("r", out var body) is false || body is not Dictionary<string, object> r) continue;
                    if (r.TryGetValue("id", out var id) && id is byte[] { Length: 20 } nodeId && r.TryGetValue("token", out var token) && token is byte[] { Length: > 0 and <= 64 } tokenBytes)
                        answered.Add((batch[index].Endpoint, Xor(nodeId, MeetingPoint), tokenBytes));

                    if (r.TryGetValue("values", out var values) && values is List<object> list)
                    {
                        foreach (var value in list.OfType<byte[]>().Where(value => value.Length == 6))
                        {
                            if (peers.Count >= _options.MaximumPeers) break;
                            var peer = Compact(value, 0);
                            if (peer.Port > 0 && _options.IsRoutable(peer.Address)) peers.TryAdd(peer.ToString(), peer);
                        }
                    }

                    if (r.TryGetValue("nodes", out var nodes) && nodes is byte[] compact && compact.Length % 26 == 0)
                    {
                        for (var offset = 0; offset + 26 <= compact.Length && candidates.Count < 1024; offset += 26)
                        {
                            var node = Compact(compact, offset + 20);
                            if (node.Port == 0 || !_options.IsRoutable(node.Address)) continue;
                            candidates.TryAdd(node.ToString(), (node, Xor(compact.AsSpan(offset, 20).ToArray(), MeetingPoint)));
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget ran out: what was found so far is the answer.
        }

        // One per address: a single machine answering on eight ports must not pass for the eight
        // nodes nearest the meeting point and take every announcement.
        var closest = answered
            .GroupBy(entry => entry.Node.Address.ToString())
            .Select(group => group.OrderBy(entry => entry.Distance, DistanceOrder.Instance).First())
            .OrderBy(entry => entry.Distance, DistanceOrder.Instance)
            .Take(8)
            .Select(entry => (entry.Node, entry.Token))
            .ToArray();
        return new MainlineLookup(peers.Values.ToArray(), closest, queries);
    }

    /// <summary>
    /// Says, at the nodes nearest the meeting point, that a CubeShelf node listens on
    /// <paramref name="port"/> at the address those nodes see this socket come from. Returns how
    /// many accepted. Only for a node others can reach directly.
    /// </summary>
    public async Task<int> AnnounceAsync(MainlineLookup lookup, int port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        budget.CancelAfter(_options.LookupBudget);

        Dictionary<string, object>?[] replies;
        try
        {
            replies = await Task.WhenAll(lookup.Closest.Select(entry => QueryAsync(entry.Node, new Dictionary<string, object>
            {
                ["q"] = "announce_peer",
                ["a"] = new Dictionary<string, object>
                {
                    ["id"] = _id,
                    ["info_hash"] = MeetingPoint,
                    ["port"] = (long)port,
                    ["implied_port"] = 0L,
                    ["token"] = entry.Token
                }
            }, budget.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        return replies.Count(reply => reply is not null && reply.ContainsKey("r"));
    }

    private Dictionary<string, object> GetPeers() => new()
    {
        ["q"] = "get_peers",
        ["a"] = new Dictionary<string, object> { ["id"] = _id, ["info_hash"] = MeetingPoint }
    };

    private async Task<IReadOnlyList<IPEndPoint>> StartingNodesAsync(CancellationToken cancellationToken)
    {
        if (_options.StartingNodes is { } fixedNodes) return fixedNodes;
        var found = new List<IPEndPoint>();
        foreach (var (host, port) in _options.Routers)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
                found.AddRange(addresses.Take(2).Select(address => new IPEndPoint(address, port)));
            }
            catch (SocketException)
            {
                // That router is gone or this network has no DNS for it: the others will do.
            }
        }
        return found;
    }

    /// <summary>One query, one answer from the node it was sent to, or null after the timeout.</summary>
    private async Task<Dictionary<string, object>?> QueryAsync(IPEndPoint to, Dictionary<string, object> query, CancellationToken cancellationToken)
    {
        ushort transaction;
        var answer = new TaskCompletionSource<Dictionary<string, object>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // Random each time: a node we asked must not be able to tell which ids come next.
            do transaction = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1); while (_pending.ContainsKey(transaction));
            _pending[transaction] = (to, answer);
        }

        try
        {
            var transactionBytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(transactionBytes, transaction);
            query["t"] = transactionBytes;
            query["y"] = "q";
            // Read-only (BEP 43): we ask, we are not to be asked or kept in anyone's table.
            query["ro"] = 1L;
            var datagram = Bencode.Encode(query);
            await _socket.SendAsync(datagram, to, cancellationToken).ConfigureAwait(false);
            return await answer.Task.WaitAsync(_options.QueryTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        finally
        {
            lock (_gate) _pending.Remove(transaction);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // An ICMP "unreachable" from one node surfaces here on Windows; the socket is fine.
                continue;
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            if (received.Buffer.Length > MaximumDatagram) continue;
            if (Bencode.Decode(received.Buffer) is not Dictionary<string, object> message) continue;
            if (!message.TryGetValue("t", out var t) || t is not byte[] { Length: 2 } transactionBytes) continue;
            if (!message.TryGetValue("y", out var y) || y is not byte[] kind || kind.Length != 1) continue;
            // Queries to us are not answered: a read-only node does not serve.
            if (kind[0] is not ((byte)'r' or (byte)'e')) continue;

            var transaction = BinaryPrimitives.ReadUInt16BigEndian(transactionBytes);
            TaskCompletionSource<Dictionary<string, object>?>? answer = null;
            lock (_gate)
            {
                // Only the node it was sent to may answer it: a reply from anywhere else is ignored.
                if (_pending.TryGetValue(transaction, out var pending) && SameEndpoint(pending.To, received.RemoteEndPoint))
                    answer = pending.Answer;
            }
            answer?.TrySetResult(kind[0] == (byte)'r' ? message : null);
        }
    }

    private static bool SameEndpoint(IPEndPoint a, IPEndPoint b) =>
        a.Port == b.Port && Normalize(a.Address).Equals(Normalize(b.Address));

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IPEndPoint Compact(byte[] bytes, int offset) =>
        new(new IPAddress(bytes.AsSpan(offset, 4)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4, 2)));

    private static byte[] Xor(byte[] a, byte[] b)
    {
        var result = new byte[20];
        for (var index = 0; index < 20; index++) result[index] = (byte)(a[index] ^ b[index]);
        return result;
    }

    /// <summary>Empty (a starting node, distance unknown) first, then by distance.</summary>
    private sealed class DistanceOrder : IComparer<byte[]>
    {
        public static readonly DistanceOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return 0;
            if (x.Length == 0 || y.Length == 0) return x.Length.CompareTo(y.Length);
            return x.AsSpan().SequenceCompareTo(y);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _socket.Dispose();
        try
        {
            _receive.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _lifetime.Dispose();
    }
}
