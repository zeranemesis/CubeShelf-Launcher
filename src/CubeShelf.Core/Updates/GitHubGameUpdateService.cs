using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Security;

namespace CubeShelf.Core.Updates;

public sealed record GitHubCommitSnapshot(
    string Sha,
    string Message,
    DateTimeOffset Date,
    string Url);

public sealed record SourceRepositorySnapshot(
    bool Present,
    bool ManagedByCubeShelf,
    string Path,
    string Commit);

public sealed record PartyBoardReleaseSnapshot(
    bool Available,
    string Commit,
    string Version,
    string RuntimeId);

public sealed record GameUpdateSnapshot(
    bool Configured,
    GitHubCommitSnapshot? LatestSource,
    PartyBoardReleaseSnapshot RuntimeRelease,
    SourceRepositorySnapshot LocalSource,
    string InstalledRuntimeCommit,
    bool InstalledCommitInferred,
    bool RuntimeUpdateAvailable,
    bool RuntimeUpdatePendingBuild,
    bool SourceUpdateAvailable,
    IReadOnlyList<GitHubCommitSnapshot> Changes);

/// <summary>
/// Portable equivalent of the WPF GitHub update logic. Source-code freshness
/// and playable PartyBoard releases are deliberately separate: a newer source
/// commit may exist while the public runtime is still waiting for CI/building.
/// </summary>
public sealed class GitHubGameUpdateService : IDisposable
{
    private const long MaximumSourceArchiveBytes = 1024L * 1024 * 1024;
    private static readonly ArchiveExtractionLimits SourceExtractionLimits =
        new(100_000, 8L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024);

    private readonly HttpClient _http;
    private readonly IPlatformPaths _paths;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly Dictionary<string, (DateTimeOffset Expires, IReadOnlyList<GitHubCommitSnapshot> Commits)> _commitCache =
        new(StringComparer.OrdinalIgnoreCase);

