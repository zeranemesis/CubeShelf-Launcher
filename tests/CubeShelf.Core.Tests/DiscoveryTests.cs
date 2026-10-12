using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Lan;
using CubeShelf.Core.Social.Mesh;
using CubeShelf.Core.Social.Mesh.Discovery;
using static MeshKit;

/// <summary>A local network announcer whose datagrams come from whatever address the test says.</summary>
sealed class SourcedAnnouncer : ILanAnnouncer
{
    public event Action<byte[], IPAddress>? Received;
    public void Start() { }
    public Task SendAsync(byte[] datagram, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Deliver(byte[] datagram, IPAddress from) => Received?.Invoke(datagram, from);
    public void Dispose() { }
}

/// <summary>
/// A BitTorrent DHT node on loopback, as much of one as a lookup meets: it answers get_peers with
/// the nodes it was told about and the peers announced to it, hands out a token, and keeps
/// announcements that bring it back.
/// </summary>
sealed class FakeDhtNode : IDisposable
{
    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly byte[] _token = Encoding.ASCII.GetBytes("tok-" + Guid.NewGuid().ToString("N")[..6]);

    public FakeDhtNode(byte[]? id = null)
    {
        Id = id ?? System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        _ = Serve();
    }

    public byte[] Id { get; }
    public IPEndPoint Endpoint => (IPEndPoint)_socket.Client.LocalEndPoint!;
    public List<FakeDhtNode> Neighbours { get; } = new();
    public ConcurrentBag<IPEndPoint> Peers { get; } = new();
    public ConcurrentBag<(byte[] InfoHash, long Port, bool ReadOnly)> Announced { get; } = new();
    public int Queries;

    /// <summary>Answers from another socket than the one asked: what a forger would do.</summary>
    public bool AnswerFromElsewhere { get; set; }

    private async Task Serve()
    {
        using var elsewhere = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        while (!_lifetime.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await _socket.ReceiveAsync(_lifetime.Token);
            }
            catch (Exception)
            {
                return;
            }
            Interlocked.Increment(ref Queries);
            if (Bencode.Decode(received.Buffer) is not Dictionary<string, object> query) continue;
            var arguments = (Dictionary<string, object>)query["a"];
            var method = Encoding.ASCII.GetString((byte[])query["q"]);
            var reply = new Dictionary<string, object> { ["id"] = Id };

            if (method == "get_peers")
            {
                reply["token"] = _token;
                if (!Peers.IsEmpty)
                    reply["values"] = Peers.Select(peer => (object)Compact(peer)).ToList();
                if (Neighbours.Count > 0)
                    reply["nodes"] = Neighbours.SelectMany(node => node.Id.Concat(Compact(node.Endpoint))).ToArray();
            }
            else if (method == "announce_peer")
            {
                if (!((byte[])arguments["token"]).AsSpan().SequenceEqual(_token)) continue;
                Announced.Add(((byte[])arguments["info_hash"], (long)arguments["port"], query.TryGetValue("ro", out var ro) && ro is 1L));
            }

            var message = Bencode.Encode(new Dictionary<string, object> { ["t"] = query["t"], ["y"] = "r", ["r"] = reply });
            await (AnswerFromElsewhere ? elsewhere : _socket).SendAsync(message, received.RemoteEndPoint);
        }
    }

    private static byte[] Compact(IPEndPoint endpoint)
    {
        var bytes = new byte[6];
        endpoint.Address.MapToIPv4().GetAddressBytes().CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)endpoint.Port);
        return bytes;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _socket.Dispose();
    }
}

static class DiscoveryTests
{
    private static MainlineRendezvousOptions Loopback(params FakeDhtNode[] start) => new()
    {
        StartingNodes = start.Select(node => node.Endpoint).ToArray(),
        IsRoutable = _ => true,
        QueryTimeout = TimeSpan.FromMilliseconds(500),
        LookupBudget = TimeSpan.FromSeconds(5)
    };

