using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Online;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>One player of the player's own room, worded for display.</summary>
public sealed class SeatRow
{
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public string Disc { get; init; } = "";
    public string Mods { get; init; } = "";
    public string Ping { get; init; } = "";
}

/// <summary>One mod of a listed room: here already, or one click away.</summary>
public sealed class RoomModItem
{
    public required RoomMod Mod { get; init; }
    public string Text { get; init; } = "";
    public string Action { get; init; } = "";
    public bool CanDownload { get; set; }
}

/// <summary>One room on the Online page, worded for display.</summary>
public sealed class RoomRow
{
    public required PublicRoom Room { get; init; }
    public required RoomModReport Mods { get; init; }
    public string Title { get; init; } = "";
    public Avalonia.Media.Imaging.Bitmap? Cover { get; init; }
    public string GameLine { get; init; } = "";
    public string ActivityLine { get; init; } = "";
    public bool HasActivity => ActivityLine.Length > 0;
    public string PlayersLine { get; init; } = "";
    public IReadOnlyList<RoomModItem> ModItems { get; init; } = Array.Empty<RoomModItem>();
    public string NoModsLine { get; init; } = "";
    public bool HasNoMods => ModItems.Count == 0;
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
        NavOnlineBadge.IsVisible = false;
        if (RoomLanguageFilter.Items.Count == 0)
        {
            RoomLanguageFilter.Items.Add(new ComboBoxItem { Content = P7("Toutes les langues", "All languages"), Tag = "" });
            foreach (var code in new[] { "fr", "en", "de", "es", "it" })
                RoomLanguageFilter.Items.Add(new ComboBoxItem { Content = LanguageName(code), Tag = code });
            RoomLanguageFilter.SelectedIndex = 0;
        }
        // Every game on the shelf that plays online; the page is about the one picked.
        var games = OnlineGames().ToList();
        if (OnlineGamePicker.Items.Count != games.Count)
        {
            _choosingGame = true;
            OnlineGamePicker.Items.Clear();
            foreach (var g in games) OnlineGamePicker.Items.Add(new ComboBoxItem { Content = g.Title, Tag = g.Id });
            OnlineGamePicker.SelectedIndex = Math.Max(0, games.FindIndex(g => g.Id == OnlineGame()?.Id));
            _choosingGame = false;
        }
        OnlineGamePicker.IsEnabled = games.Count > 1;
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
        // The companion that hosts and joins rooms is a Windows program; elsewhere there is
        // nothing to install that would make this page work, so say that rather than "install".
        if (!OperatingSystem.IsWindows())
            return P7("Les rooms en ligne passent par le compagnon Windows de PartyBoard : elles ne sont pas encore disponibles sur ce système. Tes amis restent joignables depuis la page Amis.",
                      "Online rooms go through PartyBoard's Windows companion: they are not available on this system yet. Your friends are still on the Friends page.");
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

            var installed = new PortableModManager(_paths, game.Id).GetInstalled();
            if (_roomTags is null) OnlineStatusText.Text = P7("Lecture de ton disque (une seule fois)…", "Reading your disc (once)…");
            if (await EnsureRoomTagsAsync(game) is not { } tags) return;
            OnlineSubtitleText.Text = P7($"{game.Title} · disque {RegionName(tags.Game)} · seules les rooms de ton disque et de ta version s’affichent.",
                                         $"{game.Title} · {RegionName(tags.Game)} disc · only rooms for your disc and version are shown.");

