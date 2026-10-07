using System.Reflection;
using Avalonia.Interactivity;
using CubeShelf.Core.Achievements;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// One RetroAchievements account for every game. The player logs in here once; each game whose
/// package says it can take the session (<see cref="RetroAchievementsLaunch"/>) starts logged in,
/// with nothing to type. A login made inside the game comes back here the same way, and so does
/// a session the server stopped accepting.
///
/// The password goes to retroachievements.org once and is never kept; what is kept is the token
/// the server returns, encrypted for this Windows account. And what each game reports of the
/// player's progress is shared with friends, sealed like the rest of the presence document.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly HttpClient RetroAchievementsHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private RetroAchievementsAccountStore? _raStore;
    private AchievementsCache? _raCache;

    /// <summary>The catalog game the session was last handed to: what its summaries are about.</summary>
    private string? _raGameId;

    private void InitializeAchievements()
    {
        _raStore = new RetroAchievementsAccountStore(_paths.ConfigurationDirectory);
        _raCache = new AchievementsCache(_paths.ConfigurationDirectory);
    }

    /// <summary>
    /// The session, for a game that declares it can take it -- and the in-game directory with it,
    /// which is how the game answers. A session the server refused is not handed out again.
    /// </summary>
    private void AddRetroAchievementsEnvironment(GameCatalogEntry game, IDictionary<string, string?> environment)
    {
        if (_raStore?.Load(out var rejected) is not { } session || rejected) return;
        if (!RetroAchievementsLaunch.IsSupportedBy(game.Executable)) return;
        try
        {
            Directory.CreateDirectory(InGameDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        environment[InGameBridge.DirectoryVariable] = InGameDirectory;
        RetroAchievementsLaunch.AddTo(environment, session);
        _raGameId = game.Id;
    }

    /// <summary>On the in-game timer: what the game said about the session, and where the player stands now.</summary>
    private void PumpAchievements()
    {
        if (_raStore is null || _raCache is null || !Directory.Exists(InGameDirectory)) return;

        if (AchievementsBridge.TakeSessionEvent(InGameDirectory, DateTimeOffset.UtcNow) is { } said)
        {
            switch (said.Kind)
            {
                case RetroAchievementsEventKind.Login:
                    var previous = _raStore.Load();
                    if (previous is not null && !string.Equals(previous.User, said.User, StringComparison.OrdinalIgnoreCase)) _raCache.Clear();
                    _raStore.Save(new RetroAchievementsSession(said.User!, said.Token!, DateTimeOffset.UtcNow, "game"));
                    ShowToastParity("RetroAchievements", P7(
                        $"Connecté en tant que {said.User} depuis le jeu : tous tes jeux le seront aussi.",
                        $"Logged in as {said.User} from the game: all your games will be too."));
                    break;
                case RetroAchievementsEventKind.Logout:
                    _raStore.Clear();
                    _raCache.Clear();
                    _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
                    break;
                case RetroAchievementsEventKind.Rejected:
                    _raStore.MarkRejected();
                    Notify("RetroAchievements", P7(
                        "RetroAchievements n’accepte plus ta session : reconnecte-toi sur la page Mon profil.",
                        "RetroAchievements no longer accepts your session: log in again on the My profile page."), showFriends: false);
                    break;
            }
            RefreshRetroAchievementsUi();
        }

        if (_raGameId is { } gameId && AchievementsBridge.ReadSummary(InGameDirectory) is { } summary &&
            _raStore.Load() is { } session && string.Equals(summary.User, session.User, StringComparison.OrdinalIgnoreCase) &&
            _raCache.Update(gameId, summary))
        {
            _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
        }
    }

    /// <summary>What goes into the presence document: every game's last summary, for the account logged in.</summary>
    private IReadOnlyList<SharedAchievements>? SharedAchievementsNow()
    {
        if (_raCache is null || _raStore?.Load() is not { } session) return null;
        return _raCache.All
            .Where(pair => string.Equals(pair.Value.User, session.User, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new SharedAchievements(pair.Key, pair.Value.RaGameId, pair.Value.Title, pair.Value.User,
                pair.Value.Total, pair.Value.Points, pair.Value.TotalPoints, pair.Value.UnlockedIds, pair.Value.UpdatedAt))
            .ToArray();
    }

    /// <summary>A friend's progress in one game, for the game's own menu.</summary>
    private static InGameAchievements? FriendAchievementsFor(PresenceSnapshot? known, string gameId)
    {
        var entry = known?.Achievements?.Select(SharedAchievements.Sanitized).OfType<SharedAchievements>()
            .FirstOrDefault(shared => string.Equals(shared.GameId, gameId, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : new InGameAchievements(entry.User, entry.RaGameId, entry.Total, entry.Points, entry.TotalPoints, entry.UnlockedIds);
    }

    /// <summary>"🏆 Mario Party 4 : 31/58 · 280 pts", for the friends list.</summary>
    private string DescribeFriendAchievements(PresenceSnapshot? known)
    {
        var entries = known?.Achievements?.Select(SharedAchievements.Sanitized).OfType<SharedAchievements>().Take(2).ToArray();
        if (entries is not { Length: > 0 }) return "";
        return string.Join(" • ", entries.Select(entry =>
            P7($"🏆 {entry.Title} : {entry.Unlocked}/{entry.Total} · {entry.Points} pts",
               $"🏆 {entry.Title}: {entry.Unlocked}/{entry.Total} · {entry.Points} pts")));
    }

    // ----------------------------------------------------------------- the profile page

    private void RefreshRetroAchievementsUi()
    {
        if (RaStatusText is null || _raStore is null) return;
        var session = _raStore.Load(out var rejected);
        RaLoginPanel.IsVisible = session is null || rejected;
        RaLogoutButton.IsVisible = session is not null;
        RaStatusText.Text = session is null
            ? P7("Pas connecté. Connecte-toi une fois ici : les jeux qui gèrent RetroAchievements seront connectés tout seuls.",
                 "Not logged in. Log in once here: games that support RetroAchievements will be logged in by themselves.")
            : rejected
                ? P7($"RetroAchievements n’accepte plus la session de {session.User} : reconnecte-toi.",
                     $"RetroAchievements no longer accepts {session.User}’s session: log in again.")
                : P7($"Connecté en tant que {session.User}. Les jeux qui gèrent RetroAchievements s’y connectent tout seuls.",
                     $"Logged in as {session.User}. Games that support RetroAchievements log in by themselves.");
    }

    private async void LoginRetroAchievements(object? sender, RoutedEventArgs args)
    {
        if (_raStore is null || _raCache is null) return;
        var user = RaUserBox.Text?.Trim() ?? "";
        var password = RaPasswordBox.Text ?? "";
        RaLoginButton.IsEnabled = false;
        RaStatusText.Text = P7("Connexion à RetroAchievements…", "Logging in to RetroAchievements…");
        try
        {
            var result = await SignInRetroAchievementsAsync(user, password);
            if (!result.Succeeded || result.Session is null)
            {
                RaStatusText.Text = result.Error ?? P7("Connexion impossible.", "Could not log in.");
                return;
            }

            RefreshRetroAchievementsUi();
            ShowToastParity("RetroAchievements", P7(
                $"Connecté en tant que {result.Session.User} ({result.Score} pts). Lance un jeu : il sera connecté tout seul.",
                $"Logged in as {result.Session.User} ({result.Score} pts). Start a game: it will be logged in by itself."));
        }
        finally
        {
            // The password is not kept anywhere, the box included.
            RaPasswordBox.Text = "";
            RaLoginButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Logs in and keeps the session: the profile page and the first-start wizard both come here.
    /// Another account than the one before starts from an empty progress cache.
    /// </summary>
    private async Task<RetroAchievementsLoginResult> SignInRetroAchievementsAsync(string user, string password)
    {
        if (_raStore is null || _raCache is null) return new RetroAchievementsLoginResult(false, Error: P7("Indisponible.", "Unavailable."));
        var result = await new RetroAchievementsLogin(RetroAchievementsHttp, UserAgent()).LoginAsync(user, password);
        if (!result.Succeeded || result.Session is null) return result;

        var previous = _raStore.Load();
        if (previous is not null && !string.Equals(previous.User, result.Session.User, StringComparison.OrdinalIgnoreCase)) _raCache.Clear();
        _raStore.Save(result.Session);
        return result;
    }

    private void LogoutRetroAchievements(object? sender, RoutedEventArgs args)
    {
        _raStore?.Clear();
        _raCache?.Clear();
        RefreshRetroAchievementsUi();
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
    }

    /// <summary>The server only answers clients it can identify: product, version, system.</summary>
    private static string UserAgent()
    {
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0] ?? "0";
        var system = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return $"CubeShelf/{version} ({system})";
    }
}
