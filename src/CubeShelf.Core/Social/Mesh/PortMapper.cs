using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CubeShelf.Core.Social.Mesh;

public enum PortMappingProtocol
{
    /// <summary>Port Control Protocol, RFC 6887.</summary>
    Pcp,

    /// <summary>NAT Port Mapping Protocol, RFC 6886.</summary>
    NatPmp,

    /// <summary>UPnP Internet Gateway Device (WANIPConnection / WANPPPConnection).</summary>
    Upnp
}

/// <summary>
/// A UDP port the home router forwards to this machine: <see cref="ExternalAddress"/>:<see cref="ExternalPort"/>
/// on the internet side reaches <see cref="InternalAddress"/>:<see cref="InternalPort"/>.
/// </summary>
/// <param name="ExternalAddress">Null when the router did not say (UPnP without GetExternalIPAddress).</param>
/// <param name="Lifetime">
/// How long the router keeps it, from <paramref name="ObtainedAt"/>. <see cref="TimeSpan.Zero"/>
/// means permanent: a UPnP router that refused leases, so the mapping must be removed on exit.
/// </param>
public sealed record PortMapping(
    PortMappingProtocol Protocol,
    IPAddress? ExternalAddress,
    int ExternalPort,
    int InternalPort,
    IPAddress InternalAddress,
    TimeSpan Lifetime,
    DateTimeOffset ObtainedAt)
{
    /// <summary>When to renew: halfway through the lifetime, as RFC 6886 and 6887 recommend. Null for a permanent mapping.</summary>
    public DateTimeOffset? RenewAt => Lifetime > TimeSpan.Zero ? ObtainedAt + Lifetime / 2 : null;

    /// <summary>
    /// What renewing and removing need and the caller has no use for: the PCP nonce that proves
    /// the mapping is ours, the UPnP control URL. A mapping rebuilt without it (read back from
    /// disk, say) still renews and removes, at the cost of a new discovery -- or, for PCP, of a
    /// router that may refuse a nonce it does not know.
    /// </summary>
    internal MappingState? State { get; init; }
}

internal sealed record MappingState(string? PcpNonce, UpnpControlPoint? Upnp);

public interface IPortMapper
{
    /// <summary>
    /// Opens a UDP mapping to <paramref name="internalPort"/>, trying PCP, then NAT-PMP, then UPnP
    /// IGD. Null when none works. Never throws for network reasons.
    /// </summary>
    Task<PortMapping?> MapUdpAsync(int internalPort, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Renews before expiry (same external port when the router allows). Null when it can no longer be renewed.</summary>
    Task<PortMapping?> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>Best effort; never throws for network reasons.</summary>
    Task RemoveAsync(PortMapping mapping, CancellationToken cancellationToken = default);
}

/// <param name="Gateway">Null: the IPv4 default gateway, from the network interfaces.</param>
/// <param name="SsdpTarget">Null: 239.255.255.250:1900. Tests point it at a loopback fake.</param>
/// <param name="NatPmpPort">Where PCP and NAT-PMP listen on the gateway; tests use a random loopback port.</param>
/// <param name="AllowLoopbackForTests">Lets 127.0.0.1 count as "on the local network". Tests only.</param>
/// <param name="Timeout">The budget of each step (PCP and NAT-PMP together, SSDP discovery, each HTTP request); 2 s by default.</param>
/// <param name="Description">What the router's UPnP page shows next to the mapping.</param>
public sealed record PortMapperOptions(
    IPAddress? Gateway = null,
    IPEndPoint? SsdpTarget = null,
    int NatPmpPort = 5351,
    bool AllowLoopbackForTests = false,
    TimeSpan? Timeout = null,
    string Description = "CubeShelf");

/// <summary>
/// Opens a UDP port on the home router so friends can reach this node directly, with nothing
/// but the router involved: no server, no relay, no cloud.
///
/// Three protocols, best first. PCP and NAT-PMP are a datagram each way with the default
/// gateway and nobody else. UPnP IGD is the fallback most routers still answer, and the one that
/// has to be handled with care, since anything on the local network can answer its discovery
/// (see <see cref="UpnpIgdClient"/>). None of them is trusted beyond what it says about the one
/// mapping asked for: every number is range-checked, every address validated, every body
/// size-capped and every step timed out.
///
/// The methods never throw for what the network does -- they return null (or nothing) and say
/// why in <see cref="LastError"/>. A cancelled <c>cancellationToken</c> still throws
/// <see cref="OperationCanceledException"/>, as everywhere else in .NET. No state is shared
/// between instances, and an instance may be used from several threads.
/// </summary>
public sealed class PortMapper : IPortMapper
{
    public static readonly IPEndPoint DefaultSsdpTarget = new(IPAddress.Parse("239.255.255.250"), 1900);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Longest lifetime asked for; routers commonly refuse more than a week (UPnP IGD2 caps it there).</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(7);

