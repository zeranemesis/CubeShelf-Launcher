using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>A relay that holds a reservation for us, and the token that names it there.</summary>
public sealed record MeshRelayContact(IPEndPoint Relay, byte[] Token)
{
    public const int TokenLength = 16;
}

/// <summary>
/// How to reach a node: addresses where it listens itself, and relays that will put a caller in
/// touch with it when it cannot listen. This is what a CubeShelf tells its friends -- inside its
/// sealed document, never in the clear.
/// </summary>
public sealed record MeshPeerAddress(IReadOnlyList<IPEndPoint> Direct, IReadOnlyList<MeshRelayContact> Relays)
{
    public const int MaximumDirect = 4;
    public const int MaximumRelays = 3;

    public static readonly MeshPeerAddress None = new(Array.Empty<IPEndPoint>(), Array.Empty<MeshRelayContact>());

    public bool IsEmpty => Direct.Count == 0 && Relays.Count == 0;

    public byte[] Encode()
    {
        var writer = new MeshWriter(128).U8(1).U8((byte)Math.Min(Direct.Count, MaximumDirect));
        foreach (var endpoint in Direct.Take(MaximumDirect)) writer.Endpoint(endpoint);
        writer.U8((byte)Math.Min(Relays.Count, MaximumRelays));
        foreach (var relay in Relays.Take(MaximumRelays)) writer.Endpoint(relay.Relay).Fixed(relay.Token);
        return writer.ToArray();
    }

    public string ToBase64() => Convert.ToBase64String(Encode());

    /// <summary>Null for anything malformed. Addresses outside the public Internet are dropped, not trusted.</summary>
    public static MeshPeerAddress? Decode(ReadOnlySpan<byte> data, Func<IPAddress, bool>? isRoutable = null)
    {
        isRoutable ??= MeshAddresses.IsPublic;
        var reader = new MeshReader(data);
        if (reader.U8() != 1) return null;
        int directCount = reader.U8();
        if (directCount > MaximumDirect) return null;
        var direct = new List<IPEndPoint>();
        for (var index = 0; index < directCount; index++)
            if (reader.Endpoint() is { } endpoint && isRoutable(endpoint.Address)) direct.Add(MeshAddresses.Normalize(endpoint));
        int relayCount = reader.U8();
        if (relayCount > MaximumRelays) return null;
        var relays = new List<MeshRelayContact>();
        for (var index = 0; index < relayCount; index++)
        {
            var endpoint = reader.Endpoint();
            var token = reader.Fixed(MeshRelayContact.TokenLength).ToArray();
            if (endpoint is not null && isRoutable(endpoint.Address)) relays.Add(new MeshRelayContact(MeshAddresses.Normalize(endpoint), token));
        }
        return reader.Done ? new MeshPeerAddress(direct, relays) : null;
    }

    public static MeshPeerAddress? FromBase64(string? text, Func<IPAddress, bool>? isRoutable = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048) return null;
        try
        {
            return Decode(Convert.FromBase64String(text), isRoutable);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// The relay a reachable node offers to those that are not.
///
/// A node behind a router that lets nothing in keeps a session open with a relay, which hands it a
/// token. A friend who knows the token -- from the sealed document -- asks the relay to connect
/// them; the relay tells each side the address it sees the other at, so both can try to punch
/// through their routers to a direct session, and until that works, forwards their packets. What it
/// forwards is the friends' own end-to-end encrypted session: the relay sees ciphertext, sizes and
/// timing, and nothing else.
///
/// Bounded so being a relay cannot be turned against whoever offers it: a fixed number of
/// reservations, a rate of connections per reservation, a byte budget and a packet rate per
/// circuit, and everything tied to sessions that disappear with them.
/// </summary>
public sealed class RelayService
{
    public static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CircuitIdle = TimeSpan.FromMinutes(2);
    private const long CircuitBudgetBytes = 8L * 1024 * 1024;
    private const int MaximumCircuits = 256;
    private const int MaximumRelayedPacket = MeshPackets.MaximumDatagram + 64;

    private readonly MeshTransport _transport;
    private readonly int _maximumReservations;
    private readonly object _gate = new();
    private readonly Dictionary<string, Reservation> _reservations = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, Circuit> _circuits = new();

    public RelayService(MeshTransport transport, int maximumReservations = 64)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _maximumReservations = maximumReservations;
    }

    /// <summary>Only a reachable node relays; set by the node once it knows.</summary>
    public bool Enabled { get; set; }

    public int Reservations
    {
        get
        {
            lock (_gate) return _reservations.Count;
        }
    }

    /// <summary>Reserve: [token or 16 zero bytes] → [status][token][lifetime seconds].</summary>
    public byte[]? HandleReserve(MeshSession session, byte[] body)
    {
        if (body.Length != MeshRelayContact.TokenLength) return null;
        lock (_gate)
        {
            if (!Enabled) return new byte[] { 1 };
            Expire();

            var requested = Convert.ToBase64String(body);
            if (_reservations.TryGetValue(requested, out var existing) && ReferenceEquals(existing.Session, session))
            {
                existing.ExpiresAt = Environment.TickCount64 + (long)ReservationLifetime.TotalMilliseconds;
                return Granted(body);
            }

            // One reservation per session: a node needs one per relay, not many.
            foreach (var (key, reservation) in _reservations.Where(entry => ReferenceEquals(entry.Value.Session, session)).ToArray())
                _reservations.Remove(key);
            if (_reservations.Count >= _maximumReservations) return new byte[] { 2 };

            var token = RandomNumberGenerator.GetBytes(MeshRelayContact.TokenLength);
            _reservations[Convert.ToBase64String(token)] = new Reservation(session)
            {
                ExpiresAt = Environment.TickCount64 + (long)ReservationLifetime.TotalMilliseconds
            };
            session.Pinned = true;
            return Granted(token);
        }

        static byte[] Granted(byte[] token)
        {
            var reply = new byte[1 + MeshRelayContact.TokenLength + 4];
            token.CopyTo(reply, 1);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(1 + MeshRelayContact.TokenLength), (uint)ReservationLifetime.TotalSeconds);
            return reply;
        }
    }

