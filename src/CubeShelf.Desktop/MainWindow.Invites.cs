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
/// Neither side types anything. The host clicks once and CubeShelf reads the invitation the
/// companion writes; the guest clicks once and the companion opens already joining. The manual
/// paste box stays for the case CubeShelf cannot cover: a lobby the player opened themselves.
/// </summary>
public sealed partial class MainWindow
{
    private PresenceInvite? _outgoingInvite;
    private CancellationTokenSource? _hostWatch;

    /// <summary>Where the companion is told to drop the invitation it just created.</summary>
    private string PendingInviteFile =>
        Path.Combine(_paths.ConfigurationDirectory, "pending-invite.txt");

    /// <summary>
    /// Shows the card only for a game whose catalog entry names a companion. Mario Party 4 does;
    /// Soulcalibur II and Super Mario Strikers do not, and for them there is nothing honest to
    /// put here, so nothing is put here.
    /// </summary>
    private void RefreshInviteCard()
    {
        if (InviteCard is null) return;

        var game = _selectedGame;
        InviteCard.IsVisible = OnlineCompanion.IsSupported(game);
        if (!InviteCard.IsVisible || game is null) return;

        var installed = TryResolveCompanion(game, out _);
        var waiting = _hostWatch is { IsCancellationRequested: false };
        var live = _outgoingInvite is not null &&
                   string.Equals(_outgoingInvite.GameId, game.Id, StringComparison.OrdinalIgnoreCase) &&
                   _outgoingInvite.IsPublishable(DateTimeOffset.UtcNow);

        HostLobbyButton.IsEnabled = installed && !waiting && !live;
        OpenCompanionButton.IsEnabled = installed;
        InvitePayloadBox.IsEnabled = installed;
        PublishInviteButton.IsEnabled = installed;
        CancelInviteButton.IsVisible = live || waiting;

        InviteStatusText.Text = DescribeInviteState(game, installed, live, waiting);
    }

    private string DescribeInviteState(GameCatalogEntry game, bool installed, bool live, bool waiting)
    {
        if (!installed)
            return P7($"Installe {game.RuntimeName} : son compagnon en ligne vient avec.",
                      $"Install {game.RuntimeName}: its online companion comes with it.");

        if (!_preferences.PresencePublishEnabled || _presence is null)
            return P7("Tu ne publies pas ta présence, donc personne ne recevrait l’invitation. " +
                      "Active la publication dans les Paramètres.",
                      "You are not publishing your presence, so nobody would receive the invitation. " +
                      "Turn publishing on in Settings.");

        var recipients = _friends?.ActiveRecipients().Count ?? 0;
        if (recipients == 0)
            return P7("Ajoute un ami : une invitation ne part qu’à des amis.",
                      "Add a friend: an invitation only goes to friends.");

        if (waiting)
            return P7("Le compagnon prépare le salon. Il vérifie ton disque en entier, ce qui prend " +
                      "un moment ; dès qu’il a l’invitation, CubeShelf la publie tout seul.",
                      "The companion is preparing the lobby. It hashes your whole disc, which takes a " +
                      "moment; as soon as it has the invitation, CubeShelf publishes it by itself.");

        if (!live)
            return P7($"Un clic : le compagnon crée le salon et CubeShelf envoie l’invitation, chiffrée, " +
                      $"à tes {recipients} ami(s).",
                      $"One click: the companion creates the lobby and CubeShelf sends the invitation, " +
                      $"encrypted, to your {recipients} friend(s).");

        var remaining = _outgoingInvite!.ExpiresAt - DateTimeOffset.UtcNow;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return P7($"Invitation publiée, elle expire dans {minutes} min. Tes amis la verront à leur " +
                  "prochaine lecture, pas immédiatement : c’est une invitation posée, pas une sonnerie.",
                  $"Invitation published, it expires in {minutes} min. Friends see it at their next " +
                  "poll, not immediately: it is an invitation left on the table, not a ring.");
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

        return environment;
    }

