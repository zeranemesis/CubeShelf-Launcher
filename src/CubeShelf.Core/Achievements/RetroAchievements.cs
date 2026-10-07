using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CubeShelf.Core.Social;

namespace CubeShelf.Core.Achievements;

/// <summary>
/// A RetroAchievements account as CubeShelf keeps it: the user name and the session token the
/// server handed back at login. Never the password.
/// </summary>
/// <param name="Origin">"cubeshelf" when the user logged in here, "game" when a game logged in and told us.</param>
public sealed record RetroAchievementsSession(string User, string Token, DateTimeOffset Since, string Origin = "cubeshelf")
{
    /// <summary>A user name RetroAchievements could have issued: letters, digits, a few separators.</summary>
    public static bool IsUser(string? user) =>
        !string.IsNullOrEmpty(user) && user.Length <= 64 && Regex.IsMatch(user, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>A token the server could have issued. Anything else is refused before it goes anywhere.</summary>
    public static bool IsToken(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= 128 && Regex.IsMatch(token, "^[A-Za-z0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

/// <summary>
/// Where the session lives between runs: <c>retroachievements.json</c> in the profile, the token
/// encrypted for this Windows account (DPAPI, with its own purpose so it never opens as anything
/// else), and protected by the file's permissions elsewhere -- the same level as the identity.
/// </summary>
public sealed class RetroAchievementsAccountStore
{
    private const string Purpose = "retroachievements";
    private readonly string _file;
    private readonly object _gate = new();

    public RetroAchievementsAccountStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _file = Path.Combine(Path.GetFullPath(configurationDirectory), "retroachievements.json");
    }

    public string FilePath => _file;

    /// <summary>
    /// The session, or null: none saved, unreadable, or protected by another Windows account. A
    /// session a game reported as rejected loads with <paramref name="rejected"/> set, so the page
    /// can ask for the password again instead of handing a dead token to every game.
    /// </summary>
    public RetroAchievementsSession? Load(out bool rejected)
    {
        rejected = false;
        lock (_gate)
        {
            if (!File.Exists(_file)) return null;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(_file)) as JsonObject;
                var user = node?["user"]?.GetValue<string>();
                var stored = node?["token"]?.GetValue<string>();
                var origin = node?["origin"]?.GetValue<string>() ?? "cubeshelf";
                var since = node?["since"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UnixEpoch;
                rejected = node?["rejected"]?.GetValue<bool>() == true;
                if (!RetroAchievementsSession.IsUser(user) || string.IsNullOrEmpty(stored)) return null;

                string token;
                if (stored.StartsWith(IdentityProtection.Marker, StringComparison.Ordinal))
                {
                    if (!IdentityProtection.Available) return null;
                    token = Encoding.UTF8.GetString(IdentityProtection.Unprotect(stored, Purpose));
                }
                else
                {
                    token = stored;
                }
                return RetroAchievementsSession.IsToken(token) ? new RetroAchievementsSession(user!, token, since, origin) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                                   or InvalidOperationException or FormatException
                                                   or System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }
    }

    public RetroAchievementsSession? Load() => Load(out _);

    public void Save(RetroAchievementsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!RetroAchievementsSession.IsUser(session.User) || !RetroAchievementsSession.IsToken(session.Token))
            throw new ArgumentException("Session RetroAchievements invalide.", nameof(session));
        Write(session, rejected: false);
    }

    /// <summary>A game was refused with this token: kept, but marked, until the user logs in again.</summary>
    public void MarkRejected()
    {
        var session = Load();
        if (session is not null) Write(session, rejected: true);
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_file)) File.Delete(_file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void Write(RetroAchievementsSession session, bool rejected)
    {
        lock (_gate)
        {
            var token = IdentityProtection.Available
                ? IdentityProtection.Protect(Encoding.UTF8.GetBytes(session.Token), Purpose)
                : session.Token;
            var document = new JsonObject
            {
                ["user"] = session.User,
                ["token"] = token,
                ["origin"] = session.Origin,
                ["since"] = session.Since,
                ["rejected"] = rejected
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, document.ToJsonString());
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (IOException)
                {
                }
            }
            File.Move(temporary, _file, true);
        }
    }
}

public sealed record RetroAchievementsLoginResult(bool Succeeded, RetroAchievementsSession? Session = null, string? Error = null, int Score = 0);

/// <summary>
/// Logs in to RetroAchievements with a user name and password, once, to get the session token
/// every game then uses. The same request the games make themselves (<c>login2</c>), over https,
/// to the server and nowhere else. The password is sent once and never kept.
/// </summary>
public sealed class RetroAchievementsLogin
{
    public static readonly Uri Endpoint = new("https://retroachievements.org/dorequest.php");
    private const int MaximumResponseBytes = 64 * 1024;

    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly Uri _endpoint;

