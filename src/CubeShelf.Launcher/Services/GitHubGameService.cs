using SharpCompress.Archives;
using SharpCompress.Common;

namespace CubeShelf.Launcher.Services;

public sealed record GitHubCommitInfo(
    string Sha,
    string Message,
    DateTimeOffset Date,
    string Url);

public sealed record GitHubGameStatus(
    bool Configured,
    bool Downloaded,
    bool UpdateAvailable,
    string CurrentSha,
    string LatestSha,
    string LatestMessage,
    DateTimeOffset LatestDate,
    string SourcePath,
    IReadOnlyList<GitHubCommitInfo> Changes);

public sealed record LocalRepositoryStatus(
    bool Present,
    bool ManagedByCubeShelf,
    string Path,
    string Sha);

public sealed class GitHubGameService
{
    private readonly HttpClient _http = new();
    private readonly JsonSerializerOptions _json =
        new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public GitHubGameService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.5");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    private static string GameDataRoot(GameDefinition game)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf", "Games", game.Id, "GitHub");

        Directory.CreateDirectory(root);
        return root;
    }

    private static string StateFile(GameDefinition game)
        => Path.Combine(GameDataRoot(game), "state.json");

    private static string VersionsRoot(GameDefinition game)
    {
        var path = Path.Combine(GameDataRoot(game), "versions");
        Directory.CreateDirectory(path);
        return path;
    }


    public LocalRepositoryStatus DetectLocalRepository(GameDefinition game)
    {
        // 1) Repository path already recorded in CubeShelf state.
        var state = ReadState(game);

        if (!string.IsNullOrWhiteSpace(state.SourcePath) &&
            Directory.Exists(state.SourcePath) &&
            LooksLikeRepository(state.SourcePath, game))
        {
            return new(
                true,
                IsManagedPath(game, state.SourcePath),
                state.SourcePath,
                FirstNonEmpty(state.Sha, TryReadGitSha(state.SourcePath)));
        }

        // 2) Recover a CubeShelf-managed repository if state.json was lost.
        var managed = Directory
            .EnumerateDirectories(VersionsRoot(game))
            .Where(path => LooksLikeRepository(path, game))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(managed))
        {
            var sha = TryReadGitSha(managed);

            if (string.IsNullOrWhiteSpace(sha))
            {
                var folderName = Path.GetFileName(managed);
                if (LooksLikeSha(folderName))
                    sha = folderName;
            }

            return new(true, true, managed, sha);
        }

        // 3) Detect an existing manual clone/source tree around partyboard.exe.
        foreach (var start in CandidateSearchRoots(game))
        {
            string? current = start;

            for (var depth = 0; depth < 7 && current is not null; depth++)
            {
                if (LooksLikeRepository(current, game))
                {
                    return new(
                        true,
                        false,
                        current,
                        TryReadGitSha(current));
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return new(false, false, "", "");
    }

    private static IEnumerable<string> CandidateSearchRoots(GameDefinition game)
    {
        if (!string.IsNullOrWhiteSpace(game.ExecutableFullPath) &&
            File.Exists(game.ExecutableFullPath))
        {
            var directory = Path.GetDirectoryName(game.ExecutableFullPath);

            if (!string.IsNullOrWhiteSpace(directory))
                yield return directory;
        }

        if (!string.IsNullOrWhiteSpace(game.GameRootFullPath) &&
            Directory.Exists(game.GameRootFullPath))
        {
            yield return game.GameRootFullPath;
        }
    }

    private static bool LooksLikeRepository(
        string path,
        GameDefinition game)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            var hasProjectFiles =
                File.Exists(Path.Combine(path, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(path, "src"));

            var hasGit = Directory.Exists(Path.Combine(path, ".git"));

            if (!hasProjectFiles && !hasGit)
                return false;

            var directoryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(path));

            if (directoryName.Equals(
                    game.GitHubRepo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var gitConfig = Path.Combine(path, ".git", "config");

            if (File.Exists(gitConfig))
            {
                var config = File.ReadAllText(gitConfig);

                var httpsIdentity =
                    $"github.com/{game.GitHubOwner}/{game.GitHubRepo}";

                var sshIdentity =
                    $"github.com:{game.GitHubOwner}/{game.GitHubRepo}";

                if (config.Contains(
                        httpsIdentity,
                        StringComparison.OrdinalIgnoreCase) ||
                    config.Contains(
                        sshIdentity,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // GitHub ZIP archives have no .git folder. CubeShelf-managed paths
            // are already scoped to the configured game id.
            return hasProjectFiles &&
                   path.Contains(
                       Path.Combine(
                           "CubeShelf",
                           "Games",
                           game.Id,
                           "GitHub"),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string TryReadGitSha(string repositoryPath)
    {
        try
        {
            var gitDirectory = Path.Combine(repositoryPath, ".git");
            var headPath = Path.Combine(gitDirectory, "HEAD");

            if (!File.Exists(headPath))
                return "";

            var head = File.ReadAllText(headPath).Trim();

            if (!head.StartsWith(
                    "ref:",
                    StringComparison.OrdinalIgnoreCase))
            {
                return LooksLikeSha(head) ? head : "";
            }

            var reference = head[4..].Trim();
            var looseReference = Path.Combine(
                gitDirectory,
                reference.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            if (File.Exists(looseReference))
                return File.ReadAllText(looseReference).Trim();

            var packedRefs = Path.Combine(
                gitDirectory,
                "packed-refs");

            if (File.Exists(packedRefs))
            {
                foreach (var line in File.ReadLines(packedRefs))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.StartsWith("#") ||
                        line.StartsWith("^"))
                    {
                        continue;
                    }

                    var parts = line.Split(
                        ' ',
                        2,
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 2 &&
                        parts[1].Equals(
                            reference,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return parts[0];
                    }
                }
            }
        }
        catch { }

        return "";
    }

    private static bool LooksLikeSha(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length < 7 ||
            value.Length > 40)
        {
            return false;
        }

        return value.All(c =>
            (c >= '0' && c <= '9') ||
            (c >= 'a' && c <= 'f') ||
            (c >= 'A' && c <= 'F'));
    }

    private static bool IsManagedPath(
        GameDefinition game,
        string path)
    {
        try
        {
            var managedRoot = Path.GetFullPath(
                GameDataRoot(game))
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            return Path.GetFullPath(path).StartsWith(
                managedRoot,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FirstNonEmpty(
        params string[] values)
        => values.FirstOrDefault(
            value => !string.IsNullOrWhiteSpace(value)) ?? "";

    public async Task<GitHubGameStatus> CheckAsync(GameDefinition game)
    {
        if (!game.GitHubConfigured)
        {
            return new(
                false, false, false, "", "", "", default, "",
                Array.Empty<GitHubCommitInfo>());
        }

        var latest = await GetLatestCommitAsync(game);
        var local = DetectLocalRepository(game);

        var localSha = local.Sha;
        var localPath = local.Path;

        var latestPath = Path.Combine(
            VersionsRoot(game),
            latest.Sha[..Math.Min(12, latest.Sha.Length)]);

        if (Directory.Exists(latestPath) &&
            LooksLikeRepository(latestPath, game))
        {
            local = new LocalRepositoryStatus(
                true,
                true,
                latestPath,
                latest.Sha);

            localSha = latest.Sha;
            localPath = latestPath;

            WriteState(
                game,
                latest.Sha,
                latest.Message,
                latestPath);
        }

        var changes = await GetChangesAsync(
            game,
            localSha,
            latest.Sha);

        var downloaded =
            local.Present &&
            Directory.Exists(localPath);

        var sameCommit =
            !string.IsNullOrWhiteSpace(localSha) &&
            (latest.Sha.StartsWith(
                 localSha,
                 StringComparison.OrdinalIgnoreCase) ||
             localSha.StartsWith(
                 latest.Sha,
                 StringComparison.OrdinalIgnoreCase));

        var updateAvailable =
            !downloaded ||
            !sameCommit;

        return new(
            true,
            downloaded,
            updateAvailable,
            localSha,
            latest.Sha,
            latest.Message,
            latest.Date,
            localPath,
            changes);
    }

    private async Task<GitHubCommitInfo> GetLatestCommitAsync(GameDefinition game, CancellationToken cancellationToken = default)
    {
        var branch = Uri.EscapeDataString(game.GitHubBranch);
        var url =
            $"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/commits" +
            $"?sha={branch}&per_page=1";

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();

        if (first.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("Aucun commit GitHub trouvé.");

        return ParseCommit(first);
    }

    private async Task<IReadOnlyList<GitHubCommitInfo>> GetChangesAsync(
        GameDefinition game,
        string currentSha,
        string latestSha)
    {
        if (string.IsNullOrWhiteSpace(latestSha))
            return Array.Empty<GitHubCommitInfo>();

        if (!string.IsNullOrWhiteSpace(currentSha) &&
            !string.Equals(currentSha, latestSha, StringComparison.OrdinalIgnoreCase))
        {
            var compareUrl =
                $"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/compare/" +
                $"{currentSha}...{latestSha}";

            using var compare = await _http.GetAsync(compareUrl);
            if (compare.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await compare.Content.ReadAsStringAsync());

                if (doc.RootElement.TryGetProperty("commits", out var commits))
                {
                    return commits.EnumerateArray()
                        .Select(ParseCommit)
                        .TakeLast(15)
                        .Reverse()
                        .ToList();
                }
            }
        }

        var branch = Uri.EscapeDataString(game.GitHubBranch);
        var listUrl =
            $"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/commits" +
            $"?sha={branch}&per_page=8";

        using var response = await _http.GetAsync(listUrl);
        response.EnsureSuccessStatusCode();

        using var listDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return listDoc.RootElement.EnumerateArray()
            .Select(ParseCommit)
            .ToList();
    }

    public async Task<string> DownloadLatestAsync(
        GameDefinition game,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestCommitAsync(
            game,
            cancellationToken);

        var local = DetectLocalRepository(game);

        if (local.Present &&
            !string.IsNullOrWhiteSpace(local.Sha) &&
            (latest.Sha.StartsWith(
                 local.Sha,
                 StringComparison.OrdinalIgnoreCase) ||
             local.Sha.StartsWith(
                 latest.Sha,
                 StringComparison.OrdinalIgnoreCase)))
        {
            progress?.Report(1.0);
            return local.Path;
        }

        var shortSha =
            latest.Sha[..Math.Min(12, latest.Sha.Length)];

        var versions = VersionsRoot(game);
        var target = Path.Combine(versions, shortSha);

        if (Directory.Exists(target))
        {
            WriteState(game, latest.Sha, latest.Message, target);
            progress?.Report(1.0);
            return target;
        }

        var zip = Path.Combine(
            Path.GetTempPath(),
            $"cubeshelf-{game.Id}-{Guid.NewGuid():N}.zip");

        var branch = Uri.EscapeDataString(game.GitHubBranch);
        var archiveUrl =
            $"https://github.com/{game.GitHubOwner}/{game.GitHubRepo}/archive/refs/heads/{branch}.zip";

        using (var response = await _http.GetAsync(
                   archiveUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(zip);

            var buffer = new byte[256 * 1024];
            long done = 0;
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;

                if (total is > 0)
                    progress?.Report(Math.Min(.72, (double)done / total.Value * .72));
            }
        }

        var staging = target + ".staging";
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);

        Directory.CreateDirectory(staging);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(.76);

        try
        {
            using var archive = ArchiveFactory.OpenArchive(zip);
            archive.WriteToDirectory(
                staging,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });
        }
        finally
        {
            try { File.Delete(zip); } catch { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(.90);

        // GitHub archives contain one top-level owner-repo-sha directory.
        var dirs = Directory.GetDirectories(staging);
        var files = Directory.GetFiles(staging);

        if (dirs.Length == 1 && files.Length == 0)
        {
            Directory.Move(dirs[0], target);
            Directory.Delete(staging, true);
        }
        else
        {
            Directory.Move(staging, target);
        }

        WriteState(game, latest.Sha, latest.Message, target);
        CleanupOldVersions(game, target);

        progress?.Report(1.0);
        return target;
    }

    public void DeleteDownloadedSource(GameDefinition game)
    {
        var root = GameDataRoot(game);

        if (Directory.Exists(root))
            Directory.Delete(root, true);

        Directory.CreateDirectory(root);
    }

    public string GetDownloadedSourcePath(GameDefinition game)
    {
        var state = ReadState(game);
        return Directory.Exists(state.SourcePath) ? state.SourcePath : "";
    }

    private static GitHubCommitInfo ParseCommit(JsonElement element)
    {
        var sha = element.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
        var url = element.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

        var message = "";
        DateTimeOffset date = default;

        if (element.TryGetProperty("commit", out var commit))
        {
            if (commit.TryGetProperty("message", out var m))
                message = m.GetString() ?? "";

            if (commit.TryGetProperty("committer", out var committer) &&
                committer.TryGetProperty("date", out var d) &&
                DateTimeOffset.TryParse(d.GetString(), out var parsed))
            {
                date = parsed;
            }
        }

        message = message.Split('\n')[0].Trim();
        return new GitHubCommitInfo(sha, message, date, url);
    }

    private sealed record LocalState(string Sha, string Message, string SourcePath);

    private LocalState ReadState(GameDefinition game)
    {
        var file = StateFile(game);

        if (!File.Exists(file))
            return new("", "", "");

        try
        {
            return JsonSerializer.Deserialize<LocalState>(
                       File.ReadAllText(file), _json) ??
                   new("", "", "");
        }
        catch
        {
            return new("", "", "");
        }
    }

    private void WriteState(
        GameDefinition game,
        string sha,
        string message,
        string sourcePath)
    {
        File.WriteAllText(
            StateFile(game),
            JsonSerializer.Serialize(
                new LocalState(sha, message, sourcePath),
                _json));
    }

    private static void CleanupOldVersions(GameDefinition game, string currentPath)
    {
        try
        {
            var dirs = Directory.GetDirectories(VersionsRoot(game))
                .OrderByDescending(Directory.GetCreationTimeUtc)
                .ToList();

            foreach (var dir in dirs.Skip(2))
            {
                if (!string.Equals(
                        Path.GetFullPath(dir),
                        Path.GetFullPath(currentPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
        catch
        {
        }
    }
}
