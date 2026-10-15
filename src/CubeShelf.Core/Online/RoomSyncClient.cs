using System.Text.Json;

namespace CubeShelf.Core.Online;

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
    public const string ModeVariable = "PARTYBOARD_ONLINE_MODE";
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
        return new RoomState(
            r.TryGetProperty("revision", out var rev) && rev.TryGetInt64(out var revision) ? revision : 0,
            S("state"), S("role"), S("mode"), S("visibility"),
            r.TryGetProperty("public", out var pub) && pub.ValueKind == JsonValueKind.True,
            S("game"), players, N("max") ?? 2,
            r.TryGetProperty("mods", out var m) ? PublicRoomDirectory.ReadMods(m) : Array.Empty<RoomMod>(),
            N("ping"), S("message"), S("invite"));
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
