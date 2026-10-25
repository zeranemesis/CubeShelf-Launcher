using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Mobile;

namespace CubeShelf.Android;

/// <summary>Downloads data only. All executable libraries are installed with CubeShelf.</summary>
internal sealed class PartyBoardDownloads : IRuntimeDownloads
{
    internal const string Version = "v0.20.0";
    internal const string PayloadHash = "7b11534b8e68d4386d03738c6f3fd97324ecf5a71dc212929128d0d8201c2374";
    internal const long PayloadSize = 52_716_096;
    private const string Url = "https://github.com/zeranemesis/Marioparty4/releases/download/v0.20.0/PartyBoard-v0.20.0-phone-arm64-v8a.apk";
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object _gate = new();
    private readonly string _directory, _partial, _staging, _runtime, _marker;
    private RuntimeDownloadSnapshot _snapshot;
    private CancellationTokenSource? _cancellation;
    private bool _running, _discard;
    public event Action<RuntimeDownloadSnapshot>? Changed;
    public RuntimeDownloadSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    internal DateTimeOffset? InstalledAt => File.Exists(_marker) ? File.GetLastWriteTimeUtc(_marker) : null;
    public bool IsReady
    {
        get
        {
            if (Snapshot.State != RuntimeDownloadState.Ready) return false;
            if (HasInstalledFiles()) return true;
            Publish(new(RuntimeDownloadState.NotInstalled, PartialLength(), PayloadSize));
            return false;
        }
    }

    public PartyBoardDownloads(string filesDirectory, bool supported)
    {
        _directory = Path.Combine(filesDirectory, "partyboard-downloads");
        _partial = Path.Combine(_directory, "v0.20.0.partial");
        _staging = Path.Combine(_directory, "staging-v0.20.0");
        _runtime = Path.Combine(filesDirectory, "partyboard");
        _marker = Path.Combine(_runtime, ".runtime-v0.20.0.json");
        var received = PartialLength();
        var ready = supported && HasInstalledFiles();
        _snapshot = new(!supported ? RuntimeDownloadState.Unavailable : ready
            ? RuntimeDownloadState.Ready : received > 0 ? RuntimeDownloadState.Paused : RuntimeDownloadState.NotInstalled,
            ready ? PayloadSize : received, PayloadSize);
    }

