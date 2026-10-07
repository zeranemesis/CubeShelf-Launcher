using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using CubeShelf.Core.Social.Mesh;

/// <summary>
/// The router port mapper against fake routers on 127.0.0.1: a PCP / NAT-PMP daemon on a random
/// UDP port, an SSDP responder on another, and a small HTTP server for the UPnP description and
/// SOAP actions. Nothing here ever talks to the real gateway or multicasts on the real network:
/// every test sets the gateway, the SSDP target and the NAT-PMP port explicitly.
/// </summary>
static class PortMapperTests
{
    private static readonly IPAddress External = IPAddress.Parse("203.0.113.7");

    // ------------------------------------------------------------------ PCP and NAT-PMP

    public static void NatPmpMapsRenewsAndRemoves()
    {
        // A NAT-PMP-only router that, against the RFC, ignores PCP instead of answering
        // "unsupported version": the fallback must come after half the budget.
        using var router = FakeNatPmpRouter(answerPcp: false, assignedPort: 41000, maximumLifetime: 3600);
        var mapper = new PortMapper(LoopbackOptions(router.Port, closedSsdp: true, timeout: TimeSpan.FromSeconds(1)));

        var mapping = mapper.MapUdpAsync(50123, TimeSpan.FromHours(2)).GetAwaiter().GetResult();
        Check(mapping is not null, "NAT-PMP mapping failed: " + mapper.LastError);
        Check(mapping!.Protocol == PortMappingProtocol.NatPmp, "protocol");
        Check(External.Equals(mapping.ExternalAddress), "external address");
        Check(mapping.ExternalPort == 41000, "external port " + mapping.ExternalPort);
        Check(mapping.InternalPort == 50123, "internal port");
        Check(IPAddress.Loopback.Equals(mapping.InternalAddress), "internal address");
        Check(mapping.Lifetime == TimeSpan.FromSeconds(3600), "lifetime " + mapping.Lifetime);
        Check(mapper.LastError is null, "no error after a success");
        Check(router.Received.Any(packet => packet[0] == 2), "PCP was tried first");

        var maps = MapRequests(router);
        Check(maps.Count == 1 && maps[0].Internal == 50123 && maps[0].Suggested == 50123 && maps[0].Lifetime == 7200, "map request");

        var renewed = mapper.RenewAsync(mapping).GetAwaiter().GetResult();
        Check(renewed is { Protocol: PortMappingProtocol.NatPmp, ExternalPort: 41000 }, "renewed: " + mapper.LastError);
        var renewal = MapRequests(router).Last();
        Check(renewal.Suggested == 41000 && renewal.Lifetime == 3600, "renewal asks for the same external port");

        mapper.RemoveAsync(renewed!).GetAwaiter().GetResult();
        var removal = MapRequests(router).Last();
        Check(removal.Internal == 50123 && removal.Lifetime == 0 && removal.Suggested == 0, "removal sends lifetime 0 and port 0");
        Check(mapper.LastError is null, "removal confirmed: " + mapper.LastError);
    }

