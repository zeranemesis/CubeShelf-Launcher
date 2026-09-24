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
        string JoinPayload = "");

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

        _presence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _presence = null;

        if (!_preferences.PresencePublishEnabled) return;

        var publisher = new SyncedFolderPresencePublisher(new SyncedFolderTarget(
            _preferences.PresenceFolder, SyncedFolderTarget.DefaultFileName, _preferences.PresenceUrl));
        if (!publisher.IsConfigured)
        {
            publisher.Dispose();
            return;
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
                _preferences.ShareMods),
            null,
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
        try
        {
            _presence?.ShutdownAsync(PresencePolicy.ShutdownBudget).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
        }

        _friendsLifetime?.Cancel();
        _friendsLifetime?.Dispose();
        _friendsLifetime = null;

        try
        {
            _presence?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
        }
        _presence = null;

        // _identity is deliberately not disposed: a Seal may still be in flight on a thread pool
        // thread, and disposing it there would throw inside crypto for no benefit at exit.
    }

    private void ParityShowFriends(object? sender, RoutedEventArgs args)
    {
        ShowParityView(FriendsView);
        RefreshFriendsView();
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

    private void ApplyFriendOutcomes(IReadOnlyList<PresenceFetchOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(o => o.Snapshot is not null))
            _friendPresence[outcome.FriendPublicKey] = outcome.Snapshot!;

        // Only a genuine network failure raises the global banner; a friend who has not
        // published is not a connectivity problem and must not look like one.
        if (outcomes.Any(o => o.IsConnectivityFailure))
            MarkConnectivityIssuePhase7(P7("Présence des amis indisponible.", "Friends presence unavailable."));

        if (FriendsView.IsVisible) RefreshFriendsView();
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

        var now = DateTimeOffset.UtcNow;
        var rows = _friends.Load()
            .Select(friend => BuildFriendRow(friend, now))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        FriendsList.ItemsSource = rows;
        FriendsEmptyText.IsVisible = rows.Length == 0;
        FriendsSubtitleText.Text = DescribePublishingState();
    }

    private FriendRow BuildFriendRow(Friend friend, DateTimeOffset now)
    {
        var known = _friendPresence.TryGetValue(friend.PublicKey, out var snapshot) ? snapshot : null;
        var status = known?.EffectiveStatus(PresencePolicy.FreshnessWindow, now) ?? PresenceStatus.Offline;

        var statusText = friend.Paused
            ? P7("⏸ En pause", "⏸ Paused")
            : status switch
            {
                PresenceStatus.InGame => "▶ " + (known?.CurrentGameTitle ?? P7("En jeu", "In a game")),
                PresenceStatus.Online => P7("● En ligne", "● Online"),
                _ => P7("○ Hors ligne", "○ Offline")
            };

        var detail = known is null
            ? P7("Jamais vu. Sa présence sera lue au prochain passage.",
                 "Never seen. Their presence is read on the next poll.")
            : P7($"{known.Library.Count} jeux partagés", $"{known.Library.Count} games shared");

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

        return new FriendRow(
            friend.PublicKey,
            friend.DisplayName,
            statusText,
            invited.Length > 0 ? invited : detail,
            seen + failing,
            friend.Paused ? P7("Reprendre", "Resume") : P7("Mettre en pause", "Pause"),
            CanRebuildCode(friend),
            mine && !friend.Paused,
            invite?.GameId ?? "",
            invite?.GameTitle ?? "",
            mine ? invite!.JoinPayload : "");
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
