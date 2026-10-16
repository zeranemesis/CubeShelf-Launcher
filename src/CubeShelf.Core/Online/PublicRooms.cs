using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CubeShelf.Core.Online;

/// <summary>One mod a room plays with: its GameBanana id, a name to show, and the first eight bytes
/// of its archive's SHA-256 in hex, which is what decides whether two copies are the same.</summary>
public sealed record RoomMod(int Id, string Name, string Fingerprint);

/// <summary>
/// One room of the public list, as the relay's directory describes it. Every string is bounded by
/// the relay already and again here, since the list is written by whoever hosts.
/// </summary>
public sealed record PublicRoom(
    string Room,
    string Host,
    string Language,
    string Region,
    string Game,
    string State,
    IReadOnlyList<string> Players,
    int MaxPlayers,
    IReadOnlyList<RoomMod> Mods,
    TimeSpan Open,
    TimeSpan Playing)
{
    /// <summary>The name its host gave the room, or empty.</summary>
    public string Title { get; init; } = "";
    /// <summary>What the game says is happening (board, turn, minigame), while it plays.</summary>
    public string Activity { get; init; } = "";

    /// <summary>Waiting for players: the only state in which a room can be joined.</summary>
    public bool IsJoinable => State == "waiting";
    public bool IsPlaying => State == "playing";

    /// <summary>
    /// The relay room an invitation names: the first 16 bytes of SHA-256("PBRELAY1" + token), the
    /// token being bytes 26 to 41 of the PB4 code. How CubeShelf knows its own room in the list.
    /// </summary>
    public static string? IdOfInvite(string? invite)
    {
        if (invite is null || invite.Length != 72 || !invite.StartsWith("PB4.", StringComparison.Ordinal)) return null;
        try
        {
            var b64 = invite[4..].Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            var data = Convert.FromBase64String(b64);
            if (data.Length != 49) return null;
            var input = System.Text.Encoding.ASCII.GetBytes("PBRELAY1").Concat(data.Skip(26).Take(16)).ToArray();
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input), 0, 16).ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// What the online companion says about this PC for the public list (<c>--room-tags</c>): the
/// tags the list is filtered on, the game the disc is, the mods this PC would play with, and the
/// relay it uses. Read from the companion rather than computed here so the list CubeShelf shows is
/// exactly the one the companion's own window shows.
/// </summary>
public sealed record RoomTags(string Build, string Disc, string Game, IReadOnlyList<RoomMod> Mods, Uri Relay)
{
    private static readonly Regex Hex16 = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);

    public static RoomTags Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var build = root.GetProperty("build").GetString() ?? "";
        var disc = root.GetProperty("disc").GetString() ?? "";
        var relay = root.TryGetProperty("relay", out var r) ? r.GetString() ?? "" : "";
        if (!Hex16.IsMatch(build) || !Hex16.IsMatch(disc))
            throw new FormatException("The companion gave tags that are not 16 hex digits.");
        if (!Uri.TryCreate(relay, UriKind.Absolute, out var relayUri) ||
            (relayUri.Scheme != Uri.UriSchemeHttps && relayUri.Scheme != Uri.UriSchemeHttp))
            throw new FormatException("The companion has no online relay.");
        var game = root.TryGetProperty("game", out var g) ? g.GetString() ?? "GMPE01" : "GMPE01";
        var mods = root.TryGetProperty("mods", out var m) ? PublicRoomDirectory.ReadMods(m) : Array.Empty<RoomMod>();
        return new RoomTags(build, disc, game, mods, relayUri);
    }

    /// <summary>
    /// Runs <c>companion --room-tags</c>. The first run hashes the whole disc (a few seconds); the
    /// companion keeps the result per file, so later ones answer at once.
    /// </summary>
    public static async Task<RoomTags> ReadAsync(string companion, string disc, string? modList,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(companion, "--room-tags")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(companion) ?? ""
        };
        info.Environment["PARTYBOARD_ONLINE_DISC"] = disc;
        if (!string.IsNullOrEmpty(modList)) info.Environment["PARTYBOARD_MOD_LIST"] = modList;

        using var process = Process.Start(info) ?? throw new IOException("The companion did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new IOException((await errors).Trim() is { Length: > 0 } message ? message : "The companion could not describe this disc.");
            return Parse((await output).Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
    }
}

