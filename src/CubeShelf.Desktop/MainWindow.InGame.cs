using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// CubeShelf's half of the F1 Friends tab inside Mario Party 4.
///
/// While Party Board runs, CubeShelf writes what the tab shows into the directory it named in
/// <see cref="InGameBridge.DirectoryVariable"/>, and acts on what the player asks for there.
/// The game does no networking of its own for this and knows nothing about friends; it reads a
/// file and drops one. See <see cref="InGameBridge"/> for the files, and
/// zeranemesis/Marioparty4 src/port/ui/cubeshelf.cpp for the other side.
///
/// Nothing is written while the game is not running: the tab is the only reader.
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherTimer? _inGameTimer;
    private bool _pumpingInGame;

    private string InGameDirectory => Path.Combine(_paths.DataDirectory, "ingame");

    /// <summary>The variable that tells a game where CubeShelf is listening -- only for a game that can play together.</summary>
    private Dictionary<string, string?> InGameEnvironment(GameCatalogEntry game)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!OnlineCompanion.IsSupported(game)) return environment;

        try
        {
            Directory.CreateDirectory(InGameDirectory);
            environment[InGameBridge.DirectoryVariable] = InGameDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the directory the tab simply does not appear; the game itself is unaffected.
        }
        return environment;
    }

    private void StartInGameBridge()
    {
        _inGameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _inGameTimer.Tick += (_, _) => PumpInGameBridge();
        _inGameTimer.Start();
    }

    private void StopInGameBridge()
    {
        _inGameTimer?.Stop();
        _inGameTimer = null;
    }

    /// <summary>
    /// Party Board is running, whoever started it: CubeShelf for a solo game, the companion for an
    /// online one. Both games read the same tab.
    /// </summary>
    private bool IsPartyBoardRunning()
    {
        if (OnlineGame() is { } game && _sessions.IsRunning(game.Id)) return true;
        try
        {
            var running = Process.GetProcessesByName("partyboard");
            foreach (var process in running) process.Dispose();
            return running.Length > 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The timer's tick: the state for the tab, then whatever the player asked for.</summary>
    private void PumpInGameBridge()
    {
        // StartHosting and the rest write the state themselves; if they read requests too, a
        // request would be acted on a second time before it is marked done.
        if (_pumpingInGame || OnlineGame() is not { } game || !IsPartyBoardRunning()) return;
        _pumpingInGame = true;
        try
        {
            WriteInGameState(force: false);

            var now = DateTimeOffset.UtcNow;
            foreach (var request in InGameBridge.ReadRequests(InGameDirectory, now))
            {
                (bool Ok, string Message) outcome;
                try
                {
                    outcome = request.Action switch
                    {
                        InGameAction.Host => StartHosting(game, null),
                        InGameAction.Invite => StartHosting(game, request.FriendKey),
                        InGameAction.Join => JoinFromGame(game, request.FriendKey),
                        InGameAction.Cancel => (true, CancelHosting()),
                        _ => (false, "")
                    };
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    outcome = (false, exception.Message);
                }
                InGameBridge.Complete(InGameDirectory, request, outcome.Ok, outcome.Message);
            }

            InGameBridge.RemoveStaleAnswers(InGameDirectory, now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked file this tick is read on the next one.
        }
        finally
        {
            _pumpingInGame = false;
        }
    }

    private void WriteInGameState() => WriteInGameState(force: true);

    /// <summary>
    /// Rewritten on every tick while the game runs, which is also the heartbeat: the game reads a
    /// state older than fifteen seconds as a CubeShelf that is gone, and offers nothing then. The
    /// revision inside only moves when something shown moves, so the tab is not rebuilt for this.
    /// </summary>
    private void WriteInGameState(bool force)
    {
        if (OnlineGame() is not { } game) return;
        if (!force && !IsPartyBoardRunning()) return;

        try
        {
            InGameBridge.WriteState(InGameDirectory, ComposeInGameState(game), DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private InGameState ComposeInGameState(GameCatalogEntry game)
    {
        var blocker = HostingBlocker(game);
        var preparing = _hostWatch is { IsCancellationRequested: false };
        var notice = blocker ?? (preparing
            ? P7("Le compagnon prépare le salon…", "The companion is preparing the lobby…")
            : IsHosting
                ? P7("Invitation publiée : tes amis la verront à leur prochaine lecture, pas à l’instant.",
                     "Invitation published: your friends see it at their next read, not this instant.")
                : "");

        var friends = new List<InGameFriend>();
        if (_friends is not null && _identity is not null)
        {
            var me = Convert.ToBase64String(_identity.PublicKey);
            var now = DateTimeOffset.UtcNow;
            var listed = _friends.Load().Where(friend => !friend.Blocked).ToList();
            var handles = FriendHandles(listed);

            foreach (var friend in listed)
            {
                var known = _friendPresence.TryGetValue(friend.PublicKey, out var snapshot) ? snapshot : null;
                var effective = known?.EffectiveStatus(PresencePolicy.FreshnessWindow, now) ?? PresenceStatus.Offline;
                var status = friend.Paused ? "paused" : effective switch
                {
                    PresenceStatus.InGame => "ingame",
                    PresenceStatus.Online => "online",
                    _ => "offline"
                };
                var label = status switch
                {
                    "paused" => P7("En pause", "Paused"),
                    "ingame" => P7($"En jeu : {known?.CurrentGameTitle ?? "un jeu"}", $"Playing {known?.CurrentGameTitle ?? "a game"}"),
                    "online" => P7("En ligne", "Online"),
                    _ => P7("Hors ligne", "Offline")
                };

                var invite = known?.Invite;
                var invitesYou = !friend.Paused &&
                                 invite is not null &&
                                 invite.IsLive(now) &&
                                 invite.IsFor(me) &&
                                 string.Equals(invite.GameId, game.Id, StringComparison.OrdinalIgnoreCase);

                friends.Add(new InGameFriend(
                    friend.PublicKey, handles[friend.PublicKey], status, label,
                    invitesYou, invitesYou ? ShortId(invite!.JoinPayload) : ""));
            }
        }

        // Whoever is inviting you first, then who could play right now, then everyone else.
        static int Rank(InGameFriend friend) => friend.InvitesYou ? 0 : friend.Status switch
        {
            "ingame" => 1,
            "online" => 2,
            "offline" => 3,
            _ => 4
        };
        var ordered = friends
            .OrderBy(Rank)
            .ThenBy(friend => friend.Handle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new InGameState(
            HasIdentity ? OwnHandle() : P7("Pas encore d’identité", "No identity yet"),
            blocker is null,
            notice,
            blocker is null && !preparing,
            IsHosting,
            ordered,
            InGameText());
    }

    /// <summary>A lobby's identity for the toast: changes when a friend opens a new one.</summary>
    private static string ShortId(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..12].ToLowerInvariant();

    /// <summary>
    /// <c>Zera#4821</c> for everyone, except two people who would read identically -- then both
    /// get six digits, the first four unchanged. One chance in ten thousand for two friends who
    /// share a pseudo, and the only place it could matter is a list that holds them both.
    /// </summary>
    private Dictionary<string, string> FriendHandles(IReadOnlyList<Friend> friends)
    {
        var shortHandles = friends.ToDictionary(
            friend => friend.PublicKey, friend => PeerName.Handle(friend.DisplayName, friend.PublicKey));
        var own = HasIdentity && _identity is not null ? OwnHandle() : null;

        var clashing = shortHandles
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 || (own is not null && string.Equals(group.Key, own, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(group => group.Select(pair => pair.Key))
            .ToHashSet(StringComparer.Ordinal);

        return friends.ToDictionary(
            friend => friend.PublicKey,
            friend => clashing.Contains(friend.PublicKey)
                ? PeerName.Handle(friend.DisplayName, friend.PublicKey, longTag: true)
                : shortHandles[friend.PublicKey]);
    }

    private (bool Ok, string Message) JoinFromGame(GameCatalogEntry game, string friendKey)
    {
        if (_friends is null || _identity is null) return (false, P7("Amis indisponibles.", "Friends unavailable."));

        var friend = _friends.Load().FirstOrDefault(entry => entry.PublicKey == friendKey && !entry.Blocked);
        var invite = friend is not null && _friendPresence.TryGetValue(friend.PublicKey, out var known) ? known.Invite : null;
        if (friend is null || invite is null || !invite.IsLive(DateTimeOffset.UtcNow) ||
            !invite.IsFor(Convert.ToBase64String(_identity.PublicKey)))
            return (false, P7("Cette invitation n’est plus valable : le salon a été fermé ou a expiré.",
                              "That invitation is no longer valid: the lobby was closed or has expired."));

        return StartJoining(game, PeerName.Handle(friend.DisplayName, friend.PublicKey), invite.JoinPayload);
    }

    /// <summary>Every string the tab shows, in the language the player chose in CubeShelf.</summary>
    private Dictionary<string, string> InGameText() => new(StringComparer.Ordinal)
    {
        ["tab"] = P7("Amis", "Friends"),
        ["title"] = P7("Amis", "Friends"),
        ["cubeshelfClosed"] = P7("CubeShelf n’est pas ouvert. Les amis et les invitations passent par lui : ouvre-le et reviens ici.",
                                 "CubeShelf is not running. Friends and invitations go through it: open it and come back."),
        ["pending"] = P7("En attente de CubeShelf…", "Waiting for CubeShelf…"),
        ["host"] = P7("Créer un salon et inviter tout le monde", "Create a lobby and invite everyone"),
        ["cancel"] = P7("Retirer l’invitation", "Withdraw the invitation"),
        ["friendsSection"] = P7("Amis", "Friends"),
        ["noFriends"] = P7("Pas encore d’amis. Ajoute-les depuis la page Amis de CubeShelf.",
                           "No friends yet. Add them from the Friends page in CubeShelf."),
        ["join"] = P7("Rejoindre", "Join"),
        ["invite"] = P7("Inviter", "Invite"),
        ["closeTitle"] = P7("Mario Party 4 va se fermer", "Mario Party 4 will close"),
        ["closeBody"] = P7("Le jeu en ligne relance Mario Party 4 sur les deux PC, depuis le salon. La progression non sauvegardée de cette partie sera perdue.",
                           "Online play starts Mario Party 4 again on both PCs, from the lobby. Unsaved progress in this game will be lost."),
        ["cancelButton"] = P7("Annuler", "Cancel"),
        ["continueButton"] = P7("Continuer", "Continue"),
        ["noAnswer"] = P7("CubeShelf n’a pas répondu. Est-il toujours ouvert ?", "CubeShelf did not answer. Is it still open?"),
        ["inviteTitle"] = P7("Invitation", "Invitation"),
        ["inviteToast"] = P7("t’invite à jouer. F1, onglet Amis, pour rejoindre.", "invites you to play. F1, Friends tab, to join."),
        ["writeFailed"] = P7("La demande n’a pas pu être transmise à CubeShelf.", "The request could not be handed to CubeShelf.")
    };
}
