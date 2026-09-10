using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Reflection;
using Microsoft.Win32;
using CubeShelf.Launcher.Services;

namespace CubeShelf.Launcher;

public partial class MainWindow : Window
{
    private readonly LauncherConfig _config;
    private readonly GameLibraryService _libraryService;
    private readonly UpdateService _updates;
    private readonly GitHubGameService _github = new();
    private readonly GameRuntimeInstallerService _runtimeInstaller = new();
    private readonly DownloadQueueService _queue = new();
    private readonly UserPreferencesService _preferencesService = new();

    private readonly ObservableCollection<GameDefinition> _games = new();
    private readonly ObservableCollection<UiMod> _visibleMods = new();

    private GameDefinition? _selectedGame;
    private GameBananaService? _gameBanana;
    private ModManager? _modManager;
    private GameLauncher? _gameLauncher;

    private List<UiMod> _allMods = new();
    private UiMod? _selectedMod;
    private int _selectedModImageIndex;
    private UserPreferences _preferences;
    private bool _settingsLoaded;
    private bool _gameUpdatePopupShown;
    private bool _refreshingGameUpdates;
    private bool _showFavoritesOnly;
    private bool _launcherMinimizedForGame;
    private bool _launcherUpdatePreparing;
    private PreparedLauncherUpdate? _preparedLauncherUpdate;
    private readonly Dictionary<string, (Process Process, DateTimeOffset StartedAt)> _runningGames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _githubUpdateTimer = new()
    {
        Interval = TimeSpan.FromMinutes(10)
    };

    public MainWindow()
    {
        _preferences = _preferencesService.Load();
        _preferencesService.ApplyTheme(_preferences.Theme);
        _preferencesService.ApplyLanguage(_preferences.Language);

        InitializeComponent();

        LauncherVersionText.Text =
            $"CubeShelf v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}";

        _config = LauncherConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        _libraryService = new GameLibraryService();
        _updates = new UpdateService(_config);

        GameGrid.ItemsSource = _games;
        ModsList.ItemsSource = _visibleMods;
        DownloadsList.ItemsSource = _queue.Items;
        LibrarySortCombo.SelectedIndex = 1;

        _queue.QueueChanged += (_, _) => UpdateQueueSummary();
        UpdateQueueSummary();
        _githubUpdateTimer.Tick += async (_, _) =>
        {
            if (!_preferences.CheckGamesOnStartup)
                return;

            await RefreshAllGameUpdateStatusesAsync();
            MaybeShowGameUpdatePopup();
        };

        LoadLibrary();
        LoadSettingsControls();
        UpdateThemeIndicator(false);

        Loaded += async (_, _) =>
        {
            ShowLibrary();
            RunFirstStartAssistantIfNeeded();

            if (_config.AutoCheckLauncherUpdates)
                _ = CheckLauncherUpdateAsync(false);

            if (_preferences.CheckGamesOnStartup)
            {
                await RefreshAllGameUpdateStatusesAsync();
                MaybeShowGameUpdatePopup();
                _githubUpdateTimer.Start();
            }
        };
    }

    private void LoadLibrary()
    {
        _games.Clear();

        foreach (var game in _libraryService.Load())
        {
            if (_runtimeInstaller.ApplyInstalledRuntime(game))
                _libraryService.Resolve(game);

            ApplyLocalRepositoryStatus(
                game,
                _github.DetectLocalRepository(game));

            _games.Add(game);
        }

        UpdateLibraryUpdateSummary();
        RefreshLibraryItems();
    }


    private static void ApplyLocalRepositoryStatus(
        GameDefinition game,
        LocalRepositoryStatus status)
    {
        game.GitHubLocalRepositoryPresent = status.Present;
        game.GitHubLocalRepositoryManaged = status.ManagedByCubeShelf;
        game.GitHubLocalRepositoryPath = status.Path;
        game.GitHubLocalRepositorySha = status.Sha;

        game.GitHubSourcePath =
            status.Present ? status.Path : "";

        game.Refresh();
    }

    private void ShowOnly(Grid view)
    {
        LibraryView.Visibility =
            view == LibraryView ? Visibility.Visible : Visibility.Collapsed;
        GameView.Visibility =
            view == GameView ? Visibility.Visible : Visibility.Collapsed;
        ModsView.Visibility =
            view == ModsView ? Visibility.Visible : Visibility.Collapsed;
        DownloadsView.Visibility =
            view == DownloadsView ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility =
            view == SettingsView ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsEnglish => _preferences.Language.Equals("en", StringComparison.OrdinalIgnoreCase);

    private void ShowLibrary()
    {
        _showFavoritesOnly = false;
        SidebarSelectedGame.Text = IsEnglish ? "Library" : "Bibliothèque";
        RefreshLibraryItems();
        ShowOnly(LibraryView);
    }

    private void ShowFavorites()
    {
        _showFavoritesOnly = true;
        SidebarSelectedGame.Text = IsEnglish ? "Favorites" : "Favoris";
        RefreshLibraryItems();
        ShowOnly(LibraryView);
    }

    private void RefreshLibraryItems()
    {
        IEnumerable<GameDefinition> query = _games;

        if (_showFavoritesOnly)
            query = query.Where(x => x.IsFavorite);

        var search = LibrarySearchBox?.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                x.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Genre.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        query = (LibrarySortCombo?.SelectedIndex ?? 1) switch
        {
            0 => query.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => query.OrderByDescending(x => x.TotalPlaySeconds)
                      .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(x => x.LastPlayedAt ?? DateTimeOffset.MinValue)
                      .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        GameGrid.ItemsSource = query.ToList();
    }

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshLibraryItems();

    private void LibrarySortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshLibraryItems();

    private void ShowGame()
        => ShowOnly(GameView);

    private void ShowMods()
        => ShowOnly(ModsView);

    private void ShowDownloads()
        => ShowOnly(DownloadsView);

    private void ShowSettings()
    {
        RefreshStorageSummary();
        ShowOnly(SettingsView);
    }

    private async Task RefreshAllGameUpdateStatusesAsync()
    {
        if (_refreshingGameUpdates)
            return;

        _refreshingGameUpdates = true;
        try
        {
            var tasks = _games
                .Where(x => x.GitHubConfigured)
                .Select(RefreshGameUpdateStatusAsync)
                .ToArray();

            await Task.WhenAll(tasks);
            UpdateLibraryUpdateSummary();
        }
        finally
        {
            _refreshingGameUpdates = false;
        }
    }

    private async Task RefreshGameUpdateStatusAsync(GameDefinition game)
    {
        try
        {
            // Prefer the commit embedded in the installed PartyBoard runtime.
            // This makes the update badge represent the actual playable build,
            // not whether a source-code ZIP happens to exist locally.
            var runtimeState = _runtimeInstaller.ReadState(game);
            var status = await _github.CheckAsync(game, runtimeState?.Commit);

            // Only advertise an installable Windows update once the nightly
            // release has caught up with the latest commit on the game branch.
            var releaseAvailable =
                await _runtimeInstaller.IsPlayableReleaseAvailableAsync(
                    game,
                    status.LatestSha);

            var local = _github.DetectLocalRepository(game);

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyLocalRepositoryStatus(game, local);
                game.RuntimeReleaseAvailable = releaseAvailable;
                game.RuntimeDistributionChecked = true;
                ApplyGitHubStatus(game, status);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                game.RuntimeDistributionChecked = false;

                game.GitHubStatusText =
                    IsEnglish
                        ? "GitHub temporarily unavailable • local installation state kept"
                        : "GitHub temporairement indisponible • état local conservé";

                game.GitHubChangeLog =
                    ex.Message;

                game.Refresh();

                if (_selectedGame == game)
                {
                    UpdateSelectedGameGitHubPanel();
                    UpdateSelectedGameStateBadges();
                }
            });
        }
    }

