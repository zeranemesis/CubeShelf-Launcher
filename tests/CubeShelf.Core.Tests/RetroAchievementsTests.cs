using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CubeShelf.Core.Achievements;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;
using static MeshKit;

/// <summary>Answers like retroachievements.org would, and remembers what it was asked.</summary>
sealed class FakeRetroAchievements : HttpMessageHandler
{
    private readonly Func<string, (HttpStatusCode, string)> _answer;

    public FakeRetroAchievements(Func<string, (HttpStatusCode, string)> answer) => _answer = answer;

    public string? LastBody { get; private set; }
    public string? LastUserAgent { get; private set; }
    public Uri? LastUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastUserAgent = request.Headers.UserAgent.ToString();
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var (status, body) = _answer(LastBody ?? "");
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

static class RetroAchievementsTests
{
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cubeshelf-ra-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void TheTokenIsKeptEncryptedAndNeverThePassword()
    {
        var directory = TempDirectory();
        try
        {
            var store = new RetroAchievementsAccountStore(directory);
            Check(store.Load() is null, "nothing yet");
            store.Save(new RetroAchievementsSession("Zera", "AbCdEf0123456789", DateTimeOffset.UtcNow));

            var loaded = store.Load(out var rejected);
            Check(loaded is { User: "Zera", Token: "AbCdEf0123456789" } && !rejected, "round trip");
            var onDisk = File.ReadAllText(store.FilePath);
            if (OperatingSystem.IsWindows())
                Check(!onDisk.Contains("AbCdEf0123456789") && onDisk.Contains("dpapi1:"), "on Windows the token is encrypted for the account");

            store.MarkRejected();
            Check(store.Load(out rejected) is not null && rejected, "a session the server refused is marked, not lost");
            store.Save(new RetroAchievementsSession("Zera", "Fresh0123456789A", DateTimeOffset.UtcNow));
            Check(store.Load(out rejected) is { Token: "Fresh0123456789A" } && !rejected, "logging in again clears the mark");

            // Odd values never get saved -- they would go into a game's environment.
            foreach (var (user, token) in new[] { ("Ze ra", "abc"), ("Zera", "tok en"), ("Zera", ""), ("", "abc"), ("Zera", new string('a', 500)) })
            {
                try
                {
                    store.Save(new RetroAchievementsSession(user, token, DateTimeOffset.UtcNow));
                    throw new InvalidOperationException($"Échec : accepted {user}/{token}");
                }
                catch (ArgumentException)
                {
                }
            }

            store.Clear();
            Check(store.Load() is null && !File.Exists(store.FilePath), "logged out: nothing left");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public static void LoginTalksToTheServerOnceAndReadsItsAnswer()
    {
        var fake = new FakeRetroAchievements(body => body.Contains("p=right")
            ? (HttpStatusCode.OK, "{\"Success\":true,\"User\":\"Zera\",\"Token\":\"T0k3nT0k3nT0k3n1\",\"Score\":280,\"SoftcoreScore\":0}")
            : (HttpStatusCode.Unauthorized, "{\"Success\":false,\"Status\":401,\"Code\":\"invalid_credentials\",\"Error\":\"Invalid user/password combination.\"}"));
        var login = new RetroAchievementsLogin(new HttpClient(fake), "CubeShelf/0.10.0 (Windows)");

        var ok = Wait(login.LoginAsync("zera", "right"));
        Check(ok.Succeeded && ok.Session is { User: "Zera", Token: "T0k3nT0k3nT0k3n1" } && ok.Score == 280, "a good password gives a session, named as the server spells it");
        Check(fake.LastUri == RetroAchievementsLogin.Endpoint, "to retroachievements.org, nowhere else");
        Check(fake.LastBody!.Contains("r=login2") && fake.LastBody.Contains("u=zera"), "the games' own login request");
        Check(fake.LastUserAgent!.StartsWith("CubeShelf/0.10.0"), "an identifiable user agent");

        var bad = Wait(login.LoginAsync("zera", "wrong"));
        Check(!bad.Succeeded && bad.Error!.Contains("incorrect"), "a wrong password is said plainly");

        // Answers that do not hold together give no session.
        foreach (var answer in new[]
        {
            "<html>maintenance</html>",
            "{\"Success\":true,\"User\":\"Zera\"}",
            "{\"Success\":true,\"User\":\"Zera\",\"Token\":\"not a token!\"}",
            "{\"Success\":true,\"User\":\"Ze ra\",\"Token\":\"T0k3n\"}",
            "{\"Success\":\"yes\",\"User\":\"Zera\",\"Token\":\"T0k3n\"}",
            new string('x', 200_000)
        })
        {
            var odd = new RetroAchievementsLogin(new HttpClient(new FakeRetroAchievements(_ => (HttpStatusCode.OK, answer))), "CubeShelf/0.10.0 (Windows)");
            var result = Wait(odd.LoginAsync("zera", "right"));
            Check(!result.Succeeded && result.Session is null, "no session from: " + answer[..Math.Min(30, answer.Length)]);
        }

        Check(!Wait(login.LoginAsync("ze ra", "right")).Succeeded, "a user name that cannot exist is not even sent");
    }

    public static void TheGameIsHeardButNotBelieved()
    {
        var directory = TempDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            void Say(object json) => File.WriteAllText(Path.Combine(directory, AchievementsBridge.SessionFile), System.Text.Json.JsonSerializer.Serialize(json));

            Say(new { schema = 1, @event = "login", user = "Zera", token = "T0k3nT0k3nT0k3n1", at = now.ToUnixTimeSeconds() });
            var heard = AchievementsBridge.TakeSessionEvent(directory, now);
            Check(heard is { Kind: RetroAchievementsEventKind.Login, User: "Zera", Token: "T0k3nT0k3nT0k3n1" }, "a login from the game");
            Check(!File.Exists(Path.Combine(directory, AchievementsBridge.SessionFile)), "read once, then gone");
            Check(AchievementsBridge.TakeSessionEvent(directory, now) is null, "and not heard twice");

            Say(new { schema = 1, @event = "login", user = "Zera", token = "T0k3n", at = now.AddHours(-2).ToUnixTimeSeconds() });
            Check(AchievementsBridge.TakeSessionEvent(directory, now) is null, "an old word is not acted on");
            Say(new { schema = 1, @event = "login", user = "Zera", token = "bad token", at = now.ToUnixTimeSeconds() });
            Check(AchievementsBridge.TakeSessionEvent(directory, now) is null, "a token that is not one is refused");
            Say(new { schema = 1, @event = "rejected", at = now.ToUnixTimeSeconds() });
            Check(AchievementsBridge.TakeSessionEvent(directory, now)?.Kind == RetroAchievementsEventKind.Rejected, "a refused session");
            File.WriteAllText(Path.Combine(directory, AchievementsBridge.SessionFile), "{not json");
            Check(AchievementsBridge.TakeSessionEvent(directory, now) is null, "garbage is nothing");

            void Summary(object json) => File.WriteAllText(Path.Combine(directory, AchievementsBridge.SummaryFile), System.Text.Json.JsonSerializer.Serialize(json));
            Summary(new { schema = 1, gameId = 3133, title = "Mario Party 4", user = "Zera", unlocked = new[] { 7, 3, 3, 12 }, total = 58, points = 25, totalPoints = 650, updatedAt = now.ToUnixTimeSeconds() });
            var summary = AchievementsBridge.ReadSummary(directory);
            Check(summary is { RaGameId: 3133, Total: 58 } && summary.UnlockedIds.SequenceEqual(new[] { 3, 7, 12 }), "ids cleaned: sorted, distinct");

            Summary(new { schema = 1, gameId = 3133, title = "Mario Party 4", user = "Zera", unlocked = Enumerable.Range(1, 60).ToArray(), total = 58, points = 25, totalPoints = 650 });
            Check(AchievementsBridge.ReadSummary(directory) is null, "more unlocked than exist: not believed");
            Summary(new { schema = 1, gameId = 3133, title = "Mario Party 4", user = "Zera", unlocked = new[] { 1 }, total = 58, points = 900, totalPoints = 650 });
            Check(AchievementsBridge.ReadSummary(directory) is null, "more points than the set holds: not believed");
            Summary(new { schema = 1, gameId = -1, title = "x", user = "Zera", unlocked = new[] { 1 }, total = 58 });
            Check(AchievementsBridge.ReadSummary(directory) is null, "no game: nothing");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public static void FriendsSeeProgressOnlyWhenShared()
    {
        var directory = TempDirectory();
        try
        {
            var cache = new AchievementsCache(directory);
            var summary = new AchievementsSummary(3133, "Mario Party 4", "Zera", new[] { 3, 7 }, 58, 25, 650, DateTimeOffset.UtcNow);
            Check(cache.Update("mario-party-4", summary), "first summary is a change");
            Check(!cache.Update("mario-party-4", summary), "the same again is not");
            Check(new AchievementsCache(directory).All["mario-party-4"].UnlockedIds.SequenceEqual(new[] { 3, 7 }), "kept across runs");

            var shared = new SharedAchievements("mario-party-4", 3133, "Mario Party 4", "Zera", 58, 25, 650, new[] { 3, 7 }, DateTimeOffset.UtcNow);
            Check(SharedAchievements.Sanitized(shared) is { Unlocked: 2 }, "a sound entry stays");
            Check(SharedAchievements.Sanitized(shared with { UnlockedIds = Enumerable.Range(1, 100).ToArray() }) is null, "an impossible one is dropped");
            Check(SharedAchievements.Sanitized(shared with { User = "<script>" }) is null, "a name that is not an account is dropped");
            Check(SharedAchievements.Sanitized(shared with { Title = "Mario\u0007 Party\u001b[31m" })!.Title.All(c => !char.IsControl(c) || c == '\n'), "control characters go");

            // The presence service publishes it when shared, and not otherwise, and not while invisible.
            using var me = PeerIdentity.Create();
            var friends = new FriendStore(directory);
            PresenceSnapshot? Publish(PresenceSharingOptions sharing, PresenceAvailability availability)
            {
                var publisher = new CapturingPublisher();
                var service = new PresenceService(me, friends, new PresenceComposer(new CubeShelf.Core.Platform.PlatformPaths("CubeShelf",
                        new Dictionary<string, string?> { [CubeShelf.Core.Platform.PlatformPaths.DataDirectoryVariable] = directory })),
                    new PresenceSequence(directory), publisher, new PresenceFetcher(me, friends),
                    _ => Task.FromResult(new PresenceInputs("Zera", Array.Empty<PresenceGame>(), Array.Empty<string>(), sharing,
                        Availability: availability, Achievements: new[] { shared })),
                    new PresenceServiceOptions(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
                service.Start(CancellationToken.None);
                Eventually(() => publisher.Last is not null, "published");
                service.DisposeAsync().AsTask().Wait();
                return SealedPresence.TryOpen(me, me.PublicKey, SealedPresence.FromJson(publisher.Last!), out var snapshot) ? snapshot : null;
            }

            Check(Publish(new PresenceSharingOptions(), PresenceAvailability.Available)?.Achievements is [{ GameId: "mario-party-4", Unlocked: 2 }], "shared by default");
            Check(Publish(new PresenceSharingOptions(ShareAchievements: false), PresenceAvailability.Available)?.Achievements is null, "not when the box is unticked");
            Check(Publish(new PresenceSharingOptions(), PresenceAvailability.Invisible)?.Achievements is null, "not while invisible");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public static void OnlyAGameThatAsksGetsTheSession()
    {
        var directory = TempDirectory();
        try
        {
            var game = Path.Combine(directory, "partyboard.exe");
            File.WriteAllText(game, "");
            Check(!RetroAchievementsLaunch.IsSupportedBy(game), "no manifest: nothing handed over");
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"capabilities\":[\"launcher-invites\"]}");
            Check(!RetroAchievementsLaunch.IsSupportedBy(game), "a manifest that does not ask: nothing");
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"capabilities\":[\"launcher-invites\",\"retroachievements-login\"]}");
            Check(RetroAchievementsLaunch.IsSupportedBy(game), "a manifest that asks");
            Check(OnlineCompanion.SupportsLauncherInvites(game), "invitations read the same manifest");
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "[\"retroachievements-login\"]");
            Check(!RetroAchievementsLaunch.IsSupportedBy(game), "a manifest of the wrong shape: nothing");

            var environment = new Dictionary<string, string?>();
            RetroAchievementsLaunch.AddTo(environment, new RetroAchievementsSession("Zera", "T0k3n", DateTimeOffset.UtcNow));
            Check(environment[RetroAchievementsLaunch.UserVariable] == "Zera" && environment[RetroAchievementsLaunch.TokenVariable] == "T0k3n",
                "handed over in the environment of the process, nowhere on disk");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class CapturingPublisher : IPresencePublisher
    {
        public volatile string? Last;
        public bool IsConfigured => true;
        public string PresenceUrl => "";

        public Task<PresencePublishResult> PublishAsync(string envelopeJson, CancellationToken cancellationToken = default)
        {
            Last = envelopeJson;
            return Task.FromResult(new PresencePublishResult(true, ""));
        }

        public void Dispose()
        {
        }
    }
}
