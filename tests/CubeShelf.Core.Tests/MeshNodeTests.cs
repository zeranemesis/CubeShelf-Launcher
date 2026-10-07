using System.Net;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

/// <summary>A router that honours UPnP: the mapping becomes a forwarded port on the simulated NAT.</summary>
sealed class SimulatedPortMapper : IPortMapper
{
    private readonly SimulatedNat _nat;
    private readonly SimulatedSocket _socket;

    public SimulatedPortMapper(SimulatedNat nat, SimulatedSocket socket)
    {
        _nat = nat;
        _socket = socket;
    }

    public Task<PortMapping?> MapUdpAsync(int internalPort, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var external = 30000 + internalPort % 1000;
        lock (_nat) _nat.Forwarded[external] = _socket.LocalEndPoint;
        return Task.FromResult<PortMapping?>(new PortMapping(PortMappingProtocol.Upnp, _nat.PublicAddress, external, internalPort,
            _socket.LocalEndPoint.Address, lifetime, DateTimeOffset.UtcNow));
    }

    public Task<PortMapping?> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default) =>
        MapUdpAsync(mapping.InternalPort, mapping.Lifetime, cancellationToken);

    public Task RemoveAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        lock (_nat) _nat.Forwarded.Remove(mapping.ExternalPort);
        return Task.CompletedTask;
    }
}

static class MeshNodeTests
{
    public static MeshNodeOptions NodeOptions => new()
    {
        Transport = Options(),
        Dht = new MeshDhtOptions { ProofDifficulty = Difficulty, K = 8, Replication = 6, RequestTimeout = TimeSpan.FromSeconds(1) },
        UseInterfaceAddresses = false,
        MaintenanceInterval = TimeSpan.FromMinutes(10),
        PunchTimeout = TimeSpan.FromSeconds(2)
    };

    public static MeshNode Node(SimulatedNetwork network, SimulatedSocket socket, IEnumerable<IPEndPoint>? seeds = null, IPortMapper? mapper = null)
    {
        var node = Wait(MeshNode.CreateAsync(socket, NodeOptions, mapper, () => network.BindBeside(socket)));
        node.Start(seeds);
        return node;
    }

    /// <summary>A few reachable nodes, joined, each knowing it is reachable.</summary>
    public static List<MeshNode> Backbone(SimulatedNetwork network, int count)
    {
        var first = Node(network, network.BindPublic());
        var nodes = new List<MeshNode> { first };
        nodes.AddRange(Enumerable.Range(1, count - 1).Select(_ => Node(network, network.BindPublic(), new[] { first.Transport.LocalEndPoint })));
        // The first node had nobody to ask at start: it finds out again now that others exist.
        Thread.Sleep(300);
        first.NetworkChanged();
        foreach (var node in nodes) Eventually(() => node.Reachability == MeshReachability.Public, "backbone node reachable", 20000);
        return nodes;
    }

