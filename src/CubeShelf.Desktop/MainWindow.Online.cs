using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Online;

namespace CubeShelf.Desktop;

/// <summary>One room on the Online page, worded for display.</summary>
public sealed class RoomRow
{
    public required PublicRoom Room { get; init; }
    public required RoomModReport Mods { get; init; }
    public string Title { get; init; } = "";
    public string GameLine { get; init; } = "";
    public string PlayersLine { get; init; } = "";
    public string ModsLine { get; init; } = "";
    public string CompatText { get; init; } = "";
    public bool CompatOk { get; init; }
    public bool CompatWarn { get; init; }
    public bool CanJoin { get; init; }
    public bool JoinEnabled { get; set; } = true;
    public string JoinLabel { get; init; } = "";
}

/// <summary>
/// The Online page: public rooms that wait for a player, games under way, and the player's own
/// room. Everything the page shows about rooms comes from two places the companion also uses, so
/// the companion's window, this page and the game's F1 menu never disagree: the relay's directory
/// for the public list, and the companion's room.json for the player's own room.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly HttpClient OnlineHttp = new();
    private DispatcherTimer? _onlineTimer;
    private RoomTags? _roomTags;
    private string _roomTagsKey = "";
    private DateTimeOffset _roomsFetched = DateTimeOffset.MinValue;
    private bool _onlineBusy;
    private RoomState? _myRoom;

    /// <summary>Where the companion keeps its room.json and reads CubeShelf's requests.</summary>
    private string RoomStateDirectory => Path.Combine(_paths.ConfigurationDirectory, "online-room");

    private RoomSyncClient RoomSync => new(RoomStateDirectory);

    private void ParityShowOnline(object? sender, RoutedEventArgs args)
    {
        ShowParityView(OnlineView);
        if (RoomVisibilityPicker.Items.Count == 0)
        {
            RoomVisibilityPicker.Items.Add(P7("Privée (amis invités)", "Private (invited friends)"));
            RoomVisibilityPicker.Items.Add(P7("Publique", "Public"));
            RoomVisibilityPicker.SelectedIndex = 0;
        }
        _onlineTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _onlineTimer.Tick -= OnlineTick;
        _onlineTimer.Tick += OnlineTick;
        _onlineTimer.Start();
        _roomsFetched = DateTimeOffset.MinValue;
        OnlineTick(null, EventArgs.Empty);
    }

    private void RefreshOnlineNow(object? sender, RoutedEventArgs args)
    {
        _roomsFetched = DateTimeOffset.MinValue;
        _roomTags = null;
        OnlineTick(null, EventArgs.Empty);
    }

    /// <summary>
    /// The player's own room every two seconds (a local file), the public list every ten while the
    /// page is open -- well within the relay's limit of sixty reads a minute -- and nothing at all
    /// once the page is closed.
    /// </summary>
    private void OnlineTick(object? sender, EventArgs args)
    {
        if (!OnlineView.IsVisible)
        {
            _onlineTimer?.Stop();
            return;
        }

        RefreshMyRoom();
        if (DateTimeOffset.UtcNow - _roomsFetched >= TimeSpan.FromSeconds(10) && !_onlineBusy)
            _ = RefreshRoomsAsync();
    }

    /// <summary>Why the page cannot list or open rooms, in words, or null when it can.</summary>
    private string? OnlineBlocker(out GameCatalogEntry? game, out string companion, out string disc)
    {
        game = OnlineGame();
        companion = "";
        disc = "";
        if (game is null)
            return P7("Aucun jeu de ta bibliothèque ne se joue en ligne.", "No game on your shelf plays online.");
        if (!TryResolveCompanion(game, out companion))
            return P7($"Installe {game.RuntimeName} depuis CubeShelf : son compagnon en ligne vient avec.",
                      $"Install {game.RuntimeName} from CubeShelf: its online companion comes with it.");
        if (!OnlineCompanion.SupportsPublicRooms(companion))
            return P7($"Ton {game.RuntimeName} est trop ancien pour les rooms : mets-le à jour depuis CubeShelf.",
                      $"Your {game.RuntimeName} is too old for rooms: update it from CubeShelf.");
        disc = ResolveApplicationPath(game.DiscImage) ?? "";
        if (string.IsNullOrWhiteSpace(disc) || !File.Exists(disc))
            return P7($"Choisis le disque de {game.Title} sur sa page : les rooms se trient par disque.",
                      $"Pick {game.Title}'s disc on its page: rooms are sorted by disc.");
        return null;
    }

    private async Task RefreshRoomsAsync()
    {
        _onlineBusy = true;
        _roomsFetched = DateTimeOffset.UtcNow;
        try
        {
            var blocker = OnlineBlocker(out var game, out var companion, out var disc);
            OnlineGate.IsVisible = blocker is not null;
            OnlineGateText.Text = blocker ?? "";
            CreateRoomButton.IsEnabled = blocker is null;
            if (blocker is not null || game is null)
            {
                AvailableRoomsList.ItemsSource = null;
                PlayingRoomsList.ItemsSource = null;
                return;
            }

            var manager = new PortableModManager(_paths, game.Id);
            var installed = manager.GetInstalled();
            // The tags hold this PC's mods too, so a change on the Mods page asks again.
            var key = disc + "|" + string.Join(";", installed.Select(m => $"{m.Id}:{m.Sha256}:{m.Enabled}"));
            if (_roomTags is null || key != _roomTagsKey)
            {
                OnlineStatusText.Text = P7("Lecture de ton disque (une seule fois)…", "Reading your disc (once)…");
                string? modList = null;
                try { modList = manager.PrepareActiveList(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                _roomTags = await RoomTags.ReadAsync(companion, disc, modList);
                _roomTagsKey = key;
            }

            var tags = _roomTags;
            OnlineSubtitleText.Text = P7($"{game.Title} · disque {RegionName(tags.Game)} · seules les rooms de ton disque et de ta version s’affichent.",
                                         $"{game.Title} · {RegionName(tags.Game)} disc · only rooms for your disc and version are shown.");
            CreateRoomMods.Text = ModsSummary(tags.Mods, P7("Tes mods actifs : ", "Your active mods: "));

            var rooms = await new PublicRoomDirectory(OnlineHttp, tags.Relay).ListAsync(tags);
            // The player's own room is on the list too; it is shown above, not offered to join.
            var mine = PublicRoom.IdOfInvite(_myRoom?.Invite);
            var rows = rooms.Where(room => room.Room != mine).Select(room => ToRow(game, room, installed)).ToList();
            AvailableRoomsList.ItemsSource = rows.Where(r => r.Room.IsJoinable).ToList();
            PlayingRoomsList.ItemsSource = rows.Where(r => !r.Room.IsJoinable).ToList();
            AvailableRoomsEmpty.IsVisible = !rows.Any(r => r.Room.IsJoinable);
            AvailableRoomsEmpty.Text = P7("Aucune room publique n’attend de joueur pour ton disque. Crée la tienne !",
                                          "No public room is waiting for a player on your disc. Create yours!");
            PlayingRoomsEmpty.IsVisible = !rows.Any(r => !r.Room.IsJoinable);
            PlayingRoomsEmpty.Text = P7("Aucune partie publique en cours.", "No public game under way.");
            OnlineStatusText.Text = P7($"Mis à jour à {DateTime.Now:HH:mm:ss}.", $"Updated at {DateTime.Now:HH:mm:ss}.");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException
                                              or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            OnlineStatusText.Text = P7($"Le relais en ligne ne répond pas ({exception.Message}). Les invitations d’amis marchent toujours.",
                                       $"The online relay does not answer ({exception.Message}). Friend invitations still work.");
        }
        finally
        {
            _onlineBusy = false;
        }
    }

    private RoomRow ToRow(GameCatalogEntry game, PublicRoom room, IReadOnlyList<PortableInstalledMod> installed)
    {
        var report = RoomModCheck.Compare(room.Mods, installed);
        var needs = report.Missing.Count + report.Outdated.Count;
        var state = room.State switch
        {
            "playing" => P7($"En jeu depuis {Duration(room.Playing)}", $"Playing for {Duration(room.Playing)}"),
            "full" => P7("Complète, la partie va commencer", "Full, about to start"),
            _ => P7($"Ouverte depuis {Duration(room.Open)}", $"Open for {Duration(room.Open)}")
        };
        return new RoomRow
        {
            Room = room,
            Mods = report,
            Title = P7($"Room de {room.Host}", $"{room.Host}'s room"),
            GameLine = $"{game.Title} · {RegionName(room.Game)} · {LanguageName(room.Language)} · {state}",
            PlayersLine = P7("Joueurs", "Players") + $" ({room.Players.Count}/{room.MaxPlayers}) : " + string.Join(", ", room.Players),
            ModsLine = ModsSummary(room.Mods, P7("Mods : ", "Mods: ")),
            CompatOk = room.IsJoinable && report.Status == RoomModStatus.Compatible,
            CompatWarn = room.IsJoinable && report.Status != RoomModStatus.Compatible,
            CompatText = report.Status == RoomModStatus.Compatible
                ? P7("✓ Tu as les mêmes mods", "✓ You have the same mods")
                : P7($"Il te manque {needs} mod(s) ou version(s) : CubeShelf les installe avant d’entrer.",
                     $"You are missing {needs} mod(s) or version(s): CubeShelf installs them before joining."),
            CanJoin = room.IsJoinable,
            JoinLabel = report.Status == RoomModStatus.Compatible ? P7("Rejoindre", "Join") : P7("Installer et rejoindre", "Install and join")
        };
    }

    /// <summary>The player's own room, from the companion's room.json.</summary>
    private void RefreshMyRoom()
    {
        var state = RoomSync.Read();
        // A companion closed without a word leaves its last state behind; a room nobody has
        // written for a minute is not shown as open.
        var file = Path.Combine(RoomStateDirectory, "room.json");
        if (state is not null && state.IsOpen && File.Exists(file) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromMinutes(1) &&
            !System.Diagnostics.Process.GetProcessesByName("PartyBoardOnline").Any())
            state = null;
        var before = _myRoom;
        _myRoom = state is { IsOpen: true } ? state : null;
        // The list changes with this room (it fills, it starts, it closes): read it again now
        // rather than at the next ten-second tick.
        if (before?.State != _myRoom?.State || before?.Public != _myRoom?.Public) _roomsFetched = DateTimeOffset.MinValue;

        MyRoomPanel.IsVisible = _myRoom is not null;
        CreateRoomPanel.IsVisible = _myRoom is null;
        if (_myRoom is not { } room) return;

        var game = OnlineGame();
        var what = room.State switch
        {
            "creating" => P7("Création de la room…", "Creating the room…"),
            "joining" => P7("Connexion à la room…", "Joining the room…"),
            "waiting" => P7("En attente d’un joueur", "Waiting for a player"),
            "full" => P7("Complète", "Full"),
            "playing" => P7("En jeu", "Playing"),
            _ => room.State
        };
        // Only the host knows whether its room is on the list.
        var visibility = room.IsHost ? (room.Public ? P7(" · publique", " · public") : P7(" · privée", " · private")) : "";
        MyRoomTitle.Text = $"{game?.Title ?? "Mario Party 4"} · {(room.IsHost ? P7("tu héberges", "you host") : P7("tu as rejoint", "you joined"))}{visibility} · {what}";
        MyRoomPlayers.Text = room.Players.Count == 0 ? "" :
            P7("Joueurs", "Players") + $" ({room.Players.Count}/{room.MaxPlayers}) : " + string.Join(", ", room.Players) +
            (room.Ping is { } ping ? $" · {ping} ms" : "");
        MyRoomMods.Text = ModsSummary(room.Mods, P7("Mods : ", "Mods: "));
        MyRoomMessage.Text = room.Message;

        var canChange = room.IsHost && room.Mode == "online" && room.State == "waiting" && room.MaxPlayers == 2;
        MyRoomVisibilityButton.IsVisible = canChange;
        MyRoomVisibilityButton.Content = room.Public ? P7("Rendre privée", "Make private") : P7("Rendre publique", "Make public");
        MyRoomInviteButton.IsVisible = room.IsHost && room.State == "waiting" && room.Invite.Length > 0;
    }

    private async void ToggleMyRoomVisibility(object? sender, RoutedEventArgs args)
    {
        if (_myRoom is not { } room) return;
        MyRoomVisibilityButton.IsEnabled = false;
        try
        {
            var (ok, message) = await RoomSync.AskAsync("set-visibility", room.Public ? "private" : "public");
            ShowToastParity(P7("Room", "Room"), message.Length > 0 ? message
                : ok ? "" : P7("Le compagnon n’a pas répondu.", "The companion did not answer."));
        }
        finally
        {
            MyRoomVisibilityButton.IsEnabled = true;
            RefreshMyRoom();
            _roomsFetched = DateTimeOffset.MinValue;
        }
    }

    private async void CloseMyRoom(object? sender, RoutedEventArgs args)
    {
        var (ok, _) = await RoomSync.AskAsync("close", null);
        if (ok) CancelHosting();
        ShowToastParity(P7("Room", "Room"), ok ? P7("Room fermée.", "Room closed.") : P7("Le compagnon n’a pas répondu.", "The companion did not answer."));
        RefreshMyRoom();
        _roomsFetched = DateTimeOffset.MinValue;
    }

    /// <summary>The room's own invitation, sent to every friend: the room that is already open, not a new one.</summary>
    private void InviteFriendsToMyRoom(object? sender, RoutedEventArgs args)
    {
        if (_myRoom is not { Invite.Length: > 0 } room || OnlineGame() is not { } game) return;
        if (_presence is null || (_friends?.ActiveRecipients().Count ?? 0) == 0)
        {
            ShowToastParity(P7("Invitation", "Invitation"), P7("Ajoute un ami et publie ta présence (Mon profil) pour inviter.",
                                                               "Add a friend and publish your presence (My profile) to invite."));
            return;
        }
        PublishInvitation(game, room.Invite, "");
    }

    private void OpenRoomMods(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is { } game) _selectedGame = game;
        ParityOpenMods(sender, args);
    }

    private void CreateRoom(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is not { } game) return;
        var isPublic = RoomVisibilityPicker.SelectedIndex == 1;
        var (started, message) = StartHosting(game, null, isPublic);
        ShowToastParity(P7("Room", "Room"),
            started
                ? isPublic
                    ? P7("Le compagnon ouvre ta room publique. Elle apparaîtra ici dès qu’elle sera prête.",
                         "The companion is opening your public room. It shows here as soon as it is ready.")
                    : P7("Le compagnon ouvre ta room privée. Tes amis recevront l’invitation dès qu’elle sera prête.",
                         "The companion is opening your private room. Your friends get the invitation as soon as it is ready.")
                : message);
        RefreshMyRoom();
    }

    /// <summary>
    /// Joins a public room: first the mods it plays with, from GameBanana, through the same plan
    /// and confirmation as the Mods page; then its invitation, and the companion opens holding it,
    /// with the room's mods for that session only.
    /// </summary>
    private async void JoinPublicRoom(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: RoomRow row } button || OnlineGame() is not { } game || _roomTags is null) return;
        if (_myRoom is not null)
        {
            ShowToastParity(P7("Room", "Room"), P7("Ferme d’abord ta room.", "Close your room first."));
            return;
        }

        button.IsEnabled = false;
        try
        {
            var (ready, sessionMods, problem) = await PrepareRoomModsAsync(game, row.Room.Mods, text => OnlineStatusText.Text = text);
            if (!ready)
            {
                OnlineStatusText.Text = problem;
                return;
            }

            OnlineStatusText.Text = P7("Connexion à la room…", "Joining the room…");
            var invite = await new PublicRoomDirectory(OnlineHttp, _roomTags.Relay).InviteAsync(row.Room.Room);
            var extra = new Dictionary<string, string?> { [RoomSyncClient.ModsVariable] = sessionMods };
            var (started, message) = StartJoining(game, row.Room.Host, invite, extra);
            ShowToastParity(P7("Room", "Room"), message.Length > 0 ? message
                : P7($"Tu rejoins la room de {row.Room.Host}.", $"Joining {row.Room.Host}'s room."));
            if (started) RefreshMyRoom();
        }
        catch (RoomGoneException)
        {
            OnlineStatusText.Text = P7("Cette room vient d’être prise ou fermée.", "That room was just taken or closed.");
            _roomsFetched = DateTimeOffset.MinValue;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException
                                              or InvalidOperationException or UnauthorizedAccessException)
        {
            OnlineStatusText.Text = P7($"Impossible de rejoindre : {exception.Message}", $"Could not join: {exception.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Makes this PC's mods match a room's: what is missing or in another version comes from
    /// GameBanana through the Mods page's own plan and confirmation. A mod installed for a room
    /// stays off outside it, so joining never changes what this player plays with otherwise.
    /// Returns the list to hand the companion for that session, or why it cannot be had.
    /// </summary>
    private async Task<(bool Ready, string SessionMods, string Problem)> PrepareRoomModsAsync(
        GameCatalogEntry game, IReadOnlyList<RoomMod> mods, Action<string> progressText)
    {
        var manager = new PortableModManager(_paths, game.Id);
        var report = RoomModCheck.Compare(mods, manager.GetInstalled());
        foreach (var mod in report.Missing.Concat(report.Outdated))
        {
            var before = manager.GetInstalled().FirstOrDefault(m => m.Id == mod.Id);
            progressText(P7($"Installation de {Named(mod)}…", $"Installing {Named(mod)}…"));
            var progress = new Progress<double>(p => progressText(P7($"Installation de {Named(mod)} : {p:0} %", $"Installing {Named(mod)}: {p:0}%")));
            var remote = await _gameBanana.GetAsync(mod.Id);
            var plan = await manager.PlanInstallAsync(remote, _gameBanana, progress);
            if (!await ConfirmModPlanAsync(plan, CancellationToken.None))
                return (false, "", P7("Installation annulée : tu n’as pas rejoint la room.", "Installation cancelled: you did not join the room."));
            await manager.InstallPlanAsync(plan, allowIncompatible: true, progress);
            if (before is not { Enabled: true }) manager.SetEnabled(mod.Id, false);
        }

        report = RoomModCheck.Compare(mods, manager.GetInstalled());
        if (report.Status == RoomModStatus.Compatible) return (true, report.SessionMods, "");
        var differing = string.Join(", ", report.Missing.Concat(report.Outdated).Select(Named));
        return (false, "", P7($"La version sur GameBanana de {differing} n’est pas celle de l’hôte : la room refuserait le lancement.",
                              $"GameBanana's version of {differing} is not the host's: the room would refuse to start."));
    }

    /// <summary>
    /// For a join that cannot stop to install anything (the game's F1 menu): the room's mods for
    /// the session when they are all here already, nothing otherwise -- the companion then shows
    /// what is missing, as it always has.
    /// </summary>
    private IReadOnlyDictionary<string, string?>? SessionModsIfReady(GameCatalogEntry game, IReadOnlyList<RoomMod>? mods)
    {
        if (mods is null) return null;
        var report = RoomModCheck.Compare(mods, new PortableModManager(_paths, game.Id).GetInstalled());
        return report.Status == RoomModStatus.Compatible
            ? new Dictionary<string, string?> { [RoomSyncClient.ModsVariable] = report.SessionMods }
            : null;
    }

    private static string Named(RoomMod mod) => mod.Name.Length > 0 ? $"{mod.Name} (#{mod.Id})" : $"#{mod.Id}";

    private string ModsSummary(IReadOnlyList<RoomMod> mods, string prefix) =>
        mods.Count == 0 ? prefix + P7("aucun", "none") : prefix + string.Join(", ", mods.Select(Named));

    private string RegionName(string game) => game == "GMPP01" ? P7("européen", "European") : "USA";

    private static string LanguageName(string code) => code switch
    {
        "fr" => "Français",
        "de" => "Deutsch",
        "es" => "Español",
        "it" => "Italiano",
        _ => "English"
    };

    private string Duration(TimeSpan span) =>
        span.TotalMinutes < 1 ? P7("moins d’une minute", "under a minute")
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min"
        : $"{(int)span.TotalHours} h {span.Minutes:00}";
}