    public static void BencodeRefusesWhatIsNotExactlyBencode()
    {
        var message = Bencode.Encode(new Dictionary<string, object>
        {
            ["t"] = new byte[] { 1, 2 },
            ["y"] = "q",
            ["a"] = new Dictionary<string, object> { ["port"] = 6881L, ["id"] = new byte[20] },
            ["list"] = new List<object> { 1L, "x" }
        });
        // Keys in byte order, as the format requires.
        Check(Encoding.Latin1.GetString(message).StartsWith("d1:ad2:id20:"), "keys sorted");
        var decoded = Bencode.Decode(message) as Dictionary<string, object>;
        Check(decoded is not null && ((Dictionary<string, object>)decoded["a"])["port"] is 6881L, "round trip");

        string[] hostile =
        {
            "", "i01e", "i-0e", "ie", "i-e", "i99999999999999999999e", "5:abc", "3:abc trailing", "d1:a", "l", "x",
            "d1:ai1e1:ai2ee",                            // the same key twice
            "di1ei2ee",                                  // a key that is not a byte string
            "01:a", "999999:a",
            new string('l', 40) + new string('e', 40)    // nested past any use
        };
        foreach (var input in hostile)
            Check(Bencode.Decode(Encoding.Latin1.GetBytes(input)) is null, "refused: " + (input.Length > 20 ? input[..20] + "…" : input));
        Check(Bencode.Decode(new byte[Bencode.MaximumInput + 1]) is null, "oversized refused");
        Check(Bencode.Decode(Encoding.ASCII.GetBytes("l" + string.Concat(Enumerable.Repeat("i1e", 600)) + "e")) is null, "too many items refused");
    }

    public static void ALookupFindsCubeShelfNodesAndAnnouncesWhereItShould()
    {
        // A router that knows two nodes; the nearer one holds what CubeShelf nodes announced.
        using var router = new FakeDhtNode();
        using var far = new FakeDhtNode();
        var nearId = (byte[])MainlineRendezvous.MeetingPoint.Clone();
        nearId[19] ^= 1;
        using var near = new FakeDhtNode(nearId);
        router.Neighbours.AddRange(new[] { far, near });
        near.Peers.Add(new IPEndPoint(IPAddress.Parse("81.2.69.160"), 47914));
        near.Peers.Add(new IPEndPoint(IPAddress.Parse("93.184.216.34"), 50000));

        using var rendezvous = new MainlineRendezvous(Loopback(router));
        var lookup = Wait(rendezvous.LookupAsync(), 10000);
        Check(lookup.Peers.Count == 2, $"both CubeShelf nodes found ({lookup.Peers.Count})");
        // Every fake node shares 127.0.0.1: one machine counts once, the nearest of its ports.
        Check(lookup.Closest.Count == 1 && lookup.Closest[0].Node.Equals(near.Endpoint), "the nearest node, once per address, with its token");

        var accepted = Wait(rendezvous.AnnounceAsync(lookup, 47914), 10000);
        Check(accepted == 1, $"announced at the nearest node ({accepted})");
        var said = near.Announced.Single();
        Check(said.InfoHash.AsSpan().SequenceEqual(MainlineRendezvous.MeetingPoint) && said.Port == 47914, "under the meeting point, with our port");
        Check(said.ReadOnly, "as a read-only node: never kept in anyone's table");
    }

    public static void AnswersFromElsewhereAreIgnored()
    {
        using var forger = new FakeDhtNode { AnswerFromElsewhere = true };
        forger.Peers.Add(new IPEndPoint(IPAddress.Parse("81.2.69.161"), 1234));
        using var rendezvous = new MainlineRendezvous(Loopback(forger));
        var lookup = Wait(rendezvous.LookupAsync(), 10000);
        Check(forger.Queries >= 1, "the node was asked");
        Check(lookup.Peers.Count == 0 && lookup.Closest.Count == 0, "an answer from another address than the one asked is not believed");
    }

