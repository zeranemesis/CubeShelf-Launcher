using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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

        _queue.QueueChanged += (_, _) => UpdateQueueSummary();
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

            if (_config.AutoCheckLauncherUpdates)
                await CheckLauncherUpdateAsync(false);

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
        SidebarSelectedGame.Text = IsEnglish ? "Library" : "Bibliothèque";
        ShowOnly(LibraryView);
    }

    private void ShowGame()
        => ShowOnly(GameView);

    private void ShowMods()
        => ShowOnly(ModsView);

    private void ShowDownloads()
        => ShowOnly(DownloadsView);

    private void ShowSettings()
        => ShowOnly(SettingsView);

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
                game.GitHubUpdateAvailable = false;
                game.GitHubStatusText = "GitHub : " + ex.Message;
                game.Refresh();
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
            game.GitHubStatusText = "GitHub non configuré";
        }
        else if (!status.Downloaded)
        {
            game.GitHubStatusText =
                $"Projet GitHub disponible • {ShortSha(status.LatestSha)}";
        }
        else if (status.UpdateAvailable)
        {
            game.GitHubStatusText =
                $"Mise à jour disponible • {ShortSha(status.CurrentSha)} → " +
                $"{ShortSha(status.LatestSha)}";
        }
        else
        {
            game.GitHubStatusText =
                $"À jour • {ShortSha(status.LatestSha)}";
        }

        game.Refresh();

        if (_selectedGame == game)
            UpdateSelectedGameGitHubPanel();
    }

    private void UpdateLibraryUpdateSummary()
    {
        var updates = _games.Count(x => x.GitHubUpdateAvailable);
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
            : (IsEnglish ? "GitHub repository not configured" : "Dépôt GitHub non configuré");

    GitHubStatusText.Text = game.GitHubStatusText;

    if (game.GitHubLocalRepositoryPresent)
    {
        var localType =
            game.GitHubLocalRepositoryManaged
                ? (IsEnglish ? "managed by CubeShelf" : "géré par CubeShelf")
                : (IsEnglish ? "existing local repository" : "dépôt local existant");

        GitHubLocalStatusText.Text =
            IsEnglish
                ? $"✓ Local GitHub repository present ({localType})\n{game.GitHubLocalRepositoryPath}"
                : $"✓ Dépôt GitHub présent ({localType})\n{game.GitHubLocalRepositoryPath}";
    }
    else
    {
        GitHubLocalStatusText.Text =
            IsEnglish
                ? "✕ No local GitHub repository detected"
                : "✕ Aucun dépôt GitHub local détecté";
    }

    GitHubChangesText.Text = game.GitHubChangeLog;

    if (game.RuntimeInstalled)
    {
        GitHubDownloadButton.Content =
            game.GitHubUpdateAvailable && game.RuntimeReleaseAvailable
                ? (IsEnglish ? "Update game" : "Mettre à jour le jeu")
                : (IsEnglish ? "✓ Game installed" : "✓ Jeu installé");

        GitHubDownloadButton.IsEnabled =
            game.GitHubUpdateAvailable && game.RuntimeReleaseAvailable;
    }
    else if (game.RuntimeReleaseAvailable)
    {
        GitHubDownloadButton.Content =
            IsEnglish ? "⬇ Download game" : "⬇ Télécharger le jeu";
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
                ? (IsEnglish ? "Update repository" : "Mettre à jour le dépôt")
                : (IsEnglish ? "✓ Repository present" : "✓ Dépôt présent")
            : (IsEnglish ? "Download repository" : "Télécharger le dépôt");

    GitHubSourceButton.IsEnabled =
        game.GitHubConfigured &&
        (!game.GitHubLocalRepositoryPresent || game.GitHubUpdateAvailable);
}

    private void GameCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameDefinition game })
            SelectGame(game);
    }

    private void LibraryNav_Click(object sender, RoutedEventArgs e)
        => ShowLibrary();

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

private async void DeleteRepositoryButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

    ApplyLocalRepositoryStatus(
        game,
        _github.DetectLocalRepository(game));

    if (!game.GitHubLocalRepositoryPresent)
    {
        UpdateSelectedGameGitHubPanel();
        return;
    }

    if (!game.GitHubLocalRepositoryManaged)
    {
        MessageBox.Show(
            IsEnglish
                ? "This repository was not downloaded by CubeShelf. CubeShelf will not delete an external/manual repository."
                : "Ce dépôt n'a pas été téléchargé par CubeShelf. CubeShelf ne supprimera pas un dépôt externe ou manuel.",
            "CubeShelf",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }

    var answer = MessageBox.Show(
        IsEnglish
            ? "Delete the repository downloaded by CubeShelf? Your ISO/RVZ and mods are kept."
            : "Supprimer le dépôt téléchargé par CubeShelf ? Ton ISO/RVZ et tes mods seront conservés.",
        "CubeShelf",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    if (answer != MessageBoxResult.Yes)
        return;

    _github.DeleteDownloadedSource(game);
    game.GitHubSourcePath = "";

    ApplyLocalRepositoryStatus(
        game,
        _github.DetectLocalRepository(game));

    await RefreshGameUpdateStatusAsync(game);
    UpdateSelectedGameGitHubPanel();
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

    
private void PlayButton_Click(
    object sender,
    RoutedEventArgs e)
{
    if (_selectedGame is null)
        return;

    var game = _selectedGame;

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

    var state = _runtimeInstaller.ReadState(game) ??
                _runtimeInstaller.AdoptConfiguredExecutable(game);

    var ready =
        state is not null &&
        state.GameDataReady &&
        Directory.Exists(Path.Combine(
            Path.GetDirectoryName(state.ExecutablePath)!,
            game.Id,
            "files"));

    if (!ready)
    {
        QueueGameDataPreparation(game, launchWhenReady: true);
        ShowDownloads();
        return;
    }

    try
    {
        _gameLauncher = new GameLauncher(game, _modManager);
        _gameLauncher.StartGame();
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message, "CubeShelf");
    }
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
        GameDefinition game,
        bool launchWhenReady = false)
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
                    discImagePath: discPath);
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
                }

                if (launchWhenReady)
                {
                    try
                    {
                        _gameLauncher = new GameLauncher(game, _modManager);
                        _gameLauncher.StartGame();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "CubeShelf",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }

                return Task.CompletedTask;
            });

        if (!added && launchWhenReady)
        {
            MessageBox.Show(
                IsEnglish
                    ? "Game data preparation is already running."
                    : "La préparation des données du jeu est déjà en cours.",
                "CubeShelf");
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
    }

    private async Task CheckLauncherUpdateAsync(
        bool showUpToDate)
    {
        try
        {
            var info = await _updates.CheckAsync();

            if (!info.UpdateAvailable)
            {
                if (showUpToDate)
                {
                    MessageBox.Show(
                        info.Message,
                        "CubeShelf Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var answer = MessageBox.Show(
                $"CubeShelf {info.LatestVersion} est disponible.\n\n" +
                $"Version actuelle : {info.CurrentVersion}\n\n" +
                "Installer maintenant ?",
                "Mise à jour CubeShelf",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                await _updates.PrepareAndLaunchUpdateAsync(info);
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            if (showUpToDate)
            {
                MessageBox.Show(
                    ex.Message,
                    "CubeShelf Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
