using Avalonia.Controls;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Library;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Games;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using Avalonia.Styling;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly IProcessLauncher _processLauncher = new ProcessLauncher();
    private readonly GameProfileStore _profileStore;
    private readonly PartyBoardInstaller _installer;
    private readonly GameDataPreparer _gameData;
    private readonly DownloadActivityStore _downloads;
    private readonly IPlatformPaths _paths;
    private readonly MediaCacheService _mediaCache;
    private readonly UserPreferencesStore _preferencesStore;
    private readonly LauncherUpdateService _launcherUpdates;
    private readonly GameCatalogService _catalogService;
    private readonly List<GameCatalogEntry> _catalogGames = new();
    private readonly GameSessionTracker _sessions;
    private UserPreferences _preferences;
    private bool _loadingSettings;
    private GameCatalogEntry? _selectedGame;
    private PortableModManager? _modManager;
    private readonly GameBananaClient _gameBanana = new();
    private IReadOnlyList<GameBananaMod> _remoteMods = Array.Empty<GameBananaMod>();
    private DesktopModItem? _selectedMod;
    private Bitmap? _modPreviewBitmap;

    public MainWindow()
    {
        InitializeComponent();
        var paths = new PlatformPaths();
        _paths = paths;
        string? restoredProfileMessage = null;
        var profileBackups = new CubeShelf.Core.Storage.ProfileBackupService(paths);
        try
        {
            if (profileBackups.ApplyPendingRestore()) restoredProfileMessage = "Profil restauré. / Profile restored.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or AggregateException)
        {
            restoredProfileMessage = "Restauration du profil annulée / Profile restore cancelled: " + error.Message;
            try { profileBackups.DiscardPendingRestore(); } catch (Exception discardError) when (discardError is IOException or UnauthorizedAccessException) { }
        }
        _mediaCache = new MediaCacheService(paths);
        var migration = new LegacyDataMigrator(paths, AppContext.BaseDirectory).Run();
        _preferencesStore = new UserPreferencesStore(paths);
        _launcherUpdates = new LauncherUpdateService(paths);

        // If we are the first run after an update, the updater left its old image beside
        // us; it could not delete what it was running from. We can.
        LauncherUpdateService.RemoveDisplacedUpdaterImages();
        _preferences = _preferencesStore.Load();
        ApplyPreferences();
        _profileStore = new GameProfileStore(paths.ConfigurationDirectory);
        _installer = new PartyBoardInstaller(paths);
        _gameData = new GameDataPreparer(paths);
        _downloads = new DownloadActivityStore(paths);
        _downloads.MarkInterruptedOperations();
        _sessions = new GameSessionTracker(_processLauncher);
        _sessions.Ended += session => Dispatcher.UIThread.Post(() => OnGameSessionEnded(session));
        InitializePhase3Parity();
        InitializePhase4Parity();
        InitializePhase5Polish();
        InitializePhase6Parity();
        InitializePhase7Finalization();
        InitializeBigPicture();
        RefreshMaintenanceLabels();
        PlatformStatus.Text = OperatingSystem.IsWindows()
            ? "Plateforme détectée : Windows"
            : OperatingSystem.IsLinux()
                ? "Plateforme détectée : Linux"
                : "Plateforme détectée : macOS (expérimental)";
        DataPath.Text = $"Profil : {paths.DataDirectory}";
        SettingsDataPath.Text = paths.DataDirectory;
        if (restoredProfileMessage is not null) SettingsStatus.Text = restoredProfileMessage;
        if (!migration.AlreadyCompleted && (migration.RuntimesCopied > 0 || migration.ModLibrariesCopied > 0))
            SettingsStatus.Text = $"Migration terminée : {migration.RuntimesCopied} runtime(s), {migration.ModLibrariesCopied} bibliothèque(s) de mods copiés. Sources conservées.";

        _catalogService = new GameCatalogService(
            Path.Combine(AppContext.BaseDirectory, "games.json"),
            paths.DataDirectory);
        _catalogGames.AddRange(_catalogService.Load());
        _selectedGame = ShelfGames.FirstOrDefault() is { } firstGame
            ? GameVariants.Resolve(_catalogGames, firstGame, paths)
            : null;
        if (_selectedGame is not null)
        {
            var saved = _profileStore.Load(_selectedGame.Id);
            if (saved is not null)
            {
                _selectedGame.Executable = saved.Executable;
                _selectedGame.DiscImage = saved.DiscImage;
            }

            var managedRuntime = _installer.GetStatus(_selectedGame.Id);
            if (managedRuntime.IsInstalled)
                _selectedGame.Executable = managedRuntime.ExecutablePath;

            _selectedGame.Executable = ResolveApplicationPath(_selectedGame.Executable);
            _selectedGame.DiscImage = ResolveApplicationPath(_selectedGame.DiscImage);
            GameTitle.Text = GameVariants.BaseOf(_catalogGames, _selectedGame).Title;
            _modManager = new PortableModManager(paths, _selectedGame.ModLibraryId);
            RefreshGameState();
            RefreshModItems();
        }

        // After the catalog is loaded, so the first document published is not an empty shelf.
        InitializeFriends();
        InitializeAchievements();
        InitializeSocial();
        // And again here, because the pinned-game list is the shelf: ApplyPreferences ran before
        // the catalog existed and could only offer "None".
        ApplyProfilePreferences();
        // The handle needs the key, and the key exists only once InitializeFriends has run.
        RefreshIdentityUi();
        ShowRunningVersion();
        // Feeds the F1 Friends tab inside Mario Party 4, only while the game runs.
        StartInGameBridge();
        StartOnlineWatch();
        // Near the clock, so closing the window does not mean leaving every friend.
        InitializeTray();
        // Copy a friend's message in any chat app, come back to the Friends page: it is found.
        Activated += (_, _) =>
        {
            if (FriendsView.IsVisible) _ = CheckClipboardForFriendCodeAsync();
        };
        RefreshDownloadItems();
        Closing += (_, closing) =>
        {
            if (ShouldHideInsteadOfClosing(closing))
            {
                closing.Cancel = true;
                HideToTray();
                return;
            }

            _preferences = _preferences with
            {
                WindowWidth = _bigPictureActive ? _bigPicturePreviousWidth : Width,
                WindowHeight = _bigPictureActive ? _bigPicturePreviousHeight : Height
            };
            _preferencesStore.Save(_preferences);
            // Before the tracker is disposed: the farewell document has to still know whether
            // a game is running, or a friend is left looking at a stale "in a game".
            StopInGameBridge();
            DisposeSocial();
            DisposeFriends();
            _sessions.Dispose();
            DisposeQuickPlay();
            DisposePhase3Parity();
            DisposePhase4Parity();
            DisposePhase5Polish();
            DisposePhase6Parity();
            DisposePhase7Finalization();
            GameCase.Dispose();
            foreach (var card in _parityCards) card.Dispose();
            _toastCancellation?.Cancel();
            _toastCancellation?.Dispose();
            DisposeTray();
        };
    }

    private async void PlayGame(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is { } game) await RequestPlayAsync(game);
    }

    private void LaunchReadyGame(GameCatalogEntry game)
    {
        var launch = GameLaunchConfiguration.Create(game,
            game.LaunchEnvironment.Values.Any(value => value.Contains("{mods}", StringComparison.Ordinal))
                ? new PortableModManager(_paths, game.ModLibraryId).PrepareActiveList() : null);
        var environment = launch.Environment;
        // Tells the game where its F1 Friends tab can reach CubeShelf. Written first, so the
        // tab exists, with its name in the player's language, from the first frame.
        foreach (var pair in InGameEnvironment(game)) environment[pair.Key] = pair.Value;
        // One RetroAchievements login for every game that can take it.
        AddRetroAchievementsEnvironment(game, environment);
        if (environment.ContainsKey(CubeShelf.Core.Social.InGameBridge.DirectoryVariable)) WriteInGameState();

        _sessions.Start(
            game.Id,
            game.Executable,
            Path.GetDirectoryName(game.Executable) ?? AppContext.BaseDirectory,
            environment, launch.Arguments,
            game.LogOutput ? Path.Combine(_paths.DataDirectory, "Logs", game.Id + ".log") : null);

        game.PlayCount++;
        game.LastPlayedAt = DateTimeOffset.Now;
        PersistCatalog();
        WindowState = WindowState.Minimized;
        ActionStatus.Text = P7($"{game.RuntimeName} démarré avec l’image sélectionnée.",
            $"{game.RuntimeName} started with the selected disc image.");
        if (_selectedGame?.Id == game.Id) RefreshGameState();
        else RefreshLibraryParity();
    }

    private async void ChooseDisc(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _selectedGame.DiscPatch is { SourceDescription.Length: > 0 } patch
                ? $"{_selectedGame.Title} : sélectionner {patch.SourceDescription}"
                : $"Sélectionner l’image de {_selectedGame.Title}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images GameCube") { Patterns = _selectedGame.DiscFormats.Select(format => "*" + format).ToArray() }
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        var compatibility = DiscImageService.Inspect(path, _selectedGame, UiLocalization.IsEnglish(_preferences.Language));
        if (!compatibility.Supported)
        {
            ActionStatus.Text = compatibility.Message;
            return;
        }

        _selectedGame.DiscImage = Path.GetFullPath(path);
        PersistSelection();
        RefreshGameState();
        ActionStatus.Text = $"Image enregistrée : {Path.GetFileName(path)}";
    }

    private async void ChooseExecutable(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Sélectionner l’exécutable PartyBoard",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PartyBoard")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? new[] { "*.exe" }
                        : new[] { "*" }
                }
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        _selectedGame.Executable = Path.GetFullPath(path);
        PersistSelection();
        RefreshGameState();
        ActionStatus.Text = $"Runtime enregistré : {Path.GetFileName(path)}";
    }

    private async void InstallPartyBoard(object? sender, RoutedEventArgs args)
        => await RunInstallationAsync(repair: false);

    private async void RepairPartyBoard(object? sender, RoutedEventArgs args)
        => await RunInstallationAsync(repair: true);

    private Task RunInstallationAsync(bool repair)
    {
        EnqueueRuntimeInstallationPhase3(repair);
        return Task.CompletedTask;
    }
    private void CancelInstall(object? sender, RoutedEventArgs args)
    {
        CancelActiveGameQueuePhase3();
        ActionStatus.Text = "Annulation demandée…";
    }

    private void ClearDisc(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        _selectedGame.DiscImage = "";
        PersistSelection();
        RefreshGameState();
        ActionStatus.Text = "Le lien vers l’image a été retiré. Le fichier original n’a pas été supprimé.";
    }

    private void PersistSelection()
    {
        if (_selectedGame is null) return;
        _profileStore.Save(new GameProfileEntry(
            _selectedGame.Id,
            _selectedGame.Executable,
            _selectedGame.DiscImage));
        PersistCatalog();
    }

    private void PersistCatalog() => _catalogService.Save(_catalogGames);

    /// <summary>The library's games: variants (Super Mario Eclipse) are reached through their base game.</summary>
    private IEnumerable<GameCatalogEntry> ShelfGames => _catalogGames.Where(game => !GameVariants.IsVariant(game));

    /// <summary>
    /// After the selected game's mods change: Super Mario Sunshine becomes Super Mario Eclipse
    /// while the Eclipse mod is active, and back when it is not.
    /// </summary>
    private void SyncVariantSelection()
    {
        if (_selectedGame is null) return;
        var effective = GameVariants.Resolve(_catalogGames, _selectedGame, _paths);
        if (!ReferenceEquals(effective, _selectedGame)) SelectCatalogGame(effective, showGameView: false);
    }

    private void ToggleFavorite(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        _selectedGame.IsFavorite = !_selectedGame.IsFavorite;
        PersistCatalog();
        RefreshGameState();
    }

    private void OnGameSessionEnded(GameSessionEndedEventArgs session)
    {
        var game = _catalogGames.FirstOrDefault(item =>
            item.Id.Equals(session.GameId, StringComparison.OrdinalIgnoreCase));
        if (game is not null)
        {
            game.TotalPlaySeconds += Math.Max(0, (long)Math.Round(session.Duration.TotalSeconds));
            PersistCatalog();
        }

        if (_selectedGame?.Id.Equals(session.GameId, StringComparison.OrdinalIgnoreCase) == true)
            RefreshGameState();
        else
            RefreshQuickPlayUi();

        if (_sessions.RunningGameIds.Count == 0)
        {
            RefreshBigPictureResume();
            if (WindowState == WindowState.Minimized) WindowState = _bigPictureActive ? WindowState.FullScreen : WindowState.Normal;
            Show();
            Activate();
        }
        ShowLaunchRecovery(session);
    }

    private static string FormatPlayTime(long seconds)
    {
        var total = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return total.TotalHours >= 1
            ? $"{(int)total.TotalHours} h {total.Minutes:00} min"
            : $"{total.Minutes} min";
    }

    private void RefreshGameState()
    {
        if (_selectedGame is null) return;
        var game = _selectedGame;
        RefreshGameModsPanel();

        // A runtime that recompiles the game from the disc itself owns that step. CubeShelf
        // extracting the disc underneath it would be wrong, so none of the preparation UI
        // applies to those games.
        var runtimeManagesDisc = game.DataPreparation == GameDataPreparation.Runtime;

        DiscPathText.Text = File.Exists(game.DiscImage)
            ? game.DiscImage
            : "Aucune image sélectionnée";
        ExecutablePathText.Text = File.Exists(game.Executable)
            ? game.Executable
            : $"{game.RuntimeName} non configuré";

        var runtime = _installer.GetStatus(game.Id);
        RuntimeSectionLabel.Text = game.RuntimeName.ToUpperInvariant();
        InstallButton.Content = runtime.IsInstalled ? "Mettre à jour" : $"Installer {game.RuntimeName}";
        RepairButton.IsVisible = runtime.IsInstalled || runtime.NeedsRepair;
        UninstallButton.IsVisible = runtime.IsInstalled || runtime.NeedsRepair;
        if (runtime.IsInstalled)
        {
            ExecutablePathText.Text = $"{runtime.ExecutablePath}\nVersion : {runtime.Version}";
            if (!runtime.Verified)
                ExecutablePathText.Text += "\n⚠ Installé sans vérification de checksum";
        }
        else if (runtime.NeedsRepair)
        {
            ExecutablePathText.Text = "Installation gérée incomplète — réparation nécessaire";
        }

        var compatibility = DiscImageService.Inspect(game.DiscImage, game, UiLocalization.IsEnglish(_preferences.Language));
        DiscCompatibilityText.Text = compatibility.Message + Environment.NewLine +
            DiscImageService.DescribeSupported(game.SupportedDiscIds);

        var prepared = !runtimeManagesDisc && _gameData.IsPrepared(game, game.Executable);
        PrepareDataButton.IsVisible = !runtimeManagesDisc;
        PrepareDataButton.Content = prepared ? "Repréparer les données" : "Préparer les données";
        PrepareDataButton.IsEnabled = !runtimeManagesDisc && File.Exists(game.Executable) && compatibility.Supported;
        if (prepared)
            ExecutablePathText.Text += "\nDonnées du jeu : prêtes";
        else if (runtimeManagesDisc && runtime.IsInstalled)
            ExecutablePathText.Text += $"\n{game.RuntimeName} se charge du disque lui-même ; CubeShelf n’a rien à préparer.";

        var running = _sessions.IsRunning(game.Id);
        PlayButton.IsEnabled = PlayIntentFor(game) is null;
        PlayButton.Content = running ? "■ Arrêter" : "▶ Jouer";
        FavoriteButton.Content = game.IsFavorite ? "★ Favori" : "☆ Ajouter aux favoris";
        var lastPlayed = game.LastPlayedAt is null
            ? "Jamais"
            : game.LastPlayedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        GameStatsText.Text =
            $"{game.PlayCount} lancements • {FormatPlayTime(game.TotalPlaySeconds)} • Dernier : {lastPlayed}";
        ActionStatus.Text = QuickPlayHint(game);

        RefreshParityGameDetails();
        RefreshLibraryParity();
        RefreshQuickPlayUi();
    }

    private bool CanPlay(GameCatalogEntry? targetGame = null)
    {
        var game = targetGame ?? _selectedGame;
        if (game is null || !File.Exists(game.Executable)) return false;
        if (!DiscImageService.Inspect(game.DiscImage, game).Supported) return false;

        // A runtime-managed disc has nothing for CubeShelf to prepare and nothing to wait for
        // before enabling Play: Ring Out extracts and recompiles on its own first launch, and
        // PartyBoard opens the image directly from PARTYBOARD_DISC_IMAGE.
        return game.DataPreparation != GameDataPreparation.DiscPatch ||
            (game.DiscPatch is { } patch &&
             DiscPatchSource.FindDelta(_paths, game.ModLibraryId, patch) is not null);
    }

    private static string ResolveApplicationPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? ""
            : Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    private void ShowMods(object? sender, RoutedEventArgs args)
    {
        ShowParityView(ModsView);
        RefreshModItems();
        if (_preferences.RefreshModsOnOpen && _remoteMods.Count == 0) RefreshMods(sender, args);
    }

    private async void RefreshMods(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        RefreshModsButton.IsEnabled = false;
        ModsStatus.Text = "Chargement du catalogue GameBanana…";
        try
        {
            var search = ModSearchBox.Text?.Trim() ?? "";
            _remoteMods = await _gameBanana.BrowseAsync(_selectedGame.GameBananaGameId, search);
            RefreshModItems();
            ModsStatus.Text = search.Length > 0
                ? $"{_remoteMods.Count} mod(s) GameBanana pour « {search} »."
                : $"{_remoteMods.Count} mods récupérés.";
        }
        catch (Exception exception)
        {
            ModsStatus.Text = $"Catalogue indisponible : {exception.Message}";
        }
        finally { RefreshModsButton.IsEnabled = true; }
    }

    private void RefreshModItems()
    {
        if (_modManager is null) return;
        var installed = _modManager.GetInstalled().ToDictionary(item => item.Id);
        var ids = _remoteMods.Select(item => item.Id).Concat(installed.Keys).Distinct().ToArray();
        var conflicts = _modManager.AnalyzeConflicts();
        var conflictedIds = conflicts.SelectMany(item => item.ModIds).ToHashSet();
        // A mod that makes the game another (Super Mario Eclipse) carries a disc patch, which
        // is no layout problem: activating it switches the game over.
        var variantMods = _selectedGame is null ? new HashSet<int>() : _catalogGames
            .Where(game => GameVariants.IsVariant(game) &&
                           game.VariantOf.Equals(_selectedGame.ModLibraryId, StringComparison.OrdinalIgnoreCase))
            .Select(game => game.ActivatedByModId).ToHashSet();
        var layout = _modManager.AnalyzeLayout()
            .Where(item => !(item.Kind == PortableModLayoutKind.DiscPatch && variantMods.Contains(item.Id))).ToList();
        var layoutIds = layout.Select(item => item.Id).ToHashSet();
        var playerDisabled = _modManager.GetPlayerDisabled().ToHashSet();
        var runtime = _selectedGame?.RuntimeName is { Length: > 0 } runtimeName ? runtimeName : "le jeu";
        var items = ids.Select(id =>
        {
            var remote = _modDetails.TryGetValue(id, out var detailed) ? detailed : _remoteMods.FirstOrDefault(item => item.Id == id);
            installed.TryGetValue(id, out var local);
            var status = local is null ? "Non installé"
                : playerDisabled.Contains(id) ? $"Installé • coupé dans {runtime}"
                : local.Enabled ? "Installé • activé" : "Installé • désactivé";
            if (conflictedIds.Contains(id)) status += " • conflit";
            if (layout.FirstOrDefault(item => item.Id == id) is { } problem)
                status += problem.Kind is PortableModLayoutKind.DiscPatch or PortableModLayoutKind.DolphinTextures
                    ? " • incompatible avec " + runtime : " • arborescence inattendue";
            return new DesktopModItem(id, remote?.Name ?? local?.Name ?? $"Mod {id}", status,
                local?.Priority ?? 100, local?.Enabled ?? false, local is not null, remote);
        }).ToArray();
        ShowModItems(items);
        var report = new List<string>();
        foreach (var item in layout.Take(4))
            report.Add("⚠ " + item.Name + " : " + item.Kind switch
            {
                PortableModLayoutKind.DolphinTextures =>
                    $"pack de textures Dolphin. {runtime} remplace des fichiers du disque, pas des textures au rendu : ce mod ne peut pas fonctionner ici.",
                PortableModLayoutKind.LooseFiles =>
                    $"fichiers livrés sans chemin. {runtime} les placera lui-même si le disque porte ces noms ; sinon ils resteront sans effet.",
                PortableModLayoutKind.DiscPatch =>
                    $"patch de l’image disque (xdelta, BPS, IPS…). Il fabrique un autre disque, alors que {runtime} applique des fichiers par-dessus le tien : ce mod ne peut pas fonctionner ici.",
                PortableModLayoutKind.SeveralVariants =>
                    $"plusieurs variantes côte à côte ({string.Join(", ", item.TopLevelEntries.Take(3))}). Garde-en une seule à la racine du mod.",
                _ => $"aucun dossier du disque à sa racine ({string.Join(", ", item.TopLevelEntries.Take(3))}), donc sans effet en jeu.",
            });
        if (_selectedGame is not null && GameVariants.IsVariant(_selectedGame))
            report.Add($"▶ {_selectedGame.Title} : son mod est actif, « Jouer » lance {_selectedGame.Title}. Désactive-le pour revenir à {GameVariants.BaseOf(_catalogGames, _selectedGame).Title}.");
        report.Add(conflicts.Count == 0
            ? "Aucun conflit entre les mods actifs."
            : $"⚠ {conflicts.Count} fichier(s) en conflit : " + string.Join(", ", conflicts.Take(5).Select(item => item.RelativePath)));
        ConflictsStatus.Text = string.Join(Environment.NewLine, report);
    }

    private async void SelectMod(object? sender, SelectionChangedEventArgs args)
    {
        _selectedMod = ModsList.SelectedItem as DesktopModItem;
        if (_selectedMod is null) return;
        ShowSelectedMod();
        if (_selectedMod.Remote is not { IsSummary: true }) return;
        var item = _selectedMod;
        InstallModButton.IsEnabled = false;
        ModDescription.Text = "Chargement de la page du mod…";
        try
        {
            var full = await EnsureModDetailsAsync(item);
            if (_selectedMod?.Id != item.Id || full is null) return;
            _selectedMod = item with { Remote = full };
            ShowSelectedMod();
        }
        catch (Exception exception)
        {
            if (_selectedMod?.Id == item.Id) ModDescription.Text = $"Page GameBanana indisponible : {exception.Message}";
        }
    }

    private void ShowSelectedMod()
    {
        if (_selectedMod is null) return;
        ModTitle.Text = _selectedMod.Name;
        ModDetailStatus.Text = _selectedMod.Status +
            (!_selectedMod.Installed && LooksLikeDiscPatch(_selectedMod.Remote)
                ? Environment.NewLine + "⚠ Ce mod semble être un patch du disque ou du code (xdelta, BPS, IPS, codes Gecko…) : " +
                  "le port natif applique seulement des fichiers par-dessus ton disque, il ne fonctionnera probablement pas."
                : "");
        ModVersion.Text = _selectedMod.Remote?.VersionLabel ?? "Version locale";
        ModDescription.Text = _selectedMod.Remote?.Description ?? "Métadonnées distantes indisponibles.";
        ModInstructions.Text = string.IsNullOrWhiteSpace(_selectedMod.Remote?.InstallInstructions)
            ? "" : "Instructions : " + _selectedMod.Remote.InstallInstructions;
        OpenModPageButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedMod.Remote?.ProfileUrl);
        OpenModImageButton.IsEnabled = (_selectedMod.Remote?.ImageUrls?.Count ?? 0) > 0;
        ModPriority.Text = _selectedMod.Priority.ToString();
        InstallModButton.IsEnabled = _selectedMod.Remote is { IsSummary: false };
        InstallModButton.Content = _selectedMod.Installed ? "Réinstaller" : "Installer";
        ToggleModButton.IsEnabled = _selectedMod.Installed;
        ToggleModButton.Content = _selectedMod.Enabled ? "Désactiver" : "Activer";
        DeleteModButton.IsEnabled = _selectedMod.Installed;
        ModPreview.Source = null;
        _modPreviewBitmap?.Dispose();
        _modPreviewBitmap = null;
    }

    private async void InstallMod(object? sender, RoutedEventArgs args)
        => await RunModInstallationAsync();

    private async Task RunModInstallationAsync()
    {
        await QueueModInstallationPhase3Async();
    }
    private void ToggleMod(object? sender, RoutedEventArgs args)
    {
        if (_selectedMod is null || _modManager is null) return;
        _modManager.SetEnabled(_selectedMod.Id, !_selectedMod.Enabled);
        RefreshModItems();
        SyncVariantSelection();
    }

    private void DeleteMod(object? sender, RoutedEventArgs args)
    {
        if (_selectedMod is null || _modManager is null) return;
        _modManager.Uninstall(_selectedMod.Id);
        ModsStatus.Text = $"{_selectedMod.Name} supprimé.";
        RefreshModItems();
        SyncVariantSelection();
    }

    private void ApplyModPriority(object? sender, RoutedEventArgs args)
    {
        if (_selectedMod is null || _modManager is null || !int.TryParse(ModPriority.Text, out var priority)) return;
        _modManager.SetPriority(_selectedMod.Id, priority);
        ModsStatus.Text = "Priorité enregistrée.";
        RefreshModItems();
    }

    private void SetModActions(bool enabled)
    {
        RefreshModsButton.IsEnabled = enabled;
        InstallModButton.IsEnabled = enabled && _selectedMod?.Remote is { IsSummary: false };
        ToggleModButton.IsEnabled = enabled && _selectedMod?.Installed == true;
        DeleteModButton.IsEnabled = enabled && _selectedMod?.Installed == true;
    }

    private void ShowDownloads(object? sender, RoutedEventArgs args)
    {
        ShowParityView(DownloadsView);
        RefreshDownloadItems();
    }


    private void RefreshDownloadItems() => RefreshPortableQueueView();



    public sealed record DesktopModItem(int Id, string Name, string Status, int Priority, bool Enabled,
        bool Installed, GameBananaMod? Remote) : System.ComponentModel.INotifyPropertyChanged
    {
        public string Category => Remote?.Category is { Length: > 0 } category ? category : "Mod";
        public string Byline => string.Join("  •  ", new[]
        {
            Remote?.Category,
            Remote?.Author is { Length: > 0 } author ? "par " + author : null,
            Remote?.Views > 0 ? $"{Remote.Views:N0} vues" : null
        }.Where(part => !string.IsNullOrEmpty(part)));
        public string Badge => Enabled ? "ACTIVÉ" : "INSTALLÉ";

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        // Screen readers and UI Automation name a tile by its mod.
        public override string ToString() => Name;
    }

    /// <summary>One row of the game page's mod list: what it is, and the one thing to do with it.</summary>
    public sealed record GameModRow(int Id, string Name, string Detail, string Action);

    /// <summary>
    /// The installed mods, on the game's own page. Reaching them used to mean
    /// leaving for the mod manager, which is where you go to find and install a
    /// mod - not to decide what this launch will load.
    /// </summary>
    private void RefreshGameModsPanel()
    {
        if (_modManager is null)
        {
            GameModsList.ItemsSource = null;
            GameModsEmpty.IsVisible = true;
            GameModsWarning.IsVisible = false;
            return;
        }

        var playerDisabled = _modManager.GetPlayerDisabled().ToHashSet();
        var unreachable = _modManager.AnalyzeLayout().Select(item => item.Id).ToHashSet();

        var rows = _modManager.GetInstalled()
            .OrderByDescending(item => item.Priority).ThenBy(item => item.Name)
            .Select(item =>
            {
                var off = playerDisabled.Contains(item.Id);
                var detail = off ? P7("Coupé dans PartyBoard", "Switched off in PartyBoard")
                    : item.Enabled ? P7($"Actif • priorité {item.Priority}", $"Active • priority {item.Priority}")
                    : P7("Désactivé", "Disabled");
                if (unreachable.Contains(item.Id))
                    detail += P7(" • n’atteint pas le jeu", " • cannot reach the game");
                return new GameModRow(item.Id, item.Name, detail,
                    item.Enabled && !off ? P7("Désactiver", "Disable") : P7("Activer", "Enable"));
            }).ToArray();

        GameModsList.ItemsSource = rows;
        GameModsEmpty.IsVisible = rows.Length == 0;

        var conflicts = _modManager.AnalyzeConflicts();
        GameModsWarning.IsVisible = conflicts.Count > 0;
        if (conflicts.Count > 0)
            GameModsWarning.Text = P7(
                $"⚠ {conflicts.Count} fichier(s) revendiqué(s) par plusieurs mods actifs : le plus prioritaire gagne.",
                $"⚠ {conflicts.Count} file(s) claimed by more than one active mod: the highest priority wins.");
    }

    // One button, one meaning: a mod switched off in game is off, whichever of
    // the two switches did it, and this puts it back on.
    private void ToggleModFromGamePage(object? sender, RoutedEventArgs args)
    {
        if (_modManager is null || (sender as Control)?.Tag is not int id) return;
        var mod = _modManager.GetInstalled().FirstOrDefault(item => item.Id == id);
        if (mod is null) return;

        var running = mod.Enabled && !_modManager.GetPlayerDisabled().Contains(id);
        _modManager.SetEnabled(id, !running);
        RefreshGameModsPanel();
        RefreshModItems();
        SyncVariantSelection();
    }

    private async Task<bool> ConfirmModPlanAsync(ModInstallPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var details = string.Join("\n", plan.Packages.Select(item => $"• {item.Mod.Name} (#{item.Mod.Id})"));
        if (plan.Warnings.Count > 0) details += "\n\nAvertissements :\n" + string.Join("\n", plan.Warnings.Select(item => "• " + item));
        if (plan.IncompatibleInstalledModIds.Count > 0)
            details += "\n\nMods actifs incompatibles : " + string.Join(", ", plan.IncompatibleInstalledModIds);
        var dialog = new Window { Title = "Confirmer l’installation", Width = 560, Height = 430 };
        dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = plan.Summary, FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new ScrollViewer { Content = new TextBlock { Text = details, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            CreateDialogButton("Installer", (_, _) => dialog.Close(true)),
                            CreateDialogButton("Annuler", (_, _) => dialog.Close(false))
                        }
                    }
                }
            };
        var confirmation = dialog.ShowDialog<bool>(this);
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await confirmation;
    }

    private static Button CreateDialogButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text };
        button.Click += click;
        return button;
    }

    private async void OpenModImage(object? sender, RoutedEventArgs args)
    {
        var url = _selectedMod?.Remote?.ImageUrls?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var path = await _mediaCache.GetAsync(url, false);
            var bitmap = new Bitmap(path);
            var window = new Window { Title = _selectedMod?.Name ?? "Image", Width = 1000, Height = 720,
                Content = new Image { Source = bitmap, Stretch = Avalonia.Media.Stretch.Uniform } };
            window.Closed += (_, _) => bitmap.Dispose();
            await window.ShowDialog(this);
        }
        catch (Exception exception) { ModsStatus.Text = $"Image indisponible : {exception.Message}"; }
    }

    private void OpenModPage(object? sender, RoutedEventArgs args)
    {
        var url = _selectedMod?.Remote?.ProfileUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void ShowSettings(object? sender, RoutedEventArgs args)
    {
        ShowParityView(SettingsView);
    }

    private void OpenDataDirectory(object? sender, RoutedEventArgs args)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        Process.Start(new ProcessStartInfo(_paths.DataDirectory) { UseShellExecute = true });
    }


    private void ApplyPreferences()
    {
        _loadingSettings = true;
        if (_portableQueue is not null)
        {
            _portableQueue.BandwidthLimitBytesPerSecond = _preferences.DownloadBandwidthLimit;
            DownloadManager.Refresh();
        }
        LanguagePicker.SelectedIndex = _preferences.Language.ToLowerInvariant() switch { "fr" => 1, "en" => 2, _ => 0 };
        ThemePicker.SelectedIndex = _preferences.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CheckUpdatesBox.IsChecked = _preferences.CheckGamesOnStartup;
        UpdatePopupBox.IsChecked = _preferences.ShowGameUpdatePopup;
        RefreshModsBox.IsChecked = _preferences.RefreshModsOnOpen;
        ApplyPresencePreferences();
        if (_preferences.WindowWidth >= MinWidth) Width = _preferences.WindowWidth;
        if (_preferences.WindowHeight >= MinHeight) Height = _preferences.WindowHeight;
        if (Avalonia.Application.Current is { } application)
            application.RequestedThemeVariant = _preferences.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        _loadingSettings = false;
    }

    private void SaveSettings(object? sender, RoutedEventArgs args)
    {
        if (_loadingSettings) return;
        var language = (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? UiLocalization.Automatic;
        var theme = (ThemePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "dark";
        _preferences = _preferences with
        {
            Language = language,
            Theme = theme,
            CheckGamesOnStartup = CheckUpdatesBox.IsChecked == true,
            ShowGameUpdatePopup = UpdatePopupBox.IsChecked == true,
            RefreshModsOnOpen = RefreshModsBox.IsChecked == true
        };
        SavePresencePreferences();
        _preferencesStore.Save(_preferences);
        if (Avalonia.Application.Current is { } application)
            application.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        SettingsStatus.Text = UiLocalization.IsEnglish(language) ? "Settings saved." : "Paramètres enregistrés.";
    }

    /// <summary>
    /// Defaults everywhere except the friends settings, which a reset keeps -- every one of them,
    /// listed in one place so a new one cannot be forgotten in a second.
    /// </summary>
    private static UserPreferences SocialPreferencesOnly(UserPreferences current) => new UserPreferences() with
    {
        FriendsDisplayName = current.FriendsDisplayName,
        PresenceFolder = current.PresenceFolder,
        PresenceUrl = current.PresenceUrl,
        PresencePublishEnabled = current.PresencePublishEnabled,
        ShareLibrary = current.ShareLibrary,
        SharePlayTime = current.SharePlayTime,
        ShareCurrentGame = current.ShareCurrentGame,
        ShareMods = current.ShareMods,
        ShareProfile = current.ShareProfile,
        ProfileStatus = current.ProfileStatus,
        ProfilePinnedGameId = current.ProfilePinnedGameId,
        ProfileFirstSeenAt = current.ProfileFirstSeenAt,
        PresenceVerifiedUrl = current.PresenceVerifiedUrl,
        LanVisible = current.LanVisible,
        CloseToTray = current.CloseToTray,
        NotifyFriendsOnline = current.NotifyFriendsOnline,
        TrayHintShown = current.TrayHintShown,
        Availability = current.Availability,
        AutoAway = current.AutoAway,
        PreviousPresenceFolder = current.PreviousPresenceFolder,
        PreviousPresenceUrl = current.PreviousPresenceUrl,
        IdentityBackedUpAt = current.IdentityBackedUpAt,
        ShareAchievements = current.ShareAchievements,
        MeshEnabled = current.MeshEnabled,
        MeshMapPort = current.MeshMapPort,
        MeshPort = current.MeshPort,
        MeshPublicDiscovery = current.MeshPublicDiscovery,
        MeshDiscoveryNoticeSeen = current.MeshDiscoveryNoticeSeen
    };

    private void ResetSettings(object? sender, RoutedEventArgs args)
    {
        _preferencesStore.Reset();

        // The friends settings survive a reset. Wiping the name and the address while
        // friends.json and identity.key stay on disk would leave the user with a friends list
        // they have silently stopped publishing to -- broken in the one direction nobody checks.
        _preferences = SocialPreferencesOnly(_preferences);

        ApplyPreferences();
        _preferencesStore.Save(_preferences);
        SettingsStatus.Text = "Paramètres réinitialisés. Ton identité et tes amis sont conservés.";
    }

    private async void CheckLauncherUpdate(object? sender, RoutedEventArgs args)
    {
        CheckLauncherUpdateButton.IsEnabled = false;
        SettingsStatus.Text = "Vérification de la dernière version…";
        try
        {
            var update = await _launcherUpdates.CheckAsync();
            if (!update.Available) { SettingsStatus.Text = $"CubeShelf {update.CurrentVersion} est à jour."; return; }
            SettingsStatus.Text = $"CubeShelf {update.LatestVersion} est disponible.";
            if (!OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("https://github.com/zeranemesis/CubeShelf-Launcher/releases/latest") { UseShellExecute = true });
                return;
            }
            var dialog = new Window { Title = "Mise à jour CubeShelf", Width = 480, Height = 220 };
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = $"Installer CubeShelf {update.LatestVersion} ?", FontSize = 21, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    CreateDialogButton("Télécharger et redémarrer", (_, _) => dialog.Close(true)),
                    CreateDialogButton("Plus tard", (_, _) => dialog.Close(false))
                }
            };
            if (await dialog.ShowDialog<bool>(this))
            {
                SettingsStatus.Text = "Téléchargement de la mise à jour…";
                var archive = await _launcherUpdates.DownloadAsync(update);
                _launcherUpdates.LaunchWindowsUpdater(archive, update.LatestVersion);
                Close();
            }
        }
        catch (Exception exception) { SettingsStatus.Text = $"Mise à jour indisponible : {exception.Message}"; }
        finally { CheckLauncherUpdateButton.IsEnabled = true; }
    }
}
