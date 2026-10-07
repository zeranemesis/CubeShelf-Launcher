using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// How packets reach a peer: straight to an address, or wrapped and handed to a relay that
/// forwards them. Either way what travels is the same end-to-end encrypted session; the relay sees
/// ciphertext.
/// </summary>
public sealed class MeshRoute : IEquatable<MeshRoute>
{
    private MeshRoute(IPEndPoint? endpoint, MeshSession? relay, uint circuit)
    {
        Endpoint = endpoint is null ? null : MeshAddresses.Normalize(endpoint);
        Relay = relay;
        Circuit = circuit;
    }

    public IPEndPoint? Endpoint { get; }
    public MeshSession? Relay { get; }
    public uint Circuit { get; }

    public bool IsDirect => Endpoint is not null;

    public static MeshRoute Direct(IPEndPoint endpoint) => new(endpoint ?? throw new ArgumentNullException(nameof(endpoint)), null, 0);

    public static MeshRoute Via(MeshSession relay, uint circuit) => new(null, relay ?? throw new ArgumentNullException(nameof(relay)), circuit);

    public bool Equals(MeshRoute? other) =>
        other is not null &&
        (IsDirect
            ? other.IsDirect && Endpoint!.Equals(other.Endpoint)
            : !other.IsDirect && ReferenceEquals(Relay, other.Relay) && Circuit == other.Circuit);

    public override bool Equals(object? obj) => Equals(obj as MeshRoute);

    public override int GetHashCode() => IsDirect ? Endpoint!.GetHashCode() : HashCode.Combine(Relay!.LocalIndex, Circuit);

    public override string ToString() => IsDirect ? Endpoint!.ToString() : $"relay {Relay!.Route}#{Circuit}";
}

/// <summary>What a node says about itself in the handshake.</summary>
[Flags]
public enum MeshNodeFlags : byte
{
    None = 0,

    /// <summary>Reachable from the Internet: worth keeping in routing tables.</summary>
    Server = 1,

    /// <summary>Accepts relay reservations from nodes that are not reachable.</summary>
    Relay = 2
}

/// <summary>One message, reassembled: an operation code, a request id (0 for none) and its body.</summary>
public sealed record MeshMessage(byte Op, uint RequestId, byte[] Body)
{
    public const byte ResponseBit = 0x80;
    public bool IsResponse => (Op & ResponseBit) != 0;
}

/// <summary>
/// An established, encrypted channel with one other node: the two transport keys from the
/// handshake, a send counter, a replay window, and the reassembly of messages that did not fit in
/// one datagram.
/// </summary>
public sealed class MeshSession
{
    internal const int ReplayWindowBits = 1024;

    private readonly AesGcm _send;
    private readonly AesGcm _receive;
    private readonly object _sendGate = new();
    private ulong _sendCounter;

    private ulong _highestReceived;
    private bool _receivedAny;
    private readonly ulong[] _window = new ulong[ReplayWindowBits / 64];

    internal MeshSession(
        uint localIndex,
        uint remoteIndex,
        MeshRoute route,
        bool isInitiator,
        byte[] sendKey,
        byte[] receiveKey,
        byte[] remotePublicKey,
        ulong remoteNonce,
        MeshNodeFlags remoteFlags,
        IReadOnlyList<IPEndPoint> remoteAdvertised,
        IPEndPoint? observedSelf,
        byte[] handshakeHash)
    {
        LocalIndex = localIndex;
        RemoteIndex = remoteIndex;
        Route = route;
        IsInitiator = isInitiator;
        _send = new AesGcm(sendKey, MeshCrypto.TagLength);
        _receive = new AesGcm(receiveKey, MeshCrypto.TagLength);
        CryptographicOperations.ZeroMemory(sendKey);
        CryptographicOperations.ZeroMemory(receiveKey);
        RemotePublicKey = remotePublicKey;
        RemoteNonce = remoteNonce;
        RemoteId = NodeProof.IdOf(remotePublicKey, remoteNonce);
        RemoteFlags = remoteFlags;
        RemoteAdvertised = remoteAdvertised;
        ObservedSelf = observedSelf;
        HandshakeHash = handshakeHash;
        EstablishedAt = Environment.TickCount64;
        LastReceivedAt = EstablishedAt;
        LastSentAt = EstablishedAt;
        Confirmed = !isInitiator;
    }

    public uint LocalIndex { get; }
    public uint RemoteIndex { get; }

    /// <summary>Where packets go. A relayed session moves to a direct address when one authenticates from there.</summary>
    public MeshRoute Route { get; internal set; }

    public bool IsInitiator { get; }
    public byte[] RemotePublicKey { get; }
    public ulong RemoteNonce { get; }
    public NodeId RemoteId { get; }
    /// <summary>What the peer says it offers: from the handshake, updated when it announces a change.</summary>
    public MeshNodeFlags RemoteFlags { get; internal set; }

    /// <summary>The addresses the peer says it can be reached at. Claims, not facts, until someone connects there.</summary>
    public IReadOnlyList<IPEndPoint> RemoteAdvertised { get; internal set; }

    /// <summary>Our own address as the peer sees it: how we learn what our NAT maps us to, without a STUN server.</summary>
    public IPEndPoint? ObservedSelf { get; }

    /// <summary>The Noise transcript hash: what an identity proof is bound to.</summary>
    public byte[] HandshakeHash { get; }

