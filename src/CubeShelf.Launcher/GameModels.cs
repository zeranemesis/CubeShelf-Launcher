namespace CubeShelf.Launcher;

public sealed class CoverVariant
{
    public string Label { get; set; } = "Défaut";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public string Spine { get; set; } = "";

    [JsonIgnore] public string FrontFullPath { get; set; } = "";
    [JsonIgnore] public string BackFullPath { get; set; } = "";
    [JsonIgnore] public string SpineFullPath { get; set; } = "";
}

public sealed class GameDefinition : INotifyPropertyChanged
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

    public List<CoverVariant> Covers { get; set; } = new();

    // User-owned library metadata. These values are intentionally persisted
    // in %LOCALAPPDATA%\CubeShelf\games.json across launcher updates.
    public bool IsFavorite { get; set; }
    public int PlayCount { get; set; }
    public long TotalPlaySeconds { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }

    [JsonIgnore] public string ExecutableFullPath { get; set; } = "";
    [JsonIgnore] public string GameRootFullPath { get; set; } = "";
    [JsonIgnore] public string DiscImageFullPath { get; set; } = "";

    [JsonIgnore] public bool IsInstalled =>
        !string.IsNullOrWhiteSpace(ExecutableFullPath) && File.Exists(ExecutableFullPath);

    [JsonIgnore] public bool HasDiscImage =>
        !string.IsNullOrWhiteSpace(DiscImageFullPath) && File.Exists(DiscImageFullPath);

    [JsonIgnore] public string InstallStatus =>
        IsInstalled ? "Prêt à jouer" : "Exécutable à configurer";

    [JsonIgnore] public string DiscStatus =>
        HasDiscImage ? Path.GetFileName(DiscImageFullPath) : "Aucun ISO / RVZ sélectionné";

    [JsonIgnore] public string LibraryCover =>
        Covers.FirstOrDefault()?.FrontFullPath ?? "";

    [JsonIgnore] public string PlayTimeText
    {
        get
        {
            var total = TimeSpan.FromSeconds(Math.Max(0, TotalPlaySeconds));
            if (total.TotalHours >= 1)
                return $"{(int)total.TotalHours} h {total.Minutes:00} min";
            return $"{total.Minutes} min";
        }
    }

    [JsonIgnore] public string LastPlayedText =>
        LastPlayedAt is null
            ? "Jamais lancé"
            : LastPlayedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore] public bool GitHubConfigured =>
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepo);

    [JsonIgnore] public bool GitHubUpdateAvailable { get; set; }
    [JsonIgnore] public string GitHubStatusText { get; set; } = "Vérification GitHub…";
    [JsonIgnore] public string GitHubLatestCommit { get; set; } = "";
    [JsonIgnore] public string GitHubCurrentCommit { get; set; } = "";
    [JsonIgnore] public string GitHubLatestMessage { get; set; } = "";
    [JsonIgnore] public string GitHubChangeLog { get; set; } = "";
    [JsonIgnore] public string GitHubSourcePath { get; set; } = "";
    [JsonIgnore] public bool RuntimeInstalled { get; set; }
    [JsonIgnore] public bool GameDataReady { get; set; }
    [JsonIgnore] public string RuntimeStatusText { get; set; } = "";
    [JsonIgnore] public bool RuntimeReleaseAvailable { get; set; }
    [JsonIgnore] public bool RuntimeDistributionChecked { get; set; }

    // These statuses are intentionally separate:
    // - the user's original ISO/RVZ,
    // - the installed PartyBoard runtime,
    // - the optional local GitHub source repository.
    [JsonIgnore] public bool OriginalGameConfigured => HasDiscImage;
    [JsonIgnore] public bool RuntimeUpdateAvailable =>
        RuntimeInstalled &&
        GitHubUpdateAvailable &&
        RuntimeReleaseAvailable;

    [JsonIgnore] public bool RuntimeUpdatePendingBuild =>
        RuntimeInstalled &&
        GitHubUpdateAvailable &&
        RuntimeDistributionChecked &&
        !RuntimeReleaseAvailable;

    [JsonIgnore] public bool GitHubSourceDownloaded =>
        !string.IsNullOrWhiteSpace(GitHubSourcePath) && Directory.Exists(GitHubSourcePath);

    [JsonIgnore] public bool GitHubLocalRepositoryPresent { get; set; }
    [JsonIgnore] public bool GitHubLocalRepositoryManaged { get; set; }
    [JsonIgnore] public string GitHubLocalRepositoryPath { get; set; } = "";
    [JsonIgnore] public string GitHubLocalRepositorySha { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
