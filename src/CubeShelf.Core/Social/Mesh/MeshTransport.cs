using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

public enum MeshPacketType : byte
{
    HandshakeInit = 1,
    HandshakeResponse = 2,
    HandshakeFinish = 3,
    Transport = 4,
    Cookie = 5,

    /// <summary>A reachability probe: someone dialling us back from an address we never talked to.</summary>
    Probe = 6,

    /// <summary>Opens a hole in our own NAT towards a peer; carries nothing and is ignored on arrival.</summary>
    Punch = 7
}

/// <summary>Sizes and constants of the wire format.</summary>
public static class MeshPackets
{
    public const byte Magic = 0xC5;
    public const byte ProtocolVersion = 1;

    /// <summary>Magic, type, receiver index, counter.</summary>
    public const int TransportHeaderLength = 14;

    public const int CookieLength = 16;
    public const int ProbeNonceLength = 16;

    /// <summary>
    /// The first handshake message is padded to this, and no reply to it is ever larger: a forged
    /// source address gets back no more bytes than the forger sent, so the network cannot be used
    /// to amplify an attack on someone else.
    /// </summary>
    public const int InitLength = 256;

    /// <summary>Under the 1280-byte IPv6 minimum MTU once IP and UDP headers are added.</summary>
    public const int MaximumDatagram = 1232;

    /// <summary>Bytes of a message per fragment: what fits in a datagram after headers, frame header and tag.</summary>
    public const int FragmentPayload = MaximumDatagram - TransportHeaderLength - MeshCrypto.TagLength - 7;

    /// <summary>At most this many addresses in a handshake, so the reply stays within <see cref="InitLength"/>.</summary>
    public const int MaximumAdvertised = 2;

    public static readonly byte[] Prologue = "cubeshelf-mesh-v1"u8.ToArray();

    internal const int InitPayloadLength = InitLength - 2 - 4 - CookieLength - MeshCrypto.PublicKeyLength;
}

internal enum MeshFrame : byte
{
    Fragment = 1,
    Keepalive = 2,
    Nack = 3,
    Close = 4
}

public sealed record MeshTransportOptions
{
    public int MaximumSessions { get; init; } = 1024;
    public int MaximumSessionsPerAddress { get; init; } = 32;
    public int MaximumSessionsPerNode { get; init; } = 4;
    public int MaximumHalfOpen { get; init; } = 256;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2.5);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan KeepaliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>A pinned session that has heard nothing for this long is presumed gone.</summary>
    public TimeSpan DeadAfter { get; init; } = TimeSpan.FromSeconds(75);

    public double HandshakesPerSecondPerAddress { get; init; } = 4;
    public int HandshakesPerSecondBeforeCookies { get; init; } = 48;
    public int MaximumMessageBytes { get; init; } = 160 * 1024;
    public int MaximumConcurrentRequests { get; init; } = 64;
    public int MaximumReassembliesPerSession { get; init; } = 8;

    /// <summary>
    /// What all half-arrived messages may hold together, and what one session's may. Without a
    /// total, a thousand sessions each starting eight large messages would be a gigabyte; with it,
    /// a flood costs the flooder its own messages and nobody else's memory.
    /// </summary>
    public long MaximumBufferedBytes { get; init; } = 32L * 1024 * 1024;
    public long MaximumBufferedPerSession { get; init; } = 1024 * 1024;

    /// <summary>The work every node id must show, ours and our peers'. Lowered only by tests.</summary>
    public int ProofDifficulty { get; init; } = NodeProof.Difficulty;
}

/// <summary>
/// Encrypted, authenticated sessions between nodes over datagrams, with requests and replies on
/// top.
///
/// Every session starts with a Noise XX handshake (<see cref="NoiseHandshake"/>) that also carries
/// each node's proof of work, so a node id is never just claimed. After it, every datagram is
/// AES-GCM under the session keys, numbered, and checked against a replay window; messages larger
/// than a datagram are fragmented, and fragments that go missing are asked for again.
///
/// Built to be exposed to the Internet: nothing a peer sends can make it allocate without bound,
/// block the receive loop, or answer a forged address with more bytes than it received. Handshakes
/// are rate-limited per address, and past a global threshold they need a cookie, which only someone
/// who receives at that address can return -- spoofed floods get nowhere.
/// </summary>
public sealed class MeshTransport : IAsyncDisposable
{
    private readonly IMeshSocket _socket;
    private readonly ECDiffieHellman _nodeKey;
    private readonly ulong _nonce;
    private readonly Func<MeshNodeFlags> _flags;
    private readonly Func<IReadOnlyList<IPEndPoint>> _advertised;
    private readonly MeshTransportOptions _options;

    private readonly object _gate = new();
    private readonly Dictionary<uint, MeshSession> _sessions = new();
    private readonly Dictionary<NodeId, MeshSession> _byNode = new();
    private readonly Dictionary<uint, PendingHandshake> _pending = new();
    private readonly Dictionary<MeshRoute, PendingHandshake> _pendingByRoute = new();
    private readonly Dictionary<uint, HalfOpen> _halfOpen = new();
    private readonly ConcurrentDictionary<(uint Session, uint Request), TaskCompletionSource<byte[]?>> _requests = new();
    private readonly Dictionary<string, TokenBucket> _handshakeBuckets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestSlots;

