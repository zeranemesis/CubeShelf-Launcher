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
        CancellationToken cancellationToken = default)
    {
        var release = await GetReleaseAsync(game, cancellationToken);

        return release is not null &&
               release.Value.Assets.Any(a =>
                   a.Name.Equals(
                       game.GitHubReleaseAssetName,
                       StringComparison.OrdinalIgnoreCase));
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

            if(game.HasDiscImage && !ready)
            {
                await PrepareGameDataAsync(game,state,new Progress<double>(p=>progress?.Report(.82+p*.18)), cancellationToken);
                state=ReadState(game)!;
            }

            progress?.Report(1);
            return state;
        }
        finally
        {
            try{File.Delete(temp);}catch{}
            try{if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}
        }
    }

    public async Task PrepareGameDataAsync(GameDefinition game, RuntimeState? runtime=null, IProgress<double>? progress=null, CancellationToken cancellationToken = default)
    {
        runtime ??= ReadState(game);

        if(runtime is null || !File.Exists(runtime.ExecutablePath))
            throw new InvalidOperationException("PartyBoard doit être téléchargé avant de préparer l'ISO/RVZ.");

        if(!game.HasDiscImage)
            throw new InvalidOperationException("Sélectionne d'abord ton ISO / RVZ.");

        var exeDir=Path.GetDirectoryName(runtime.ExecutablePath)!;
        var targetFiles=Path.Combine(exeDir,game.Id,"files");

        if(Directory.Exists(targetFiles)) Directory.Delete(targetFiles,true);
        Directory.CreateDirectory(targetFiles);

        var ext=Path.GetExtension(game.DiscImageFullPath).ToLowerInvariant();
        string iso=game.DiscImageFullPath;
        string? tempIso=null;

        try
        {
            if(ext==".rvz")
            {
                var dolphin=FindDolphinTool();
                if(dolphin is null)
                    throw new InvalidOperationException(
                        "RVZ : CubeShelf a besoin de DolphinTool.exe pour convertir automatiquement le fichier. Installe Dolphin ou utilise un ISO.");

                tempIso=Path.Combine(Path.GetTempPath(),$"cubeshelf-{game.Id}-{Guid.NewGuid():N}.iso");

                var psi=new ProcessStartInfo(dolphin){UseShellExecute=false,CreateNoWindow=true};
                psi.ArgumentList.Add("convert");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("iso");
                psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(game.DiscImageFullPath);
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
        var commit=r.TryGetProperty("target_commitish",out var c)?c.GetString()??"":"";
        var version=r.TryGetProperty("name",out var n)?n.GetString()??game.GitHubReleaseTag:game.GitHubReleaseTag;
        var assets=new List<(string Name,string Url)>();

        if(r.TryGetProperty("assets",out var arr))
            foreach(var item in arr.EnumerateArray())
                assets.Add((item.GetProperty("name").GetString()??"",item.GetProperty("browser_download_url").GetString()??""));

        return (commit,version,assets);
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
        foreach(var name in new[]{"DolphinTool.exe","dolphin-tool.exe"})
        {
            var local=Path.Combine(AppContext.BaseDirectory,name);
            if(File.Exists(local)) return local;
        }

        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"")
                     .Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            foreach(var name in new[]{"DolphinTool.exe","dolphin-tool.exe"})
            {
                try
                {
                    var p=Path.Combine(dir.Trim(),name);
                    if(File.Exists(p)) return p;
                }
                catch{}
            }
        }

        return null;
    }
}
