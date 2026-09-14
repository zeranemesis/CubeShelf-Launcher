using System.Text.Json;

namespace CubeShelf.Core.Library;

public sealed class GameCatalogEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Executable { get; set; } = "";
    public string DiscImage { get; set; } = "";
    public string GitHubOwner { get; set; } = "";
    public string GitHubRepo { get; set; } = "";
    public string GitHubReleaseTag { get; set; } = "cubeshelf-nightly";
    public int GameBananaGameId { get; set; }
    public IReadOnlyList<GameCover> Covers { get; set; } = Array.Empty<GameCover>();
}

public sealed class GameCover
{
    public string Label { get; set; } = "Default";
    public string Front { get; set; } = "";
}

public sealed class GameCatalogService
{
    private readonly string _shippedCatalog;
    private readonly string _userCatalog;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GameCatalogService(string shippedCatalog, string dataDirectory)
    {
        _shippedCatalog = Path.GetFullPath(shippedCatalog);
        _userCatalog = Path.Combine(Path.GetFullPath(dataDirectory), "games.json");
    }

    public IReadOnlyList<GameCatalogEntry> Load()
    {
        var source = File.Exists(_userCatalog) ? _userCatalog : _shippedCatalog;
        if (!File.Exists(source)) return Array.Empty<GameCatalogEntry>();

        try
        {
            return JsonSerializer.Deserialize<List<GameCatalogEntry>>(
                       File.ReadAllText(source), _json) ?? new List<GameCatalogEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<GameCatalogEntry>();
        }
    }
}