    /// <param name="userAgent">The server only answers clients it can identify: "CubeShelf/0.10.0 (Windows)".</param>
    public RetroAchievementsLogin(HttpClient http, string userAgent, Uri? endpoint = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _userAgent = userAgent;
        _endpoint = endpoint ?? Endpoint;
    }

    public async Task<RetroAchievementsLoginResult> LoginAsync(string user, string password, CancellationToken cancellationToken = default)
    {
        user = (user ?? "").Trim();
        if (!RetroAchievementsSession.IsUser(user)) return new(false, Error: "Nom d’utilisateur RetroAchievements invalide.");
        if (string.IsNullOrEmpty(password)) return new(false, Error: "Mot de passe manquant.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["r"] = "login2", ["u"] = user, ["p"] = password })
        };
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var body = await ReadCappedAsync(response, timeout.Token).ConfigureAwait(false);
            if (body is null) return new(false, Error: "Réponse de RetroAchievements trop longue ou illisible.");

            JsonObject? json;
            try
            {
                json = JsonNode.Parse(body) as JsonObject;
            }
            catch (JsonException)
            {
                json = null;
            }
            if (json is null)
                return new(false, Error: response.IsSuccessStatusCode
                    ? "Réponse de RetroAchievements illisible."
                    : $"RetroAchievements a répondu {(int)response.StatusCode}.");

            if (json["Success"]?.GetValueKind() == JsonValueKind.True)
            {
                var name = json["User"]?.GetValue<string>();
                var token = json["Token"]?.GetValue<string>();
                if (!RetroAchievementsSession.IsUser(name) || !RetroAchievementsSession.IsToken(token))
                    return new(false, Error: "RetroAchievements a répondu sans jeton utilisable.");
                var score = json["Score"]?.GetValueKind() == JsonValueKind.Number ? json["Score"]!.GetValue<int>() : 0;
                return new(true, new RetroAchievementsSession(name!, token!, DateTimeOffset.UtcNow), Score: score);
            }

            var error = json["Error"]?.GetValueKind() == JsonValueKind.String ? json["Error"]!.GetValue<string>() : null;
            var code = json["Code"]?.GetValueKind() == JsonValueKind.String ? json["Code"]!.GetValue<string>() : null;
            return new(false, Error: response.StatusCode == HttpStatusCode.Unauthorized || code == "invalid_credentials"
                ? "Nom d’utilisateur ou mot de passe incorrect."
                : "Connexion refusée par RetroAchievements" + (error is null ? "." : " : " + ChatText.Clean(error)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, Error: "RetroAchievements ne répond pas.");
        }
        catch (HttpRequestException exception)
        {
            return new(false, Error: "RetroAchievements injoignable : " + exception.Message);
        }
    }

    private static async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>
/// How a game says it can take the CubeShelf RetroAchievements session, and how it is handed over.
///
/// The session goes only to a game whose package declares the capability in its manifest, and
/// only in the environment of the process CubeShelf starts: nothing is written for the game to
/// find, and a game that never asked is never given anyone's token.
/// </summary>
public static class RetroAchievementsLaunch
{
    public const string Capability = "retroachievements-login";
    public const string UserVariable = "CUBESHELF_RA_USER";
    public const string TokenVariable = "CUBESHELF_RA_TOKEN";

    public static void AddTo(IDictionary<string, string?> environment, RetroAchievementsSession session)
    {
        environment[UserVariable] = session.User;
        environment[TokenVariable] = session.Token;
    }

    /// <summary>Whether the package beside <paramref name="executable"/> declares <see cref="Capability"/>.</summary>
    public static bool IsSupportedBy(string? executable) => Library.RuntimeManifest.HasCapability(executable, Capability);
}