    public long EstablishedAt { get; }
    public long LastReceivedAt { get; internal set; }
    public long LastSentAt { get; internal set; }

    /// <summary>
    /// The initiator only knows its last handshake message arrived once something comes back
    /// authenticated under the new keys. Until then it keeps that message to send again.
    /// </summary>
    public bool Confirmed { get; internal set; }

    internal byte[]? PendingFinish { get; set; }

    /// <summary>Kept alive and never evicted for idleness: relay reservations and friends.</summary>
    public bool Pinned { get; set; }

    /// <summary>The friend identity (base64) this session has proven to belong to, once it has.</summary>
    public string? FriendKey { get; set; }

    public bool Closed { get; internal set; }

    internal readonly Dictionary<uint, Reassembly> Incoming = new();

    /// <summary>Bytes held in <see cref="Incoming"/>, counted against the session's and the transport's budgets.</summary>
    internal long BufferedBytes;
    internal readonly Queue<(uint MessageId, long At, byte[][] Frames)> RecentlySent = new();
    internal readonly Queue<uint> RecentlyCompleted = new();
    internal uint NextMessageId;

    // ------------------------------------------------------------------ transport packets

    /// <summary>Header (magic, type, receiver index, counter) then ciphertext and tag; the header is the associated data.</summary>
    internal byte[]? Encrypt(ReadOnlySpan<byte> frame)
    {
        var packet = new byte[MeshPackets.TransportHeaderLength + frame.Length + MeshCrypto.TagLength];
        packet[0] = MeshPackets.Magic;
        packet[1] = (byte)MeshPacketType.Transport;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(2), RemoteIndex);
        lock (_sendGate)
        {
            // Closed concurrently by another thread: nothing to send with any more.
            if (Closed) return null;
            var counter = _sendCounter++;
            BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(6), counter);
            _send.Encrypt(
                NoiseHandshake.Nonce(counter),
                frame,
                packet.AsSpan(MeshPackets.TransportHeaderLength, frame.Length),
                packet.AsSpan(MeshPackets.TransportHeaderLength + frame.Length),
                packet.AsSpan(0, MeshPackets.TransportHeaderLength));
        }
        LastSentAt = Environment.TickCount64;
        return packet;
    }

    /// <summary>
    /// Opens a transport packet addressed to this session. The replay window is checked before
    /// decrypting (cheap rejection) and updated only after the tag verified (so a forged packet
    /// cannot move it).
    /// </summary>
    internal byte[]? Decrypt(ReadOnlySpan<byte> packet, out ulong counter)
    {
        counter = 0;
        if (Closed || packet.Length < MeshPackets.TransportHeaderLength + MeshCrypto.TagLength) return null;
        counter = BinaryPrimitives.ReadUInt64BigEndian(packet[6..]);
        if (!WindowAllows(counter)) return null;

        var cipherLength = packet.Length - MeshPackets.TransportHeaderLength - MeshCrypto.TagLength;
        var frame = new byte[cipherLength];
        try
        {
            _receive.Decrypt(
                NoiseHandshake.Nonce(counter),
                packet.Slice(MeshPackets.TransportHeaderLength, cipherLength),
                packet[^MeshCrypto.TagLength..],
                frame,
                packet[..MeshPackets.TransportHeaderLength]);
        }
        catch (CryptographicException)
        {
            return null;
        }

        WindowAccept(counter);
        LastReceivedAt = Environment.TickCount64;
        return frame;
    }

    internal bool IsNewest(ulong counter) => counter >= _highestReceived;

    private bool WindowAllows(ulong counter)
    {
        if (!_receivedAny || counter > _highestReceived) return true;
        var behind = _highestReceived - counter;
        if (behind >= ReplayWindowBits) return false;
        var bit = (int)(counter % ReplayWindowBits);
        return (_window[bit / 64] & (1UL << (bit % 64))) == 0;
    }

    private void WindowAccept(ulong counter)
    {
        if (!_receivedAny || counter > _highestReceived)
        {
            var start = _receivedAny ? _highestReceived + 1 : 0;
            // Clear the bits of every counter skipped over; past a whole window, clear it all.
            if (counter - start >= ReplayWindowBits) Array.Clear(_window);
            else
                for (var skipped = start; skipped < counter; skipped++)
                {
                    var b = (int)(skipped % ReplayWindowBits);
                    _window[b / 64] &= ~(1UL << (b % 64));
                }
            _highestReceived = counter;
            _receivedAny = true;
        }
        var bit = (int)(counter % ReplayWindowBits);
        _window[bit / 64] |= 1UL << (bit % 64);
    }

    internal void Dispose()
    {
        lock (_sendGate)
        {
            if (Closed) return;
            Closed = true;
        }
        _send.Dispose();
        _receive.Dispose();
        Incoming.Clear();
        RecentlySent.Clear();
    }

    public override string ToString() => $"{RemoteId} via {Route}";
}

/// <summary>A message arriving in pieces.</summary>
internal sealed class Reassembly
{
    public Reassembly(int count)
    {
        Count = count;
        Chunks = new byte[]?[count];
        CreatedAt = Environment.TickCount64;
        LastFragmentAt = CreatedAt;
    }

    public int Count { get; }
    public byte[]?[] Chunks { get; }
    public int Received { get; set; }
    public int Bytes { get; set; }
    public long CreatedAt { get; }
    public long LastFragmentAt { get; set; }
    public int NacksSent { get; set; }
}
