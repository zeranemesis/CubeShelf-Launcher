using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Online;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private void RefreshSimpleRoomLabels()
    {
        RoomCodeHeading.Text = P7("Rejoindre avec un code", "Join with a code");
        RoomCodeHelp.Text = P7("Salons publics : saisis le code court de ton ami. Les salons privés se rejoignent depuis son invitation dans Amis.",
            "Public rooms: enter your friend's short code. Private rooms use the invitation on the Friends page.");
        RoomCodeBox.Watermark = "ABCD-1234";
        JoinRoomCodeButton.Content = P7("Vérifier le salon", "Check room");
        CreateRoomButton.Content = P7("Créer une partie", "Create game");
    }

    private void RefreshSimpleRoomState(RoomState room)
    {
        MyRoomReadySummary.Text = room.State == "playing" ? P7("Partie en cours", "Game in progress")
            : room.CanStart ? P7("✓ Joueurs prêts — disque et mods validés", "✓ Players ready — disc and mods verified")
            : !room.DiscsMatch ? P7("Disques différents : le lancement est bloqué.", "Different discs: launch is blocked.")
            : !room.ModsMatch ? P7("Les mods ne correspondent pas encore.", "Mods do not match yet.")
            : P7("En attente des joueurs et de leur vérification…", "Waiting for players and verification…");
        MyRoomJoinCode.Text = room.Public && room.RelayRoom is { Length: 32 } id && id.All(Uri.IsHexDigit) ? PublicRoomCode.Display(id)
            : room.Public ? P7("Code en préparation…", "Preparing code…") : P7("Salon privé", "Private room");
        CopyMyRoomCodeButton.Content = room.Public ? P7("Copier le code court", "Copy short code") : P7("Copier l’invitation privée", "Copy private invite");
        CopyMyRoomCodeButton.IsEnabled = room.Public
            ? room.RelayRoom is { Length: 32 } code && code.All(Uri.IsHexDigit) : room.Invite.Length > 0;
    }

    private async void CopyMyRoomJoinCode(object? sender, RoutedEventArgs args)
    {
        if (_myRoom is not { } room || Clipboard is null) return;
        var text = room.Public ? room.RelayRoom is { Length: 32 } id && id.All(Uri.IsHexDigit)
            ? PublicRoomCode.Display(id) : "" : room.Invite;
        if (text.Length == 0) return;
        try { await Clipboard.SetTextAsync(text); MyRoomMessage.Text = P7("Copié.", "Copied."); }
        catch (Exception exception) { MyRoomMessage.Text = exception.Message; }
    }

    private async void JoinRoomByCode(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is not { } game || _myRoom is not null) return;
        JoinRoomCodeButton.IsEnabled = false;
        try
        {
            if (OnlineBlocker(out _, out _, out _) is { } blocker) throw new InvalidOperationException(blocker);
            var tags = await EnsureRoomTagsAsync(game) ?? throw new InvalidOperationException(P7("Vérification du disque impossible.", "Could not verify disc."));
            var rooms = await new PublicRoomDirectory(OnlineHttp, tags.Relay).ListAsync(tags);
            if (_quickPlayClosing || OnlineGame()?.Id != game.Id || _myRoom is not null) return;
            var room = PublicRoomCode.Resolve(RoomCodeBox.Text ?? "", rooms);
            OpenRoomReview(game, room);
        }
        catch (Exception exception) { OnlineStatusText.Text = exception.Message; }
        finally { JoinRoomCodeButton.IsEnabled = true; }
    }

    private async void OpenRoomReview(GameCatalogEntry game, PublicRoom room)
    {
        var report = RoomModCheck.Compare(room.Mods, new PortableModManager(_paths, game.ModLibraryId).GetInstalled());
        var summary = P7($"{game.Title} · {room.Title}\nHôte : {room.Host}\nCode : {PublicRoomCode.Display(room.Room)}\n\nDisque et moteur : salon issu de la liste compatible. Ils seront revérifiés avant la connexion.",
            $"{game.Title} · {room.Title}\nHost: {room.Host}\nCode: {PublicRoomCode.Display(room.Room)}\n\nDisc and runtime: room is from the compatible list. They will be checked again before joining.");
        summary += "\n\n" + (room.Mods.Count == 0 ? P7("Aucun mod requis.", "No mods required.")
            : string.Join("\n", room.Mods.Select(mod => (report.Missing.Any(item => item.Id == mod.Id) ? "↓ "
                : report.Outdated.Any(item => item.Id == mod.Id) ? "↻ " : "✓ ") + Named(mod))));
        summary += "\n\n" + P7("Les mods du salon seront utilisés pour cette session. Ta sélection personnelle est conservée.",
            "The room's mods will be used for this session. Your personal selection is preserved.");
        var dialog = new Window { Title = P7("Avant de rejoindre", "Before joining"), Width = 760, Height = 660, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var confirm = new Button { Content = report.Status == RoomModStatus.Compatible ? P7("Vérifier et rejoindre", "Check and join")
            : P7("Installer les mods requis et rejoindre", "Install required mods and join"), FontSize = 20, Padding = new Thickness(20, 14) };
        var cancel = new Button { Content = P7("Retour", "Back"), Padding = new Thickness(20, 14) };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_friendsLifetime?.Token ?? CancellationToken.None);
        var cancellation = lifetime.Token;
        dialog.Closed += (_, _) => lifetime.Cancel();
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = false;
            try
            {
                status.Text = P7("Vérification du disque, du moteur et du salon…", "Checking disc, runtime and room…");
                if (OnlineGame()?.Id != game.Id || _myRoom is not null) throw new InvalidOperationException(P7("La sélection ou le salon a changé.", "Selection or current room changed."));
                _roomTags = null;
                var tags = await EnsureRoomTagsAsync(game, cancellation) ?? throw new IOException("Disc verification failed.");
                var directory = new PublicRoomDirectory(OnlineHttp, tags.Relay);
                var fresh = (await directory.ListAsync(tags, cancellation)).SingleOrDefault(item => item.Room == room.Room && item.IsJoinable);
                if (fresh is null || !SameRoomMods(room.Mods, fresh.Mods)) throw new IOException(P7("Le salon a changé, fermé ou n’est plus compatible. Reviens à la liste.", "The room changed, closed or is no longer compatible. Return to the list."));
                var (ready, sessionMods, problem) = await PrepareRoomModsAsync(game, fresh.Mods, text => status.Text = text, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (!ready) throw new IOException(problem);
                _roomTags = null;
                tags = await EnsureRoomTagsAsync(game, cancellation) ?? throw new IOException("Disc verification failed.");
                directory = new PublicRoomDirectory(OnlineHttp, tags.Relay);
                fresh = (await directory.ListAsync(tags, cancellation)).SingleOrDefault(item => item.Room == room.Room && item.IsJoinable);
                if (fresh is null || !SameRoomMods(room.Mods, fresh.Mods)) throw new RoomGoneException(P7("Le salon a changé pendant la préparation.", "The room changed during setup."));
                var invite = await directory.InviteAsync(room.Room, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (_quickPlayClosing || OnlineGame()?.Id != game.Id || _myRoom is not null)
                    throw new InvalidOperationException(P7("La sélection ou le salon a changé.", "Selection or current room changed."));
                if (!string.Equals(PublicRoom.IdOfInvite(invite), fresh.Room, StringComparison.OrdinalIgnoreCase))
                    throw new RoomGoneException(P7("L’invitation ne correspond plus à ce salon.", "The invitation no longer matches this room."));
                var (started, message) = StartJoining(game, room.Host, invite, new Dictionary<string, string?> { [RoomSyncClient.ModsVariable] = sessionMods });
                if (!started) throw new IOException(message);
                dialog.Close();
                ParityShowOnline(this, new RoutedEventArgs());
                RefreshMyRoom();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception) { status.Text = exception.Message; }
            finally { confirm.IsEnabled = true; }
        };
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = summary, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(status); panel.Children.Add(confirm); panel.Children.Add(cancel);
        dialog.Content = new ScrollViewer { Content = panel };
        dialog.Opened += (_, _) => confirm.Focus();
        await dialog.ShowDialog(this);
    }

    private static bool SameRoomMods(IReadOnlyList<RoomMod> first, IReadOnlyList<RoomMod> second) =>
        first.Select(mod => (mod.Id, mod.Fingerprint)).SequenceEqual(second.Select(mod => (mod.Id, mod.Fingerprint)));

    private async Task<bool> ConfirmFriendRoomReviewAsync(FriendRow friend, GameCatalogEntry game)
    {
        if (!CanPlay(game))
        {
            await PrepareForPlayAsync(game, () => Task.CompletedTask);
            if (!CanPlay(game)) return false;
        }
        var mods = _friendPresence.TryGetValue(friend.PublicKey, out var known) ? known.Invite?.SafeMods() : null;
        var details = P7($"Rejoindre {friend.Name} sur {game.Title}\n\nDisque local configuré. Pour un salon privé, PartyBoard compare les versions et disques lors de la connexion ; le lancement reste bloqué en cas de différence.",
            $"Join {friend.Name} in {game.Title}\n\nLocal disc configured. For a private room, PartyBoard compares versions and discs during connection; mismatches block launch.");
        if (mods is not null)
        {
            var report = RoomModCheck.Compare(mods, new PortableModManager(_paths, game.ModLibraryId).GetInstalled());
            details += "\n\n" + (mods.Count == 0 ? P7("Aucun mod requis.", "No mods required.")
                : string.Join("\n", mods.Select(mod => (report.Missing.Any(item => item.Id == mod.Id) ? "↓ "
                    : report.Outdated.Any(item => item.Id == mod.Id) ? "↻ " : "✓ ") + Named(mod))));
        }
        else details += "\n\n" + P7("L’invitation ne décrit pas ses mods : PartyBoard fera la vérification.", "This invitation does not describe its mods: PartyBoard will check them.");
        var dialog = new Window { Title = P7("Vérifier l’invitation", "Check invitation"), Width = 740, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(28), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = details, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var yes = new Button { Content = P7("Continuer et installer les mods requis", "Continue and install required mods"), FontSize = 18, Padding = new Thickness(20, 14) };
        var no = new Button { Content = P7("Retour", "Back"), Padding = new Thickness(20, 14) };
        var confirmed = false;
        yes.Click += (_, _) => { confirmed = true; dialog.Close(); }; no.Click += (_, _) => dialog.Close();
        panel.Children.Add(yes); panel.Children.Add(no);
        dialog.Content = new ScrollViewer { Content = panel };
        dialog.Opened += (_, _) => yes.Focus();
        await dialog.ShowDialog(this);
        return confirmed;
    }
}
