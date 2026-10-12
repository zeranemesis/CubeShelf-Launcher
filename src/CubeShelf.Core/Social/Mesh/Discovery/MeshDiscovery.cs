using System.Net;
using System.Net.Sockets;

namespace CubeShelf.Core.Social.Mesh.Discovery;

public sealed record MeshDiscoveryOptions
{
    /// <summary>How often the meeting point is read once the node knows enough of the network.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>How often while it knows almost nobody: sooner, but never in a tight loop.</summary>
    public TimeSpan IntervalWhileAlone { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How many nodes count as "enough": below this the short interval applies.</summary>
    public int EnoughNodes { get; init; } = 8;

    /// <summary>Entry points handed to the node per lookup.</summary>
    public int SeedsPerLookup { get; init; } = 16;

    public MainlineRendezvousOptions Rendezvous { get; init; } = new();
}

/// <summary>
/// Keeps a node in touch with CubeShelf installations it was never introduced to: reads the
/// meeting point (<see cref="MainlineRendezvous"/>) and hands what it finds to the node as ways
/// in; and, while the node is directly reachable, says so there -- so the next newcomer finds it.
/// A node behind a closed router only reads: announcing an address nobody can reach would only
/// send newcomers into a wall.
/// </summary>
public sealed class MeshDiscovery : IAsyncDisposable
{
    private readonly MeshNode _node;
    private readonly MeshDiscoveryOptions _options;
    private readonly Func<MainlineRendezvous> _rendezvous;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private bool _disposed;

    public MeshDiscovery(MeshNode node, MeshDiscoveryOptions? options = null, Func<MainlineRendezvous>? rendezvous = null)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _options = options ?? new MeshDiscoveryOptions();
        _rendezvous = rendezvous ?? (() => new MainlineRendezvous(_options.Rendezvous));
    }

    /// <summary>The last lookup: when, how many CubeShelf nodes it found, and whether we announced ourselves.</summary>
    public (DateTimeOffset At, int Found, bool Announced)? Last { get; private set; }

    /// <summary>A lookup finished. Any thread.</summary>
    public event Action? Changed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        _loop = RunAsync(_lifetime.Token);
    }

    /// <summary>Reads the meeting point now: the node just became reachable, or the network changed.</summary>
    public void Kick()
    {
        try
        {
            _wake.Release();
        }
        catch (Exception exception) when (exception is SemaphoreFullException or ObjectDisposedException)
        {
        }
    }

    /// <summary>One round, for tests and for the loop.</summary>
    public async Task<(int Found, bool Announced)> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var rendezvous = _rendezvous();
        var lookup = await rendezvous.LookupAsync(cancellationToken).ConfigureAwait(false);
        var found = lookup.Peers.Where(peer => !IsSelf(peer)).ToArray();
        if (found.Length > 0) _node.AddSeeds(found.OrderBy(_ => Random.Shared.Next()).Take(_options.SeedsPerLookup));

        var announced = false;
        if (AnnouncePort() is { } port && lookup.Closest.Count > 0)
            announced = await rendezvous.AnnounceAsync(lookup, port, cancellationToken).ConfigureAwait(false) > 0;

        Last = (DateTimeOffset.UtcNow, found.Length, announced);
        Changed?.Invoke();
        return (found.Length, announced);
    }

    /// <summary>
    /// The port others reach us on, when they can: our proven public IPv4 endpoint. The meeting
    /// point records the address an announcement comes from, and the rendezvous speaks IPv4.
    /// </summary>
    private int? AnnouncePort()
    {
        if (_node.Reachability != MeshReachability.Public) return null;
        return _node.Address.Direct.FirstOrDefault(endpoint => endpoint.AddressFamily == AddressFamily.InterNetwork)?.Port;
    }

    private bool IsSelf(IPEndPoint peer) =>
        _node.Address.Direct.Any(own => own.Port == peer.Port && MeshAddresses.Normalize(own).Address.Equals(MeshAddresses.Normalize(peer).Address));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // No network, no DNS, a router gone: the next round tries again.
                }

                var wait = _node.Table.Count < _options.EnoughNodes ? _options.IntervalWhileAlone : _options.Interval;
                await _wake.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _lifetime?.Dispose();
        _wake.Dispose();
    }
}
