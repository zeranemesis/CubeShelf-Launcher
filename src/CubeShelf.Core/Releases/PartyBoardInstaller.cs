using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Releases;

public sealed record PartyBoardInstallResult(string Version, string ExecutablePath);
public sealed record PartyBoardRuntimeStatus(bool IsInstalled, bool NeedsRepair, string Version, string ExecutablePath);

public sealed class PartyBoardInstaller
{
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly IPlatformPaths _paths;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PartyBoardInstaller(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _http = httpClient ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-avalonia");
    }

    public async Task<PartyBoardInstallResult> InstallLatestAsync(
        string owner,
        string repository,
        string tag,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        ValidateIdentifier(owner, nameof(owner));
        ValidateIdentifier(repository, nameof(repository));
        ValidateIdentifier(tag, nameof(tag));
        ValidateIdentifier(gameId, nameof(gameId));

        var releaseRoot = new Uri($"https://github.com/{owner}/{repository}/releases/download/{tag}/");
        var manifest = await DownloadManifestAsync(new Uri(releaseRoot, "manifest.json"), cancellationToken)
            .ConfigureAwait(false);
        var artifact = manifest.Schema == 1
            ? await ResolveSchema1ArtifactAsync(releaseRoot, manifest, cancellationToken).ConfigureAwait(false)
            : SelectArtifact(manifest);
        ValidateArtifact(artifact, requireSize: manifest.Schema != 1);
        progress?.Report(0.05);

        var safeVersion = SanitizeSegment(manifest.Version);
        var runtimeRoot = RuntimeRoot(gameId);
        var versionsRoot = Path.Combine(runtimeRoot, "versions");
        var destination = Path.Combine(versionsRoot, safeVersion);
        var expectedExecutable = Path.Combine(destination, NormalizeRelativePath(artifact.Launch));
        if (!force && File.Exists(expectedExecutable))
        {
            WriteState(runtimeRoot, manifest.Version, artifact.Launch);
            progress?.Report(1);
            return new(manifest.Version, expectedExecutable);
        }

        var cacheDirectory = Path.Combine(_paths.CacheDirectory, "downloads");
        Directory.CreateDirectory(cacheDirectory);
        var package = Path.Combine(cacheDirectory, artifact.Name);
        var packageUri = new Uri(releaseRoot, Uri.EscapeDataString(artifact.Name));
        if (manifest.Schema == 1)
            await DownloadLegacyVerifiedAsync(packageUri, package, artifact.Sha256, progress, cancellationToken).ConfigureAwait(false);
        else
            await DownloadVerifiedAsync(packageUri, package, artifact.Size, artifact.Sha256, progress, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(versionsRoot);
        var staging = Path.Combine(versionsRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            if (artifact.Kind.Equals("appimage", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(staging, NormalizeRelativePath(artifact.Launch));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(package, target, true);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(target, File.GetUnixFileMode(target) |
                        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            else if (artifact.Kind.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                SecureArchiveExtractor.Extract(package, staging,
                    new ArchiveExtractionLimits(20_000, MaximumPackageBytes * 3, MaximumPackageBytes));
            }
            else
            {
                throw new InvalidDataException($"Type de paquet PartyBoard non pris en charge : {artifact.Kind}.");
            }

            var stagedExecutable = Path.Combine(staging, NormalizeRelativePath(artifact.Launch));
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException("Le paquet PartyBoard ne contient pas l’exécutable annoncé.");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stagedExecutable, File.GetUnixFileMode(stagedExecutable) |
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(staging, destination);
            WriteState(runtimeRoot, manifest.Version, artifact.Launch);
            CleanupOtherVersions(versionsRoot, destination);
            progress?.Report(1);
            return new(manifest.Version, expectedExecutable);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public Task<PartyBoardInstallResult> RepairLatestAsync(
        string owner,
        string repository,
        string tag,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        InstallLatestAsync(owner, repository, tag, gameId, progress, cancellationToken, force: true);

    public PartyBoardRuntimeStatus GetStatus(string gameId)
    {
        ValidateIdentifier(gameId, nameof(gameId));
        var runtimeRoot = RuntimeRoot(gameId);
        var statePath = Path.Combine(runtimeRoot, "runtime-state.json");
        if (!File.Exists(statePath)) return new(false, false, "", "");
        try
        {
            var state = JsonSerializer.Deserialize<RuntimeStateFile>(File.ReadAllText(statePath), _json);
            if (state is null || string.IsNullOrWhiteSpace(state.Version))
                return new(false, true, "", "");
            var executable = Path.Combine(runtimeRoot, "versions", SanitizeSegment(state.Version),
                NormalizeRelativePath(state.RelativeExecutable));
            return new(File.Exists(executable), !File.Exists(executable), state.Version, executable);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return new(false, true, "", "");
        }
    }

    public bool Uninstall(string gameId)
    {
        ValidateIdentifier(gameId, nameof(gameId));
        var runtimeRoot = RuntimeRoot(gameId);
        if (!Directory.Exists(runtimeRoot)) return false;
        Directory.Delete(runtimeRoot, true);
        return true;
    }

    public async Task<bool> UninstallAsync(
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(gameId, nameof(gameId));
        var runtimeRoot = RuntimeRoot(gameId);
        if (!Directory.Exists(runtimeRoot))
        {
            progress?.Report(1);
            return false;
        }

        var files = Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories).ToArray();
        var directories = Directory.EnumerateDirectories(runtimeRoot, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToArray();

        progress?.Report(.03);
        var fileCount = Math.Max(1, files.Length);
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(files[index]);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(files[index], attributes & ~FileAttributes.ReadOnly);
            }
            catch { }
            File.Delete(files[index]);
            progress?.Report(.03 + .82 * (index + 1d) / fileCount);
            if ((index & 63) == 63) await Task.Yield();
        }

        var directoryCount = Math.Max(1, directories.Length);
        for (var index = 0; index < directories.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directories[index]))
                Directory.Delete(directories[index], false);
            progress?.Report(.85 + .13 * (index + 1d) / directoryCount);
            if ((index & 63) == 63) await Task.Yield();
        }

        if (Directory.Exists(runtimeRoot))
            Directory.Delete(runtimeRoot, false);
        progress?.Report(1);
        return true;
    }

    private string RuntimeRoot(string gameId) =>
        Path.Combine(Path.GetFullPath(_paths.DataDirectory), "Runtimes", gameId);

    private void WriteState(string runtimeRoot, string version, string relativeExecutable)
    {
        Directory.CreateDirectory(runtimeRoot);
        var path = Path.Combine(runtimeRoot, "runtime-state.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            new RuntimeStateFile { Version = version, RelativeExecutable = relativeExecutable }, _json));
        File.Move(temporary, path, true);
    }

    private static void CleanupOtherVersions(string versionsRoot, string current)
    {
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            if (Path.GetFullPath(directory).Equals(Path.GetFullPath(current),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;
            Directory.Delete(directory, true);
        }
    }

    private async Task<PartyBoardManifest> DownloadManifestAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024)
            throw new InvalidDataException("Le manifeste PartyBoard est trop volumineux.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<PartyBoardManifest>(stream, _json, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null || (manifest.Schema != 1 && manifest.Schema != 2) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Manifeste PartyBoard invalide ou non pris en charge.");
        if (manifest.Schema == 1 &&
            (string.IsNullOrWhiteSpace(manifest.Platform) || string.IsNullOrWhiteSpace(manifest.Executable)))
            throw new InvalidDataException("Manifeste PartyBoard schema 1 incomplet.");
        return manifest;
    }

    private async Task<PartyBoardArtifact> ResolveSchema1ArtifactAsync(
        Uri releaseRoot,
        PartyBoardManifest manifest,
        CancellationToken cancellationToken)
    {
        var runtimeId = CurrentRuntimeId();
        if (!manifest.Platform.Equals(runtimeId, StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException(
                $"Le runtime PartyBoard publié cible {manifest.Platform}, pas {runtimeId}.");

        var name = $"PartyBoard-{runtimeId}.zip";
        var checksumUri = new Uri(releaseRoot, "checksums.txt");
        using var checksumResponse = await _http.GetAsync(
            checksumUri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        checksumResponse.EnsureSuccessStatusCode();
        var checksums = await checksumResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var sha256 = ParseChecksum(checksums, name);
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new CryptographicException(
                $"checksums.txt ne contient pas un SHA-256 valide pour {name}.");

        return new PartyBoardArtifact
        {
            Name = name,
            Kind = "zip",
            Launch = manifest.Executable,
            Sha256 = sha256,
            Size = 0
        };
    }

    private static PartyBoardArtifact SelectArtifact(PartyBoardManifest manifest)
    {
        var rid = CurrentRuntimeId();
        return manifest.Artifacts.TryGetValue(rid, out var artifact)
            ? artifact
            : throw new PlatformNotSupportedException($"Aucun runtime PartyBoard n’est publié pour {rid}.");
    }

    private static string CurrentRuntimeId()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Architecture PartyBoard non prise en charge : {RuntimeInformation.ProcessArchitecture}.")
        };
        return OperatingSystem.IsWindows()
            ? $"win-{architecture}"
            : OperatingSystem.IsLinux()
                ? $"linux-{architecture}"
                : $"osx-{architecture}";
    }

    private static void ValidateArtifact(PartyBoardArtifact artifact, bool requireSize = true)
    {
        if (string.IsNullOrWhiteSpace(artifact.Name) || artifact.Name != Path.GetFileName(artifact.Name))
            throw new InvalidDataException("Nom d’artefact PartyBoard invalide.");
        if (requireSize && artifact.Size is <= 0 or > MaximumPackageBytes)
            throw new InvalidDataException("Taille d’artefact PartyBoard invalide.");
        if (!requireSize && artifact.Size > MaximumPackageBytes)
            throw new InvalidDataException("Taille d’artefact PartyBoard invalide.");
        if (artifact.Sha256.Length != 64 || artifact.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("SHA-256 PartyBoard invalide.");
        _ = NormalizeRelativePath(artifact.Launch);
    }

    private async Task DownloadLegacyVerifiedAsync(
        Uri uri,
        string destination,
        string expectedHash,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var announcedLength = response.Content.Headers.ContentLength;
        if (announcedLength > MaximumPackageBytes)
            throw new InvalidDataException("Le paquet PartyBoard dépasse la taille autorisée.");

        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                         FileShare.None, 256 * 1024, true))
        {
            var buffer = new byte[256 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > MaximumPackageBytes)
                    throw new InvalidDataException("Le paquet PartyBoard dépasse la taille autorisée.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                if (announcedLength is > 0)
                    progress?.Report(.05 + .75 * Math.Clamp((double)total / announcedLength.Value, 0, 1));
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (total <= 0)
            throw new InvalidDataException("Le paquet PartyBoard téléchargé est vide.");
        if (announcedLength is > 0 && total != announcedLength.Value)
            throw new InvalidDataException("Le téléchargement PartyBoard est incomplet.");

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Le paquet PartyBoard téléchargé ne correspond pas au checksum publié.");

        File.Move(temporary, destination, true);
        progress?.Report(.8);
    }

    private async Task DownloadVerifiedAsync(Uri uri, string destination, long expectedSize, string expectedHash,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temporary = destination + ".part";
        var existing = File.Exists(temporary) ? new FileInfo(temporary).Length : 0L;
        if (existing > expectedSize)
        {
            File.Delete(temporary);
            existing = 0;
        }
        if (existing == expectedSize)
        {
            var completedHash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            if (completedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(temporary, destination, true);
                progress?.Report(.8);
                return;
            }
            File.Delete(temporary);
            existing = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange?.From == existing;
        if (!append) existing = 0;
        var expectedResponseBytes = expectedSize - existing;
        if (response.Content.Headers.ContentLength is long length && length != expectedResponseBytes)
            throw new InvalidDataException("La taille téléchargée ne correspond pas au manifeste.");

        try
        {
            long total = existing;
            string actualHash;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                if (append)
                {
                    await using var partial = File.OpenRead(temporary);
                    var prior = new byte[256 * 1024];
                    while (true)
                    {
                        var priorRead = await partial.ReadAsync(prior, cancellationToken).ConfigureAwait(false);
                        if (priorRead == 0) break;
                        hash.AppendData(prior, 0, priorRead);
                    }
                }
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(temporary, append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 256 * 1024, true);
                var buffer = new byte[256 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > expectedSize || total > MaximumPackageBytes)
                        throw new InvalidDataException("Le téléchargement PartyBoard dépasse la taille annoncée.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    progress?.Report(0.05 + 0.75 * total / expectedSize);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if (total != expectedSize || !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                throw new CryptographicException("Le paquet PartyBoard téléchargé ne correspond pas au manifeste.");
            }
            File.Move(temporary, destination, true);
        }
        catch (InvalidDataException)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    private static string ParseChecksum(string text, string fileName)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (parts[^1].TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].Trim();
        }
        return "";
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidDataException("Chemin de lancement PartyBoard invalide.");
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Chemin de lancement PartyBoard non sûr.");
        return normalized;
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException("Identifiant GitHub invalide.", name);
    }

    private static string SanitizeSegment(string value)
    {
        var sanitized = new string(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private sealed class PartyBoardManifest
    {
        public int Schema { get; set; }
        public string Version { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Executable { get; set; } = "";
        public Dictionary<string, PartyBoardArtifact> Artifacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PartyBoardArtifact
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Launch { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }

    private sealed class RuntimeStateFile
    {
        public string Version { get; set; } = "";
        public string RelativeExecutable { get; set; } = "";
    }
}