    private const int MaximumDescriptionLength = 64;

    private readonly PortMapperOptions _options;
    private readonly TimeSpan _timeout;
    private readonly string _description;
    private volatile string? _lastError;

    public PortMapper(PortMapperOptions? options = null)
    {
        _options = options ?? new PortMapperOptions();
        if (_options.NatPmpPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "NatPmpPort must be between 1 and 65535.");
        var timeout = _options.Timeout ?? DefaultTimeout;
        _timeout = timeout < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100)
            : timeout > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30)
            : timeout;
        _description = CleanDescription(_options.Description);
    }

    /// <summary>Why the last call came back empty-handed, in French; null after a success.</summary>
    public string? LastError => _lastError;

    // ------------------------------------------------------------------ the interface

    public async Task<PortMapping?> MapUdpAsync(int internalPort, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(internalPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(internalPort, 65535);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var report = new Report();
        var mapping = await GuardAsync<PortMapping>(async () =>
        {
            var seconds = LifetimeSeconds(lifetime);
            var gateway = ResolveGateway(report, out var refused);
            if (refused) return null;
            if (gateway is not null)
            {
                var mapped = await MapWithPortControlAsync(gateway, internalPort, internalPort, null, seconds, null, report, cancellationToken)
                    .ConfigureAwait(false);
                if (mapped is not null) return mapped;
            }
            return await MapWithUpnpAsync(gateway, internalPort, internalPort, seconds, null, report, cancellationToken)
                .ConfigureAwait(false);
        }, report, cancellationToken).ConfigureAwait(false);

        _lastError = mapping is null ? report.Text("Aucun protocole d'ouverture de port n'a fonctionné.") : null;
        return mapping;
    }

    public async Task<PortMapping?> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var report = new Report();
        var renewed = await GuardAsync<PortMapping>(async () =>
        {
            if (!IsValidPort(mapping.InternalPort))
            {
                report.Add("Ouverture de port à renouveler invalide.");
                return null;
            }

            // A permanent mapping has nothing to renew, but asking again says whether it is
            // still there; a lease is asked for again as long as it was.
            var seconds = mapping.Lifetime > TimeSpan.Zero ? LifetimeSeconds(mapping.Lifetime) : 0u;
            var preferred = IsValidPort(mapping.ExternalPort) ? mapping.ExternalPort : mapping.InternalPort;
            var gateway = ResolveGateway(report, out var refused);
            if (refused) return null;

            switch (mapping.Protocol)
            {
                case PortMappingProtocol.Pcp or PortMappingProtocol.NatPmp:
                    if (gateway is null) return null;
                    return await MapWithPortControlAsync(gateway, mapping.InternalPort, preferred, mapping.ExternalAddress,
                        Math.Max(seconds, 1u), mapping, report, cancellationToken).ConfigureAwait(false);

                case PortMappingProtocol.Upnp:
                    return await MapWithUpnpAsync(gateway, mapping.InternalPort, preferred, seconds,
                        KnownControlPoint(mapping, gateway), report, cancellationToken).ConfigureAwait(false);

                default:
                    return null;
            }
        }, report, cancellationToken).ConfigureAwait(false);

        _lastError = renewed is null ? report.Text("L'ouverture de port n'a pas pu être renouvelée.") : null;
        return renewed;
    }

    public async Task RemoveAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var report = new Report();
        var removed = await GuardAsync<object>(async () =>
        {
            if (!IsValidPort(mapping.InternalPort)) return (object?)null;
            var gateway = ResolveGateway(report, out var refused);
            if (refused) return null;

            switch (mapping.Protocol)
            {
                case PortMappingProtocol.Pcp:
                {
                    if (gateway is null) return null;
                    var nonce = NonceOf(mapping) ?? NatPmpClient.NewNonce();
                    var client = new NatPmpClient(new IPEndPoint(gateway, _options.NatPmpPort));
                    var reply = await client.PcpMapAsync(mapping.InternalPort, mapping.ExternalPort, mapping.ExternalAddress, 0, nonce,
                        _timeout, cancellationToken).ConfigureAwait(false);
                    return Said(reply, "PCP", report);
                }

                case PortMappingProtocol.NatPmp:
                {
                    if (gateway is null) return null;
                    var client = new NatPmpClient(new IPEndPoint(gateway, _options.NatPmpPort));
                    var reply = await client.NatPmpMapAsync(mapping.InternalPort, 0, 0, _timeout, cancellationToken).ConfigureAwait(false);
                    return Said(reply, "NAT-PMP", report);
                }

                case PortMappingProtocol.Upnp:
                {
                    if (!IsValidPort(mapping.ExternalPort)) return null;
                    using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    phase.CancelAfter(_timeout * 3);
                    using var upnp = new UpnpIgdClient(_timeout, _options.AllowLoopbackForTests, report.Add);
                    try
                    {
                        await foreach (var point in ControlPointsAsync(upnp, gateway, KnownControlPoint(mapping, gateway), phase.Token)
                                           .ConfigureAwait(false))
                        {
                            if (await upnp.DeletePortMappingAsync(point, mapping.ExternalPort, phase.Token).ConfigureAwait(false))
                                return new object();
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                    report.Add("UPnP : le routeur n'a pas confirmé la fermeture du port.");
                    return null;
                }

                default:
                    return null;
            }
        }, report, cancellationToken).ConfigureAwait(false);

        _lastError = removed is null ? report.Text("La fermeture du port n'a pas été confirmée.") : null;
    }

    // ------------------------------------------------------------------ PCP and NAT-PMP

    /// <summary>
    /// PCP first; NAT-PMP when the gateway says it only speaks that, or says nothing at all to
    /// PCP within half the budget. A gateway that refuses in PCP is not asked again in NAT-PMP:
    /// it is the same daemon with the same rules. When <paramref name="renewing"/> is given, only
    /// its protocol is used.
    /// </summary>
    private async Task<PortMapping?> MapWithPortControlAsync(
        IPAddress gateway,
        int internalPort,
        int suggestedExternalPort,
        IPAddress? suggestedExternalAddress,
        uint lifetimeSeconds,
        PortMapping? renewing,
        Report report,
        CancellationToken cancellationToken)
    {
        var client = new NatPmpClient(new IPEndPoint(gateway, _options.NatPmpPort));
        var started = DateTimeOffset.UtcNow;
        var endpoint = client.Server;

        if (renewing is null || renewing.Protocol == PortMappingProtocol.Pcp)
        {
            var nonce = (renewing is null ? null : NonceOf(renewing)) ?? NatPmpClient.NewNonce();
            var pcpBudget = renewing is null ? _timeout / 2 : _timeout;
            var reply = await client.PcpMapAsync(internalPort, suggestedExternalPort, suggestedExternalAddress, lifetimeSeconds, nonce,
                pcpBudget, cancellationToken).ConfigureAwait(false);

            switch (reply.Outcome)
            {
                case PortControlOutcome.Success when reply.LifetimeSeconds > 0 && IsValidPort(reply.ExternalPort):
                {
                    var local = LocalAddressToward(gateway);
                    if (local is null) return null;
                    return new PortMapping(PortMappingProtocol.Pcp, reply.ExternalAddress, reply.ExternalPort, internalPort, local,
                        ReportedLifetime(reply.LifetimeSeconds), DateTimeOffset.UtcNow)
                    {
                        State = new MappingState(Convert.ToHexString(nonce), null)
                    };
                }
                case PortControlOutcome.Refused:
                    report.Add($"PCP : la passerelle {endpoint} refuse ({PcpResult(reply.ResultCode)}).");
                    return null;
                case PortControlOutcome.Unreachable:
                    report.Add($"PCP / NAT-PMP : rien n'écoute sur la passerelle {endpoint}.");
                    return null;
                case PortControlOutcome.UseNatPmp:
                    break;
                default:
                    report.Add($"PCP : pas de réponse de la passerelle {endpoint}.");
                    break;
            }
            if (renewing is not null) return null;
        }

        var remaining = _timeout - (DateTimeOffset.UtcNow - started);
        if (remaining < TimeSpan.FromMilliseconds(250)) remaining = TimeSpan.FromMilliseconds(250);

        var natPmp = await client.NatPmpMapAsync(internalPort, suggestedExternalPort, lifetimeSeconds, remaining, cancellationToken)
            .ConfigureAwait(false);
        switch (natPmp.Outcome)
        {
            case PortControlOutcome.Success when natPmp.LifetimeSeconds > 0 && IsValidPort(natPmp.ExternalPort):
            {
                var local = LocalAddressToward(gateway);
                if (local is null) return null;

                // The gateway just answered; its address takes one more round trip, not another full budget.
                var address = await client.NatPmpExternalAddressAsync(_timeout / 2, cancellationToken).ConfigureAwait(false);
                return new PortMapping(PortMappingProtocol.NatPmp,
                    address.Outcome == PortControlOutcome.Success ? address.ExternalAddress : null,
                    natPmp.ExternalPort, internalPort, local, ReportedLifetime(natPmp.LifetimeSeconds), DateTimeOffset.UtcNow);
            }
            case PortControlOutcome.Refused:
                report.Add($"NAT-PMP : la passerelle {endpoint} refuse ({NatPmpResult(natPmp.ResultCode)}).");
                return null;
            case PortControlOutcome.Unreachable:
                report.Add($"NAT-PMP : rien n'écoute sur la passerelle {endpoint}.");
                return null;
            default:
                report.Add($"NAT-PMP : pas de réponse de la passerelle {endpoint}.");
                return null;
        }
    }

    private static object? Said(PortControlReply reply, string protocol, Report report)
    {
        if (reply.Outcome == PortControlOutcome.Success) return new object();
        report.Add(reply.Outcome == PortControlOutcome.Refused
            ? $"{protocol} : la passerelle refuse la fermeture (code {reply.ResultCode})."
            : $"{protocol} : la passerelle n'a pas confirmé la fermeture.");
        return null;
    }

    private static byte[]? NonceOf(PortMapping mapping)
    {
        if (mapping.State?.PcpNonce is not { Length: NatPmpClient.NonceLength * 2 } hex) return null;
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string PcpResult(int code) => code switch
    {
        2 => "non autorisé",
        3 => "requête mal formée",
        4 => "opération non prise en charge",
        5 or 6 => "option refusée",
        7 => "panne réseau côté internet",
        8 => "plus de ressources",
        9 => "protocole non pris en charge",
        10 => "quota dépassé",
        11 => "impossible de fournir l'adresse externe",
        12 => "adresse source inattendue, un autre NAT est sans doute en chemin",
        13 => "trop de pairs distants",
        _ => "code " + code.ToString(CultureInfo.InvariantCulture)
    };

    private static string NatPmpResult(int code) => code switch
    {
        2 => "non autorisé",
        3 => "panne réseau côté internet",
        4 => "plus de ressources",
        5 => "opération non prise en charge",
        _ => "code " + code.ToString(CultureInfo.InvariantCulture)
    };

    // ------------------------------------------------------------------ UPnP

    private async Task<PortMapping?> MapWithUpnpAsync(
        IPAddress? gateway,
        int internalPort,
        int preferredExternalPort,
        uint leaseSeconds,
        UpnpControlPoint? known,
        Report report,
        CancellationToken cancellationToken)
    {
        // The whole of UPnP, discovery included, gets three budgets: a router that answers
        // slowly at every step must not hold the caller for a minute.
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        phase.CancelAfter(_timeout * 3);
        using var upnp = new UpnpIgdClient(_timeout, _options.AllowLoopbackForTests, report.Add);

        try
        {
            await foreach (var point in ControlPointsAsync(upnp, gateway, known, phase.Token).ConfigureAwait(false))
            {
                var added = await upnp.AddPortMappingAsync(point, internalPort, preferredExternalPort, leaseSeconds, _description, phase.Token)
                    .ConfigureAwait(false);
                if (added is not { } obtained) continue;

                var external = await upnp.GetExternalAddressAsync(point, phase.Token).ConfigureAwait(false);
                return new PortMapping(PortMappingProtocol.Upnp, external, obtained.ExternalPort, internalPort, point.LocalAddress,
                    obtained.LeaseSeconds == 0 ? TimeSpan.Zero : ReportedLifetime(obtained.LeaseSeconds), DateTimeOffset.UtcNow)
                {
                    State = new MappingState(null, point)
                };
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            report.Add("UPnP : le routeur a mis trop de temps à répondre.");
            return null;
        }
    }

    /// <summary>
    /// The control point remembered with a mapping first, then -- only if the caller gets that
    /// far, which means it did not work -- whatever discovery finds now. miniupnpd picks a new
    /// HTTP port each time it starts, so a router that rebooted has moved its control URL.
    /// </summary>
    private async IAsyncEnumerable<UpnpControlPoint> ControlPointsAsync(
        UpnpIgdClient upnp,
        IPAddress? gateway,
        UpnpControlPoint? known,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (known is not null) yield return known;
        foreach (var point in await DiscoverAsync(upnp, gateway, cancellationToken).ConfigureAwait(false))
        {
            if (!point.Equals(known)) yield return point;
        }
    }

    private async Task<IReadOnlyList<UpnpControlPoint>> DiscoverAsync(UpnpIgdClient upnp, IPAddress? gateway, CancellationToken cancellationToken)
    {
        var target = _options.SsdpTarget ?? DefaultSsdpTarget;
        var local = gateway is null ? null : LocalAddressToward(gateway);
        return await upnp.DiscoverAsync(target, gateway, local, _timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The control point remembered with a mapping, as long as it still belongs to the current gateway.</summary>
    private static UpnpControlPoint? KnownControlPoint(PortMapping mapping, IPAddress? gateway)
    {
        var point = mapping.State?.Upnp;
        if (point is null) return null;
        return gateway is null || point.Router.Equals(gateway) ? point : null;
    }

    // ------------------------------------------------------------------ the gateway and this machine

    /// <summary>
    /// The configured gateway, or the default one. No default gateway at all still leaves UPnP to
    /// try (any router on the local network may answer); a gateway that is not a plausible
    /// router address sets <paramref name="refused"/>, and nothing is sent anywhere.
    /// </summary>
    private IPAddress? ResolveGateway(Report report, out bool refused)
    {
        refused = false;
        var gateway = _options.Gateway ?? FindDefaultGateway();
        if (gateway is null)
        {
            report.Add("Aucune passerelle IPv4 par défaut trouvée.");
            return null;
        }
        if (gateway.IsIPv4MappedToIPv6) gateway = gateway.MapToIPv4();
        if (!IsUsableGateway(gateway, _options.AllowLoopbackForTests))
        {
            report.Add($"Passerelle {gateway} refusée : ce n'est pas une adresse IPv4 de routeur.");
            refused = true;
            return null;
        }
        return gateway;
    }

    private static bool IsUsableGateway(IPAddress address, bool allowLoopback)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(address)) return allowLoopback;
        var b = address.GetAddressBytes();
        return b[0] != 0 && b[0] < 224 && !address.Equals(IPAddress.Broadcast);
    }

    /// <summary>
    /// The IPv4 default gateway of the interface the system would use to reach the internet:
    /// the one that owns the local address a UDP socket "connected" to a documentation address
    /// gets (connecting a UDP socket sends nothing). Failing that, the first interface that is up
    /// and has a gateway, physical adapters before virtual ones.
    /// </summary>
    public static IPAddress? FindDefaultGateway()
    {
        IPAddress? routed = null;
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9)); // TEST-NET-1, RFC 5737
            routed = (probe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
        }

        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return null;
        }

        var found = new List<(IPAddress Gateway, int Rank)>();
        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var gateway = properties.GatewayAddresses
                .Select(entry => entry.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && IsUsableGateway(address, false));
            if (gateway is null) continue;

            var ownsRoute = routed is not null && properties.UnicastAddresses.Any(unicast => unicast.Address.Equals(routed));
            var virtualAdapter = new[] { "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "wsl", "docker" }
                .Any(word => adapter.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                             adapter.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
            found.Add((gateway, ownsRoute ? 0 : virtualAdapter ? 2 : 1));
        }

        return found.OrderBy(entry => entry.Rank).Select(entry => entry.Gateway).FirstOrDefault();
    }

    /// <summary>
    /// This machine's own IPv4 address on the way to <paramref name="remote"/>, as the routing
    /// table picks it. Connecting a UDP socket sends nothing.
    /// </summary>
    public static IPAddress? LocalAddressToward(IPAddress remote)
    {
        if (remote.AddressFamily != AddressFamily.InterNetwork) return null;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(remote, 9));
            return socket.LocalEndPoint is IPEndPoint { Address: var local } &&
                   local.AddressFamily == AddressFamily.InterNetwork && !local.Equals(IPAddress.Any)
                ? local
                : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ addresses and numbers

    /// <summary>
    /// Private (RFC 1918) or link-local IPv4: where a home router lives. Loopback only when
    /// <paramref name="allowLoopback"/>, for tests.
    /// </summary>
    public static bool IsLanAddress(IPAddress address, bool allowLoopback = false)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        if (b[0] == 127) return allowLoopback;
        return b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254);
    }

    /// <summary>
    /// An IPv4 address a router may claim as its public one: not unspecified, loopback,
    /// multicast, broadcast or in 0.0.0.0/8. Private addresses pass -- a router behind another
    /// router has one, and the caller is better told the truth.
    /// </summary>
    public static bool IsUsableExternalAddress(IPAddress address, bool allowLoopback = false)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        if (b[0] == 127) return allowLoopback;
        return b[0] != 0 && b[0] < 224;
    }

    /// <summary>
    /// Exactly four decimal numbers from 0 to 255 separated by dots. Stricter than
    /// <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>, which also reads "1", "0x7f.1"
    /// and IPv6.
    /// </summary>
    public static IPAddress? ParseDottedQuad(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 15) return null;
        var parts = text.Split('.');
        if (parts.Length != 4) return null;
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var part = parts[i];
            if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit)) return null;
            var value = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
            if (value > 255) return null;
            bytes[i] = (byte)value;
        }
        return new IPAddress(bytes);
    }

    private static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    private static uint LifetimeSeconds(TimeSpan lifetime)
    {
        var seconds = Math.Ceiling(Math.Min(lifetime.TotalSeconds, MaximumLifetime.TotalSeconds));
        return (uint)Math.Max(1, seconds);
    }

    /// <summary>A router may grant more than was asked; more than a week is not believed, which only makes us renew early.</summary>
    private static TimeSpan ReportedLifetime(uint seconds) =>
        TimeSpan.FromSeconds(Math.Min(seconds, MaximumLifetime.TotalSeconds));

    private static string CleanDescription(string? description)
    {
        var cleaned = new string((description ?? "").Where(c => !char.IsControl(c)).Take(MaximumDescriptionLength).ToArray()).Trim();
        return cleaned.Length == 0 ? "CubeShelf" : cleaned;
    }

    // ------------------------------------------------------------------ never throwing

    /// <summary>
    /// Runs one operation and turns whatever the network made it throw into a diagnostic. Only
    /// the caller's own cancellation goes through.
    /// </summary>
    private static async Task<T?> GuardAsync<T>(Func<Task<T?>> operation, Report report, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Every parser above is meant to say no rather than throw; this is the net under them.
            report.Add($"Erreur inattendue pendant l'ouverture de port ({exception.GetType().Name}).");
            return null;
        }
    }

    /// <summary>What went wrong along the way, in order; several steps may each have something to say.</summary>
    private sealed class Report
    {
        private readonly List<string> _lines = new();

        public void Add(string line)
        {
            lock (_lines)
            {
                if (_lines.Count < 16 && !_lines.Contains(line)) _lines.Add(line);
            }
        }

        public string Text(string fallback)
        {
            lock (_lines)
                return _lines.Count == 0 ? fallback : string.Join(" ", _lines);
        }
    }
}