    private static string ShortSha(string sha)
        => string.IsNullOrWhiteSpace(sha)
            ? "—"
            : sha[..Math.Min(8, sha.Length)];

    private void ApplyGitHubStatus(
        GameDefinition game,
        GitHubGameStatus status)
    {
        game.GitHubUpdateAvailable = status.UpdateAvailable;
        game.GitHubLatestCommit = status.LatestSha;
        game.GitHubCurrentCommit = status.CurrentSha;
        game.GitHubLatestMessage = status.LatestMessage;
        game.GitHubSourcePath = status.SourcePath;

        game.GitHubChangeLog = status.Changes.Count == 0
            ? "Aucun changement listé."
            : string.Join(
                Environment.NewLine,
                status.Changes.Select(x =>
                    $"• {ShortSha(x.Sha)}  {x.Message}"));

        if (!status.Configured)
        {
            game.GitHubStatusText =
                IsEnglish ? "GitHub not configured" : "GitHub non configuré";
        }
        else if (!game.RuntimeInstalled)
        {
            game.GitHubStatusText =
                IsEnglish
                    ? $"PartyBoard available • {ShortSha(status.LatestSha)}"
                    : $"PartyBoard disponible • {ShortSha(status.LatestSha)}";
        }
        else if (status.UpdateAvailable && game.RuntimeReleaseAvailable)
        {
            game.GitHubStatusText =
                IsEnglish
                    ? $"PartyBoard update available • {ShortSha(status.CurrentSha)} → {ShortSha(status.LatestSha)}"
                    : $"Mise à jour PartyBoard disponible • {ShortSha(status.CurrentSha)} → {ShortSha(status.LatestSha)}";
        }
        else if (status.UpdateAvailable && game.RuntimeDistributionChecked)
        {
            game.GitHubStatusText =
                IsEnglish
                    ? $"New commit detected • Windows build pending • {ShortSha(status.LatestSha)}"
                    : $"Nouveau commit détecté • build Windows en attente • {ShortSha(status.LatestSha)}";
        }
        else
        {
            game.GitHubStatusText =
                IsEnglish
                    ? $"PartyBoard up to date • {ShortSha(status.LatestSha)}"
                    : $"PartyBoard à jour • {ShortSha(status.LatestSha)}";
        }

        game.Refresh();

        if (_selectedGame == game)
        {
            UpdateSelectedGameGitHubPanel();
            UpdateSelectedGameStateBadges();
        }
    }

    private void UpdateLibraryUpdateSummary()
    {
        var updates = _games.Count(x => x.RuntimeUpdateAvailable);
        LibraryUpdatesText.Text = IsEnglish
            ? updates == 0 ? "Everything is up to date"
              : updates == 1 ? "1 game update"
              : $"{updates} game updates"
            : updates == 0 ? "Tout est à jour"
              : updates == 1 ? "1 jeu à mettre à jour"
              : $"{updates} jeux à mettre à jour";
    }

    private void UpdateQueueSummary()
    {
        Dispatcher.Invoke(() =>
        {
            var pending = _queue.PendingCount;
            QueueSidebarText.Text = IsEnglish
                ? pending == 0 ? "No downloads"
                  : pending == 1 ? "1 item in queue"
                  : $"{pending} items in queue"
                : pending == 0 ? "Aucun téléchargement"
                  : pending == 1 ? "1 élément en attente"
                  : $"{pending} éléments en attente";
        });
    }

    private void SelectGame(GameDefinition game)
    {
        _selectedGame = game;

        SidebarSelectedGame.Text = $"{game.Title}\n{game.Id}";
        GameHeader.Text = $"{game.Title} • {game.Id}";
        SelectedTitle.Text = game.Title;
        SelectedYear.Text = game.Year > 0 ? game.Year.ToString() : "—";
        SelectedGenre.Text =
            string.IsNullOrWhiteSpace(game.Genre) ? "—" : game.Genre;
        SelectedPlayers.Text =
            string.IsNullOrWhiteSpace(game.Players) ? "—" : game.Players;
        SelectedId.Text = game.Id;
        SelectedDescription.Text = game.Description;
        SelectedInstallStatus.Text = game.RuntimeInstalled ? game.RuntimeStatusText : game.InstallStatus;
        ExecutablePathText.Text =
            string.IsNullOrWhiteSpace(game.ExecutableFullPath)
                ? "Non configuré"
                : game.ExecutableFullPath;
        DiscImagePathText.Text = game.DiscStatus;
        UpdateDiscCompatibility();
        UpdateSelectedGamePlayUi();
        UpdateSelectedGameStateBadges();

        var palEuropeCover = game.Covers.FirstOrDefault(x =>
            x.Label.Equals("PAL Europe", StringComparison.OrdinalIgnoreCase))
            ?? game.Covers.FirstOrDefault();

        if (palEuropeCover is not null)
            GameCase.SetCover(palEuropeCover);

        if (game.GameBananaGameId > 0)
        {
            _gameBanana = new GameBananaService(game.GameBananaGameId);
            _modManager = new ModManager(_config, game, _gameBanana);
        }
        else
        {
            _gameBanana = null;
            _modManager = null;
        }

        _gameLauncher = new GameLauncher(game, _modManager);

        UpdateSelectedGameGitHubPanel();
        ShowGame();

        // Opening a game page also refreshes its GitHub commit status so the
        // user does not have to rely only on the periodic background check.
        _ = RefreshGameUpdateStatusAsync(game);
    }


private void UpdateSelectedGameGitHubPanel()
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    GitHubRepoText.Text =
        game.GitHubConfigured
            ? $"{game.GitHubOwner}/{game.GitHubRepo} • {game.GitHubBranch}"
            : (IsEnglish ? "GitHub not configured" : "GitHub non configuré");

    GitHubStatusText.Text = game.GitHubStatusText;

    if (game.GitHubLocalRepositoryPresent)
    {
        var localType =
            game.GitHubLocalRepositoryManaged
                ? (IsEnglish ? "managed by CubeShelf" : "gérées par CubeShelf")
                : (IsEnglish ? "external/manual" : "externes / manuelles");

        GitHubLocalStatusText.Text =
            IsEnglish
                ? $"Source code: ✓ local copy present ({localType})\n{game.GitHubLocalRepositoryPath}"
                : $"Sources : ✓ copie locale présente ({localType})\n{game.GitHubLocalRepositoryPath}";
    }
    else
    {
        // A source checkout is not required to play. The playable Windows
        // runtime is stored independently in %LOCALAPPDATA%\CubeShelf\Games\...
        GitHubLocalStatusText.Text =
            IsEnglish
                ? "Source code: not downloaded • optional • not required to play"
                : "Sources : non téléchargées • optionnel • inutile pour jouer";
    }

    GitHubChangesText.Text = game.GitHubChangeLog;

    if (game.RuntimeInstalled)
    {
        if (game.RuntimeUpdateAvailable)
        {
            GitHubDownloadButton.Content =
                IsEnglish ? "Update PartyBoard" : "Mettre à jour PartyBoard";
            GitHubDownloadButton.IsEnabled = true;
        }
        else if (game.RuntimeUpdatePendingBuild)
        {
            GitHubDownloadButton.Content =
                IsEnglish ? "Windows build pending" : "Build Windows en préparation";
            GitHubDownloadButton.IsEnabled = false;
        }
        else
        {
            GitHubDownloadButton.Content =
                IsEnglish ? "✓ PartyBoard up to date" : "✓ PartyBoard à jour";
            GitHubDownloadButton.IsEnabled = false;
        }
    }
    else if (game.RuntimeReleaseAvailable)
    {
        GitHubDownloadButton.Content =
            IsEnglish ? "⬇ Install PartyBoard" : "⬇ Installer PartyBoard";
        GitHubDownloadButton.IsEnabled = true;
    }
    else
    {
        GitHubDownloadButton.Content =
            IsEnglish ? "Windows build unavailable" : "Build Windows indisponible";
        GitHubDownloadButton.IsEnabled = false;
    }

