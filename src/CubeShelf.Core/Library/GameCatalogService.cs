using System.Text.Json;

namespace CubeShelf.Core.Library;

public sealed class GameCatalogEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string Genre { get; set; } = "";
    public string Players { get; set; } = "";
    public string Region { get; set; } = "";
    public string Description { get; set; } = "";

    public string Executable { get; set; } = "";
    public string GameRoot { get; set; } = "";
    public string DiscImage { get; set; } = "";

    public int GameBananaGameId { get; set; }

    public string GitHubOwner { get; set; } = "";
    public string GitHubRepo { get; set; } = "";
    public string GitHubBranch { get; set; } = "main";
    public string GitHubReleaseTag { get; set; } = "cubeshelf-nightly";
    public string GitHubReleaseAssetName { get; set; } = "";
    public string GitHubReleaseChecksumAssetName { get; set; } = "checksums.txt";

    public List<GameCover> Covers { get; set; } = new();

    // User-owned state. These values must survive catalog updates.
    public bool IsFavorite { get; set; }
    public int PlayCount { get; set; }
    public long TotalPlaySeconds { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
}

public sealed class GameCover
{
    public string Label { get; set; } = "Default";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public string Spine { get; set; } = "";
}

/// <summary>
/// Owns the persistent game catalog used by the Avalonia desktop client.
/// Shipped metadata is authoritative for presentation/distribution fields while
/// user paths, favorites and play statistics remain user-owned.
/// </summary>
public sealed class GameCatalogService
{
    private readonly string _shippedCatalog;
    private readonly string _userCatalog;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public GameCatalogService(string shippedCatalog, string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shippedCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _shippedCatalog = Path.GetFullPath(shippedCatalog);
        _userCatalog = Path.Combine(Path.GetFullPath(dataDirectory), "games.json");
    }

    public string UserCatalogPath => _userCatalog;

    public IReadOnlyList<GameCatalogEntry> Load()
    {
        var shipped = ReadCatalog(_shippedCatalog);
        var user = ReadCatalog(_userCatalog);

        if (user.Count == 0)
        {
            user = shipped.Select(Clone).ToList();
            if (user.Count > 0)
                Save(user);
            return user;
        }

        MergeShippedMetadata(user, shipped);
        Save(user);
        return user;
    }

    public void Save(IEnumerable<GameCatalogEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        Directory.CreateDirectory(Path.GetDirectoryName(_userCatalog)!);
        var temporary = _userCatalog + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(games.ToList(), _json));
        File.Move(temporary, _userCatalog, true);
    }

    public void ResetToShipped()
    {
        if (!File.Exists(_shippedCatalog))
            throw new FileNotFoundException("The shipped CubeShelf catalog is missing.", _shippedCatalog);

        Directory.CreateDirectory(Path.GetDirectoryName(_userCatalog)!);
        File.Copy(_shippedCatalog, _userCatalog, true);
    }

    private List<GameCatalogEntry> ReadCatalog(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<GameCatalogEntry>>(File.ReadAllText(path), _json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static void MergeShippedMetadata(
        List<GameCatalogEntry> user,
        IReadOnlyList<GameCatalogEntry> shipped)
    {
        foreach (var source in shipped)
        {
            var target = user.FirstOrDefault(game =>
                game.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                user.Add(Clone(source));
                continue;
            }

            // Presentation metadata belongs to CubeShelf.
            target.Title = source.Title;
            target.Year = source.Year;
            target.Genre = source.Genre;
            target.Players = source.Players;
            target.Region = source.Region;
            target.Description = source.Description;
            target.Covers = source.Covers.Select(Clone).ToList();

            // Distribution/integration metadata also belongs to CubeShelf.
            target.GameBananaGameId = source.GameBananaGameId;
            target.GitHubOwner = source.GitHubOwner;
            target.GitHubRepo = source.GitHubRepo;
            target.GitHubBranch = source.GitHubBranch;
            target.GitHubReleaseTag = source.GitHubReleaseTag;
            target.GitHubReleaseAssetName = source.GitHubReleaseAssetName;
            target.GitHubReleaseChecksumAssetName = source.GitHubReleaseChecksumAssetName;

            // Executable/GameRoot/DiscImage and all play statistics are intentionally preserved.
        }
    }

    private static GameCatalogEntry Clone(GameCatalogEntry source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Year = source.Year,
        Genre = source.Genre,
        Players = source.Players,
        Region = source.Region,
        Description = source.Description,
        Executable = source.Executable,
        GameRoot = source.GameRoot,
        DiscImage = source.DiscImage,
        GameBananaGameId = source.GameBananaGameId,
        GitHubOwner = source.GitHubOwner,
        GitHubRepo = source.GitHubRepo,
        GitHubBranch = source.GitHubBranch,
        GitHubReleaseTag = source.GitHubReleaseTag,
        GitHubReleaseAssetName = source.GitHubReleaseAssetName,
        GitHubReleaseChecksumAssetName = source.GitHubReleaseChecksumAssetName,
        Covers = source.Covers.Select(Clone).ToList(),
        IsFavorite = source.IsFavorite,
        PlayCount = source.PlayCount,
        TotalPlaySeconds = source.TotalPlaySeconds,
        LastPlayedAt = source.LastPlayedAt
    };

    private static GameCover Clone(GameCover source) => new()
    {
        Label = source.Label,
        Front = source.Front,
        Back = source.Back,
        Spine = source.Spine
    };
}
