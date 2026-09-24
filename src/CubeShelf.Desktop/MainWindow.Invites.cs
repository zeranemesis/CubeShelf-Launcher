using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// Invitations, for the one game that can use them.
///
/// CubeShelf does not host a lobby and does not know how to join one. The runtime does. What
/// CubeShelf owns is everything around it: it knows the player's name, which disc they use and
/// who their friends are, and it can carry an invitation encrypted to exactly the people it is
/// meant for. So it hands the companion what it already knows and lets it do the network part.
///
/// Three actions, each returning what happened in words instead of showing it, because they are
/// asked for from three places: the game's F1 menu, where CubeShelf is minimised behind the game
/// and a toast of its own would reach nobody; the Friends page; and nowhere else -- the game page
/// used to carry a card for this and does not any more.
/// </summary>
public sealed partial class MainWindow
{
    private PresenceInvite? _outgoingInvite;
    private CancellationTokenSource? _hostWatch;

    /// <summary>Where the companion is told to drop the invitation it just created.</summary>
    private string PendingInviteFile =>
        Path.Combine(_paths.ConfigurationDirectory, "pending-invite.txt");

    /// <summary>The game that can be played together, if the catalog has one. Today, Mario Party 4.</summary>
    private GameCatalogEntry? OnlineGame() => _catalogGames.FirstOrDefault(OnlineCompanion.IsSupported);

    private bool IsHosting =>
        _hostWatch is { IsCancellationRequested: false } ||
        (_outgoingInvite is not null && _outgoingInvite.IsPublishable(DateTimeOffset.UtcNow));

    /// <summary>
    /// Why this player cannot host right now, in words, or null when they can. Every reason names
    /// the one thing to do about it, because it is read in the game, far from any setting.
    /// </summary>
    private string? HostingBlocker(GameCatalogEntry game)
    {
        if (!TryResolveCompanion(game, out var companion))
            return P7($"Installe {game.RuntimeName} depuis CubeShelf : son compagnon en ligne vient avec.",
                      $"Install {game.RuntimeName} from CubeShelf: its online companion comes with it.");

        if (!OnlineCompanion.SupportsLauncherInvites(companion))
            return P7($"Ton {game.RuntimeName} est trop ancien pour les invitations : mets-le à jour depuis CubeShelf.",
                      $"Your {game.RuntimeName} is too old for invitations: update it from CubeShelf.");

        if (!HasIdentity)
            return P7("Crée d’abord ton identité : CubeShelf, page Mon profil.",
                      "Create your identity first: CubeShelf, My profile page.");

        if (!_preferences.PresencePublishEnabled || _presence is null)
            return P7("Ta présence n’est pas publiée, donc personne ne recevrait l’invitation : CubeShelf, page Mon profil.",
                      "Your presence is not published, so nobody would receive the invitation: CubeShelf, My profile page.");

        if ((_friends?.ActiveRecipients().Count ?? 0) == 0)
            return P7("Ajoute un ami dans CubeShelf : une invitation ne part qu’à des amis.",
                      "Add a friend in CubeShelf: an invitation only goes to friends.");

        return null;
    }

    /// <summary>The companion beside the runtime CubeShelf installed, if both are there.</summary>
    private bool TryResolveCompanion(GameCatalogEntry game, out string path)
    {
        var runtime = _installer.GetStatus(game.Id);
        var executable = runtime.IsInstalled ? runtime.ExecutablePath : ResolveApplicationPath(game.Executable);
        return OnlineCompanion.TryResolve(executable, game.OnlineCompanion, out path);
    }

    /// <summary>
    /// What CubeShelf already knows and the companion would otherwise ask for. Passed through the
    /// environment rather than the command line because an invitation is a bearer token, and a
    /// command line is the one of the two that Windows shows in its own task manager.
    /// </summary>
    private Dictionary<string, string?> CompanionEnvironment(GameCatalogEntry game)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        var name = (_preferences.FriendsDisplayName ?? "").Trim();
        if (name.Length > 0) environment["PARTYBOARD_ONLINE_NICKNAME"] = name;

        var disc = ResolveApplicationPath(game.DiscImage);
        if (!string.IsNullOrWhiteSpace(disc) && File.Exists(disc))
            environment["PARTYBOARD_ONLINE_DISC"] = disc;

