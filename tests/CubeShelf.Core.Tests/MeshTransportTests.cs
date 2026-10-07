using System.Net;
using System.Security.Cryptography;
using CubeShelf.Core.Social.Mesh;
using static MeshKit;

static class MeshTransportTests
{
    public static void NoiseHandshakeAgreesAndRejectsTampering()
    {
        using var alice = MeshCrypto.NewAgreementKey();
        using var bob = MeshCrypto.NewAgreementKey();

        void Run(Func<byte[], byte[]>? tamper2, Func<byte[], byte[]>? tamper3, bool expectSuccess)
        {
            using var initiator = NoiseHandshake.Initiator(alice, "p"u8);
            using var responder = NoiseHandshake.Responder(bob, "p"u8);
            var m1 = initiator.WriteMessage1("hello"u8);
            Check(responder.ReadMessage1(m1) is { } p1 && p1.AsSpan().SequenceEqual("hello"u8), "message 1 read");
            var m2 = responder.WriteMessage2("from bob"u8);
            if (tamper2 is not null) m2 = tamper2(m2);
            var p2 = initiator.ReadMessage2(m2);
            if (tamper2 is not null)
            {
                Check(p2 is null, "tampered message 2 refused");
                return;
            }
            Check(p2 is not null && p2.AsSpan().SequenceEqual("from bob"u8), "message 2 payload");
            var m3 = initiator.WriteMessage3("from alice"u8);
            if (tamper3 is not null) m3 = tamper3(m3);
            var p3 = responder.ReadMessage3(m3);
            if (!expectSuccess)
            {
                Check(p3 is null, "tampered message 3 refused");
                return;
            }

            Check(p3 is not null && p3.AsSpan().SequenceEqual("from alice"u8), "message 3 payload");
            Check(initiator.HandshakeHash.AsSpan().SequenceEqual(responder.HandshakeHash), "same transcript hash");
            Check(initiator.RemoteStatic!.AsSpan().SequenceEqual(MeshCrypto.PublicKeyOf(bob)), "initiator learnt bob");
            Check(responder.RemoteStatic!.AsSpan().SequenceEqual(MeshCrypto.PublicKeyOf(alice)), "responder learnt alice");
            var (iSend, iReceive) = initiator.Split();
            var (rSend, rReceive) = responder.Split();
            Check(iSend.AsSpan().SequenceEqual(rReceive) && iReceive.AsSpan().SequenceEqual(rSend), "keys cross over");
            Check(!iSend.AsSpan().SequenceEqual(iReceive), "two directions, two keys");
        }

        Run(null, null, true);
        Run(m => Flip(m, 70), null, false);   // inside the encrypted static key
        Run(m => Flip(m, m.Length - 1), null, false);
        Run(null, m => Flip(m, 3), false);
        Run(null, m => m[..^1], false);

        // A point that is not on the curve, in place of the responder's ephemeral key.
        using (var initiator = NoiseHandshake.Initiator(alice, "p"u8))
        using (var responder = NoiseHandshake.Responder(bob, "p"u8))
        {
            responder.ReadMessage1(initiator.WriteMessage1(ReadOnlySpan<byte>.Empty));
            var m2 = responder.WriteMessage2(ReadOnlySpan<byte>.Empty);
            m2[10] ^= 0x55;
            Check(initiator.ReadMessage2(m2) is null, "off-curve ephemeral refused");
        }
    }

