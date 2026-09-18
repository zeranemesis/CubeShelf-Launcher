using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CubeShelf.Core.Platform;

namespace CubeShelf.Core.Releases;

public sealed record LauncherUpdateInfo(bool Available, string CurrentVersion, string LatestVersion, ReleaseArtifact? Artifact);

public sealed class LauncherUpdateService
{
    private const long MaximumArtifactBytes = 1024L * 1024 * 1024;
    private readonly IPlatformPaths _paths;
    private readonly HttpClient _http;
    private readonly Uri _manifestUri;

    public LauncherUpdateService(IPlatformPaths paths, HttpClient? httpClient = null, Uri? manifestUri = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _http = httpClient ?? new HttpClient();
        _manifestUri = manifestUri ?? new Uri(
            "https://github.com/zeranemesis/CubeShelf-Launcher/releases/latest/download/release-manifest-v2.json");
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8");
    }

    public async Task<LauncherUpdateInfo> CheckAsync(
        string? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        currentVersion ??= Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                              .InformationalVersion.Split('+', 2)[0]
                          ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
                          ?? "0.0.0";
        using var response = await _http.GetAsync(_manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024)
            throw new InvalidDataException("Le manifeste de mise à jour est trop volumineux.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(stream,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                       .ConfigureAwait(false)
                       ?? throw new InvalidDataException("Manifeste de mise à jour vide.");
        manifest.Validate();
        var artifact = manifest.Select(CurrentOs(), CurrentArchitecture(), "launcher");
        return new(IsNewer(manifest.Version, currentVersion), currentVersion, manifest.Version, artifact);
    }

    public async Task<string> DownloadAsync(
        LauncherUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var artifact = update.Artifact ?? throw new InvalidOperationException("Aucun artefact de mise à jour sélectionné.");
        if (!update.Available) throw new InvalidOperationException("Aucune mise à jour n’est disponible.");
        if (artifact.Size is <= 0 or > MaximumArtifactBytes)
            throw new InvalidDataException("Taille de mise à jour invalide.");
        var directory = Path.Combine(_paths.CacheDirectory, "Updates");
        Directory.CreateDirectory(directory);
        var suffix = ArtifactSuffix(artifact.Url);
        var destination = Path.Combine(directory,
            $"CubeShelf-{update.LatestVersion}-{CurrentOs()}-{CurrentArchitecture()}{suffix}");
        var temporary = destination + ".part";
        using var response = await _http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("La redirection de mise à jour n’utilise pas HTTPS.");
        if (response.Content.Headers.ContentLength is long length && length != artifact.Size)
            throw new InvalidDataException("La taille de mise à jour ne correspond pas au manifeste.");
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > artifact.Size)
                        throw new InvalidDataException("La mise à jour dépasse la taille annoncée.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report((double)total / artifact.Size);
                }
                if (total != artifact.Size)
                    throw new InvalidDataException("Téléchargement de mise à jour incomplet.");
            }
            string hash;
            await using (var file = File.OpenRead(temporary))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (!hash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Le SHA-256 de la mise à jour est invalide.");
            File.Move(temporary, destination, true);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Deletes the updater image a previous update displaced. The updater renames its own
    /// executable aside before writing the new one -- Windows will not let a process
    /// overwrite the image it is running from -- and cannot delete that copy afterwards,
    /// because it is still running from it. The application can, and it is the first thing
    /// to run after an update, so it reclaims the space (the updater is ~160 MB) rather
    /// than leaving it until the next update happens to come along.
    /// </summary>
    public static void RemoveDisplacedUpdaterImages()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // Narrow on purpose: CubeShelf's own executables, and only the suffix the
            // updater writes, so this can never reach a file the application needs.
            foreach (var stale in Directory.EnumerateFiles(AppContext.BaseDirectory, "CubeShelf*.exe*.old"))
            {
                try { File.Delete(stale); } catch { }
            }
        }
        catch { }
    }

    public Process LaunchWindowsUpdater(string archive, string version)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La mise à jour intégrée Windows n’est pas disponible sur cette plateforme.");
        var installDirectory = AppContext.BaseDirectory;
        var updater = Path.Combine(installDirectory, "CubeShelf.Updater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("CubeShelf.Updater.exe est absent.", updater);
        var start = new ProcessStartInfo(updater) { UseShellExecute = false, WorkingDirectory = installDirectory };
        foreach (var argument in new[]
                 {
                     Environment.ProcessId.ToString(), Path.GetFullPath(archive),
                     Path.GetFullPath(installDirectory), version
                 })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Impossible de démarrer le programme de mise à jour.");
    }

    private static string ArtifactSuffix(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return ".bin";
        var name = Path.GetFileName(uri.AbsolutePath);
        foreach (var suffix in new[] { ".tar.gz", ".AppImage", ".dmg", ".zip", ".exe", ".pkg" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return suffix;
        var extension = Path.GetExtension(name);
        return string.IsNullOrWhiteSpace(extension) ? ".bin" : extension;
    }

    private static bool IsNewer(string latest, string current)
    {
        static Version Parse(string value)
        {
            var core = value.Split('-', 2)[0];
            return Version.TryParse(core, out var result) ? result : new Version(0, 0, 0);
        }
        var latestCore = Parse(latest);
        var currentCore = Parse(current);
        if (latestCore != currentCore) return latestCore > currentCore;
        var latestIsPreview = latest.Contains('-', StringComparison.Ordinal);
        var currentIsPreview = current.Contains('-', StringComparison.Ordinal);
        return !latestIsPreview && currentIsPreview;
    }

    private static string CurrentOs() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos";

    private static string CurrentArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException("Architecture non prise en charge.")
    };
}
