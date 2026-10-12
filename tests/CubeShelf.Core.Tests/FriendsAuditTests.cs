using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

/// <summary>Serves a document from one host, and cuts the connection mid-body on every other.</summary>
sealed class CutConnectionHandler(string host, string document) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host == host)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(document, Encoding.UTF8, "application/json") });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CutStream()) });
    }

    /// <summary>A body that ends in the middle, the way .NET reports a reset connection.</summary>
    private sealed class CutStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new HttpIOException(HttpRequestError.ResponseEnded);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>What the review of the friends system found, each pinned so it cannot come back.</summary>
static class FriendsAuditTests
{
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cubeshelf-friends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static FriendCodePayload Code(PeerIdentity who, string url = "") => new(who.PublicKey, url);

    public static void ABusyFriendsFileIsNeverReadAsEmpty()
    {
        var root = TempDirectory();
        using var me = PeerIdentity.Create();
        using var ann = PeerIdentity.Create();
        using var bea = PeerIdentity.Create();
        using var cid = PeerIdentity.Create();
        var store = new FriendStore(root);
        Check(store.TryAdd(Code(ann, "https://a.example.test/p.json"), "Ann", me.PublicKey, out _), "Ann added");
        Check(store.TryAdd(Code(bea, "https://b.example.test/p.json"), "Bea", me.PublicKey, out _), "Bea added");
        var before = File.ReadAllText(store.FriendsFile);

        // Something else holds the file -- an antivirus, a backup -- while a friend is added.
        // Windows enforces the lock; elsewhere the share mode is advisory and there is nothing to test.
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(store.FriendsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var refused = false;
                try
                {
                    store.TryAdd(Code(cid), "Cid", me.PublicKey, out _);
                }
                catch (ProfileFileUnavailableException)
                {
                    refused = true;
                }
                Check(refused, "the change is refused, not saved over a list that was never read");
                Check(store.Load().Count == 2, "the screen still shows the friends it knew");

                // A store that never read the file has nothing to stand in for it: nothing is
                // sealed for an empty list, which would tell every friend they were dropped.
                var fresh = new FriendStore(root);
                var reported = false;
                try
                {
                    PresenceRecipients.ForPublication(me, fresh);
                }
                catch (ProfileFileUnavailableException)
                {
                    reported = true;
                }
                Check(reported, "publication waits rather than addressing nobody");
                Check(fresh.Load().Count == 0, "reading for the screen never throws");
            }
            Check(File.ReadAllText(store.FriendsFile) == before, "both friends are still on disk");
            Check(store.TryAdd(Code(cid), "Cid", me.PublicKey, out _) && store.Load().Count == 3, "and the add works once the file is free");
        }