    private bool TryStartCompanion(
        GameCatalogEntry game,
        Dictionary<string, string?> environment,
        IReadOnlyList<string>? arguments)
    {
        if (!TryResolveCompanion(game, out var companion)) return false;

        try
        {
            _processLauncher.Start(
                companion, Path.GetDirectoryName(companion) ?? "", environment, arguments);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"Impossible de lancer le compagnon : {exception.Message}",
                   $"The companion could not be started: {exception.Message}"));
            return false;
        }
    }

    private void OpenCompanion(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is { } game) TryStartCompanion(game, CompanionEnvironment(game), null);
    }

    /// <summary>
    /// The host's one click. The companion creates the lobby and writes the invitation where we
    /// told it to; we watch that file and publish what lands there.
    /// </summary>
    private void HostLobbyAndInvite(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is not { } game) return;

        // A leftover from a previous lobby would be published as if it were this one's.
        TryDeletePendingInvite();

        var environment = CompanionEnvironment(game);
        environment["PARTYBOARD_ONLINE_INVITE_OUT"] = PendingInviteFile;
        if (!TryStartCompanion(game, environment, new[] { "--host" })) return;

        _hostWatch?.Cancel();
        _hostWatch = CancellationTokenSource.CreateLinkedTokenSource(
            _friendsLifetime?.Token ?? CancellationToken.None);
        _ = WatchForInvitationAsync(game, _hostWatch.Token);
        RefreshInviteCard();
    }

    /// <summary>
    /// Waits for the companion to publish its invitation. Bounded: a player who changes their mind
    /// and closes the companion must not leave a task watching a file forever.
    /// </summary>
    private async Task WatchForInvitationAsync(GameCatalogEntry game, CancellationToken cancellationToken)
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
                await Dispatcher.UIThread.InvokeAsync(() => PublishInvitation(game, payload));
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
                RefreshInviteCard();
            });
        }
    }

    private void PublishInvitation(GameCatalogEntry game, string payload)
    {
        var candidate = new PresenceInvite(
            game.Id, game.Title, payload, DateTimeOffset.UtcNow.Add(PresenceInvite.DefaultLifetime));

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
        RefreshInviteCard();
        ShowToastParity(P7("Invitation", "Invitation"),
            P7("Salon prêt, invitation envoyée à tes amis.", "Lobby ready, invitation sent to your friends."));
    }

    /// <summary>The manual path, for a lobby the player opened themselves.</summary>
    private void PublishInvite(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is { } game)
            PublishInvitation(game, (InvitePayloadBox.Text ?? "").Trim());
    }

    private void CancelInvite(object? sender, RoutedEventArgs args)
    {
        _hostWatch?.Cancel();
        _outgoingInvite = null;
        InvitePayloadBox.Text = "";
        TryDeletePendingInvite();
        // Republish at once: an invitation withdrawn here should stop being offered there,
        // rather than standing for the rest of its quarter of an hour.
        _presence?.RequestPublish(PresencePublishReason.GameChanged);
        RefreshInviteCard();
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
    /// The guest's one click. The companion opens already holding the invitation, the pseudo and
    /// the disc, so there is nothing to paste and nothing to pick.
    /// </summary>
    private void JoinFriendInvite(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || row.JoinPayload.Length == 0) return;

        var game = _catalogGames.FirstOrDefault(entry =>
            string.Equals(entry.Id, row.InviteGameId, StringComparison.OrdinalIgnoreCase));

        if (game is null || !TryResolveCompanion(game, out _))
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"{row.Name} t’invite sur {row.InviteGameTitle}, mais ce jeu n’est pas installé ici.",
                   $"{row.Name} invites you to {row.InviteGameTitle}, but it is not installed here."));
            return;
        }

        var environment = CompanionEnvironment(game);
        environment["PARTYBOARD_ONLINE_INVITE"] = row.JoinPayload;
        if (!TryStartCompanion(game, environment, new[] { "--join" })) return;

        // Both sides need byte-identical files, and only the companion can say so -- but a disc
        // we never configured is a failure CubeShelf can see coming.
        var missingDisc = !environment.ContainsKey("PARTYBOARD_ONLINE_DISC");
        ShowToastParity(P7("Invitation", "Invitation"),
            missingDisc
                ? P7($"Salon de {row.Name} ouvert. Choisis ton disque dans le compagnon : la partie " +
                     "commencera dès qu’il sera vérifié.",
                     $"{row.Name}’s lobby opened. Pick your disc in the companion: it will join as " +
                     "soon as the file is verified.")
                : P7($"Tu rejoins {row.Name}. Le compagnon vérifie ton disque puis entre dans le salon.",
                     $"Joining {row.Name}. The companion checks your disc, then enters the lobby."));
    }
}