    private byte[] _cookieSecret = RandomNumberGenerator.GetBytes(32);
    private byte[] _previousCookieSecret = RandomNumberGenerator.GetBytes(32);
    private long _cookieRotatedAt = Environment.TickCount64;
    private long _handshakeSecond;
    private int _handshakesThisSecond;
    private int _nextRequestId;
    private long _bufferedBytes;

    /// <summary>Bytes held across every session's half-arrived messages. For tests and diagnostics.</summary>
    public long BufferedBytes => Interlocked.Read(ref _bufferedBytes);

    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private Task? _maintenanceLoop;
    private bool _disposed;

    public MeshTransport(
        IMeshSocket socket,
        ECDiffieHellman nodeKey,
        ulong proofNonce,
        Func<MeshNodeFlags>? flags = null,
        Func<IReadOnlyList<IPEndPoint>>? advertised = null,
        MeshTransportOptions? options = null)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _nodeKey = nodeKey ?? throw new ArgumentNullException(nameof(nodeKey));
        _nonce = proofNonce;
        _options = options ?? new MeshTransportOptions();
        PublicKey = MeshCrypto.PublicKeyOf(nodeKey);
        if (!NodeProof.IsValid(PublicKey, proofNonce, _options.ProofDifficulty))
            throw new ArgumentException("La preuve de travail du nœud n’est pas valide.", nameof(proofNonce));
        LocalId = NodeProof.IdOf(PublicKey, proofNonce);
        _flags = flags ?? (() => MeshNodeFlags.None);
        _advertised = advertised ?? (() => Array.Empty<IPEndPoint>());
        _requestSlots = new SemaphoreSlim(_options.MaximumConcurrentRequests, _options.MaximumConcurrentRequests);
    }

    public NodeId LocalId { get; }
    public byte[] PublicKey { get; }
    public ulong ProofNonce => _nonce;
    public IPEndPoint LocalEndPoint => _socket.LocalEndPoint;
    public MeshTransportOptions Options => _options;

    /// <summary>A session finished its handshake, in either direction.</summary>
    public event Action<MeshSession>? SessionEstablished;

    public event Action<MeshSession>? SessionClosed;

    /// <summary>A message with no request id. Runs on the receive loop: keep it short.</summary>
    public event Action<MeshSession, MeshMessage>? NotificationReceived;

    /// <summary>A reachability probe arrived, with its nonce.</summary>
    public event Action<byte[], IPEndPoint>? ProbeReceived;

    /// <summary>Answers requests. Returning null sends no reply.</summary>
    public Func<MeshSession, MeshMessage, CancellationToken, Task<byte[]?>>? RequestHandler { get; set; }

    /// <summary>How a packet for a relayed route leaves: set by the relay layer.</summary>
    public Action<MeshRoute, byte[]>? RelaySender { get; set; }

    /// <summary>Counters, for diagnostics and tests.</summary>
    public long HandshakesCompleted => Interlocked.Read(ref _handshakesCompleted);
    public long PacketsDropped => Interlocked.Read(ref _packetsDropped);
    private long _handshakesCompleted;
    private long _packetsDropped;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
        _maintenanceLoop = MaintenanceLoopAsync(_lifetime.Token);
    }

    public IReadOnlyList<MeshSession> Sessions
    {
        get
        {
            lock (_gate) return _sessions.Values.Where(session => !session.Closed).ToArray();
        }
    }

    /// <summary>A live session whose packets go straight to <paramref name="endpoint"/>, if any.</summary>
    public MeshSession? SessionAt(IPEndPoint endpoint)
    {
        var normalized = MeshAddresses.Normalize(endpoint);
        lock (_gate)
            return _sessions.Values.FirstOrDefault(session => !session.Closed && session.Route.IsDirect && session.Route.Endpoint!.Equals(normalized));
    }

    /// <summary>The live session with a node, if any.</summary>
    public MeshSession? SessionWith(NodeId node)
    {
        lock (_gate) return _byNode.TryGetValue(node, out var session) && !session.Closed ? session : null;
    }

    // ------------------------------------------------------------------ connecting

    /// <summary>
    /// A session with whoever answers on <paramref name="route"/>: an existing one when there is,
    /// a new handshake otherwise. Null when nothing answered in time. The caller checks who
    /// answered (<see cref="MeshSession.RemoteId"/>) when it matters.
    /// </summary>
    public async Task<MeshSession?> ConnectAsync(MeshRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (route.IsDirect && !IsSendable(route.Endpoint!)) return null;

        PendingHandshake pending;
        lock (_gate)
        {
            var existing = _sessions.Values.FirstOrDefault(session =>
                !session.Closed && session.Route.Equals(route) &&
                Environment.TickCount64 - session.EstablishedAt < _options.SessionLifetime.TotalMilliseconds);
            if (existing is not null) return existing;

            if (!_pendingByRoute.TryGetValue(route, out pending!))
            {
                var index = NewIndexUnsynchronized();
                var handshake = NoiseHandshake.Initiator(_nodeKey, MeshPackets.Prologue);
                var payload = new byte[MeshPackets.InitPayloadLength];
                payload[0] = MeshPackets.ProtocolVersion;
                var message = handshake.WriteMessage1(payload);
                pending = new PendingHandshake(index, route, handshake, message);
                _pending[index] = pending;
                _pendingByRoute[route] = pending;
                pending.SentAt = Environment.TickCount64;
                pending.Sends = 1;
                SendPacket(pending.BuildInit(), route);
            }
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// Sends a request and waits for its reply, retrying once. Null on timeout or when the session
    /// closes. Requests on this network are all safe to repeat, which is what makes the retry safe.
    /// </summary>
    public async Task<byte[]?> RequestAsync(
        MeshSession session,
        byte op,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if ((op & MeshMessage.ResponseBit) != 0) throw new ArgumentException("Code d’opération réservé aux réponses.", nameof(op));
        if (session.Closed) return null;

        var requestId = NextRequestId();
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = (session.LocalIndex, requestId);
        _requests[key] = completion;
        var wait = timeout ?? _options.RequestTimeout;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (session.Closed) return null;
                if (attempt > 0 && !session.Confirmed && session.PendingFinish is { } finish) SendPacket(finish, session.Route);
                SendMessage(session, op, requestId, body.Span);
                try
                {
                    return await completion.Task.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
            return null;
        }
        finally
        {
            _requests.TryRemove(key, out _);
        }
    }

    /// <summary>A message that expects no reply.</summary>
    public void Notify(MeshSession session, byte op, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Closed) return;
        SendMessage(session, op, 0, body);
    }

    /// <summary>A datagram outside any session: probes and punches.</summary>
    public void SendRaw(byte[] datagram, IPEndPoint destination)
    {
        if (IsSendable(destination)) _socket.Send(datagram, destination);
    }

    /// <summary>Feeds a packet that arrived inside a relay, as if it had come from <paramref name="route"/>.</summary>
    public void ProcessRelayed(ReadOnlySpan<byte> packet, MeshRoute route)
    {
        if (route.IsDirect) throw new ArgumentException("Route relayée attendue.", nameof(route));
        Process(packet, route);
    }

    public void Close(MeshSession session) => CloseSession(session, notifyPeer: true);

    // ------------------------------------------------------------------ receiving

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            MeshDatagram datagram;
            try
            {
                datagram = await _socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                Process(datagram.Data, MeshRoute.Direct(datagram.From));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A packet that trips a bug costs that packet. The loop is what keeps the node alive.
                Interlocked.Increment(ref _packetsDropped);
            }
        }
    }

    private void Process(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length < 2 || packet[0] != MeshPackets.Magic || packet.Length > MeshPackets.MaximumDatagram + 64)
        {
            Interlocked.Increment(ref _packetsDropped);
            return;
        }

        switch ((MeshPacketType)packet[1])
        {
            case MeshPacketType.HandshakeInit:
                OnInit(packet, from);
                break;
            case MeshPacketType.HandshakeResponse:
                OnResponse(packet, from);
                break;
            case MeshPacketType.HandshakeFinish:
                OnFinish(packet, from);
                break;
            case MeshPacketType.Transport:
                OnTransport(packet, from);
                break;
            case MeshPacketType.Cookie:
                OnCookie(packet, from);
                break;
            case MeshPacketType.Probe when from.IsDirect && packet.Length == 2 + MeshPackets.ProbeNonceLength:
                ProbeReceived?.Invoke(packet[2..].ToArray(), from.Endpoint!);
                break;
            case MeshPacketType.Punch:
                break;
            default:
                Interlocked.Increment(ref _packetsDropped);
                break;
        }
    }

    private void OnInit(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length != MeshPackets.InitLength) return;
        var senderIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[2..]);
        var cookie = packet.Slice(6, MeshPackets.CookieLength);
        var message = packet[(6 + MeshPackets.CookieLength)..];

        lock (_gate)
        {
            if (!AllowHandshake(from)) return;

            // Under load, prove you receive at that address before we spend an agreement on you.
            if (UnderLoad() && from.IsDirect && !CookieMatches(cookie, from.Endpoint!))
            {
                var reply = new byte[2 + 4 + MeshPackets.CookieLength];
                reply[0] = MeshPackets.Magic;
                reply[1] = (byte)MeshPacketType.Cookie;
                BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(2), senderIndex);
                CookieFor(from.Endpoint!, _cookieSecret).CopyTo(reply, 6);
                SendPacket(reply, from);
                return;
            }

            if (_halfOpen.Count >= _options.MaximumHalfOpen)
            {
                var oldest = _halfOpen.MinBy(entry => entry.Value.CreatedAt);
                oldest.Value.Handshake.Dispose();
                _halfOpen.Remove(oldest.Key);
            }
        }

        var handshake = NoiseHandshake.Responder(_nodeKey, MeshPackets.Prologue);
        var payload = handshake.ReadMessage1(message);
        if (payload is null || payload.Length != MeshPackets.InitPayloadLength || payload[0] != MeshPackets.ProtocolVersion)
        {
            handshake.Dispose();
            return;
        }

        uint localIndex;
        lock (_gate) localIndex = NewIndexUnsynchronized();
        var response = handshake.WriteMessage2(BuildPayload(from.IsDirect ? from.Endpoint : null));
        var packetOut = new byte[2 + 4 + 4 + response.Length];
        packetOut[0] = MeshPackets.Magic;
        packetOut[1] = (byte)MeshPacketType.HandshakeResponse;
        BinaryPrimitives.WriteUInt32BigEndian(packetOut.AsSpan(2), localIndex);
        BinaryPrimitives.WriteUInt32BigEndian(packetOut.AsSpan(6), senderIndex);
        response.CopyTo(packetOut, 10);

        // By construction a reply is never larger than the request; checked anyway, because this
        // is the property that keeps the network useless as an amplifier.
        if (packetOut.Length > packet.Length)
        {
            handshake.Dispose();
            return;
        }

        lock (_gate) _halfOpen[localIndex] = new HalfOpen(handshake, from, senderIndex, Environment.TickCount64);
        SendPacket(packetOut, from);
    }

    private void OnResponse(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length < 10 + MeshCrypto.PublicKeyLength * 2) return;
        var responderIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[2..]);
        var ourIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[6..]);

        PendingHandshake? pending;
        lock (_gate)
        {
            if (!_pending.TryGetValue(ourIndex, out pending) || !pending.Route.Equals(from)) return;
            _pending.Remove(ourIndex);
            _pendingByRoute.Remove(pending.Route);
        }

        var handshake = pending.Handshake;
        var payload = handshake.ReadMessage2(packet[10..]);
        var parsed = payload is null ? null : ParsePayload(payload, handshake.RemoteStatic);
        if (parsed is null || handshake.RemoteStatic is null)
        {
            handshake.Dispose();
            pending.Completion.TrySetResult(null);
            return;
        }

        var finish = handshake.WriteMessage3(BuildPayload(from.IsDirect ? from.Endpoint : null));
        var finishPacket = new byte[2 + 4 + finish.Length];
        finishPacket[0] = MeshPackets.Magic;
        finishPacket[1] = (byte)MeshPacketType.HandshakeFinish;
        BinaryPrimitives.WriteUInt32BigEndian(finishPacket.AsSpan(2), responderIndex);
        finish.CopyTo(finishPacket, 6);

        var (send, receive) = handshake.Split();
        var session = new MeshSession(
            pending.LocalIndex, responderIndex, pending.Route, isInitiator: true, send, receive,
            handshake.RemoteStatic, parsed.Nonce, parsed.Flags, parsed.Advertised, parsed.Observed,
            handshake.HandshakeHash)
        {
            PendingFinish = finishPacket
        };
        handshake.Dispose();

        if (!Register(session))
        {
            pending.Completion.TrySetResult(null);
            return;
        }

        SendPacket(finishPacket, session.Route);
        Interlocked.Increment(ref _handshakesCompleted);
        pending.Completion.TrySetResult(session);
        SessionEstablished?.Invoke(session);
    }

    private void OnFinish(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length < 6 + MeshCrypto.PublicKeyLength + MeshCrypto.TagLength * 2) return;
        var ourIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[2..]);

        HalfOpen? state;
        lock (_gate)
        {
            if (_sessions.TryGetValue(ourIndex, out var established))
            {
                // Our confirmation was lost and they sent the last message again: confirm again.
                if (established.Route.Equals(from)) SendFrame(established, new[] { (byte)MeshFrame.Keepalive, (byte)0 });
                return;
            }
            if (!_halfOpen.TryGetValue(ourIndex, out state) || !state.Route.Equals(from)) return;
            _halfOpen.Remove(ourIndex);
        }

        var handshake = state.Handshake;
        var payload = handshake.ReadMessage3(packet[6..]);
        var parsed = payload is null ? null : ParsePayload(payload, handshake.RemoteStatic);
        if (parsed is null || handshake.RemoteStatic is null)
        {
            handshake.Dispose();
            return;
        }

        var (send, receive) = handshake.Split();
        var session = new MeshSession(
            ourIndex, state.RemoteIndex, state.Route, isInitiator: false, send, receive,
            handshake.RemoteStatic, parsed.Nonce, parsed.Flags, parsed.Advertised, parsed.Observed,
            handshake.HandshakeHash);
        handshake.Dispose();

        if (!Register(session)) return;
        SendFrame(session, new[] { (byte)MeshFrame.Keepalive, (byte)0 });
        Interlocked.Increment(ref _handshakesCompleted);
        SessionEstablished?.Invoke(session);
    }

    private void OnCookie(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length != 2 + 4 + MeshPackets.CookieLength) return;
        var ourIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[2..]);
        lock (_gate)
        {
            if (!_pending.TryGetValue(ourIndex, out var pending) || !pending.Route.Equals(from) || pending.Cookie is not null) return;
            pending.Cookie = packet.Slice(6, MeshPackets.CookieLength).ToArray();
            pending.SentAt = Environment.TickCount64;
            SendPacket(pending.BuildInit(), from);
        }
    }

    private void OnTransport(ReadOnlySpan<byte> packet, MeshRoute from)
    {
        if (packet.Length < MeshPackets.TransportHeaderLength + MeshCrypto.TagLength) return;
        var ourIndex = BinaryPrimitives.ReadUInt32BigEndian(packet[2..]);

        MeshSession? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(ourIndex, out session) || session.Closed) return;
        }

        byte[]? frame;
        bool newest;
        lock (session)
        {
            frame = session.Decrypt(packet, out var counter);
            if (frame is null)
            {
                Interlocked.Increment(ref _packetsDropped);
                return;
            }
            newest = session.IsNewest(counter);
        }

        // Only an authenticated packet can move a session, and only the newest one: a stale or
        // replayed datagram from an old address cannot drag it back. A relayed session that hears
        // from a direct address has just been hole-punched; a direct one never moves to a relay.
        if (!session.Route.Equals(from) && newest && from.IsDirect) session.Route = from;

        if (session.IsInitiator && !session.Confirmed)
        {
            session.Confirmed = true;
            session.PendingFinish = null;
        }

        HandleFrame(session, frame);
    }

    private void HandleFrame(MeshSession session, byte[] frame)
    {
        if (frame.Length == 0) return;
        switch ((MeshFrame)frame[0])
        {
            case MeshFrame.Keepalive:
                // A ping asks for an answer: that is how a one-sided keepalive still detects a dead peer.
                if (frame.Length == 2 && frame[1] == 1) SendFrame(session, new[] { (byte)MeshFrame.Keepalive, (byte)0 });
                break;
            case MeshFrame.Close:
                CloseSession(session, notifyPeer: false);
                break;
            case MeshFrame.Nack:
                OnNack(session, frame);
                break;
            case MeshFrame.Fragment:
                OnFragment(session, frame);
                break;
        }
    }

    private void OnFragment(MeshSession session, byte[] frame)
    {
        if (frame.Length < 7) return;
        var messageId = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1));
        int index = frame[5];
        int count = frame[6];
        var data = frame.AsSpan(7);
        if (count == 0 || index >= count || data.Length > MeshPackets.FragmentPayload) return;
        if ((long)count * MeshPackets.FragmentPayload > _options.MaximumMessageBytes + MeshPackets.FragmentPayload) return;

        byte[]? complete = null;
        lock (session)
        {
            if (session.RecentlyCompleted.Contains(messageId)) return;

            if (count == 1)
            {
                complete = data.ToArray();
                Remember(session, messageId);
            }
            else
            {
                if (!session.Incoming.TryGetValue(messageId, out var assembly))
                {
                    if (session.Incoming.Count >= _options.MaximumReassembliesPerSession)
                    {
                        var oldest = session.Incoming.MinBy(entry => entry.Value.CreatedAt);
                        Discard(session, oldest.Key, oldest.Value);
                    }
                    assembly = new Reassembly(count);
                    session.Incoming[messageId] = assembly;
                }

                if (assembly.Count != count) return;
                assembly.LastFragmentAt = Environment.TickCount64;
                if (assembly.Chunks[index] is null)
                {
                    // Over budget, the fragment is dropped: the sender asks again, or gives up.
                    if (session.BufferedBytes + data.Length > _options.MaximumBufferedPerSession ||
                        Interlocked.Read(ref _bufferedBytes) + data.Length > _options.MaximumBufferedBytes)
                        return;
                    assembly.Chunks[index] = data.ToArray();
                    assembly.Received++;
                    assembly.Bytes += data.Length;
                    session.BufferedBytes += data.Length;
                    Interlocked.Add(ref _bufferedBytes, data.Length);
                }

                if (assembly.Bytes > _options.MaximumMessageBytes)
                {
                    Discard(session, messageId, assembly);
                    return;
                }

                if (assembly.Received == assembly.Count)
                {
                    complete = new byte[assembly.Bytes];
                    var offset = 0;
                    foreach (var chunk in assembly.Chunks)
                    {
                        chunk!.CopyTo(complete, offset);
                        offset += chunk.Length;
                    }
                    Discard(session, messageId, assembly);
                    Remember(session, messageId);
                }
            }
        }

        if (complete is not null) Deliver(session, complete);
    }

    /// <summary>Forgets a half-arrived message and gives its bytes back to the budgets. Called under the session's lock.</summary>
    private void Discard(MeshSession session, uint messageId, Reassembly assembly)
    {
        if (!session.Incoming.Remove(messageId)) return;
        session.BufferedBytes -= assembly.Bytes;
        Interlocked.Add(ref _bufferedBytes, -assembly.Bytes);
    }

    private static void Remember(MeshSession session, uint messageId)
    {
        session.RecentlyCompleted.Enqueue(messageId);
        while (session.RecentlyCompleted.Count > 128) session.RecentlyCompleted.Dequeue();
    }

    private void OnNack(MeshSession session, byte[] frame)
    {
        if (frame.Length < 6) return;
        var messageId = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1));
        int count = frame[5];
        if (frame.Length != 6 + count) return;

        byte[][]? frames;
        lock (session) frames = session.RecentlySent.FirstOrDefault(entry => entry.MessageId == messageId).Frames;
        if (frames is null) return;

        foreach (var index in frame.AsSpan(6).ToArray().Distinct())
            if (index < frames.Length && session.Encrypt(frames[index]) is { } packet) SendPacket(packet, session.Route);
    }

    private void Deliver(MeshSession session, byte[] message)
    {
        if (message.Length < 5) return;
        var op = message[0];
        var requestId = BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(1));
        var body = message.AsSpan(5).ToArray();
        var parsed = new MeshMessage(op, requestId, body);

        if (parsed.IsResponse)
        {
            if (_requests.TryGetValue((session.LocalIndex, requestId), out var completion)) completion.TrySetResult(body);
            return;
        }

        if (requestId == 0)
        {
            NotificationReceived?.Invoke(session, parsed);
            return;
        }

        if (RequestHandler is not { } handler) return;
        if (!_requestSlots.Wait(0)) return;   // saturated: the requester retries or gives up
        _ = AnswerAsync(handler, session, parsed);
    }

    private async Task AnswerAsync(Func<MeshSession, MeshMessage, CancellationToken, Task<byte[]?>> handler, MeshSession session, MeshMessage request)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime?.Token ?? CancellationToken.None);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var reply = await handler(session, request, timeout.Token).ConfigureAwait(false);
            if (reply is not null && !session.Closed)
                SendMessage(session, (byte)(request.Op | MeshMessage.ResponseBit), request.RequestId, reply);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A handler that fails sends nothing; the requester's timeout covers it.
        }
        finally
        {
            _requestSlots.Release();
        }
    }

    // ------------------------------------------------------------------ sending

    private void SendMessage(MeshSession session, byte op, uint requestId, ReadOnlySpan<byte> body)
    {
        var length = 5 + body.Length;
        if (length > _options.MaximumMessageBytes) throw new ArgumentException("Message trop long pour le réseau.", nameof(body));

        var message = new byte[length];
        message[0] = op;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(1), requestId);
        body.CopyTo(message.AsSpan(5));

        var count = Math.Max(1, (length + MeshPackets.FragmentPayload - 1) / MeshPackets.FragmentPayload);
        var frames = new byte[count][];
        uint messageId;
        lock (session) messageId = session.NextMessageId++;

        for (var index = 0; index < count; index++)
        {
            var offset = index * MeshPackets.FragmentPayload;
            var take = Math.Min(MeshPackets.FragmentPayload, length - offset);
            var frame = new byte[7 + take];
            frame[0] = (byte)MeshFrame.Fragment;
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), messageId);
            frame[5] = (byte)index;
            frame[6] = (byte)count;
            message.AsSpan(offset, take).CopyTo(frame.AsSpan(7));
            frames[index] = frame;
        }

        if (count > 1)
        {
            lock (session)
            {
                session.RecentlySent.Enqueue((messageId, Environment.TickCount64, frames));
                while (session.RecentlySent.Count > 16) session.RecentlySent.Dequeue();
            }
        }

        foreach (var frame in frames)
            if (session.Encrypt(frame) is { } packet) SendPacket(packet, session.Route);
    }

    private void SendFrame(MeshSession session, byte[] frame)
    {
        if (session.Encrypt(frame) is { } packet) SendPacket(packet, session.Route);
    }

    private void SendPacket(byte[] packet, MeshRoute route)
    {
        if (route.IsDirect) _socket.Send(packet, route.Endpoint!);
        else RelaySender?.Invoke(route, packet);
    }

    // ------------------------------------------------------------------ housekeeping

    private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                try
                {
                    Maintain();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Maintain()
    {
        var now = Environment.TickCount64;
        var expired = new List<PendingHandshake>();
        var toClose = new List<MeshSession>();
        var toPing = new List<MeshSession>();
        var toNack = new List<(MeshSession, uint, byte[])>();

        lock (_gate)
        {
            foreach (var pending in _pending.Values)
            {
                var age = now - pending.StartedAt;
                if (age > _options.HandshakeTimeout.TotalMilliseconds)
                {
                    expired.Add(pending);
                    continue;
                }
                // Resend at roughly 0.4 s, 1 s and 2 s: a lost datagram is the common case on the Internet.
                var due = pending.Sends switch { 1 => 400, 2 => 1000, 3 => 2000, _ => long.MaxValue };
                if (now - pending.StartedAt >= due)
                {
                    pending.Sends++;
                    pending.SentAt = now;
                    SendPacket(pending.BuildInit(), pending.Route);
                }
            }
            foreach (var pending in expired)
            {
                _pending.Remove(pending.LocalIndex);
                _pendingByRoute.Remove(pending.Route);
            }

            foreach (var (index, state) in _halfOpen.Where(entry => now - entry.Value.CreatedAt > 5000).ToArray())
            {
                state.Handshake.Dispose();
                _halfOpen.Remove(index);
            }

            foreach (var session in _sessions.Values)
            {
                if (session.Closed) continue;
                var idle = now - session.LastReceivedAt;
                if (session.Pinned)
                {
                    if (idle > _options.DeadAfter.TotalMilliseconds) toClose.Add(session);
                    else if (now - session.LastSentAt > _options.KeepaliveInterval.TotalMilliseconds ||
                             idle > _options.KeepaliveInterval.TotalMilliseconds * 1.5) toPing.Add(session);
                }
                else if (idle > _options.IdleTimeout.TotalMilliseconds ||
                         now - session.EstablishedAt > _options.SessionLifetime.TotalMilliseconds + _options.IdleTimeout.TotalMilliseconds)
                {
                    toClose.Add(session);
                }

                lock (session)
                {
                    foreach (var (messageId, assembly) in session.Incoming.ToArray())
                    {
                        if (now - assembly.CreatedAt > 8000 || assembly.NacksSent >= 4)
                        {
                            if (now - assembly.LastFragmentAt > 1000) Discard(session, messageId, assembly);
                            continue;
                        }
                        if (now - assembly.LastFragmentAt < 250) continue;
                        var missing = Enumerable.Range(0, assembly.Count).Where(i => assembly.Chunks[i] is null).Take(200).Select(i => (byte)i).ToArray();
                        assembly.NacksSent++;
                        assembly.LastFragmentAt = now;
                        toNack.Add((session, messageId, missing));
                    }
                    while (session.RecentlySent.Count > 0 && now - session.RecentlySent.Peek().At > 8000) session.RecentlySent.Dequeue();
                }
            }

            if (now - _cookieRotatedAt > 120_000)
            {
                _previousCookieSecret = _cookieSecret;
                _cookieSecret = RandomNumberGenerator.GetBytes(32);
                _cookieRotatedAt = now;
            }

            if (_handshakeBuckets.Count > 4096) _handshakeBuckets.Clear();
            else
                foreach (var stale in _handshakeBuckets.Where(entry => now - entry.Value.UpdatedAt > 60_000).Select(entry => entry.Key).ToArray())
                    _handshakeBuckets.Remove(stale);
        }

        foreach (var pending in expired)
        {
            pending.Handshake.Dispose();
            pending.Completion.TrySetResult(null);
        }
        foreach (var session in toClose) CloseSession(session, notifyPeer: !session.Pinned);
        foreach (var session in toPing) SendFrame(session, new[] { (byte)MeshFrame.Keepalive, (byte)1 });
        foreach (var (session, messageId, missing) in toNack)
        {
            var frame = new byte[6 + missing.Length];
            frame[0] = (byte)MeshFrame.Nack;
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), messageId);
            frame[5] = (byte)missing.Length;
            missing.CopyTo(frame, 6);
            SendFrame(session, frame);
        }
    }

    private bool Register(MeshSession session)
    {
        List<MeshSession> evicted = new();
        lock (_gate)
        {
            if (session.RemoteId == LocalId)
            {
                session.Dispose();
                return false;
            }

            if (session.Route.IsDirect)
            {
                var address = session.Route.Endpoint!.Address;
                var fromAddress = _sessions.Values.Count(other => !other.Closed && other.Route.IsDirect && other.Route.Endpoint!.Address.Equals(address));
                if (fromAddress >= _options.MaximumSessionsPerAddress)
                {
                    session.Dispose();
                    return false;
                }
            }

            var sameNode = _sessions.Values.Where(other => !other.Closed && other.RemoteId == session.RemoteId).OrderBy(other => other.LastReceivedAt).ToList();
            while (sameNode.Count >= _options.MaximumSessionsPerNode)
            {
                evicted.Add(sameNode[0]);
                sameNode.RemoveAt(0);
            }

            if (_sessions.Count - evicted.Count >= _options.MaximumSessions)
            {
                var victim = _sessions.Values.Where(other => !other.Pinned && !evicted.Contains(other)).MinBy(other => other.LastReceivedAt);
                if (victim is null)
                {
                    session.Dispose();
                    return false;
                }
                evicted.Add(victim);
            }

            _sessions[session.LocalIndex] = session;
            _byNode[session.RemoteId] = session;
        }

        foreach (var victim in evicted) CloseSession(victim, notifyPeer: true);
        return true;
    }

    private void CloseSession(MeshSession session, bool notifyPeer)
    {
        lock (_gate)
        {
            if (!_sessions.Remove(session.LocalIndex)) return;
            if (_byNode.TryGetValue(session.RemoteId, out var current) && ReferenceEquals(current, session))
            {
                _byNode.Remove(session.RemoteId);
                var replacement = _sessions.Values.Where(other => other.RemoteId == session.RemoteId && !other.Closed).MaxBy(other => other.LastReceivedAt);
                if (replacement is not null) _byNode[session.RemoteId] = replacement;
            }
        }

        if (notifyPeer)
        {
            SendFrame(session, new[] { (byte)MeshFrame.Close });
        }

        foreach (var key in _requests.Keys.Where(key => key.Session == session.LocalIndex).ToArray())
            if (_requests.TryRemove(key, out var completion)) completion.TrySetResult(null);

        lock (session)
        {
            foreach (var (messageId, assembly) in session.Incoming.ToArray()) Discard(session, messageId, assembly);
            session.Dispose();
        }
        SessionClosed?.Invoke(session);
    }

    // ------------------------------------------------------------------ handshake payloads

    private sealed record HandshakePayload(ulong Nonce, MeshNodeFlags Flags, IPEndPoint? Observed, IReadOnlyList<IPEndPoint> Advertised);

    /// <summary>Our proof of work, what we offer, how the other side looks from here, and where we can be reached.</summary>
    private byte[] BuildPayload(IPEndPoint? observed)
    {
        var writer = new MeshWriter(96)
            .U8(MeshPackets.ProtocolVersion)
            .U64(_nonce)
            .U8((byte)_flags());
        writer.OptionalEndpoint(observed);

        var advertised = _advertised().Take(MeshPackets.MaximumAdvertised).ToArray();
        writer.U8((byte)advertised.Length);
        foreach (var endpoint in advertised) writer.Endpoint(endpoint);
        return writer.ToArray();
    }

    private HandshakePayload? ParsePayload(byte[] payload, byte[]? remoteStatic)
    {
        if (remoteStatic is null) return null;
        var reader = new MeshReader(payload);
        var version = reader.U8();
        var nonce = reader.U64();
        var flags = (MeshNodeFlags)(reader.U8() & 0x03);

        var observed = reader.OptionalEndpoint();

        int count = reader.U8();
        if (count > MeshPackets.MaximumAdvertised) return null;
        var advertised = new List<IPEndPoint>(count);
        for (var index = 0; index < count; index++)
        {
            var endpoint = reader.Endpoint();
            if (endpoint is not null) advertised.Add(endpoint);
        }

        if (!reader.Done || version != MeshPackets.ProtocolVersion) return null;

        // The id is only worth something because it cost something: no proof, no session.
        if (!NodeProof.IsValid(remoteStatic, nonce, _options.ProofDifficulty)) return null;
        return new HandshakePayload(nonce, flags, observed, advertised);
    }

    // ------------------------------------------------------------------ admission

    private bool AllowHandshake(MeshRoute from)
    {
        var now = Environment.TickCount64;
        var second = now / 1000;
        if (second != _handshakeSecond)
        {
            _handshakeSecond = second;
            _handshakesThisSecond = 0;
        }
        _handshakesThisSecond++;

        var key = from.IsDirect ? from.Endpoint!.Address.ToString() : "relay:" + from;
        if (!_handshakeBuckets.TryGetValue(key, out var bucket))
        {
            bucket = new TokenBucket(_options.HandshakesPerSecondPerAddress * 2, now);
            _handshakeBuckets[key] = bucket;
        }
        return bucket.TryTake(now, _options.HandshakesPerSecondPerAddress, _options.HandshakesPerSecondPerAddress * 2);
    }

    private bool UnderLoad() => _handshakesThisSecond > _options.HandshakesPerSecondBeforeCookies;

    private bool CookieMatches(ReadOnlySpan<byte> cookie, IPEndPoint from) =>
        CryptographicOperations.FixedTimeEquals(cookie, CookieFor(from, _cookieSecret)) ||
        CryptographicOperations.FixedTimeEquals(cookie, CookieFor(from, _previousCookieSecret));

    private static byte[] CookieFor(IPEndPoint endpoint, byte[] secret)
    {
        var writer = new MeshWriter(32).Endpoint(endpoint);
        return HMACSHA256.HashData(secret, writer.Span).AsSpan(0, MeshPackets.CookieLength).ToArray();
    }

    private static bool IsSendable(IPEndPoint endpoint) =>
        endpoint.Port is > 0 and <= 65535 &&
        !endpoint.Address.Equals(IPAddress.Any) && !endpoint.Address.Equals(IPAddress.IPv6Any) &&
        !endpoint.Address.Equals(IPAddress.Broadcast) &&
        !(endpoint.Address.IsIPv6Multicast) &&
        !(endpoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && endpoint.Address.GetAddressBytes()[0] >= 224);

    private uint NewIndexUnsynchronized()
    {
        while (true)
        {
            var index = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
            if (index != 0 && !_sessions.ContainsKey(index) && !_pending.ContainsKey(index) && !_halfOpen.ContainsKey(index)) return index;
        }
    }

    private uint NextRequestId()
    {
        while (true)
        {
            var id = (uint)Interlocked.Increment(ref _nextRequestId);
            if (id != 0) return id;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime?.Cancel();

        foreach (var session in Sessions) CloseSession(session, notifyPeer: true);
        _socket.Dispose();

        foreach (var loop in new[] { _receiveLoop, _maintenanceLoop })
        {
            if (loop is null) continue;
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_gate)
        {
            foreach (var pending in _pending.Values)
            {
                pending.Handshake.Dispose();
                pending.Completion.TrySetResult(null);
            }
            _pending.Clear();
            _pendingByRoute.Clear();
            foreach (var state in _halfOpen.Values) state.Handshake.Dispose();
            _halfOpen.Clear();
        }

        _lifetime?.Dispose();
    }

    // ------------------------------------------------------------------ state

    private sealed class PendingHandshake
    {
        public PendingHandshake(uint localIndex, MeshRoute route, NoiseHandshake handshake, byte[] message)
        {
            LocalIndex = localIndex;
            Route = route;
            Handshake = handshake;
            Message = message;
            StartedAt = Environment.TickCount64;
        }

        public uint LocalIndex { get; }
        public MeshRoute Route { get; }
        public NoiseHandshake Handshake { get; }
        public byte[] Message { get; }
        public byte[]? Cookie { get; set; }
        public long StartedAt { get; }
        public long SentAt { get; set; }
        public int Sends { get; set; }
        public TaskCompletionSource<MeshSession?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[] BuildInit()
        {
            var packet = new byte[MeshPackets.InitLength];
            packet[0] = MeshPackets.Magic;
            packet[1] = (byte)MeshPacketType.HandshakeInit;
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(2), LocalIndex);
            Cookie?.CopyTo(packet, 6);
            Message.CopyTo(packet, 6 + MeshPackets.CookieLength);
            return packet;
        }
    }

    private sealed record HalfOpen(NoiseHandshake Handshake, MeshRoute Route, uint RemoteIndex, long CreatedAt);

    private sealed class TokenBucket
    {
        private double _tokens;

        public TokenBucket(double capacity, long now)
        {
            _tokens = capacity;
            UpdatedAt = now;
        }

        public long UpdatedAt { get; private set; }

        public bool TryTake(long now, double perSecond, double capacity)
        {
            _tokens = Math.Min(capacity, _tokens + (now - UpdatedAt) / 1000.0 * perSecond);
            UpdatedAt = now;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }
}
