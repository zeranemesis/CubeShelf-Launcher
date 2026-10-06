using System.Net;
using System.Net.Sockets;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>One datagram as it arrived: its exact bytes and who sent it.</summary>
public readonly record struct MeshDatagram(byte[] Data, IPEndPoint From);

/// <summary>
/// Where the transport sends and receives datagrams. The real one is a UDP socket; tests put a
/// simulated network behind it -- NATs, loss, many nodes -- so the whole network can be exercised
/// in one process.
/// </summary>
public interface IMeshSocket : IDisposable
{
    IPEndPoint LocalEndPoint { get; }

    /// <summary>Fire and forget, like UDP itself. Never throws for a network reason.</summary>
    void Send(byte[] datagram, IPEndPoint destination);

    ValueTask<MeshDatagram> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A UDP socket on all interfaces, IPv6 and IPv4 at once where the system allows it.
/// </summary>
public sealed class UdpMeshSocket : IMeshSocket
{
    /// <summary>
    /// SIO_UDP_CONNRESET. Without turning it off, Windows reports an ICMP "port unreachable" from
    /// one peer as an error on the next receive -- any peer that went away would interrupt
    /// receiving from all the others.
    /// </summary>
    private const int UdpConnectionReset = unchecked((int)0x9800000C);

    private readonly Socket _socket;
    private readonly bool _dualMode;
    private readonly EndPoint _anyRemote;
    private bool _disposed;

    /// <param name="port">0 for a port of the system's choosing.</param>
    public UdpMeshSocket(int port, IPAddress? bindTo = null)
    {
        if (bindTo is null && Socket.OSSupportsIPv6)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                _dualMode = true;
                _anyRemote = new IPEndPoint(IPAddress.IPv6Any, 0);
            }
            catch (SocketException)
            {
                _socket?.Dispose();
                _socket = BindIPv4(IPAddress.Any, port);
                _anyRemote = new IPEndPoint(IPAddress.Any, 0);
            }
        }
        else
        {
            var address = bindTo ?? IPAddress.Any;
            _socket = address.AddressFamily == AddressFamily.InterNetworkV6
                ? new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(address, port));
            _anyRemote = new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _socket.IOControl(UdpConnectionReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (SocketException)
            {
            }
        }
    }

    public IPEndPoint LocalEndPoint => MeshAddresses.Normalize((IPEndPoint)_socket.LocalEndPoint!);

    public void Send(byte[] datagram, IPEndPoint destination)
    {
        if (_disposed) return;
        var target = _dualMode && destination.AddressFamily == AddressFamily.InterNetwork
            ? new IPEndPoint(destination.Address.MapToIPv6(), destination.Port)
            : destination;
        try
        {
            _socket.SendTo(datagram, 0, datagram.Length, SocketFlags.None, target);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or ArgumentException)
        {
            // Unreachable network, an address family this socket cannot reach: as lost as any datagram.
        }
    }

    public async ValueTask<MeshDatagram> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[MeshPackets.MaximumDatagram + 256];
        while (true)
        {
            try
            {
                var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _anyRemote, cancellationToken).ConfigureAwait(false);
                if (result.RemoteEndPoint is not IPEndPoint from) continue;
                return new MeshDatagram(buffer.AsSpan(0, result.ReceivedBytes).ToArray(), MeshAddresses.Normalize(from));
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // A datagram too large, a reset that slipped through: drop it, keep receiving.
            }
        }
    }

    private static Socket BindIPv4(IPAddress address, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(address, port));
        return socket;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
    }
}