    public GitHubGameUpdateService(IPlatformPaths paths, HttpClient? httpClient = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.8-avalonia");
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<GameUpdateSnapshot> CheckAsync(
        GameCatalogEntry game,
        bool runtimeInstalled,
        string installedRuntimeVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!IsConfigured(game))
        {
            return new(
                false, null,
                new(false, "", "", ""),
                new(false, false, "", ""),
                "", false, false, false, false,
                Array.Empty<GitHubCommitSnapshot>());
        }

        var commitsTask = GetCommitFeedAsync(game, cancellationToken);
        var releaseTask = GetRuntimeReleaseAsync(game, cancellationToken);
        await Task.WhenAll(commitsTask, releaseTask).ConfigureAwait(false);

        var commits = await commitsTask.ConfigureAwait(false);
        var release = await releaseTask.ConfigureAwait(false);
        var latest = commits.FirstOrDefault();
        var localSource = DetectLocalRepository(game);
        var metadata = ReadRuntimeMetadata(game.Id);

        var installedCommit = metadata?.Commit ?? "";
        var inferred = false;
        if (runtimeInstalled &&
            string.IsNullOrWhiteSpace(installedCommit) &&
            release.Available &&
            SameCommitOrVersion(installedRuntimeVersion, release.Version))
        {
            installedCommit = release.Commit;
            inferred = !string.IsNullOrWhiteSpace(installedCommit);
        }

        var runtimeUpdate = false;
        if (release.Available)
        {
            if (!runtimeInstalled)
            {
                runtimeUpdate = true;
            }
            else if (LooksLikeSha(installedCommit) && LooksLikeSha(release.Commit))
            {
                runtimeUpdate = !SameCommit(installedCommit, release.Commit);
            }
            else if (!string.IsNullOrWhiteSpace(release.Version) &&
                     !string.Equals(installedRuntimeVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                runtimeUpdate = true;
            }
        }

        var pendingBuild = latest is not null &&
                           LooksLikeSha(latest.Sha) &&
                           release.Available &&
                           LooksLikeSha(release.Commit) &&
                           !SameCommit(latest.Sha, release.Commit);

        var sourceUpdate = localSource.Present &&
                           latest is not null &&
                           LooksLikeSha(localSource.Commit) &&
                           !SameCommit(localSource.Commit, latest.Sha);

        var baseline = LooksLikeSha(installedCommit)
            ? installedCommit
            : localSource.Commit;
        var changes = GetChanges(commits, baseline);

        return new(
            true,
            latest,
            release,
            localSource,
            installedCommit,
            inferred,
            runtimeUpdate,
            pendingBuild,
            sourceUpdate,
            changes);
    }

    public async Task<PartyBoardReleaseSnapshot> GetRuntimeReleaseAsync(
        GameCatalogEntry game,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(game))
            return new(false, "", "", "");

        var tag = Uri.EscapeDataString(game.GitHubReleaseTag);
        var manifestUrl =
            $"https://github.com/{game.GitHubOwner}/{game.GitHubRepo}/releases/download/{tag}/manifest.json";

        using var response = await _http.GetAsync(
            manifestUrl,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new(false, "", "", CurrentRuntimeId());

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (!root.TryGetProperty("schema", out var schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out var schema) ||
            (schema != 1 && schema != 2))
            throw new InvalidDataException("Manifeste PartyBoard invalide ou non pris en charge.");

        var commit = root.TryGetProperty("commit", out var commitElement)
            ? commitElement.GetString() ?? ""
            : "";
        var version = root.TryGetProperty("version", out var versionElement)
            ? versionElement.GetString() ?? game.GitHubReleaseTag
            : game.GitHubReleaseTag;
        var runtimeId = CurrentRuntimeId();

        // The existing public cubeshelf-nightly release was generated by the
        // first Windows-only workflow and therefore still uses schema 1. Keep
        // it installable until CI replaces it with the newer schema-2 release.
        if (schema == 1)
        {
            var platform = root.TryGetProperty("platform", out var platformElement)
                ? platformElement.GetString() ?? ""
                : "";
            var executable = root.TryGetProperty("executable", out var executableElement)
                ? executableElement.GetString() ?? ""
                : "";
            var assetName = string.IsNullOrWhiteSpace(game.GitHubReleaseAssetName)
                ? $"PartyBoard-{runtimeId}.zip"
                : game.GitHubReleaseAssetName;

            var available = platform.Equals(runtimeId, StringComparison.OrdinalIgnoreCase) &&
                            IsSafeRelativePath(executable) &&
                            !string.IsNullOrWhiteSpace(assetName) &&
                            assetName == Path.GetFileName(assetName);
            return new(available, commit, version, runtimeId);
        }

        var artifactAvailable = false;
        if (root.TryGetProperty("artifacts", out var artifacts) &&
            artifacts.ValueKind == JsonValueKind.Object &&
            artifacts.TryGetProperty(runtimeId, out var artifact) &&
            artifact.ValueKind == JsonValueKind.Object)
        {
            var name = artifact.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "" : "";
            var kind = artifact.TryGetProperty("kind", out var kindElement)
                ? kindElement.GetString() ?? "" : "";
            var launch = artifact.TryGetProperty("launch", out var launchElement)
                ? launchElement.GetString() ?? "" : "";
            var sha256 = artifact.TryGetProperty("sha256", out var shaElement)
                ? shaElement.GetString() ?? "" : "";
            var size = artifact.TryGetProperty("size", out var sizeElement) &&
                       sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;

            artifactAvailable = !string.IsNullOrWhiteSpace(name) &&
                                name == Path.GetFileName(name) &&
                                (kind.Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                                 kind.Equals("appimage", StringComparison.OrdinalIgnoreCase) ||
                                 kind.Equals("app-bundle-zip", StringComparison.OrdinalIgnoreCase)) &&
                                IsSafeRelativePath(launch) &&
                                sha256.Length == 64 && sha256.All(Uri.IsHexDigit) &&
                                size > 0;
        }

        return new(artifactAvailable, commit, version, runtimeId);
    }

    public void RecordInstalledRuntime(GameCatalogEntry game, PartyBoardReleaseSnapshot release)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!release.Available) return;
        var path = RuntimeMetadataPath(game.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicWrite(path, JsonSerializer.Serialize(
            new RuntimeBuildMetadata(release.Commit, release.Version, DateTimeOffset.UtcNow), _json));
    }

