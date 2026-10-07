using System.Net;
using System.Security.Cryptography;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

/// <summary>One node of a simulated network: transport, table, records and the lookups over them.</summary>
sealed class TestNode : IAsyncDisposable
{
    public TestNode(SimulatedNetwork network, IMeshSocket socket, bool server, MeshDhtOptions? options = null)
    {
        Network = network;
        Socket = socket;
        var (key, nonce) = NodeKey();
        Transport = new MeshTransport(socket, key, nonce, () => server ? MeshNodeFlags.Server : MeshNodeFlags.None, null, Options());
        Table = new RoutingTable(Transport.LocalId, bucketSize: (options ?? DhtOptions).K);
        Records = new RecordStore { MailboxDifficulty = 6 };
        Dht = new MeshDht(Transport, Table, Records, options ?? DhtOptions) { Serving = server };
        Transport.RequestHandler = (session, message, _) => Dht.HandleAsync(session, message);
        Transport.SessionEstablished += Dht.OnSessionEstablished;
        Transport.Start();
    }

    public static MeshDhtOptions DhtOptions => new() { ProofDifficulty = Difficulty, K = 8, Replication = 6, RequestTimeout = TimeSpan.FromSeconds(1) };

    public SimulatedNetwork Network { get; }
    public IMeshSocket Socket { get; }
    public MeshTransport Transport { get; }
    public RoutingTable Table { get; }
    public RecordStore Records { get; }
    public MeshDht Dht { get; }
    public NodeId Id => Transport.LocalId;
    public IPEndPoint Endpoint => Transport.LocalEndPoint;

    public static List<TestNode> Many(SimulatedNetwork network, int count)
    {
        var nodes = Enumerable.Range(0, count).Select(_ => new TestNode(network, network.BindPublic(), server: true)).ToList();
        // Everyone joins through the first node, a few at a time, as people would.
        foreach (var batch in nodes.Skip(1).Chunk(6))
            Wait(Task.WhenAll(batch.Select(node => node.Dht.BootstrapAsync(new[] { nodes[0].Endpoint }))), 60000);
        // One more round, so the early joiners also learn about the late ones.
        Wait(Task.WhenAll(nodes.Select(node => node.Dht.RefreshAsync(TimeSpan.Zero))), 60000);
        return nodes;
    }

    public ValueTask DisposeAsync() => Transport.DisposeAsync();
}

