using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CubeShelf.Core.Games;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    /// <summary>One line of the friends list, already formatted: the template only binds.</summary>
    public sealed record FriendRow(
        string PublicKey,
        string Name,
        string Status,
        string Detail,
        string Extra,
        string PauseAction,
        bool CanCopyCode,
        bool CanJoin = false,
        string InviteGameId = "",
        string InviteGameTitle = "",
        string JoinPayload = "",
        bool IsBlocked = false,
        Avalonia.Media.Imaging.Bitmap? Avatar = null,
        string StatusLine = "",
        bool CanInvite = false)
    {
        /// <summary>Everything that is a decision about an active friend, and not about a tombstone.</summary>
        public bool CanBlock => !IsBlocked;

        public bool HasStatusLine => StatusLine.Length > 0;
    }

    private PeerIdentity? _identity;
    private FriendStore? _friends;
    private PresenceService? _presence;
    private CancellationTokenSource? _friendsLifetime;
    private readonly ConcurrentDictionary<string, PresenceSnapshot> _friendPresence = new(StringComparer.Ordinal);
    private string _ownFriendCode = "";
    private string _friendsFailure = "";

    private void InitializeFriends()
    {
        // LoadOrCreate throws on a corrupt key rather than silently re-keying, which is right --
        // a new identity would mean every friend has to add us again. But called unguarded from
        // the constructor it would be a startup crash, so the Friends page degrades instead.
        try
        {
            _identity = PeerIdentity.LoadOrCreate(
                Path.Combine(_paths.ConfigurationDirectory, "identity.key"));
            _friends = new FriendStore(_paths.ConfigurationDirectory);
        }
        catch (Exception exception) when (
            exception is System.Security.Cryptography.CryptographicException
                or IOException or UnauthorizedAccessException)
        {
            _friendsFailure = exception.Message;
            return;
        }

        _friendsLifetime = new CancellationTokenSource();
        StartPresenceService();

        // The first subscriber Started has ever had. Both events can arrive on any thread, and
        // this handler touches no control, so it is safe where it is.
        _sessions.Started += OnFriendsSessionChanged;
        _sessions.Ended += OnFriendsSessionEnded;
    }

    private void StartPresenceService()
    {
        if (_identity is null || _friends is null) return;

        StopPresenceServiceBounded();

        if (!_preferences.PresencePublishEnabled) return;

        // No pseudo, no document: a friend would be reading someone with no name.
        if (!HasIdentity) return;

        var publisher = new SyncedFolderPresencePublisher(new SyncedFolderTarget(
            _preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName, _preferences.PresenceUrl));
        if (!publisher.IsConfigured)
        {
            publisher.Dispose();
            return;
        }

        // The day we started publishing is ours to state because nobody else can: a friend only
        // ever sees us from the day they added us. Set once, on the first configured publisher.
        if (_preferences.ProfileFirstSeenAt is null)
        {
            _preferences = _preferences with { ProfileFirstSeenAt = DateTimeOffset.UtcNow };
            _preferencesStore.Save(_preferences);
        }

        var service = new PresenceService(
            _identity,
            _friends,
            new PresenceComposer(_paths),
            new PresenceSequence(_paths.ConfigurationDirectory),
            publisher,
            new PresenceFetcher(_identity, _friends),
            CapturePresenceInputsAsync);

        service.FriendsRefreshed += outcomes => Dispatcher.UIThread.Post(() => ApplyFriendOutcomes(outcomes));
        service.Start(_friendsLifetime?.Token ?? CancellationToken.None);
        _presence = service;
    }

    /// <summary>
    /// The only place the catalog is read for presence, and it happens on the UI thread.
    /// GameCatalogEntry is mutable and written there as games start and stop, so copying the
    /// list would not be enough -- the entries themselves have to be projected.
    /// </summary>
    private Task<PresenceInputs> CapturePresenceInputsAsync(CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => new PresenceInputs(
            _preferences.FriendsDisplayName,
            _catalogGames.Select(game => new PresenceGame(
                game.Id, game.Title, game.PlayCount, game.TotalPlaySeconds,
                game.IsFavorite, game.LastPlayedAt)).ToArray(),
            _sessions.RunningGameIds,
            new PresenceSharingOptions(
                _preferences.ShareLibrary,
                _preferences.SharePlayTime,
                _preferences.ShareCurrentGame,
                _preferences.ShareMods,
                _preferences.ShareProfile),
            CurrentProfileInputs(),
            // A lapsed invitation is handed over unchanged and the composer drops it, so an
            // invitation nobody withdrew simply stops being published when its time is up.
            _outgoingInvite)).GetTask();

    private void OnFriendsSessionChanged(string gameId) =>
        _presence?.RequestPublish(PresencePublishReason.GameChanged);

    private void OnFriendsSessionEnded(GameSessionEndedEventArgs session) =>
        _presence?.RequestPublish(PresencePublishReason.GameChanged);

    private void DisposeFriends()
    {
        _sessions.Started -= OnFriendsSessionChanged;
        _sessions.Ended -= OnFriendsSessionEnded;

        // Say goodbye before the lifetime is cancelled, so a friend who sees us quit mid-game
        // does not keep reading "in a game" for the whole freshness window. Bounded, because
        // closing the window has to stay instant.
        //
        // The name is read here, on the UI thread, and handed over. Asking the service to fetch it
        // would have it post to this very thread while this call blocks it -- a deadlock that kept
        // CubeShelf 0.9.0 alive after its window closed, which in turn is what let its updater copy
        // files under a CubeShelf that never exited.
        try
        {
            _presence?.ShutdownAsync(PresencePolicy.ShutdownBudget, _preferences.FriendsDisplayName)
                .WaitAsync(PresencePolicy.ShutdownBudget + TimeSpan.FromSeconds(1))
                .GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or OperationCanceledException or TimeoutException)
        {
        }

        _friendsLifetime?.Cancel();
        _friendsLifetime?.Dispose();
        _friendsLifetime = null;

        StopPresenceServiceBounded();

        // _identity is deliberately not disposed: a Seal may still be in flight on a thread pool
        // thread, and disposing it there would throw inside crypto for no benefit at exit.
    }

    /// <summary>
    /// Disposes the service without letting it hold the UI thread. Every wait on the UI thread is
    /// bounded, because a window that will not close is worse than anything left unfinished:
    /// the service is cancelled first and has two seconds to notice.
    /// </summary>
    private void StopPresenceServiceBounded()
    {
        try
        {
            _presence?.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or OperationCanceledException or TimeoutException)
        {
        }
        _presence = null;
    }

    private void ParityShowFriends(object? sender, RoutedEventArgs args)
    {
        ShowParityView(FriendsView);
        RefreshFriendsView();
        _ = CheckClipboardForFriendCodeAsync();
        if (_presence is not null) _ = RefreshFriendsSilentlyAsync();
    }

    private async Task RefreshFriendsSilentlyAsync()
    {
        try
        {
            if (_presence is not null) await _presence.RefreshNowAsync();
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
        }
    }

    /// <summary>Invitations already announced, so a heartbeat does not announce them again.</summary>
    private readonly HashSet<string> _announcedInvites = new(StringComparer.Ordinal);

    private void ApplyFriendOutcomes(IReadOnlyList<PresenceFetchOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(o => o.Snapshot is not null))
        {
            _friendPresence[outcome.FriendPublicKey] = outcome.Snapshot!;
            AnnounceInvite(outcome.FriendPublicKey, outcome.Snapshot!);
        }

        // Only a genuine network failure raises the global banner; a friend who has not
        // published is not a connectivity problem and must not look like one.
        if (outcomes.Any(o => o.IsConnectivityFailure))
            MarkConnectivityIssuePhase7(P7("Présence des amis indisponible.", "Friends presence unavailable."));

        if (FriendsView.IsVisible) RefreshFriendsView();
    }

    /// <summary>
    /// A toast the first time an invitation is seen. The document is republished on every
    /// heartbeat with the invitation unchanged, so without remembering what has already been
    /// announced a standing invitation would ring every five minutes for a quarter of an hour.
    /// </summary>
    private void AnnounceInvite(string friendPublicKey, PresenceSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        if (snapshot.Invite is not { } invite || !invite.IsLive(now)) return;
        if (_identity is null || !invite.IsFor(Convert.ToBase64String(_identity.PublicKey))) return;

        var friend = _friends?.Load().FirstOrDefault(entry => entry.PublicKey == friendPublicKey);
        if (friend is null || friend.Paused || friend.Blocked) return;

        // Keyed on the payload, so the same lobby announced twice is silent but a friend who
        // closes their lobby and opens a new one is announced again.
        if (!_announcedInvites.Add(friendPublicKey + '|' + invite.JoinPayload)) return;

        var handle = PeerName.Handle(friend.DisplayName, friend.PublicKey);
        ShowToastParity(
            P7($"{handle} t’invite", $"{handle} invites you"),
            P7($"{invite.GameTitle} — ouvre la page Amis pour rejoindre.",
               $"{invite.GameTitle} — open the Friends page to join."));
    }

    private void RefreshFriendsView()
    {
        if (FriendsList is null) return;

        if (_friends is null)
        {
            FriendsList.ItemsSource = Array.Empty<FriendRow>();
            FriendsEmptyText.IsVisible = true;
            FriendsEmptyText.Text = _friendsFailure.Length > 0
                ? P7($"Amis indisponibles : {_friendsFailure}", $"Friends unavailable: {_friendsFailure}")
                : P7("Amis indisponibles.", "Friends unavailable.");
            FriendsSubtitleText.Text = "";
            return;
        }

        // Without a pseudo there is nothing to add anyone as; the page says so and points at
        // the one place that fixes it.
        FriendsIdentityGate.IsVisible = !HasIdentity;
        FriendsList.IsVisible = HasIdentity;
        if (!HasIdentity)
        {
            FriendsEmptyText.IsVisible = false;
            ClipboardInviteBanner.IsVisible = false;
            FriendsSubtitleText.Text = "";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var listed = _friends.Load();
        // Two friends who would read identically get six digits instead of four.
        var handles = FriendHandles(listed);
        var game = OnlineGame();
        var canHost = game is not null && HostingBlocker(game) is null && _hostWatch is not { IsCancellationRequested: false };
        var rows = listed
            .Select(friend => BuildFriendRow(friend, handles[friend.PublicKey], canHost, now))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        FriendsList.ItemsSource = rows;
        FriendsEmptyText.IsVisible = rows.Length == 0;
        FriendsSubtitleText.Text = DescribePublishingState();
    }

    private FriendRow BuildFriendRow(Friend friend, string handle, bool canHost, DateTimeOffset now)
    {
        var known = _friendPresence.TryGetValue(friend.PublicKey, out var snapshot) ? snapshot : null;
        var status = known?.EffectiveStatus(PresencePolicy.FreshnessWindow, now) ?? PresenceStatus.Offline;

        var statusText = friend.Blocked
            ? P7("⛔ Bloqué", "⛔ Blocked")
            : friend.Paused
            ? P7("⏸ En pause", "⏸ Paused")
            : status switch
            {
                PresenceStatus.InGame => "▶ " + (known?.CurrentGameTitle ?? P7("En jeu", "In a game")),
                PresenceStatus.Online => P7("● En ligne", "● Online"),
                _ => P7("○ Hors ligne", "○ Offline")
            };

        var detail = friend.Blocked
            ? P7("Bloqué. Ni lu, ni destinataire de ta présence.",
                 "Blocked. Neither read, nor a recipient of your presence.")
            : known is null
                ? P7("Jamais vu. Sa présence sera lue au prochain passage.",
                     "Never seen. Their presence is read on the next poll.")
                : DescribeFriendLibrary(known);

        var seen = friend.LastSeenAt is { } last
            ? P7($"Vu le {last.ToLocalTime():dd/MM/yyyy HH:mm}", $"Seen {last.ToLocalTime():dd/MM/yyyy HH:mm}")
            : "";
        var failing = friend.ConsecutiveFailures > 0
            ? P7($" • {friend.ConsecutiveFailures} échec(s) de lecture", $" • {friend.ConsecutiveFailures} read failure(s)")
            : "";

        // An invitation is only ours to act on if it is still live and either open to everyone
        // or addressed to us -- the document is sealed once for every friend, so the address is
        // what separates "come and play" from "this was meant for someone else".
        var invite = known?.Invite;
        var mine = invite is not null &&
                   invite.IsLive(now) &&
                   _identity is not null &&
                   invite.IsFor(Convert.ToBase64String(_identity.PublicKey));

        var invited = mine
            ? P7($"T’invite sur {invite!.GameTitle}", $"Invites you to {invite!.GameTitle}")
            : "";

        // Invite someone who could come now; not someone who is inviting you already -- the Join
        // button beside them is the answer to that.
        var reachable = status is PresenceStatus.Online or PresenceStatus.InGame;
        var canInvite = canHost && reachable && !mine && !friend.Paused && !friend.Blocked;

        return new FriendRow(
            friend.PublicKey,
            handle,
            statusText,
            invited.Length > 0 ? invited : detail,
            seen + failing,
            friend.Paused ? P7("Reprendre", "Resume") : P7("Mettre en pause", "Pause"),
            CanRebuildCode(friend),
            mine && !friend.Paused && !friend.Blocked,
            invite?.GameId ?? "",
            invite?.GameTitle ?? "",
            mine ? invite!.JoinPayload : "",
            friend.Blocked,
            // A blocked peer's face is not shown: the point of blocking is to stop seeing them.
            friend.Blocked ? null : FriendAvatar(known?.Profile?.AvatarPng),
            friend.Blocked ? "" : known?.Profile?.StatusLine ?? "",
            canInvite);
    }

    /// <summary>
    /// The library line, with the profile's own pick folded in when there is one. A friend who
    /// chose a game to put forward said something; the count alone would drop it.
    /// </summary>
    private string DescribeFriendLibrary(PresenceSnapshot known)
    {
        var games = P7($"{known.Library.Count} jeux partagés", $"{known.Library.Count} games shared");
        var pinned = known.Profile?.PinnedGameTitle;
        return string.IsNullOrWhiteSpace(pinned)
            ? games
            : games + P7($" • en avant : {pinned}", $" • featured: {pinned}");
    }

    /// <summary>
    /// friends.json can be hand-edited, so a stored address may no longer be something
    /// FriendCode.Encode accepts. The button is disabled rather than throwing when pressed.
    /// </summary>
    private static bool CanRebuildCode(Friend friend)
    {
        try
        {
            _ = FriendCode.Encode(Convert.FromBase64String(friend.PublicKey), friend.PresenceUrl);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private string DescribePublishingState()
    {
        if (!_preferences.PresencePublishEnabled)
            return P7("Tu ne publies pas ta présence. Tes amis ne te verront pas.",
                      "You are not publishing your presence. Your friends will not see you.");
        if (_presence is null)
            return P7("Publication configurée mais inactive : vérifie le dossier et l’adresse.",
                      "Publishing is configured but inactive: check the folder and the address.");

        var recipients = _friends?.ActiveRecipients().Count ?? 0;
        return recipients == 0
            ? P7("Tu publies, mais personne ne peut te lire : ajoute un ami.",
                 "You are publishing, but nobody can read it: add a friend.")
            : P7($"Tu publies pour {recipients} ami(s).", $"Publishing to {recipients} friend(s).");
    }

    private async void RefreshFriendsNow(object? sender, RoutedEventArgs args)
    {
        if (_presence is null)
        {
            ShowToastParity(P7("Amis", "Friends"), DescribePublishingState());
            return;
        }

        await RefreshFriendsSilentlyAsync();
        RefreshFriendsView();
    }
}