    GitHubSourceButton.Content =
        game.GitHubLocalRepositoryPresent
            ? game.GitHubUpdateAvailable
                ? (IsEnglish ? "Update sources" : "Mettre à jour les sources")
                : (IsEnglish ? "✓ Sources present" : "✓ Sources présentes")
            : (IsEnglish
                ? "Download sources (optional)"
                : "Télécharger les sources (optionnel)");

    GitHubSourceButton.IsEnabled =
        game.GitHubConfigured &&
        (!game.GitHubLocalRepositoryPresent || game.GitHubUpdateAvailable);

    DeleteRepositoryButton.Visibility =
        game.GitHubLocalRepositoryPresent
            ? Visibility.Visible
            : Visibility.Collapsed;
}

private void UpdateSelectedGameStateBadges()
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;
    var runtime = _runtimeInstaller.ReadState(game);

    OriginalGameStateText.Text =
        game.HasDiscImage
            ? (IsEnglish
                ? $"✓ Original configured • {Path.GetExtension(game.DiscImageFullPath).TrimStart('.').ToUpperInvariant()}"
                : $"✓ Jeu original configuré • {Path.GetExtension(game.DiscImageFullPath).TrimStart('.').ToUpperInvariant()}")
            : (IsEnglish
                ? "○ Original not configured"
                : "○ Jeu original non configuré");

    OriginalGameStateText.Foreground =
        game.HasDiscImage
            ? new SolidColorBrush(Color.FromRgb(34, 168, 97))
            : Application.Current.TryFindResource("Muted") as Brush ?? Brushes.Gray;

    RuntimeStateText.Text =
        runtime is not null && game.RuntimeInstalled
            ? (IsEnglish
                ? $"✓ PartyBoard installed • {ShortSha(runtime.Commit)}"
                : $"✓ PartyBoard installé • {ShortSha(runtime.Commit)}")
            : (IsEnglish ? "○ PartyBoard not installed" : "○ PartyBoard non installé");

    RuntimeStateText.Foreground =
        game.RuntimeInstalled
            ? new SolidColorBrush(Color.FromRgb(34, 168, 97))
            : Application.Current.TryFindResource("Muted") as Brush ?? Brushes.Gray;

    if (!game.RuntimeInstalled)
    {
        GameUpdateStateText.Text =
            IsEnglish ? "Installation required" : "Installation requise";
        GameUpdateStateText.Foreground =
            Application.Current.TryFindResource("Muted") as Brush ?? Brushes.Gray;
    }
    else if (game.RuntimeUpdateAvailable)
    {
        GameUpdateStateText.Text =
            IsEnglish
                ? $"⚠ Update available • {ShortSha(game.GitHubLatestCommit)}"
                : $"⚠ Mise à jour disponible • {ShortSha(game.GitHubLatestCommit)}";
        GameUpdateStateText.Foreground =
            new SolidColorBrush(Color.FromRgb(216, 138, 22));
    }
    else if (game.RuntimeUpdatePendingBuild)
    {
        GameUpdateStateText.Text =
            IsEnglish
                ? $"◷ New commit • Windows build pending"
                : "◷ Nouveau commit • build Windows en attente";
        GameUpdateStateText.Foreground =
            new SolidColorBrush(Color.FromRgb(216, 138, 22));
    }
    else if (game.RuntimeDistributionChecked)
    {
        GameUpdateStateText.Text =
            IsEnglish
                ? $"✓ Up to date • {ShortSha(game.GitHubLatestCommit)}"
                : $"✓ À jour • {ShortSha(game.GitHubLatestCommit)}";
        GameUpdateStateText.Foreground =
            new SolidColorBrush(Color.FromRgb(34, 168, 97));
    }
    else
    {
        GameUpdateStateText.Text =
            IsEnglish ? "Checking updates…" : "Vérification des mises à jour…";
        GameUpdateStateText.Foreground =
            Application.Current.TryFindResource("Muted") as Brush ?? Brushes.Gray;
    }
}

    private void GameCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameDefinition game })
            SelectGame(game);
    }

    private void LibraryNav_Click(object sender, RoutedEventArgs e)
        => ShowLibrary();

    private void FavoritesNav_Click(object sender, RoutedEventArgs e)
        => ShowFavorites();

    private void ToggleFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
            return;

        _selectedGame.IsFavorite = !_selectedGame.IsFavorite;
        _selectedGame.Refresh();
        _libraryService.Save(_games);
        UpdateSelectedGamePlayUi();
        RefreshLibraryItems();
    }

    private async void ModsNav_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            MessageBox.Show(
                "Sélectionne d'abord un jeu dans la bibliothèque.",
                "CubeShelf",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            ShowLibrary();
            return;
        }

        await OpenModsAsync();
    }

    private void DownloadsNav_Click(object sender, RoutedEventArgs e)
        => ShowDownloads();

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
        => ShowSettings();

    private async void OpenModsButton_Click(object sender, RoutedEventArgs e)
        => await OpenModsAsync();

    private async Task OpenModsAsync()
    {
        if (_selectedGame is null)
            return;

        if (_gameBanana is null || _modManager is null)
        {
            MessageBox.Show(
                $"Aucun GameBanana Game ID n'est configuré pour « {_selectedGame.Title} ».",
                "Mods",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ModsGameTitle.Text = $"Mods • {_selectedGame.Title}";
        ShowMods();

        if (_preferences.RefreshModsOnOpen || _allMods.Count == 0)
            await RefreshModsAsync();
    }

    private void BackToGameButton_Click(object sender, RoutedEventArgs e)
        => ShowGame();

    private async Task RefreshModsAsync()
    {
        if (_gameBanana is null || _modManager is null)
            return;

        try
        {
            ModsStatusText.Text = "Actualisation GameBanana…";

            var remote = await _gameBanana.GetLatestModsAsync();
            var installed = _modManager
                .GetInstalled()
                .ToDictionary(x => x.Id);

            _allMods = remote.Select(r =>
            {
                installed.TryGetValue(r.Id, out var local);

                return new UiMod
                {
                    Id = r.Id,
                    Name = r.Name,
                    ProfileUrl = r.ProfileUrl,
                    Description = r.Description,
                    VersionLabel = r.VersionLabel,
                    LatestUpdateSummary = r.LatestUpdateSummary,
                    ThumbnailUrl = r.ThumbnailUrl,
                    Images = r.ImageUrls,
                    Installed = local is not null,
                    Enabled = local?.Enabled ?? false,
                    UpdateAvailable =
                        local is not null && r.Updated > local.Updated,
                    Priority = local?.Priority ?? 100
                };
            }).ToList();

            ApplyModSearch();

            ModsSummaryText.Text =
                $"{remote.Count} mods trouvés • " +
                $"{installed.Count} installés • " +
                $"{_allMods.Count(x => x.UpdateAvailable)} mises à jour";

            ModsStatusText.Text = "GameBanana actualisé";

            if (_visibleMods.Count > 0)
                ModsList.SelectedIndex = 0;
            else
                ClearModDetails();
        }
        catch (Exception ex)
        {
            ModsStatusText.Text = "Erreur GameBanana";

            MessageBox.Show(
                ex.Message,
                "GameBanana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplyModSearch()
    {
        var query = ModSearchBox.Text?.Trim() ?? "";
        _visibleMods.Clear();

        foreach (var mod in _allMods.Where(m =>
                     query.Length == 0 ||
                     m.Name.Contains(
                         query,
                         StringComparison.OrdinalIgnoreCase)))
        {
            _visibleMods.Add(mod);
        }
    }

    private void ModSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
        => ApplyModSearch();

    private async void RefreshModsButton_Click(
        object sender,
        RoutedEventArgs e)
        => await RefreshModsAsync();

    private void ModsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedMod = ModsList.SelectedItem as UiMod;
        _selectedModImageIndex = 0;
        UpdateModDetails();
    }

    private void UpdateModDetails()
    {
        if (_selectedMod is null)
        {
            ClearModDetails();
            return;
        }

        var mod = _selectedMod;

        ModDetailTitle.Text = mod.Name;
        ModDetailVersion.Text = mod.VersionLabel;
        ModDetailDescription.Text = mod.Description;
        ModDetailStatus.Text =
            mod.StatusText +
            (mod.UpdateAvailable ? " • mise à jour disponible" : "");

        SelectedModPriorityBox.Text = mod.Priority.ToString();
        SelectedModInstallButton.Content = mod.InstallText;
        SelectedModToggleButton.Content = mod.ToggleText;
        SelectedModToggleButton.IsEnabled = mod.Installed;

        UpdateModGallery();
    }

    private void ClearModDetails()
    {
        _selectedMod = null;
        ModDetailTitle.Text = "Sélectionne un mod";
        ModDetailVersion.Text = "";
        ModDetailDescription.Text = "Aucun mod sélectionné.";
        ModDetailStatus.Text = "";
        ModGalleryImage.Source = null;
        ModGalleryCounter.Text = "0 / 0";
    }

    private void UpdateModGallery()
    {
        if (_selectedMod is null || _selectedMod.Images.Count == 0)
        {
            ModGalleryImage.Source = null;
            ModGalleryCounter.Text = "0 / 0";
            return;
        }

        _selectedModImageIndex = Math.Clamp(
            _selectedModImageIndex,
            0,
            _selectedMod.Images.Count - 1);

        var url = _selectedMod.Images[_selectedModImageIndex];

        try
        {
            ModGalleryImage.Source =
                new BitmapImage(new Uri(url, UriKind.Absolute));
        }
        catch
        {
            ModGalleryImage.Source = null;
        }

        ModGalleryCounter.Text =
            $"{_selectedModImageIndex + 1} / {_selectedMod.Images.Count}";
    }

    private void PreviousModImage_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null || _selectedMod.Images.Count == 0)
            return;

        _selectedModImageIndex--;

        if (_selectedModImageIndex < 0)
            _selectedModImageIndex = _selectedMod.Images.Count - 1;

        UpdateModGallery();
    }

    private void NextModImage_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null || _selectedMod.Images.Count == 0)
            return;

        _selectedModImageIndex =
            (_selectedModImageIndex + 1) % _selectedMod.Images.Count;

        UpdateModGallery();
    }

    private void OpenSelectedModGameBanana_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null ||
            string.IsNullOrWhiteSpace(_selectedMod.ProfileUrl))
            return;

        Process.Start(
            new ProcessStartInfo(_selectedMod.ProfileUrl)
            {
                UseShellExecute = true
            });
    }

    private void InstallSelectedMod_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null || _modManager is null)
            return;

        var mod = _selectedMod;
        var manager = _modManager;

        var changes =
            !string.IsNullOrWhiteSpace(mod.LatestUpdateSummary)
                ? mod.LatestUpdateSummary
                : mod.Description[..Math.Min(180, mod.Description.Length)];

        _queue.Enqueue(
            $"mod:{_selectedGame?.Id}:{mod.Id}",
            mod.Name,
            "MOD",
            changes,
            async (progress, cancellationToken) =>
            {
                await manager.InstallAsync(
                    mod.Id,
                    p => progress.Report(p.Progress),
                    cancellationToken);
            },
            async () =>
            {
                if (ModsView.Visibility == Visibility.Visible)
                    await RefreshModsAsync();
            });

        ShowDownloads();
    }

    private async void ToggleSelectedMod_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null ||
            _modManager is null ||
            !_selectedMod.Installed)
            return;

        if (int.TryParse(
                SelectedModPriorityBox.Text,
                out var priority))
        {
            _modManager.SetPriority(_selectedMod.Id, priority);
        }

        _modManager.SetEnabled(
            _selectedMod.Id,
            !_selectedMod.Enabled);

        await RefreshModsAsync();
    }

    private async void UninstallSelectedMod_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedMod is null ||
            _modManager is null ||
            !_selectedMod.Installed)
            return;

        var result = MessageBox.Show(
            $"Supprimer « {_selectedMod.Name} » ?\n\n" +
            "Les fichiers originaux du jeu ne seront pas modifiés.",
            "CubeShelf",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        _modManager.Uninstall(_selectedMod.Id);
        await RefreshModsAsync();
    }

    private void UpdateAllModsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_modManager is null)
            return;

        var manager = _modManager;
        var updates = _allMods
            .Where(x => x.UpdateAvailable)
            .ToList();

        if (updates.Count == 0)
        {
            MessageBox.Show(
                "Aucune mise à jour de mod disponible.",
                "CubeShelf",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        foreach (var mod in updates)
        {
            var captured = mod;

            _queue.Enqueue(
                $"mod:{_selectedGame?.Id}:{captured.Id}",
                captured.Name,
                "MOD UPDATE",
                FirstNonEmpty(
                    captured.LatestUpdateSummary,
                    "Nouvelle version GameBanana"),
                async (progress, cancellationToken) =>
                {
                    await manager.InstallAsync(
                        captured.Id,
                        p => progress.Report(p.Progress),
                        cancellationToken);
                });
        }

        ShowDownloads();
    }

    private static string FirstNonEmpty(
        string first,
        string second)
        => !string.IsNullOrWhiteSpace(first) ? first : second;

    private void GitHubDownloadButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedGame is null || !_selectedGame.GitHubConfigured)
            return;

        QueueGameUpdate(_selectedGame);
        ShowDownloads();
    }


