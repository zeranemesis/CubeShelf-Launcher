using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CubeShelf.Mobile;

public enum AppUpdateState { Idle, Checking, Current, Available, Downloading, Ready, Failed }
public sealed record AppUpdatePackage(string Version, string Name, Uri Url, long Size, string Sha256);
public sealed record AppUpdateSnapshot(AppUpdateState State, string CurrentVersion,
    AppUpdatePackage? Package = null, long Received = 0, string? Error = null);

/// <summary>Downloads only stable CubeShelf APKs from the project's own GitHub releases.</summary>
public sealed class LauncherAppUpdates
{
    private const string Repository = "https://github.com/zeranemesis/CubeShelf-Launcher/releases/download/";
    private const long MaximumApkBytes = 256L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _architecture, _directory;
    private readonly Func<string, string> _install;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _download;
    private string? _verifiedFile;
    public AppUpdateSnapshot Snapshot { get; private set; }
    public event Action<AppUpdateSnapshot>? Changed;

    public LauncherAppUpdates(string currentVersion, string architecture, string cacheDirectory,
        Func<string, string> install, HttpClient? http = null)
    {
        Snapshot = new(AppUpdateState.Idle, currentVersion);
        _architecture = architecture;
        _directory = Path.Combine(cacheDirectory, "launcher-updates");
        _install = install;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CubeShelf-Android", "1.0"));
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        var previous = Snapshot;
        try
        {
            if (Snapshot.State == AppUpdateState.Ready) return;
            Set(Snapshot with { State = AppUpdateState.Checking, Error = null });
            using var document = JsonDocument.Parse(await ReadSmallAsync(
                new Uri("https://api.github.com/repos/zeranemesis/CubeShelf-Launcher/releases/latest"), cancellationToken));
            var release = document.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("La dernière release n'est pas une version stable.");
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest.Build < 0 || tag.Contains('-'))
                throw new InvalidDataException("Numéro de version GitHub invalide.");
            var currentText = Snapshot.CurrentVersion.Split('+')[0].Split('-')[0];
            if (!Version.TryParse(currentText, out var current)) throw new InvalidDataException("Version installée inconnue.");
            if (latest <= current && !(latest == current && Snapshot.CurrentVersion.Contains('-')))
            { Set(new(AppUpdateState.Current, Snapshot.CurrentVersion)); return; }
            var name = $"CubeShelf-Android-{_architecture}-preview.apk";
            var matches = release.GetProperty("assets").EnumerateArray()
                .Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Aucun APK CubeShelf compatible dans cette release.");
            var asset = matches[0];
            var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            var expectedUrl = Repository + Uri.EscapeDataString(tag) + "/" + name;
            if (url.AbsoluteUri != expectedUrl) throw new InvalidDataException("Adresse de mise à jour inattendue.");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaximumApkBytes) throw new InvalidDataException("Taille de mise à jour invalide.");
            // checksums.txt is generated from all final release packages before publication.
            var checksums = await ReadSmallAsync(new Uri(Repository + Uri.EscapeDataString(tag) + "/checksums.txt"), cancellationToken);
            var lines = checksums.Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2 && parts[1] == name).ToArray();
            if (lines.Length != 1 || lines[0][0].Length != 64 || !lines[0][0].All(Uri.IsHexDigit))
                throw new InvalidDataException("Empreinte SHA-256 absente ou invalide.");
            if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
                && !string.Equals(digest.GetString(), "sha256:" + lines[0][0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Les empreintes de la release ne correspondent pas.");
            Set(new(AppUpdateState.Available, Snapshot.CurrentVersion,
                new(latest.ToString(), name, url, size, lines[0][0])));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Set(previous); }
        catch (Exception error) { Set(previous with { State = AppUpdateState.Failed, Error = error.Message }); }
        finally { _gate.Release(); }
    }

    public async Task DownloadAsync()
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        string? temporary = null;
        try
        {
            if (Snapshot.Package is not { } package || Snapshot.State == AppUpdateState.Ready) return;
            using var cancellation = new CancellationTokenSource();
            _download = cancellation;
            Directory.CreateDirectory(_directory);
            _verifiedFile = null;
            temporary = Path.Combine(_directory, "cubeshelf-update.part");
            Set(Snapshot with { State = AppUpdateState.Downloading, Received = 0, Error = null });
            using var response = await _http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Téléchargement sans HTTPS refusé.");
            if (response.Content.Headers.ContentLength is long size && size != package.Size)
                throw new InvalidDataException("La taille ne correspond pas à la release.");
            long total = 0;
            var lastProgress = Environment.TickCount64;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > package.Size) throw new InvalidDataException("APK trop volumineux.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token).ConfigureAwait(false);
                    if (Environment.TickCount64 - lastProgress >= 200)
                    { Set(Snapshot with { Received = total }); lastProgress = Environment.TickCount64; }
                }
            }
            if (total != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("L'APK est incomplet ou son SHA-256 est invalide.");
            cancellation.Token.ThrowIfCancellationRequested();
            var destination = Path.Combine(_directory, "cubeshelf-update.apk");
            File.Move(temporary, destination, true);
            _verifiedFile = destination;
            Set(Snapshot with { State = AppUpdateState.Ready, Received = total });
        }
        catch (OperationCanceledException) { Set(Snapshot with { State = AppUpdateState.Available, Received = 0, Error = null }); }
        catch (Exception error) { Set(Snapshot with { State = AppUpdateState.Failed, Error = error.Message }); }
        finally
        {
            _download = null;
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    public void CancelDownload() { try { _download?.Cancel(); } catch (ObjectDisposedException) { } }
    public string Install()
    {
        if (Snapshot.State != AppUpdateState.Ready || _verifiedFile is null || !File.Exists(_verifiedFile))
            return "Télécharge et vérifie la mise à jour avant de l'installer.";
        try { return _install(_verifiedFile); }
        catch (Exception error) { return "Installation impossible : " + error.Message; }
    }

    private void Set(AppUpdateSnapshot snapshot) { Snapshot = snapshot; Changed?.Invoke(snapshot); }
    private async Task<string> ReadSmallAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Réponse sans HTTPS refusée.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Réponse GitHub trop volumineuse.");
            output.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
}
