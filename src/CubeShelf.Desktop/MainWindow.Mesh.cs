using System.Net;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Social;
using CubeShelf.Core.Social.Mesh;

namespace CubeShelf.Desktop;

/// <summary>
/// The CubeShelf network: how friends see each other from anywhere, with no server and no cloud.
/// Every CubeShelf is a node; the reachable ones hold a share of the network and relay for the
/// others. See <see cref="MeshNode"/> for the network and <see cref="MeshFriends"/> for what friends
/// do over it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A friend request that came through the network, as the Friends page lists it.</summary>
    public sealed record MeshRequestRow(string PublicKey, string Text);

    private MeshNode? _meshNode;
    private MeshFriends? _meshFriends;
    private Task? _meshStarting;
    private CancellationTokenSource? _meshLifetime;
    private string _meshFailure = "";
    private readonly HashSet<string> _announcedRequests = new(StringComparer.Ordinal);
    private DispatcherTimer? _networkChangeDebounce;
    private bool _watchingNetwork;

    private bool MeshWanted => _identity is not null && _friends is not null && HasIdentity && _preferences.MeshEnabled;

    /// <summary>Starts the network when it should run and does not, stops it in the opposite case.</summary>
    private void EnsureMesh()
    {
        if (MeshWanted && _meshNode is null && _meshStarting is null) _meshStarting = StartMeshAsync();
        else if (!MeshWanted && (_meshNode is not null || _meshStarting is not null)) StopMesh();
    }

    private async Task StartMeshAsync()
    {
        _meshFailure = "";
        var lifetime = new CancellationTokenSource();
        _meshLifetime = lifetime;
        MeshNode? node = null;
        try
        {
            var socket = OpenMeshSocket(_preferences.MeshPort);
            var mapper = _preferences.MeshMapPort ? new PortMapper() : null;
            var options = new MeshNodeOptions
            {
                MapPort = _preferences.MeshMapPort,
                NodesFile = Path.Combine(_paths.ConfigurationDirectory, "mesh-nodes.json")
            };
            // The proof of work behind the node id takes a moment: off the UI thread.
            node = await MeshNode.CreateAsync(socket, options, mapper, () => new UdpMeshSocket(0), lifetime.Token);
            if (lifetime.IsCancellationRequested || _identity is null || _friends is null)
            {
                await node.DisposeAsync();
                return;
            }

            var friends = new MeshFriends(node, _identity, _friends, BuildNetworkFriendCode);
            node.Changed += () => Dispatcher.UIThread.Post(OnMeshChanged);
            friends.Changed += () => Dispatcher.UIThread.Post(OnMeshFriendsChanged);
            friends.DocumentReceived += outcome =>
            {
                if (outcome.Snapshot is not null) _presence?.NoteSnapshot(outcome.FriendPublicKey, outcome.Snapshot);
                Dispatcher.UIThread.Post(() => ApplyFriendOutcomes(new[] { outcome }));
            };

            node.Start();
            friends.Start();
            _meshNode = node;
            _meshFriends = friends;
            WatchNetworkChanges();

            // The presence service starts again with the network as a way to publish and to read.
            StartPresenceService();
            OnMeshChanged();
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _meshFailure = exception is OperationCanceledException ? "" : exception.Message;
            if (node is not null) await node.DisposeAsync();
            RefreshMeshUi();
        }
        finally
        {
            _meshStarting = null;
        }
    }

    /// <summary>The usual port when it is free; another CubeShelf on this PC (a second profile) takes any other.</summary>
    private static UdpMeshSocket OpenMeshSocket(int port)
    {
        try
        {
            return new UdpMeshSocket(port is > 0 and <= 65535 ? port : 0);
        }
        catch (System.Net.Sockets.SocketException)
        {
            return new UdpMeshSocket(0);
        }
    }

    private void StopMesh()
    {
        _meshLifetime?.Cancel();
        var friends = _meshFriends;
        var node = _meshNode;
        _meshFriends = null;
        _meshNode = null;
        try
        {
            // Bounded: removing the router's port mapping is worth a moment, not a window that hangs.
            Task.WhenAll(
                    friends?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    node?.DisposeAsync().AsTask() ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or OperationCanceledException)
        {
        }
        _meshLifetime?.Dispose();
        _meshLifetime = null;
    }

    private void RestartMesh()
    {
        StopMesh();
        EnsureMesh();
        StartPresenceService();
        RefreshMeshUi();
    }

    /// <summary>A new Wi-Fi, a cable plugged in: the node finds out again how it is reached. Once per burst.</summary>
    private void WatchNetworkChanges()
    {
        if (_watchingNetwork) return;
        _watchingNetwork = true;
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _networkChangeDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _networkChangeDebounce.Stop();
            _networkChangeDebounce.Tick -= OnNetworkSettled;
            _networkChangeDebounce.Tick += OnNetworkSettled;
            _networkChangeDebounce.Start();
        });
    }

    private void OnNetworkSettled(object? sender, EventArgs args)
    {
        _networkChangeDebounce?.Stop();
        _meshNode?.NetworkChanged();
    }

    /// <summary>Our code for the network: the identity, the pseudo, and ways in through which a newcomer can join.</summary>
    private string BuildNetworkFriendCode()
    {
        if (_identity is null || !HasIdentity) return "";
        var entries = _meshNode?.EntryPoints() ?? Array.Empty<IPEndPoint>();
        return FriendCode.EncodeForNetwork(_identity.PublicKey, _preferences.FriendsDisplayName, entries,
            IsAddressVerified() ? _preferences.PresenceUrl : null);
    }

    private void OnMeshChanged()
    {
        RefreshOwnFriendCode();
        RefreshMeshUi();
        // Where friends can reach us is inside our document: a new relay, a newly opened port, and
        // it has to be said again.
        _presence?.RequestPublish(PresencePublishReason.ProfileChanged);
        if (FriendsView.IsVisible) RefreshFriendsView();
    }

    private void OnMeshFriendsChanged()
    {
        if (_meshFriends is { } friends)
        {
            foreach (var request in friends.Requests.Where(request => _announcedRequests.Add(request.PublicKey)))
                Notify(P7("Demande d’ami", "Friend request"),
                    P7($"{request.Handle} veut t’ajouter. Ouvre la page Amis pour répondre.",
                       $"{request.Handle} wants to add you. Open the Friends page to answer."));
        }
        RefreshMeshRequests();
        if (FriendsView.IsVisible) RefreshFriendsView();
    }

    /// <summary>Everything a change of the friends list has to set in motion: the next document, and the network's pointers and sessions.</summary>
    private void OnFriendsListChanged()
    {
        _meshFriends?.FriendsChanged();
        _presence?.RequestPublish(PresencePublishReason.FriendsChanged);
    }

    /// <summary>
    /// A friend was just added from a code: their code's entry points help us join, and a request
    /// reaches them through the network -- so they see us without anyone pasting a code back.
    /// </summary>
    private void AfterAddingFromCode(FriendCodePayload payload)
    {
        _meshNode?.AddSeeds(payload.Seeds);
        // The friend layer sends the request itself, and keeps trying until it lands.
        OnFriendsListChanged();
    }

    // ----------------------------------------------------------------- the Friends page

    private void RefreshMeshRequests()
    {
        if (MeshRequestsPanel is null) return;
        var requests = _meshFriends?.Requests ?? Array.Empty<MeshFriendRequest>();
        MeshRequestsList.ItemsSource = requests
            .Select(request => new MeshRequestRow(request.PublicKey, request.Note.Length > 0
                ? P7($"{request.Handle} veut t’ajouter : « {request.Note} »", $"{request.Handle} wants to add you: “{request.Note}”")
                : P7($"{request.Handle} veut t’ajouter.", $"{request.Handle} wants to add you.")))
            .ToArray();
        MeshRequestsPanel.IsVisible = requests.Count > 0 && HasIdentity;
    }

    private void AcceptMeshRequest(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: MeshRequestRow row } || _meshFriends is not { } mesh || _friends is null || _identity is null) return;
        var request = mesh.Requests.FirstOrDefault(entry => entry.PublicKey == row.PublicKey);
        if (request is null) return;

        var name = request.Code.DisplayName.Length > 0 ? request.Code.DisplayName : P7("Ami", "Friend");
        if (!_friends.TryAdd(request.Code, name, _identity.PublicKey, out var error))
        {
            ShowToastParity(P7("Amis", "Friends"), error);
            mesh.Dismiss(request);
            return;
        }

        mesh.Dismiss(request);
        _meshNode?.AddSeeds(request.Code.Seeds);
        OnFriendsListChanged();
        _presence?.PollEagerly();
        _ = RefreshFriendsSilentlyAsync();
        RefreshFriendsView();
        ShowToastParity(P7("Amis", "Friends"), P7($"{request.Handle} est ton ami.", $"{request.Handle} is your friend."));
    }

    private void DeclineMeshRequest(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: MeshRequestRow row } || _meshFriends is not { } mesh) return;
        var request = mesh.Requests.FirstOrDefault(entry => entry.PublicKey == row.PublicKey);
        if (request is not null) mesh.Dismiss(request);
    }

    // ----------------------------------------------------------------- the profile page

    private void RefreshMeshUi()
    {
        if (MeshStatusText is null) return;
        MeshStatusText.Text = DescribeMesh();
    }

    /// <summary>Where this CubeShelf stands on the network, in one or two sentences.</summary>
    private string DescribeMesh()
    {
        if (!_preferences.MeshEnabled)
            return P7("Désactivé : seuls tes amis sur le même réseau local te voient.",
                      "Off: only friends on the same local network see you.");
        if (_meshFailure.Length > 0)
            return P7($"Le réseau CubeShelf n’a pas pu démarrer : {_meshFailure}", $"The CubeShelf network could not start: {_meshFailure}");
        if (!HasIdentity) return P7("Il démarrera dès que tu auras choisi un pseudo.", "It starts as soon as you choose a pseudo.");
        if (_meshNode is not { } node) return P7("Démarrage…", "Starting…");

        var known = node.Table.Count;
        var mapped = node.Mapping is not null ? P7(" (port ouvert sur ta box)", " (port opened on your router)") : "";
        return node.Reachability switch
        {
            MeshReachability.Starting => P7("Connexion au réseau…", "Joining the network…"),
            MeshReachability.Public => P7(
                $"Connecté et joignable directement{mapped}. Ton PC porte une part du réseau et relaie pour ceux qui ne sont pas joignables. {known} nœud(s) connu(s), {_meshFriends?.ConnectedCount ?? 0} ami(s) en direct.",
                $"Connected and directly reachable{mapped}. Your PC holds a share of the network and relays for those who are not reachable. {known} node(s) known, {_meshFriends?.ConnectedCount ?? 0} friend(s) connected directly."),
            MeshReachability.Relayed => P7(
                $"Connecté, joignable par {node.RelayContacts.Count} relais : ta box ne laisse rien entrer, d’autres CubeShelf te mettent en contact. {known} nœud(s) connu(s), {_meshFriends?.ConnectedCount ?? 0} ami(s) en direct.",
                $"Connected, reachable through {node.RelayContacts.Count} relay(s): your router lets nothing in, other CubeShelf nodes put friends in touch. {known} node(s) known, {_meshFriends?.ConnectedCount ?? 0} friend(s) connected directly."),
            _ when node.Mapping is not null => P7(
                "Aucun autre CubeShelf connu pour l’instant, mais ta box a ouvert le port : ton code ami permettra à un ami d’entrer dans le réseau par ton PC. Envoie-le-lui.",
                "No other CubeShelf known yet, but your router opened the port: your friend code will let a friend into the network through your PC. Send it to them."),
            _ => P7(
                "Aucun autre CubeShelf connu pour l’instant, et ta box n’a pas ouvert le port. Le code ami d’un ami sert de porte d’entrée : ajoute-en un. Si ni toi ni lui n’êtes joignables, vous ne vous verrez que sur le même réseau local.",
                "No other CubeShelf known yet, and your router did not open the port. A friend’s code is a way in: add one. If neither of you is reachable, you only see each other on the same local network.")
        };
    }
}