private void QueueGameUpdate(GameDefinition game)
{
    if (_runtimeInstaller.ApplyInstalledRuntime(game))
        _libraryService.Resolve(game);

    if (!game.RuntimeReleaseAvailable)
    {
        MessageBox.Show(
            IsEnglish
                ? "No public Windows build is currently available. You can still download the GitHub repository with the repository button."
                : "Aucun build Windows public n'est disponible actuellement. Tu peux quand même télécharger les fichiers du dépôt GitHub avec le bouton « Télécharger le dépôt ».",
            "CubeShelf",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }

    var changes = string.IsNullOrWhiteSpace(game.GitHubChangeLog)
        ? game.GitHubLatestMessage
        : game.GitHubChangeLog;

    var added = _queue.Enqueue(
        $"runtime:{game.Id}",
        game.Title,
        "GAME / WINDOWS",
        changes,
        async (progress, cancellationToken) =>
        {
            var state = await _runtimeInstaller.InstallLatestAsync(
                game,
                progress,
                cancellationToken);

            game.Executable = state.ExecutablePath;
            game.GameRoot = Path.Combine(
                Path.GetDirectoryName(state.ExecutablePath)!,
                game.Id);

            _libraryService.Resolve(game);
            game.RuntimeInstalled = true;
            game.GameDataReady = state.GameDataReady;
            game.RuntimeStatusText = state.GameDataReady
                ? (IsEnglish ? "Installed • ready to play" : "Installé • prêt à jouer")
                : (IsEnglish ? "Installed • select your ISO/RVZ" : "Installé • sélectionne ton ISO/RVZ");

            _libraryService.Save(_games);
        },
        async () =>
        {
            _runtimeInstaller.ApplyInstalledRuntime(game);
            _libraryService.Resolve(game);
            _libraryService.Save(_games);

            if (_selectedGame == game)
            {
                _gameLauncher = new GameLauncher(game, _modManager);
                SelectedInstallStatus.Text = game.RuntimeStatusText;
                ExecutablePathText.Text = game.ExecutableFullPath;
                UpdateDiscCompatibility();
                UpdateSelectedGameGitHubPanel();
            }

            await RefreshGameUpdateStatusAsync(game);
            UpdateLibraryUpdateSummary();

            ShowToast(
                IsEnglish ? "Game update finished" : "Mise à jour du jeu terminée",
                IsEnglish
                    ? $"{game.Title} was updated. CubeShelf will never launch it automatically after an update."
                    : $"{game.Title} a été mis à jour. CubeShelf ne le lancera jamais automatiquement après une mise à jour.");
        });

    if (!added)
    {
        MessageBox.Show(
            IsEnglish
                ? "This game is already in the download queue."
                : "Ce jeu est déjà présent dans la file de téléchargement.",
            "CubeShelf");
    }
}


private void DownloadRepositoryButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null || !_selectedGame.GitHubConfigured)
        return;

    if (QueueRepositoryUpdate(_selectedGame, showDuplicateMessage: true))
        ShowDownloads();
}

