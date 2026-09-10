using SharpCompress.Archives;
using SharpCompress.Common;

namespace CubeShelf.Launcher.Services;

public sealed record RuntimeState(
    string Commit,
    string Version,
    string RuntimeDirectory,
    string ExecutablePath,
    bool GameDataReady);

public sealed record RuntimeHealth(
    bool Healthy,
    bool RuntimePresent,
    bool ResourcesPresent,
    bool GameDataPresent,
    IReadOnlyList<string> Issues);

public sealed class GameRuntimeInstallerService
{
    private readonly HttpClient _http = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public GameRuntimeInstallerService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CubeShelf/0.6.4");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    private static string Root(GameDefinition game)
    {
        var root=Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf","Games",game.Id,"Runtime");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Current(GameDefinition game)
    {
        var p=Path.Combine(Root(game),"current");
        Directory.CreateDirectory(p);
        return p;
    }

    private static string StatePath(GameDefinition game) => Path.Combine(Root(game),"runtime-state.json");


public RuntimeState? AdoptConfiguredExecutable(GameDefinition game)
{
    var configured = game.ExecutableFullPath;

    if (string.IsNullOrWhiteSpace(configured) || !File.Exists(configured))
        return null;

    var exeDir = Path.GetDirectoryName(configured)!;
    var gameRoot = Path.Combine(exeDir, game.Id);
    var ready = Directory.Exists(Path.Combine(gameRoot, "files"));

    var state = new RuntimeState(
        "manual",
        "manual",
        exeDir,
        configured,
        ready);

    WriteState(game, state);

    game.Executable = configured;
    game.GameRoot = gameRoot;
    game.RuntimeInstalled = true;
    game.GameDataReady = ready;
    game.RuntimeStatusText = ready
        ? "Installé • prêt à jouer"
        : "Installé • données du jeu à préparer";

    return state;
}

    public RuntimeState? ReadState(GameDefinition game)
    {
        try
        {
            return File.Exists(StatePath(game))
                ? JsonSerializer.Deserialize<RuntimeState>(File.ReadAllText(StatePath(game)),_json)
                : null;
        }
        catch { return null; }
    }

    public bool ApplyInstalledRuntime(GameDefinition game)
{
    var state = ReadState(game);

    if (state is null || !File.Exists(state.ExecutablePath))
    {
        var current = Current(game);
        var recoveredExe = FindPartyBoardExecutable(current);

        if (recoveredExe is not null)
        {
            var recoveredReady = Directory.Exists(
                Path.Combine(Path.GetDirectoryName(recoveredExe)!, game.Id, "files"));

            state = new RuntimeState("", "local", current, recoveredExe, recoveredReady);
            WriteState(game, state);
        }
        else
        {
            state = AdoptConfiguredExecutable(game);

            if (state is null)
            {
                MarkRuntimeMissing(game);
                return false;
            }
        }
    }

    var exeDir = Path.GetDirectoryName(state.ExecutablePath)!;
    game.Executable = state.ExecutablePath;
    game.GameRoot = Path.Combine(exeDir, game.Id);
    game.RuntimeInstalled = true;
    game.GameDataReady = Directory.Exists(Path.Combine(exeDir, game.Id, "files"));
    game.RuntimeStatusText = game.GameDataReady
        ? "Installé • prêt à jouer"
        : "Installé • données du jeu à préparer";

    return true;
}

    private static void MarkRuntimeMissing(GameDefinition game)
    {
        game.RuntimeInstalled = false;
        game.GameDataReady = false;
        game.RuntimeStatusText = "Non installé";

        // If the configured executable disappeared (for example because a
        // CubeShelf-managed source repository containing it was deleted), clear
        // that stale path so the UI immediately reflects reality.
        if (string.IsNullOrWhiteSpace(game.ExecutableFullPath) ||
            !File.Exists(game.ExecutableFullPath))
        {
            game.Executable = "";
            game.ExecutableFullPath = "";
            game.GameRoot = "";
            game.GameRootFullPath = "";
        }

        try
        {
            var staleState = StatePath(game);
            if (File.Exists(staleState))
                File.Delete(staleState);
        }
        catch
        {
        }

        game.Refresh();
    }

    public RuntimeHealth InspectInstallation(GameDefinition game)
    {
        var issues = new List<string>();
        var state = ReadState(game);

        var runtimePresent =
            state is not null &&
            !string.IsNullOrWhiteSpace(state.ExecutablePath) &&
            File.Exists(state.ExecutablePath);

        if (!runtimePresent)
            issues.Add("PartyBoard.exe est absent");

        var exeDirectory = runtimePresent
            ? Path.GetDirectoryName(state!.ExecutablePath)!
            : Current(game);

        var resourcesPresent =
            Directory.Exists(Path.Combine(exeDirectory, "res"));

        if (runtimePresent && !resourcesPresent)
            issues.Add("Le dossier res de PartyBoard est absent");

        var gameDataPresent =
            Directory.Exists(Path.Combine(exeDirectory, game.Id, "files"));

        if (state?.GameDataReady == true && !gameDataPresent)
            issues.Add("Les données de jeu préparées sont marquées prêtes mais sont absentes");

        var healthy = runtimePresent && resourcesPresent &&
                      (state?.GameDataReady != true || gameDataPresent);

        if (healthy && issues.Count == 0)
            issues.Add("Aucun problème détecté");

        return new RuntimeHealth(
            healthy,
            runtimePresent,
            resourcesPresent,
            gameDataPresent,
            issues);
    }

    public Task<RuntimeState> RepairLatestAsync(
        GameDefinition game,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => InstallLatestAsync(game, progress, cancellationToken);

    public async Task<bool> IsPlayableReleaseAvailableAsync(
        GameDefinition game,
        string? requiredCommit = null,
        CancellationToken cancellationToken = default)
    {
        var release = await GetReleaseAsync(game, cancellationToken);

        if (release is null ||
            !release.Value.Assets.Any(a =>
                a.Name.Equals(
                    game.GitHubReleaseAssetName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredCommit))
            return true;

        return SameCommit(release.Value.Commit, requiredCommit);
    }

    public async Task<RuntimeState> InstallLatestAsync(GameDefinition game, IProgress<double>? progress=null, CancellationToken cancellationToken = default)
    {
        var release=await GetReleaseAsync(game, cancellationToken)
            ?? throw new InvalidOperationException(
                "Aucune release Windows CubeShelf n'est disponible. Le dépôt Marioparty4 doit publier PartyBoard-win-x64.zip.");

        var asset=release.Assets.FirstOrDefault(a =>
            a.Name.Equals(game.GitHubReleaseAssetName,StringComparison.OrdinalIgnoreCase));

        if(string.IsNullOrWhiteSpace(asset.Name))
            throw new InvalidOperationException($"La release ne contient pas {game.GitHubReleaseAssetName}.");

        var checksumAsset = release.Assets.FirstOrDefault(a =>
            a.Name.Equals(
                game.GitHubReleaseChecksumAssetName,
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(checksumAsset.Name))
            throw new CryptographicException(
                $"La release PartyBoard ne contient pas {game.GitHubReleaseChecksumAssetName}. " +
                "CubeShelf refuse d'installer un runtime sans checksum SHA-256.");

        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf",
            "Cache",
            "Downloads");

        Directory.CreateDirectory(cacheDirectory);

        var cacheKey = string.IsNullOrWhiteSpace(release.Commit)
            ? SanitizeFileName(release.Version)
            : release.Commit[..Math.Min(12, release.Commit.Length)];

        var temp = Path.Combine(
            cacheDirectory,
            $"{game.Id}-{cacheKey}-{SanitizeFileName(game.GitHubReleaseAssetName)}");

        var checksumText = await _http.GetStringAsync(checksumAsset.Url, cancellationToken);
        var expectedHash = ParseChecksum(checksumText, game.GitHubReleaseAssetName);

        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new CryptographicException(
                $"{game.GitHubReleaseChecksumAssetName} ne contient pas le hash de {game.GitHubReleaseAssetName}.");

        var validCachedDownload =
            File.Exists(temp) &&
            await VerifySha256Async(temp, expectedHash, cancellationToken);

        if (!validCachedDownload)
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch { }

            progress?.Report(.02);
            await DownloadResumableAsync(
                asset.Url,
                temp,
                p => progress?.Report(.02 + p * .56),
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(.60);

        if (!await VerifySha256Async(temp, expectedHash, cancellationToken))
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            try { if (File.Exists(temp + ".part")) File.Delete(temp + ".part"); } catch { }

            throw new CryptographicException(
                "Le SHA-256 du runtime PartyBoard est invalide. " +
                "Le téléchargement a été supprimé et n'a pas été installé.");
        }

        progress?.Report(.66);
        var staging=Path.Combine(Root(game),"staging-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(.70);
            using(var archive=ArchiveFactory.OpenArchive(temp))
            {
                archive.WriteToDirectory(staging,new ExtractionOptions{ExtractFullPath=true,Overwrite=true});
            }

            var current=Current(game);
            var preserved=Path.Combine(current,game.Id);

            foreach(var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if(string.Equals(Path.GetFullPath(entry),Path.GetFullPath(preserved),StringComparison.OrdinalIgnoreCase))
                    continue;
                if(Directory.Exists(entry)) Directory.Delete(entry,true); else File.Delete(entry);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CopyContents(staging,current);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(.88);

            var exe=FindPartyBoardExecutable(current)
                ?? throw new FileNotFoundException("Le package Windows ne contient aucun exécutable PartyBoard.");

            var old=ReadState(game);
            var ready=old?.GameDataReady==true && Directory.Exists(Path.Combine(Path.GetDirectoryName(exe)!,game.Id,"files"));

            var state=new RuntimeState(release.Commit,release.Version,current,exe,ready);
            WriteState(game,state);

            // Runtime download and local disc preparation are intentionally separate.
            // A previously selected ISO/RVZ must never make the GitHub runtime download fail.
            progress?.Report(1);
            return state;
        }
        finally
        {
            // Keep the verified package in CubeShelf cache so a repair can reuse it.
            // Settings > Storage can remove this cache at any time.
            try{if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}
        }
    }

    public async Task PrepareGameDataAsync(
        GameDefinition game,
        RuntimeState? runtime = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? discImagePath = null,
        bool allowDolphinToolDownload = false)
    {
        runtime ??= ReadState(game);

        if(runtime is null || !File.Exists(runtime.ExecutablePath))
            throw new InvalidOperationException("PartyBoard doit être téléchargé avant de préparer l'ISO/RVZ.");

        var sourceDisc = string.IsNullOrWhiteSpace(discImagePath)
            ? game.DiscImageFullPath
            : Path.GetFullPath(discImagePath);

        if (string.IsNullOrWhiteSpace(sourceDisc) || !File.Exists(sourceDisc))
            throw new InvalidOperationException("Sélectionne d'abord ton ISO / RVZ.");

        var exeDir=Path.GetDirectoryName(runtime.ExecutablePath)!;
        var targetFiles=Path.Combine(exeDir,game.Id,"files");

        if(Directory.Exists(targetFiles)) Directory.Delete(targetFiles,true);
        Directory.CreateDirectory(targetFiles);

        var ext=Path.GetExtension(sourceDisc).ToLowerInvariant();
        string iso=sourceDisc;
        string? tempIso=null;

        try
        {
            if(ext==".rvz")
            {
                var dolphin = await EnsureDolphinToolAsync(
                    allowDolphinToolDownload,
                    progress,
                    cancellationToken);

                tempIso=Path.Combine(Path.GetTempPath(),$"cubeshelf-{game.Id}-{Guid.NewGuid():N}.iso");

                var psi=new ProcessStartInfo(dolphin)
                {
                    UseShellExecute=false,
                    CreateNoWindow=true,
                    WorkingDirectory=Path.GetDirectoryName(dolphin)!
                };
                psi.ArgumentList.Add("convert");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("iso");
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(sourceDisc);
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(tempIso);

                using var proc=Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer DolphinTool.");
                try
                {
                    await proc.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                    throw;
                }

                if(proc.ExitCode!=0 || !File.Exists(tempIso))
                    throw new InvalidOperationException("La conversion RVZ → ISO a échoué.");

                iso=tempIso;
                progress?.Report(.35);

                var convertedCompatibility =
                    DiscImageService.Inspect(iso, english: false);

                if (!convertedCompatibility.Recognized ||
                    !convertedCompatibility.Supported)
                {
                    throw new InvalidDataException(
                        "Le RVZ converti n'est pas compatible avec le build PartyBoard actuel. " +
                        convertedCompatibility.Message);
                }
            }
            else if(ext!=".iso" && ext!=".gcm")
            {
                throw new InvalidOperationException("Utilise ISO, GCM ou RVZ.");
            }
            else
            {
                var compatibility =
                    DiscImageService.Inspect(sourceDisc, english: false);

                if (!compatibility.Recognized || !compatibility.Supported)
                {
                    throw new InvalidDataException(
                        "L'image sélectionnée n'est pas compatible avec le build PartyBoard actuel. " +
                        compatibility.Message);
                }
            }

            double b=ext==".rvz" ? .35 : 0, scale=ext==".rvz" ? .65 : 1;
            await GameCubeIsoExtractor.ExtractFilesAsync(
                iso,targetFiles,new Progress<double>(p=>progress?.Report(b+p*scale)), cancellationToken);

            WriteState(game,runtime with { GameDataReady=true });
            game.RuntimeInstalled=true;
            game.GameDataReady=true;
            game.RuntimeStatusText="Installé • prêt à jouer";
        }
        catch
        {
            try{if(Directory.Exists(targetFiles))Directory.Delete(targetFiles,true);}catch{}
            throw;
        }
        finally
        {
            if(tempIso is not null) try{File.Delete(tempIso);}catch{}
        }
    }

    private async Task<
        (string Commit, string Version, List<(string Name, string Url)> Assets)?>
        GetReleaseAsync(
            GameDefinition game,
            CancellationToken cancellationToken = default)
    {
        var tag =
            Uri.EscapeDataString(game.GitHubReleaseTag);

        var releaseRoot =
            $"https://github.com/{game.GitHubOwner}/{game.GitHubRepo}" +
            $"/releases/download/{tag}/";

        var manifestUrl =
            releaseRoot + "manifest.json";

        using var manifestResponse =
            await _http.GetAsync(
                manifestUrl,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

        if (manifestResponse.StatusCode == HttpStatusCode.NotFound)
            return null;

        manifestResponse.EnsureSuccessStatusCode();

        using var manifest =
            JsonDocument.Parse(
                await manifestResponse.Content.ReadAsStringAsync(
                    cancellationToken));

        var root = manifest.RootElement;

        var commit =
            root.TryGetProperty("commit", out var commitElement)
                ? commitElement.GetString() ?? ""
                : "";

        var version =
            root.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? game.GitHubReleaseTag
                : game.GitHubReleaseTag;

        var runtimeName =
            game.GitHubReleaseAssetName;

        var checksumName =
            game.GitHubReleaseChecksumAssetName;

        var runtimeUrl =
            releaseRoot + Uri.EscapeDataString(runtimeName);

        var checksumUrl =
            releaseRoot + Uri.EscapeDataString(checksumName);

        if (!await ReleaseAssetExistsAsync(
                runtimeUrl,
                cancellationToken))
        {
            return null;
        }

        if (!await ReleaseAssetExistsAsync(
                checksumUrl,
                cancellationToken))
        {
            return null;
        }

        return (
            commit,
            version,
            new List<(string Name, string Url)>
            {
                (runtimeName, runtimeUrl),
                (checksumName, checksumUrl)
            });
    }

    private async Task<bool> ReleaseAssetExistsAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response =
                await _http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static bool SameCommit(string left, string right)
    {
        if (!LooksLikeSha(left) || !LooksLikeSha(right))
            return false;

        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) ||
               right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
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

    private static string ParseChecksum(string text, string fileName)
    {
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            if (parts[^1].TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].Trim();
        }

        return "";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "download";

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static async Task<bool> VerifySha256Async(
        string file,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                useAsync: true);

            var actual = Convert
                .ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
                .ToLowerInvariant();

            return actual.Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private Task DownloadAsync(
        string url,
        string destination,
        Action<double>? progress,
        CancellationToken cancellationToken)
        => DownloadResumableAsync(url, destination, progress, cancellationToken);

    private async Task DownloadResumableAsync(
        string url,
        string destination,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        var partial = destination + ".part";
        var existing = File.Exists(partial)
            ? new FileInfo(partial).Length
            : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (existing > 0 &&
            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Move(partial, destination, true);
            progress?.Invoke(1);
            return;
        }

        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
            existing = 0;

        response.EnsureSuccessStatusCode();

        var responseLength = response.Content.Headers.ContentLength;
        var total = response.Content.Headers.ContentRange?.Length ??
                    (responseLength is > 0 ? existing + responseLength.Value : (long?)null);

        var buffer = new byte[256 * 1024];
        var done = existing;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            partial,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            useAsync: true))
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read <= 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;

                if (total is > 0)
                    progress?.Invoke(Math.Clamp((double)done / total.Value, 0, 1));
            }

            await output.FlushAsync(cancellationToken);
        }

        File.Move(partial, destination, true);
        progress?.Invoke(1);
    }

    private void WriteState(GameDefinition game,RuntimeState state)
        => File.WriteAllText(StatePath(game),JsonSerializer.Serialize(state,_json));

    public void DeleteRuntime(GameDefinition game, bool keepPreparedGameData)
    {
        var current = Current(game);
        var keep = Path.Combine(current, game.Id);

        if (Directory.Exists(current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (keepPreparedGameData &&
                    string.Equals(Path.GetFullPath(entry), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(entry))
                    Directory.Delete(entry, true);
                else
                    File.Delete(entry);
            }
        }

        try { if (File.Exists(StatePath(game))) File.Delete(StatePath(game)); } catch { }

        game.Executable = "";
        game.GameRoot = "";
        game.RuntimeInstalled = false;
        game.GameDataReady = keepPreparedGameData && Directory.Exists(Path.Combine(keep, "files"));
        game.RuntimeStatusText = game.GameDataReady
            ? "PartyBoard supprimé • données du jeu conservées"
            : "Non installé";
    }

    public void DeletePreparedGameData(GameDefinition game)
    {
        var state = ReadState(game);
        var current = state?.RuntimeDirectory ?? Current(game);
        var data = Path.Combine(current, game.Id);

        if (Directory.Exists(data))
            Directory.Delete(data, true);

        if (state is not null)
            WriteState(game, state with { GameDataReady = false });

        game.GameDataReady = false;
        game.RuntimeStatusText = game.RuntimeInstalled
            ? "Installé • données du jeu à préparer"
            : "Non installé";
    }

    public void DeleteAllRuntimeFiles(GameDefinition game)
    {
        var root = Root(game);
        if (Directory.Exists(root))
            Directory.Delete(root, true);

        game.Executable = "";
        game.GameRoot = "";
        game.RuntimeInstalled = false;
        game.GameDataReady = false;
        game.RuntimeStatusText = "Non installé";
    }

    private static string? FindPartyBoardExecutable(string root)
    {
        var exes=Directory.EnumerateFiles(root,"*.exe",SearchOption.AllDirectories).ToList();
        return exes.FirstOrDefault(x=>Path.GetFileName(x).Equals("partyboard.exe",StringComparison.OrdinalIgnoreCase))
            ?? exes.FirstOrDefault(x=>Path.GetFileName(x).Contains("partyboard",StringComparison.OrdinalIgnoreCase)
                                      && !Path.GetFileName(x).Contains("online",StringComparison.OrdinalIgnoreCase))
            ?? exes.FirstOrDefault();
    }

    private static void CopyContents(string source,string destination)
    {
        foreach(var d in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination,Path.GetRelativePath(source,d)));

        foreach(var f in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        {
            var target=Path.Combine(destination,Path.GetRelativePath(source,f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f,target,true);
        }
    }

    public bool IsDolphinToolAvailable()
        => FindDolphinTool() is not null;

    private static string DolphinToolCacheRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf",
            "Tools",
            "Dolphin");

    private async Task<string> EnsureDolphinToolAsync(
        bool allowDownload,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var existing = FindDolphinTool();
        if (existing is not null)
            return existing;

        if (!allowDownload)
        {
            throw new InvalidOperationException(
                "DolphinTool.exe est nécessaire pour un RVZ. " +
                "Autorise son téléchargement automatique ou utilise un ISO.");
        }

        const string version = "2606a";
        const string url =
            "https://dl.dolphin-emu.org/releases/2606a/dolphin-2606a-x64.7z";

        var toolsRoot = DolphinToolCacheRoot();
        var staging = toolsRoot + ".staging-" + Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"cubeshelf-dolphin-{version}-{Guid.NewGuid():N}.7z");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(toolsRoot)!);

            progress?.Report(.01);

            await DownloadAsync(
                url,
                archivePath,
                p => progress?.Report(.01 + p * .14),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(staging))
                Directory.Delete(staging, true);

            Directory.CreateDirectory(staging);
            progress?.Report(.16);

            using (var archive = ArchiveFactory.OpenArchive(archivePath))
            {
                archive.WriteToDirectory(
                    staging,
                    new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
            }

            cancellationToken.ThrowIfCancellationRequested();

            var found = Directory
                .EnumerateFiles(
                    staging,
                    "DolphinTool.exe",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            found ??= Directory
                .EnumerateFiles(
                    staging,
                    "*dolphin*tool*.exe",
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (found is null)
                throw new FileNotFoundException(
                    "Le package officiel Dolphin ne contient pas DolphinTool.exe.");

            var relative = Path.GetRelativePath(staging, found);

            if (Directory.Exists(toolsRoot))
                Directory.Delete(toolsRoot, true);

            Directory.Move(staging, toolsRoot);

            var installed = Path.Combine(toolsRoot, relative);

            if (!File.Exists(installed))
            {
                installed = Directory
                    .EnumerateFiles(
                        toolsRoot,
                        "DolphinTool.exe",
                        SearchOption.AllDirectories)
                    .FirstOrDefault()
                    ?? "";
            }

            if (string.IsNullOrWhiteSpace(installed) || !File.Exists(installed))
                throw new FileNotFoundException(
                    "DolphinTool.exe n'a pas pu être installé dans le cache CubeShelf.");

            progress?.Report(.20);
            return installed;
        }
        finally
        {
            try
            {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
                if (File.Exists(archivePath + ".part"))
                    File.Delete(archivePath + ".part");
            }
            catch { }

            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch { }
        }
    }

    private static string? FindDolphinTool()
    {
        var names = new[] { "DolphinTool.exe", "dolphin-tool.exe" };

        var candidateDirectories = new List<string>
        {
            AppContext.BaseDirectory,
            DolphinToolCacheRoot()
        };

        void AddCandidateRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;

            candidateDirectories.Add(Path.Combine(root, "Dolphin Emulator"));
            candidateDirectories.Add(Path.Combine(root, "Dolphin"));
        }

        AddCandidateRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        AddCandidateRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        AddCandidateRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        foreach (var dir in candidateDirectories
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in names)
            {
                try
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path))
                        return path;
                }
                catch { }
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var path = Path.Combine(dir.Trim(), name);
                    if (File.Exists(path))
                        return path;
                }
                catch { }
            }
        }

        return null;
    }
}