    public SourceRepositorySnapshot DetectLocalRepository(GameCatalogEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var state = ReadSourceState(game);
        if (!string.IsNullOrWhiteSpace(state.SourcePath) &&
            Directory.Exists(state.SourcePath) &&
            LooksLikeRepository(state.SourcePath, game))
        {
            return new(
                true,
                IsManagedPath(game, state.SourcePath),
                state.SourcePath,
                FirstNonEmpty(state.Commit, TryReadGitSha(state.SourcePath)));
        }

        var versions = VersionsRoot(game);
        var recovered = Directory.Exists(versions)
            ? Directory.EnumerateDirectories(versions)
                .Where(path => LooksLikeRepository(path, game))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        if (!string.IsNullOrWhiteSpace(recovered))
        {
            var sha = TryReadGitSha(recovered);
            if (string.IsNullOrWhiteSpace(sha))
            {
                var folder = Path.GetFileName(recovered);
                if (LooksLikeSha(folder)) sha = folder;
            }
            return new(true, true, recovered, sha);
        }

        foreach (var start in CandidateSearchRoots(game))
        {
            string? current = start;
            for (var depth = 0; depth < 7 && current is not null; depth++)
            {
                if (LooksLikeRepository(current, game))
                    return new(true, false, current, TryReadGitSha(current));
                current = Directory.GetParent(current)?.FullName;
            }
        }

        return new(false, false, "", "");
    }

