using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>How one request to the gateway's port control daemon ended.</summary>
internal enum PortControlOutcome
{
    /// <summary>The gateway did what was asked (mapped, renewed or removed).</summary>
    Success,

    /// <summary>The gateway answered "unsupported version" to PCP: it speaks NAT-PMP only.</summary>
    UseNatPmp,

    /// <summary>The gateway speaks the protocol and said no; <see cref="PortControlReply.ResultCode"/> says why.</summary>
    Refused,

    /// <summary>Nothing acceptable came back within the budget.</summary>
    NoAnswer,

    /// <summary>The gateway said, by ICMP, that nothing listens on the port.</summary>
    Unreachable
}

internal readonly record struct PortControlReply(
    PortControlOutcome Outcome,
    int ResultCode = 0,
    int ExternalPort = 0,
    uint LifetimeSeconds = 0,
    IPAddress? ExternalAddress = null)
{
    public static PortControlReply Of(PortControlOutcome outcome, int resultCode = 0) => new(outcome, resultCode);
}

/// <summary>
/// PCP (RFC 6887) and its predecessor NAT-PMP (RFC 6886): one UDP datagram to the default
/// gateway's port 5351, one back. Most home routers that do either run miniupnpd, which answers
/// both on the same port, and a NAT-PMP-only router answers a PCP request with "unsupported
/// version", which is how the fallback is decided without waiting.
///
/// Everything that arrives is distrusted. The socket is connected to the gateway, so the system
/// drops datagrams from anyone else, and the source is checked again anyway; a response must
/// carry the right version, opcode and length, name the internal port that was asked for, and
/// for PCP echo the random nonce of the request, so another host on the network cannot answer in
/// the gateway's place without also forging its address and guessing 96 random bits.
/// </summary>
internal sealed class NatPmpClient
{
    /// <summary>RFC 6887 section 7: no PCP message is longer.</summary>
    public const int MaximumMessageBytes = 1100;

    public const int NonceLength = 12;

    private const byte PcpVersion = 2;
    private const byte NatPmpVersion = 0;
    private const byte PcpOpMap = 1;
    private const byte NatPmpOpExternalAddress = 0;
    private const byte NatPmpOpMapUdp = 1;
    private const byte ResponseBit = 0x80;
    private const byte UdpProtocolNumber = 17;
    private const int PcpUnsupportedVersion = 1;
    private const int PcpMapMessageLength = 60;

    /// <summary>RFC 6886 section 3.1: the first retransmission comes after 250 ms, then the wait doubles.</summary>
    private static readonly TimeSpan FirstRetransmission = TimeSpan.FromMilliseconds(250);

    private readonly IPEndPoint _server;