static class MeshDhtTests
{
    public static void LookupsConvergeOnTheClosestNodes()
    {
        var network = new SimulatedNetwork();
        var nodes = TestNode.Many(network, 30);
        try
        {
            Check(nodes.All(node => node.Table.Count >= 6), "every table filled: " + string.Join(",", nodes.Select(node => node.Table.Count)));
            for (var trial = 0; trial < 5; trial++)
            {
                var target = NodeId.Random();
                var asker = nodes[Random.Shared.Next(nodes.Count)];
                var found = Wait(asker.Dht.FindClosestAsync(target));
                var truth = nodes.Where(node => node != asker).Select(node => node.Id).OrderBy(id => id.Xor(target)).Take(8).ToArray();
                Check(found.Count > 0 && found[0].Id == truth[0], $"the closest node is found (trial {trial})");
                var overlap = found.Select(contact => contact.Id).Intersect(truth).Count();
                Check(overlap >= 7, $"nearly all of the 8 closest are found ({overlap}/8)");
            }
        }
        finally
        {
            foreach (var node in nodes) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void RecordsAreStoredFoundAndUpdated()
    {
        var network = new SimulatedNetwork();
        var nodes = TestNode.Many(network, 20);
        try
        {
            using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = DateTimeOffset.UtcNow;
            var first = MeshRecord.CreateSigned(owner, 1, now.AddHours(2), "version one"u8.ToArray());
            var stored = Wait(nodes[3].Dht.StoreAsync(first));
            Check(stored >= 4, $"stored on several holders ({stored})");

            var reader = nodes[^1];
            var found = Wait(reader.Dht.FindValueAsync(first.Locator));
            Check(found.Count == 1 && found[0].Sequence == 1 && found[0].Payload.AsSpan().SequenceEqual("version one"u8), "found from elsewhere");

            // One holder misses the update and keeps serving the old copy: the others outvote it.
            var holders = nodes.Where(node => node.Records.Get(first.Locator, now).Count > 0).ToList();
            var frozen = holders[0];
            network.Filter = (datagram, _, to) => !(to.Equals(frozen.Endpoint) && datagram[1] == (byte)MeshPacketType.Transport);
            var second = MeshRecord.CreateSigned(owner, 2, now.AddHours(2), "version two"u8.ToArray());
            Wait(nodes[5].Dht.StoreAsync(second));
            network.Filter = null;
            var latest = Wait(reader.Dht.FindValueAsync(first.Locator));
            Check(latest.Count == 1 && latest[0].Sequence == 2, "the newest sequence wins over a stale holder");

            // And an old copy pushed back in is refused by holders that have the new one.
            var holder = nodes.First(node => node.Records.Get(first.Locator, now).FirstOrDefault()?.Sequence == 2);
            Check(holder.Records.Put(first, "replay", now) == StoreResult.Stale, "a replayed older record is refused");
        }
        finally
        {
            foreach (var node in nodes) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void HoldersRefuseWhatDoesNotVerify()
    {
        var store = new RecordStore { MailboxDifficulty = 6 };
        var now = DateTimeOffset.UtcNow;
        using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var good = MeshRecord.CreateSigned(owner, 5, now.AddHours(1), new byte[] { 1, 2, 3 });
        Check(store.Put(good, "a", now) == StoreResult.Stored, "a good record");
        Check(store.Put(good, "a", now) == StoreResult.Unchanged, "the same again is a no-op");

        var tampered = good with { Payload = new byte[] { 1, 2, 4 } };
        Check(store.Put(tampered with { Sequence = 6 }, "a", now) == StoreResult.Invalid, "a changed payload breaks the signature");

        // Someone else's key cannot write at the owner's locator, even with a valid signature of their own.
        var squatter = MeshRecord.CreateSigned(other, 9, now.AddHours(1), new byte[] { 6 }) with { Locator = good.Locator };
        Check(store.Put(squatter, "b", now) == StoreResult.Invalid, "the locator belongs to its key");

        Check(store.Put(MeshRecord.CreateSigned(owner, 7, now.AddDays(3), new byte[] { 1 }), "a", now) == StoreResult.Invalid, "no record lives past a day");
        Check(store.Put(MeshRecord.CreateSigned(owner, 7, now.AddSeconds(-1), new byte[] { 1 }), "a", now) == StoreResult.Invalid, "nor is an expired one taken");
        Check(store.Put(MeshRecord.CreateSigned(owner, 8, now.AddHours(1), new byte[MeshRecord.MaximumPayload + 1]), "a", now) == StoreResult.Invalid, "nor an oversized one");

        // A mailbox entry without its work is refused; with it, accepted; a flood fills one box and no more.
        var mailbox = NodeId.Random();
        var lazy = MeshRecord.CreateMailboxEntry(other, mailbox, now.AddHours(1), new byte[] { 1 }, difficulty: 0);
        var lazyIsCheap = NodeProof.LeadingZeroBits(MeshCrypto.Hash("cubeshelf-mailbox-work-v1", mailbox.ToBytes(), lazy.SignerKey, SHA256.HashData(lazy.Payload),
            BigEndian(lazy.Sequence))) < 6;
        if (lazyIsCheap) Check(store.Put(lazy, "c", now) == StoreResult.Invalid, "a mailbox entry without work");
        var senders = Enumerable.Range(0, 40).Select(_ => ECDsa.Create(ECCurve.NamedCurves.nistP256)).ToArray();
        foreach (var sender in senders)
            store.Put(MeshRecord.CreateMailboxEntry(sender, mailbox, now.AddHours(1), new byte[] { 2 }, difficulty: 6), "flood", now);
        Check(store.Get(mailbox, now).Count == store.MailboxEntries, "a mailbox holds a bounded number of entries");
        foreach (var sender in senders) sender.Dispose();

        // Quotas per source: one neighbourhood cannot fill a holder.
        var small = new RecordStore(perSourceRecords: 3) { MailboxDifficulty = 6 };
        var keys = Enumerable.Range(0, 5).Select(_ => ECDsa.Create(ECCurve.NamedCurves.nistP256)).ToArray();
        var results = keys.Select(key => small.Put(MeshRecord.CreateSigned(key, 1, now.AddHours(1), new byte[] { 1 }), "11.0.0", now)).ToArray();
        Check(results.Count(result => result == StoreResult.Stored) == 3 && results.Count(result => result == StoreResult.Full) == 2, "per-source quota");
        Check(small.Put(MeshRecord.CreateSigned(ECDsa.Create(ECCurve.NamedCurves.nistP256), 1, now.AddHours(1), new byte[] { 1 }), "12.0.0", now) == StoreResult.Stored,
            "another source still gets in");
        foreach (var key in keys) key.Dispose();
    }

    public static void RecordsOutliveTheirHolders()
    {
        var network = new SimulatedNetwork();
        var nodes = TestNode.Many(network, 24);
        var gone = new List<TestNode>();
        try
        {
            using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = DateTimeOffset.UtcNow;
            var record = MeshRecord.CreateSigned(owner, 1, now.AddHours(6), "still here"u8.ToArray());
            Wait(nodes[0].Dht.StoreAsync(record));

            // The owner goes offline; most holders leave.
            var holders = nodes.Where(node => node.Records.Get(record.Locator, now).Count > 0).ToList();
            Check(holders.Count >= 4, $"replicated ({holders.Count})");
            foreach (var holder in holders.Take(holders.Count - 2))
            {
                holder.DisposeAsync().AsTask().Wait();
                gone.Add(holder);
            }

            // The holders left pass it on to the nodes now closest.
            foreach (var holder in holders.Skip(holders.Count - 2))
                Wait(holder.Dht.RepublishAsync(TimeSpan.Zero), 60000);
            foreach (var holder in holders.Skip(holders.Count - 2))
            {
                holder.DisposeAsync().AsTask().Wait();
                gone.Add(holder);
            }

            var alive = nodes.Except(gone).ToList();
            var found = Wait(alive[^1].Dht.FindValueAsync(record.Locator), 60000);
            Check(found.Count == 1 && found[0].Payload.AsSpan().SequenceEqual("still here"u8), "found after every original holder left");
        }
        finally
        {
            foreach (var node in nodes.Except(gone)) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void NodesBehindNatUseTheTable()
    {
        var network = new SimulatedNetwork();
        var nodes = TestNode.Many(network, 12);
        var nat = network.CreateNat(SimulatedNatKind.PortRestricted);
        var client = new TestNode(network, network.BindBehind(nat), server: false);
        var otherNat = network.CreateNat(SimulatedNatKind.Symmetric);
        var otherClient = new TestNode(network, network.BindBehind(otherNat), server: false);
        try
        {
            Check(Wait(client.Dht.BootstrapAsync(new[] { nodes[0].Endpoint })) >= 6, "a client behind NAT learns the table");
            Wait(otherClient.Dht.BootstrapAsync(new[] { nodes[5].Endpoint }));

            using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var record = MeshRecord.CreateSigned(owner, 1, DateTimeOffset.UtcNow.AddHours(1), "from behind a router"u8.ToArray());
            Check(Wait(client.Dht.StoreAsync(record)) >= 4, "and stores into it");
            var found = Wait(otherClient.Dht.FindValueAsync(record.Locator));
            Check(found.Count == 1, "another client, behind another kind of router, finds it");

            // Clients never end up in anyone's table: nobody could reach them there.
            Check(nodes.All(node => node.Table.Get(client.Id) is null && node.Table.Get(otherClient.Id) is null), "clients are not routed to");
        }
        finally
        {
            client.DisposeAsync().AsTask().Wait();
            otherClient.DisposeAsync().AsTask().Wait();
            foreach (var node in nodes) node.DisposeAsync().AsTask().Wait();
        }
    }

    /// <summary>
    /// A hostile node answers lookups with made-up contacts: ids it paid for, at addresses where
    /// nobody listens, and private addresses aimed at the asker's own network. None of them gets
    /// into the asker's table, and no packet is ever sent to the private ones.
    /// </summary>
    public static void LiesInAnswersNeverEnterTheTable()
    {
        var network = new SimulatedNetwork();
        var nodes = TestNode.Many(network, 10);
        var liar = new TestNode(network, network.BindPublic(), server: true);
        var asker = new TestNode(network, network.BindPublic(), server: true);
        try
        {
            var fakes = Enumerable.Range(0, 8).Select(index =>
            {
                using var key = MeshCrypto.NewAgreementKey();
                var publicKey = MeshCrypto.PublicKeyOf(key);
                var address = index < 4 ? new IPAddress(new byte[] { 11, 250, (byte)index, 1 }) : new IPAddress(new byte[] { 192, 168, 1, (byte)(10 + index) });
                return new NodeContact(publicKey, NodeProof.Solve(publicKey, Difficulty), new IPEndPoint(address, 40000));
            }).ToList();

            liar.Transport.RequestHandler = (session, message, _) =>
            {
                if (message.Op is not (MeshOps.FindNode or MeshOps.FindValue)) return liar.Dht.HandleAsync(session, message);
                var writer = new MeshWriter();
                if (message.Op == MeshOps.FindValue) writer.U8(0);
                writer.U8((byte)fakes.Count);
                foreach (var fake in fakes) fake.WriteTo(writer);
                return Task.FromResult<byte[]?>(writer.ToArray());
            };

            var privateTouched = false;
            network.Filter = (_, _, to) =>
            {
                if (to.Address.GetAddressBytes()[0] == 192) privateTouched = true;
                return true;
            };
            // Nobody listens at the invented addresses, so the network never delivers there: what
            // the asker's own socket sent is the count.
            var askerSocket = (SimulatedSocket)asker.Socket;
            long SentToInvented()
            {
                lock (askerSocket.SentTo)
                    return askerSocket.SentTo.Where(entry => entry.Key.Address.GetAddressBytes() is [11, 250, ..]).Sum(entry => entry.Value);
            }

            Wait(asker.Dht.BootstrapAsync(new[] { liar.Endpoint, nodes[0].Endpoint }), 60000);
            // Let the handshakes the bootstrap started run out, then see what later lookups add.
            Thread.Sleep(3500);
            var paidOnce = SentToInvented();
            for (var i = 0; i < 3; i++) Wait(asker.Dht.FindClosestAsync(NodeId.Random()), 60000);
            // The invented addresses cost a timeout once, not on every lookup. Counted in bytes
            // sent rather than timed: a slow test machine must not decide it.
            Check(paidOnce > 0, "the lies were tried once");
            Check(SentToInvented() == paidOnce, $"later lookups never try them again ({SentToInvented() - paidOnce} more bytes)");

            var table = asker.Table.All().Select(contact => contact.Id).ToHashSet();
            Check(fakes.All(fake => !table.Contains(fake.Id)), "no invented contact entered the table");
            Check(!privateTouched, "no handshake was aimed at a private address");
            Check(asker.Table.Count >= 6, "the honest part of the network was still learnt");
        }
        finally
        {
            liar.DisposeAsync().AsTask().Wait();
            asker.DisposeAsync().AsTask().Wait();
            foreach (var node in nodes) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TableLimitsOneNeighbourhood()
    {
        var table = new RoutingTable(NodeId.Random(), bucketSize: 16);
        var accepted = 0;
        for (var index = 0; index < 50; index++)
        {
            using var key = MeshCrypto.NewAgreementKey();
            var publicKey = MeshCrypto.PublicKeyOf(key);
            var contact = new NodeContact(publicKey, NodeProof.Solve(publicKey, 4), new IPEndPoint(new IPAddress(new byte[] { 11, 9, 9, (byte)(index % 3 + 1) }), 40000 + index));
            if (table.Observe(contact) is RoutingResult.Added) accepted++;
        }
        // 50 ids from one /24 land in a handful of buckets; each bucket takes two of them at most,
        // and each address two at most -- so three addresses give six entries at the very most.
        Check(accepted <= 6, $"one neighbourhood cannot flood the table ({accepted} entries)");
        Check(table.Observe(new NodeContact(table.All()[0].PublicKey, table.All()[0].Nonce, new IPEndPoint(IPAddress.Parse("192.168.1.5"), 1))) == RoutingResult.Refused,
            "private addresses are refused");
    }

    private static byte[] BigEndian(ulong value)
    {
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }
}
