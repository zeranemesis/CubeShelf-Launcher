using System.Text.Json;
using System.Text.Json.Nodes;
using CubeShelf.Core.Social;

namespace CubeShelf.Core.Achievements;

public enum RetroAchievementsEventKind
{
    /// <summary>The player logged in from inside the game: CubeShelf takes that session for every game.</summary>
    Login,

    /// <summary>The player logged out from inside the game: CubeShelf forgets the session too.</summary>
    Logout,

    /// <summary>The server refused the session CubeShelf handed over: it needs the password again.</summary>
    Rejected
}

public sealed record RetroAchievementsEvent(RetroAchievementsEventKind Kind, string? User, string? Token, DateTimeOffset At);

/// <summary>Where a player stands in one game's achievement set, as the game reports it.</summary>
public sealed record AchievementsSummary(
    long RaGameId,
    string Title,
    string User,
    IReadOnlyList<int> UnlockedIds,
    int Total,
    int Points,
    int TotalPoints,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The two files a game with RetroAchievements leaves for CubeShelf in the in-game directory
/// (<see cref="InGameBridge.DirectoryVariable"/>), beside the ones the Friends tab uses:
///
/// <list type="bullet">
/// <item><c>ra-session.json</c> -- the player logged in or out inside the game, or the server
/// refused the session CubeShelf gave it. Read once and deleted.</item>
/// <item><c>achievements.json</c> -- the set being played and what is unlocked in it, rewritten
/// when that changes. What friends see.</item>
/// </list>
///
/// Both come from a program CubeShelf started, but everything in them is checked as if it did
/// not: sizes, formats, counts.
/// </summary>
public static class AchievementsBridge
{
    public const string SessionFile = "ra-session.json";
    public const string SummaryFile = "achievements.json";
    public const int MaximumAchievements = 2000;
    private const long MaximumBytes = 128 * 1024;
    private static readonly TimeSpan SessionEventLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The game's latest word about the session, removed as it is read. Null when there is none, or only a stale or malformed one.</summary>
    public static RetroAchievementsEvent? TakeSessionEvent(string directory, DateTimeOffset now)
    {
        var path = Path.Combine(directory, SessionFile);
        var node = ReadObject(path);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        if (node is null || Number(node, "schema") != 1) return null;

        var at = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(Number(node, "at"), 0, 253402300799));
        if (now - at > SessionEventLifetime || at - now > TimeSpan.FromMinutes(5)) return null;

        var user = Text(node, "user");
        var token = Text(node, "token");
        return Text(node, "event") switch
        {
            "login" when RetroAchievementsSession.IsUser(user) && RetroAchievementsSession.IsToken(token) =>
                new RetroAchievementsEvent(RetroAchievementsEventKind.Login, user, token, at),
            "logout" => new RetroAchievementsEvent(RetroAchievementsEventKind.Logout, null, null, at),
            "rejected" => new RetroAchievementsEvent(RetroAchievementsEventKind.Rejected, null, null, at),
            _ => null
        };
    }

    public static AchievementsSummary? ReadSummary(string directory)
    {
        var node = ReadObject(Path.Combine(directory, SummaryFile));
        return node is null ? null : ParseSummary(node);
    }

    internal static AchievementsSummary? ParseSummary(JsonObject node)
    {
        if (Number(node, "schema") != 1) return null;
        var gameId = Number(node, "gameId");
        var user = Text(node, "user");
        var title = ChatText.Clean(Text(node, "title"));
        var total = (int)Math.Clamp(Number(node, "total"), 0, MaximumAchievements);
        var points = (int)Math.Clamp(Number(node, "points"), 0, 1_000_000);
        var totalPoints = (int)Math.Clamp(Number(node, "totalPoints"), 0, 1_000_000);
        if (gameId <= 0 || !RetroAchievementsSession.IsUser(user) || total == 0) return null;
        if (title.Length > 100) title = title[..100];

        var unlocked = new SortedSet<int>();
        if (node["unlocked"] is JsonArray ids)
        {
            foreach (var id in ids)
            {
                if (unlocked.Count >= MaximumAchievements) break;
                if (id is JsonValue value && value.TryGetValue<long>(out var number) && number is > 0 and <= int.MaxValue)
                    unlocked.Add((int)number);
            }
        }
        if (unlocked.Count > total || points > totalPoints) return null;

        var updated = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(Number(node, "updatedAt"), 0, 253402300799));
        return new AchievementsSummary(gameId, title, user!, unlocked.ToArray(), total, points, totalPoints, updated);
    }

    private static JsonObject? ReadObject(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > MaximumBytes) return null;
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static long Number(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// The last summary of each game, kept in the profile so friends see where we stand even while we
/// are not playing: <c>achievements-shared.json</c>, by catalog game id.
/// </summary>
public sealed class AchievementsCache
{
    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, AchievementsSummary>? _loaded;

    public AchievementsCache(string configurationDirectory) =>
        _file = Path.Combine(Path.GetFullPath(configurationDirectory), "achievements-shared.json");

    public IReadOnlyDictionary<string, AchievementsSummary> All
    {
        get
        {
            lock (_gate) return new Dictionary<string, AchievementsSummary>(Loaded(), StringComparer.Ordinal);
        }
    }

    /// <summary>Records a game's summary. True when it changed what friends would see.</summary>
    public bool Update(string catalogGameId, AchievementsSummary summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogGameId);
        ArgumentNullException.ThrowIfNull(summary);
        lock (_gate)
        {
            var all = Loaded();
            if (all.TryGetValue(catalogGameId, out var previous) && Same(previous, summary)) return false;
            all[catalogGameId] = summary;
            Save(all);
            return true;
        }
    }

    /// <summary>Forgets everything: logged out, or another account.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _loaded = new Dictionary<string, AchievementsSummary>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(_file)) File.Delete(_file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool Same(AchievementsSummary a, AchievementsSummary b) =>
        a.RaGameId == b.RaGameId && a.User == b.User && a.Total == b.Total && a.Points == b.Points &&
        a.TotalPoints == b.TotalPoints && a.UnlockedIds.SequenceEqual(b.UnlockedIds) && a.Title == b.Title;

    private Dictionary<string, AchievementsSummary> Loaded()
    {
        if (_loaded is not null) return _loaded;
        _loaded = new Dictionary<string, AchievementsSummary>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(_file) && JsonNode.Parse(File.ReadAllText(_file)) is JsonObject root)
            {
                foreach (var (gameId, value) in root)
                {
                    if (value is JsonObject entry && AchievementsBridge.ParseSummary(entry) is { } summary)
                        _loaded[gameId] = summary;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return _loaded;
    }

    private void Save(Dictionary<string, AchievementsSummary> all)
    {
        var root = new JsonObject();
        foreach (var (gameId, summary) in all)
        {
            root[gameId] = new JsonObject
            {
                ["schema"] = 1,
                ["gameId"] = summary.RaGameId,
                ["title"] = summary.Title,
                ["user"] = summary.User,
                ["unlocked"] = new JsonArray(summary.UnlockedIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
                ["total"] = summary.Total,
                ["points"] = summary.Points,
                ["totalPoints"] = summary.TotalPoints,
                ["updatedAt"] = summary.UpdatedAt.ToUnixTimeSeconds()
            };
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, root.ToJsonString());
            File.Move(temporary, _file, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
