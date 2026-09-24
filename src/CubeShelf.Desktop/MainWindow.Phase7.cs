using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private bool _phase7ConnectivityFailed;
    private string _phase7ConnectivityDetail = "";

    private string P7(string french, string english) =>
        UiLocalization.IsEnglish(_preferences.Language) ? english : french;

    private void InitializePhase7Finalization()
    {
        KeyDown += Phase7KeyDown;
        LanguagePicker.SelectionChanged += Phase7LanguageChanged;
        ApplyPhase7Accessibility();
        RefreshPhase7LocalizedState();
        MarkConnectivityHealthyPhase7();
    }

    private void DisposePhase7Finalization()
    {
        KeyDown -= Phase7KeyDown;
        LanguagePicker.SelectionChanged -= Phase7LanguageChanged;
    }

    private void Phase7LanguageChanged(object? sender, SelectionChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyPhase7Accessibility();
            RefreshPhase7LocalizedState();
            RefreshPortableQueueView();
            RefreshDolphinStatusPhase4Core();
            RefreshStoragePhase6();
            RefreshParityGameDetails();
            RefreshFriendsView();
        }, DispatcherPriority.Background);
    }

    private void RefreshPhase7LocalizedState()
    {
        if (ConnectivityBanner is not null && _phase7ConnectivityFailed)
            ConnectivityText.Text = BuildConnectivityMessagePhase7(_phase7ConnectivityDetail);

        if (ShortcutSummaryText is not null)
        {
            ShortcutSummaryText.Text = P7(
                "Ctrl+1 Bibliothèque • Ctrl+2 Favoris • Ctrl+3 Mods • Ctrl+4 Téléchargements • Ctrl+5 Paramètres • Ctrl+6 Amis • Ctrl+7 Mon profil • Ctrl+F Rechercher • F5 Actualiser • Échap Retour",
                "Ctrl+1 Library • Ctrl+2 Favorites • Ctrl+3 Mods • Ctrl+4 Downloads • Ctrl+5 Settings • Ctrl+6 Friends • Ctrl+7 My profile • Ctrl+F Search • F5 Refresh • Esc Back");
        }
    }

    private void ApplyPhase7Accessibility()
    {
        ConfigureAccessiblePhase7(NavLibraryButton, P7("Bibliothèque", "Library"), "Ctrl+1");
        ConfigureAccessiblePhase7(NavFavoritesButton, P7("Favoris", "Favorites"), "Ctrl+2");
        ConfigureAccessiblePhase7(NavModsButton, P7("Mods du jeu sélectionné", "Mods for selected game"), "Ctrl+3");
        ConfigureAccessiblePhase7(NavDownloadsButton, P7("Téléchargements", "Downloads"), "Ctrl+4");
        ConfigureAccessiblePhase7(NavFriendsButton, P7("Amis", "Friends"), "Ctrl+6");
        ConfigureAccessiblePhase7(NavSettingsButton, P7("Paramètres", "Settings"), "Ctrl+5");
        ConfigureAccessiblePhase7(ProfileChipButton, P7("Mon profil", "My profile"), "Ctrl+7");
        ConfigureAccessiblePhase7(IdentityNameBox, P7("Ton pseudo", "Your pseudo"));
        ConfigureAccessiblePhase7(CreateIdentityButton, P7("Créer mon identité", "Create my identity"));
        ConfigureAccessiblePhase7(ClipboardAddButton, P7("Ajouter l’ami trouvé dans le presse-papiers", "Add the friend found in the clipboard"));
        AutomationProperties.SetLiveSetting(ClipboardInviteText, AutomationLiveSetting.Polite);
        ConfigureAccessiblePhase7(LibrarySearchBox, P7("Rechercher dans la bibliothèque", "Search the library"), "Ctrl+F");
        ConfigureAccessiblePhase7(PlayButton, P7("Jouer ou arrêter le jeu sélectionné", "Play or stop the selected game"));
        ConfigureAccessiblePhase7(GameCase, P7("Boîtier 3D du jeu. Cliquer pour retourner la jaquette.", "3D game case. Click to flip the cover."));
        ConfigureAccessiblePhase7(PreviousModImageButton, P7("Image précédente du mod", "Previous mod image"));
        ConfigureAccessiblePhase7(NextModImageButton, P7("Image suivante du mod", "Next mod image"));
        ConfigureAccessiblePhase7(OpenModImageButton, P7("Ouvrir l’image du mod", "Open mod image"));
        ConfigureAccessiblePhase7(RetryConnectivityButton, P7("Réessayer les vérifications réseau", "Retry network checks"));
        ConfigureAccessiblePhase7(HostLobbyButton,
            P7("Créer un salon et inviter mes amis", "Create a lobby and invite my friends"));
        ConfigureAccessiblePhase7(OpenCompanionButton,
            P7("Ouvrir le compagnon en ligne du runtime", "Open the runtime’s online companion"));
        ConfigureAccessiblePhase7(InvitePayloadBox,
            P7("Code d’invitation donné par le compagnon", "Invitation code given by the companion"));
        ConfigureAccessiblePhase7(PublishInviteButton,
            P7("Publier cette invitation pour mes amis", "Publish this invitation for my friends"));
        ConfigureAccessiblePhase7(CancelInviteButton,
            P7("Retirer l’invitation publiée", "Withdraw the published invitation"));

        // The invitation status is the only place that says an invitation is not a notification.
        AutomationProperties.SetLiveSetting(InviteStatusText, AutomationLiveSetting.Polite);

        AutomationProperties.SetName(ToastHost, P7("Notification CubeShelf", "CubeShelf notification"));
        AutomationProperties.SetLiveSetting(ToastHost, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(ConnectivityBanner, P7("État de connexion", "Connection status"));
        AutomationProperties.SetLiveSetting(ConnectivityBanner, AutomationLiveSetting.Polite);

        ToolTip.SetTip(NavLibraryButton, P7("Bibliothèque • Ctrl+1", "Library • Ctrl+1"));
        ToolTip.SetTip(NavFavoritesButton, P7("Favoris • Ctrl+2", "Favorites • Ctrl+2"));
        ToolTip.SetTip(NavModsButton, P7("Mods • Ctrl+3", "Mods • Ctrl+3"));
        ToolTip.SetTip(NavDownloadsButton, P7("Téléchargements • Ctrl+4", "Downloads • Ctrl+4"));
        ToolTip.SetTip(NavFriendsButton, P7("Amis • Ctrl+6", "Friends • Ctrl+6"));
        ToolTip.SetTip(NavSettingsButton, P7("Paramètres • Ctrl+5", "Settings • Ctrl+5"));
        ToolTip.SetTip(ProfileChipButton, P7("Mon profil • Ctrl+7", "My profile • Ctrl+7"));
        ToolTip.SetTip(LibrarySearchBox, P7("Rechercher • Ctrl+F", "Search • Ctrl+F"));
    }

    private static void ConfigureAccessiblePhase7(Avalonia.StyledElement element, string name, string? accelerator = null)
    {
        AutomationProperties.SetName(element, name);
        if (!string.IsNullOrWhiteSpace(accelerator))
            AutomationProperties.SetAcceleratorKey(element, accelerator);
    }

    private async void Phase7KeyDown(object? sender, KeyEventArgs args)
    {
        var primaryModifier =
            (args.KeyModifiers & KeyModifiers.Control) != 0 ||
            (args.KeyModifiers & KeyModifiers.Meta) != 0;

        if (primaryModifier)
        {
            switch (args.Key)
            {
                case Key.D1:
                    ParityShowLibrary(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D2:
                    ParityShowFavorites(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D3:
                    ParityOpenMods(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D4:
                    ParityShowDownloads(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D5:
                    // Ctrl+5 stays Settings: it is in users' fingers and printed in the
                    // shortcut summary. Friends takes the next free number instead.
                    ParityShowSettings(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D6:
                    ParityShowFriends(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.D7:
                    ParityShowProfile(this, new RoutedEventArgs());
                    args.Handled = true;
                    return;
                case Key.F:
                    if (!LibraryView.IsVisible)
                        ParityShowLibrary(this, new RoutedEventArgs());
                    LibrarySearchBox.Focus();
                    LibrarySearchBox.SelectAll();
                    args.Handled = true;
                    return;
            }
        }

        if (args.Key == Key.F5)
        {
            args.Handled = true;
            await RefreshCurrentViewPhase7Async();
            return;
        }

        if (args.Key == Key.Escape)
        {
            if (GameView.IsVisible)
                ParityShowLibrary(this, new RoutedEventArgs());
            // Any page that is not the library or a game goes back the same way, so a page added
            // later is covered without being named here.
            else if (!LibraryView.IsVisible)
            {
                if (_selectedGame is not null)
                    ParityBackToGame(this, new RoutedEventArgs());
                else
                    ParityShowLibrary(this, new RoutedEventArgs());
            }
            else if (!string.IsNullOrEmpty(LibrarySearchBox.Text))
            {
                LibrarySearchBox.Text = "";
                RefreshLibraryParity();
            }
            args.Handled = true;
        }
    }

    private async Task RefreshCurrentViewPhase7Async()
    {
        if (FriendsView.IsVisible)
        {
            await RefreshFriendsSilentlyAsync();
            RefreshFriendsView();
            await CheckClipboardForFriendCodeAsync();
            return;
        }

        if (ProfileView.IsVisible)
        {
            RefreshIdentityUi();
            return;
        }

        if (ModsView.IsVisible)
        {
            RefreshMods(this, new RoutedEventArgs());
            return;
        }

        if (SettingsView.IsVisible)
        {
            RefreshDolphinStatusPhase4Core();
            RefreshStoragePhase6();
            await CheckLauncherUpdatePhase4Async(interactive: false);
            return;
        }

        if (GameView.IsVisible && _selectedGame is not null)
        {
            await RefreshGameUpdatePhase3Async(_selectedGame, forceUi: true, showToast: true);
            return;
        }

        await RefreshAllGameUpdatesPhase3Async(showToast: true);
    }

    private async void RetryConnectivityPhase7(object? sender, RoutedEventArgs args)
    {
        RetryConnectivityButton.IsEnabled = false;
        ConnectivityText.Text = P7("Nouvelle tentative…", "Retrying…");
        try
        {
            if (_selectedGame is not null)
                await RefreshGameUpdatePhase3Async(_selectedGame, forceUi: GameView.IsVisible, showToast: false);
            else
                await RefreshAllGameUpdatesPhase3Async(showToast: false);

            await CheckLauncherUpdatePhase4Async(interactive: false);
            if (!_phase7ConnectivityFailed)
                MarkConnectivityHealthyPhase7();
        }
        finally
        {
            RetryConnectivityButton.IsEnabled = true;
        }
    }


    private static bool IsConnectivityFailurePhase7(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or TimeoutException or System.Net.WebException or System.Net.Sockets.SocketException)
                return true;
        }
        return false;
    }

    private string BuildConnectivityMessagePhase7(string detail)
    {
        var prefix = P7(
            "Connexion indisponible ou service distant inaccessible. Les données locales restent utilisables.",
            "Connection unavailable or remote service unreachable. Local data remains usable.");
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}\n{detail}";
    }

    private void MarkConnectivityIssuePhase7(string? detail)
    {
        _phase7ConnectivityFailed = true;
        _phase7ConnectivityDetail = detail ?? "";
        Dispatcher.UIThread.Post(() =>
        {
            ConnectivityText.Text = BuildConnectivityMessagePhase7(_phase7ConnectivityDetail);
            ConnectivityBanner.IsVisible = true;
        });
    }

    private void MarkConnectivityHealthyPhase7()
    {
        _phase7ConnectivityFailed = false;
        _phase7ConnectivityDetail = "";
        Dispatcher.UIThread.Post(() => ConnectivityBanner.IsVisible = false);
    }
    private Window CreatePhase7Dialog(string title, double width, double height, bool canResize = false)
    {
        var dialog = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 480),
            MinHeight = Math.Min(height, 220),
            CanResize = canResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        AutomationProperties.SetName(dialog, title);
        return dialog;
    }

}