        // The game the companion starts for online play inherits this, so its F1 menu still
        // reaches CubeShelf.
        foreach (var pair in InGameEnvironment(game)) environment[pair.Key] = pair.Value;

        return environment;
    }

    private bool TryStartCompanion(
        GameCatalogEntry game,
        Dictionary<string, string?> environment,
        IReadOnlyList<string>? arguments,
        out string error)
    {
        error = "";
        if (!TryResolveCompanion(game, out var companion))
        {
            error = P7($"Le compagnon de {game.RuntimeName} est introuvable.", $"{game.RuntimeName}’s companion is missing.");
            return false;
        }

        try
        {
            _processLauncher.Start(
                companion, Path.GetDirectoryName(companion) ?? "", environment, arguments);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = P7($"Impossible de lancer le compagnon : {exception.Message}",
                       $"The companion could not be started: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a lobby. The companion creates it and writes the invitation where we told it to; we
    /// watch that file and publish what lands there, to everyone or to the one friend named.
    /// </summary>
    private (bool Started, string Message) StartHosting(GameCatalogEntry game, string? forFriendKey)
    {
        if (HostingBlocker(game) is { } blocker) return (false, blocker);
        if (_hostWatch is { IsCancellationRequested: false })
            return (false, P7("Un salon est déjà en préparation.", "A lobby is already being prepared."));

        // A leftover from a previous lobby would be published as if it were this one's.
        TryDeletePendingInvite();

        var environment = CompanionEnvironment(game);
        environment["PARTYBOARD_ONLINE_INVITE_OUT"] = PendingInviteFile;
        if (!TryStartCompanion(game, environment, new[] { "--host" }, out var error)) return (false, error);

        _hostWatch?.Dispose();
        _hostWatch = CancellationTokenSource.CreateLinkedTokenSource(
            _friendsLifetime?.Token ?? CancellationToken.None);
        _ = WatchForInvitationAsync(game, forFriendKey ?? "", _hostWatch.Token);
        WriteInGameState();
        return (true, "");
    }

    /// <summary>
    /// Waits for the companion to publish its invitation. Bounded: a player who changes their mind
    /// and closes the companion must not leave a task watching a file forever.
    /// </summary>
    private async Task WatchForInvitationAsync(
        GameCatalogEntry game, string forFriendKey, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                string payload;
                try
                {
                    if (!File.Exists(PendingInviteFile)) continue;
                    payload = File.ReadAllText(PendingInviteFile).Trim();
                }
                catch (IOException)
                {
                    // The companion may still be moving it into place; look again next tick.
                    continue;
                }

                if (payload.Length == 0) continue;
                TryDeletePendingInvite();
                await Dispatcher.UIThread.InvokeAsync(() => PublishInvitation(game, payload, forFriendKey));
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => ShowToastParity(P7("Invitation", "Invitation"),
                P7("Le compagnon n’a pas créé de salon. Rien n’a été envoyé.",
                   "The companion did not create a lobby. Nothing was sent.")));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_hostWatch is not null && _hostWatch.Token == cancellationToken)
                {
                    _hostWatch.Dispose();
                    _hostWatch = null;
                }
                WriteInGameState();
                if (FriendsView.IsVisible) RefreshFriendsView();
            });
        }
    }

    private void PublishInvitation(GameCatalogEntry game, string payload, string forFriendKey)
    {
        var candidate = new PresenceInvite(
            game.Id, game.Title, payload, DateTimeOffset.UtcNow.Add(PresenceInvite.DefaultLifetime), forFriendKey);

        // The only checks CubeShelf can make on a payload it deliberately does not read: that
        // there is one, and that it is not large enough to bloat every heartbeat from here on.
        if (!candidate.IsPublishable(DateTimeOffset.UtcNow))
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                payload.Length == 0
                    ? P7("Aucun code d’invitation à envoyer.", "No invitation code to send.")
                    : P7("Ce code est trop long pour être transporté.", "That code is too long to carry."));
            return;
        }

        _outgoingInvite = candidate;
        _presence?.RequestPublish(PresencePublishReason.GameChanged);
        WriteInGameState();
        ShowToastParity(P7("Invitation", "Invitation"),
            forFriendKey.Length > 0
                ? P7("Salon prêt, invitation envoyée à ton ami.", "Lobby ready, invitation sent to your friend.")
                : P7("Salon prêt, invitation envoyée à tes amis.", "Lobby ready, invitation sent to your friends."));
    }

    /// <summary>Withdraws the invitation, and stops waiting for one that has not arrived yet.</summary>
    private string CancelHosting()
    {
        _hostWatch?.Cancel();
        _outgoingInvite = null;
        TryDeletePendingInvite();
        // Republish at once: an invitation withdrawn here should stop being offered there,
        // rather than standing for the rest of its quarter of an hour.
        _presence?.RequestPublish(PresencePublishReason.GameChanged);
        WriteInGameState();
        return P7("Invitation retirée.", "Invitation withdrawn.");
    }

    private void TryDeletePendingInvite()
    {
        try
        {
            if (File.Exists(PendingInviteFile)) File.Delete(PendingInviteFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Joins a friend's lobby. The companion opens already holding the invitation, the pseudo and
    /// the disc, so there is nothing to paste and nothing to pick. Against a companion too old to
    /// be driven, the code goes to the clipboard and the message says so.
    /// </summary>
    private (bool Started, string Message) StartJoining(GameCatalogEntry game, string friendHandle, string payload)
    {
        if (!TryResolveCompanion(game, out var companion))
            return (false, P7($"{friendHandle} t’invite sur {game.Title}, mais {game.RuntimeName} n’est pas installé ici.",
                              $"{friendHandle} invites you to {game.Title}, but {game.RuntimeName} is not installed here."));

        var environment = CompanionEnvironment(game);

        if (!OnlineCompanion.SupportsLauncherInvites(companion))
        {
            _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(payload);
            return TryStartCompanion(game, environment, null, out var oldError)
                ? (true, P7($"Ton {game.RuntimeName} est trop ancien pour rejoindre tout seul. Le code est copié : colle-le dans le compagnon, puis « Rejoindre ».",
                            $"Your {game.RuntimeName} is too old to join by itself. The code is copied: paste it into the companion, then press Join."))
                : (false, oldError);
        }

        environment["PARTYBOARD_ONLINE_INVITE"] = payload;
        if (!TryStartCompanion(game, environment, new[] { "--join" }, out var error)) return (false, error);

        // Both sides need byte-identical files, and only the companion can say so -- but a disc
        // we never configured is a failure CubeShelf can see coming.
        return environment.ContainsKey("PARTYBOARD_ONLINE_DISC")
            ? (true, "")
            : (true, P7("Choisis ton disque dans le compagnon : la partie commencera dès qu’il sera vérifié.",
                        "Pick your disc in the companion: it will join as soon as the file is verified."));
    }

    /// <summary>The Friends page's Join button.</summary>
    private void JoinFriendInvite(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || row.JoinPayload.Length == 0) return;

        var game = _catalogGames.FirstOrDefault(entry =>
            string.Equals(entry.Id, row.InviteGameId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"{row.Name} t’invite sur {row.InviteGameTitle}, qui n’est pas dans ta bibliothèque.",
                   $"{row.Name} invites you to {row.InviteGameTitle}, which is not on your shelf."));
            return;
        }

        var (started, message) = StartJoining(game, row.Name, row.JoinPayload);
        ShowToastParity(P7("Invitation", "Invitation"),
            message.Length > 0 ? message
                : P7($"Tu rejoins {row.Name}. Le compagnon vérifie ton disque puis entre dans le salon.",
                     $"Joining {row.Name}. The companion checks your disc, then enters the lobby."));
        if (started) RefreshFriendsView();
    }

    /// <summary>The Friends page's Invite button: a lobby, and the invitation addressed to them alone.</summary>
    private void InviteFriend(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || OnlineGame() is not { } game) return;

        var (started, message) = StartHosting(game, row.PublicKey);
        ShowToastParity(P7("Invitation", "Invitation"),
            started
                ? P7($"Le compagnon prépare le salon. {row.Name} recevra l’invitation dès qu’il sera prêt.",
                     $"The companion is preparing the lobby. {row.Name} gets the invitation as soon as it is ready.")
                : message);
        RefreshFriendsView();
    }
}
