namespace CubeShelf.Launcher.Services;

public sealed class GameLibraryService
{
    private readonly string _shippedGamesFile;
    private readonly string _userGamesFile;
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _json =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public GameLibraryService()
    {
        _baseDirectory = AppContext.BaseDirectory;
        _shippedGamesFile = Path.Combine(_baseDirectory, "games.json");

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CubeShelf");

        Directory.CreateDirectory(dataDir);
        _userGamesFile = Path.Combine(dataDir, "games.json");
    }

    public List<GameDefinition> Load()
    {
        if (!File.Exists(_userGamesFile))
            File.Copy(_shippedGamesFile, _userGamesFile, true);

        var games = JsonSerializer.Deserialize<List<GameDefinition>>(
            File.ReadAllText(_userGamesFile), _json) ?? new();

        // Merge newly shipped metadata into an existing Mario Party 4 entry.
        MergeShippedMetadata(games);

        foreach (var game in games)
            Resolve(game);

        Save(games);
        return games;
    }

    private void MergeShippedMetadata(List<GameDefinition> userGames)
    {
        if (!File.Exists(_shippedGamesFile))
            return;

        var shipped = JsonSerializer.Deserialize<List<GameDefinition>>(
            File.ReadAllText(_shippedGamesFile), _json) ?? new();

        foreach (var source in shipped)
        {
            var target = userGames.FirstOrDefault(x => x.Id == source.Id);
            if (target is null)
            {
                userGames.Add(source);
                continue;
            }

            // Preserve user paths, but add launcher-managed metadata introduced in updates.
            if (target.GameBananaGameId <= 0)
                target.GameBananaGameId = source.GameBananaGameId;

            if (string.IsNullOrWhiteSpace(target.GitHubOwner))
                target.GitHubOwner = source.GitHubOwner;

            if (string.IsNullOrWhiteSpace(target.GitHubRepo))
                target.GitHubRepo = source.GitHubRepo;

            if (string.IsNullOrWhiteSpace(target.GitHubBranch))
                target.GitHubBranch = source.GitHubBranch;

            if (string.IsNullOrWhiteSpace(target.GitHubReleaseTag))
                target.GitHubReleaseTag = source.GitHubReleaseTag;

            if (string.IsNullOrWhiteSpace(target.GitHubReleaseAssetName))
                target.GitHubReleaseAssetName = source.GitHubReleaseAssetName;

            if (target.Covers.Count == 0)
                target.Covers = source.Covers;
        }
    }

    public void Save(IEnumerable<GameDefinition> games)
        => File.WriteAllText(
            _userGamesFile,
            JsonSerializer.Serialize(games.ToList(), _json));

    public void ResetToShipped()
        => File.Copy(_shippedGamesFile, _userGamesFile, true);

    public GameDefinition AddCustomGame(string executable)
    {
        var id = "CUSTOM_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var game = new GameDefinition
        {
            Id = id,
            Title = Path.GetFileNameWithoutExtension(executable),
            Genre = "GameCube",
            Description =
                "Jeu ajouté manuellement. Tu peux ensuite renseigner son GameBanana ID " +
                "et son dépôt GitHub dans %LOCALAPPDATA%\\CubeShelf\\games.json.",
            Executable = executable,
            GameRoot = Path.GetDirectoryName(executable) ?? "",
            GameBananaGameId = 0,
            Covers = new List<CoverVariant>
            {
                new()
                {
                    Label = "Défaut",
                    Front = "Assets\\Covers\\Common\\placeholder.png",
                    Back = "Assets\\Covers\\Common\\placeholder.png",
                    Spine = "Assets\\Covers\\Common\\placeholder.png"
                }
            }
        };

        Resolve(game);
        return game;
    }

    public void Resolve(GameDefinition game)
    {
        game.ExecutableFullPath = ResolvePath(game.Executable);
        game.GameRootFullPath = ResolvePath(game.GameRoot);
        game.DiscImageFullPath = ResolvePath(game.DiscImage);

        foreach (var cover in game.Covers)
        {
            cover.FrontFullPath = ResolvePath(cover.Front);
            cover.BackFullPath = ResolvePath(cover.Back);
            cover.SpineFullPath = ResolvePath(cover.Spine);
        }

        game.Refresh();
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(_baseDirectory, path));
    }
}
