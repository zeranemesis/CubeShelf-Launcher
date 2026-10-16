using System.Net;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Online;

/// <summary>The Online page's core: the relay's list, a room's mods against ours, the companion's room.</summary>
internal static class OnlineRoomsTests
{
    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    private const string Room = "0123456789abcdef0123456789abcdef";

    public static void ParsesTheList()
    {
        var json = $$"""
        {"rooms":[
          {"room":"{{Room}}","name":"Zera","lang":"fr","region":"eu","game":"GMPP01","state":"waiting",
           "players":["Zera"],"max":2,"mods":[{"id":4242,"name":"Board Pack","fp":"0102030405060708"}],"age":75,"playing":0},
          {"room":"fedcba9876543210fedcba9876543210","name":"Luigi","lang":"en","region":"us","game":"GMPE01","state":"playing",
           "players":["Luigi","Daisy"],"max":2,"mods":[],"age":900,"playing":600,"title":"Soirée","activity":"Plateau Jardin de Toad, tour 5/20"},
          {"room":"not-a-room","name":"x"},
          {"room":"{{Room}}","name":"Bad\u202Ename\u0007","state":"hacked","max":99,
           "players":["a","b","c","d","e","f"],"mods":[{"id":-1,"fp":"zz"},{"id":5,"fp":"0011223344556677"}]}
        ]}
        """;
        var rooms = PublicRoomDirectory.ParseList(json);
        Check(rooms.Count == 3, "a room with a malformed id is dropped");
        var zera = rooms[0];
        Check(zera.Host == "Zera" && zera.Language == "fr" && zera.Region == "eu" && zera.Game == "GMPP01", "host, language, region and game");
        Check(zera.IsJoinable && !zera.IsPlaying && zera.Open == TimeSpan.FromSeconds(75), "a waiting room, open for 75 s");
        Check(zera.Mods.Count == 1 && zera.Mods[0] == new RoomMod(4242, "Board Pack", "0102030405060708"), "its mod");
        var luigi = rooms[1];
        Check(luigi.Title == "Soirée" && luigi.Activity == "Plateau Jardin de Toad, tour 5/20", "the room's name and what its game says");
        Check(luigi.IsPlaying && !luigi.IsJoinable && luigi.Players.SequenceEqual(new[] { "Luigi", "Daisy" }) && luigi.Playing == TimeSpan.FromMinutes(10), "a game under way, with its players");
        var hostile = rooms[2];
        Check(hostile.Host == "Badname", "control and direction characters are removed from names");
        Check(hostile.State == "waiting" && hostile.MaxPlayers == 2 && hostile.Players.Count == 4, "unknown state, seat count and extra players are bounded");
        Check(hostile.Mods.Count == 1 && hostile.Mods[0].Id == 5, "malformed mods are dropped");
        Check(PublicRoomDirectory.ParseList("{}").Count == 0, "an empty answer is an empty list");
        // Same token as the companion's own test vector: room id of an all-zero-but-token code.
        var token = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var code = new byte[49];
        token.CopyTo(code, 26);
        code[48] = 2;
        var invite = "PB4." + Convert.ToBase64String(code).Replace('+', '-').Replace('/', '_');
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes("PBRELAY1").Concat(token).ToArray()), 0, 16).ToLowerInvariant();
        Check(PublicRoom.IdOfInvite(invite) == expected, "an invitation names its relay room");
        Check(PublicRoom.IdOfInvite("PB4.short") is null && PublicRoom.IdOfInvite(null) is null, "anything else names none");
    }

    public static void ReadsTheCompanionTags()
    {
        var tags = RoomTags.Parse("""{"schema":1,"build":"57057c1c8bf2b2b9","disc":"703d77caf79b40cb","game":"GMPE01","mods":[{"id":7,"name":"M","fp":"0909090909090909"}],"relay":"https://relay.example"}""");
        Check(tags.Build == "57057c1c8bf2b2b9" && tags.Disc == "703d77caf79b40cb" && tags.Game == "GMPE01", "build, disc and game");
        Check(tags.Mods.Count == 1 && tags.Relay == new Uri("https://relay.example"), "mods and relay");
        var refused = 0;
        foreach (var bad in new[]
                 {
                     """{"build":"XYZ","disc":"703d77caf79b40cb","relay":"https://r"}""",
                     """{"build":"57057c1c8bf2b2b9","disc":"703d77caf79b40cb","relay":"file:///c:/x"}""",
                     """{"build":"57057c1c8bf2b2b9","disc":"703d77caf79b40cb","relay":""}"""
                 })
            try { RoomTags.Parse(bad); } catch (FormatException) { refused++; }
        Check(refused == 3, "tags that are not hex, or a relay that is not http(s), are refused");
    }

    public static void ComparesMods()
    {
        PortableInstalledMod Installed(int id, string sha, bool enabled = true) => new(id, "m" + id, 1, enabled, 0, "C:/mods/" + id, sha);
        var installed = new[]
        {
            Installed(1, "0102030405060708aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            Installed(2, "ffffffffffffffffbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", enabled: false),
            Installed(3, "")
        };
        var same = RoomModCheck.Compare(new[] { new RoomMod(2, "", "ffffffffffffffff"), new RoomMod(1, "", "0102030405060708") }, installed);
        Check(same.Status == RoomModStatus.Compatible && same.SessionMods == "2,1", "same mods, even one switched off here: compatible, in the room's order");
        var mixed = RoomModCheck.Compare(new[] { new RoomMod(1, "", "1111111111111111"), new RoomMod(9, "Neuf", "2222222222222222") }, installed);
        Check(mixed.Status == RoomModStatus.NeedsInstall && mixed.Outdated.Single().Id == 1 && mixed.Missing.Single().Id == 9, "another version and a missing mod");
        Check(RoomModCheck.Fingerprint(installed[2]) == Convert.ToHexString(BitConverter.GetBytes(3L)).ToLowerInvariant(), "no recorded hash: the id stands in, as the companion does");
        Check(RoomModCheck.Compare(Array.Empty<RoomMod>(), installed) is { Status: RoomModStatus.Compatible, SessionMods: "" }, "a room without mods asks for none");
    }

    public static void FollowsTheCompanionRoom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-room-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sync = new RoomSyncClient(dir);
            sync.Reset();
            Check(sync.Read() is null, "no room yet");
            File.WriteAllText(Path.Combine(dir, "room.json"),
                """
                {"schema":1,"revision":4,"state":"full","role":"host","mode":"online","visibility":"public","public":true,"game":"GMPE01","players":["Zera","Ami"],"max":2,"mods":[{"id":4242,"name":"Board Pack","fp":"0102030405060708"}],"ping":31,"message":"ok",
                  "seats":[{"name":"Zera","host":true,"you":true,"disc":"same","mods":[],"ping":null},{"name":"Ami","host":false,"you":false,"disc":"different","mods":[{"id":7,"name":"M","fp":"0909090909090909"}],"ping":31}],
                  "phase":"waiting","canStart":false,"discsMatch":false,"modsMatch":false,"modAdvice":"Ami : 7","missingMods":[{"id":7,"name":"M","fp":"0909090909090909"}],"differsFromHost":true}
                """);
            var state = sync.Read()!;
            Check(state.Revision == 4 && state.State == "full" && state.IsHost && state.IsOpen && state.Public, "state, role and visibility");
            Check(state.Players.SequenceEqual(new[] { "Zera", "Ami" }) && state.Mods.Single().Id == 4242 && state.Ping == 31, "players, mods and ping");
            Check(state.Seats.Count == 2 && state.Seats[0] is { Name: "Zera", Host: true, You: true, Disc: "same", Ping: null } &&
                  state.Seats[1] is { Disc: "different", Ping: 31 } && state.Seats[1].Mods.Single().Id == 7, "every seat with its disc, mods and ping");
            Check(state.Phase == "waiting" && !state.CanStart && !state.DiscsMatch && !state.ModsMatch && state.ModAdvice == "Ami : 7", "the lobby's verdicts");
            Check(state.MissingMods.Single().Id == 7 && state.DiffersFromHost, "what a guest is missing");
            File.WriteAllText(Path.Combine(dir, "room.json"), "{half");
            Check(sync.Read() is null, "a half-written file reads as nothing rather than throwing");

            // The companion's side, played here: take the request, answer it.
            var answer = Task.Run(async () =>
            {
                var requests = Path.Combine(dir, "requests");
                for (var i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    var file = Directory.Exists(requests) ? Directory.GetFiles(requests, "*.json").FirstOrDefault() : null;
                    if (file is null) continue;
                    using var request = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                    var id = request.RootElement.GetProperty("id").GetString();
                    Check(request.RootElement.GetProperty("action").GetString() == "set-visibility" && request.RootElement.GetProperty("value").GetString() == "public", "the request says what is asked");
                    File.Delete(file);
                    File.WriteAllText(Path.Combine(requests, id + ".done"), $$"""{"id":"{{id}}","ok":true,"message":"Room publique."}""");
                    return;
                }
            });
            var (ok, message) = sync.AskAsync("set-visibility", "public").GetAwaiter().GetResult();
            answer.GetAwaiter().GetResult();
            Check(ok && message == "Room publique.", "the answer comes back");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    public static void TalksToTheDirectory()
    {
        var handler = new StubHandler();
        var directory = new PublicRoomDirectory(new HttpClient(handler), new Uri("https://relay.example"));
        var tags = new RoomTags("57057c1c8bf2b2b9", "703d77caf79b40cb", "GMPE01", Array.Empty<RoomMod>(), new Uri("https://relay.example"));
        handler.Reply = (HttpStatusCode.OK, $$"""{"rooms":[{"room":"{{Room}}","name":"Zera"}]}""");
        var rooms = directory.ListAsync(tags).GetAwaiter().GetResult();
        Check(handler.Last == "https://relay.example/rooms?build=57057c1c8bf2b2b9&disc=703d77caf79b40cb", "only the two tags are sent");
        Check(rooms.Single().Host == "Zera", "the list comes back");
        handler.Reply = (HttpStatusCode.OK, """{"invite":"PB4.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""");
        Check(directory.InviteAsync(Room).GetAwaiter().GetResult().Length == 72, "a waiting room's invitation");
        handler.Reply = (HttpStatusCode.NotFound, "no such room");
        var gone = false;
        try { directory.InviteAsync(Room).GetAwaiter().GetResult(); } catch (RoomGoneException) { gone = true; }
        Check(gone, "a room that filled up or closed says so");
        var rejected = false;
        try { directory.InviteAsync("../turn").GetAwaiter().GetResult(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "only a room id goes into the path");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Reply = (HttpStatusCode.OK, "{}");
        public string Last = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(Reply.Status) { Content = new StringContent(Reply.Body) });
        }
    }
}
