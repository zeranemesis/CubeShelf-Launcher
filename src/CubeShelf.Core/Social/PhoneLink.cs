using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CubeShelf.Core.Social;

/// <summary>What happened on the link, for the window showing the QR code.</summary>
public sealed record PhoneLinkEvent(bool Succeeded, string Message);

/// <summary>
/// The PC's side of "Play on a phone": PartyBoard on Android scans a QR code and gets this profile
/// and the Mario Party 4 memory cards, or sends its own cards back.
///
/// The code reads <c>CSL1:192.168.1.20:48213/token#key</c>: this PC's address on the local
/// network, a request token and a 256-bit AES-GCM key. Every body is sealed under that key
/// (associated data "cubeshelf-link-v1|down" or "|up"), so plain HTTP on the local network is
/// enough: nobody who has not seen the screen can read or change a byte. Exactly two requests are
/// answered, <c>GET /l/token</c> and <c>POST /l/token/saves</c>, while the window is open and for
/// ten minutes at most. The phone's side is CubeShelfLink.java in PartyBoard; both change together.
///
/// Memory cards received are never written under a running game: with Mario Party 4 open the
/// transfer is refused and says why. What they replace is kept in save-backups (the five newest).
/// </summary>
public sealed class PhoneLinkServer : IAsyncDisposable
{
    public const string Prefix = "CSL1:";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MaximumHeaderBytes = 8 * 1024;
    private const int MaximumBodyBytes = 128 * 1024 * 1024;
    private const int MaximumSaveBytes = 32 * 1024 * 1024;
    private const int MaximumSaves = 64;
    private const int MaximumBadRequests = 20;

    private readonly Func<JsonNode> _profile;
    private readonly string _savesDirectory;
    private readonly Func<bool> _gameRunning;
    private readonly IPAddress _address;
    private readonly bool _french;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly DateTimeOffset _expiresAt;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private int _badRequests;

    /// <param name="profile">The profile document, built when the phone asks (ProfileTransfer's shape).</param>
    /// <param name="savesDirectory">Where PartyBoard keeps its memory cards on this PC.</param>
    /// <param name="gameRunning">Whether Mario Party 4 runs now, which forbids writing its cards.</param>
    /// <param name="french">The language of what the phone is told after sending its cards.</param>
    public PhoneLinkServer(Func<JsonNode> profile, string savesDirectory, Func<bool> gameRunning, IPAddress address, bool french = true)
    {
        _french = french;
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _savesDirectory = savesDirectory ?? throw new ArgumentNullException(nameof(savesDirectory));
        _gameRunning = gameRunning ?? throw new ArgumentNullException(nameof(gameRunning));
        _address = address ?? throw new ArgumentNullException(nameof(address));
        Token = Base64Url(RandomNumberGenerator.GetBytes(18));
        _expiresAt = DateTimeOffset.UtcNow + Lifetime;
    }

    public string Token { get; }
    public int Port { get; private set; }

    /// <summary>What the QR code says.</summary>
    public string Code => $"{Prefix}{_address}:{Port}/{Token}#{Base64Url(_key)}";

    public event Action<PhoneLinkEvent>? Happened;

    public void Start()
    {
        if (_listener is not null) return;
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _lifetime = new CancellationTokenSource(Lifetime);
        _ = AcceptLoopAsync(_listener, _lifetime.Token);
    }

    // ------------------------------------------------------------------ the address to show

