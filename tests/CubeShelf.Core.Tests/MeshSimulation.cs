using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using CubeShelf.Core.Social.Mesh;

/// <summary>How a simulated home router treats traffic coming back in (RFC 4787 vocabulary).</summary>
enum SimulatedNatKind
{
    /// <summary>Anyone can reach a mapping once it exists.</summary>
    FullCone,

    /// <summary>Only addresses the inside host has sent to.</summary>
    AddressRestricted,

    /// <summary>Only the exact address and port the inside host has sent to: the common home router.</summary>
    PortRestricted,

    /// <summary>A new mapping per destination, port-restricted: hole punching fails, a relay is needed.</summary>
    Symmetric
}

/// <summary>
/// A whole Internet in one process: public hosts, home routers with their NAT behaviour, loss,
/// latency, duplication. Datagrams are delivered through channels, so every transport keeps its
/// own receive loop exactly as on a real socket.
/// </summary>
sealed class SimulatedNetwork
{
    private readonly object _gate = new();
    private readonly Dictionary<IPEndPoint, SimulatedSocket> _bound = new();
    private readonly Dictionary<IPAddress, SimulatedNat> _nats = new();
    private int _nextPublic = 1;
    private int _nextPrivateNetwork = 1;

    public double Loss { get; set; }
    public double Duplication { get; set; }
    public int LatencyMilliseconds { get; set; }

    /// <summary>Sees every datagram before delivery; returning false drops it.</summary>
    public Func<byte[], IPEndPoint, IPEndPoint, bool>? Filter { get; set; }

    public long Delivered;

    /// <summary>11.0.0.0/8 is ordinary public address space, so the code under test treats these as Internet hosts.</summary>
    public IPAddress NewPublicAddress()
    {
        lock (_gate)
        {
            var n = _nextPublic++;
            return new IPAddress(new byte[] { 11, (byte)(n >> 16), (byte)(n >> 8), (byte)n });
        }
    }

    public SimulatedSocket BindPublic(IPAddress? address = null, int port = 40000)
    {
        address ??= NewPublicAddress();
        var socket = new SimulatedSocket(this, new IPEndPoint(address, port), null);
        lock (_gate) _bound[socket.LocalEndPoint] = socket;
        return socket;
    }

    public SimulatedNat CreateNat(SimulatedNatKind kind)
    {
        int network;
        lock (_gate) network = _nextPrivateNetwork++;
        var nat = new SimulatedNat(kind, NewPublicAddress(), network);
        lock (_gate) _nats[nat.PublicAddress] = nat;
        return nat;
    }

    public SimulatedSocket BindBehind(SimulatedNat nat, int port = 40000)
    {
        var address = new IPAddress(new byte[] { 192, 168, (byte)nat.Network, (byte)(nat.NextHost()) });
        var socket = new SimulatedSocket(this, new IPEndPoint(address, port), nat);
        lock (_gate) _bound[socket.LocalEndPoint] = socket;
        return socket;
    }

    /// <summary>A second socket on the same host: the address a dial-back probe comes from.</summary>
    public SimulatedSocket BindBeside(SimulatedSocket socket)
    {
        var sibling = new SimulatedSocket(this, new IPEndPoint(socket.LocalEndPoint.Address, socket.LocalEndPoint.Port + 1 + Random.Shared.Next(1000)), socket.Nat);
        lock (_gate) _bound[sibling.LocalEndPoint] = sibling;
        return sibling;
    }

    internal void Unbind(SimulatedSocket socket)
    {
        lock (_gate) _bound.Remove(socket.LocalEndPoint);
    }

    internal void Send(SimulatedSocket from, byte[] datagram, IPEndPoint to)
    {
        to = MeshAddresses.Normalize(to);
        IPEndPoint source;
        SimulatedSocket? target;
        lock (_gate)
        {
            source = from.Nat is { } outbound ? outbound.Outgoing(from.LocalEndPoint, to) : from.LocalEndPoint;

            if (_nats.TryGetValue(to.Address, out var inbound))
            {
                var inside = inbound.Incoming(to.Port, source);
                target = inside is null ? null : _bound.GetValueOrDefault(inside);
            }
            else
            {
                target = _bound.GetValueOrDefault(to);
                // Private addresses are only reachable from behind the same router.
                if (target?.Nat is not null && !ReferenceEquals(target.Nat, from.Nat)) target = null;
            }
        }

        if (target is null) return;
        if (Filter is { } filter && !filter(datagram, source, to)) return;
        if (Loss > 0 && Random.Shared.NextDouble() < Loss) return;

        var copies = Duplication > 0 && Random.Shared.NextDouble() < Duplication ? 2 : 1;
        for (var copy = 0; copy < copies; copy++)
        {
            var bytes = (byte[])datagram.Clone();
            if (LatencyMilliseconds <= 0) target.Deliver(new MeshDatagram(bytes, source));
            else
            {
                var delay = LatencyMilliseconds + Random.Shared.Next(LatencyMilliseconds / 2 + 1);
                _ = Task.Delay(delay).ContinueWith(_ => target.Deliver(new MeshDatagram(bytes, source)), TaskScheduler.Default);
            }
        }
        Interlocked.Increment(ref Delivered);
    }
}