private bool QueueRepositoryUpdate(
    GameDefinition game,
    bool showDuplicateMessage)
{
    ApplyLocalRepositoryStatus(
        game,
        _github.DetectLocalRepository(game));

    if (game.GitHubLocalRepositoryPresent &&
        !game.GitHubUpdateAvailable)
    {
        if (_selectedGame == game)
            UpdateSelectedGameGitHubPanel();

        return false;
    }

    var added = _queue.Enqueue(
        $"source:{game.Id}",
        game.Title,
        "GITHUB / SOURCE",
        game.GitHubChangeLog,
        async (progress, cancellationToken) =>
        {
            game.GitHubSourcePath = await _github.DownloadLatestAsync(
                game,
                progress,
                cancellationToken);
        },
        async () =>
        {
            ApplyLocalRepositoryStatus(
                game,
                _github.DetectLocalRepository(game));

            await RefreshGameUpdateStatusAsync(game);

            if (_selectedGame == game)
                UpdateSelectedGameGitHubPanel();

            UpdateLibraryUpdateSummary();
        });

    if (!added && showDuplicateMessage)
    {
        MessageBox.Show(
            IsEnglish
                ? "The repository is already downloading."
                : "Le dépôt est déjà en cours de téléchargement.",
            "CubeShelf");
    }

    return added;
}

private void DeleteRepositoryButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    ApplyLocalRepositoryStatus(
        game,
        _github.DetectLocalRepository(game));

    if (!game.GitHubLocalRepositoryPresent)
    {
        ShowToast(
            IsEnglish ? "No local repository" : "Aucun dépôt local",
            IsEnglish
                ? "CubeShelf did not find a local GitHub repository to delete."
                : "CubeShelf n'a trouvé aucun dépôt GitHub local à supprimer.");

        UpdateSelectedGameGitHubPanel();
        return;
    }

    ShowRepositoryDeleteConfirmation(game);
}

    private async void CheckSelectedGameUpdateButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedGame is null)
            return;

        await RefreshGameUpdateStatusAsync(_selectedGame);
        UpdateLibraryUpdateSummary();
        if (_selectedGame.GitHubUpdateAvailable && _preferences.ShowGameUpdatePopup)
        {
            _gameUpdatePopupShown = false;
            MaybeShowGameUpdatePopup();
        }
    }

    
private async void PlayButton_Click(
    object sender,
    RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    if (_runningGames.TryGetValue(game.Id, out var running) &&
        IsProcessRunning(running.Process))
    {
        await StopRunningGameAsync(game, running.Process);
        return;
    }

    if (!game.IsInstalled)
    {
        if (_runtimeInstaller.ApplyInstalledRuntime(game))
        {
            _libraryService.Resolve(game);
            _gameLauncher = new GameLauncher(game, _modManager);
        }
        else
        {
            QueueGameUpdate(game);

            if (game.RuntimeReleaseAvailable)
                ShowDownloads();

            return;
        }
    }
    else if (!game.RuntimeInstalled)
    {
        _runtimeInstaller.AdoptConfiguredExecutable(game);
        _libraryService.Resolve(game);
        _gameLauncher = new GameLauncher(game, _modManager);
    }

    if (!game.HasDiscImage)
    {
        ConfigureDiscImage();
        if (!game.HasDiscImage)
            return;
    }

    var state =
        _runtimeInstaller.ReadState(game) ??
        _runtimeInstaller.AdoptConfiguredExecutable(game);

    var ready =
        state is not null &&
        state.GameDataReady &&
        Directory.Exists(
            Path.Combine(
                Path.GetDirectoryName(state.ExecutablePath)!,
                game.Id,
                "files"));

    if (!ready)
    {
        QueueGameDataPreparation(game);

        ShowDownloads();
        ShowToast(
            IsEnglish ? "Preparing the game" : "Préparation du jeu",
            IsEnglish
                ? "CubeShelf will prepare the data in the background. The game will not start automatically."
                : "CubeShelf prépare les données en arrière-plan. Le jeu ne démarrera pas automatiquement.");
        return;
    }

    StartGameTracked(game);
}

private static bool IsProcessRunning(Process process)
{
    try
    {
        return !process.HasExited;
    }
    catch
    {
        return false;
    }
}

private async Task StopRunningGameAsync(
    GameDefinition game,
    Process process)
{
    PlayButton.IsEnabled = false;
    PlayButton.Content =
        IsEnglish ? "Stopping…" : "Arrêt…";

    try
    {
        if (IsProcessRunning(process))
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
            }

            var gracefulExit = process.WaitForExitAsync();
            var timeout =
                Task.Delay(TimeSpan.FromSeconds(2));

            if (await Task.WhenAny(
                    gracefulExit,
                    timeout) != gracefulExit &&
                IsProcessRunning(process))
            {
                // Killing only PartyBoard itself is much less aggressive than
                // Kill(entireProcessTree: true) and avoids the Win32 resource
                // error seen on some Windows machines.
                process.Kill(
                    entireProcessTree: false);

                await process.WaitForExitAsync();
            }
        }
    }
    catch (InvalidOperationException)
    {
        // The process may already have exited between checks.
    }
    catch (System.ComponentModel.Win32Exception ex)
    {
        ShowToast(
            IsEnglish ? "Unable to stop the game" : "Impossible d'arrêter le jeu",
            ex.Message);
    }
    finally
    {
        FinalizeTrackedGameSession(
            game,
            process);

        PlayButton.IsEnabled = true;
        UpdateSelectedGamePlayUi();
    }
}

private void StartGameTracked(GameDefinition game)
{
    try
    {
        if (_runningGames.TryGetValue(
                game.Id,
                out var existing) &&
            IsProcessRunning(existing.Process))
        {
            return;
        }

        var modsForGame =
            _selectedGame == game
                ? _modManager
                : null;

        _gameLauncher =
            new GameLauncher(
                game,
                modsForGame);

        var process =
            _gameLauncher.StartGame();

        var startedAt =
            DateTimeOffset.Now;

        _runningGames[game.Id] =
            (process, startedAt);

        process.Exited += (_, _) =>
        {
            try
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        FinalizeTrackedGameSession(
                            game,
                            process);
                    }));
            }
            catch
            {
                // CubeShelf may itself be closing.
            }
        };

        process.EnableRaisingEvents = true;

        game.PlayCount++;
        game.LastPlayedAt = startedAt;
        game.Refresh();

        _libraryService.Save(_games);
        RefreshLibraryItems();
        UpdateSelectedGamePlayUi();

        _launcherMinimizedForGame = true;
        WindowState =
            WindowState.Minimized;
    }
    catch (Exception ex)
    {
        ShowToast(
            IsEnglish ? "Unable to start the game" : "Impossible de démarrer le jeu",
            ex.Message);
    }
}

private void FinalizeTrackedGameSession(
    GameDefinition game,
    Process process)
{
    if (_runningGames.TryGetValue(
            game.Id,
            out var tracked) &&
        ReferenceEquals(
            tracked.Process,
            process))
    {
        _runningGames.Remove(game.Id);

        var elapsed =
            DateTimeOffset.Now -
            tracked.StartedAt;

        if (elapsed > TimeSpan.Zero)
        {
            game.TotalPlaySeconds +=
                (long)Math.Round(
                    elapsed.TotalSeconds);
        }

        _ = DisposeProcessLaterAsync(
            tracked.Process);

        game.Refresh();
        _libraryService.Save(_games);
        RefreshLibraryItems();
    }

    if (_selectedGame == game)
        UpdateSelectedGamePlayUi();

    RestoreLauncherAfterGameIfNeeded();
}