    public static void ReachabilityIsProvenNotClaimed()
    {
        var network = new SimulatedNetwork();
        var backbone = Backbone(network, 6);
        var seeds = new[] { backbone[1].Transport.LocalEndPoint };

        var openNat = network.CreateNat(SimulatedNatKind.FullCone);
        var open = Node(network, network.BindBehind(openNat), seeds);
        var closedNat = network.CreateNat(SimulatedNatKind.PortRestricted);
        var closed = Node(network, network.BindBehind(closedNat), seeds);
        var mappedNat = network.CreateNat(SimulatedNatKind.PortRestricted);
        var mappedSocket = network.BindBehind(mappedNat);
        var mapped = Node(network, mappedSocket, seeds, new SimulatedPortMapper(mappedNat, mappedSocket));
        try
        {
            Eventually(() => open.Reachability != MeshReachability.Starting && closed.Reachability != MeshReachability.Starting &&
                             mapped.Reachability != MeshReachability.Starting, "all three know", 20000);

            Check(open.Reachability == MeshReachability.Public, "a full-cone router lets probes in: reachable");
            Check(closed.Reachability == MeshReachability.Relayed, "a port-restricted router does not: relayed");
            Check(mapped.Reachability == MeshReachability.Public, "the same router with the port mapped: reachable");
            Check(mapped.PublicEndpoints.Any(endpoint => endpoint.Address.Equals(mappedNat.PublicAddress) && endpoint.Port == mapped.Mapping!.ExternalPort),
                "at the mapped address");

            Eventually(() => closed.RelayContacts.Count == 2, "the relayed node holds two reservations", 20000);
            Check(closed.Address.Direct.Count == 0 && closed.Address.Relays.Count == 2, "and tells friends to come through them");

            // Reachable nodes end up in tables; the relayed one never does.
            Eventually(() => backbone.Any(node => node.Table.Get(mapped.Transport.LocalId) is not null), "the mapped node is routed to", 20000);
            Check(backbone.All(node => node.Table.Get(closed.Transport.LocalId) is null), "the relayed node is not");
        }
        finally
        {
            foreach (var node in backbone.Concat(new[] { open, closed, mapped })) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TwoClosedRoutersPunchThrough()
    {
        var network = new SimulatedNetwork();
        var backbone = Backbone(network, 5);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var a = Node(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), seeds);
        var b = Node(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), seeds);
        try
        {
            Eventually(() => a.RelayContacts.Count > 0 && b.RelayContacts.Count > 0, "both have relays", 20000);
            b.ApplicationRequests = (_, message, _) => Task.FromResult<byte[]?>(message.Body.Reverse().ToArray());

            var session = Wait(a.ConnectAsync(b.Address), 20000);
            Check(session is not null, "a reached b");
            Check(session!.RemoteId == b.Transport.LocalId, "it is b");
            Check(session.Route.IsDirect, "through both routers, directly: the relay only introduced them");
            Check(Wait(a.Transport.RequestAsync(session, 0x40, new byte[] { 1, 2 })) is [2, 1], "and they talk");
        }
        finally
        {
            foreach (var node in backbone.Concat(new[] { a, b })) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void SymmetricRoutersFallBackToTheRelay()
    {
        var network = new SimulatedNetwork();
        var backbone = Backbone(network, 5);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var a = Node(network, network.BindBehind(network.CreateNat(SimulatedNatKind.Symmetric)), seeds);
        var b = Node(network, network.BindBehind(network.CreateNat(SimulatedNatKind.Symmetric)), seeds);
        try
        {
            Eventually(() => a.RelayContacts.Count > 0 && b.RelayContacts.Count > 0, "both have relays", 20000);
            b.ApplicationRequests = (_, message, _) => Task.FromResult<byte[]?>(message.Body);

            var session = Wait(a.ConnectAsync(b.Address), 30000);
            Check(session is not null, "a reached b");
            Check(session!.RemoteId == b.Transport.LocalId, "it is b, proven by the handshake inside the relay");
            Check(!session.Route.IsDirect, "through the relay: no direct path exists");
            var body = RandomBytes(20_000);
            Check(Wait(a.Transport.RequestAsync(session, 0x41, body, timeout: TimeSpan.FromSeconds(5))) is { } echo && echo.AsSpan().SequenceEqual(body),
                "20 KB both ways through the relay");

            // The relay carried it without being a party: it has no session with a or b's ids
            // other than the reservation and the caller's, and it cannot open what passed.
            var relayNode = backbone.First(node => node.Transport.Sessions.Any(s => s.RemoteId == b.Transport.LocalId && s.Pinned));
            Check(relayNode.Transport.Sessions.All(s => s.RemoteId != a.Transport.LocalId || !s.Route.Equals(session.Route)), "the relay is not the endpoint");
        }
        finally
        {
            foreach (var node in backbone.Concat(new[] { a, b })) node.DisposeAsync().AsTask().Wait();
        }
    }

    /// <summary>
    /// Two people start CubeShelf for the first time, nobody else around. Neither can be proven
    /// reachable -- there is nobody to send the probe -- yet each one's code has to carry a way in,
    /// or neither could ever join the other: the router's mapped address is that way.
    /// </summary>
    public static void TwoNewcomersFindEachOtherFromTheirCodes()
    {
        var network = new SimulatedNetwork();
        var natA = network.CreateNat(SimulatedNatKind.PortRestricted);
        var socketA = network.BindBehind(natA);
        var a = Node(network, socketA, null, new SimulatedPortMapper(natA, socketA));
        MeshNode? b = null;
        try
        {
            Eventually(() => a.Mapping is not null && a.Reachability == MeshReachability.Isolated, "alone, with its port mapped", 20000);
            var code = a.EntryPoints();
            Check(code.Any(entry => entry.Address.Equals(natA.PublicAddress) && entry.Port == a.Mapping!.ExternalPort),
                "the code carries the mapped address, unproven as it is");

            var natB = network.CreateNat(SimulatedNatKind.PortRestricted);
            var socketB = network.BindBehind(natB);
            b = Node(network, socketB, code, new SimulatedPortMapper(natB, socketB));
            Eventually(() => a.Reachability == MeshReachability.Public && b.Reachability == MeshReachability.Public,
                "each proves the other reachable", 20000);
            Eventually(() => a.Table.Get(b.Transport.LocalId) is not null && b.Table.Get(a.Transport.LocalId) is not null,
                "and each is in the other's table: a network of two", 20000);
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b?.DisposeAsync().AsTask().Wait();
        }
    }

    public static void DialBacksCannotBeAimedElsewhere()
    {
        var network = new SimulatedNetwork();
        var backbone = Backbone(network, 3);
        var victim = network.BindPublic();
        var received = 0;
        network.Filter = (_, _, to) =>
        {
            if (to.Address.Equals(victim.LocalEndPoint.Address)) Interlocked.Increment(ref received);
            return true;
        };
        var attacker = Transport(network.BindPublic());
        try
        {
            var session = Wait(attacker.ConnectAsync(MeshRoute.Direct(backbone[0].Transport.LocalEndPoint)))!;
            // The body only names a port; the address is always the requester's own.
            var body = new byte[MeshPackets.ProbeNonceLength + 2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(MeshPackets.ProbeNonceLength), (ushort)victim.LocalEndPoint.Port);
            for (var i = 0; i < 50; i++) attacker.RequestAsync(session, MeshOps.DialBack, body).Wait(2000);
            Thread.Sleep(100);
            Check(received == 0, "no probe ever reached another address");
        }
        finally
        {
            attacker.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void NodesFileBringsANodeBackWithoutSeeds()
    {
        var network = new SimulatedNetwork();
        var backbone = Backbone(network, 4);
        var file = Path.Combine(Path.GetTempPath(), "cubeshelf-nodes-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var options = NodeOptions with { NodesFile = file };
            var socket = network.BindPublic();
            var first = Wait(MeshNode.CreateAsync(socket, options, null, () => network.BindBeside(socket)));
            first.Start(new[] { backbone[0].Transport.LocalEndPoint });
            Eventually(() => first.Table.Count >= 3, "joined", 20000);
            first.DisposeAsync().AsTask().Wait();
            Check(File.Exists(file), "the way back in was kept");

            var socket2 = network.BindPublic();
            var second = Wait(MeshNode.CreateAsync(socket2, options, null, () => network.BindBeside(socket2)));
            second.Start();   // no seeds at all
            Eventually(() => second.Table.Count >= 3, "rejoined from the file alone", 20000);
            Check(second.Transport.LocalId != first.Transport.LocalId, "with a new id: runs are not linkable by id");
            second.DisposeAsync().AsTask().Wait();
        }
        finally
        {
            File.Delete(file);
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }
}