    public static void PrivateAndLoopbackAddressesAreNeverFollowed()
    {
        using var router = new FakeDhtNode();
        using var inside = new FakeDhtNode();
        router.Neighbours.Add(inside);    // a loopback node, as a LAN address would be
        router.Peers.Add(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47914));
        router.Peers.Add(new IPEndPoint(IPAddress.Parse("81.2.69.162"), 47914));

        // The real rule: only public addresses. The router itself is a starting point, so it is asked.
        using var rendezvous = new MainlineRendezvous(Loopback(router) with { IsRoutable = MeshAddresses.IsPublic });
        var lookup = Wait(rendezvous.LookupAsync(), 10000);
        Check(inside.Queries == 0, "a node at a private or loopback address is never asked");
        Check(lookup.Peers.Count == 1 && lookup.Peers[0].Address.ToString() == "81.2.69.162", "and no private address is handed on");
    }

    public static void ANewcomerJoinsThroughTheMeetingPoint()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 4);
        using var dht = new FakeDhtNode();
        // The backbone's nodes announced themselves earlier.
        foreach (var node in backbone) dht.Peers.Add(node.Transport.LocalEndPoint);

        // A newcomer with no code and no seed at all.
        var newcomer = MeshNodeTests.Node(network, network.BindPublic());
        var discovery = new MeshDiscovery(newcomer, new MeshDiscoveryOptions { Rendezvous = Loopback(dht) });
        try
        {
            Check(newcomer.Table.Count == 0, "knows nobody at first");
            var (found, _) = Wait(discovery.RunOnceAsync(), 15000);
            Check(found == 4, $"found the CubeShelf nodes at the meeting point ({found})");
            Eventually(() => newcomer.Table.Count >= 3, "and joined the network through them", 20000);

            // Reachable now, it says so in turn, with the port others reach it on.
            Eventually(() => newcomer.Reachability == MeshReachability.Public, "proven reachable", 20000);
            var (_, announced) = Wait(discovery.RunOnceAsync(), 15000);
            Check(announced, "announced itself");
            var port = newcomer.Address.Direct.First(endpoint => endpoint.AddressFamily == AddressFamily.InterNetwork).Port;
            Check(dht.Announced.Any(entry => entry.Port == port), "with its reachable port");
        }
        finally
        {
            discovery.DisposeAsync().AsTask().Wait();
            newcomer.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void AClosedNodeOnlyLooks()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 3);
        using var dht = new FakeDhtNode();
        foreach (var node in backbone) dht.Peers.Add(node.Transport.LocalEndPoint);

        var closed = MeshNodeTests.Node(network, network.BindBehind(network.CreateNat(SimulatedNatKind.Symmetric)));
        var discovery = new MeshDiscovery(closed, new MeshDiscoveryOptions { Rendezvous = Loopback(dht) });
        try
        {
            Wait(discovery.RunOnceAsync(), 15000);
            Eventually(() => closed.Table.Count >= 2, "joined", 20000);
            Eventually(() => closed.Reachability == MeshReachability.Relayed, "behind its router", 20000);
            var (_, announced) = Wait(discovery.RunOnceAsync(), 15000);
            Check(!announced && dht.Announced.IsEmpty, "an address nobody can reach is never announced");
        }
        finally
        {
            discovery.DisposeAsync().AsTask().Wait();
            closed.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void AFloodOfPlantedAddressesCannotLockTheDoor()
    {
        var network = new SimulatedNetwork();
        var lonely = MeshNodeTests.Node(network, network.BindPublic());
        try
        {
            var friendSeed = new IPEndPoint(IPAddress.Parse("81.2.69.200"), 47914);
            lonely.AddSeeds(new[] { friendSeed });

            // Someone floods the meeting point or the local network with addresses of their choosing.
            for (var batch = 0; batch < 20; batch++)
                lonely.AddDiscoveredSeeds(Enumerable.Range(1, 16).Select(index => new IPEndPoint(IPAddress.Parse($"93.184.{batch}.{index}"), 1000 + index)));

            var honest = new IPEndPoint(IPAddress.Parse("81.2.69.201"), 47914);
            lonely.AddDiscoveredSeeds(new[] { honest });
            var seeds = lonely.AllSeeds();
            Check(seeds.Contains(friendSeed), "a way in we were given is never pushed out");
            Check(seeds.Contains(honest), "a way in found after the flood still gets in");
            Check(seeds.Length <= 33, $"the found ones stay few ({seeds.Length})");
        }
        finally
        {
            lonely.DisposeAsync().AsTask().Wait();
        }
    }

    public static void ALookupSurvivesNamesThatNeverResolve()
    {
        using var rendezvous = new MainlineRendezvous(new MainlineRendezvousOptions
        {
            Routers = new[] { ("router.cubeshelf.invalid", 6881) },
            LookupBudget = TimeSpan.FromMilliseconds(1)
        });
        var lookup = Wait(rendezvous.LookupAsync(), 10000);
        Check(lookup.Peers.Count == 0 && lookup.Queries == 0, "an empty round, not an exception");
    }

    public static void OnlyNeighboursPassWaysInAndNotTooOften()
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-lanhint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var me = PeerIdentity.Create();
        using var other = PeerIdentity.Create();
        var announcer = new SourcedAnnouncer();
        var node = new LanNode(me, new FriendStore(root), announcer, () => "Me", () => "");
        var hints = 0;
        node.MeshEntryPointsHeard += _ => Interlocked.Increment(ref hints);

        var sender = new LanNode(other, new FriendStore(Path.Combine(root, "o")), new SourcedAnnouncer(), () => "Other", () => "")
        {
            MeshEntryPoints = () => new[] { new IPEndPoint(IPAddress.Parse("81.2.69.163"), 47914) }
        };
        // Started, so its announcement names a port, as a real one always does.
        sender.Start();
        var datagram = LanProtocol.Serialize(sender.BuildAnnouncements().First());
        try
        {
            node.Start();
            announcer.Deliver(datagram, IPAddress.Parse("81.2.69.99"));
            Check(hints == 0, "a public sender is no neighbour: ignored");
            announcer.Deliver(datagram, IPAddress.Parse("192.168.1.40"));
            Check(hints == 1, "a neighbour is heard");
            announcer.Deliver(datagram, IPAddress.Parse("192.168.1.40"));
            Check(hints == 1, "but not again within the minute");
        }
        finally
        {
            node.DisposeAsync().AsTask().Wait();
            sender.DisposeAsync().AsTask().Wait();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    public static void TheLocalNetworkPassesWaysIn()
    {
        var root = Path.Combine(Path.GetTempPath(), "cubeshelf-lanmesh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var bus = new TestLanBus();
        using var aliceId = PeerIdentity.Create();
        using var bobId = PeerIdentity.Create();
        // Strangers: neither is in the other's list.
        var alice = new LanNode(aliceId, new FriendStore(Path.Combine(root, "a")), bus.Join(), () => "Alice", () => "");
        var bob = new LanNode(bobId, new FriendStore(Path.Combine(root, "b")), bus.Join(), () => "Bob", () => "");
        alice.MeshEntryPoints = () => new[]
        {
            new IPEndPoint(IPAddress.Parse("81.2.69.163"), 47914),
            new IPEndPoint(IPAddress.Parse("192.168.1.30"), 47914),   // never passed on
            new IPEndPoint(IPAddress.Parse("93.184.216.35"), 50001)
        };
        var heard = new ConcurrentBag<IPEndPoint>();
        bob.MeshEntryPointsHeard += seeds => { foreach (var seed in seeds) heard.Add(seed); };
        try
        {
            // Bob listens first: Alice's very first announcement is the one he should hear.
            bob.Start();
            alice.Start();
            Eventually(() => heard.Count >= 2, "Bob heard Alice's ways in", 5000);
            Check(heard.All(seed => MeshAddresses.IsPublic(seed.Address)), "public addresses only");
            Check(heard.Any(seed => seed.ToString() == "81.2.69.163:47914"), "as Alice knows them");
        }
        finally
        {
            alice.DisposeAsync().AsTask().Wait();
            bob.DisposeAsync().AsTask().Wait();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