    public bool HasInstalledFiles()
    {
        try
        {
            if (!File.Exists(_marker) || new FileInfo(_marker).Length > 32768) return false;
            var record = JsonSerializer.Deserialize<Installation>(File.ReadAllText(_marker));
            return record?.Version == Version && record.PayloadHash == PayloadHash && record.Files is { Length: 20 }
                && record.Files.All(file => IsResource(file.Path)
                    && File.Exists(Path.Combine(_runtime, file.Path))
                    && new FileInfo(Path.Combine(_runtime, file.Path)).Length == file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running || _snapshot.State is RuntimeDownloadState.Unavailable or RuntimeDownloadState.Ready) return;
            _running = true; _discard = false;
            _cancellation = new CancellationTokenSource();
            _snapshot = new(RuntimeDownloadState.Downloading, PartialLength(), PayloadSize);
        }
        Publish(Snapshot);
        _ = RunAsync(_cancellation.Token);
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running || _snapshot.State != RuntimeDownloadState.Downloading) return;
            _snapshot = _snapshot with { State = RuntimeDownloadState.Pausing, BytesPerSecond = 0 };
            _cancellation?.Cancel();
        }
        Publish(Snapshot);
    }

    public void Cancel()
    {
        bool running;
        lock (_gate)
        {
            if (_snapshot.State is RuntimeDownloadState.Ready or RuntimeDownloadState.Unavailable
                or RuntimeDownloadState.Verifying or RuntimeDownloadState.Installing) return;
            _discard = true;
            running = _running;
            if (running) _cancellation?.Cancel();
        }
        if (!running)
        {
            try { File.Delete(_partial); DeleteStaging(); }
            catch (IOException error) { global::Android.Util.Log.Warn("CubeShelf.Download", error.Message); }
            Publish(new(RuntimeDownloadState.NotInstalled, 0, PayloadSize));
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        RuntimeDownloadSnapshot final;
        try
        {
            Directory.CreateDirectory(_directory);
            var needed = PayloadSize - PartialLength() + 24L * 1024 * 1024;
            using (var storage = new global::Android.OS.StatFs(_directory))
                if (storage.AvailableBytes < needed) throw new IOException("Espace insuffisant pour préparer PartyBoard.");
            await DownloadAsync(cancellation).ConfigureAwait(false);
            Publish(new(RuntimeDownloadState.Verifying, PayloadSize, PayloadSize));
            await using (var stream = File.OpenRead(_partial))
            {
                var digest = await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false);
                if (!Convert.ToHexString(digest).Equals(PayloadHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(_partial);
                    throw new InvalidDataException("Le fichier reçu n’a pas pu être vérifié. Relance le téléchargement.");
                }
            }
            Publish(new(RuntimeDownloadState.Installing, PayloadSize, PayloadSize));
            await InstallAsync(cancellation).ConfigureAwait(false);
            File.Delete(_partial);
            final = new(RuntimeDownloadState.Ready, PayloadSize, PayloadSize);
        }
        catch (OperationCanceledException)
        {
            final = cancellation.IsCancellationRequested
                ? new(RuntimeDownloadState.Paused, PartialLength(), PayloadSize)
                : new(RuntimeDownloadState.Failed, PartialLength(), PayloadSize, Error: "Connexion trop lente ou interrompue. Tu peux reprendre.");
        }
        catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException
            or CryptographicException or JsonException)
        {
            global::Android.Util.Log.Warn("CubeShelf.Download", error.ToString());
            final = new(RuntimeDownloadState.Failed, PartialLength(), PayloadSize, Error:
                error is HttpRequestException ? "Connexion interrompue. Tu peux reprendre le téléchargement."
                : error is InvalidDataException ? error.Message : "Impossible de préparer PartyBoard. Vérifie l’espace disponible puis réessaie.");
        }
        finally
        {
            lock (_gate) { _cancellation?.Dispose(); _cancellation = null; }
        }
        lock (_gate)
        {
            if (_discard)
            {
                try { File.Delete(_partial); DeleteStaging(); }
                catch (IOException error) { global::Android.Util.Log.Warn("CubeShelf.Download", error.Message); }
                final = new(RuntimeDownloadState.NotInstalled, 0, PayloadSize);
            }
            _running = false;
            _snapshot = final;
        }
        Publish(final);
    }

    private async Task DownloadAsync(CancellationToken cancellation)
    {
        var offset = PartialLength();
        if (offset == PayloadSize) return;
        if (offset > PayloadSize) { File.Delete(_partial); offset = 0; }
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.UserAgent.ParseAdd("CubeShelf/0.17 PartyBoard-resources");
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        headersTimeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token).ConfigureAwait(false);
        headersTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != offset || range.Length != PayloadSize)
                throw new InvalidDataException("Le serveur n’a pas pu reprendre ce fichier. Annule puis relance le téléchargement.");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0; // Range unsupported: restart safely.
        else throw new HttpRequestException("Unexpected download response");
        await using var output = new FileStream(_partial, offset == 0 ? FileMode.Create : FileMode.Append,
            FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        var buffer = new byte[128 * 1024];
        var received = offset;
        var checkpointBytes = offset;
        var clock = Stopwatch.StartNew();
        var checkpoint = clock.Elapsed;
        while (true)
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var count = await input.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
            if (count == 0) break;
            if (received + count > PayloadSize) throw new InvalidDataException("Le fichier reçu dépasse la taille attendue.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
            received += count;
            if (clock.Elapsed - checkpoint >= TimeSpan.FromMilliseconds(250))
            {
                var speed = (received - checkpointBytes) / (clock.Elapsed - checkpoint).TotalSeconds;
                Publish(new(RuntimeDownloadState.Downloading, received, PayloadSize, speed));
                checkpoint = clock.Elapsed; checkpointBytes = received;
            }
        }
        await output.FlushAsync(cancellation).ConfigureAwait(false);
        if (received != PayloadSize) throw new IOException("Incomplete download");
    }

    private async Task InstallAsync(CancellationToken cancellation)
    {
        DeleteStaging(); Directory.CreateDirectory(_staging);
        var files = new List<ResourceFile>();
        long total = 0;
        using (var archive = ZipFile.OpenRead(_partial))
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal) || entry.Name.Length == 0) continue;
                var name = entry.FullName[7..];
                if (!IsResource(name)) continue; // No classes, libraries, discs or other APK contents.
                if (++total > 128 || entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Archive de ressources invalide.");
                var path = Path.Combine(_staging, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using (var source = entry.Open())
                await using (var output = File.Create(path))
                    await source.CopyToAsync(output, cancellation).ConfigureAwait(false);
                files.Add(new(name, new FileInfo(path).Length));
            }
        }
        if (files.Count != 20 || !files.Any(file => file.Path == "res/rml/prelaunch.rcss")
            || !files.Any(file => file.Path == "initial_pipeline_cache.db"))
            throw new InvalidDataException("Les ressources PartyBoard sont incomplètes.");
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_runtime);
        // Only replace runtime resources. Settings, memory cards and imported discs stay untouched.
        File.Delete(_marker);
        foreach (var file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            var target = Path.Combine(_runtime, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(Path.Combine(_staging, file.Path), target, true);
        }
        var marker = JsonSerializer.Serialize(new Installation(Version, PayloadHash, files.ToArray()));
        await File.WriteAllTextAsync(_marker + ".tmp", marker, cancellation).ConfigureAwait(false);
        File.Move(_marker + ".tmp", _marker, true);
        DeleteStaging();
    }

    private static bool IsResource(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\\') && !path.Split('/').Any(part => part is ".." or "." or "")
        && (path == "initial_pipeline_cache.db" || path.StartsWith("res/", StringComparison.Ordinal));

    private long PartialLength() { try { return new FileInfo(_partial) is { Exists: true } file ? file.Length : 0; } catch (IOException) { return 0; } }
    private void DeleteStaging() { if (Directory.Exists(_staging)) Directory.Delete(_staging, true); }
    private void Publish(RuntimeDownloadSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_snapshot.State == RuntimeDownloadState.Pausing && snapshot.State == RuntimeDownloadState.Downloading) return;
            _snapshot = snapshot;
        }
        Changed?.Invoke(snapshot);
    }
    private sealed record ResourceFile(string Path, long Length);
    private sealed record Installation(string Version, string PayloadHash, ResourceFile[] Files);
}
