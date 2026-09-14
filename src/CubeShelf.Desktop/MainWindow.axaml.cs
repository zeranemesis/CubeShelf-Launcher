using Avalonia.Controls;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Library;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Downloads;
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
    private UserPreferences _preferences;
    private bool _loadingSettings;
    private CancellationTokenSource? _installationCancellation;
    private GameCatalogEntry? _selectedGame;
    private PortableModManager? _modManager;
    private readonly GameBananaClient _gameBanana = new();
    private IReadOnlyList<GameBananaMod> _remoteMods = Array.Empty<GameBananaMod>();
    private DesktopModItem? _selectedMod;
    private DownloadActivity? _selectedDownload;
    private Bitmap? _modPreviewBitmap;

    public MainWindow()
    {
        InitializeComponent();
        var paths = new PlatformPaths();
        _paths = paths;
        _mediaCache = new MediaCacheService(paths);
        var migration = new LegacyDataMigrator(paths, AppContext.BaseDirectory).Run();
        _preferencesStore = new UserPreferencesStore(paths);
        _launcherUpdates = new LauncherUpdateService(paths);
        _preferences = _preferencesStore.Load();
        ApplyPreferences();
        _profileStore = new GameProfileStore(paths.ConfigurationDirectory);
        _installer = new PartyBoardInstaller(paths);
        _gameData = new GameDataPreparer(paths);
        _downloads = new DownloadActivityStore(paths);
        _downloads.MarkInterruptedOperations();
        PlatformStatus.Text = OperatingSystem.IsWindows()
            ? "Plateforme détectée : Windows"
            : OperatingSystem.IsLinux()
                ? "Plateforme détectée : Linux"
                : "Plateforme détectée : macOS (non supportée)";
        DataPath.Text = $"Profil : {paths.DataDirectory}";
        SettingsDataPath.Text = paths.DataDirectory;
        if (!migration.AlreadyCompleted && (migration.RuntimesCopied > 0 || migration.ModLibrariesCopied > 0))
            SettingsStatus.Text = $"Migration terminée : {migration.RuntimesCopied} runtime(s), {migration.ModLibrariesCopied} bibliothèque(s) de mods copiés. Sources conservées.";

        var catalog = new GameCatalogService(
            Path.Combine(AppContext.BaseDirectory, "games.json"),
            paths.DataDirectory).Load();
        _selectedGame = catalog.FirstOrDefault();
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
            GameTitle.Text = $"{_selectedGame.Title} — PartyBoard";
            _modManager = new PortableModManager(paths, _selectedGame.Id);
            RefreshGameState();
            RefreshModItems();
        }
        RefreshDownloadItems();
        Closing += (_, _) =>
        {
            _preferences = _preferences with { WindowWidth = Width, WindowHeight = Height };
            _preferencesStore.Save(_preferences);
        };
    }

    private void PlayGame(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null || !CanPlay()) return;
        _processLauncher.Start(
            _selectedGame.Executable,
            Path.GetDirectoryName(_selectedGame.Executable) ?? AppContext.BaseDirectory,
            new Dictionary<string, string?>
            {
                ["PARTYBOARD_DISC_IMAGE"] = _selectedGame.DiscImage,
                ["PARTYBOARD_MOD_LIST"] = _modManager?.ActiveListFile
            });
        ActionStatus.Text = "PartyBoard démarré avec l’image sélectionnée.";
    }

    private async void ChooseDisc(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Sélectionner l’image de {_selectedGame.Title}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images GameCube") { Patterns = new[] { "*.iso", "*.gcm", "*.rvz" } }
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        var compatibility = DiscImageService.Inspect(path);
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

    private async Task RunInstallationAsync(bool repair)
    {
        if (_selectedGame is null || _installationCancellation is not null) return;
        var activity = _downloads.Create("runtime", $"PartyBoard — {_selectedGame.Title}", _selectedGame.Id);
        _downloads.Update(activity.Id, DownloadActivityState.Running, 0, repair ? "Réparation" : "Installation");
        RefreshDownloadItems();
        _installationCancellation = new CancellationTokenSource();
        InstallButton.IsEnabled = false;
        RepairButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        CancelInstallButton.IsVisible = true;
        InstallProgress.IsVisible = true;
        InstallProgress.Value = 0;
        ActionStatus.Text = "Téléchargement du manifeste PartyBoard…";

        var lastPersistedProgress = 0d;
        var progress = new Progress<double>(value =>
        {
            InstallProgress.Value = Math.Clamp(value * 100, 0, 100);
            ActionStatus.Text = value < 0.8
                ? $"Téléchargement sécurisé… {value:P0}"
                : "Vérification et installation…";
            if (value >= 1 || value - lastPersistedProgress >= .02)
            {
                lastPersistedProgress = value;
                _downloads.Update(activity.Id, DownloadActivityState.Running, value, ActionStatus.Text);
                RefreshDownloadItems();
            }
        });

        try
        {
            var result = repair
                ? await _installer.RepairLatestAsync(
                    _selectedGame.GitHubOwner, _selectedGame.GitHubRepo,
                    _selectedGame.GitHubReleaseTag, _selectedGame.Id,
                    progress, _installationCancellation.Token)
                : await _installer.InstallLatestAsync(
                    _selectedGame.GitHubOwner, _selectedGame.GitHubRepo,
                    _selectedGame.GitHubReleaseTag, _selectedGame.Id,
                    progress, _installationCancellation.Token);
            _selectedGame.Executable = result.ExecutablePath;
            PersistSelection();
            RefreshGameState();
            ActionStatus.Text = $"PartyBoard {result.Version} est installé et prêt.";
            _downloads.Update(activity.Id, DownloadActivityState.Completed, 1, ActionStatus.Text);
        }
        catch (OperationCanceledException)
        {
            ActionStatus.Text = "Installation annulée. Aucun runtime incomplet n’a été activé.";
            _downloads.Update(activity.Id, DownloadActivityState.Cancelled, InstallProgress.Value / 100, ActionStatus.Text);
        }
        catch (Exception exception)
        {
            ActionStatus.Text = $"Installation impossible : {exception.Message}";
            _downloads.Update(activity.Id, DownloadActivityState.Failed, InstallProgress.Value / 100, exception.Message);
        }
        finally
        {
            _installationCancellation.Dispose();
            _installationCancellation = null;
            InstallButton.IsEnabled = true;
            RepairButton.IsEnabled = true;
            UninstallButton.IsEnabled = true;
            CancelInstallButton.IsVisible = false;
            InstallProgress.IsVisible = false;
            RefreshDownloadItems();
        }
    }

    private void CancelInstall(object? sender, RoutedEventArgs args) =>
        _installationCancellation?.Cancel();

    private async void PrepareGameData(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null || _installationCancellation is not null) return;
        _installationCancellation = new CancellationTokenSource();
        PrepareDataButton.IsEnabled = false;
        CancelInstallButton.IsVisible = true;
        InstallProgress.IsVisible = true;
        InstallProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(value =>
            {
                InstallProgress.Value = value * 100;
                ActionStatus.Text = $"Préparation des données du jeu… {value:P0}";
            });
            await _gameData.PrepareAsync(_selectedGame.Id, _selectedGame.Executable, _selectedGame.DiscImage,
                progress, _installationCancellation.Token);
            ActionStatus.Text = "Données du jeu préparées. L’image d’origine a été conservée.";
        }
        catch (OperationCanceledException) { ActionStatus.Text = "Préparation annulée; les anciennes données ont été conservées."; }
        catch (Exception exception) { ActionStatus.Text = $"Préparation impossible : {exception.Message}"; }
        finally
        {
            _installationCancellation.Dispose();
            _installationCancellation = null;
            PrepareDataButton.IsEnabled = true;
            CancelInstallButton.IsVisible = false;
            InstallProgress.IsVisible = false;
            RefreshGameState();
        }
    }

    private void UninstallPartyBoard(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null || _installationCancellation is not null) return;
        var managed = _installer.GetStatus(_selectedGame.Id);
        _installer.Uninstall(_selectedGame.Id);
        if (string.Equals(_selectedGame.Executable, managed.ExecutablePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            _selectedGame.Executable = "";
        PersistSelection();
        RefreshGameState();
        ActionStatus.Text = "PartyBoard a été désinstallé. L’image ISO/GCM/RVZ n’a pas été supprimée.";
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
    }

    private void RefreshGameState()
    {
        if (_selectedGame is null) return;
        DiscPathText.Text = File.Exists(_selectedGame.DiscImage)
            ? _selectedGame.DiscImage
            : "Aucune image sélectionnée";
        ExecutablePathText.Text = File.Exists(_selectedGame.Executable)
            ? _selectedGame.Executable
            : "PartyBoard non configuré";
        var runtime = _installer.GetStatus(_selectedGame.Id);
        InstallButton.Content = runtime.IsInstalled ? "Mettre à jour" : "Installer PartyBoard";
        RepairButton.IsVisible = runtime.IsInstalled || runtime.NeedsRepair;
        UninstallButton.IsVisible = runtime.IsInstalled || runtime.NeedsRepair;
        if (runtime.IsInstalled)
            ExecutablePathText.Text = $"{runtime.ExecutablePath}\nVersion : {runtime.Version}";
        else if (runtime.NeedsRepair)
            ExecutablePathText.Text = "Installation gérée incomplète — réparation nécessaire";
        var compatibility = DiscImageService.Inspect(_selectedGame.DiscImage);
        DiscCompatibilityText.Text = compatibility.Message;
        var prepared = _gameData.IsPrepared(_selectedGame.Id, _selectedGame.Executable);
        PrepareDataButton.Content = prepared ? "Repréparer les données" : "Préparer les données";
        PrepareDataButton.IsEnabled = File.Exists(_selectedGame.Executable) && compatibility.Supported;
        if (prepared) ExecutablePathText.Text += "\nDonnées du jeu : prêtes";
        PlayButton.IsEnabled = CanPlay();
        ActionStatus.Text = PlayButton.IsEnabled
            ? "Configuration prête."
            : "Sélectionne une image compatible et l’exécutable PartyBoard.";
    }

    private bool CanPlay() =>
        _selectedGame is not null &&
        File.Exists(_selectedGame.Executable) &&
        DiscImageService.Inspect(_selectedGame.DiscImage).Supported &&
        _gameData.IsPrepared(_selectedGame.Id, _selectedGame.Executable);

    private static string ResolveApplicationPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? ""
            : Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    private void ShowLibrary(object? sender, RoutedEventArgs args)
    {
        LibraryView.IsVisible = true;
        ModsView.IsVisible = false;
        DownloadsView.IsVisible = false;
        SettingsView.IsVisible = false;
    }

    private void ShowMods(object? sender, RoutedEventArgs args)
    {
        LibraryView.IsVisible = false;
        ModsView.IsVisible = true;
        DownloadsView.IsVisible = false;
        SettingsView.IsVisible = false;
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
            _remoteMods = await _gameBanana.GetLatestAsync(_selectedGame.GameBananaGameId);
            RefreshModItems();
            ModsStatus.Text = $"{_remoteMods.Count} mods récupérés.";
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
        var items = ids.Select(id =>
        {
            var remote = _remoteMods.FirstOrDefault(item => item.Id == id);
            installed.TryGetValue(id, out var local);
            var status = local is null ? "Non installé" : local.Enabled ? "Installé • activé" : "Installé • désactivé";
            if (conflictedIds.Contains(id)) status += " • conflit";
            return new DesktopModItem(id, remote?.Name ?? local?.Name ?? $"Mod {id}", status,
                local?.Priority ?? 100, local?.Enabled ?? false, local is not null, remote);
        }).OrderBy(item => item.Name).ToArray();
        ModsList.ItemsSource = items;
        ConflictsStatus.Text = conflicts.Count == 0
            ? "Aucun conflit entre les mods actifs."
            : $"⚠ {conflicts.Count} fichier(s) en conflit : " + string.Join(", ", conflicts.Take(5).Select(item => item.RelativePath));
    }

    private async void SelectMod(object? sender, SelectionChangedEventArgs args)
    {
        _selectedMod = ModsList.SelectedItem as DesktopModItem;
        if (_selectedMod is null) return;
        ModTitle.Text = _selectedMod.Name;
        ModDetailStatus.Text = _selectedMod.Status;
        ModVersion.Text = _selectedMod.Remote?.VersionLabel ?? "Version locale";
        ModDescription.Text = _selectedMod.Remote?.Description ?? "Métadonnées distantes indisponibles.";
        ModInstructions.Text = string.IsNullOrWhiteSpace(_selectedMod.Remote?.InstallInstructions)
            ? "" : "Instructions : " + _selectedMod.Remote.InstallInstructions;
        OpenModPageButton.IsEnabled = !string.IsNullOrWhiteSpace(_selectedMod.Remote?.ProfileUrl);
        OpenModImageButton.IsEnabled = (_selectedMod.Remote?.ImageUrls?.Count ?? 0) > 0;
        ModPriority.Text = _selectedMod.Priority.ToString();
        InstallModButton.IsEnabled = _selectedMod.Remote is not null;
        InstallModButton.Content = _selectedMod.Installed ? "Réinstaller" : "Installer";
        ToggleModButton.IsEnabled = _selectedMod.Installed;
        ToggleModButton.Content = _selectedMod.Enabled ? "Désactiver" : "Activer";
        DeleteModButton.IsEnabled = _selectedMod.Installed;
        ModPreview.Source = null;
        _modPreviewBitmap?.Dispose();
        _modPreviewBitmap = null;
        var selectedId = _selectedMod.Id;
        var previewUrl = _selectedMod.Remote?.ThumbnailUrl;
        if (!string.IsNullOrWhiteSpace(previewUrl))
        {
            try
            {
                var cached = await _mediaCache.GetAsync(previewUrl, true);
                if (_selectedMod?.Id != selectedId) return;
                _modPreviewBitmap = new Bitmap(cached);
                ModPreview.Source = _modPreviewBitmap;
            }
            catch (Exception exception) { ModsStatus.Text = $"Aperçu indisponible : {exception.Message}"; }
        }
    }

    private async void InstallMod(object? sender, RoutedEventArgs args)
        => await RunModInstallationAsync();

    private async Task RunModInstallationAsync()
    {
        if (_selectedMod?.Remote is null || _modManager is null) return;
        var activity = _downloads.Create("mod", _selectedMod.Name, _selectedMod.Id.ToString());
        _downloads.Update(activity.Id, DownloadActivityState.Running, 0, "Téléchargement du mod");
        RefreshDownloadItems();
        SetModActions(false);
        ModProgress.IsVisible = true;
        try
        {
            var lastPersistedProgress = 0d;
            var progress = new Progress<double>(value =>
            {
                ModProgress.Value = value * 100;
                if (value >= 1 || value - lastPersistedProgress >= .02)
                {
                    lastPersistedProgress = value;
                    _downloads.Update(activity.Id, DownloadActivityState.Running, value, "Installation sécurisée du mod");
                    RefreshDownloadItems();
                }
            });
            ModsStatus.Text = "Analyse des dépendances…";
            var plan = await _modManager.PlanInstallAsync(_selectedMod.Remote, _gameBanana, progress);
            if (!await ConfirmModPlanAsync(plan))
            {
                ModsStatus.Text = "Installation annulée avant toute modification.";
                _downloads.Update(activity.Id, DownloadActivityState.Cancelled, ModProgress.Value / 100, ModsStatus.Text);
                return;
            }
            await _modManager.InstallPlanAsync(plan, allowIncompatible: true, progress);
            ModsStatus.Text = $"{plan.Packages.Count} mod(s) installé(s) et activé(s).";
            _downloads.Update(activity.Id, DownloadActivityState.Completed, 1, ModsStatus.Text);
            RefreshModItems();
        }
        catch (OperationCanceledException)
        {
            ModsStatus.Text = "Installation du mod annulée.";
            _downloads.Update(activity.Id, DownloadActivityState.Cancelled, ModProgress.Value / 100, ModsStatus.Text);
        }
        catch (Exception exception)
        {
            ModsStatus.Text = $"Installation impossible : {exception.Message}";
            _downloads.Update(activity.Id, DownloadActivityState.Failed, ModProgress.Value / 100, exception.Message);
        }
        finally { ModProgress.IsVisible = false; SetModActions(true); RefreshDownloadItems(); }
    }

    private void ToggleMod(object? sender, RoutedEventArgs args)
    {
        if (_selectedMod is null || _modManager is null) return;
        _modManager.SetEnabled(_selectedMod.Id, !_selectedMod.Enabled);
        RefreshModItems();
    }

    private void DeleteMod(object? sender, RoutedEventArgs args)
    {
        if (_selectedMod is null || _modManager is null) return;
        _modManager.Uninstall(_selectedMod.Id);
        ModsStatus.Text = $"{_selectedMod.Name} supprimé.";
        RefreshModItems();
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
        InstallModButton.IsEnabled = enabled && _selectedMod?.Remote is not null;
        ToggleModButton.IsEnabled = enabled && _selectedMod?.Installed == true;
        DeleteModButton.IsEnabled = enabled && _selectedMod?.Installed == true;
    }

    private void ShowDownloads(object? sender, RoutedEventArgs args)
    {
        LibraryView.IsVisible = false;
        ModsView.IsVisible = false;
        DownloadsView.IsVisible = true;
        SettingsView.IsVisible = false;
        RefreshDownloadItems();
    }

    private void ClearDownloads(object? sender, RoutedEventArgs args)
    {
        _downloads.ClearFinished();
        RefreshDownloadItems();
    }

    private void RefreshDownloadItems() => DownloadsList.ItemsSource = _downloads.Load();

    private void SelectDownload(object? sender, SelectionChangedEventArgs args)
    {
        _selectedDownload = DownloadsList.SelectedItem as DownloadActivity;
        ResumeDownloadButton.IsEnabled = _selectedDownload is not null &&
            _selectedDownload.State is DownloadActivityState.Interrupted or DownloadActivityState.Failed or DownloadActivityState.Cancelled;
    }

    private async void ResumeDownload(object? sender, RoutedEventArgs args)
    {
        if (_selectedDownload is null) return;
        if (_selectedDownload.Kind == "runtime")
        {
            ShowLibrary(sender, args);
            await RunInstallationAsync(repair: false);
            return;
        }
        if (_selectedDownload.Kind == "mod" && int.TryParse(_selectedDownload.ReferenceId, out var modId) &&
            _modManager is not null)
        {
            try
            {
                var remote = _remoteMods.FirstOrDefault(item => item.Id == modId) ?? await _gameBanana.GetAsync(modId);
                var installed = _modManager.GetInstalled().FirstOrDefault(item => item.Id == modId);
                _selectedMod = new DesktopModItem(modId, remote.Name,
                    installed is null ? "Non installé" : "Installation interrompue",
                    installed?.Priority ?? 100, installed?.Enabled ?? false, installed is not null, remote);
                ShowMods(sender, args);
                await RunModInstallationAsync();
            }
            catch (Exception exception)
            {
                ModsStatus.Text = $"Reprise impossible : {exception.Message}";
            }
        }
    }

    public sealed record DesktopModItem(int Id, string Name, string Status, int Priority, bool Enabled,
        bool Installed, GameBananaMod? Remote);

    private async Task<bool> ConfirmModPlanAsync(ModInstallPlan plan)
    {
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
        return await dialog.ShowDialog<bool>(this);
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
        LibraryView.IsVisible = ModsView.IsVisible = DownloadsView.IsVisible = false;
        SettingsView.IsVisible = true;
    }

    private void OpenDataDirectory(object? sender, RoutedEventArgs args)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        Process.Start(new ProcessStartInfo(_paths.DataDirectory) { UseShellExecute = true });
    }

    private void ClearMediaCache(object? sender, RoutedEventArgs args)
    {
        ModPreview.Source = null;
        _modPreviewBitmap?.Dispose();
        _modPreviewBitmap = null;
        var bytes = _mediaCache.Clear();
        SettingsStatus.Text = $"Cache vidé ({bytes / 1024d / 1024d:F1} Mio libérés).";
    }

    private void ApplyPreferences()
    {
        _loadingSettings = true;
        LanguagePicker.SelectedIndex = _preferences.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ThemePicker.SelectedIndex = _preferences.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CheckUpdatesBox.IsChecked = _preferences.CheckGamesOnStartup;
        UpdatePopupBox.IsChecked = _preferences.ShowGameUpdatePopup;
        RefreshModsBox.IsChecked = _preferences.RefreshModsOnOpen;
        if (_preferences.WindowWidth >= MinWidth) Width = _preferences.WindowWidth;
        if (_preferences.WindowHeight >= MinHeight) Height = _preferences.WindowHeight;
        if (Avalonia.Application.Current is { } application)
            application.RequestedThemeVariant = _preferences.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        _loadingSettings = false;
    }

    private void SaveSettings(object? sender, RoutedEventArgs args)
    {
        if (_loadingSettings) return;
        var language = (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "fr";
        var theme = (ThemePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "dark";
        _preferences = _preferences with
        {
            Language = language,
            Theme = theme,
            CheckGamesOnStartup = CheckUpdatesBox.IsChecked == true,
            ShowGameUpdatePopup = UpdatePopupBox.IsChecked == true,
            RefreshModsOnOpen = RefreshModsBox.IsChecked == true
        };
        _preferencesStore.Save(_preferences);
        if (Avalonia.Application.Current is { } application)
            application.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        SettingsStatus.Text = language == "en"
            ? "Settings saved. Full English interface resources are being finalized for v0.8."
            : "Paramètres enregistrés.";
    }

    private void ResetSettings(object? sender, RoutedEventArgs args)
    {
        _preferencesStore.Reset();
        _preferences = new UserPreferences();
        ApplyPreferences();
        _preferencesStore.Save(_preferences);
        SettingsStatus.Text = "Paramètres réinitialisés.";
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
