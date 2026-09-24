using Avalonia.Controls;
using Avalonia.Interactivity;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// Invitations, for the one game that can use them.
///
/// CubeShelf does not host a lobby, does not open a port and does not know how to join one. The
/// runtime does. PartyBoard's companion creates the lobby and hands its host a short code; what
/// CubeShelf adds is that the code travels encrypted to the friends it is meant for, expires on
/// its own, and appears in the launcher instead of in a chat window.
///
/// That leaves one manual step, and the interface says so rather than hiding it: the companion
/// takes no join-by-argument, so the guest pastes the code into it. Promising otherwise would
/// mean a Join button that opens a window and then appears to do nothing.
/// </summary>
public sealed partial class MainWindow
{
    private PresenceInvite? _outgoingInvite;

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
        var live = _outgoingInvite is not null &&
                   string.Equals(_outgoingInvite.GameId, game.Id, StringComparison.OrdinalIgnoreCase) &&
                   _outgoingInvite.IsPublishable(DateTimeOffset.UtcNow);

        OpenCompanionButton.IsEnabled = installed;
        InvitePayloadBox.IsEnabled = installed;
        PublishInviteButton.IsEnabled = installed;
        CancelInviteButton.IsVisible = live;

        InviteStatusText.Text = DescribeInviteState(game, installed, live);
    }

    private string DescribeInviteState(GameCatalogEntry game, bool installed, bool live)
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

        if (!live)
            return P7($"Crée le salon dans le compagnon, puis colle ici le code qu’il te donne. " +
                      $"Il partira chiffré à tes {recipients} ami(s) et expirera tout seul.",
                      $"Create the lobby in the companion, then paste the code it gives you here. " +
                      $"It goes out encrypted to your {recipients} friend(s) and expires on its own.");

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

    private void OpenCompanion(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is not { } game || !TryResolveCompanion(game, out var companion))
            return;

        try
        {
            _processLauncher.Start(companion, Path.GetDirectoryName(companion) ?? "");
        }
        catch (Exception exception) when (
            exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"Impossible de lancer le compagnon : {exception.Message}",
                   $"The companion could not be started: {exception.Message}"));
        }
    }

    private void PublishInvite(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is not { } game) return;

        var payload = (InvitePayloadBox.Text ?? "").Trim();
        var candidate = new PresenceInvite(
            game.Id, game.Title, payload, DateTimeOffset.UtcNow.Add(PresenceInvite.DefaultLifetime));

        // The only checks CubeShelf can make on a payload it deliberately does not read: that
        // there is one, and that it is not large enough to bloat every heartbeat from here on.
        if (!candidate.IsPublishable(DateTimeOffset.UtcNow))
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                payload.Length == 0
                    ? P7("Colle d’abord le code que le compagnon t’a donné.",
                         "Paste the code the companion gave you first.")
                    : P7("Ce code est trop long pour être transporté.",
                         "That code is too long to carry."));
            return;
        }

        _outgoingInvite = candidate;
        _presence?.RequestPublish(PresencePublishReason.GameChanged);
        RefreshInviteCard();
        ShowToastParity(P7("Invitation", "Invitation"),
            P7("Invitation publiée pour tes amis.", "Invitation published for your friends."));
    }

    private void CancelInvite(object? sender, RoutedEventArgs args)
    {
        _outgoingInvite = null;
        InvitePayloadBox.Text = "";
        // Republish at once: an invitation withdrawn here should stop being offered there,
        // rather than standing for the rest of its quarter of an hour.
        _presence?.RequestPublish(PresencePublishReason.GameChanged);
        RefreshInviteCard();
    }

    /// <summary>
    /// The guest's half. The companion takes no join-by-argument, so the code goes to the
    /// clipboard and the companion is opened for them to paste it. The toast says so, because a
    /// button called Join that silently needed one more step would be the worse lie.
    /// </summary>
    private async void JoinFriendInvite(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: FriendRow row } || row.JoinPayload.Length == 0) return;

        var game = _catalogGames.FirstOrDefault(entry =>
            string.Equals(entry.Id, row.InviteGameId, StringComparison.OrdinalIgnoreCase));

        if (game is null || !TryResolveCompanion(game, out var companion))
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"{row.Name} t’invite sur {row.InviteGameTitle}, mais ce jeu n’est pas installé ici.",
                   $"{row.Name} invites you to {row.InviteGameTitle}, but it is not installed here."));
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(row.JoinPayload);

        try
        {
            _processLauncher.Start(companion, Path.GetDirectoryName(companion) ?? "");
            ShowToastParity(P7("Invitation", "Invitation"),
                P7("Code copié et compagnon ouvert : colle-le dans « Rejoindre un salon ».",
                   "Code copied and companion opened: paste it into “Join a lobby”."));
        }
        catch (Exception exception) when (
            exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToastParity(P7("Invitation", "Invitation"),
                P7($"Code copié, mais le compagnon n’a pas démarré : {exception.Message}",
                   $"Code copied, but the companion did not start: {exception.Message}"));
        }
    }
}
