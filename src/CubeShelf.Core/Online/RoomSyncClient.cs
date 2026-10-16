using System.Text.Json;

namespace CubeShelf.Core.Online;

/// <summary>One player of the room as the companion sees them.</summary>
/// <param name="Disc">same, different, pending (not compared yet) or unknown.</param>
public sealed record RoomSeat(string Name, bool Host, bool You, string Disc, IReadOnlyList<RoomMod> Mods, int? Ping);

/// <summary>The room the companion has open, as its room.json says.</summary>
public sealed record RoomState(
    long Revision,
    string State,
    string Role,
    string Mode,
    string Visibility,
    bool Public,
    string Game,
    IReadOnlyList<string> Players,
    int MaxPlayers,
    IReadOnlyList<RoomMod> Mods,
    int? Ping,
    string Message,
    string Invite)
{
    /// <summary>Every player, with disc and mods, once the lobby exists.</summary>
    public IReadOnlyList<RoomSeat> Seats { get; init; } = Array.Empty<RoomSeat>();
    /// <summary>waiting, preparing or running -- the lobby's own phase.</summary>
    public string Phase { get; init; } = "";
    public bool CanStart { get; init; }
    public bool DiscsMatch { get; init; } = true;
    public bool ModsMatch { get; init; } = true;
    /// <summary>What differs between the mods, in the companion's words.</summary>
    public string ModAdvice { get; init; } = "";
    /// <summary>A guest's mods the host plays with and this PC has never installed.</summary>
    public IReadOnlyList<RoomMod> MissingMods { get; init; } = Array.Empty<RoomMod>();
    /// <summary>A guest whose list is not the host's.</summary>
    public bool DiffersFromHost { get; init; }
    /// <summary>Why the room ended, when it has.</summary>
    public string Ending { get; init; } = "";
    /// <summary>The relay room this is, host or guest: never to be listed as someone else's.</summary>
    public string RoomId { get; init; } = "";

    /// <summary>This room's relay id, from the companion or else from its invitation.</summary>
    public string? RelayRoom => RoomId.Length == 32 ? RoomId : PublicRoom.IdOfInvite(Invite);

    public bool IsOpen => State is "creating" or "waiting" or "full" or "playing" or "joining";
    public bool IsHost => Role == "host";
}

/// <summary>
/// The companion side of a room, seen from CubeShelf: the directory named by
/// PARTYBOARD_ONLINE_STATE_DIR, where the companion writes <c>room.json</c> and reads requests.
/// The companion's half is tools/online/RoomSync.cs in the Mario Party 4 port; the names here must
/// match it.
/// </summary>
public sealed class RoomSyncClient(string directory)
{
    public const string DirectoryVariable = "PARTYBOARD_ONLINE_STATE_DIR";
    public const string VisibilityVariable = "PARTYBOARD_ONLINE_VISIBILITY";
    public const string ModsVariable = "PARTYBOARD_ONLINE_MODS";
    public const string HeadlessVariable = "PARTYBOARD_ONLINE_HEADLESS";
    private const int MaximumBytes = 64 * 1024;

    public string Directory => directory;

    /// <summary>Starts a room afresh: what a previous one left must not be read as this one.</summary>
    public void Reset()
    {
        try
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        System.IO.Directory.CreateDirectory(directory);
    }

    public RoomState? Read()
    {
        var file = Path.Combine(directory, "room.json");
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0 || info.Length > MaximumBytes) return null;
            return Parse(File.ReadAllText(file));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return null;
        }
    }

    public static RoomState Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var r = document.RootElement;
        string S(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int? N(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
        var players = r.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(4).Select(x => x.GetString() ?? "").ToArray()
            : Array.Empty<string>();
        bool B(string name, bool fallback = false) => r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True : fallback;
        var seats = new List<RoomSeat>();
        if (r.TryGetProperty("seats", out var s) && s.ValueKind == JsonValueKind.Array)
            foreach (var seat in s.EnumerateArray().Take(4))
            {
                if (seat.ValueKind != JsonValueKind.Object) continue;
                string SS(string name) => seat.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                seats.Add(new RoomSeat(SS("name"),
                    seat.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.True,
                    seat.TryGetProperty("you", out var y) && y.ValueKind == JsonValueKind.True,
                    SS("disc"),
                    seat.TryGetProperty("mods", out var sm) ? PublicRoomDirectory.ReadMods(sm) : Array.Empty<RoomMod>(),
                    seat.TryGetProperty("ping", out var p2) && p2.ValueKind == JsonValueKind.Number && p2.TryGetInt32(out var ms) ? ms : null));
            }
        return new RoomState(
            r.TryGetProperty("revision", out var rev) && rev.TryGetInt64(out var revision) ? revision : 0,
            S("state"), S("role"), S("mode"), S("visibility"),
            r.TryGetProperty("public", out var pub) && pub.ValueKind == JsonValueKind.True,
            S("game"), players, N("max") ?? 2,
            r.TryGetProperty("mods", out var m) ? PublicRoomDirectory.ReadMods(m) : Array.Empty<RoomMod>(),
            N("ping"), S("message"), S("invite"))
        {
            Seats = seats,
            Phase = S("phase"),
            CanStart = B("canStart"),
            DiscsMatch = B("discsMatch", true),
            ModsMatch = B("modsMatch", true),
            ModAdvice = S("modAdvice"),
            MissingMods = r.TryGetProperty("missingMods", out var mm) ? PublicRoomDirectory.ReadMods(mm) : Array.Empty<RoomMod>(),
            DiffersFromHost = B("differsFromHost"),
            Ending = S("ending"),
            RoomId = S("roomId")
        };
    }

    /// <summary>Asks the companion for something and waits for its answer (false, with why, if none came).</summary>
    public async Task<(bool Ok, string Message)> AskAsync(string action, string? value, CancellationToken cancellationToken = default)
    {
        var requests = Path.Combine(directory, "requests");
        System.IO.Directory.CreateDirectory(requests);
        var id = Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(requests, id + ".tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { id, action, value }), cancellationToken);
        File.Move(temporary, Path.Combine(requests, id + ".json"));

        var done = Path.Combine(requests, id + ".done");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200, cancellationToken);
            if (!File.Exists(done)) continue;
            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(done, cancellationToken));
                var ok = document.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
                var message = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                try { File.Delete(done); } catch (IOException) { }
                return (ok, message);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                // Still being written; look again.
            }
        }
        try { File.Delete(Path.Combine(requests, id + ".json")); } catch (IOException) { }
        return (false, "");
    }
}