            var rooms = await new PublicRoomDirectory(OnlineHttp, tags.Relay).ListAsync(tags);
            // The player's own room is on the list too; it is shown above, not offered to join.
            var mine = _myRoom?.RelayRoom;
            var language = (RoomLanguageFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var rows = rooms.Where(room => room.Room != mine && (language.Length == 0 || room.Language == language))
                .Select(room => ToRow(game, room, installed)).ToList();
            foreach (var room in rooms) _seenRooms.Add(room.Room);
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

    /// <summary>
    /// The companion's tags for the public list, asked once and again only when the disc or the
    /// installed mods change (the tags hold this PC's mods too). Null when rooms are not possible.
    /// </summary>
    private async Task<RoomTags?> EnsureRoomTagsAsync(GameCatalogEntry game)
    {
        if (OnlineBlocker(out _, out var companion, out var disc) is not null) return null;
        var manager = new PortableModManager(_paths, game.Id);
        var installed = manager.GetInstalled();
        var key = disc + "|" + string.Join(";", installed.Select(m => $"{m.Id}:{m.Sha256}:{m.Enabled}"));
        if (_roomTags is not null && key == _roomTagsKey) return _roomTags;
        string? modList = null;
        try { modList = manager.PrepareActiveList(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        _roomTags = await RoomTags.ReadAsync(companion, disc, modList);
        _roomTagsKey = key;
        return _roomTags;
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
            Title = room.Title.Length > 0 ? room.Title : P7($"Room de {room.Host}", $"{room.Host}'s room"),
            Cover = CoverOf(game),
            ActivityLine = room.IsPlaying && room.Activity.Length > 0 ? "▶ " + room.Activity : "",
            GameLine = $"{game.Title} · {RegionName(room.Game)} · {LanguageName(room.Language)} · {state}",
            PlayersLine = P7("Joueurs", "Players") + $" ({room.Players.Count}/{room.MaxPlayers}) : " + string.Join(", ", room.Players),
            ModItems = room.Mods.Select(mod =>
            {
                var mine = installed.FirstOrDefault(m => m.Id == mod.Id);
                var same = mine is not null && RoomModCheck.Fingerprint(mine) == mod.Fingerprint;
                return new RoomModItem
                {
                    Mod = mod,
                    Text = (same ? "✓ " : "○ ") + Named(mod) + (same ? P7(" — installé", " — installed")
                        : mine is null ? P7(" — non installé", " — not installed") : P7(" — autre version", " — other version")),
                    Action = P7("Télécharger", "Download"),
                    CanDownload = !same
                };
            }).ToList(),
            NoModsLine = P7("Mods : aucun", "Mods: none"),
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
        // A companion closed without a word leaves its last state behind: a room is open only
        // while the very process that wrote it runs (not just any companion on this PC).
        if (state is { IsOpen: true } && !CompanionAlive(state)) state = null;
        var before = _myRoom;
        _myRoom = state is { IsOpen: true } ? state : null;
        AnnounceRoomEnd(before, state);
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
            "full" => room.Phase == "waiting" ? P7("Complète", "Full") : P7("Lancement…", "Starting…"),
            "playing" => P7("En jeu", "Playing"),
            _ => room.State
        };
        // Only the host knows whether its room is on the list.
        var visibility = room.IsHost ? (room.Public ? P7(" · publique", " · public") : P7(" · privée", " · private")) : "";
        MyRoomTitle.Text = $"{game?.Title ?? ""} · {(room.IsHost ? P7("tu héberges", "you host") : P7("tu as rejoint", "you joined"))}{visibility} · {what}";

        MyRoomCover.Source = game is null ? null : CoverOf(game);
        MyRoomActivity.Text = room.State == "playing" && room.Activity.Length > 0 ? "▶ " + room.Activity : "";
        MyRoomActivity.IsVisible = MyRoomActivity.Text.Length > 0;
        MyRoomSeats.ItemsSource = room.Seats.Select(seat => new SeatRow
        {
            Name = seat.Name + (seat.You ? P7(" (toi)", " (you)") : ""),
            Role = seat.Host ? P7("Hôte", "Host") : P7("Invité", "Guest"),
            Disc = seat.Disc switch
            {
                "same" => P7("✓ Même disque (SHA-256 vérifié)", "✓ Same disc (SHA-256 verified)"),
                "different" => P7("✗ DISQUE DIFFÉRENT", "✗ DIFFERENT DISC"),
                "pending" => P7("Disque vérifié, comparaison en cours", "Disc verified, being compared"),
                _ => P7("Disque non vérifié", "Disc not verified")
            },
            Mods = ModsSummary(seat.Mods, P7("Mods : ", "Mods: ")),
            Ping = seat.You ? "" : seat.Ping is { } ms ? $"{ms} ms" : "—"
        }).ToList();
        MyRoomSeats.IsVisible = room.Seats.Count > 0;
        MyRoomMods.Text = room.Seats.Count > 0 ? "" : ModsSummary(room.Mods, P7("Mods : ", "Mods: "));
        MyRoomMods.IsVisible = MyRoomMods.Text.Length > 0;
        MyRoomMessage.Text = room.Message;

        var waiting = room.Phase is "" or "waiting";
        var canChange = room.IsHost && room.Mode == "online" && room.State == "waiting" && room.MaxPlayers == 2;
        MyRoomVisibilityButton.IsVisible = canChange;
        MyRoomVisibilityButton.Content = room.Public ? P7("Rendre privée", "Make private") : P7("Rendre publique", "Make public");
        MyRoomInviteButton.IsVisible = room.IsHost && room.State == "waiting" && room.Invite.Length > 0;
        MyRoomStartButton.IsVisible = room.IsHost && waiting;
        MyRoomStartButton.IsEnabled = room.CanStart;
        MyRoomModsButton.IsVisible = room.IsHost && waiting;
        MyRoomInstallButton.IsVisible = !room.IsHost && waiting && room.MissingMods.Count > 0;
        MyRoomAlignButton.IsVisible = !room.IsHost && waiting && room.DiffersFromHost && room.MissingMods.Count == 0;
    }

    private static bool CompanionAlive(RoomState state)
    {
        if (state.Pid is not { } pid) return true; // an older companion says nothing: trust the file
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited && process.ProcessName.Equals("PartyBoardOnline", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>A room that ended while CubeShelf watched it: said once, with the companion's reason.</summary>
    private void AnnounceRoomEnd(RoomState? before, RoomState? now)
    {
        if (before is not { IsOpen: true } || now is not { State: "closed" } closed || closed.Message.Length == 0) return;
        ShowToastParity(P7("Room", "Room"), closed.Message);
    }

    private async void StartMyRoom(object? sender, RoutedEventArgs args) =>
        await AskRoomAsync(MyRoomStartButton, "start", null);

    private async void AlignMyRoomOnHost(object? sender, RoutedEventArgs args) =>
        await AskRoomAsync(MyRoomAlignButton, "align-host", null);

    /// <summary>A guest missing some of the host's mods: installed from GameBanana, then the room aligns.</summary>
    private async void InstallMyRoomMissing(object? sender, RoutedEventArgs args)
    {
        if (_myRoom is not { } room || OnlineGame() is not { } game) return;
        var hostMods = room.Seats.FirstOrDefault(seat => seat.Host)?.Mods ?? room.MissingMods;
        MyRoomInstallButton.IsEnabled = false;
        try
        {
            var (ready, _, problem) = await PrepareRoomModsAsync(game, hostMods, text => MyRoomMessage.Text = text);
            if (!ready)
            {
                MyRoomMessage.Text = problem;
                return;
            }
            await AskRoomAsync(MyRoomAlignButton, "align-host", null);
        }
        finally
        {
            MyRoomInstallButton.IsEnabled = true;
        }
    }

    private async void ChooseMyRoomMods(object? sender, RoutedEventArgs args)
    {
        if (_myRoom is not { } room || OnlineGame() is not { } game) return;
        var current = room.Seats.FirstOrDefault(seat => seat.You)?.Mods ?? room.Mods;
        if (await ChooseRoomModsAsync(game, current.Select(m => m.Id).ToList()) is { } ids)
            await AskRoomAsync(MyRoomModsButton, "set-mods", ids);
    }

    /// <summary>
    /// The room's mods, picked among those installed on this PC, in CubeShelf's load order; or more
    /// from GameBanana through the Mods page. The player's own selection is not changed by this.
    /// Returns the ids for the companion ("12,345"), or null when cancelled.
    /// </summary>
    private async Task<string?> ChooseRoomModsAsync(GameCatalogEntry game, IReadOnlyList<int> current)
    {
        var installed = new PortableModManager(_paths, game.Id).GetInstalled()
            .OrderByDescending(m => m.Priority).ThenBy(m => m.Id).ToList();
        var boxes = installed.Select(m => new CheckBox { Content = m.Name.Length > 0 ? $"{m.Name} (#{m.Id})" : $"#{m.Id}", IsChecked = current.Contains(m.Id), Tag = m.Id }).ToList();
        var list = new StackPanel { Spacing = 4 };
        foreach (var box in boxes) list.Children.Add(box);
        if (boxes.Count == 0)
            list.Children.Add(new TextBlock { Text = P7("Aucun mod installé pour ce jeu.", "No mod installed for this game."), TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var dialog = new Window
        {
            Title = P7("Mods de la room", "Room mods"),
            Width = 520, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false
        };
        string? result = null;
        var apply = new Button { Content = P7("Appliquer", "Apply"), Classes = { "primary" } };
        var cancel = new Button { Content = P7("Annuler", "Cancel") };
        var gameBanana = new Button { Content = P7("Ajouter depuis GameBanana…", "Add from GameBanana…") };
        apply.Click += (_, _) =>
        {
            result = string.Join(",", boxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag!));
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        gameBanana.Click += (_, _) =>
        {
            dialog.Close();
            _selectedGame = game;
            ParityOpenMods(this, new RoutedEventArgs());
            ShowToastParity(P7("Mods", "Mods"), P7("Installe le mod ici, puis reviens dans En ligne et rouvre « Mods de la room… ».",
                                                   "Install the mod here, then come back to Online and open “Room mods…” again."));
        };
        dialog.Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new TextBlock
                {
                    Text = P7("Les mods cochés sont ceux de la room. Tous les joueurs doivent avoir les mêmes : un invité qui en manque les installe en un clic. Ta sélection habituelle n’est pas modifiée.",
                              "The ticked mods are the room's. Every player must have the same: a guest missing one installs it in one click. Your usual selection is not changed."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 12),
                    [DockPanel.DockProperty] = Dock.Top
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Children = { gameBanana, cancel, apply }
                },
                new ScrollViewer { Content = list }
            }
        };
        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>Asks the companion, with the button held while it answers, and says what it answered.</summary>
    private async Task AskRoomAsync(Button button, string action, string? value)
    {
        button.IsEnabled = false;
        try
        {
            var (ok, message) = await RoomSync.AskAsync(action, value);
            if (!ok || message.Length > 0)
                ShowToastParity(P7("Room", "Room"), message.Length > 0 ? message : P7("Le compagnon n’a pas répondu.", "The companion did not answer."));
        }
        finally
        {
            button.IsEnabled = true;
            RefreshMyRoom();
        }
    }

    private bool _choosingGame;

    private void OnlineGameChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_choosingGame || (OnlineGamePicker.SelectedItem as ComboBoxItem)?.Tag is not string id) return;
        _onlineGameId = id;
        _roomTags = null;
        _roomsFetched = DateTimeOffset.MinValue;
        if (OnlineView.IsVisible) OnlineTick(null, EventArgs.Empty);
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

    /// <summary>The room setup page: its name, who can come in, its mods; then Create.</summary>
    private void OpenRoomSetup(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is not { } game) return;
        if (_myRoom is not null)
        {
            ShowToastParity(P7("Room", "Room"), P7("Ferme d’abord ta room.", "Close your room first."));
            return;
        }
        var me = (_preferences.FriendsDisplayName ?? "").Trim();
        if (RoomNameBox.Text is not { Length: > 0 }) RoomNameBox.Text = me.Length > 0 ? P7($"Room de {me}", $"{me}'s room") : "";
        RoomSetupSubtitle.Text = $"{game.Title} · " + P7("une room publique est vue de tous ceux qui ont le même disque.",
                                                          "a public room is seen by everyone with the same disc.");
        RoomSetupStatus.Text = "";
        FillRoomSetupMods(game, _setupMods);
        ShowParityView(RoomSetupView);
    }

    private void CloseRoomSetup(object? sender, RoutedEventArgs args) => ParityShowOnline(sender, args);

    /// <summary>The setup page's mod choice; null until the player touches it (then CubeShelf's own active mods).</summary>
    private HashSet<int>? _setupMods;
    private readonly List<CheckBox> _setupModBoxes = new();

    private void FillRoomSetupMods(GameCatalogEntry game, HashSet<int>? chosen)
    {
        var installed = new PortableModManager(_paths, game.Id).GetInstalled()
            .OrderByDescending(m => m.Priority).ThenBy(m => m.Id).ToList();
        RoomSetupModsList.Children.Clear();
        _setupModBoxes.Clear();
        foreach (var mod in installed)
        {
            var box = new CheckBox
            {
                Content = mod.Name.Length > 0 ? $"{mod.Name} (#{mod.Id})" : $"#{mod.Id}",
                IsChecked = chosen?.Contains(mod.Id) ?? mod.Enabled,
                Tag = mod.Id
            };
            box.IsCheckedChanged += (_, _) => _setupMods = _setupModBoxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag!).ToHashSet();
            _setupModBoxes.Add(box);
            RoomSetupModsList.Children.Add(box);
        }
        if (installed.Count == 0)
            RoomSetupModsList.Children.Add(new TextBlock
            {
                Text = P7("Aucun mod installé pour ce jeu : la room se jouera sans mods, ou ajoute-en depuis GameBanana.",
                          "No mod installed for this game: the room plays without mods, or add some from GameBanana."),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
    }

    /// <summary>To GameBanana through the Mods page; coming back to the setup keeps what was ticked.</summary>
    private void RoomSetupAddMods(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is not { } game) return;
        _setupMods = _setupModBoxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag!).ToHashSet();
        _selectedGame = game;
        ParityOpenMods(sender, args);
        ShowToastParity(P7("Mods", "Mods"), P7("Installe le mod ici, puis reviens dans En ligne → Créer une room… : il y sera.",
                                               "Install the mod here, then come back to Online → Create a room…: it will be there."));
    }

    private void ConfirmCreateRoom(object? sender, RoutedEventArgs args)
    {
        if (OnlineGame() is not { } game) return;
        var isPublic = RoomPublicRadio.IsChecked == true;
        var mods = string.Join(",", _setupModBoxes.Where(b => b.IsChecked == true).Select(b => (int)b.Tag!));
        var extra = new Dictionary<string, string?>
        {
            [RoomSyncClient.ModsVariable] = mods,
            [RoomSyncClient.RoomNameVariable] = (RoomNameBox.Text ?? "").Trim()
        };
        var (started, message) = StartHosting(game, null, isPublic, extra);
        if (!started)
        {
            RoomSetupStatus.Text = message;
            return;
        }
        ShowToastParity(P7("Room", "Room"), isPublic
            ? P7("Ta room publique s’ouvre : tout le monde la verra dans un instant.", "Your public room is opening: everyone sees it in a moment.")
            : P7("Ta room privée s’ouvre. Tes amis recevront l’invitation dès qu’elle sera prête.",
                 "Your private room is opening. Your friends get the invitation as soon as it is ready."));
        _setupMods = null;
        ParityShowOnline(sender, args);
        RefreshMyRoom();
    }

    /// <summary>One mod of a listed room, fetched from GameBanana in one click.</summary>
    private async void DownloadRoomMod(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: RoomModItem item } button || OnlineGame() is not { } game) return;
        button.IsEnabled = false;
        try
        {
            var (ready, _, problem) = await PrepareRoomModsAsync(game, new[] { item.Mod }, text => OnlineStatusText.Text = text);
            OnlineStatusText.Text = ready
                ? P7($"{Named(item.Mod)} est installé.", $"{Named(item.Mod)} is installed.")
                : problem;
            _roomsFetched = DateTimeOffset.MinValue;
            OnlineTick(null, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException
                                              or InvalidOperationException or UnauthorizedAccessException)
        {
            OnlineStatusText.Text = P7($"Téléchargement impossible : {exception.Message}", $"Download failed: {exception.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
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
            var plain = plan.Packages.Count <= 1 && plan.Warnings.Count == 0 && plan.IncompatibleInstalledModIds.Count == 0;
            if (!plain && !await ConfirmModPlanAsync(plan, CancellationToken.None))
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

    private void RoomLanguageFilterChanged(object? sender, SelectionChangedEventArgs args)
    {
        _roomsFetched = DateTimeOffset.MinValue;
        if (OnlineView.IsVisible) OnlineTick(null, EventArgs.Empty);
    }

    // The sidebar's dot on "Online": something there the player has not seen -- a public room
    // that opened for their disc, or a player who came into their room -- while the page was
    // closed. Read once a minute (the room is a local file; the list one request a minute), only
    // with a companion that has rooms, and cleared by opening the page.
    private readonly HashSet<string> _seenRooms = new(StringComparer.Ordinal);
    private DispatcherTimer? _onlineWatch;
    private int _seenPlayers;
    private bool _watchingOnline;

    private void StartOnlineWatch()
    {
        _onlineWatch ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _onlineWatch.Tick -= OnlineWatchTick;
        _onlineWatch.Tick += OnlineWatchTick;
        _onlineWatch.Start();
    }

    private async void OnlineWatchTick(object? sender, EventArgs args)
    {
        if (_watchingOnline || OnlineView.IsVisible || OnlineGame() is not { } game) return;
        _watchingOnline = true;
        try
        {
            var room = RoomSync.Read();
            var players = room is { IsOpen: true } ? room.Players.Count : 0;
            if (players > _seenPlayers && _seenPlayers > 0) NavOnlineBadge.IsVisible = true;
            _seenPlayers = players;

            if (await EnsureRoomTagsAsync(game) is not { } tags) return;
            var rooms = await new PublicRoomDirectory(OnlineHttp, tags.Relay).ListAsync(tags);
            var mine = room?.RelayRoom;
            var fresh = rooms.Where(r => r.IsJoinable && r.Room != mine && !_seenRooms.Contains(r.Room)).ToList();
            // The first read only learns what is already there: a dot means something new since.
            if (fresh.Count > 0 && _seenRooms.Count > 0) NavOnlineBadge.IsVisible = true;
            foreach (var r in rooms) _seenRooms.Add(r.Room);
            if (_seenRooms.Count == 0) _seenRooms.Add("");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or TaskCanceledException
                                              or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The next minute tries again; the dot is a nicety.
        }
        finally
        {
            _watchingOnline = false;
        }
    }

    // The game's front cover, as the library shows it, loaded once per game.
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _covers = new(StringComparer.OrdinalIgnoreCase);

    private Avalonia.Media.Imaging.Bitmap? CoverOf(GameCatalogEntry game)
    {
        if (_covers.TryGetValue(game.Id, out var cover)) return cover;
        cover = LoadBitmap(ResolveCoverPath(game.Covers.FirstOrDefault()?.Front));
        _covers[game.Id] = cover;
        return cover;
    }

    // What the running game says is happening, passed on to the companion so the room's status
    // carries it -- the list shows it under games in progress. Checked with the in-game bridge's
    // own tick; sent only when it changes.
    private string _sentActivity = "";
    private bool _sendingActivity;

    private async Task PumpRoomActivityAsync()
    {
        if (_sendingActivity) return;
        var room = RoomSync.Read();
        if (room is not { State: "playing" } || !CompanionAlive(room)) { _sentActivity = ""; return; }
        var activity = InGameBridge.ReadActivity(InGameDirectory, DateTimeOffset.UtcNow) ?? "";
        if (activity == _sentActivity) return;
        _sendingActivity = true;
        try
        {
            var (ok, _) = await RoomSync.AskAsync("set-activity", activity);
            if (ok) _sentActivity = activity;
        }
        finally
        {
            _sendingActivity = false;
        }
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
