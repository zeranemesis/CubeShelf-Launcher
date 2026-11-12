using System.Globalization;
using System.Text.Json;

namespace CubeShelf.Core.Achievements;

public sealed record GameAchievement(int Id, string Title, string Description, int Points, bool? Unlocked);
public sealed record GameAchievementProgress(int? Unlocked, int? Total, IReadOnlyList<GameAchievement> Entries, string? Message = null);

public sealed class AchievementProgressClient(HttpClient? http = null)
{
    public const int MarioParty4 = 25450;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient _http = http ?? Client;

    public async Task<GameAchievementProgress> LoadAsync(long gameId, RetroAchievementsSession? session,
        AchievementsSummary? summary, CancellationToken cancellation)
    {
        var local = summary is not null && summary.RaGameId == gameId && session is not null
            && string.Equals(summary.User, session.User, StringComparison.OrdinalIgnoreCase) ? summary : null;
        if (session is null)
            return new(null, null, [], OperatingSystem.IsAndroid()
                ? "Connecte-toi à RetroAchievements dans le jeu, puis reviens ici."
                : "Connecte-toi à RetroAchievements dans les paramètres de DecompShelf.");
        if (gameId <= 0) return new(null, null, [], "Lance ce jeu une première fois pour identifier sa liste de succès.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var definitions = await RequestAsync("patch", gameId, session, timeout.Token).ConfigureAwait(false);
            if (!definitions.TryGetProperty("PatchData", out var patch) || Number(patch, "ID") != gameId)
                throw new InvalidDataException();
            var entries = patch.GetProperty("Achievements").EnumerateArray()
                .Take(AchievementsBridge.MaximumAchievements + 1)
                .Where(item => Number(item, "Flags") == 3 && Number(item, "ID") is > 0 and < 101000000)
                .Select(item => new GameAchievement(Number(item, "ID"), Text(item, "Title", 160),
                    Text(item, "Description", 600), Math.Clamp(Number(item, "Points"), 0, 1000), null))
                .DistinctBy(item => item.Id).ToArray();
            var unlocked = local?.UnlockedIds.ToHashSet();
            string? message = null;
            try
            {
                var progress = await RequestAsync("unlocks", gameId, session, timeout.Token).ConfigureAwait(false);
                unlocked = progress.GetProperty("UserUnlocks").EnumerateArray()
                    .Take(AchievementsBridge.MaximumAchievements)
                    .Select(item => item.GetInt32()).ToHashSet();
            }
            catch (Exception error) when (IsUnavailable(error, cancellation))
            {
                message = unlocked is null ? "Progression indisponible." : "Dernière progression enregistrée.";
            }
            var ordered = entries.Select(item => item with { Unlocked = unlocked?.Contains(item.Id) })
                .OrderByDescending(item => item.Unlocked == true).ToArray();
            return new(unlocked is null ? null : ordered.Count(item => item.Unlocked == true), ordered.Length, ordered, message);
        }
        catch (Exception error) when (IsUnavailable(error, cancellation))
        {
            return new(local?.UnlockedIds.Count, local?.Total, [], "Impossible de charger la liste des succès. Vérifie la connexion et la session RetroAchievements.");
        }
    }

    private async Task<JsonElement> RequestAsync(string method, long gameId, RetroAchievementsSession session, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RetroAchievementsLogin.Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["r"] = method, ["g"] = gameId.ToString(CultureInfo.InvariantCulture), ["h"] = "0",
                ["u"] = session.User, ["t"] = session.Token
            })
        };
        request.Headers.UserAgent.ParseAdd(OperatingSystem.IsAndroid() ? "DecompShelf/0.19.0 (Android)" : "DecompShelf/0.19.0 (Windows)");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        for (int read; (read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0;)
        {
            if (buffer.Length + read > 4 * 1024 * 1024) throw new InvalidDataException();
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: token).ConfigureAwait(false);
        if (document.RootElement.GetProperty("Success").ValueKind != JsonValueKind.True) throw new InvalidDataException();
        return document.RootElement.Clone();
    }

    private static bool IsUnavailable(Exception error, CancellationToken cancellation) => !cancellation.IsCancellationRequested &&
        error is HttpRequestException or InvalidDataException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OperationCanceledException;

    private static int Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var number) ? number : 0;

    private static string Text(JsonElement value, string name, int limit)
    {
        var text = value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
        return text[..Math.Min(limit, text.Length)];
    }
}