    /// <summary>Connect: [token] → [status][circuit][the target's address as we see it]. The target hears of it at once.</summary>
    public byte[]? HandleConnect(MeshSession caller, byte[] body)
    {
        if (body.Length != MeshRelayContact.TokenLength) return null;
        Reservation? reservation;
        uint circuitId;
        lock (_gate)
        {
            Expire();
            if (!Enabled || !_reservations.TryGetValue(Convert.ToBase64String(body), out reservation) || reservation.Session.Closed)
                return new byte[] { 1 };
            if (ReferenceEquals(reservation.Session, caller)) return new byte[] { 1 };
            if (!reservation.Connects.TryTake(Environment.TickCount64, 10.0 / 60, 10)) return new byte[] { 2 };
            if (_circuits.Count >= MaximumCircuits) return new byte[] { 2 };

            do circuitId = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
            while (circuitId == 0 || _circuits.ContainsKey(circuitId));
            _circuits[circuitId] = new Circuit(caller, reservation.Session);
        }

        // The target learns where the caller is, the caller where the target is: each as this
        // relay sees them, which is what their routers will let back in once each has sent out.
        var incoming = new MeshWriter(32).U32(circuitId).OptionalEndpoint(caller.Route.IsDirect ? caller.Route.Endpoint : null);
        _transport.Notify(reservation.Session, MeshOps.Incoming, incoming.Span);

        return new MeshWriter(32).U8(0).U32(circuitId)
            .OptionalEndpoint(reservation.Session.Route.IsDirect ? reservation.Session.Route.Endpoint : null)
            .ToArray();
    }

    /// <summary>RelayData: [circuit][packet], passed to the other end of the circuit if the sender is one of its ends.</summary>
    public void OnRelayData(MeshSession from, byte[] body)
    {
        if (body.Length < 4 || body.Length > 4 + MaximumRelayedPacket) return;
        var circuitId = BinaryPrimitives.ReadUInt32BigEndian(body);
        MeshSession other;
        lock (_gate)
        {
            if (!_circuits.TryGetValue(circuitId, out var circuit)) return;
            if (ReferenceEquals(circuit.A, from)) other = circuit.B;
            else if (ReferenceEquals(circuit.B, from)) other = circuit.A;
            else return;

            var now = Environment.TickCount64;
            if (circuit.BytesLeft < body.Length || !circuit.Packets.TryTake(now, 200, 400)) return;
            circuit.BytesLeft -= body.Length;
            circuit.LastUsed = now;
        }
        _transport.Notify(other, MeshOps.RelayData, body);
    }

    public void OnSessionClosed(MeshSession session)
    {
        lock (_gate)
        {
            foreach (var (key, _) in _reservations.Where(entry => ReferenceEquals(entry.Value.Session, session)).ToArray()) _reservations.Remove(key);
            foreach (var (id, _) in _circuits.Where(entry => ReferenceEquals(entry.Value.A, session) || ReferenceEquals(entry.Value.B, session)).ToArray()) _circuits.Remove(id);
        }
    }

    private void Expire()
    {
        var now = Environment.TickCount64;
        foreach (var (key, reservation) in _reservations.Where(entry => entry.Value.ExpiresAt < now || entry.Value.Session.Closed).ToArray())
        {
            _reservations.Remove(key);
        }
        foreach (var (id, _) in _circuits.Where(entry => now - entry.Value.LastUsed > CircuitIdle.TotalMilliseconds || entry.Value.A.Closed || entry.Value.B.Closed).ToArray())
            _circuits.Remove(id);
    }

    private sealed class Reservation
    {
        public Reservation(MeshSession session) => Session = session;
        public MeshSession Session { get; }
        public long ExpiresAt { get; set; }
        public RateBucket Connects { get; } = new(10);
    }

    private sealed class Circuit
    {
        public Circuit(MeshSession a, MeshSession b)
        {
            A = a;
            B = b;
            LastUsed = Environment.TickCount64;
        }

        public MeshSession A { get; }
        public MeshSession B { get; }
        public long BytesLeft { get; set; } = CircuitBudgetBytes;
        public long LastUsed { get; set; }
        public RateBucket Packets { get; } = new(400);
    }
}

/// <summary>A token bucket, refilled continuously.</summary>
internal sealed class RateBucket
{
    private double _tokens;
    private long _updatedAt = Environment.TickCount64;

    public RateBucket(double initial) => _tokens = initial;

    public bool TryTake(long now, double perSecond, double capacity)
    {
        _tokens = Math.Min(capacity, _tokens + (now - _updatedAt) / 1000.0 * perSecond);
        _updatedAt = now;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}