sealed class SimulatedNat
{
    private readonly Dictionary<(IPEndPoint Inside, IPEndPoint? Destination), int> _mappings = new();
    private readonly Dictionary<int, (IPEndPoint Inside, HashSet<IPEndPoint> Contacted)> _byPort = new();
    private int _nextPort = 50000;
    private int _nextHost = 2;

    public SimulatedNat(SimulatedNatKind kind, IPAddress publicAddress, int network)
    {
        Kind = kind;
        PublicAddress = publicAddress;
        Network = network;
    }

    public SimulatedNatKind Kind { get; }
    public IPAddress PublicAddress { get; }
    public int Network { get; }

    /// <summary>Ports forwarded by hand or by UPnP: anyone reaches the inside host there.</summary>
    public Dictionary<int, IPEndPoint> Forwarded { get; } = new();

    public int NextHost() => _nextHost++;

    public IPEndPoint Outgoing(IPEndPoint inside, IPEndPoint destination)
    {
        var forwarded = Forwarded.FirstOrDefault(entry => entry.Value.Equals(inside));
        if (forwarded.Value is not null) return new IPEndPoint(PublicAddress, forwarded.Key);

        var key = (inside, Kind == SimulatedNatKind.Symmetric ? destination : null);
        if (!_mappings.TryGetValue(key, out var port))
        {
            port = _nextPort++;
            _mappings[key] = port;
            _byPort[port] = (inside, new HashSet<IPEndPoint>());
        }
        _byPort[port].Contacted.Add(destination);
        return new IPEndPoint(PublicAddress, port);
    }

    public IPEndPoint? Incoming(int port, IPEndPoint source)
    {
        if (Forwarded.TryGetValue(port, out var forwarded)) return forwarded;
        if (!_byPort.TryGetValue(port, out var mapping)) return null;
        var allowed = Kind switch
        {
            SimulatedNatKind.FullCone => true,
            SimulatedNatKind.AddressRestricted => mapping.Contacted.Any(endpoint => endpoint.Address.Equals(source.Address)),
            _ => mapping.Contacted.Contains(source)
        };
        return allowed ? mapping.Inside : null;
    }
}

sealed class SimulatedSocket : IMeshSocket
{
    private readonly SimulatedNetwork _network;
    private readonly Channel<MeshDatagram> _inbox = Channel.CreateUnbounded<MeshDatagram>();
    private bool _disposed;

    public SimulatedSocket(SimulatedNetwork network, IPEndPoint local, SimulatedNat? nat)
    {
        _network = network;
        LocalEndPoint = local;
        Nat = nat;
    }

    public IPEndPoint LocalEndPoint { get; }
    public SimulatedNat? Nat { get; }

    /// <summary>Bytes sent, by destination: what the amplification test counts.</summary>
    public Dictionary<IPEndPoint, long> SentTo { get; } = new();

    public void Send(byte[] datagram, IPEndPoint destination)
    {
        if (_disposed) return;
        lock (SentTo) SentTo[destination] = SentTo.GetValueOrDefault(destination) + datagram.Length;
        _network.Send(this, datagram, destination);
    }

    public ValueTask<MeshDatagram> ReceiveAsync(CancellationToken cancellationToken) => _inbox.Reader.ReadAsync(cancellationToken);

    internal void Deliver(MeshDatagram datagram)
    {
        if (!_disposed) _inbox.Writer.TryWrite(datagram);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _network.Unbind(this);
        _inbox.Writer.TryComplete();
    }
}

static class MeshKit
{
    /// <summary>Low enough to make hundreds of node ids in a second; production uses <see cref="NodeProof.Difficulty"/>.</summary>
    public const int Difficulty = 8;

    public static MeshTransportOptions Options(MeshTransportOptions? options = null) =>
        (options ?? new MeshTransportOptions()) with { ProofDifficulty = Difficulty };

    public static (ECDiffieHellman Key, ulong Nonce) NodeKey()
    {
        var key = MeshCrypto.NewAgreementKey();
        return (key, NodeProof.Solve(MeshCrypto.PublicKeyOf(key), Difficulty));
    }

    public static MeshTransport Transport(IMeshSocket socket, MeshNodeFlags flags = MeshNodeFlags.None, MeshTransportOptions? options = null)
    {
        var (key, nonce) = NodeKey();
        var transport = new MeshTransport(socket, key, nonce, () => flags, null, Options(options));
        transport.Start();
        return transport;
    }

    public static T Wait<T>(Task<T> task, int milliseconds = 15000)
    {
        if (!task.Wait(milliseconds)) throw new InvalidOperationException("Délai dépassé dans le test.");
        return task.Result;
    }

    public static void Eventually(Func<bool> condition, string what, int milliseconds = 8000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return;
            Thread.Sleep(20);
        }
        throw new InvalidOperationException("Jamais atteint : " + what);
    }

    public static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException("Échec : " + what);
    }

    public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);
}
