using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Lan;

namespace CubeShelf.Desktop;

/// <summary>
/// Friends on the same network: seen and read directly, invitations included, with nothing to set
/// up -- and strangers befriended in person, both opening "Local network" at the same time, like
/// pairing two Bluetooth devices. The network only ever carries the same sealed document the sync
/// folder does; see <see cref="LanNode"/> for what it reveals and to whom.
/// </summary>
public sealed partial class MainWindow
{
    private LanNode? _lan;
    private string _lanFailure = "";

    private void StartLan()
    {
        StopLan();
        _lanFailure = "";
        if (_identity is null || _friends is null || !HasIdentity || !_preferences.LanVisible) return;

        var node = new LanNode(
            _identity,
            _friends,
            new UdpBroadcastAnnouncer(),
            () => _preferences.FriendsDisplayName,
            () => IsAddressVerified() ? _preferences.PresenceUrl : "");

        node.FriendDocumentReceived += outcome => Dispatcher.UIThread.Post(() => ApplyFriendOutcomes(new[] { outcome }));
        node.Changed += () => Dispatcher.UIThread.Post(OnLanChanged);
        node.FriendAdded += handle => Dispatcher.UIThread.Post(() =>
        {
            ShowToastParity(P7("Amis", "Friends"),
                P7($"{handle} a accepté : vous êtes amis.", $"{handle} accepted: you are friends."));
            _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
            RefreshFriendsView();
        });

        try
        {
            node.Start();
            _lan = node;
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            // Another program on the port, or a policy forbidding listening: the rest of the
            // friends feature works without it, and the profile page says why it is off.
            _lanFailure = exception.Message;
            node.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Starts the node when it should run and does not, stops it in the opposite case.</summary>
    private void EnsureLan()
    {
        var wanted = _identity is not null && _friends is not null && HasIdentity && _preferences.LanVisible;
        if (wanted && _lan is null) StartLan();
        else if (!wanted && _lan is not null) StopLan();
    }

    private void StopLan()
    {
        var node = _lan;
        _lan = null;
        if (node is null) return;
        try
        {
            node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or OperationCanceledException)
        {
        }
    }

    /// <summary>At shutdown: friends on the network pull the farewell, then the node goes.</summary>
    private void FarewellLan()
    {
        var node = _lan;
        if (node is null) return;
        try
        {
            node.FarewellAsync(TimeSpan.FromMilliseconds(400)).WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
        }
        StopLan();
    }

    private Action? _lanDialogRefresh;

    private void OnLanChanged()
    {
        _lanDialogRefresh?.Invoke();
        if (FriendsView.IsVisible) RefreshFriendsView();
    }

    private bool IsOnLan(string friendKey) => _lan?.IsOnNetwork(friendKey) == true;

    /// <summary>
    /// "Local network": findable while the window is open, and showing who else is, with what
    /// they asked. Closing it stops being findable -- nobody stays visible by accident.
    /// </summary>
    private async void OpenLanDialog(object? sender, RoutedEventArgs args)
    {
        if (!HasIdentity)
        {
            ParityShowProfile(sender, args);
            return;
        }

        if (_lan is null)
        {
            ShowToastParity(P7("Réseau local", "Local network"), _lanFailure.Length > 0
                ? P7($"Le réseau local est indisponible : {_lanFailure}", $"The local network is unavailable: {_lanFailure}")
                : P7("Le réseau local est désactivé : active-le sur la page Mon profil.",
                     "The local network is off: turn it on in My profile."));
            return;
        }

        var node = _lan;
        var dialog = CreatePhase7Dialog(P7("Ajouter depuis le réseau local", "Add from the local network"), 640, 520);
        var people = new StackPanel { Spacing = 8 };
        var requests = new StackPanel { Spacing = 8 };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.Gray };

        void Render()
        {
            requests.Children.Clear();
            foreach (var request in node.IncomingRequests)
            {
                var accept = new Button { Content = P7("Accepter", "Accept"), Classes = { "primary" } };
                var decline = new Button { Content = P7("Refuser", "Decline") };
                accept.Click += async (_, _) =>
                {
                    accept.IsEnabled = decline.IsEnabled = false;
                    var result = await node.AcceptAsync(request);
                    status.Text = result switch
                    {
                        LanIntroductionResult.Accepted => P7($"{request.Handle} est ton ami.", $"{request.Handle} is your friend."),
                        LanIntroductionResult.Unreachable => P7($"{request.Handle} est ajouté, mais n’a pas pu être prévenu : il apparaîtra en attente jusqu’à ce qu’il t’ajoute.",
                                                                $"{request.Handle} is added but could not be told: they show as pending until they add you."),
                        _ => P7($"{request.Handle} est ajouté.", $"{request.Handle} is added.")
                    };
                    _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
                    RefreshFriendsView();
                    Render();
                };
                decline.Click += (_, _) =>
                {
                    node.Decline(request);
                    Render();
                };
                requests.Children.Add(Row(P7($"{request.Handle} veut t’ajouter.", $"{request.Handle} wants to add you."), accept, decline));
            }

            people.Children.Clear();
            foreach (var peer in node.Strangers)
            {
                var awaiting = node.IsAwaiting(peer.PublicKey);
                var add = new Button
                {
                    Content = awaiting ? P7("Demande envoyée", "Request sent") : P7("Ajouter", "Add"),
                    IsEnabled = !awaiting,
                    Classes = { "primary" }
                };
                add.Click += async (_, _) =>
                {
                    add.IsEnabled = false;
                    var result = await node.RequestFriendshipAsync(peer);
                    status.Text = result switch
                    {
                        LanIntroductionResult.Sent => P7($"Demande envoyée à {peer.Handle} : il doit l’accepter sur son PC.",
                                                         $"Request sent to {peer.Handle}: they accept it on their PC."),
                        LanIntroductionResult.AlreadyFriends => P7($"{peer.Handle} t’avait déjà ajouté : vous êtes amis.",
                                                                   $"{peer.Handle} had already added you: you are friends."),
                        LanIntroductionResult.Refused => P7($"{peer.Handle} n’accepte pas de demande en ce moment : il doit ouvrir « Réseau local » lui aussi.",
                                                            $"{peer.Handle} is not taking requests right now: they need to open “Local network” too."),
                        LanIntroductionResult.NotGenuine => P7($"Celui qui a répondu n’a pas pu prouver qu’il est {peer.Handle}. Rien n’a été échangé.",
                                                              $"Whoever answered could not prove they are {peer.Handle}. Nothing was exchanged."),
                        _ => P7($"{peer.Handle} ne répond pas. Le pare-feu de son PC bloque peut-être CubeShelf.",
                                $"{peer.Handle} does not answer. Their PC’s firewall may be blocking CubeShelf.")
                    };
                    if (result == LanIntroductionResult.AlreadyFriends)
                    {
                        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
                        RefreshFriendsView();
                    }
                    Render();
                };
                people.Children.Add(Row(peer.Handle, add));
            }

            if (people.Children.Count == 0)
                people.Children.Add(new TextBlock
                {
                    Text = P7("Personne pour l’instant. Ton ami doit ouvrir cette même fenêtre sur son PC, sur le même réseau.",
                              "Nobody yet. Your friend opens this same window on their PC, on the same network."),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.Gray
                });
        }

        Control Row(string text, params Button[] buttons)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var button in buttons) actions.Children.Add(button);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            return grid;
        }

        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = P7($"Tant que cette fenêtre est ouverte, les PC de ton réseau te voient comme {OwnHandle()} et peuvent te demander en ami. Fermer la fenêtre te rend invisible à nouveau.",
                                  $"While this window is open, PCs on your network see you as {OwnHandle()} and can ask to be your friend. Closing it makes you invisible again."),
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    requests,
                    new TextBlock { Text = P7("SUR TON RÉSEAU", "ON YOUR NETWORK"), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.Gray },
                    people,
                    status
                }
            }
        };

        _lanDialogRefresh = Render;
        node.SetDiscoverable(true);
        Render();

        // Who is around changes on its own -- people arrive, people leave -- and being findable
        // lasts ten minutes at a time: renewed while the window stays open.
        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            if (++ticks % 48 == 0) node.SetDiscoverable(true);
            Render();
        };
        timer.Start();
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            timer.Stop();
            _lanDialogRefresh = null;
            node.SetDiscoverable(false);
            RefreshFriendsView();
        }
    }
}
