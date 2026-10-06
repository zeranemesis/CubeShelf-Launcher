using System.Text;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

/// <summary>A whole CubeShelf as far as friends go: identity, friends list, node, and the friend layer.</summary>
sealed class FriendNode : IAsyncDisposable
{
    private readonly string _directory;

    public FriendNode(SimulatedNetwork network, SimulatedSocket socket, string name, IEnumerable<System.Net.IPEndPoint> seeds)
    {
        Name = name;
        _directory = Path.Combine(Path.GetTempPath(), "cubeshelf-meshfriends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Identity = PeerIdentity.Create();
        Store = new FriendStore(_directory);
        Node = MeshNodeTests.Node(network, socket, seeds);
        Friends = new MeshFriends(Node, Identity, Store, () => Code);
        Friends.Start();
    }

    public string Name { get; }
    public PeerIdentity Identity { get; }
    public FriendStore Store { get; }
    public MeshNode Node { get; }
    public MeshFriends Friends { get; }
    public string Key => Convert.ToBase64String(Identity.PublicKey);
    public string Code => FriendCode.EncodeForNetwork(Identity.PublicKey, Name, Node.EntryPoints());

    public void Befriend(FriendNode other)
    {
        Check(FriendCode.TryDecode(other.Code, out var payload, out var error) && payload is not null, "code decodes: " + error);
        Check(Store.TryAdd(payload!, other.Name, Identity.PublicKey, out var addError), "added: " + addError);
        Friends.FriendsChanged();
    }

    /// <summary>A sealed document saying <paramref name="status"/>, for our friends, as the presence service would build it.</summary>
    public (string Json, long Sequence) Document(string status, long sequence)
    {
        var snapshot = new PresenceSnapshot(PresenceSnapshot.CurrentVersion, Name, DateTimeOffset.UtcNow, sequence, PresenceStatus.Online,
            null, status, Array.Empty<SharedGame>(), Array.Empty<SharedMod>(), Mesh: Node.Address.ToBase64());
        var envelope = SealedPresence.Seal(Identity, snapshot, PresenceRecipients.ForPublication(Identity, Store));
        return (SealedPresence.ToJson(envelope), sequence);
    }

    public PresenceSnapshot? Open(FriendNode author, string? json)
    {
        if (json is null) return null;
        return SealedPresence.TryOpen(Identity, author.Identity.PublicKey, SealedPresence.FromJson(json), out var snapshot) ? snapshot : null;
    }

    public async ValueTask DisposeAsync()
    {
        await Friends.DisposeAsync();
        await Node.DisposeAsync();
        Identity.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }
}

static class MeshFriendsTests
{
    public static void FriendsFindEachOtherAndStrangersDoNot()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 6);
        var seeds = new[] { backbone[2].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), "Alice", seeds);
        var bob = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.Symmetric)), "Bob", seeds);
        var eve = new FriendNode(network, network.BindPublic(), "Eve", seeds);
        try
        {
            Eventually(() => alice.Node.Table.Count >= 4 && bob.Node.Table.Count >= 4 && eve.Node.Table.Count >= 4, "everyone joined", 20000);
            alice.Befriend(bob);
            bob.Befriend(alice);
            // Eve has Alice's code -- it was pasted somewhere public -- and adds her.
            eve.Befriend(alice);

            var (json, sequence) = alice.Document("au salon", 10);
            var published = Wait(alice.Friends.PublishAsync(json, sequence), 30000);
            Check(published.Succeeded, "published: " + published.Error);

            var bobsAlice = bob.Store.Load().Single();
            var read = Wait(bob.Friends.ReadAsync(bobsAlice, alice.Identity.PublicKey), 30000);
            Check(read.Sequence == 10, $"Bob found today's document ({read.Sequence})");
            Check(bob.Open(alice, read.Json)?.CurrentGameTitle == "au salon", "and it opens for him");

            // Eve has the code, so she knows the key -- but Alice keeps no pointer for her.
            var evesAlice = eve.Store.Load().Single();
            var spied = Wait(eve.Friends.ReadAsync(evesAlice, alice.Identity.PublicKey), 30000);
            Check(spied.Json is null, "a stranger holding the code finds nothing at all");

            // What the holders store says nothing about whose it is: no identity key in any record.
            var everything = backbone.SelectMany(node => node.Records.NotRefreshedSince(TimeSpan.Zero, DateTimeOffset.UtcNow.AddMinutes(1))).ToList();
            Check(everything.Count > 0, "records are spread over the backbone");
            var aliceKey = alice.Identity.PublicKey;
            Check(everything.All(record => !record.SignerKey.AsSpan().SequenceEqual(aliceKey) &&
                                           record.Payload.AsSpan().IndexOf(aliceKey) < 0 &&
                                           !Encoding.UTF8.GetString(record.Payload).Contains("Alice")),
                "no holder can tell the records are Alice's");
        }
        finally
        {
            foreach (var node in new[] { alice, bob, eve }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void OnlineFriendsTalkDirectly()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 5);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), "Alice", seeds);
        var bob = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), "Bob", seeds);
        try
        {
            Eventually(() => alice.Node.RelayContacts.Count > 0 && bob.Node.RelayContacts.Count > 0, "both relayed", 20000);
            alice.Befriend(bob);
            bob.Befriend(alice);

            var (first, seq1) = alice.Document("premier", 1);
            Wait(alice.Friends.PublishAsync(first, seq1), 30000);

            // Bob reads it from the table: that teaches him where Alice can be reached.
            var fetcher = new PresenceFetcher(bob.Identity, bob.Store) { NetworkReader = (friend, key, token) => bob.Friends.ReadAsync(friend, key, token) };
            var outcome = Wait(fetcher.FetchAsync(bob.Store.Load().Single()), 30000);
            Check(outcome.Status == PresenceFetchStatus.Updated && outcome.Snapshot!.CurrentGameTitle == "premier", "read through the network: " + outcome.Status);
            Check(bob.Store.Load().Single().MeshAddress is { Length: > 0 }, "Alice's network address was learnt from her sealed document");

            // Then he connects, and both prove who they are.
            Wait(bob.Friends.ConnectNowAsync(), 30000);
            Eventually(() => bob.Friends.IsConnected(alice.Key) && alice.Friends.IsConnected(bob.Key), "a proven direct session, both ways", 15000);

            // From now on Alice's changes reach Bob at once, pushed, not polled.
            PresenceFetchOutcome? pushed = null;
            bob.Friends.DocumentReceived += received => pushed = received;
            var (second, seq2) = alice.Document("deuxième", 2);
            Wait(alice.Friends.PublishAsync(second, seq2), 30000);
            Eventually(() => pushed?.Snapshot?.CurrentGameTitle == "deuxième", "pushed over the session", 5000);

            // A replay of the older document over the same session changes nothing.
            pushed = null;
            var session = alice.Node.Transport.Sessions.First(s => s.FriendKey == bob.Key);
            alice.Node.Transport.Notify(session, MeshOps.FriendDocument, Encoding.UTF8.GetBytes(first));
            Thread.Sleep(300);
            Check(pushed is null, "an old document pushed again is refused");

            // Removing a friend ends the session at once.
            alice.Store.Remove(bob.Key);
            alice.Friends.FriendsChanged();
            Eventually(() => !bob.Friends.IsConnected(alice.Key), "removed: the session is gone", 5000);
        }
        finally
        {
            foreach (var node in new[] { alice, bob }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    /// <summary>
    /// The identity proof is bound to the session it was made in. Eve, who saw nothing she can use,
    /// cannot pass for Bob; and a proof lifted from one session is worthless in another.
    /// </summary>
    public static void FriendProofsCannotBeForgedOrReplayed()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 3);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindPublic(), "Alice", seeds);
        var bob = new FriendNode(network, network.BindPublic(), "Bob", seeds);
        var eve = Transport(network.BindPublic());
        try
        {
            Eventually(() => alice.Node.Reachability == MeshReachability.Public, "alice reachable", 20000);
            alice.Befriend(bob);
            bob.Befriend(alice);

            // Bob's genuine hello, captured from his own session with Alice.
            var bobSession = Wait(bob.Node.Transport.ConnectAsync(MeshRoute.Direct(alice.Node.Transport.LocalEndPoint)))!;
            var genuine = Wait(bob.Friends.HelloAsync(bobSession, bob.Store.Load().Single()));
            Check(genuine, "Bob is recognised");

            // Eve claims Bob's key with a proof she made up.
            var eveSession = Wait(eve.ConnectAsync(MeshRoute.Direct(alice.Node.Transport.LocalEndPoint)))!;
            var forged = new MeshWriter().Fixed(bob.Identity.PublicKey).Fixed(RandomBytes(32)).ToArray();
            Check(Wait(eve.RequestAsync(eveSession, MeshOps.FriendHello, forged, timeout: TimeSpan.FromSeconds(1))) is null, "a made-up proof gets no answer");

            // Eve replays a proof Bob made for another session: the handshake hash differs.
            using var pair = new PairKeyHolder(bob.Identity, alice.Identity.PublicKey);
            var captured = new MeshWriter().Fixed(bob.Identity.PublicKey)
                .Fixed(System.Security.Cryptography.HMACSHA256.HashData(pair.Key,
                    MeshCrypto.Hash("cubeshelf-mesh-friend-v1", new[] { (byte)'I' }, bobSession.HandshakeHash)))
                .ToArray();
            Check(Wait(eve.RequestAsync(eveSession, MeshOps.FriendHello, captured, timeout: TimeSpan.FromSeconds(1))) is null,
                "a proof lifted from another session gets no answer");
            Check(alice.Node.Transport.Sessions.Where(s => s.RemoteId == eve.LocalId).All(s => s.FriendKey is null),
                "Eve's session is never taken for Bob's");

            // And someone Alice never added gets no answer either, even with a valid proof of their own.
            using var stranger = PeerIdentity.Create();
            using var strangerPair = new PairKeyHolder(stranger, alice.Identity.PublicKey);
            var strangerHello = new MeshWriter().Fixed(stranger.PublicKey)
                .Fixed(System.Security.Cryptography.HMACSHA256.HashData(strangerPair.Key,
                    MeshCrypto.Hash("cubeshelf-mesh-friend-v1", new[] { (byte)'I' }, eveSession.HandshakeHash)))
                .ToArray();
            Check(Wait(eve.RequestAsync(eveSession, MeshOps.FriendHello, strangerHello, timeout: TimeSpan.FromSeconds(1))) is null,
                "a stranger's genuine proof gets no answer: Alice says nothing to people she did not add");
        }
        finally
        {
            eve.DisposeAsync().AsTask().Wait();
            foreach (var node in new[] { alice, bob }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void RequestsArriveAndCannotBeForged()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 5);
        var seeds = new[] { backbone[1].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.PortRestricted)), "Alice", seeds);
        var carol = new FriendNode(network, network.BindBehind(network.CreateNat(SimulatedNatKind.FullCone)), "Carol", seeds);
        var eve = new FriendNode(network, network.BindPublic(), "Eve", seeds);
        try
        {
            Eventually(() => alice.Node.Table.Count >= 3 && carol.Node.Table.Count >= 3 && eve.Node.Table.Count >= 3, "joined", 20000);
            Check(Wait(carol.Friends.SendRequestAsync(alice.Identity.PublicKey, "C'est Carol, du club !"), 30000), "Carol's request was stored");

            // Eve drops one in Carol's name: Carol's code, but Eve cannot make Carol's proof.
            var eveAsCarol = new FriendNode(network, network.BindPublic(), "Carol", seeds);
            try
            {
                Eventually(() => eveAsCarol.Node.Table.Count >= 3, "impostor joined", 20000);
                // The impostor's own code would name its own key; it pastes Carol's instead.
                var forgedCode = carol.Code;
                var impostor = new MeshFriends(eveAsCarol.Node, eveAsCarol.Identity, eveAsCarol.Store, () => forgedCode);
                Wait(impostor.SendRequestAsync(alice.Identity.PublicKey, "c'est vraiment moi"), 30000);
                impostor.DisposeAsync().AsTask().Wait();
            }
            finally
            {
                eveAsCarol.DisposeAsync().AsTask().Wait();
            }

            Wait(alice.Friends.CheckMailboxAsync(), 30000);
            var requests = alice.Friends.Requests;
            Check(requests.Count == 1, $"exactly one request: the genuine one ({requests.Count})");
            Check(requests[0].Handle.StartsWith("Carol#") && requests[0].Note == "C'est Carol, du club !", "from Carol, with her note");
            Check(requests[0].PublicKey == carol.Key, "carrying Carol's real key");

            // Accepting it: Alice adds Carol, and the request is gone.
            Check(alice.Store.TryAdd(requests[0].Code, "Carol", alice.Identity.PublicKey, out _), "accepted");
            alice.Friends.Dismiss(requests[0]);
            Wait(alice.Friends.CheckMailboxAsync(), 30000);
            Check(alice.Friends.Requests.Count == 0, "and not shown again");

            // Nobody else can read what was in Alice's mailbox.
            var entries = backbone.SelectMany(node => node.Records.NotRefreshedSince(TimeSpan.Zero, DateTimeOffset.UtcNow.AddMinutes(1)))
                .Where(record => record.Kind == MeshRecordKind.Mailbox).ToList();
            Check(entries.Count > 0 && entries.All(entry => !Encoding.UTF8.GetString(entry.Payload).Contains("Carol")), "mailbox entries are opaque to their holders");
        }
        finally
        {
            foreach (var node in new[] { alice, carol, eve }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void FriendCodesCarryEntryPointsAndRefusePrivateOnes()
    {
        using var identity = PeerIdentity.Create();
        var entries = new[]
        {
            new System.Net.IPEndPoint(System.Net.IPAddress.Parse("11.2.3.4"), 47914),
            new System.Net.IPEndPoint(System.Net.IPAddress.Parse("2a01:e0a::1"), 47914),
            new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.10"), 47914)
        };
        var code = FriendCode.EncodeForNetwork(identity.PublicKey, "Zera", entries);
        Check(code.StartsWith("CSF3-"), "a version-3 code");
        Check(FriendCode.TryDecode("  " + code + "\n", out var payload, out var error) && payload is not null, "decodes: " + error);
        Check(payload!.DisplayName == "Zera" && payload.PresenceUrl.Length == 0, "pseudo, and no address needed");
        Check(payload.Seeds.Count == 2, "the private address never made it in");
        Check(FriendCode.TryFind("bla " + code + " bla", out var found) && found!.PublicKey.AsSpan().SequenceEqual(identity.PublicKey), "found inside a message");

        var tampered = code[..^3] + (code[^3] == 'A' ? 'B' : 'A') + code[^2..];
        Check(!FriendCode.TryDecode(tampered, out _, out _), "a changed character is caught");
    }

    private sealed class PairKeyHolder : IDisposable
    {
        public PairKeyHolder(PeerIdentity identity, byte[] other) => Key = identity.DeriveSharedKey(other);
        public byte[] Key { get; }
        public void Dispose() => System.Security.Cryptography.CryptographicOperations.ZeroMemory(Key);
    }
}
