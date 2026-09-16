using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Releases;

public sealed record PartyBoardInstallResult(string Version, string ExecutablePath, bool Verified = true);

public sealed record PartyBoardRuntimeStatus(
    bool IsInstalled,
    bool NeedsRepair,
    string Version,
    string ExecutablePath,
    bool Verified = true);

/// <summary>
/// Where a game's runtime comes from and how far CubeShelf can verify it.
/// Built from a <see cref="GameCatalogEntry"/> by <see cref="FromCatalog"/>.
/// </summary>
public sealed record GameRuntimeSource(
    string Owner,
    string Repository,
    string Tag,
    RuntimeSourceKind Kind,
    IReadOnlyDictionary<string, string> AssetPatterns,
    IReadOnlyDictionary<string, string> LaunchPaths,
    string PinnedSha256,
    string ChecksumAsset)
{
    public static GameRuntimeSource FromCatalog(GameCatalogEntry game) => new(
        game.GitHubOwner,
        game.GitHubRepo,
        game.GitHubReleaseTag,
        game.RuntimeSource,
        game.RuntimeAssets,
        game.RuntimeLaunchPaths,
        game.RuntimeSha256,
        game.RuntimeChecksumAsset);
}

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

    public Task<PartyBoardInstallResult> InstallLatestAsync(
        string owner,
        string repository,
        string tag,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false) =>
        InstallLatestAsync(
            new GameRuntimeSource(owner, repository, tag, RuntimeSourceKind.Manifest,
                new Dictionary<string, string>(), new Dictionary<string, string>(), "", ""),
            gameId, progress, cancellationToken, force);

    public async Task<PartyBoardInstallResult> InstallLatestAsync(
        GameRuntimeSource source,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateIdentifier(source.Owner, nameof(source.Owner));
        ValidateIdentifier(source.Repository, nameof(source.Repository));
        ValidateIdentifier(gameId, nameof(gameId));

        var resolved = source.Kind == RuntimeSourceKind.GitHubReleaseAsset
            ? await ResolveReleaseAssetAsync(source, cancellationToken).ConfigureAwait(false)
            : await ResolveManifestAsync(source, cancellationToken).ConfigureAwait(false);

        var artifact = resolved.Artifact;
        ValidateArtifact(artifact, resolved.RequireSize, resolved.Verified);
        progress?.Report(0.05);

        var safeVersion = SanitizeSegment(resolved.Version);
        var runtimeRoot = RuntimeRoot(gameId);
        var versionsRoot = Path.Combine(runtimeRoot, "versions");
        var destination = Path.Combine(versionsRoot, safeVersion);
        var expectedExecutable = Path.Combine(destination, NormalizeRelativePath(artifact.Launch));
        if (!force && File.Exists(expectedExecutable))
        {
            WriteState(runtimeRoot, resolved.Version, artifact.Launch, resolved.Verified);
            progress?.Report(1);
            return new(resolved.Version, expectedExecutable, resolved.Verified);
        }

        var cacheDirectory = Path.Combine(_paths.CacheDirectory, "downloads");
        Directory.CreateDirectory(cacheDirectory);
        var package = Path.Combine(cacheDirectory, artifact.Name);
        if (resolved.LegacyDownload)
            await DownloadLegacyVerifiedAsync(resolved.DownloadUri, package, artifact.Sha256, progress, cancellationToken)
                .ConfigureAwait(false);
        else
            await DownloadVerifiedAsync(resolved.DownloadUri, package, artifact.Size, artifact.Sha256,
                progress, cancellationToken).ConfigureAwait(false);

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
            else if (artifact.Kind.Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                     artifact.Kind.Equals("archive", StringComparison.OrdinalIgnoreCase))
            {
                SecureArchiveExtractor.Extract(package, staging,
                    new ArchiveExtractionLimits(20_000, MaximumPackageBytes * 3, MaximumPackageBytes));
            }
            else
            {
                throw new InvalidDataException($"Type de paquet non pris en charge : {artifact.Kind}.");
            }

            var relativeExecutable = ResolveStagedExecutable(staging, artifact.Launch);
            var stagedExecutable = Path.Combine(staging, relativeExecutable);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stagedExecutable, File.GetUnixFileMode(stagedExecutable) |
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(staging, destination);
            WriteState(runtimeRoot, resolved.Version, relativeExecutable, resolved.Verified);
            CleanupOtherVersions(versionsRoot, destination);
            progress?.Report(1);
            return new(resolved.Version, Path.Combine(destination, relativeExecutable), resolved.Verified);
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

    public Task<PartyBoardInstallResult> RepairLatestAsync(
        GameRuntimeSource source,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        InstallLatestAsync(source, gameId, progress, cancellationToken, force: true);

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
            return new(File.Exists(executable), !File.Exists(executable), state.Version, executable, state.Verified);
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

    private void WriteState(string runtimeRoot, string version, string relativeExecutable, bool verified)
    {
        Directory.CreateDirectory(runtimeRoot);
        var path = Path.Combine(runtimeRoot, "runtime-state.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            new RuntimeStateFile
            {
                Version = version,
                RelativeExecutable = relativeExecutable,
                Verified = verified
            }, _json));
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

    /// <summary>
    /// The declared launch path wins. When it is absent -- a publisher renamed the root folder,
    /// or moved the binary between releases -- fall back to the single file with that name in the
    /// package, rather than failing an otherwise good download.
    /// </summary>
    private static string ResolveStagedExecutable(string staging, string declaredLaunch)
    {
        var declared = NormalizeRelativePath(declaredLaunch);
        if (File.Exists(Path.Combine(staging, declared))) return declared;

        var fileName = Path.GetFileName(declared);
        var matches = Directory
            .EnumerateFiles(staging, fileName, SearchOption.AllDirectories)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Le paquet ne contient pas l’exécutable annoncé ({declared}).");

        return Path.GetRelativePath(staging, matches[0]);
    }

    private async Task<ResolvedRuntime> ResolveManifestAsync(
        GameRuntimeSource source,
        CancellationToken cancellationToken)
    {
        // A fixed tag is one URL that a publisher has to keep pointing at the
        // newest build forever. PartyBoard moved to a release per tag and the
        // launcher went on asking for the old rolling one, so it reinstalled a
        // months-old build over anything newer, every startup check. A catalog
        // can now say "latest" and be told by GitHub which tag that is.
        var tag = source.Tag.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? await ResolveLatestTagAsync(source, cancellationToken).ConfigureAwait(false)
            : source.Tag;
        ValidateIdentifier(tag, nameof(source.Tag));
        var releaseRoot = new Uri(
            $"https://github.com/{source.Owner}/{source.Repository}/releases/download/{tag}/");
        var manifest = await DownloadManifestAsync(new Uri(releaseRoot, "manifest.json"), cancellationToken)
            .ConfigureAwait(false);
        var artifact = manifest.Schema == 1
            ? await ResolveSchema1ArtifactAsync(releaseRoot, manifest, cancellationToken).ConfigureAwait(false)
            : SelectArtifact(manifest);

        return new ResolvedRuntime(
            manifest.Version,
            artifact,
            new Uri(releaseRoot, Uri.EscapeDataString(artifact.Name)),
            LegacyDownload: manifest.Schema == 1,
            RequireSize: manifest.Schema != 1,
            Verified: true);
    }

    /// <summary>
    /// The tag of the most recent release, for a catalog that says "latest" rather than naming
    /// one. Draft and pre-release builds are what PartyBoard publishes, and /releases/latest
    /// skips both, so this reads the release list and takes the first entry GitHub returns.
    /// </summary>
    private async Task<string> ResolveLatestTagAsync(
        GameRuntimeSource source,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(source.Owner, nameof(source.Owner));
        ValidateIdentifier(source.Repository, nameof(source.Repository));
        var api = new Uri(
            $"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases?per_page=1");
        using var request = new HttpRequestMessage(HttpMethod.Get, api);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
            throw new InvalidDataException("Réponse GitHub trop volumineuse.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, _json, cancellationToken)
            .ConfigureAwait(false);
        var tag = releases?.FirstOrDefault()?.TagName;
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidDataException($"{source.Repository} ne publie aucune release exploitable.");
        return tag;
    }

    /// <summary>
    /// Resolves a runtime from a repository that publishes plain release assets and no CubeShelf
    /// manifest. Verification comes from whatever the publisher offers, in order: a checksum file
    /// in the same release (Strikers ships SHA256SUMS), then a SHA-256 pinned in the catalog.
    /// With neither, the install still runs but is recorded as unverified.
    /// </summary>
    private async Task<ResolvedRuntime> ResolveReleaseAssetAsync(
        GameRuntimeSource source,
        CancellationToken cancellationToken)
    {
        var runtimeId = CurrentRuntimeId();
        if (!source.AssetPatterns.TryGetValue(runtimeId, out var pattern) || string.IsNullOrWhiteSpace(pattern))
            throw new PlatformNotSupportedException(
                $"{source.Repository} ne publie pas d’artefact pour {runtimeId}.");

        var api = new Uri($"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, api);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
            throw new InvalidDataException("Réponse GitHub trop volumineuse.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, _json, cancellationToken)
            .ConfigureAwait(false);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidDataException($"Aucune release exploitable pour {source.Repository}.");

        var asset = release.Assets.FirstOrDefault(candidate => ReleaseAssetPattern.Matches(candidate.Name, pattern))
            ?? throw new InvalidDataException(
                $"La release {release.TagName} ne contient aucun artefact correspondant à {pattern}.");
        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new InvalidDataException("L’artefact publié n’a pas d’URL de téléchargement.");

        var version = release.TagName.TrimStart('v', 'V');
        if (!source.LaunchPaths.TryGetValue(runtimeId, out var declaredLaunch) || string.IsNullOrWhiteSpace(declaredLaunch))
            throw new InvalidDataException(
                $"Le catalogue ne déclare pas de chemin de lancement {runtimeId} pour {source.Repository}.");
        var launch = declaredLaunch.Replace("{version}", version, StringComparison.OrdinalIgnoreCase);

        // A checksum published in the same release is the best evidence available here: it comes
        // from the publisher and moves with every version, unlike a hash pinned in the catalog
        // which goes stale on the next release.
        var sha256 = await ResolvePublishedChecksumAsync(source, release, asset.Name, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sha256))
            sha256 = source.PinnedSha256 ?? "";

        return new ResolvedRuntime(
            version,
            new PartyBoardArtifact
            {
                Name = asset.Name,
                Kind = ArchiveKindFor(asset.Name),
                Launch = launch,
                Sha256 = sha256,
                Size = asset.Size
            },
            new Uri(asset.BrowserDownloadUrl),
            LegacyDownload: false,
            RequireSize: true,
            Verified: !string.IsNullOrWhiteSpace(sha256));
    }

    /// <summary>
    /// Reads the release's checksum file, when the catalog names one, and returns the SHA-256 it
    /// lists for this artifact. A missing or unparsable file is not fatal: the install falls back
    /// to the pinned hash, or to being recorded as unverified.
    /// </summary>
    private async Task<string> ResolvePublishedChecksumAsync(
        GameRuntimeSource source,
        GitHubRelease release,
        string assetName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ChecksumAsset)) return "";

        var checksumAsset = release.Assets.FirstOrDefault(candidate =>
            candidate.Name.Equals(source.ChecksumAsset, StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null || string.IsNullOrWhiteSpace(checksumAsset.BrowserDownloadUrl)) return "";

        try
        {
            using var response = await _http.GetAsync(new Uri(checksumAsset.BrowserDownloadUrl),
                HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return "";
            if (response.Content.Headers.ContentLength > 1024 * 1024) return "";

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var parsed = ParseChecksum(text, assetName);
            return parsed.Length == 64 && parsed.All(Uri.IsHexDigit) ? parsed.ToLowerInvariant() : "";
        }
        catch (HttpRequestException)
        {
            return "";
        }
    }

    /// <summary>
    /// Archive type from the asset's name. SharpCompress detects the container itself, so this
    /// only has to separate an archive from a bare AppImage.
    /// </summary>
    private static string ArchiveKindFor(string assetName) =>
        assetName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) ? "appimage" : "archive";



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

    private static void ValidateArtifact(PartyBoardArtifact artifact, bool requireSize = true, bool requireHash = true)
    {
        if (string.IsNullOrWhiteSpace(artifact.Name) || artifact.Name != Path.GetFileName(artifact.Name))
            throw new InvalidDataException("Nom d’artefact invalide.");
        if (requireSize && artifact.Size is <= 0 or > MaximumPackageBytes)
            throw new InvalidDataException("Taille d’artefact invalide.");
        if (!requireSize && artifact.Size > MaximumPackageBytes)
            throw new InvalidDataException("Taille d’artefact invalide.");
        if (requireHash && (artifact.Sha256.Length != 64 || artifact.Sha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("SHA-256 invalide.");
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

    /// <summary>
    /// Downloads with HTTP Range resume. An empty <paramref name="expectedHash"/> means the
    /// publisher offers nothing to check against: the bytes are still hashed and size-checked,
    /// but the result cannot be called verified.
    /// </summary>
    private async Task DownloadVerifiedAsync(Uri uri, string destination, long expectedSize, string expectedHash,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var comparing = !string.IsNullOrWhiteSpace(expectedHash);
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
            if (!comparing || completedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
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
            throw new InvalidDataException("La taille téléchargée ne correspond pas à celle annoncée.");

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
                        throw new InvalidDataException("Le téléchargement dépasse la taille annoncée.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    progress?.Report(0.05 + 0.75 * total / expectedSize);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if (total != expectedSize || (comparing && !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(temporary);
                throw new CryptographicException("Le paquet téléchargé ne correspond pas à ce qui était annoncé.");
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
            throw new InvalidDataException("Chemin de lancement invalide.");
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Chemin de lancement non sûr.");
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

    private sealed record ResolvedRuntime(
        string Version,
        PartyBoardArtifact Artifact,
        Uri DownloadUri,
        bool LegacyDownload,
        bool RequireSize,
        bool Verified);

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

    private sealed class GitHubRelease
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAsset
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }

    private sealed class RuntimeStateFile
    {
        public string Version { get; set; } = "";
        public string RelativeExecutable { get; set; } = "";
        public bool Verified { get; set; } = true;
    }
}

/// <summary>
/// </summary>