private static async Task DisposeProcessLaterAsync(
    Process process)
{
    try
    {
        // Give any pending WaitForExitAsync continuation time to finish before
        // releasing the native Process handle.
        await Task.Delay(
            TimeSpan.FromSeconds(2));

        process.Dispose();
    }
    catch
    {
    }
}

private void RestoreLauncherAfterGameIfNeeded()
{
    if (!_launcherMinimizedForGame)
        return;

    var anyGameStillRunning =
        _runningGames.Values.Any(
            x => IsProcessRunning(x.Process));

    if (anyGameStillRunning)
        return;

    _launcherMinimizedForGame = false;

    try
    {
        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Show();
        Activate();
    }
    catch
    {
        // Restoring the launcher must never turn a game exit into a launcher crash.
    }
}

private void UpdateSelectedGamePlayUi()
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    var running =
        _runningGames.TryGetValue(
            game.Id,
            out var item) &&
        IsProcessRunning(item.Process);

    var runtimeReady =
        game.RuntimeInstalled &&
        File.Exists(
            game.ExecutableFullPath);

    if (running)
    {
        PlayButton.Content =
            IsEnglish
                ? "■ Stop"
                : "■ Arrêter";

        PlayButton.Background =
            Application.Current.TryFindResource(
                "Danger") as Brush ??
            Brushes.IndianRed;

        PlayButton.Foreground =
            Brushes.White;
    }
    else
    {
        PlayButton.Content =
            !runtimeReady
                ? (IsEnglish
                    ? "⬇ Install"
                    : "⬇ Installer")
                : !game.HasDiscImage
                    ? (IsEnglish
                        ? "Choose ISO / RVZ"
                        : "Choisir ISO / RVZ")
                    : !game.GameDataReady
                        ? (IsEnglish
                            ? "Prepare game"
                            : "Préparer le jeu")
                        : (IsEnglish
                            ? "▶ Play"
                            : "▶ Jouer");

        PlayButton.Background =
            !runtimeReady ||
            !game.HasDiscImage ||
            !game.GameDataReady
                ? (Application.Current.TryFindResource(
                       "Accent") as Brush ??
                   Brushes.MediumPurple)
                : Brushes.White;

        PlayButton.Foreground =
            !runtimeReady ||
            !game.HasDiscImage ||
            !game.GameDataReady
                ? Brushes.White
                : new SolidColorBrush(
                    Color.FromRgb(
                        16,
                        20,
                        30));
    }

    FavoriteGameButton.Content =
        game.IsFavorite
            ? (IsEnglish
                ? "★ Favorite"
                : "★ Favori")
            : (IsEnglish
                ? "☆ Add favorite"
                : "☆ Ajouter aux favoris");

    GameStatsText.Text =
        IsEnglish
            ? $"{game.PlayCount} launches • {game.PlayTimeText} • Last: {game.LastPlayedText}"
            : $"{game.PlayCount} lancements • {game.PlayTimeText} • Dernier : {game.LastPlayedText}";

    UpdateSetupProgress(game);
}

private void ConfigureExecutableButton_Click(
        object sender,
        RoutedEventArgs e)
        => ConfigureExecutable();

    
private void ConfigureExecutable()
{
    if (_selectedGame is null)
        return;

    var dialog = new OpenFileDialog
    {
        Title =
            $"Sélectionne l'exécutable de {_selectedGame.Title}",
        Filter =
            "Application Windows (*.exe)|*.exe|" +
            "Tous les fichiers (*.*)|*.*",
        CheckFileExists = true
    };

    if (dialog.ShowDialog(this) != true)
        return;

    _selectedGame.Executable = dialog.FileName;
    _libraryService.Resolve(_selectedGame);

    _runtimeInstaller.AdoptConfiguredExecutable(_selectedGame);
    _libraryService.Resolve(_selectedGame);
    _libraryService.Save(_games);

    _gameLauncher =
        new GameLauncher(_selectedGame, _modManager);

    SelectedInstallStatus.Text =
        _selectedGame.RuntimeInstalled
            ? _selectedGame.RuntimeStatusText
            : _selectedGame.InstallStatus;

    ExecutablePathText.Text =
        _selectedGame.ExecutableFullPath;

    DiscImagePathText.Text = _selectedGame.DiscStatus;
    UpdateDiscCompatibility();
    UpdateSelectedGameGitHubPanel();
    _selectedGame.Refresh();
}

