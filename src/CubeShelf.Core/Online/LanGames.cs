using System.Diagnostics;
using System.Text.Json;

namespace CubeShelf.Core.Online;

/// <summary>A game on this network, as the companion's LAN discovery found it.</summary>
public sealed record LanGame(string Host, string Language, string Region, int Players, int MaxPlayers, string Invite)
{
    public bool HasSeat => Players < MaxPlayers;
}

/// <summary>
/// The games on this network (<c>companion --lan-games</c>): the companion asks the network the way
/// its own "Parties en LAN" list does, for the same build and disc, and nothing leaves the network.
/// </summary>
public static class LanGames
{
    public static IReadOnlyList<LanGame> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            return Array.Empty<LanGame>();
        var list = new List<LanGame>();
        foreach (var g in games.EnumerateArray().Take(32))
        {
            if (g.ValueKind != JsonValueKind.Object) continue;
            string S(string name, int max) => g.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? new string((v.GetString() ?? "").Where(c => !char.IsControl(c)).Take(max).ToArray()).Trim() : "";
            int N(string name) => g.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
            var invite = S("invite", 72);
            if (invite.Length != 72 || !invite.StartsWith("PB4.", StringComparison.Ordinal)) continue;
            var max = Math.Clamp(N("max"), 2, 4);
            list.Add(new LanGame(S("name", 24) is { Length: > 0 } name ? name : "?", S("lang", 2), S("region", 2) == "eu" ? "eu" : "us",
                Math.Clamp(N("players"), 1, max), max, invite));
        }
        return list;
    }

    public static async Task<IReadOnlyList<LanGame>> ReadAsync(string companion, string disc, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(companion, "--lan-games")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(companion) ?? ""
        };
        info.Environment["PARTYBOARD_ONLINE_DISC"] = disc;
        using var process = Process.Start(info) ?? throw new IOException("The companion did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new IOException((await errors).Trim() is { Length: > 0 } message ? message : "The companion could not look for games on this network.");
            return Parse((await output).Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw;
        }
    }
}