    /// <summary>
    /// This PC's addresses a phone on the same network can reach, best first: private IPv4, on an
    /// adapter that is up and has a gateway, physical before virtual.
    /// </summary>
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        var found = new List<(IPAddress Address, int Rank)>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<IPAddress>();
        }

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

            var virtualAdapter = new[] { "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "wsl", "docker" }
                .Any(word => adapter.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                             adapter.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
            var hasGateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (!IsPrivate(unicast.Address)) continue;
                found.Add((unicast.Address, (hasGateway ? 0 : 2) + (virtualAdapter ? 1 : 0)));
            }
        }

        return found.OrderBy(entry => entry.Rank).Select(entry => entry.Address).Distinct().ToArray();
    }

    /// <summary>The phone refuses any other: a code must never send it outside the local network.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    // ------------------------------------------------------------------ sealing

    public static byte[] Seal(byte[] key, string direction, ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var framed = new byte[NonceLength + plaintext.Length + TagLength];
        nonce.CopyTo(framed, 0);
        using var gcm = new AesGcm(key, TagLength);
        gcm.Encrypt(nonce, plaintext, framed.AsSpan(NonceLength, plaintext.Length),
            framed.AsSpan(NonceLength + plaintext.Length, TagLength), AssociatedData(direction));
        return framed;
    }

    public static byte[]? Open(byte[] key, string direction, ReadOnlySpan<byte> framed)
    {
        if (framed.Length < NonceLength + TagLength) return null;
        var plaintext = new byte[framed.Length - NonceLength - TagLength];
        try
        {
            using var gcm = new AesGcm(key, TagLength);
            gcm.Decrypt(framed[..NonceLength], framed.Slice(NonceLength, plaintext.Length),
                framed[^TagLength..], plaintext, AssociatedData(direction));
            return plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[] AssociatedData(string direction) =>
        Encoding.ASCII.GetBytes("cubeshelf-link-v1|" + direction);

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The key inside a code, for tests and for a client written in this language.</summary>
    public static byte[]? KeyOf(string code)
    {
        var hash = code.IndexOf('#');
        if (hash < 0) return null;
        var text = code[(hash + 1)..].Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
        try
        {
            var key = Convert.FromBase64String(text);
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ memory cards

    /// <summary>
    /// Memory cards only, never a path outside the folder: MemoryCard*.raw at the top, or
    /// USA|EUR|JAP / Card A|Card B / *.gci. The same rule as CubeShelfLink.isSavePath.
    /// </summary>
    public static bool IsSavePath(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == "..")) return false;
        if (parts.Length == 1)
            return parts[0].StartsWith("MemoryCard", StringComparison.Ordinal) &&
                   parts[0].EndsWith(".raw", StringComparison.OrdinalIgnoreCase);
        return parts.Length == 3 &&
               parts[0] is "USA" or "EUR" or "JAP" &&
               parts[1] is "Card A" or "Card B" &&
               parts[2].EndsWith(".gci", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ListSaves(string root)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "MemoryCard*", SearchOption.TopDirectoryOnly))
            {
                var relative = Path.GetFileName(file);
                if (IsSavePath(relative)) result.Add(relative);
            }
            foreach (var region in new[] { "USA", "EUR", "JAP" })
            foreach (var card in new[] { "Card A", "Card B" })
            {
                var folder = Path.Combine(root, region, card);
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*.gci"))
                {
                    var relative = region + "/" + card + "/" + Path.GetFileName(file);
                    if (IsSavePath(relative)) result.Add(relative);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return result;
    }

    /// <summary>Where PartyBoard keeps its memory cards on this PC (SDL's pref path for MarioPartyRD / Party Board).</summary>
    public static string DefaultSavesDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarioPartyRD", "Party Board");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "MarioPartyRD", "Party Board");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(data) ? Path.Combine(home, ".local", "share") : data, "MarioPartyRD", "Party Board");
    }

    // ------------------------------------------------------------------ the two requests

    /// <summary>GET: the profile and this PC's cards, sealed for the phone.</summary>
    public byte[] BuildDownload()
    {
        var saves = new JsonArray();
        foreach (var relative in ListSaves(_savesDirectory))
        {
            var bytes = File.ReadAllBytes(Path.Combine(_savesDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (bytes.Length > MaximumSaveBytes) continue;
            saves.Add(new JsonObject { ["path"] = relative, ["gz"] = Convert.ToBase64String(Gzip(bytes)) });
        }

        var document = new JsonObject { ["v"] = 1, ["profile"] = _profile(), ["saves"] = saves };
        return Seal(_key, "down", Encoding.UTF8.GetBytes(document.ToJsonString()));
    }

    /// <summary>POST: the phone's cards, put in place unless the game runs. Returns what to tell the phone.</summary>
    public (bool Ok, string Message) ApplyUpload(byte[] sealedBody)
    {
        var plaintext = Open(_key, "up", sealedBody);
        if (plaintext is null) return (false, "Unreadable");

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(plaintext);
        }
        catch (JsonException)
        {
            return (false, "Unreadable");
        }
        if (document?["v"]?.GetValue<int>() != 1 || document["saves"] is not JsonArray saves)
            return (false, "Unsupported");

        if (_gameRunning())
            return (false, Say("Mario Party 4 est ouvert sur le PC : ferme-le, puis renvoie tes sauvegardes.",
                               "Mario Party 4 is open on the PC: close it, then send your saves again."));

        var received = new List<(string Path, byte[] Data)>();
        foreach (var save in saves.Take(MaximumSaves))
        {
            var path = save?["path"]?.GetValue<string>() ?? "";
            var gz = save?["gz"]?.GetValue<string>() ?? "";
            if (!IsSavePath(path)) continue;
            try
            {
                received.Add((path, Gunzip(Convert.FromBase64String(gz))));
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException or IOException)
            {
                return (false, Say("Une sauvegarde est illisible : " + path, "A save could not be read: " + path));
            }
        }
        if (received.Count == 0) return (true, Say("Aucune sauvegarde reçue.", "No save received."));

        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var backup = Path.Combine(_savesDirectory, "save-backups", stamp);
        foreach (var (path, data) in received)
        {
            var target = Path.Combine(_savesDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                var copy = Path.Combine(backup, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(target, copy, true);
            }
            var temporary = target + ".tmp";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, target, true);
        }
        PruneBackups(Path.Combine(_savesDirectory, "save-backups"));

        return (true, Say($"{received.Count} sauvegarde(s) reçue(s) sur le PC. Les anciennes sont gardées dans save-backups.",
                          $"{received.Count} save(s) received on the PC. The old ones are kept in save-backups."));
    }

    private string Say(string french, string english) => _french ? french : english;

    private static void PruneBackups(string folder)
    {
        try
        {
            foreach (var old in Directory.GetDirectories(folder).OrderByDescending(path => path, StringComparer.Ordinal).Skip(5))
                Directory.Delete(old, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaximumSaveBytes) throw new InvalidDataException("save too large");
        }
        return output.ToArray();
    }

    // ------------------------------------------------------------------ HTTP, the bare minimum

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                continue;
            }
            _ = ServeAsync(client, cancellationToken);
        }

        try
        {
            listener.Stop();
        }
        catch (SocketException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                var stream = client.GetStream();
                var (method, path, body) = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
                var (status, reply) = Route(method, path, body);
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: application/octet-stream\r\nContent-Length: {reply.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(reply, timeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
            {
            }
        }
    }

    private (string Status, byte[] Body) Route(string method, string path, byte[] body)
    {
        if (DateTimeOffset.UtcNow > _expiresAt || _badRequests >= MaximumBadRequests)
            return ("410 Gone", Array.Empty<byte>());

        if (method == "GET" && path == "/l/" + Token)
        {
            try
            {
                var reply = BuildDownload();
                Happened?.Invoke(new PhoneLinkEvent(true, "sent"));
                return ("200 OK", reply);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Happened?.Invoke(new PhoneLinkEvent(false, exception.Message));
                return ("500 Internal Server Error", Array.Empty<byte>());
            }
        }

        if (method == "POST" && path == "/l/" + Token + "/saves")
        {
            (bool Ok, string Message) result;
            try
            {
                result = ApplyUpload(body);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result = (false, exception.Message);
            }
            Happened?.Invoke(new PhoneLinkEvent(result.Ok, result.Message));
            return result.Message == "Unreadable" || result.Message == "Unsupported"
                ? ("400 Bad Request", Array.Empty<byte>())
                : ("200 OK", Seal(_key, "down", Encoding.UTF8.GetBytes(result.Message)));
        }

        // A wrong token is someone guessing: a few are tolerated, then the link closes.
        Interlocked.Increment(ref _badRequests);
        return ("404 Not Found", Array.Empty<byte>());
    }

    private static async Task<(string Method, string Path, byte[] Body)> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0) throw new InvalidDataException("closed");
            header.WriteByte(one[0]);
            if (header.Length > MaximumHeaderBytes) throw new InvalidDataException("header too large");
            var buffer = header.GetBuffer();
            var length = (int)header.Length;
            if (length >= 4 && buffer[length - 4] == '\r' && buffer[length - 3] == '\n' && buffer[length - 2] == '\r' && buffer[length - 1] == '\n')
                break;
        }

        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) throw new InvalidDataException("bad request line");

        var contentLength = 0L;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                !long.TryParse(line["Content-Length:".Length..].Trim(), out contentLength))
                throw new InvalidDataException("bad length");
        }
        if (contentLength is < 0 or > MaximumBodyBytes) throw new InvalidDataException("body too large");

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException("truncated");
            offset += read;
        }
        return (requestLine[0], requestLine[1], body);
    }

    public ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (SocketException)
        {
        }
        _lifetime?.Dispose();
        CryptographicOperations.ZeroMemory(_key);
        return ValueTask.CompletedTask;
    }
}
