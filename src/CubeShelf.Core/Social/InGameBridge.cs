using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CubeShelf.Core.Social;

/// <summary>One friend as the game's Friends tab shows them. Every string is ready to display.</summary>
public sealed record InGameFriend(
    string Key,
    string Handle,
    string Status,
    string Label,
    bool InvitesYou = false,
    string InviteId = "");

/// <summary>What the game's Friends tab (F1) needs, already worded in the player's language.</summary>
public sealed record InGameState(
    string Me,
    bool Ready,
    string Notice,
    bool CanHost,
    bool Hosting,
    IReadOnlyList<InGameFriend> Friends,
    IReadOnlyDictionary<string, string> Text);

public enum InGameAction
{
    Host,
    Invite,
    Join,
    Cancel
}

/// <summary>Something the player asked for from inside the game.</summary>
public sealed record InGameRequest(string Id, InGameAction Action, string FriendKey);

/// <summary>
/// The files CubeShelf and a running Party Board exchange, in the directory named by
/// <see cref="DirectoryVariable"/>. No socket and no port: two processes of the same user, one
/// directory in that user's profile.
///
/// <list type="bullet">
/// <item><c>state.json</c> -- written here every few seconds, read by the game's F1 menu.</item>
/// <item><c>requests/&lt;id&gt;.json</c> -- written by the game when the player asks for something.</item>
/// <item><c>requests/&lt;id&gt;.done</c> -- the answer, written here once the request was acted on.</item>
/// </list>
///
/// The game side lives in zeranemesis/Marioparty4, src/port/ui/cubeshelf.cpp; the two must agree
/// on every name in this file.
/// </summary>
public static class InGameBridge
{
    public const string DirectoryVariable = "CUBESHELF_INGAME_DIR";
    public const int Schema = 1;

    /// <summary>
    /// A request older than this is dropped unacted. The game gives up after twelve seconds; a
    /// request found later was written while CubeShelf was closed, and opening a lobby an hour
    /// after someone clicked, for a game that has long since moved on, would be the worst outcome.
    /// </summary>
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromSeconds(30);

    private const int MaximumRequestBytes = 4096;
    private const int MaximumRequestsPerRead = 32;

    private static readonly Regex IdPattern = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string StateFile(string directory) => Path.Combine(directory, "state.json");

    public static string RequestsDirectory(string directory) => Path.Combine(directory, "requests");

    /// <summary>
    /// Writes <c>state.json</c>. The revision changes only when something shown changes, so the
    /// game can rebuild its tab on a real change and leave the focus alone on a heartbeat.
    /// </summary>
    public static long WriteState(string directory, InGameState state, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(directory);

        var content = JsonSerializer.SerializeToNode(state, Json)!.AsObject();
        var revision = Revision(content.ToJsonString());

        var document = new JsonObject
        {
            ["schema"] = Schema,
            ["updatedAt"] = now.ToUnixTimeSeconds(),
            ["revision"] = revision
        };
        foreach (var property in content.ToList())
        {
            content.Remove(property.Key);
            document[property.Key] = property.Value;
        }

        WriteAtomically(StateFile(directory), document.ToJsonString(Json));
        return revision;
    }

    /// <summary>
    /// The requests waiting to be acted on. Anything malformed, oversized or stale is removed here
    /// and never returned; a request with a readable id but nonsense in it is answered as refused,
    /// so the game is not left waiting out its timeout.
    /// </summary>
    public static IReadOnlyList<InGameRequest> ReadRequests(string directory, DateTimeOffset now)
    {
        var folder = RequestsDirectory(directory);
        if (!Directory.Exists(folder)) return Array.Empty<InGameRequest>();

        var found = new List<InGameRequest>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(MaximumRequestsPerRead).ToList())
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!IdPattern.IsMatch(id))
            {
                TryDelete(file);
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists) continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (now - new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero) > RequestLifetime)
            {
                TryDelete(file);
                continue;
            }

            if (info.Length > MaximumRequestBytes || !TryParse(file, id, out var request))
            {
                Answer(directory, id, ok: false, "Demande illisible.");
                TryDelete(file);
                continue;
            }

            found.Add(request!);
        }

        return found;
    }

    /// <summary>Answers a request and removes it, so it is acted on once.</summary>
    public static void Complete(string directory, InGameRequest request, bool ok, string message)
    {
        ArgumentNullException.ThrowIfNull(request);
        Answer(directory, request.Id, ok, message);
        TryDelete(Path.Combine(RequestsDirectory(directory), request.Id + ".json"));
    }

    /// <summary>
    /// Removes answers nobody collected -- the game reads and deletes its own, so a leftover belongs
    /// to a game that closed before reading it.
    /// </summary>
    public static void RemoveStaleAnswers(string directory, DateTimeOffset now)
    {
        var folder = RequestsDirectory(directory);
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.done").ToList())
        {
            try
            {
                if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) > TimeSpan.FromMinutes(5))
                    File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool TryParse(string file, string id, out InGameRequest? request)
    {
        request = null;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
            if (node is null) return false;

            // The name on disk is what the game waits on; a body claiming another id is refused.
            if (node["id"]?.GetValue<string>() is not { } bodyId || bodyId != id) return false;

            var action = node["action"]?.GetValue<string>() switch
            {
                "host" => InGameAction.Host,
                "invite" => InGameAction.Invite,
                "join" => InGameAction.Join,
                "cancel" => InGameAction.Cancel,
                _ => (InGameAction?)null
            };
            if (action is null) return false;

            var key = node["key"]?.GetValue<string>() ?? "";
            if (action is InGameAction.Invite or InGameAction.Join)
            {
                // A friend is named by their key; anything that is not one names nobody.
                if (key.Length is 0 or > 200) return false;
                try
                {
                    PeerIdentity.ValidatePublicKey(Convert.FromBase64String(key));
                }
                catch (Exception exception) when (exception is FormatException or ArgumentException)
                {
                    return false;
                }
            }
            else
            {
                key = "";
            }

            request = new InGameRequest(id, action.Value, key);
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or IOException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Answer(string directory, string id, bool ok, string message)
    {
        var folder = RequestsDirectory(directory);
        Directory.CreateDirectory(folder);
        var answer = new JsonObject { ["ok"] = ok, ["message"] = message ?? "" };
        WriteAtomically(Path.Combine(folder, id + ".done"), answer.ToJsonString(Json));
    }

    private static long Revision(string content)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        // Six bytes: well inside a C++ long long and a JavaScript number alike.
        long value = 0;
        for (var i = 0; i < 6; i++) value = (value << 8) | digest[i];
        return value;
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
