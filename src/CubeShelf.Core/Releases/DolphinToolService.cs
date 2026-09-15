using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Releases;

public sealed record DolphinToolStatus(bool Available, string Path, string Source, bool AutoInstallSupported);

public sealed class DolphinToolService
{
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const string WindowsVersion = "2606a";
    private const string WindowsSha256 = "4c58045f9821cb63913f4df08ea86ece3cdda9f9e646154516000fa1547e0c37";
    private static readonly Uri WindowsArchive =
        new("https://dl.dolphin-emu.org/releases/2606a/dolphin-2606a-x64.7z");
    private static readonly ArchiveExtractionLimits ExtractionLimits =
        new(30_000, 2L * 1024 * 1024 * 1024, 512L * 1024 * 1024);

    private readonly IPlatformPaths _paths;
    private readonly HttpClient _http;

    public DolphinToolService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _http = httpClient ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8");
    }

    public string ManagedToolsRoot => Path.Combine(Path.GetFullPath(_paths.DataDirectory), "Tools", "Dolphin");

    public DolphinToolStatus GetStatus()
    {
        var found = Find();
        if (found.Path.Length > 0) return new(true, found.Path, found.Source, SupportsAutoInstall());
        return new(false, "", "Non détecté", SupportsAutoInstall());
    }

    public bool IsAvailable() => Find().Path.Length > 0;

    public async Task<string> EnsureAvailableAsync(
        bool allowAutomaticInstall,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = Find();
        if (existing.Path.Length > 0)
        {
            progress?.Report(1);
            return existing.Path;
        }

        if (!allowAutomaticInstall)
            throw new InvalidOperationException(
                "DolphinTool est nécessaire pour convertir un RVZ. CubeShelf peut l’installer automatiquement sous Windows, ou tu peux installer Dolphin manuellement.");

        if (!SupportsAutoInstall())
            throw new PlatformNotSupportedException(
                OperatingSystem.IsMacOS()
                    ? "DolphinTool n’est pas détecté. Installe Dolphin Emulator dans Applications puis relance CubeShelf."
                    : "DolphinTool n’est pas détecté. Installe Dolphin via le gestionnaire de paquets de ta distribution puis relance CubeShelf.");

        return await InstallOfficialWindowsAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    private static bool SupportsAutoInstall() =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    private async Task<string> InstallOfficialWindowsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var root = ManagedToolsRoot;
        var parent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(parent);
        var staging = root + ".staging-" + Guid.NewGuid().ToString("N");
        var archive = Path.Combine(_paths.CacheDirectory, "downloads", $"dolphin-{WindowsVersion}-x64.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

        try
        {
            await DownloadVerifiedAsync(WindowsArchive, archive, WindowsSha256,
                new Progress<double>(value => progress?.Report(value * .80)), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            SecureArchiveExtractor.Extract(archive, staging, ExtractionLimits,
                value => progress?.Report(.80 + value * .18));

            var tool = Directory.EnumerateFiles(staging, "DolphinTool.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? Directory.EnumerateFiles(staging, "*dolphin*tool*.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException("Le paquet officiel Dolphin ne contient pas DolphinTool.exe.");
            var relative = Path.GetRelativePath(staging, tool);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.Move(staging, root);
            var installed = Path.Combine(root, relative);
            if (!File.Exists(installed))
                throw new FileNotFoundException("DolphinTool.exe n’a pas pu être installé dans le cache CubeShelf.");
            progress?.Report(1);
            return installed;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    private async Task DownloadVerifiedAsync(
        Uri uri,
        string destination,
        string sha256,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        var existing = File.Exists(temporary) ? new FileInfo(temporary).Length : 0L;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!append) existing = 0;
        response.EnsureSuccessStatusCode();
        var responseLength = response.Content.Headers.ContentLength;
        var totalLength = response.Content.Headers.ContentRange?.Length
                          ?? (responseLength is > 0 ? existing + responseLength.Value : (long?)null);
        if (totalLength > MaximumDownloadBytes)
            throw new InvalidDataException("Le paquet Dolphin dépasse la taille autorisée.");

        long total = existing;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(temporary, append ? FileMode.Append : FileMode.Create,
                         FileAccess.Write, FileShare.None, 256 * 1024, true))
        {
            var buffer = new byte[256 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > MaximumDownloadBytes) throw new InvalidDataException("Le paquet Dolphin dépasse la taille autorisée.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                if (totalLength is > 0) progress?.Report(Math.Clamp((double)total / totalLength.Value, 0, 1));
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        string actual;
        await using (var stream = File.OpenRead(temporary))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            throw new CryptographicException("Le SHA-256 du paquet Dolphin officiel est invalide. Installation refusée.");
        }
        File.Move(temporary, destination, true);
        progress?.Report(1);
    }

    private (string Path, string Source) Find()
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { "DolphinTool.exe", "dolphin-tool.exe" }
            : new[] { "DolphinTool", "dolphin-tool" };
        var candidates = new List<(string Directory, string Source)>
        {
            (AppContext.BaseDirectory, "Dossier CubeShelf"),
            (ManagedToolsRoot, "Cache CubeShelf")
        };

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                     }.Where(root => !string.IsNullOrWhiteSpace(root)))
            {
                candidates.Add((Path.Combine(root, "Dolphin Emulator"), "Dolphin installé"));
                candidates.Add((Path.Combine(root, "Dolphin"), "Dolphin installé"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(("/Applications/Dolphin.app/Contents/MacOS", "Dolphin.app"));
            candidates.Add((Path.Combine(home, "Applications", "Dolphin.app", "Contents", "MacOS"), "Dolphin.app utilisateur"));
            candidates.Add(("/usr/local/bin", "PATH système"));
            candidates.Add(("/opt/homebrew/bin", "Homebrew"));
        }
        else
        {
            candidates.Add(("/usr/bin", "Paquet système"));
            candidates.Add(("/usr/local/bin", "Paquet système"));
            candidates.Add(("/app/bin", "Flatpak"));
            candidates.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"), "Utilisateur"));
        }

        foreach (var (directory, source) in candidates)
        foreach (var name in names)
        {
            try
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return (path, source);
            }
            catch { }
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var name in names)
        {
            try
            {
                var path = Path.Combine(directory.Trim(), name);
                if (File.Exists(path)) return (path, "PATH");
            }
            catch { }
        }
        return ("", "");
    }
}
