using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CubeShelf.Core.Social.Lan;

/// <summary>How announcements leave and arrive. A seam, so the node can be tested without a network.</summary>
public interface ILanAnnouncer : IDisposable
{
    /// <summary>Raised for every datagram received, with the address it came from.</summary>
    event Action<byte[], IPAddress>? Received;

    void Start();

    Task SendAsync(byte[] datagram, CancellationToken cancellationToken = default);
}

/// <summary>
/// UDP broadcast on <see cref="LanProtocol.AnnouncementPort"/>, to every IPv4 network this
/// machine is on.
///
/// Broadcast rather than multicast because it needs no group membership per interface and
/// crosses every home router's switch; and to each interface's own broadcast address as well as
/// the limited one, because Windows sends 255.255.255.255 out of one interface only.
/// </summary>
public sealed class UdpBroadcastAnnouncer : ILanAnnouncer
{
    private readonly int _port;
    private UdpClient? _client;
    private CancellationTokenSource? _lifetime;

    public UdpBroadcastAnnouncer(int port = LanProtocol.AnnouncementPort) => _port = port;

    public event Action<byte[], IPAddress>? Received;

    public void Start()
    {
        if (_client is not null) return;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Two CubeShelf on one machine -- two profiles, a test -- both hear announcements.
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.EnableBroadcast = true;
        socket.Bind(new IPEndPoint(IPAddress.Any, _port));

        _client = new UdpClient { Client = socket };
        _lifetime = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_client, _lifetime.Token);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var received = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (received.Buffer.Length <= LanProtocol.MaximumAnnouncementBytes)
                    Received?.Invoke(received.Buffer, received.RemoteEndPoint.Address);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // An ICMP "port unreachable" from a previous send surfaces here on Windows; it
                // says nothing about the next datagram.
            }
        }
    }

    public async Task SendAsync(byte[] datagram, CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client is null) return;

        foreach (var target in BroadcastTargets())
        {
            try
            {
                await client.SendAsync(datagram, new IPEndPoint(target, _port), cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // One unplugged adapter must not silence the others.
            }
        }
    }

    private static IEnumerable<IPAddress> BroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return targets;
        }

        foreach (var adapter in interfaces)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var address = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                if (mask.All(part => part == 0)) continue;
                var broadcast = new byte[4];
                for (var index = 0; index < 4; index++) broadcast[index] = (byte)(address[index] | ~mask[index]);
                targets.Add(new IPAddress(broadcast));
            }
        }

        return targets;
    }

    public void Dispose()
    {
        _lifetime?.Cancel();
        _client?.Dispose();
        _lifetime?.Dispose();
        _client = null;
    }
}