        // A file that is not a list at all is kept aside, not overwritten.
        File.WriteAllText(store.FriendsFile, "{ pas du json");
        using var dan = PeerIdentity.Create();
        var reopened = new FriendStore(root);
        Check(reopened.TryAdd(Code(dan), "Dan", me.PublicKey, out _), "a friend can be added after a damaged file");
        var kept = Directory.GetFiles(root, "friends.json.unreadable-*");
        Check(kept.Length == 1 && File.ReadAllText(kept[0]) == "{ pas du json", "the damaged file is kept for whoever wants it back");
        Directory.Delete(root, true);
    }

    public static void ABusyMessageHistoryIsNeverOverwritten()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = TempDirectory();
        var first = new MessageStore(root);
        Check(first.Send("friend", "salut", DateTimeOffset.UtcNow) is not null, "a message is kept");
        var file = Path.Combine(root, "messages.json");
        var before = File.ReadAllText(file);

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var second = new MessageStore(root);
            second.Send("friend", "pendant le verrou", DateTimeOffset.UtcNow);
            second.MarkRead("friend");
        }
        Check(File.ReadAllText(file) == before, "the history on disk is untouched");

        var third = new MessageStore(root);
        Check(third.Conversation("friend").Single().Text == "salut", "and reads back whole");
        Directory.Delete(root, true);
    }

    public static void AConnectionCutMidBodyStaysWithThatFriend()
    {
        var root = TempDirectory();
        using var me = PeerIdentity.Create();
        using var good = PeerIdentity.Create();
        using var cut = PeerIdentity.Create();
        var friends = new FriendStore(root);
        Check(friends.TryAdd(Code(good, "https://ok.example.test/p.json"), "Bon", me.PublicKey, out _), "good added");
        Check(friends.TryAdd(Code(cut, "https://cut.example.test/p.json"), "Coupé", me.PublicKey, out _), "cut added");

        var document = SealedPresence.ToJson(SealedPresence.Seal(
            good, PresenceComposer.Offline("Bon", 1, DateTimeOffset.UtcNow), new[] { me.PublicKey }));
        using var client = new HttpClient(new CutConnectionHandler("ok.example.test", document));
        using var fetcher = new PresenceFetcher(me, friends, client);

        var outcomes = Wait(fetcher.PollAsync(), 10000);
        Check(outcomes.Count == 2, "both friends have an outcome: the batch survived");
        Check(outcomes.Single(o => o.FriendPublicKey == Convert.ToBase64String(good.PublicKey)).Status == PresenceFetchStatus.Updated, "the working friend is read");
        var broken = outcomes.Single(o => o.FriendPublicKey == Convert.ToBase64String(cut.PublicKey));
        Check(broken.Status == PresenceFetchStatus.Unreachable && broken.IsConnectivityFailure, "the cut one is a failure like any other");
        Directory.Delete(root, true);
    }

    public static void AFriendWhoMovedToTheNetworkIsFoundThere()
    {
        var root = TempDirectory();
        using var me = PeerIdentity.Create();
        using var moved = PeerIdentity.Create();
        var friends = new FriendStore(root);
        Check(friends.TryAdd(Code(moved, "https://old.example.test/p.json"), "Parti", me.PublicKey, out _), "added with an old address");

        var document = SealedPresence.ToJson(SealedPresence.Seal(
            moved, PresenceComposer.Offline("Parti", 50, DateTimeOffset.UtcNow), new[] { me.PublicKey }));
        var askedNetwork = 0;
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound));
        using var fetcher = new PresenceFetcher(me, friends, client)
        {
            NetworkReader = (_, _, _) =>
            {
                Interlocked.Increment(ref askedNetwork);
                return Task.FromResult(new PresenceNetworkRead(document, 50));
            }
        };

        var outcome = Wait(fetcher.PollAsync(), 10000).Single();
        Check(outcome.Status == PresenceFetchStatus.Updated && outcome.Snapshot!.Sequence == 50, "the old address is gone, the network has them: " + outcome.Status);

        // Backing off the dead address does not stop the network being asked.
        var again = Wait(fetcher.PollAsync(), 10000).Single();
        Check(askedNetwork == 2 && again.Status == PresenceFetchStatus.NotModified, "asked again while the address backs off: " + again.Status);
        Directory.Delete(root, true);
    }

    public static void AnOvertakenDocumentDoesNotComeBack()
    {
        var latest = new ConcurrentDictionary<string, PresenceSnapshot>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var playing = PresenceComposer.Offline("Zera", 10, now) with { Status = PresenceStatus.InGame };
        var gone = PresenceComposer.Offline("Zera", 11, now);
        Check(PresenceService.KeepNewest(latest, "k", gone), "the farewell is kept");
        Check(!PresenceService.KeepNewest(latest, "k", playing), "the older 'in game' arriving after it is not");
        Check(latest["k"].Status == PresenceStatus.Offline, "so they read as gone");
        Check(PresenceService.KeepNewest(latest, "k", gone), "the same document again is harmless");
    }

    public static void AFriendsClockIsNotTrustedOverOurs()
    {
        var now = DateTimeOffset.UtcNow;
        var window = PresencePolicy.FreshnessWindow;
        var online = PresenceComposer.Offline("Zera", 1, now) with { Status = PresenceStatus.Online };

        var fast = online with { PublishedAt = now.AddMinutes(20) };
        Check(fast.EffectiveStatus(window, now) == PresenceStatus.Offline, "as published, a fast clock reads as offline");
        Check(fast.AsReceivedAt(now).EffectiveStatus(window, now) == PresenceStatus.Online, "as received, it is online");

        var slow = online with { PublishedAt = now.AddMinutes(-40) };
        Check(slow.AsReceivedAt(now).EffectiveStatus(window, now) == PresenceStatus.Offline,
            "a date in the past is kept from a folder: late and slow look the same there");
        Check(slow.AsDeliveredAt(now).EffectiveStatus(window, now) == PresenceStatus.Online,
            "but straight from them, it was written just now");

        var close = online with { PublishedAt = now.AddSeconds(-30) };
        Check(close.AsDeliveredAt(now).PublishedAt == close.PublishedAt, "clocks that agree keep the author's date");
    }

    public static void AchievementsFollowTheBoxesThatCoverThem()
    {
        var progress = new SharedAchievements("mp4", 1234, "Mario Party 4", "Zera", 10, 5, 50, new[] { 1, 2 }, DateTimeOffset.UtcNow);
        var other = progress with { GameId = "sms", RaGameId = 99, Title = "Sunshine" };
        PresenceInputs Inputs(PresenceSharingOptions sharing, PresenceAvailability availability = PresenceAvailability.Available) =>
            new("Zera", Array.Empty<PresenceGame>(), new[] { "mp4" }, sharing, Availability: availability, Achievements: new[] { progress, other });

        Check(PresenceService.AchievementsToShare(Inputs(new PresenceSharingOptions())).Count == 2, "everything shared: both games");
        Check(PresenceService.AchievementsToShare(Inputs(new PresenceSharingOptions(ShareAchievements: false))).Count == 0, "achievements off: none");
        Check(PresenceService.AchievementsToShare(Inputs(new PresenceSharingOptions(ShareLibrary: false))).Count == 0,
            "library off: none, they name the games");
        Check(PresenceService.AchievementsToShare(Inputs(new PresenceSharingOptions(), PresenceAvailability.Invisible)).Count == 0, "invisible: none");

        var hidden = PresenceService.AchievementsToShare(Inputs(new PresenceSharingOptions(ShareCurrentGame: false)));
        Check(hidden.Count == 1 && hidden[0].GameId == "sms", "current game off: the game being played is left out");
    }

    public static void PausedInvisibleAndDeclinedMeanWhatTheySay()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 4);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindPublic(), "Alice", seeds);
        var bob = new FriendNode(network, network.BindPublic(), "Bob", seeds);
        var carol = new FriendNode(network, network.BindPublic(), "Carol", seeds);
        MeshFriends? restarted = null;
        try
        {
            Eventually(() => alice.Node.Reachability == MeshReachability.Public && bob.Node.Table.Count >= 3 && carol.Node.Table.Count >= 3, "joined", 20000);
            alice.Befriend(bob);
            bob.Befriend(alice);
            var session = Wait(bob.Node.Transport.ConnectAsync(MeshRoute.Direct(alice.Node.Transport.LocalEndPoint)))!;
            Check(Wait(bob.Friends.HelloAsync(session, bob.Store.Load().Single())), "connected");
            Eventually(() => alice.Friends.IsConnected(bob.Key), "both ways", 5000);

            // Bob pauses Alice while the session is up and before the network layer hears of it --
            // the moment a hello in flight used to slip through. Her pushes are not taken.
            PresenceFetchOutcome? received = null;
            bob.Friends.DocumentReceived += outcome => received = outcome;
            bob.Store.Update(alice.Key, friend => friend.Paused = true);
            var (json, sequence) = alice.Document("en pause", 5);
            Wait(alice.Friends.PublishAsync(json, sequence), 30000);
            Thread.Sleep(300);
            Check(received is null, "a paused friend's document is refused");
            bob.Store.Update(alice.Key, friend => friend.Paused = false);

            // Going invisible ends the sessions already open: the other side sees nobody.
            alice.Friends.Invisible = true;
            Eventually(() => !alice.Friends.IsConnected(bob.Key) && !bob.Friends.IsConnected(alice.Key), "invisible closes the session", 5000);

            // A declined request stays declined after a restart.
            Check(Wait(carol.Friends.SendRequestAsync(alice.Identity.PublicKey, "coucou"), 30000), "Carol asks");
            Wait(alice.Friends.CheckMailboxAsync(), 30000);
            Check(alice.Friends.Requests.Count == 1, "the request arrives");
            alice.Friends.Dismiss(alice.Friends.Requests[0]);
            Wait(alice.Friends.DisposeAsync().AsTask(), 10000);

            restarted = new MeshFriends(alice.Node, alice.Identity, alice.Store, () => alice.Code);
            Wait(restarted.CheckMailboxAsync(), 30000);
            Check(restarted.Requests.Count == 0, "and does not come back with the next launch");
        }
        finally
        {
            restarted?.DisposeAsync().AsTask().Wait();
            foreach (var node in new[] { alice, bob, carol }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }

    public static void ARequestEndsWhenTheyAreAddedByCode()
    {
        var network = new SimulatedNetwork();
        var backbone = MeshNodeTests.Backbone(network, 4);
        var seeds = new[] { backbone[0].Transport.LocalEndPoint };
        var alice = new FriendNode(network, network.BindPublic(), "Alice", seeds);
        var carol = new FriendNode(network, network.BindPublic(), "Carol", seeds);
        try
        {
            Eventually(() => alice.Node.Table.Count >= 3 && carol.Node.Table.Count >= 3, "joined", 20000);
            Check(Wait(carol.Friends.SendRequestAsync(alice.Identity.PublicKey), 30000), "Carol asks");
            Wait(alice.Friends.CheckMailboxAsync(), 30000);
            Check(alice.Friends.Requests.Count == 1, "the request arrives");

            // Alice pastes Carol's code instead of answering: the request has nothing left to ask.
            alice.Befriend(carol);
            Check(alice.Friends.Requests.Count == 0, "the request is gone");
        }
        finally
        {
            foreach (var node in new[] { alice, carol }) node.DisposeAsync().AsTask().Wait();
            foreach (var node in backbone) node.DisposeAsync().AsTask().Wait();
        }
    }
}