    public static void PcpMapsAndIgnoresForgeries()
    {
        var requests = new ConcurrentQueue<byte[]>();
        using var router = new FakeUdpServer((data, from, socket) =>
        {
            if (data.Length != 60 || data[0] != 2 || data[1] != 1) return;
            requests.Enqueue(data);
            var lifetime = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            var granted = Math.Min(lifetime, 1800u);
            var port = lifetime == 0 ? (ushort)0 : (ushort)42000;

            // Junk first: truncated, oversized, a wrong nonce, and the right nonce from the
            // wrong port. Only the last datagram is the gateway's genuine answer.
            socket.Send(new byte[] { 2, 0x81, 0 }, from);
            var oversized = PcpResponse(data, 0, granted, 666, IPAddress.Parse("198.51.100.66"));
            Array.Resize(ref oversized, 1200);
            socket.Send(oversized, from);
            var wrongNonce = PcpResponse(data, 0, granted, 666, IPAddress.Parse("198.51.100.66"));
            wrongNonce[24] ^= 0xFF;
            socket.Send(wrongNonce, from);
            using (var impostor = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                impostor.Send(PcpResponse(data, 0, granted, 667, IPAddress.Parse("198.51.100.67")), from);
            socket.Send(PcpResponse(data, 0, granted, port, lifetime == 0 ? IPAddress.Any : IPAddress.Parse("198.51.100.9")), from);
        });
        var mapper = new PortMapper(LoopbackOptions(router.Port, closedSsdp: true, timeout: TimeSpan.FromSeconds(2)));

        var mapping = mapper.MapUdpAsync(50124, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is not null, "PCP mapping failed: " + mapper.LastError);
        Check(mapping!.Protocol == PortMappingProtocol.Pcp, "protocol");
        Check(mapping.ExternalPort == 42000, "forgeries were ignored, port " + mapping.ExternalPort);
        Check(IPAddress.Parse("198.51.100.9").Equals(mapping.ExternalAddress), "external address " + mapping.ExternalAddress);
        Check(mapping.Lifetime == TimeSpan.FromSeconds(1800), "lifetime");
        Check(IPAddress.Loopback.Equals(mapping.InternalAddress), "internal address");

        var first = requests.First();
        Check(first.AsSpan(8, 16).SequenceEqual(MappedIPv4(IPAddress.Loopback)), "client address is IPv4-mapped");
        Check(first[36] == 17, "UDP");
        Check(BinaryPrimitives.ReadUInt16BigEndian(first.AsSpan(40)) == 50124, "internal port");
        Check(first.AsSpan(44, 16).SequenceEqual(MappedIPv4(IPAddress.Any)), "no preferred external address");
        Check(!router.Received.Any(packet => packet[0] == 0), "NAT-PMP never asked");
        var nonce = first.AsSpan(24, 12).ToArray();

        var renewed = mapper.RenewAsync(mapping).GetAwaiter().GetResult();
        Check(renewed is { Protocol: PortMappingProtocol.Pcp, ExternalPort: 42000 }, "renewed: " + mapper.LastError);
        var renewal = requests.Last();
        Check(renewal.AsSpan(24, 12).SequenceEqual(nonce), "renewal keeps the nonce");
        Check(BinaryPrimitives.ReadUInt16BigEndian(renewal.AsSpan(42)) == 42000, "renewal suggests the same port");
        Check(renewal.AsSpan(44, 16).SequenceEqual(MappedIPv4(IPAddress.Parse("198.51.100.9"))), "renewal suggests the same address");

        mapper.RemoveAsync(renewed!).GetAwaiter().GetResult();
        var removal = requests.Last();
        Check(BinaryPrimitives.ReadUInt32BigEndian(removal.AsSpan(4)) == 0, "removal: lifetime 0");
        Check(removal.AsSpan(24, 12).SequenceEqual(nonce), "removal: same nonce");
        Check(mapper.LastError is null, "removal confirmed: " + mapper.LastError);
    }

    public static void PcpUnsupportedVersionFallsBackToNatPmp()
    {
        using var router = FakeNatPmpRouter(answerPcp: true, assignedPort: 43000, maximumLifetime: 7200);
        var mapper = new PortMapper(LoopbackOptions(router.Port, closedSsdp: true, timeout: TimeSpan.FromSeconds(2)));

        var clock = Stopwatch.StartNew();
        var mapping = mapper.MapUdpAsync(50125, TimeSpan.FromMinutes(30)).GetAwaiter().GetResult();
        clock.Stop();
        Check(mapping is { Protocol: PortMappingProtocol.NatPmp, ExternalPort: 43000 }, "fell back to NAT-PMP: " + mapper.LastError);
        Check(mapping!.Lifetime == TimeSpan.FromMinutes(30), "lifetime");
        // Silence would have cost half the 2 s budget; "unsupported version" costs a round trip.
        Check(clock.Elapsed < TimeSpan.FromMilliseconds(900), "no wait for silence: " + clock.Elapsed);
    }

    // ------------------------------------------------------------------ UPnP

    public static void UpnpMapsReadsAddressAndRemoves()
    {
        using var router = new FakeUpnpRouter();
        var mapper = new PortMapper(router.Options());

        var mapping = mapper.MapUdpAsync(50200, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is not null, "UPnP mapping failed: " + mapper.LastError);
        Check(mapping!.Protocol == PortMappingProtocol.Upnp, "protocol");
        Check(mapping.ExternalPort == 50200 && mapping.InternalPort == 50200, "ports");
        Check(IPAddress.Parse("203.0.113.50").Equals(mapping.ExternalAddress), "external address " + mapping.ExternalAddress);
        Check(IPAddress.Loopback.Equals(mapping.InternalAddress), "internal address");
        Check(mapping.Lifetime == TimeSpan.FromHours(1), "lifetime");

        var search = router.Searches.FirstOrDefault() ?? "";
        Check(search.StartsWith("M-SEARCH * HTTP/1.1\r\n", StringComparison.Ordinal) && search.Contains("MAN: \"ssdp:discover\"") &&
              router.Searches.Any(text => text.Contains("ST: urn:schemas-upnp-org:service:WANIPConnection:1")), "M-SEARCH");

        Check(router.Http.Requests.First().Method == "GET" && router.Http.Requests.First().Path == "/desc.xml", "description fetched");
        var add = router.Soap("AddPortMapping").Single();
        Check(add.Headers["soapaction"] == "\"urn:schemas-upnp-org:service:WANIPConnection:1#AddPortMapping\"", "SOAPAction");
        Check(add.Path == "/ctl/IPConn", "the IP connection service, not the PPP one");
        var arguments = Arguments(add.Body);
        Check(arguments["NewRemoteHost"] == "" && arguments["NewExternalPort"] == "50200" && arguments["NewProtocol"] == "UDP" &&
              arguments["NewInternalPort"] == "50200" && arguments["NewInternalClient"] == "127.0.0.1" &&
              arguments["NewEnabled"] == "1" && arguments["NewLeaseDuration"] == "3600" &&
              arguments["NewPortMappingDescription"] == "CubeShelf &amp; co", "AddPortMapping arguments: " + add.Body);
        Check(ArgumentOrder(add.Body) == "NewRemoteHost,NewExternalPort,NewProtocol,NewInternalPort,NewInternalClient,NewEnabled,NewPortMappingDescription,NewLeaseDuration",
            "arguments in the order of the specification");
        Check(router.Soap("GetExternalIPAddress").Count == 1, "external address asked");

        var renewed = mapper.RenewAsync(mapping).GetAwaiter().GetResult();
        Check(renewed is { Protocol: PortMappingProtocol.Upnp, ExternalPort: 50200 }, "renewed: " + mapper.LastError);
        Check(router.Http.Requests.Count(request => request.Method == "GET") == 1, "renewal reuses the control URL");

        mapper.RemoveAsync(renewed!).GetAwaiter().GetResult();
        var delete = router.Soap("DeletePortMapping").Single();
        var deleted = Arguments(delete.Body);
        Check(deleted["NewRemoteHost"] == "" && deleted["NewExternalPort"] == "50200" && deleted["NewProtocol"] == "UDP", "DeletePortMapping arguments");
        Check(mapper.LastError is null, "removal confirmed: " + mapper.LastError);
    }

    public static void UpnpRenewalFollowsARestartedRouter()
    {
        using var router = new FakeUpnpRouter();
        var mapper = new PortMapper(router.Options());

        var mapping = mapper.MapUdpAsync(50250, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is { Protocol: PortMappingProtocol.Upnp, ExternalPort: 50250 }, "mapped: " + mapper.LastError);

        // The router restarts and serves its description and control URL on another port.
        router.MoveHttp();
        var renewed = mapper.RenewAsync(mapping!).GetAwaiter().GetResult();
        Check(renewed is { Protocol: PortMappingProtocol.Upnp, ExternalPort: 50250 }, "renewed after the move: " + mapper.LastError);
        Check(router.Http.Requests.Any(request => request.Method == "GET" && request.Path == "/desc.xml"), "rediscovered");
        Check(router.Soap("AddPortMapping").Count == 1, "mapped again at the new control URL");

        mapper.RemoveAsync(renewed!).GetAwaiter().GetResult();
        Check(router.Soap("DeletePortMapping").Count == 1 && mapper.LastError is null, "removed: " + mapper.LastError);
    }

    public static void UpnpConflictTriesAnotherPort()
    {
        using var router = new FakeUpnpRouter { TakenPorts = { 50300, 50301 } };
        var mapper = new PortMapper(router.Options());

        var mapping = mapper.MapUdpAsync(50300, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is { Protocol: PortMappingProtocol.Upnp, ExternalPort: 50302, InternalPort: 50300 }, "third port: " + mapper.LastError);
        var tried = router.Soap("AddPortMapping").Select(request => Arguments(request.Body)["NewExternalPort"]).ToArray();
        Check(string.Join(",", tried) == "50300,50301,50302", "ports tried " + string.Join(",", tried));
        Check(router.Soap("AddPortMapping").All(request => Arguments(request.Body)["NewInternalPort"] == "50300"), "internal port unchanged");
    }

    public static void UpnpPermanentLeaseOnly()
    {
        using var router = new FakeUpnpRouter { PermanentOnly = true };
        var mapper = new PortMapper(router.Options());

        var mapping = mapper.MapUdpAsync(50400, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is { Protocol: PortMappingProtocol.Upnp, ExternalPort: 50400 }, "mapped: " + mapper.LastError);
        Check(mapping!.Lifetime == TimeSpan.Zero && mapping.RenewAt is null, "permanent");
        var leases = router.Soap("AddPortMapping").Select(request => Arguments(request.Body)["NewLeaseDuration"]).ToArray();
        Check(string.Join(",", leases) == "3600,0", "lease 0 after 725: " + string.Join(",", leases));
    }

    public static void UpnpRejectsForeignLocations()
    {
        // Every LOCATION here would have us fetch something other than the router that answered.
        using var router = new FakeUpnpRouter();
        router.Locations = new[]
        {
            $"http://127.0.0.2:{router.Http.Port}/desc.xml",
            "http://203.0.113.5/desc.xml",
            $"http://localhost:{router.Http.Port}/desc.xml",
            $"https://127.0.0.1:{router.Http.Port}/desc.xml",
            $"http://admin@127.0.0.1:{router.Http.Port}/desc.xml",
            $"file:///C:/Windows/win.ini",
            "not a url at all"
        };
        var mapper = new PortMapper(router.Options(TimeSpan.FromMilliseconds(600)));

        var mapping = mapper.MapUdpAsync(50500, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is null, "no mapping");
        Check(router.Http.Requests.IsEmpty, "nothing fetched");
        Check(!router.Searches.IsEmpty, "the search did happen");
        Check(mapper.LastError?.Contains("UPnP") == true, "said why: " + mapper.LastError);
    }

    public static void UpnpRejectsForeignControlUrls()
    {
        foreach (var (urlBase, control) in new (string?, string)[]
                 {
                     (null, "http://127.0.0.2:{port}/ctl/IPConn"),
                     (null, "//203.0.113.5/ctl/IPConn"),
                     (null, "https://127.0.0.1:{port}/ctl/IPConn"),
                     (null, "http://user@127.0.0.1:{port}/ctl/IPConn"),
                     ("http://203.0.113.5:80/", "/ctl/IPConn"),
                     ("http://127.0.0.2:{port}/", "ctl/IPConn")
                 })
        {
            using var router = new FakeUpnpRouter();
            router.Description = port =>
            {
                var hostile = control.Replace("{port}", port.ToString());
                return DeviceDescription(hostile, urlBase?.Replace("{port}", port.ToString()), pppControlUrl: hostile);
            };
            var mapper = new PortMapper(router.Options(TimeSpan.FromMilliseconds(600)));

            var mapping = mapper.MapUdpAsync(50600, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
            Check(mapping is null, "no mapping with " + control);
            Check(router.Http.Requests.Count == 1 && router.Http.Requests.All(request => request.Method == "GET"), "only the description was fetched with " + control);
        }
    }

    public static void UpnpRejectsHostileDescriptions()
    {
        var bomb = "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">" +
                   "<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\">]><root>&lol3;</root>";
        var padding = new string(' ', 70 * 1024);
        var scenarios = new (string Name, Func<int, FakeHttpServer.Response> Respond)[]
        {
            ("entity bomb", _ => FakeHttpServer.Response.Xml(bomb)),
            ("external entity", port => FakeHttpServer.Response.Xml(
                $"<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY xxe SYSTEM \"http://127.0.0.1:{port}/xxe\">]><root>&xxe;</root>")),
            ("DTD in front of a valid description", port => FakeHttpServer.Response.Xml(
                DeviceDescription("/ctl/IPConn").Replace("<?xml version=\"1.0\"?>", "<?xml version=\"1.0\"?><!DOCTYPE root [<!ELEMENT root ANY>]>"))),
            ("oversized, with a length", _ => FakeHttpServer.Response.Xml(DeviceDescription("/ctl/IPConn").Replace("<device>", "<device>" + padding))),
            ("oversized, without a length", _ => FakeHttpServer.Response.Xml(DeviceDescription("/ctl/IPConn").Replace("<device>", "<device>" + padding)) with { OmitContentLength = true }),
            ("garbage", _ => new FakeHttpServer.Response(200, Enumerable.Range(0, 3000).Select(i => (byte)(i * 7919 % 251)).ToArray())),
            ("truncated", _ => FakeHttpServer.Response.Xml(DeviceDescription("/ctl/IPConn")[..400])),
            ("empty", _ => new FakeHttpServer.Response(200, Array.Empty<byte>())),
            ("redirect", port => new FakeHttpServer.Response(302, Array.Empty<byte>(), ExtraHeaders: $"Location: http://127.0.0.1:{port}/elsewhere.xml\r\n")),
            ("server error", _ => new FakeHttpServer.Response(500, Encoding.UTF8.GetBytes("no")))
        };

        foreach (var (name, respond) in scenarios)
        {
            using var router = new FakeUpnpRouter();
            router.DescriptionResponse = respond;
            var mapper = new PortMapper(router.Options(TimeSpan.FromMilliseconds(600)));

            PortMapping? mapping;
            try
            {
                mapping = mapper.MapUdpAsync(50700, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"{name}: threw {exception.GetType().Name}: {exception.Message}");
            }
            Check(mapping is null, name + ": no mapping");
            Check(router.Http.Requests.All(request => request.Method == "GET" && request.Path == "/desc.xml"), name + ": nothing else fetched, nothing posted");
            Check(!string.IsNullOrEmpty(mapper.LastError), name + ": said why");
        }
    }

    public static void UpnpRejectsHostileSoapAnswers()
    {
        // The mapping is made, but the address the router claims is not one.
        foreach (var claimed in new[] { "1.2.3", "300.1.1.1", "::1", "0.0.0.0", "127.0.0.1", "224.0.0.1", "0x7f.0.0.1", "203.0.113.50 ; rm" })
        {
            using var router = new FakeUpnpRouter { ExternalAddress = claimed };
            var mapper = new PortMapper(router.Options(TimeSpan.FromMilliseconds(600)));
            var mapping = mapper.MapUdpAsync(50800, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
            Check(mapping is { Protocol: PortMappingProtocol.Upnp, ExternalAddress: null }, "address refused: " + claimed);
        }

        // AddPortMapping answered with a DTD, with garbage, with too much: not a success.
        foreach (var answer in new Func<FakeHttpServer.Response>[]
                 {
                     () => FakeHttpServer.Response.Xml("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"b\">]><x>&a;</x>"),
                     () => new FakeHttpServer.Response(200, new byte[] { 0xFF, 0xFE, 0x00, 0x3C }),
                     () => FakeHttpServer.Response.Xml("<x>" + new string('a', 70 * 1024) + "</x>"),
                     () => FakeHttpServer.Response.Xml("<x>" + new string('a', 70 * 1024) + "</x>") with { OmitContentLength = true }
                 })
        {
            using var router = new FakeUpnpRouter { AddAnswer = answer };
            var mapper = new PortMapper(router.Options(TimeSpan.FromMilliseconds(600)));
            var mapping = mapper.MapUdpAsync(50801, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
            Check(mapping is null, "a hostile AddPortMapping answer is not a mapping");
        }
    }

    // ------------------------------------------------------------------ silence and refusal

    public static void NothingAnswersWithinTheBudget()
    {
        using var natPmp = new FakeUdpServer(null);
        using var ssdp = new FakeUdpServer(null);
        var mapper = new PortMapper(new PortMapperOptions(
            Gateway: IPAddress.Loopback,
            SsdpTarget: new IPEndPoint(IPAddress.Loopback, ssdp.Port),
            NatPmpPort: natPmp.Port,
            AllowLoopbackForTests: true));

        var clock = Stopwatch.StartNew();
        var mapping = mapper.MapUdpAsync(50900, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        clock.Stop();

        Check(mapping is null, "no mapping");
        Check(clock.Elapsed < TimeSpan.FromSeconds(6), "within the default budget: " + clock.Elapsed);
        Check(natPmp.Received.Any(packet => packet[0] == 2) && natPmp.Received.Any(packet => packet[0] == 0), "PCP then NAT-PMP were tried");
        Check(natPmp.Received.Count(packet => packet[0] == 2) >= 2, "PCP was retransmitted");
        Check(ssdp.Received.Any(), "UPnP was tried");
        Check(mapper.LastError?.Contains("UPnP") == true && mapper.LastError.Contains("NAT-PMP"), "said why: " + mapper.LastError);
    }

    public static void LoopbackIsNotTheNetworkOutsideTests()
    {
        using var router = FakeNatPmpRouter(answerPcp: true, assignedPort: 44000, maximumLifetime: 3600);
        using var ssdp = new FakeUdpServer(null);
        var mapper = new PortMapper(new PortMapperOptions(
            Gateway: IPAddress.Loopback,
            SsdpTarget: new IPEndPoint(IPAddress.Loopback, ssdp.Port),
            NatPmpPort: router.Port,
            Timeout: TimeSpan.FromMilliseconds(500)));

        var mapping = mapper.MapUdpAsync(51000, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        Check(mapping is null, "no mapping");
        Thread.Sleep(100);
        Check(router.Received.IsEmpty && ssdp.Received.IsEmpty, "nothing sent at all");
        Check(mapper.LastError?.Contains("127.0.0.1") == true, "said why: " + mapper.LastError);
    }

    public static void CancellationIsHonoured()
    {
        using var natPmp = new FakeUdpServer(null);
        using var ssdp = new FakeUdpServer(null);
        var mapper = new PortMapper(new PortMapperOptions(
            Gateway: IPAddress.Loopback,
            SsdpTarget: new IPEndPoint(IPAddress.Loopback, ssdp.Port),
            NatPmpPort: natPmp.Port,
            AllowLoopbackForTests: true));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        var cancelled = false;
        try
        {
            mapper.MapUdpAsync(51100, TimeSpan.FromHours(1), cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check(cancelled, "the caller's cancellation goes through");
        Check(clock.Elapsed < TimeSpan.FromSeconds(1.5), "promptly: " + clock.Elapsed);
    }

    // ------------------------------------------------------------------ helpers

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    private static int ClosedUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static PortMapperOptions LoopbackOptions(int natPmpPort, bool closedSsdp, TimeSpan timeout) => new(
        Gateway: IPAddress.Loopback,
        SsdpTarget: new IPEndPoint(IPAddress.Loopback, closedSsdp ? ClosedUdpPort() : 0),
        NatPmpPort: natPmpPort,
        AllowLoopbackForTests: true,
        Timeout: timeout);

    private static byte[] MappedIPv4(IPAddress address)
    {
        var field = new byte[16];
        field[10] = field[11] = 0xFF;
        address.GetAddressBytes().CopyTo(field, 12);
        return field;
    }

    private static byte[] PcpResponse(byte[] request, byte result, uint lifetime, ushort externalPort, IPAddress externalAddress)
    {
        var response = new byte[60];
        response[0] = 2;
        response[1] = 0x81;
        response[3] = result;
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(4), lifetime);
        BinaryPrimitives.WriteUInt32BigEndian(response.AsSpan(8), 1234);
        request.AsSpan(24, 18).CopyTo(response.AsSpan(24)); // nonce, protocol, reserved, internal port
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(42), externalPort);
        MappedIPv4(externalAddress).CopyTo(response, 44);
        return response;
    }

    private sealed record NatPmpMapRequest(int Internal, int Suggested, uint Lifetime);

    private static List<NatPmpMapRequest> MapRequests(FakeUdpServer router) => router.Received
        .Where(packet => packet.Length == 12 && packet[0] == 0 && packet[1] == 1)
        .Select(packet => new NatPmpMapRequest(
            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)),
            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6)),
            BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8))))
        .ToList();