    public async Task<string> DownloadLatestSourceAsync(
        GameCatalogEntry game,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commits = await GetCommitFeedAsync(game, cancellationToken).ConfigureAwait(false);
        var latest = commits.FirstOrDefault()
            ?? throw new InvalidOperationException("GitHub n'a retourné aucun commit pour la branche configurée.");

        var local = DetectLocalRepository(game);
        if (local.Present && LooksLikeSha(local.Commit) && SameCommit(local.Commit, latest.Sha))
        {
            progress?.Report(1);
            return local.Path;
        }

        var shortSha = latest.Sha[..Math.Min(12, latest.Sha.Length)];
        var target = Path.Combine(VersionsRoot(game), shortSha);
        if (Directory.Exists(target) && LooksLikeRepository(target, game))
        {
            WriteSourceState(game, latest.Sha, latest.Message, target);
            progress?.Report(1);
            return target;
        }

        var zip = Path.Combine(Path.GetTempPath(), $"cubeshelf-{game.Id}-{Guid.NewGuid():N}.zip");
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var branch = string.Join("/", game.GitHubBranch
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var archiveUrl =
            $"https://github.com/{game.GitHubOwner}/{game.GitHubRepo}/archive/refs/heads/{branch}.zip";

        try
        {
            using (var response = await _http.GetAsync(
                       archiveUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                if (total > MaximumSourceArchiveBytes)
                    throw new InvalidDataException("L’archive source dépasse la taille autorisée.");

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = File.Create(zip);
                var buffer = new byte[256 * 1024];
                long done = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    if (done > MaximumSourceArchiveBytes)
                        throw new InvalidDataException("L’archive source dépasse la taille autorisée.");
                    if (total is > 0)
                        progress?.Report(Math.Min(.72, (double)done / total.Value * .72));
                }
            }

            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            progress?.Report(.76);
            SecureArchiveExtractor.Extract(zip, staging, SourceExtractionLimits);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(.90);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var directories = Directory.GetDirectories(staging);
            var files = Directory.GetFiles(staging);
            if (Directory.Exists(target)) Directory.Delete(target, true);

            if (directories.Length == 1 && files.Length == 0)
            {
                Directory.Move(directories[0], target);
                Directory.Delete(staging, true);
            }
            else
            {
                Directory.Move(staging, target);
            }

            WriteSourceState(game, latest.Sha, latest.Message, target);
            CleanupOldSourceVersions(game, target);
            progress?.Report(1);
            return target;
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    public bool DeleteManagedSource(GameCatalogEntry game)
    {
        var local = DetectLocalRepository(game);
        if (!local.Present) return false;
        if (!local.ManagedByCubeShelf)
            throw new InvalidOperationException(
                "Ce dépôt source est externe à CubeShelf. Il ne sera jamais supprimé automatiquement.");

        var root = GameDataRoot(game);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        return true;
    }

    private async Task<IReadOnlyList<GitHubCommitSnapshot>> GetCommitFeedAsync(
        GameCatalogEntry game,
        CancellationToken cancellationToken)
    {
        var key = $"{game.GitHubOwner}/{game.GitHubRepo}:{game.GitHubBranch}";
        lock (_commitCache)
        {
            if (_commitCache.TryGetValue(key, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
                return cached.Commits;
        }

        IReadOnlyList<GitHubCommitSnapshot> commits;
        try
        {
            commits = await DownloadAtomCommitFeedAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            commits = await DownloadRestCommitFeedAsync(game, cancellationToken).ConfigureAwait(false);
        }

        lock (_commitCache)
            _commitCache[key] = (DateTimeOffset.UtcNow.AddMinutes(3), commits);
        return commits;
    }

    private async Task<IReadOnlyList<GitHubCommitSnapshot>> DownloadAtomCommitFeedAsync(
        GameCatalogEntry game,
        CancellationToken cancellationToken)
    {
        var branch = string.Join("/", game.GitHubBranch
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var url = $"https://github.com/{game.GitHubOwner}/{game.GitHubRepo}/commits/{branch}.atom";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/atom+xml, application/xml;q=0.9");
        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var document = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var result = new List<GitHubCommitSnapshot>();
        foreach (var entry in document.Descendants(atom + "entry"))
        {
            var link = entry.Elements(atom + "link")
                           .FirstOrDefault(element => string.Equals(
                               (string?)element.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                       ?? entry.Elements(atom + "link").FirstOrDefault();
            var href = (string?)link?.Attribute("href") ?? "";
            const string marker = "/commit/";
            var index = href.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var sha = href[(index + marker.Length)..]
                .Split(new[] { '?', '#', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (!LooksLikeSha(sha)) continue;
            var message = (entry.Element(atom + "title")?.Value ?? "").Trim();
            var updated = entry.Element(atom + "updated")?.Value ?? "";
            var date = DateTimeOffset.TryParse(updated, out var parsed) ? parsed : DateTimeOffset.MinValue;
            result.Add(new(sha, message, date, href));
        }

        if (result.Count == 0)
            throw new InvalidDataException("Le flux de commits GitHub est vide ou illisible.");
        return result;
    }

    private async Task<IReadOnlyList<GitHubCommitSnapshot>> DownloadRestCommitFeedAsync(
        GameCatalogEntry game,
        CancellationToken cancellationToken)
    {
        var branch = Uri.EscapeDataString(game.GitHubBranch);
        var url = $"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/commits?sha={branch}&per_page=15";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new InvalidOperationException(
                "GitHub limite temporairement les requêtes. CubeShelf réessaiera automatiquement plus tard.");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.EnumerateArray().Select(ParseCommit).ToArray();
    }

    private static GitHubCommitSnapshot ParseCommit(JsonElement element)
    {
        var sha = element.TryGetProperty("sha", out var shaElement) ? shaElement.GetString() ?? "" : "";
        var url = element.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
        var message = "";
        var date = DateTimeOffset.MinValue;
        if (element.TryGetProperty("commit", out var commit))
        {
            if (commit.TryGetProperty("message", out var messageElement))
                message = (messageElement.GetString() ?? "").Split('\n')[0].Trim();
            if (commit.TryGetProperty("committer", out var committer) &&
                committer.TryGetProperty("date", out var dateElement) &&
                DateTimeOffset.TryParse(dateElement.GetString(), out var parsed))
                date = parsed;
        }
        return new(sha, message, date, url);
    }

    private static IReadOnlyList<GitHubCommitSnapshot> GetChanges(
        IReadOnlyList<GitHubCommitSnapshot> commits,
        string baseline)
    {
        if (commits.Count == 0) return Array.Empty<GitHubCommitSnapshot>();
        if (!LooksLikeSha(baseline)) return commits.Take(8).ToArray();
        var result = new List<GitHubCommitSnapshot>();
        foreach (var commit in commits)
        {
            if (SameCommit(commit.Sha, baseline)) break;
            result.Add(commit);
            if (result.Count >= 15) break;
        }
        return result;
    }

    private string GameDataRoot(GameCatalogEntry game)
    {
        var root = Path.Combine(_paths.DataDirectory, "Games", game.Id, "GitHub");
        Directory.CreateDirectory(root);
        return root;
    }

    private string VersionsRoot(GameCatalogEntry game)
    {
        var root = Path.Combine(GameDataRoot(game), "versions");
        Directory.CreateDirectory(root);
        return root;
    }

    private string SourceStatePath(GameCatalogEntry game) => Path.Combine(GameDataRoot(game), "state.json");
    private string RuntimeMetadataPath(string gameId) =>
        Path.Combine(_paths.DataDirectory, "Runtimes", gameId, "cubeshelf-build-source.json");

    private RuntimeBuildMetadata? ReadRuntimeMetadata(string gameId)
    {
        var path = RuntimeMetadataPath(gameId);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<RuntimeBuildMetadata>(File.ReadAllText(path), _json)
                : null;
        }
        catch { return null; }
    }

    private SourceState ReadSourceState(GameCatalogEntry game)
    {
        var path = SourceStatePath(game);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SourceState>(File.ReadAllText(path), _json) ?? new("", "", "")
                : new("", "", "");
        }
        catch { return new("", "", ""); }
    }

    private void WriteSourceState(GameCatalogEntry game, string commit, string message, string sourcePath) =>
        AtomicWrite(SourceStatePath(game), JsonSerializer.Serialize(new SourceState(commit, message, sourcePath), _json));

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private IEnumerable<string> CandidateSearchRoots(GameCatalogEntry game)
    {
        var executable = ResolveConfiguredPath(game.Executable);
        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            var directory = Path.GetDirectoryName(executable);
            if (!string.IsNullOrWhiteSpace(directory)) yield return directory;
        }

        var gameRoot = ResolveConfiguredPath(game.GameRoot);
        if (!string.IsNullOrWhiteSpace(gameRoot) && Directory.Exists(gameRoot))
            yield return gameRoot;
    }

    private static string ResolveConfiguredPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }
        catch
        {
            return "";
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return false;
        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return normalized.Split(Path.DirectorySeparatorChar)
            .All(part => part is not ("" or "." or ".."));
    }

    private bool IsManagedPath(GameCatalogEntry game, string path)
    {
        try
        {
            var root = Path.GetFullPath(GameDataRoot(game)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(
                root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private bool LooksLikeRepository(string path, GameCatalogEntry game)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            var hasProjectFiles = File.Exists(Path.Combine(path, "CMakeLists.txt")) && Directory.Exists(Path.Combine(path, "src"));
            var hasGit = Directory.Exists(Path.Combine(path, ".git"));
            if (!hasProjectFiles && !hasGit) return false;

            var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (directoryName.Equals(game.GitHubRepo, StringComparison.OrdinalIgnoreCase)) return true;

            var configPath = Path.Combine(path, ".git", "config");
            if (File.Exists(configPath))
            {
                var config = File.ReadAllText(configPath);
                if (config.Contains($"github.com/{game.GitHubOwner}/{game.GitHubRepo}", StringComparison.OrdinalIgnoreCase) ||
                    config.Contains($"github.com:{game.GitHubOwner}/{game.GitHubRepo}", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return hasProjectFiles && IsManagedPath(game, path);
        }
        catch { return false; }
    }

    private static string TryReadGitSha(string repositoryPath)
    {
        try
        {
            var git = Path.Combine(repositoryPath, ".git");
            var headPath = Path.Combine(git, "HEAD");
            if (!File.Exists(headPath)) return "";
            var head = File.ReadAllText(headPath).Trim();
            if (!head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                return LooksLikeSha(head) ? head : "";
            var reference = head[4..].Trim();
            var loose = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
            var packed = Path.Combine(git, "packed-refs");
            if (!File.Exists(packed)) return "";
            foreach (var line in File.ReadLines(packed))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('^')) continue;
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].Equals(reference, StringComparison.OrdinalIgnoreCase))
                    return parts[0];
            }
        }
        catch { }
        return "";
    }

    private static bool IsConfigured(GameCatalogEntry game) =>
        !string.IsNullOrWhiteSpace(game.GitHubOwner) &&
        !string.IsNullOrWhiteSpace(game.GitHubRepo) &&
        !string.IsNullOrWhiteSpace(game.GitHubBranch) &&
        !string.IsNullOrWhiteSpace(game.GitHubReleaseTag);

    private static bool LooksLikeSha(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 7 and <= 40 &&
        value.All(character => Uri.IsHexDigit(character));

    private static bool SameCommit(string left, string right) =>
        LooksLikeSha(left) && LooksLikeSha(right) &&
        (left.StartsWith(right, StringComparison.OrdinalIgnoreCase) ||
         right.StartsWith(left, StringComparison.OrdinalIgnoreCase));

    private static bool SameCommitOrVersion(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string CurrentRuntimeId()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };
        return OperatingSystem.IsWindows()
            ? $"win-{architecture}"
            : OperatingSystem.IsLinux()
                ? $"linux-{architecture}"
                : $"osx-{architecture}";
    }

    private void CleanupOldSourceVersions(GameCatalogEntry game, string current)
    {
        try
        {
            foreach (var directory in Directory.GetDirectories(VersionsRoot(game))
                         .OrderByDescending(Directory.GetCreationTimeUtc)
                         .Skip(2))
            {
                if (!Path.GetFullPath(directory).Equals(
                        Path.GetFullPath(current),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    Directory.Delete(directory, true);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    private sealed record SourceState(string Commit, string Message, string SourcePath);
    private sealed record RuntimeBuildMetadata(string Commit, string Version, DateTimeOffset InstalledAt);
}
