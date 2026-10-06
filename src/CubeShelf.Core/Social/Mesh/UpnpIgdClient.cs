using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;

namespace CubeShelf.Core.Social.Mesh;

/// <summary>
/// Where to send SOAP actions for one router: the control URL of its WANIPConnection or
/// WANPPPConnection service, already checked to stay on the router that answered discovery.
/// </summary>
internal sealed record UpnpControlPoint(IPAddress Router, Uri ControlUrl, string ServiceType, IPAddress LocalAddress);

/// <summary>What a SOAP action came to: success with the values asked for, or the UPnP error code (-1 when there was none to read).</summary>
internal sealed record SoapReply(bool Succeeded, int ErrorCode, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// UPnP Internet Gateway Device: SSDP discovery, the device description over HTTP, then SOAP
/// actions on the connection service. The oldest and most widespread of the three protocols,
/// and by far the largest attack surface: anything on the local network can answer an M-SEARCH,
/// and the answer names a URL we are then asked to fetch.
///
/// So the URL is held on a short leash. Its host must be an IP literal equal to the address the
/// answer came from, that address must be on the local network (private or link-local IPv4) and,
/// when the default gateway is known, be the gateway itself -- no name to resolve, no way to
/// point us at a third host, a public address or this machine's own services. The control URL
/// in the description is held to the same host. Plain HTTP only (routers do not do TLS), no
/// redirects, no proxy, no cookies, short timeouts, 64 KB at most per body, and XML read with
/// DTDs prohibited and no resolver, so neither an entity bomb nor an external entity gets
/// anywhere.
/// </summary>
internal sealed class UpnpIgdClient : IDisposable
{
    public const int MaximumBodyBytes = 64 * 1024;
    public const int MaximumDatagramBytes = 4096;

    public const string WanIpConnection1 = "urn:schemas-upnp-org:service:WANIPConnection:1";
    public const string WanIpConnection2 = "urn:schemas-upnp-org:service:WANIPConnection:2";
    public const string WanPppConnection1 = "urn:schemas-upnp-org:service:WANPPPConnection:1";
    public const string GatewayDevice1 = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";

    /// <summary>Preferred first: an IP connection is the usual one; a PPP service is often listed and down.</summary>
    private static readonly string[] ServiceTypes = { WanIpConnection2, WanIpConnection1, WanPppConnection1 };

    private static readonly string[] SearchTargets = { WanIpConnection1, WanIpConnection2, WanPppConnection1, GatewayDevice1 };

    private const int MaximumCandidates = 4;
    private const int MaximumServices = 16;
    private const int MaximumUrlLength = 1024;

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly bool _allowLoopback;
    private readonly Action<string> _report;

    public UpnpIgdClient(TimeSpan timeout, bool allowLoopback, Action<string> report)
    {
        _timeout = timeout;
        _allowLoopback = allowLoopback;
        _report = report ?? throw new ArgumentNullException(nameof(report));
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = timeout,
            MaxResponseHeadersLength = 16, // KB
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(5)
        };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf UPnP/1.1");
    }

    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// The connection services of the first router that answers discovery with a description we
    /// accept, best first. Empty when none does within the budget.
    /// </summary>
    public async Task<IReadOnlyList<UpnpControlPoint>> DiscoverAsync(
        IPEndPoint target,
        IPAddress? gateway,
        IPAddress? localInterface,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        using var search = SsdpSearch.Open(target, localInterface, budget, _report);
        if (search is null) return Array.Empty<UpnpControlPoint>();

        var heard = false;
        for (var tried = 0; tried < MaximumCandidates;)
        {
            var candidate = await search.NextAsync(cancellationToken).ConfigureAwait(false);
            if (candidate is null) break;
            heard = true;

            var (responder, location) = candidate.Value;
            if (!IsAcceptableRouter(responder, gateway)) continue;
            if (!IsRouterUrl(location, responder))
            {
                _report($"UPnP : adresse de description refusée ({Shorten(location.OriginalString)}), elle ne pointe pas vers le routeur qui a répondu ({responder}).");
                continue;
            }

            tried++;
            var points = await ReadDescriptionAsync(responder, location, cancellationToken).ConfigureAwait(false);
            if (points.Count > 0) return points;
        }

        if (!heard) _report("UPnP : aucun routeur n'a répondu à la découverte.");
        return Array.Empty<UpnpControlPoint>();
    }

    private bool IsAcceptableRouter(IPAddress responder, IPAddress? gateway)
    {
        if (!PortMapper.IsLanAddress(responder, _allowLoopback))
        {
            _report($"UPnP : réponse ignorée de {responder}, qui n'est pas sur le réseau local.");
            return false;
        }
        if (gateway is not null && !responder.Equals(gateway))
        {
            _report($"UPnP : réponse ignorée de {responder}, qui n'est pas la passerelle ({gateway}).");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Plain http, no user info, and a host that is the IPv4 literal of the router itself.
    /// <see cref="Uri"/> canonicalises the forms an IPv4 address can be spelled in (decimal,
    /// octal, shortened), so comparing addresses is enough.
    /// </summary>
    public static bool IsRouterUrl(Uri url, IPAddress router) =>
        url.IsAbsoluteUri &&
        url.OriginalString.Length <= MaximumUrlLength &&
        string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) &&
        url.HostNameType == UriHostNameType.IPv4 &&
        string.IsNullOrEmpty(url.UserInfo) &&
        IPAddress.TryParse(url.Host, out var host) &&
        host.Equals(router) &&
        url.Port is > 0 and <= 65535;

    private async Task<IReadOnlyList<UpnpControlPoint>> ReadDescriptionAsync(IPAddress router, Uri location, CancellationToken cancellationToken)
    {
        var body = await GetAsync(location, cancellationToken).ConfigureAwait(false);
        if (body is null) return Array.Empty<UpnpControlPoint>();

        var description = ParseDescription(body);
        if (description is null)
        {
            _report($"UPnP : description illisible ou refusée ({Shorten(location.OriginalString)}).");
            return Array.Empty<UpnpControlPoint>();
        }

        var baseUrl = location;
        if (!string.IsNullOrWhiteSpace(description.Value.UrlBase))
        {
            if (!Uri.TryCreate(description.Value.UrlBase.Trim(), UriKind.Absolute, out var declared))
            {
                _report("UPnP : URLBase illisible dans la description.");
                return Array.Empty<UpnpControlPoint>();
            }
            baseUrl = declared;
        }

        var local = PortMapper.LocalAddressToward(router);
        if (local is null)
        {
            _report($"UPnP : aucune adresse locale ne mène au routeur {router}.");
            return Array.Empty<UpnpControlPoint>();
        }

        var points = new List<UpnpControlPoint>();
        foreach (var serviceType in ServiceTypes)
        {
            foreach (var (type, control) in description.Value.Services)
            {
                if (!string.Equals(type, serviceType, StringComparison.Ordinal)) continue;
                if (!Uri.TryCreate(baseUrl, control, out var controlUrl) || !IsRouterUrl(controlUrl, router))
                {
                    _report($"UPnP : URL de contrôle refusée ({Shorten(control)}), elle sort du routeur {router}.");
                    continue;
                }
                points.Add(new UpnpControlPoint(router, controlUrl, type, local));
            }
        }

        if (points.Count == 0 && description.Value.Services.Count == 0)
            _report("UPnP : le routeur ne propose pas de service de connexion WAN.");
        return points;
    }

    /// <summary>
    /// The URLBase (UPnP 1.0) and the WAN connection services of a device description. Null for
    /// anything that is not well-formed XML within the limits, or that declares a DTD.
    /// </summary>
    internal static (string? UrlBase, IReadOnlyList<(string Type, string ControlUrl)> Services)? ParseDescription(byte[] xml)
    {
        var services = new List<(string, string)>();
        string? urlBase = null;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), SafeXmlSettings());
            var serviceDepth = -1;
            string? type = null, control = null;
            string? capturing = null;
            var captureDepth = -1;
            var text = new StringBuilder();

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var name = reader.LocalName;
                        if (name == "service" && !reader.IsEmptyElement && serviceDepth < 0)
                        {
                            serviceDepth = reader.Depth;
                            type = control = null;
                        }
                        else if (!reader.IsEmptyElement && capturing is null &&
                                 ((serviceDepth >= 0 && reader.Depth == serviceDepth + 1 && name is "serviceType" or "controlURL") ||
                                  (name == "URLBase" && reader.Depth == 1)))
                        {
                            capturing = name;
                            captureDepth = reader.Depth;
                            text.Clear();
                        }
                        break;

                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                        if (capturing is not null && reader.Depth == captureDepth + 1)
                        {
                            text.Append(reader.Value);
                            if (text.Length > MaximumUrlLength) return null;
                        }
                        break;

                    case XmlNodeType.EndElement:
                        if (capturing is not null && reader.Depth == captureDepth && reader.LocalName == capturing)
                        {
                            var value = text.ToString().Trim();
                            switch (capturing)
                            {
                                case "serviceType": type = value; break;
                                case "controlURL": control = value; break;
                                case "URLBase": urlBase = value; break;
                            }
                            capturing = null;
                        }
                        else if (serviceDepth >= 0 && reader.Depth == serviceDepth && reader.LocalName == "service")
                        {
                            if (type is not null && !string.IsNullOrEmpty(control) &&
                                ServiceTypes.Contains(type, StringComparer.Ordinal) && services.Count < MaximumServices)
                                services.Add((type, control));
                            serviceDepth = -1;
                        }
                        break;
                }
            }
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
        return (urlBase, services);
    }

    // ------------------------------------------------------------------ SOAP

    /// <summary>
    /// AddPortMapping on <paramref name="externalPort"/>, falling back on a conflict (718) to the
    /// next few ports then a random high one, and on "permanent leases only" (725) to a lease of
    /// 0. Returns the external port and lease obtained, or null.
    /// </summary>
    public async Task<(int ExternalPort, uint LeaseSeconds)?> AddPortMappingAsync(
        UpnpControlPoint point,
        int internalPort,
        int externalPort,
        uint leaseSeconds,
        string description,
        CancellationToken cancellationToken)
    {
        var ports = new List<int> { externalPort };
        for (var step = 1; step <= 8; step++)
        {
            var next = internalPort + step;
            if (next <= 65535 && !ports.Contains(next)) ports.Add(next);
        }
        int random;
        do random = System.Security.Cryptography.RandomNumberGenerator.GetInt32(49152, 65536);
        while (ports.Contains(random));
        ports.Add(random);

        var lease = leaseSeconds;
        for (var index = 0; index < ports.Count; index++)
        {
            var port = ports[index];
            var reply = await SoapAsync(point, "AddPortMapping", new (string, string)[]
            {
                ("NewRemoteHost", ""),
                ("NewExternalPort", port.ToString(CultureInfo.InvariantCulture)),
                ("NewProtocol", "UDP"),
                ("NewInternalPort", internalPort.ToString(CultureInfo.InvariantCulture)),
                ("NewInternalClient", point.LocalAddress.ToString()),
                ("NewEnabled", "1"),
                ("NewPortMappingDescription", description),
                ("NewLeaseDuration", lease.ToString(CultureInfo.InvariantCulture))
            }, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);

            if (reply is null) return null;
            if (reply.Succeeded) return (port, lease);

            switch (reply.ErrorCode)
            {
                case 725 when lease != 0:
                    // OnlyPermanentLeasesSupported: the router keeps it until removed, which the
                    // caller must then do on the way out.
                    lease = 0;
                    index--;
                    continue;
                case 718:
                    // ConflictInMappingEntry: someone else on the network holds that port.
                    _report($"UPnP : le port externe {port} est déjà pris sur le routeur.");
                    continue;
                default:
                    _report(reply.ErrorCode > 0
                        ? $"UPnP : le routeur refuse l'ouverture du port (erreur {reply.ErrorCode})."
                        : "UPnP : le routeur refuse l'ouverture du port.");
                    return null;
            }
        }

        _report("UPnP : aucun port externe libre sur le routeur.");
        return null;
    }

    /// <summary>The router's public address, or null when it does not say or says something that is not one.</summary>
    public async Task<IPAddress?> GetExternalAddressAsync(UpnpControlPoint point, CancellationToken cancellationToken)
    {
        var reply = await SoapAsync(point, "GetExternalIPAddress", Array.Empty<(string, string)>(),
            new[] { "NewExternalIPAddress" }, cancellationToken).ConfigureAwait(false);
        if (reply is not { Succeeded: true } || !reply.Values.TryGetValue("NewExternalIPAddress", out var text)) return null;

        var address = PortMapper.ParseDottedQuad(text.Trim());
        if (address is null || !PortMapper.IsUsableExternalAddress(address, allowLoopback: false))
        {
            _report($"UPnP : adresse externe invalide annoncée par le routeur ({Shorten(text)}).");
            return null;
        }
        return address;
    }

    /// <summary>True when the mapping is gone, including when the router says it had none (714 NoSuchEntryInArray).</summary>
    public async Task<bool> DeletePortMappingAsync(UpnpControlPoint point, int externalPort, CancellationToken cancellationToken)
    {
        var reply = await SoapAsync(point, "DeletePortMapping", new (string, string)[]
        {
            ("NewRemoteHost", ""),
            ("NewExternalPort", externalPort.ToString(CultureInfo.InvariantCulture)),
            ("NewProtocol", "UDP")
        }, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
        return reply is { Succeeded: true } or { ErrorCode: 714 };
    }

    /// <summary>One SOAP action. Null when the exchange itself failed (timeout, size, not HTTP).</summary>
    private async Task<SoapReply?> SoapAsync(
        UpnpControlPoint point,
        string action,
        IReadOnlyList<(string Name, string Value)> arguments,
        IReadOnlyCollection<string> wanted,
        CancellationToken cancellationToken)
    {
        var envelope = new StringBuilder()
            .Append("<?xml version=\"1.0\"?>\r\n")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">")
            .Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(point.ServiceType).Append("\">");
        foreach (var (name, value) in arguments)
            envelope.Append('<').Append(name).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
        envelope.Append("</u:").Append(action).Append("></s:Body></s:Envelope>\r\n");

        using var request = new HttpRequestMessage(HttpMethod.Post, point.ControlUrl)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(envelope.ToString()))
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=\"utf-8\"");
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{point.ServiceType}#{action}\"");
        request.Headers.ConnectionClose = true;

        var exchanged = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (exchanged is null) return null;
        var (status, body) = exchanged.Value;

        var values = ParseSoap(body, wanted, out var errorCode, out var wellFormed);
        if (status == HttpStatusCode.OK && wellFormed && errorCode is null)
            return new SoapReply(true, 0, values);
        if (!wellFormed && body.Length > 0)
            _report($"UPnP : réponse illisible du routeur à {action}.");
        return new SoapReply(false, errorCode ?? -1, values);
    }

    /// <summary>
    /// The text of the elements named in <paramref name="wanted"/>, and the UPnP errorCode when
    /// the body is a fault. <paramref name="wellFormed"/> is false for anything that is not
    /// acceptable XML (a DTD, a bomb, garbage, over the limit).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseSoap(byte[] xml, IReadOnlyCollection<string> wanted, out int? errorCode, out bool wellFormed)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        errorCode = null;
        wellFormed = false;
        if (xml.Length == 0) return values;

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), SafeXmlSettings());
            string? capturing = null;
            var captureDepth = -1;
            var text = new StringBuilder();
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element when capturing is null && !reader.IsEmptyElement &&
                                                  (reader.LocalName == "errorCode" || wanted.Contains(reader.LocalName)):
                        capturing = reader.LocalName;
                        captureDepth = reader.Depth;
                        text.Clear();
                        break;
                    case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace
                        when capturing is not null && reader.Depth == captureDepth + 1:
                        text.Append(reader.Value);
                        if (text.Length > 256) return values;
                        break;
                    case XmlNodeType.EndElement when capturing is not null && reader.Depth == captureDepth:
                        var value = text.ToString().Trim();
                        if (capturing == "errorCode")
                        {
                            errorCode = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code is > 0 and < 10000
                                ? code
                                : -1;
                        }
                        else
                        {
                            values.TryAdd(capturing, value);
                        }
                        capturing = null;
                        break;
                }
            }
            wellFormed = true;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            values.Clear();
            errorCode = null;
        }
        return values;
    }

    /// <summary>What XmlReader throws for bytes that are not acceptable XML: a DTD, a bad encoding, garbage.</summary>
    private static bool IsUnreadable(Exception exception) =>
        exception is XmlException or DecoderFallbackException or ArgumentException or NotSupportedException or InvalidOperationException;

    // ------------------------------------------------------------------ HTTP

    private async Task<byte[]?> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.ConnectionClose = true;
        var exchanged = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (exchanged is null) return null;
        if (exchanged.Value.Status != HttpStatusCode.OK)
        {
            _report($"UPnP : la description n'a pas pu être lue (HTTP {(int)exchanged.Value.Status}).");
            return null;
        }
        return exchanged.Value.Body;
    }

    /// <summary>The status and at most 64 KB of body, within the timeout. Null on any failure, said in the report.</summary>
    private async Task<(HttpStatusCode Status, byte[] Body)?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is > MaximumBodyBytes)
            {
                _report("UPnP : réponse du routeur trop grande, ignorée.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var body = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > MaximumBodyBytes)
                {
                    _report("UPnP : réponse du routeur trop grande, ignorée.");
                    return null;
                }
                body.Write(chunk, 0, read);
            }
            return (response.StatusCode, body.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _report($"UPnP : le routeur n'a pas répondu à temps ({request.RequestUri?.Authority}).");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or InvalidOperationException)
        {
            _report($"UPnP : échange avec le routeur impossible ({request.RequestUri?.Authority}).");
            return null;
        }
    }

    private static XmlReaderSettings SafeXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaximumBodyBytes,
        MaxCharactersFromEntities = 1024,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true
    };

    private static string Shorten(string text)
    {
        var printable = new string(text.Where(c => !char.IsControl(c)).Take(120).ToArray());
        return printable.Length < text.Length ? printable + "…" : printable;
    }

    // ------------------------------------------------------------------ SSDP

    /// <summary>
    /// One M-SEARCH round: the four search targets sent together, repeated twice more if nothing
    /// usable came back, answers read until the budget runs out. Each LOCATION is offered once.
    /// </summary>
    private sealed class SsdpSearch : IDisposable
    {
        private const int MaximumDatagrams = 256;

        private readonly Socket _socket;
        private readonly IPEndPoint _target;
        private readonly TimeSpan _budget;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly byte[] _buffer = new byte[MaximumDatagramBytes + 1];
        private int _rounds;
        private int _datagrams;
        private TimeSpan _nextRound = TimeSpan.Zero;

        private SsdpSearch(Socket socket, IPEndPoint target, TimeSpan budget)
        {
            _socket = socket;
            _target = target;
            _budget = budget;
        }

        public static SsdpSearch? Open(IPEndPoint target, IPAddress? localInterface, TimeSpan budget, Action<string> report)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // SIO_UDP_CONNRESET off: an ICMP "port unreachable" must not surface as an
                    // error on the next receive and cut the search short.
                    try
                    {
                        socket.IOControl(unchecked((int)0x9800000C), new byte[4], null);
                    }
                    catch (SocketException)
                    {
                    }
                }

                var multicast = target.Address.AddressFamily == AddressFamily.InterNetwork &&
                                (target.Address.GetAddressBytes()[0] & 0xF0) == 0xE0;
                if (multicast && localInterface is not null)
                {
                    // Out of the interface that faces the gateway, not whichever the system prefers.
                    socket.Bind(new IPEndPoint(localInterface, 0));
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localInterface.GetAddressBytes());
                }
                else
                {
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                }
                if (multicast)
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                return new SsdpSearch(socket, target, budget);
            }
            catch (SocketException)
            {
                socket.Dispose();
                report("UPnP : impossible d'envoyer la découverte sur le réseau local.");
                return null;
            }
        }

        public void Dispose() => _socket.Dispose();

        /// <summary>The next router that answered with a LOCATION not offered before, or null when the budget is spent.</summary>
        public async Task<(IPAddress Responder, Uri Location)?> NextAsync(CancellationToken cancellationToken)
        {
            while (_clock.Elapsed < _budget && _datagrams < MaximumDatagrams)
            {
                if (_rounds < 3 && _clock.Elapsed >= _nextRound)
                {
                    await SendRoundAsync(cancellationToken).ConfigureAwait(false);
                    _rounds++;
                    _nextRound = _clock.Elapsed + _budget / 3;
                }

                var until = _rounds < 3 && _nextRound < _budget ? _nextRound : _budget;
                var left = until - _clock.Elapsed;
                if (left <= TimeSpan.Zero) continue;

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(left);
                SocketReceiveFromResult received;
                try
                {
                    received = await _socket.ReceiveFromAsync(_buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), wait.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }
                catch (SocketException)
                {
                    // Oversized, or an ICMP error from an earlier send: neither is an answer.
                    _datagrams++;
                    continue;
                }

                _datagrams++;
                if (received.RemoteEndPoint is not IPEndPoint from || received.ReceivedBytes > MaximumDatagramBytes) continue;

                var location = ParseResponse(_buffer.AsSpan(0, received.ReceivedBytes));
                if (location is null) continue;
                if (!_seen.Add(from.Address + " " + location.OriginalString)) continue;
                return (from.Address.IsIPv4MappedToIPv6 ? from.Address.MapToIPv4() : from.Address, location);
            }
            return null;
        }

        private async Task SendRoundAsync(CancellationToken cancellationToken)
        {
            foreach (var searchTarget in SearchTargets)
            {
                var message = "M-SEARCH * HTTP/1.1\r\n" +
                              $"HOST: {_target}\r\n" +
                              "MAN: \"ssdp:discover\"\r\n" +
                              "MX: 1\r\n" +
                              $"ST: {searchTarget}\r\n" +
                              "\r\n";
                try
                {
                    await _socket.SendToAsync(Encoding.ASCII.GetBytes(message), SocketFlags.None, _target, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // Nothing to do about a send that fails; the next round may go through.
                }
            }
        }

        /// <summary>The LOCATION of a "200 OK" answer for one of our search targets, or null.</summary>
        internal static Uri? ParseResponse(ReadOnlySpan<byte> datagram)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(datagram);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            var lines = text.Split('\n');
            if (lines.Length < 2 || lines.Length > 64) return null;
            var status = lines[0].TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (status.Length < 2 || !status[0].StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase) || status[1] != "200")
                return null;

            string? location = null, searchTarget = null;
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.TrimEnd('\r');
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) location ??= value;
                else if (name.Equals("ST", StringComparison.OrdinalIgnoreCase)) searchTarget ??= value;
            }

            if (searchTarget is null || !SearchTargets.Contains(searchTarget, StringComparer.Ordinal)) return null;
            if (string.IsNullOrEmpty(location) || location.Length > MaximumUrlLength) return null;
            return Uri.TryCreate(location, UriKind.Absolute, out var url) ? url : null;
        }
    }
}