    /// <summary>A NAT-PMP daemon; for PCP it either stays silent or answers "unsupported version" as RFC 6886 asks.</summary>
    private static FakeUdpServer FakeNatPmpRouter(bool answerPcp, ushort assignedPort, uint maximumLifetime) => new((data, from, socket) =>
    {
        if (data.Length >= 1 && data[0] != 0)
        {
            if (answerPcp) socket.Send(new byte[] { 0, 0x81, 0, 1, 0, 0, 0, 42 }, from);
            return;
        }
        if (data.Length == 2 && data[1] == 0)
        {
            var reply = new byte[12];
            reply[1] = 128;
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(4), 42);
            External.GetAddressBytes().CopyTo(reply, 8);
            socket.Send(reply, from);
            return;
        }
        if (data.Length == 12 && data[1] == 1)
        {
            var lifetime = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
            var reply = new byte[16];
            reply[1] = 129;
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(4), 42);
            data.AsSpan(4, 2).CopyTo(reply.AsSpan(8));
            BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(10), lifetime == 0 ? (ushort)0 : assignedPort);
            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12), Math.Min(lifetime, maximumLifetime));

            // The same answer with another port, from another socket: must be ignored.
            var forged = (byte[])reply.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(forged.AsSpan(10), 666);
            using (var impostor = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                impostor.Send(forged, from);
            socket.Send(reply, from);
        }
    });

    private static Dictionary<string, string> Arguments(string body) =>
        Regex.Matches(body, "<(New[A-Za-z]+)>([^<]*)</\\1>")
            .GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups[2].Value);

    private static string ArgumentOrder(string body) =>
        string.Join(",", Regex.Matches(body, "<(New[A-Za-z]+)>").Select(match => match.Groups[1].Value));

    private static string DeviceDescription(string controlUrl, string? urlBase = null, string pppControlUrl = "/ctl/PPPConn") =>
        "<?xml version=\"1.0\"?>" +
        "<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><specVersion><major>1</major><minor>0</minor></specVersion>" +
        (urlBase is null ? "" : $"<URLBase>{urlBase}</URLBase>") +
        "<device><deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType><friendlyName>Fake</friendlyName>" +
        "<serviceList><service><serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType>" +
        "<serviceId>urn:upnp-org:serviceId:L3Forwarding1</serviceId><controlURL>/ctl/L3F</controlURL><eventSubURL>/evt/L3F</eventSubURL><SCPDURL>/L3F.xml</SCPDURL></service></serviceList>" +
        "<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>" +
        "<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType><serviceList>" +
        "<service><serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType><serviceId>urn:upnp-org:serviceId:WANPPPConn1</serviceId>" +
        $"<controlURL>{pppControlUrl}</controlURL><eventSubURL>/evt/PPPConn</eventSubURL><SCPDURL>/PPPConn.xml</SCPDURL></service>" +
        "<service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><serviceId>urn:upnp-org:serviceId:WANIPConn1</serviceId>" +
        $"<controlURL>{controlUrl}</controlURL><eventSubURL>/evt/IPConn</eventSubURL><SCPDURL>/IPConn.xml</SCPDURL></service>" +
        "</serviceList></device></deviceList></device></deviceList></device></root>";

    private static string SoapFault(int code, string description) =>
        "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
        "<s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail>" +
        $"<UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>{code}</errorCode><errorDescription>{description}</errorDescription></UPnPError>" +
        "</detail></s:Fault></s:Body></s:Envelope>";

    private static string SoapResponse(string action, string inner) =>
        "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
        $"<s:Body><u:{action}Response xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">{inner}</u:{action}Response></s:Body></s:Envelope>";

    // ------------------------------------------------------------------ fakes

    /// <summary>A UDP socket on 127.0.0.1 that records every datagram and hands it to a handler (none: silent).</summary>
    private sealed class FakeUdpServer : IDisposable
    {
        private readonly UdpClient _client;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public FakeUdpServer(Action<byte[], IPEndPoint, UdpClient>? handler)
        {
            _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            if (OperatingSystem.IsWindows())
                _client.Client.IOControl(unchecked((int)0x9800000C), new byte[4], null);
            Port = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await _client.ReceiveAsync(_stop.Token);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                    catch (SocketException) { continue; }

                    Received.Enqueue(received.Buffer);
                    try
                    {
                        handler?.Invoke(received.Buffer, received.RemoteEndPoint, _client);
                    }
                    catch (SocketException)
                    {
                    }
                }
            });
        }

        public int Port { get; }
        public ConcurrentQueue<byte[]> Received { get; } = new();

        public void Dispose()
        {
            _stop.Cancel();
            _client.Dispose();
            try { _loop.Wait(1000); } catch (AggregateException) { }
        }
    }

    /// <summary>Just enough HTTP/1.1 on 127.0.0.1 for a router's description and SOAP control: one request per connection.</summary>
    private sealed class FakeHttpServer : IDisposable
    {
        public sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);

        public sealed record Response(int Status, byte[] Body, bool OmitContentLength = false, string ExtraHeaders = "")
        {
            public static Response Xml(string text, int status = 200) => new(status, Encoding.UTF8.GetBytes(text));
        }

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Func<Request, Response> _handler;
        private readonly CancellationTokenSource _stop = new();

        public FakeHttpServer(Func<Request, Response> handler)
        {
            _handler = handler;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }
        public ConcurrentQueue<Request> Requests { get; } = new();

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception) { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new List<byte>();
                    var chunk = new byte[4096];
                    int headerEnd;
                    while ((headerEnd = IndexOfHeaderEnd(buffer)) < 0)
                    {
                        var read = await stream.ReadAsync(chunk, _stop.Token);
                        if (read == 0 || buffer.Count > 65536) return;
                        buffer.AddRange(chunk.AsSpan(0, read).ToArray());
                    }

                    var head = Encoding.ASCII.GetString(buffer.GetRange(0, headerEnd).ToArray()).Split("\r\n");
                    var requestLine = head[0].Split(' ');
                    var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var line in head.Skip(1))
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
                    }
                    var length = headers.TryGetValue("content-length", out var text) ? int.Parse(text) : 0;
                    var body = buffer.Skip(headerEnd + 4).ToList();
                    while (body.Count < length)
                    {
                        var read = await stream.ReadAsync(chunk, _stop.Token);
                        if (read == 0) break;
                        body.AddRange(chunk.AsSpan(0, read).ToArray());
                    }

                    var request = new Request(requestLine[0], requestLine.Length > 1 ? requestLine[1] : "", headers, Encoding.UTF8.GetString(body.ToArray()));
                    Requests.Enqueue(request);
                    var response = _handler(request);

                    var header = new StringBuilder()
                        .Append("HTTP/1.1 ").Append(response.Status).Append(" Fake\r\n")
                        .Append("Content-Type: text/xml; charset=\"utf-8\"\r\n")
                        .Append("Connection: close\r\n")
                        .Append(response.ExtraHeaders);
                    if (!response.OmitContentLength) header.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
                    header.Append("\r\n");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), _stop.Token);
                    await stream.WriteAsync(response.Body, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException or FormatException)
                {
                }
            }
        }

        private static int IndexOfHeaderEnd(List<byte> buffer)
        {
            for (var i = 0; i + 3 < buffer.Count; i++)
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return i;
            return -1;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    /// <summary>
    /// A UPnP IGD on 127.0.0.1: an SSDP responder, the description and the WANIPConnection
    /// control URL. Its PCP / NAT-PMP port is closed, so the mapper comes to UPnP at once.
    /// </summary>
    private sealed class FakeUpnpRouter : IDisposable
    {
        private readonly FakeUdpServer _ssdp;

        public FakeUpnpRouter()
        {
            Http = new FakeHttpServer(Handle);
            Locations = new[] { $"http://127.0.0.1:{Http.Port}/desc.xml" };
            _ssdp = new FakeUdpServer((data, from, socket) =>
            {
                var text = Encoding.ASCII.GetString(data);
                Searches.Enqueue(text);
                if (!text.StartsWith("M-SEARCH", StringComparison.Ordinal)) return;
                var searchTarget = Regex.Match(text, "\r\nST: ([^\r]*)\r\n").Groups[1].Value;
                foreach (var location in Locations)
                {
                    var answer = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\n" +
                                 $"ST: {searchTarget}\r\nUSN: uuid:fake-igd::{searchTarget}\r\nEXT:\r\n" +
                                 "SERVER: Fake/1.0 UPnP/1.1 FakeIGD/1.0\r\n" +
                                 $"LOCATION: {location}\r\n\r\n";
                    socket.Send(Encoding.ASCII.GetBytes(answer), from);
                }
            });
        }

        public FakeHttpServer Http { get; private set; }
        public ConcurrentQueue<string> Searches { get; } = new();
        public string[] Locations { get; set; }
        public Func<int, string> Description { get; set; } = _ => DeviceDescription("/ctl/IPConn");
        public Func<int, FakeHttpServer.Response>? DescriptionResponse { get; set; }
        public HashSet<int> TakenPorts { get; } = new();
        public bool PermanentOnly { get; set; }
        public string ExternalAddress { get; set; } = "203.0.113.50";
        public Func<FakeHttpServer.Response>? AddAnswer { get; set; }

        /// <summary>What miniupnpd does on a restart: the same router, its HTTP server on a new port.</summary>
        public void MoveHttp()
        {
            var previous = Http;
            Http = new FakeHttpServer(Handle);
            Locations = new[] { $"http://127.0.0.1:{Http.Port}/desc.xml" };
            previous.Dispose();
        }

        public PortMapperOptions Options(TimeSpan? timeout = null) => new(
            Gateway: IPAddress.Loopback,
            SsdpTarget: new IPEndPoint(IPAddress.Loopback, _ssdp.Port),
            NatPmpPort: ClosedUdpPort(),
            AllowLoopbackForTests: true,
            Timeout: timeout ?? TimeSpan.FromSeconds(1),
            Description: "CubeShelf & co\u0007");

        public List<FakeHttpServer.Request> Soap(string action) => Http.Requests
            .Where(request => request.Method == "POST" && request.Headers.TryGetValue("soapaction", out var value) && value.EndsWith("#" + action + "\""))
            .ToList();

        private FakeHttpServer.Response Handle(FakeHttpServer.Request request)
        {
            if (request.Method == "GET" && request.Path == "/desc.xml")
                return DescriptionResponse?.Invoke(Http.Port) ?? FakeHttpServer.Response.Xml(Description(Http.Port));
            if (request.Method != "POST" || request.Path != "/ctl/IPConn")
                return new FakeHttpServer.Response(404, Array.Empty<byte>());

            var action = request.Headers.TryGetValue("soapaction", out var value) ? value.Trim('"').Split('#').Last() : "";
            var arguments = Arguments(request.Body);
            switch (action)
            {
                case "AddPortMapping":
                    if (AddAnswer is not null) return AddAnswer();
                    if (TakenPorts.Contains(int.Parse(arguments["NewExternalPort"])))
                        return FakeHttpServer.Response.Xml(SoapFault(718, "ConflictInMappingEntry"), 500);
                    if (PermanentOnly && arguments["NewLeaseDuration"] != "0")
                        return FakeHttpServer.Response.Xml(SoapFault(725, "OnlyPermanentLeasesSupported"), 500);
                    return FakeHttpServer.Response.Xml(SoapResponse(action, ""));
                case "GetExternalIPAddress":
                    return FakeHttpServer.Response.Xml(SoapResponse(action, $"<NewExternalIPAddress>{ExternalAddress}</NewExternalIPAddress>"));
                case "DeletePortMapping":
                    return FakeHttpServer.Response.Xml(SoapResponse(action, ""));
                default:
                    return FakeHttpServer.Response.Xml(SoapFault(401, "Invalid Action"), 500);
            }
        }

        public void Dispose()
        {
            _ssdp.Dispose();
            Http.Dispose();
        }
    }
}
