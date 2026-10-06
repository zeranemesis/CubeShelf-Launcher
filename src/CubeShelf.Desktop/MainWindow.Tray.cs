using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using CubeShelf.Core.Social;

namespace CubeShelf.Desktop;

/// <summary>
/// Staying reachable with the window closed, like Steam: an icon near the clock, closing the
/// window hides it there, and what friends do still reaches the user in the corner of the screen.
///
/// Presence is the reason. Closing CubeShelf says goodbye to every friend and stops invitations
/// arriving; most people closing a window only mean "out of my way". So with friends set up,
/// closing hides, and quitting is one right-click away -- said the first time it happens.
/// Everything that must really end the process does: an update, a Windows shutdown, Quit.
/// </summary>
public sealed partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _quitting;
    private readonly List<NotificationWindow> _notifications = new();

    private void InitializeTray()
    {
        try
        {
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CubeShelf/Assets/Brand/gamecube_logo.png")));
            var open = new NativeMenuItem(P7("Ouvrir CubeShelf", "Open CubeShelf"));
            open.Click += (_, _) => RestoreFromTray(showFriends: false);
            var friends = new NativeMenuItem(P7("Amis", "Friends"));
            friends.Click += (_, _) => RestoreFromTray(showFriends: true);
            var quit = new NativeMenuItem(P7("Quitter", "Quit"));
            quit.Click += (_, _) => QuitCompletely();

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "CubeShelf",
                Menu = new NativeMenu { open, friends, new NativeMenuItemSeparator(), quit },
                IsVisible = true
            };
            _tray.Clicked += (_, _) => RestoreFromTray(showFriends: false);
            if (Application.Current is { } application)
                TrayIcon.SetIcons(application, new TrayIcons { _tray });
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            // A desktop with no notification area: closing simply closes, as before.
            _tray = null;
        }
    }

    private void DisposeTray()
    {
        foreach (var notification in _notifications.ToArray()) notification.Close();
        if (_tray is null) return;
        _tray.IsVisible = false;
        _tray.Dispose();
        _tray = null;
    }

    /// <summary>
    /// Closing the window by hand hides it when there is something to stay open for. Anything
    /// programmatic (an update installing), anything from the system (logging off) or a real
    /// Quit goes through.
    /// </summary>
    private bool ShouldHideInsteadOfClosing(WindowClosingEventArgs closing) =>
        !_quitting &&
        _tray is not null &&
        _preferences.CloseToTray &&
        HasIdentity &&
        !closing.IsProgrammatic &&
        closing.CloseReason == WindowCloseReason.WindowClosing;

    private void HideToTray()
    {
        Hide();
        if (_preferences.TrayHintShown) return;

        _preferences = _preferences with { TrayHintShown = true };
        _preferencesStore.Save(_preferences);
        Notify(P7("CubeShelf reste ouvert", "CubeShelf stays open"),
            P7("Tes amis te voient toujours et tes invitations arrivent. Pour quitter vraiment : clic droit sur l’icône près de l’horloge, puis Quitter.",
               "Your friends still see you and invitations still arrive. To really quit: right-click the icon near the clock, then Quit."),
            showFriends: false);
    }

    private void RestoreFromTray(bool showFriends)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (showFriends) ParityShowFriends(null, new RoutedEventArgs());
    }

    private void QuitCompletely()
    {
        _quitting = true;
        if (!IsVisible) Show();
        Close();
    }

    /// <summary>
    /// Tells the user something about their friends. In the window when they are looking at it;
    /// in the corner of the screen when it is out of sight -- unless a game is running, which
    /// shows its own (the F1 bridge), and must not be covered or lose the focus to a popup.
    /// </summary>
    private void Notify(string title, string body, bool showFriends = true)
    {
        var looking = IsVisible && IsActive && WindowState != WindowState.Minimized;
        if (looking || _sessions.RunningGameIds.Count > 0)
        {
            ShowToastParity(title, body);
            return;
        }

        // Three at a time at most: a burst of friends coming online is not worth a wall.
        if (_notifications.Count >= 3) _notifications[0].Close();

        var slot = Enumerable.Range(0, 3).FirstOrDefault(index => _notifications.All(open => open.Tag is not int used || used != index));
        var window = new NotificationWindow(title, body, () => RestoreFromTray(showFriends), slot) { Tag = slot };
        window.Closed += (_, _) => _notifications.Remove(window);
        _notifications.Add(window);
        window.Show();
        // The window says it in the corner; the in-app toast is there too when they come back.
        ShowToastParity(title, body);
    }

    /// <summary>When each friend was last announced as coming online, so a flaky friend does not ring every minute.</summary>
    private readonly Dictionary<string, DateTimeOffset> _announcedOnline = new(StringComparer.Ordinal);

    /// <summary>
    /// A friend who was offline and now is not. Only on a change seen while running -- the first
    /// read after start finds everyone at once and announces nobody -- and at most every ten
    /// minutes per friend.
    /// </summary>
    private void AnnounceComingOnline(string friendPublicKey, PresenceSnapshot? previous, PresenceSnapshot current)
    {
        if (!_preferences.NotifyFriendsOnline || previous is null) return;

        var now = DateTimeOffset.UtcNow;
        var was = previous.EffectiveStatus(PresencePolicy.FreshnessWindow, now);
        var isNow = current.EffectiveStatus(PresencePolicy.FreshnessWindow, now);
        if (was is PresenceStatus.Online or PresenceStatus.InGame) return;
        if (isNow is not (PresenceStatus.Online or PresenceStatus.InGame)) return;
        if (_announcedOnline.TryGetValue(friendPublicKey, out var last) && now - last < TimeSpan.FromMinutes(10)) return;

        var friend = _friends?.Load().FirstOrDefault(entry => entry.PublicKey == friendPublicKey);
        if (friend is null || friend.Paused || friend.Blocked) return;

        _announcedOnline[friendPublicKey] = now;
        var handle = PeerName.Handle(friend.DisplayName, friend.PublicKey);
        Notify(isNow == PresenceStatus.InGame && current.CurrentGameTitle is { Length: > 0 } game
                ? P7($"{handle} joue à {game}", $"{handle} is playing {game}")
                : P7($"{handle} est en ligne", $"{handle} is online"),
            P7("Ouvre la page Amis pour l’inviter.", "Open the Friends page to invite them."));
    }
}