    public NatPmpClient(IPEndPoint server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public IPEndPoint Server => _server;

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    // ------------------------------------------------------------------ PCP

    /// <summary>
    /// A PCP MAP request for UDP. A lifetime of 0 removes the mapping the nonce names. The
    /// suggested external address and port are hints the gateway may ignore.
    /// </summary>
    public async Task<PortControlReply> PcpMapAsync(
        int internalPort,
        int suggestedExternalPort,
        IPAddress? suggestedExternalAddress,
        uint lifetimeSeconds,
        byte[] nonce,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        if (nonce is not { Length: NonceLength }) throw new ArgumentException("A PCP nonce is 12 bytes.", nameof(nonce));

        using var socket = ConnectedSocket(out var reply);
        if (socket is null) return reply;
        if (socket.LocalEndPoint is not IPEndPoint { Address: var clientAddress } ||
            clientAddress.AddressFamily != AddressFamily.InterNetwork ||
            clientAddress.Equals(IPAddress.Any))
            return PortControlReply.Of(PortControlOutcome.NoAnswer);

        var request = new byte[PcpMapMessageLength];
        request[0] = PcpVersion;
        request[1] = PcpOpMap;
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4), lifetimeSeconds);
        WriteMappedIPv4(request.AsSpan(8, 16), clientAddress);
        nonce.CopyTo(request, 24);
        request[36] = UdpProtocolNumber;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(40), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(42), (ushort)Math.Clamp(suggestedExternalPort, 0, 65535));
        WriteMappedIPv4(request.AsSpan(44, 16), suggestedExternalAddress ?? IPAddress.Any);

        return await ExchangeAsync(socket, request, data => InterpretPcpMap(data, nonce, internalPort), budget, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static PortControlReply? InterpretPcpMap(ReadOnlySpan<byte> data, ReadOnlySpan<byte> nonce, int internalPort)
    {
        if (data.Length < 4 || data.Length > MaximumMessageBytes) return null;

        // A NAT-PMP-only gateway answers in its own layout: version 0 and a 16-bit result code
        // of 1, "unsupported version" (RFC 6886 section 3.5; the opcode byte varies between
        // implementations). An older PCP answers UNSUPP_VERSION in the PCP header layout. Neither
        // can echo the nonce, so this one answer is taken unauthenticated -- all it can do is
        // make us try NAT-PMP, which we would do on silence anyway.
        if (data[0] == NatPmpVersion)
            return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2)) == PcpUnsupportedVersion
                ? PortControlReply.Of(PortControlOutcome.UseNatPmp)
                : null;
        if (data[0] < PcpVersion)
            return data[3] == PcpUnsupportedVersion ? PortControlReply.Of(PortControlOutcome.UseNatPmp) : null;

        if (data[0] != PcpVersion || data[1] != (ResponseBit | PcpOpMap)) return null;
        if (data.Length % 4 != 0) return null;

        // A header without the MAP payload cannot be tied to our request by its nonce, so it
        // is not believed, even as a refusal: anyone able to forge the gateway's address could
        // otherwise turn PCP and NAT-PMP off with one datagram.
        if (data.Length < PcpMapMessageLength) return null;

        int result = data[3];
        if (!CryptographicOperations.FixedTimeEquals(data.Slice(24, NonceLength), nonce)) return null;
        if (data[36] != UdpProtocolNumber) return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(data.Slice(40)) != internalPort) return null;

        if (result != 0) return PortControlReply.Of(PortControlOutcome.Refused, result);

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4));
        var externalPort = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(42));
        var external = ReadMappedIPv4(data.Slice(44, 16));
        if (lifetime > 0)
        {
            // A live mapping must say where it lives; one that does not is not believed.
            if (externalPort == 0 || external is null || !PortMapper.IsUsableExternalAddress(external, allowLoopback: false))
                return null;
        }
        return new PortControlReply(PortControlOutcome.Success, 0, externalPort, lifetime, lifetime > 0 ? external : null);
    }

    // ------------------------------------------------------------------ NAT-PMP

    /// <summary>A NAT-PMP UDP mapping request; a lifetime of 0 removes it (and the suggested port must then be 0).</summary>
    public async Task<PortControlReply> NatPmpMapAsync(
        int internalPort,
        int suggestedExternalPort,
        uint lifetimeSeconds,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        using var socket = ConnectedSocket(out var reply);
        if (socket is null) return reply;

        var request = new byte[12];
        request[0] = NatPmpVersion;
        request[1] = NatPmpOpMapUdp;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6), (ushort)(lifetimeSeconds == 0 ? 0 : Math.Clamp(suggestedExternalPort, 0, 65535)));
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8), lifetimeSeconds);

        return await ExchangeAsync(socket, request, data => InterpretNatPmpMap(data, internalPort, lifetimeSeconds), budget, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static PortControlReply? InterpretNatPmpMap(ReadOnlySpan<byte> data, int internalPort, uint requestedLifetime)
    {
        if (data.Length < 8 || data.Length > MaximumMessageBytes) return null;
        if (data[0] != NatPmpVersion || data[1] != (ResponseBit | NatPmpOpMapUdp)) return null;

        int result = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
        if (data.Length >= 16 && BinaryPrimitives.ReadUInt16BigEndian(data.Slice(8)) != internalPort) return null;
        if (result != 0) return PortControlReply.Of(PortControlOutcome.Refused, result);
        if (data.Length < 16) return null;

        var externalPort = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(10));
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12));
        if (requestedLifetime > 0 && (lifetime == 0 || externalPort == 0)) return null;
        return new PortControlReply(PortControlOutcome.Success, 0, externalPort, lifetime);
    }

    /// <summary>The gateway's public address. A gateway without one yet says 0.0.0.0, which comes back as null.</summary>
    public async Task<PortControlReply> NatPmpExternalAddressAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        using var socket = ConnectedSocket(out var reply);
        if (socket is null) return reply;

        var request = new byte[] { NatPmpVersion, NatPmpOpExternalAddress };
        return await ExchangeAsync(socket, request, InterpretNatPmpExternalAddress, budget, cancellationToken).ConfigureAwait(false);
    }

    internal static PortControlReply? InterpretNatPmpExternalAddress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || data.Length > MaximumMessageBytes) return null;
        if (data[0] != NatPmpVersion || data[1] != (ResponseBit | NatPmpOpExternalAddress)) return null;

        int result = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
        if (result != 0) return PortControlReply.Of(PortControlOutcome.Refused, result);
        if (data.Length < 12) return null;

        var address = new IPAddress(data.Slice(8, 4));
        return new PortControlReply(PortControlOutcome.Success,
            ExternalAddress: PortMapper.IsUsableExternalAddress(address, allowLoopback: false) ? address : null);
    }

    // ------------------------------------------------------------------ the exchange

    /// <summary>
    /// A UDP socket connected to the gateway. Connecting sends nothing; it makes the system pick
    /// the local address that faces the gateway, drop datagrams from anyone else, and report an
    /// ICMP "port unreachable" as an error instead of leaving us to wait for the whole budget.
    /// </summary>
    private Socket? ConnectedSocket(out PortControlReply failure)
    {
        failure = PortControlReply.Of(PortControlOutcome.NoAnswer);
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect(_server);
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            failure = PortControlReply.Of(PortControlOutcome.Unreachable);
            return null;
        }
    }

    /// <summary>
    /// Sends the request, then again after 250 ms, 500 ms, 1 s... (RFC 6886 section 3.1) until
    /// an acceptable response arrives or the budget is spent. <paramref name="interpret"/> returns
    /// null for a datagram to ignore.
    /// </summary>
    private async Task<PortControlReply> ExchangeAsync(
        Socket socket,
        byte[] request,
        Interpreter interpret,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var wait = FirstRetransmission;
        var buffer = new byte[MaximumMessageBytes + 1];
        var datagrams = 0;

        while (clock.Elapsed < budget)
        {
            try
            {
                await socket.SendAsync(request, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception) when (IsUnreachable(exception))
            {
                return PortControlReply.Of(PortControlOutcome.Unreachable);
            }

            var attemptEnds = clock.Elapsed + wait;
            if (attemptEnds > budget) attemptEnds = budget;

            while (clock.Elapsed < attemptEnds)
            {
                // A flood of junk from the gateway's address must not keep us spinning forever.
                if (++datagrams > 256) return PortControlReply.Of(PortControlOutcome.NoAnswer);

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(attemptEnds - clock.Elapsed);
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), attempt.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception) when (IsUnreachable(exception))
                {
                    return PortControlReply.Of(PortControlOutcome.Unreachable);
                }
                catch (SocketException exception) when (exception.SocketErrorCode == SocketError.MessageSize)
                {
                    continue; // larger than any PCP message: not for us
                }

                if (received.RemoteEndPoint is not IPEndPoint from ||
                    !from.Address.Equals(_server.Address) || from.Port != _server.Port)
                    continue;
                if (received.ReceivedBytes > MaximumMessageBytes) continue;

                var reply = interpret(buffer.AsSpan(0, received.ReceivedBytes));
                if (reply is { } answer) return answer;
            }

            wait += wait;
        }

        return PortControlReply.Of(PortControlOutcome.NoAnswer);
    }

    private delegate PortControlReply? Interpreter(ReadOnlySpan<byte> data);

    private static bool IsUnreachable(SocketException exception) =>
        exception.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused
            or SocketError.HostUnreachable or SocketError.NetworkUnreachable;

    private static void WriteMappedIPv4(Span<byte> destination, IPAddress address)
    {
        destination.Clear();
        destination[10] = 0xff;
        destination[11] = 0xff;
        address.MapToIPv4().GetAddressBytes().CopyTo(destination[12..]);
    }

    /// <summary>The IPv4 address inside ::ffff:a.b.c.d, or null for anything else.</summary>
    private static IPAddress? ReadMappedIPv4(ReadOnlySpan<byte> field)
    {
        for (var i = 0; i < 10; i++)
            if (field[i] != 0) return null;
        if (field[10] != 0xff || field[11] != 0xff) return null;
        return new IPAddress(field[12..16]);
    }
}
