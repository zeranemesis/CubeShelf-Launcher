using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeShelf.Core.Library;

/// <summary>How CubeShelf obtains a game's runtime from its GitHub repository.</summary>
public enum RuntimeSourceKind
{
    /// <summary>
    /// The publisher ships a CubeShelf manifest (schema 1 or 2) beside the release assets,
    /// naming the artifact per platform with its size and SHA-256. PartyBoard does this.
    /// </summary>
    Manifest = 0,

    /// <summary>
    /// The publisher ships ordinary release assets and no manifest. CubeShelf asks the GitHub
    /// API for the latest release and matches an asset per platform. Ring Out does this.
    /// </summary>
    GitHubReleaseAsset = 1
}

/// <summary>Who turns the user's disc image into something the runtime can boot.</summary>
public enum GameDataPreparation
{
    /// <summary>CubeShelf extracts the disc itself (Dolphin tooling for RVZ, then the file tree).</summary>
    CubeShelf = 0,

    /// <summary>
    /// The runtime owns the disc and CubeShelf must not touch it. Two kinds of runtime land
    /// here. Ring Out recompiles the game from the disc on first launch, which CubeShelf
    /// cannot and should not replicate. PartyBoard simply opens the image and reads it -- it
    /// takes the path in PARTYBOARD_DISC_IMAGE, mounts it through nod, and accepts iso, gcm,
    /// ciso, gcz, nfs, rvz, wbfs, wia and tgc, so there is nothing to convert and nothing to
    /// extract. Either way the answer to "what does CubeShelf prepare" is: nothing.
    /// </summary>
    Runtime = 1
}

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

    /// <summary>Name of the runtime this game runs on, for UI labels ("Installer Ring Out").</summary>
    public string RuntimeName { get; set; } = "PartyBoard";

    /// <summary>
    /// Disc revisions the runtime accepts. An entry is either a full version id
    /// ("GMPE01_00" -- that revision only) or a bare disc id ("GRSEAF" -- any revision).
    /// </summary>
    public List<string> SupportedDiscIds { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RuntimeSourceKind RuntimeSource { get; set; } = RuntimeSourceKind.Manifest;

    /// <summary>
    /// Runtime identifier ("win-x64", "linux-x64") to the release asset name that carries it.
    /// The value may contain a single wildcard, because publishers put the version in the
    /// file name: RingOut-*-windows-x64.zip. Only used by <see cref="RuntimeSourceKind.GitHubReleaseAsset"/>.
    /// </summary>
    public Dictionary<string, string> RuntimeAssets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runtime identifier to the executable to launch inside the extracted package, relative to
    /// its root. The token {version} is replaced by the resolved release version, since packages
    /// are usually rooted in a version-named folder: RingOut-{version}/RingOut.exe. Keyed per
    /// platform because the same package ships RingOut.exe on Windows and RingOut on Linux.
    /// </summary>
    public Dictionary<string, string> RuntimeLaunchPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional pinned SHA-256 of the release asset. When a publisher ships no checksum file
    /// this is the only way to verify the download; leaving it empty makes the install an
    /// explicitly unverified one, which the user has to allow.
    /// </summary>
    public string RuntimeSha256 { get; set; } = "";

    /// <summary>
    /// Name of a checksum file published in the same release, if the project ships one
    /// (Strikers ships SHA256SUMS). Read in sha256sum format and preferred over
    /// <see cref="RuntimeSha256"/>, because it moves with every release instead of going stale.
    /// </summary>
    public string RuntimeChecksumAsset { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameDataPreparation DataPreparation { get; set; } = GameDataPreparation.CubeShelf;

    /// <summary>Project page shown to the user, for runtimes CubeShelf cannot fully automate.</summary>
    public string HomepageUrl { get; set; } = "";

    /// <summary>
    /// The runtime's own online companion, relative to the installed runtime, when it has one.
    ///
    /// Empty means this game cannot be played together and CubeShelf offers no invitation for
    /// it -- which is the honest default, since a launcher cannot add multiplayer to a game it
    /// only starts. PartyBoard ships PartyBoardOnline.exe; Strikers has no netplay at all.
    /// </summary>
    public string OnlineCompanion { get; set; } = "";

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

            // How the runtime is acquired, and who prepares the disc, are CubeShelf's to decide.
            // A user catalog written before these fields existed deserialises them as defaults,
            // so they are overwritten from the shipped catalog rather than preserved.
            target.RuntimeName = source.RuntimeName;
            target.SupportedDiscIds = source.SupportedDiscIds.ToList();
            target.RuntimeSource = source.RuntimeSource;
            target.RuntimeAssets = new Dictionary<string, string>(source.RuntimeAssets, StringComparer.OrdinalIgnoreCase);
            target.RuntimeLaunchPaths = new Dictionary<string, string>(source.RuntimeLaunchPaths, StringComparer.OrdinalIgnoreCase);
            target.RuntimeSha256 = source.RuntimeSha256;
            target.RuntimeChecksumAsset = source.RuntimeChecksumAsset;
            target.DataPreparation = source.DataPreparation;
            target.HomepageUrl = source.HomepageUrl;
            target.OnlineCompanion = source.OnlineCompanion;

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
        RuntimeName = source.RuntimeName,
        SupportedDiscIds = source.SupportedDiscIds.ToList(),
        RuntimeSource = source.RuntimeSource,
        RuntimeAssets = new Dictionary<string, string>(source.RuntimeAssets, StringComparer.OrdinalIgnoreCase),
        RuntimeLaunchPaths = new Dictionary<string, string>(source.RuntimeLaunchPaths, StringComparer.OrdinalIgnoreCase),
        RuntimeSha256 = source.RuntimeSha256,
        RuntimeChecksumAsset = source.RuntimeChecksumAsset,
        DataPreparation = source.DataPreparation,
        HomepageUrl = source.HomepageUrl,
        OnlineCompanion = source.OnlineCompanion,
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