private void ConfigureDiscImageButton_Click(
        object sender,
        RoutedEventArgs e)
        => ConfigureDiscImage();

    private void ConfigureDiscImage()
    {
        if (_selectedGame is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = IsEnglish
                ? $"Select the ISO / RVZ for {_selectedGame.Title}"
                : $"Sélectionne l'ISO / RVZ de {_selectedGame.Title}",
            Filter =
                "GameCube images (*.iso;*.rvz;*.gcm)|*.iso;*.rvz;*.gcm|" +
                "ISO (*.iso)|*.iso|RVZ (*.rvz)|*.rvz|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _selectedGame.DiscImage = dialog.FileName;
        _libraryService.Resolve(_selectedGame);
        _libraryService.Save(_games);

        DiscImagePathText.Text = _selectedGame.DiscStatus;
        UpdateDiscCompatibility();
        _selectedGame.Refresh();
        UpdateSelectedGamePlayUi();

        // Selecting an ISO/RVZ only stores the user's choice.
        // Data preparation starts when the user presses Play.
        if (_selectedGame.RuntimeInstalled)
        {
            SelectedInstallStatus.Text = IsEnglish
                ? $"Disc selected: {Path.GetFileName(_selectedGame.DiscImageFullPath)} • press Play"
                : $"Image sélectionnée : {Path.GetFileName(_selectedGame.DiscImageFullPath)} • clique sur Jouer";
        }
    }

    private void QueueGameDataPreparation(
        GameDefinition game)
    {
        var runtime = _runtimeInstaller.ReadState(game);

        if (runtime is null || !File.Exists(runtime.ExecutablePath))
        {
            QueueGameUpdate(game);
            return;
        }

        var discPath = game.DiscImageFullPath;

        if (string.IsNullOrWhiteSpace(discPath) || !File.Exists(discPath))
        {
            MessageBox.Show(
                IsEnglish
                    ? "Select your ISO / RVZ before starting the game."
                    : "Sélectionne ton ISO / RVZ avant de démarrer le jeu.",
                "CubeShelf",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var key = $"gamedata:{game.Id}";

        // Remove a previous failed/completed preparation before retrying.
        _queue.RemoveTerminalByKey(key);

        var discName = Path.GetFileName(discPath);
        var allowDolphinDownload = false;

        if (Path.GetExtension(discPath).Equals(
                ".rvz",
                StringComparison.OrdinalIgnoreCase) &&
            !_runtimeInstaller.IsDolphinToolAvailable())
        {
            var answer = MessageBox.Show(
                IsEnglish
                    ? "This RVZ needs DolphinTool to be converted.\n\nDownload the official Dolphin Windows tool automatically now?"
                    : "Ce RVZ nécessite DolphinTool pour être converti.\n\nTélécharger automatiquement l'outil Windows officiel de Dolphin maintenant ?",
                "CubeShelf • RVZ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            allowDolphinDownload = true;
        }

        var added = _queue.Enqueue(
            key,
            game.Title,
            "GAME DATA",
            IsEnglish
                ? $"Preparing {discName} for PartyBoard."
                : $"Préparation de {discName} pour PartyBoard.",
            async (progress, cancellationToken) =>
            {
                await _runtimeInstaller.PrepareGameDataAsync(
                    game,
                    runtime,
                    progress,
                    cancellationToken,
                    discImagePath: discPath,
                    allowDolphinToolDownload: allowDolphinDownload);
            },
            () =>
            {
                _runtimeInstaller.ApplyInstalledRuntime(game);
                _libraryService.Resolve(game);
                _libraryService.Save(_games);

                if (_selectedGame == game)
                {
                    SelectedInstallStatus.Text =
                        IsEnglish
                            ? "Installed • ready to play"
                            : "Installé • prêt à jouer";

                    ExecutablePathText.Text = game.ExecutableFullPath;
                    UpdateDiscCompatibility();
                    UpdateSelectedGameGitHubPanel();
                    UpdateSelectedGamePlayUi();
                }

                ShowToast(
                    IsEnglish ? "Game ready" : "Jeu prêt",
                    IsEnglish
                        ? $"{game.Title} is ready. Click Play when you want to start it."
                        : $"{game.Title} est prêt. Clique sur Jouer quand tu souhaites le démarrer.");

                return Task.CompletedTask;
            });

        if (!added)
        {
            ShowToast(
                IsEnglish ? "Preparation already running" : "Préparation déjà en cours",
                IsEnglish
                    ? "The game data is already being prepared."
                    : "Les données du jeu sont déjà en cours de préparation.");
        }
    }

    private void ClearDiscImageButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedGame is null)
            return;

        _selectedGame.DiscImage = "";
        _libraryService.Resolve(_selectedGame);
        _libraryService.Save(_games);

        DiscImagePathText.Text =
            _selectedGame.DiscStatus;
        UpdateDiscCompatibility();

        _selectedGame.Refresh();
        UpdateSelectedGamePlayUi();
    }

    private void FlipCaseButton_Click(
        object sender,
        RoutedEventArgs e)
        => GameCase.Flip();

    private void AddGameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Ajouter un port GameCube / exécutable",
            Filter =
                "Application Windows (*.exe)|*.exe|" +
                "Tous les fichiers (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var game =
            _libraryService.AddCustomGame(dialog.FileName);

        _games.Add(game);
        _libraryService.Save(_games);
        RefreshLibraryItems();
    }


    private void LoadSettingsControls()
    {
        _settingsLoaded = false;
        LanguageCombo.SelectedIndex = _preferences.Language == "en" ? 1 : 0;
        CheckGamesStartupBox.IsChecked = _preferences.CheckGamesOnStartup;
        GameUpdatePopupBox.IsChecked = _preferences.ShowGameUpdatePopup;
        RefreshModsOnOpenBox.IsChecked = _preferences.RefreshModsOnOpen;
        DataFolderText.Text = _preferencesService.DataDirectory;
        _settingsLoaded = true;
    }

    private void UpdateDiscCompatibility()
    {
        if (_selectedGame is null) return;
        var result = DiscImageService.Inspect(_selectedGame.DiscImageFullPath, IsEnglish);
        DiscCompatibilityText.Text = result.Message + Environment.NewLine + DiscImageService.SupportedSummary;
    }

    private void MaybeShowGameUpdatePopup()
    {
        if (_gameUpdatePopupShown ||
            !_preferences.ShowGameUpdatePopup)
        {
            return;
        }

        var updates = _games
            .Where(x => x.GitHubUpdateAvailable)
            .ToList();

        if (updates.Count == 0)
            return;

        _gameUpdatePopupShown = true;

        var runtimeUpdates = updates
            .Where(x => x.RuntimeReleaseAvailable)
            .ToList();

        var sourceOnlyUpdates = updates
            .Where(x =>
                !x.RuntimeReleaseAvailable &&
                x.GitHubConfigured)
            .ToList();

        if (runtimeUpdates.Count == 0 &&
            sourceOnlyUpdates.Count > 0)
        {
            GameUpdatePopupEyebrow.Text =
                IsEnglish
                    ? "GITHUB REPOSITORY UPDATE AVAILABLE"
                    : "MISE À JOUR DU DÉPÔT GITHUB DISPONIBLE";

            GameUpdatePopupTitle.Text =
                sourceOnlyUpdates.Count == 1
                    ? sourceOnlyUpdates[0].Title
                    : IsEnglish
                        ? $"{sourceOnlyUpdates.Count} repository updates available"
                        : $"{sourceOnlyUpdates.Count} dépôts à mettre à jour";

            GameUpdatePopupActionButton.Content =
                IsEnglish
                    ? "Update repository"
                    : "Mettre à jour le dépôt";
        }
        else if (sourceOnlyUpdates.Count == 0)
        {
            GameUpdatePopupEyebrow.Text =
                IsEnglish
                    ? "GAME UPDATE AVAILABLE"
                    : "MISE À JOUR DE JEU DISPONIBLE";

            GameUpdatePopupTitle.Text =
                runtimeUpdates.Count == 1
                    ? runtimeUpdates[0].Title
                    : IsEnglish
                        ? $"{runtimeUpdates.Count} game updates available"
                        : $"{runtimeUpdates.Count} jeux à mettre à jour";

            GameUpdatePopupActionButton.Content =
                IsEnglish
                    ? "Update now"
                    : "Mettre à jour maintenant";
        }
        else
        {
            GameUpdatePopupEyebrow.Text =
                IsEnglish
                    ? "UPDATES AVAILABLE"
                    : "MISES À JOUR DISPONIBLES";

            GameUpdatePopupTitle.Text =
                IsEnglish
                    ? $"{updates.Count} updates available"
                    : $"{updates.Count} mises à jour disponibles";

            GameUpdatePopupActionButton.Content =
                IsEnglish
                    ? "Update all"
                    : "Tout mettre à jour";
        }

        GameUpdatePopupText.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            updates.Select(game =>
            {
                var updateType =
                    game.RuntimeReleaseAvailable
                        ? (IsEnglish ? "Windows game build" : "Build Windows du jeu")
                        : (IsEnglish ? "GitHub repository" : "Dépôt GitHub");

                return
                    $"{game.Title} — {updateType}" +
                    Environment.NewLine +
                    game.GitHubStatusText +
                    Environment.NewLine +
                    game.GitHubChangeLog;
            }));

        GameUpdatePopup.Visibility = Visibility.Visible;
        GameUpdatePopup.Opacity = 0;

        GameUpdatePopup.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(180)));
    }

    private void UpdatePopupLater_Click(
        object sender,
        RoutedEventArgs e)
        => GameUpdatePopup.Visibility = Visibility.Collapsed;

    private void UpdatePopupNow_Click(
        object sender,
        RoutedEventArgs e)
    {
        var updates = _games
            .Where(x => x.GitHubUpdateAvailable)
            .ToList();

        var queuedSomething = false;

        foreach (var game in updates)
        {
            if (game.RuntimeReleaseAvailable)
            {
                QueueGameUpdate(game);
                queuedSomething = true;
            }
            else if (game.GitHubConfigured)
            {
                queuedSomething |= QueueRepositoryUpdate(
                    game,
                    showDuplicateMessage: false);
            }
        }

        GameUpdatePopup.Visibility = Visibility.Collapsed;

        if (queuedSomething)
            ShowDownloads();
    }


private void CancelDownload_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: DownloadQueueItem item })
        _queue.Cancel(item);
}

private void RemoveDownload_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button { Tag: DownloadQueueItem item })
        _queue.Remove(item);
}

private void ClearFinishedDownloads_Click(object sender, RoutedEventArgs e)
    => _queue.ClearFinished();

private void DeleteRuntimeButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;
    var answer = MessageBox.Show(
        IsEnglish
            ? "Delete PartyBoard but keep the game data prepared from your ISO/RVZ?"
            : "Supprimer PartyBoard en conservant les données préparées depuis ton ISO/RVZ ?",
        "CubeShelf",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
        return;

    _runtimeInstaller.DeleteRuntime(game, keepPreparedGameData: true);
    _libraryService.Resolve(game);
    _libraryService.Save(_games);
    RefreshSelectedLocalState();
}

