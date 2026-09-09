using SharpCompress.Archives;
using SharpCompress.Common;

namespace CubeShelf.Launcher.Services;

public sealed record RuntimeState(
    string Commit,
    string Version,
    string RuntimeDirectory,
    string ExecutablePath,
    bool GameDataReady);

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
                return false;
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

        var temp=Path.Combine(Path.GetTempPath(),$"cubeshelf-runtime-{game.Id}-{Guid.NewGuid():N}.zip");
        progress?.Report(.02);
        await DownloadAsync(asset.Url,temp,p=>progress?.Report(.02+p*.62), cancellationToken);

        var staging=Path.Combine(Root(game),"staging-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(.67);
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
            progress?.Report(.82);

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
            try{File.Delete(temp);}catch{}
            try{if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}
        }
    }

    public async Task PrepareGameDataAsync(
        GameDefinition game,
        RuntimeState? runtime = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        string? discImagePath = null)
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
                var dolphin=FindDolphinTool();
                if(dolphin is null)
                    throw new InvalidOperationException(
                        $"Le fichier sélectionné est un RVZ ({Path.GetFileName(sourceDisc)}). " +
                        "CubeShelf doit le convertir avant de préparer le jeu, mais DolphinTool.exe est introuvable. " +
                        "Installe Dolphin, place DolphinTool.exe à côté de CubeShelf.exe, ou sélectionne un ISO.");

                tempIso=Path.Combine(Path.GetTempPath(),$"cubeshelf-{game.Id}-{Guid.NewGuid():N}.iso");

                var psi=new ProcessStartInfo(dolphin){UseShellExecute=false,CreateNoWindow=true};
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
                progress?.Report(.18);
            }
            else if(ext!=".iso" && ext!=".gcm")
            {
                throw new InvalidOperationException("Utilise ISO, GCM ou RVZ.");
            }

            double b=ext==".rvz" ? .18 : 0, scale=ext==".rvz" ? .82 : 1;
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

    private async Task<(string Commit,string Version,List<(string Name,string Url)> Assets)?> GetReleaseAsync(GameDefinition game, CancellationToken cancellationToken = default)
    {
        var url=$"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/releases/tags/{Uri.EscapeDataString(game.GitHubReleaseTag)}";
        using var response=await _http.GetAsync(url, cancellationToken);
        if(response.StatusCode==HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var r=doc.RootElement;
        var commit=await ResolveReleaseCommitAsync(game,r,cancellationToken);
        var version=r.TryGetProperty("name",out var n)?n.GetString()??game.GitHubReleaseTag:game.GitHubReleaseTag;
        var assets=new List<(string Name,string Url)>();

        if(r.TryGetProperty("assets",out var arr))
            foreach(var item in arr.EnumerateArray())
                assets.Add((item.GetProperty("name").GetString()??"",item.GetProperty("browser_download_url").GetString()??""));

        return (commit,version,assets);
    }

    private async Task<string> ResolveReleaseCommitAsync(
        GameDefinition game,
        JsonElement release,
        CancellationToken cancellationToken)
    {
        var tag = release.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? ""
            : "";

        foreach (var reference in new[]
                 {
                     tag,
                     release.TryGetProperty("target_commitish", out var target)
                         ? target.GetString() ?? ""
                         : ""
                 })
        {
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            if (LooksLikeSha(reference))
                return reference;

            var url =
                $"https://api.github.com/repos/{game.GitHubOwner}/{game.GitHubRepo}/commits/" +
                Uri.EscapeDataString(reference);

            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            if (doc.RootElement.TryGetProperty("sha", out var sha))
            {
                var value = sha.GetString() ?? "";
                if (LooksLikeSha(value))
                    return value;
            }
        }

        return "";
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

    private async Task DownloadAsync(string url,string destination,Action<double>? progress,CancellationToken cancellationToken)
    {
        using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        response.EnsureSuccessStatusCode();
        var total=response.Content.Headers.ContentLength;
        await using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output=File.Create(destination);
        var buffer=new byte[256*1024]; long done=0; int read;
        while((read=await input.ReadAsync(buffer,cancellationToken))>0)
        {
            await output.WriteAsync(buffer.AsMemory(0,read),cancellationToken);
            done+=read;
            if(total is >0) progress?.Invoke((double)done/total.Value);
        }
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

    private static string? FindDolphinTool()
    {
        var names = new[] { "DolphinTool.exe", "dolphin-tool.exe" };

        var candidateDirectories = new List<string>
        {
            AppContext.BaseDirectory
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