/// <summary>Why a room could not be joined after all: it filled up, started or closed.</summary>
public sealed class RoomGoneException(string message) : IOException(message);

/// <summary>
/// The relay's public list: <c>GET /rooms</c> and <c>GET /rooms/&lt;id&gt;</c>. Nothing about this
/// player is sent but the two tags the list is filtered on.
/// </summary>
public sealed class PublicRoomDirectory(HttpClient http, Uri relay)
{
    private static readonly Regex RoomId = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex16 = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    private const int MaximumBytes = 512 * 1024;

    public async Task<IReadOnlyList<PublicRoom>> ListAsync(RoomTags tags, CancellationToken cancellationToken = default)
    {
        var json = await GetAsync($"rooms?build={tags.Build}&disc={tags.Disc}", cancellationToken);
        return ParseList(json);
    }

    /// <summary>The invitation of a room that still waits; <see cref="RoomGoneException"/> otherwise.</summary>
    public async Task<string> InviteAsync(string room, CancellationToken cancellationToken = default)
    {
        if (!RoomId.IsMatch(room)) throw new ArgumentException("Not a room id.", nameof(room));
        var json = await GetAsync("rooms/" + room, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var invite = document.RootElement.TryGetProperty("invite", out var i) ? i.GetString() : null;
        if (invite is null || invite.Length != 72 || !invite.StartsWith("PB4.", StringComparison.Ordinal))
            throw new RoomGoneException("The relay gave no usable invitation.");
        return invite;
    }

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        var baseUri = relay.AbsoluteUri.EndsWith('/') ? relay : new Uri(relay.AbsoluteUri + "/");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.UserAgent.ParseAdd("CubeShelf");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new RoomGoneException("That room is no longer open.");
        if ((int)response.StatusCode == 429)
            throw new IOException("Too many requests to the relay; wait a minute.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new IOException("The relay's answer is too large.");
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (text.Length > MaximumBytes) throw new IOException("The relay's answer is too large.");
        return text;
    }

    public static IReadOnlyList<PublicRoom> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("rooms", out var rooms) || rooms.ValueKind != JsonValueKind.Array)
            return Array.Empty<PublicRoom>();

        var result = new List<PublicRoom>();
        foreach (var r in rooms.EnumerateArray().Take(100))
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            var id = Text(r, "room", 32);
            if (!RoomId.IsMatch(id)) continue;
            var host = Text(r, "name", 24);
            var players = r.TryGetProperty("players", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(4).Select(x => Clean(x.GetString(), 24)).Where(x => x.Length > 0).ToArray()
                : Array.Empty<string>();
            if (players.Length == 0 && host.Length > 0) players = new[] { host };
            var state = Text(r, "state", 10) is "playing" or "full" ? Text(r, "state", 10) : "waiting";
            result.Add(new PublicRoom(
                id,
                host.Length > 0 ? host : players.FirstOrDefault() ?? "?",
                Text(r, "lang", 2),
                Text(r, "region", 2) == "eu" ? "eu" : "us",
                Text(r, "game", 6),
                state,
                players,
                Number(r, "max") is >= 2 and <= 4 and var max ? max : 2,
                r.TryGetProperty("mods", out var m) ? ReadMods(m) : Array.Empty<RoomMod>(),
                TimeSpan.FromSeconds(Math.Max(0, Number(r, "age"))),
                TimeSpan.FromSeconds(Math.Max(0, Number(r, "playing"))))
            { Title = Text(r, "title", 32), Activity = Text(r, "activity", 120) });
        }
        return result;
    }

    internal static IReadOnlyList<RoomMod> ReadMods(JsonElement mods)
    {
        if (mods.ValueKind != JsonValueKind.Array) return Array.Empty<RoomMod>();
        var list = new List<RoomMod>();
        foreach (var m in mods.EnumerateArray().Take(24))
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            var id = Number(m, "id");
            var fp = Text(m, "fp", 16);
            if (id <= 0 || !Hex16.IsMatch(fp)) continue;
            list.Add(new RoomMod(id, Text(m, "name", 48), fp));
        }
        return list;
    }

    private static string Text(JsonElement e, string name, int max) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Clean(v.GetString(), max) : "";

    private static int Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static string Clean(string? text, int max)
    {
        var clean = new string((text ?? "").Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray()).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