    public static void TransportCarriesRequestsBothWays()
    {
        var network = new SimulatedNetwork();
        var a = Transport(network.BindPublic());
        var b = Transport(network.BindPublic(), MeshNodeFlags.Server);
        try
        {
            b.RequestHandler = (session, message, _) => Task.FromResult<byte[]?>(message.Body.Reverse().ToArray());
            var heard = new List<string>();
            b.NotificationReceived += (_, message) => { lock (heard) heard.Add(System.Text.Encoding.UTF8.GetString(message.Body)); };

            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)));
            Check(session is not null, "connected");
            Check(session!.RemoteId == b.LocalId, "a sees b's proven id");
            Check(session.RemoteFlags == MeshNodeFlags.Server, "flags travel");
            Check(Equals(session.ObservedSelf, a.LocalEndPoint), "b tells a how it looks from outside");

            var reply = Wait(a.RequestAsync(session, 0x10, new byte[] { 1, 2, 3 }));
            Check(reply is not null && reply.SequenceEqual(new byte[] { 3, 2, 1 }), "reply");

            a.Notify(session, 0x11, "ping"u8);
            Eventually(() => { lock (heard) return heard.Contains("ping"); }, "notification");
            Eventually(() => b.SessionWith(a.LocalId) is not null, "b registered a");

            // The other way, on the same session.
            a.RequestHandler = (_, message, _) => Task.FromResult<byte[]?>(new byte[] { (byte)(message.Body[0] + 1) });
            var back = Wait(b.RequestAsync(b.SessionWith(a.LocalId)!, 0x12, new byte[] { 41 }));
            Check(back is [42], "reply from the initiator side");

            // The same route twice is one session, not two.
            var again = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)));
            Check(ReferenceEquals(again, session), "session reused");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportSurvivesLossDuplicationAndLargeMessages()
    {
        var network = new SimulatedNetwork { Loss = 0.08, Duplication = 0.1, LatencyMilliseconds = 5 };
        var a = Transport(network.BindPublic());
        var b = Transport(network.BindPublic());
        try
        {
            var calls = 0;
            b.RequestHandler = (_, message, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<byte[]?>(SHA256.HashData(message.Body));
            };

            MeshSession? session = null;
            for (var attempt = 0; attempt < 5 && session is null; attempt++) session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)));
            Check(session is not null, "connected despite loss");

            var body = RandomBytes(120_000);
            byte[]? digest = null;
            for (var attempt = 0; attempt < 4 && digest is null; attempt++)
                digest = Wait(a.RequestAsync(session!, 0x20, body, timeout: TimeSpan.FromSeconds(4)));
            Check(digest is not null && digest.AsSpan().SequenceEqual(SHA256.HashData(body)), "120 KB arrived whole, in about a hundred fragments");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportDropsReplayedPackets()
    {
        var network = new SimulatedNetwork();
        var a = Transport(network.BindPublic());
        var b = Transport(network.BindPublic());
        try
        {
            var notifications = 0;
            b.NotificationReceived += (_, _) => Interlocked.Increment(ref notifications);
            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)))!;
            Eventually(() => session.Confirmed, "confirmed");

            // Capture the next transport packet on the wire, then play it again three times.
            byte[]? captured = null;
            network.Filter = (datagram, _, _) =>
            {
                if (captured is null && datagram[1] == (byte)MeshPacketType.Transport) captured = (byte[])datagram.Clone();
                return true;
            };
            a.Notify(session, 0x30, "once"u8);
            Eventually(() => notifications == 1, "first delivery");
            network.Filter = null;

            var dropped = b.PacketsDropped;
            var replayer = network.BindPublic(a.LocalEndPoint.Address, a.LocalEndPoint.Port + 1);
            for (var i = 0; i < 3; i++) replayer.Send(captured!, b.LocalEndPoint);
            Eventually(() => b.PacketsDropped >= dropped + 3, "replays counted as dropped");
            Thread.Sleep(100);
            Check(notifications == 1, "a replayed packet is never delivered twice");
            // Nor did the authenticated-but-replayed packet move the session to the replayer's address.
            Check(b.SessionWith(a.LocalId)!.Route.Endpoint!.Equals(a.LocalEndPoint), "the session did not follow the replay");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportShrugsOffGarbageAndNeverAmplifies()
    {
        var network = new SimulatedNetwork();
        var socket = network.BindPublic();
        var b = Transport(socket, MeshNodeFlags.Server);
        var a = Transport(network.BindPublic());
        var attacker = network.BindPublic();
        try
        {
            b.RequestHandler = (_, message, _) => Task.FromResult<byte[]?>(message.Body);

            // Real packets, captured to be mutated below.
            var seen = new List<byte[]>();
            network.Filter = (datagram, _, _) => { lock (seen) seen.Add((byte[])datagram.Clone()); return true; };
            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)))!;
            Check(Wait(a.RequestAsync(session, 0x40, new byte[] { 9 })) is [9], "baseline");
            network.Filter = null;

            var received = 0L;
            for (var i = 0; i < 3000; i++)
            {
                byte[] junk;
                if (i % 3 == 0)
                {
                    junk = RandomBytes(Random.Shared.Next(0, 1500));
                }
                else
                {
                    byte[] template;
                    lock (seen) template = seen[Random.Shared.Next(seen.Count)];
                    junk = (byte[])template.Clone();
                    var flips = Random.Shared.Next(1, 4);
                    for (var f = 0; f < flips && junk.Length > 0; f++) junk[Random.Shared.Next(junk.Length)] ^= (byte)Random.Shared.Next(1, 256);
                    if (i % 5 == 0 && junk.Length > 3) junk = junk[..Random.Shared.Next(junk.Length)];
                }
                received += junk.Length;
                attacker.Send(junk, b.LocalEndPoint);
            }

            // Still alive and serving.
            Check(Wait(a.RequestAsync(session, 0x40, new byte[] { 7 })) is [7], "still serving after 3000 hostile datagrams");
            long sentBack;
            lock (socket.SentTo) sentBack = socket.SentTo.GetValueOrDefault(attacker.LocalEndPoint);
            Check(sentBack <= received, $"never more bytes back than received ({sentBack} for {received})");

            // A well-formed first message from a forged address gets at most its own size back.
            var forged = network.BindPublic();
            var init = seen.First(packet => packet[1] == (byte)MeshPacketType.HandshakeInit);
            forged.Send(init, b.LocalEndPoint);
            Thread.Sleep(50);
            lock (socket.SentTo) Check(socket.SentTo.GetValueOrDefault(forged.LocalEndPoint) <= init.Length, "handshake reply no larger than the request");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportAsksForCookiesUnderLoad()
    {
        var network = new SimulatedNetwork();
        var server = Transport(network.BindPublic(), MeshNodeFlags.Server, new MeshTransportOptions { HandshakesPerSecondBeforeCookies = 1 });
        var clients = Enumerable.Range(0, 8).Select(_ => Transport(network.BindPublic())).ToArray();
        try
        {
            var cookies = 0;
            network.Filter = (datagram, _, _) =>
            {
                if (datagram[1] == (byte)MeshPacketType.Cookie) Interlocked.Increment(ref cookies);
                return true;
            };
            var sessions = Wait(Task.WhenAll(clients.Select(client => client.ConnectAsync(MeshRoute.Direct(server.LocalEndPoint)))));
            Check(sessions.All(session => session is not null), "every client got in, cookie or not");
            Check(cookies > 0, "cookies were asked for");
        }
        finally
        {
            server.DisposeAsync().AsTask().Wait();
            foreach (var client in clients) client.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportFailsPendingRequestsWhenThePeerLeaves()
    {
        var network = new SimulatedNetwork();
        var a = Transport(network.BindPublic());
        var b = Transport(network.BindPublic());
        try
        {
            b.RequestHandler = async (_, _, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return null;
            };
            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)))!;
            Eventually(() => session.Confirmed, "confirmed");
            var pending = a.RequestAsync(session, 0x50, new byte[] { 1 }, timeout: TimeSpan.FromSeconds(20));
            Thread.Sleep(100);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            b.DisposeAsync().AsTask().Wait();
            Check(Wait(pending, 5000) is null, "the request fails");
            Check(watch.ElapsedMilliseconds < 3000, "as soon as the peer says goodbye, not at the timeout");
            Check(session.Closed, "and the session is closed");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportRefusesToTalkToItself()
    {
        var network = new SimulatedNetwork();
        var a = Transport(network.BindPublic());
        try
        {
            Check(Wait(a.ConnectAsync(MeshRoute.Direct(a.LocalEndPoint))) is null, "no session with oneself");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
        }
    }

    public static void TransportWorksOverRealUdp()
    {
        var a = Transport(new UdpMeshSocket(0, IPAddress.Loopback));
        var b = Transport(new UdpMeshSocket(0, IPAddress.Loopback));
        try
        {
            b.RequestHandler = (_, message, _) => Task.FromResult<byte[]?>(message.Body);
            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)));
            Check(session is not null, "connected over loopback UDP");
            var body = RandomBytes(40_000);
            Check(Wait(a.RequestAsync(session!, 0x60, body)) is { } echo && echo.AsSpan().SequenceEqual(body), "40 KB echoed over real sockets");
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    public static void ProofOfWorkBindsTheId()
    {
        using var key = MeshCrypto.NewAgreementKey();
        var publicKey = MeshCrypto.PublicKeyOf(key);
        var nonce = NodeProof.Solve(publicKey, 12);
        Check(NodeProof.IsValid(publicKey, nonce, 12), "solved");
        using var other = MeshCrypto.NewAgreementKey();
        var otherKey = MeshCrypto.PublicKeyOf(other);
        // The same nonce on another key is worth nothing (with overwhelming probability at 12 bits... retry a few).
        var reused = Enumerable.Range(0, 8).Count(_ =>
        {
            using var k = MeshCrypto.NewAgreementKey();
            return NodeProof.IsValid(MeshCrypto.PublicKeyOf(k), nonce, 12);
        });
        Check(reused <= 1, "a nonce does not transfer to other keys");
        Check(NodeProof.IdOf(publicKey, nonce) != NodeProof.IdOf(otherKey, nonce), "ids differ");

        // Transports refuse to start with an unproven key.
        var network = new SimulatedNetwork();
        try
        {
            _ = new MeshTransport(network.BindPublic(), key, nonce ^ 0xFFFF, options: new MeshTransportOptions { ProofDifficulty = 20 });
            throw new InvalidOperationException("Échec : unproven key accepted");
        }
        catch (ArgumentException)
        {
        }
    }

    /// <summary>
    /// A peer that starts big messages and never finishes them holds at most its own budget of
    /// our memory, and gives all of it back when it goes.
    /// </summary>
    public static void HalfSentMessagesCannotExhaustMemory()
    {
        var network = new SimulatedNetwork();
        var a = Transport(network.BindPublic());
        var b = Transport(network.BindPublic());
        try
        {
            var session = Wait(a.ConnectAsync(MeshRoute.Direct(b.LocalEndPoint)))!;
            Eventually(() => session.Confirmed, "confirmed");

            for (uint messageId = 1000; messageId < 1200; messageId++)
            {
                for (var index = 0; index < 5; index++)
                {
                    var frame = new byte[7 + MeshPackets.FragmentPayload];
                    frame[0] = 1;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), messageId);
                    frame[5] = (byte)index;
                    frame[6] = 120;
                    a.SendRaw(session.Encrypt(frame)!, b.LocalEndPoint);
                }
            }

            Thread.Sleep(300);
            var held = b.BufferedBytes;
            Check(held > 0, "something is buffered");
            Check(held <= new MeshTransportOptions().MaximumBufferedPerSession, $"never more than one session's budget ({held} bytes)");

            a.DisposeAsync().AsTask().Wait();
            Eventually(() => b.BufferedBytes == 0, "all of it given back when the peer leaves", 5000);
        }
        finally
        {
            a.DisposeAsync().AsTask().Wait();
            b.DisposeAsync().AsTask().Wait();
        }
    }

    private static byte[] Flip(byte[] message, int index)
    {
        var copy = (byte[])message.Clone();
        copy[index] ^= 0x01;
        return copy;
    }
}