private void DeleteGameDataButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;
    var answer = MessageBox.Show(
        IsEnglish
            ? "Delete the extracted game data? Your original ISO/RVZ file will not be deleted."
            : "Supprimer les données extraites du jeu ? Ton fichier ISO/RVZ original ne sera pas supprimé.",
        "CubeShelf",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
        return;

    _runtimeInstaller.DeletePreparedGameData(game);
    _libraryService.Resolve(game);
    _libraryService.Save(_games);
    RefreshSelectedLocalState();
}

private void DeleteAllGameFilesButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;
    var answer = MessageBox.Show(
        IsEnglish
            ? "Delete PartyBoard, prepared game data and the downloaded repository? Your original ISO/RVZ and mods are kept."
            : "Supprimer PartyBoard, les données préparées et le dépôt téléchargé ? Ton ISO/RVZ original et tes mods seront conservés.",
        "CubeShelf",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (answer != MessageBoxResult.Yes)
        return;

    _runtimeInstaller.DeleteAllRuntimeFiles(game);
    _github.DeleteDownloadedSource(game);
    game.GitHubSourcePath = "";

    _libraryService.Resolve(game);
    _libraryService.Save(_games);
    RefreshSelectedLocalState();
    _ = RefreshGameUpdateStatusAsync(game);
}

private void RefreshSelectedLocalState()
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    SelectedInstallStatus.Text = game.RuntimeInstalled
        ? game.RuntimeStatusText
        : (IsEnglish ? "Not installed" : "Non installé");

    ExecutablePathText.Text = game.IsInstalled
        ? game.ExecutableFullPath
        : (IsEnglish ? "Not installed" : "Non installé");

    DiscImagePathText.Text = game.DiscStatus;
    UpdateDiscCompatibility();
    UpdateSelectedGameGitHubPanel();
    UpdateSelectedGameStateBadges();
    game.Refresh();
}

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded || LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string language) return;
        _preferences.Language = language;
        _preferencesService.Save(_preferences);
        _preferencesService.ApplyLanguage(language);
        UpdateLibraryUpdateSummary();
        UpdateQueueSummary();
        UpdateDiscCompatibility();

        if (_selectedGame is not null)
        {
            SidebarSelectedGame.Text =
                $"{_selectedGame.Title}\n{_selectedGame.Id}";

            UpdateSelectedGamePlayUi();
        }
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("dark");
    private void LightTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("light");

    private void ApplyTheme(string theme)
    {
        if (_preferences.Theme == theme) return;
        var fadeIn = new DoubleAnimation(0, 0.42, TimeSpan.FromMilliseconds(110));
        fadeIn.Completed += (_, _) =>
        {
            _preferences.Theme = theme;
            _preferencesService.Save(_preferences);
            _preferencesService.ApplyTheme(theme);
            UpdateThemeIndicator(true);
            ThemeTransitionOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0.42, 0, TimeSpan.FromMilliseconds(230)));
        };
        ThemeTransitionOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void UpdateThemeIndicator(bool animated)
    {
        var target = _preferences.Theme == "light" ? 125.0 : 4.0;
        if (!animated) { ThemeIndicatorTransform.X = target; return; }
        ThemeIndicatorTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(ThemeIndicatorTransform.X, target, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void PreferenceCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded) return;
        _preferences.CheckGamesOnStartup = CheckGamesStartupBox.IsChecked == true;
        _preferences.ShowGameUpdatePopup = GameUpdatePopupBox.IsChecked == true;
        _preferences.RefreshModsOnOpen = RefreshModsOnOpenBox.IsChecked == true;
        _preferencesService.Save(_preferences);

        if (_preferences.CheckGamesOnStartup)
        {
            if (!_githubUpdateTimer.IsEnabled)
                _githubUpdateTimer.Start();

            _ = RefreshAllGameUpdateStatusesAsync();
        }
        else
        {
            _githubUpdateTimer.Stop();
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_preferencesService.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _preferencesService.DataDirectory) { UseShellExecute = true });
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _preferencesService.Reset();
        _preferences = new UserPreferences();
        _preferencesService.ApplyTheme(_preferences.Theme);
        _preferencesService.ApplyLanguage(_preferences.Language);
        LoadSettingsControls();
        UpdateThemeIndicator(true);
        ShowFirstStartAssistant();
    }

    private async Task CheckLauncherUpdateAsync(
        bool showUpToDate)
    {
        if (_launcherUpdatePreparing)
            return;

        _launcherUpdatePreparing = true;

        try
        {
            var info =
                await _updates.CheckAsync();

            if (!info.UpdateAvailable)
            {
                if (showUpToDate)
                {
                    LauncherUpdateReadyTitle.Text =
                        IsEnglish
                            ? "CubeShelf is up to date"
                            : "CubeShelf est à jour";

                    LauncherUpdateReadyText.Text =
                        info.Message;

                    LauncherUpdateRestartButton.Visibility =
                        Visibility.Collapsed;

                    LauncherUpdateReadyPopup.Visibility =
                        Visibility.Visible;
                }

                return;
            }

            // Run the whole download/hash pipeline on a thread-pool thread.
            // UpdateService performs many awaited reads while downloading large ZIPs;
            // invoking it from the UI context made WPF sluggish on some PCs.
            var prepared =
                await Task.Run(
                    () => _updates.PrepareUpdateAsync(
                        info,
                        progress: null));

            _preparedLauncherUpdate =
                prepared;

            await Dispatcher.InvokeAsync(() =>
            {
                LauncherUpdateReadyTitle.Text =
                    IsEnglish
                        ? $"CubeShelf {info.LatestVersion} is ready"
                        : $"CubeShelf {info.LatestVersion} est prêt";

                LauncherUpdateReadyText.Text =
                    IsEnglish
                        ? $"The update was downloaded and verified in the background.\nCurrent version: {info.CurrentVersion}\n\nRestart CubeShelf when you are ready to install it."
                        : $"La mise à jour a été téléchargée et vérifiée en arrière-plan.\nVersion actuelle : {info.CurrentVersion}\n\nRedémarre CubeShelf quand tu veux pour l'installer.";

                LauncherUpdateRestartButton.Visibility =
                    Visibility.Visible;

                LauncherUpdateReadyPopup.Opacity = 0;
                LauncherUpdateReadyPopup.Visibility =
                    Visibility.Visible;

                LauncherUpdateReadyPopup.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(
                        0,
                        1,
                        TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction =
                            new QuadraticEase
                            {
                                EasingMode =
                                    EasingMode.EaseOut
                            }
                    });
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (showUpToDate)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LauncherUpdateReadyTitle.Text =
                        IsEnglish
                            ? "Update unavailable"
                            : "Mise à jour indisponible";

                    LauncherUpdateReadyText.Text =
                        ex.Message;

                    LauncherUpdateRestartButton.Visibility =
                        Visibility.Collapsed;

                    LauncherUpdateReadyPopup.Visibility =
                        Visibility.Visible;
                });
            }
        }
        finally
        {
            _launcherUpdatePreparing = false;
        }
    }

    private void LauncherUpdateLater_Click(
        object sender,
        RoutedEventArgs e)
    {
        LauncherUpdateReadyPopup.Visibility =
            Visibility.Collapsed;
    }

    private void LauncherUpdateRestart_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_preparedLauncherUpdate is null)
        {
            LauncherUpdateReadyPopup.Visibility =
                Visibility.Collapsed;
            return;
        }

        try
        {
            LauncherUpdateRestartButton.IsEnabled =
                false;

            _updates.LaunchPreparedUpdate(
                _preparedLauncherUpdate);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LauncherUpdateRestartButton.IsEnabled =
                true;

            LauncherUpdateReadyText.Text =
                IsEnglish
                    ? $"Unable to restart for the update.\n\n{ex.Message}"
                    : $"Impossible de redémarrer pour la mise à jour.\n\n{ex.Message}";
        }
    }

}
