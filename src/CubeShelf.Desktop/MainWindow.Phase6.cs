using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CubeShelf.Core.Library;
using CubeShelf.Core.Storage;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private CubeShelfStorageService? _phase6Storage;
    private PreparedGameDataVault? _phase6PreparedData;
    private CancellationTokenSource? _phase6Lifetime;
    private Bitmap? _phase6GalleryBitmap;
    private int _phase6GalleryIndex;
    private readonly HashSet<string> _phase6UpdateNotices = new(StringComparer.OrdinalIgnoreCase);

    private void InitializePhase6Parity()
    {
        _phase6Storage = new CubeShelfStorageService(_paths);
        _phase6PreparedData = new PreparedGameDataVault(_paths);
        _phase6Lifetime = new CancellationTokenSource();
        ModsList.SelectionChanged += Phase6ModSelectionChanged;
        Dispatcher.UIThread.Post(RefreshStoragePhase6, DispatcherPriority.Background);
        _ = Phase6UpdatePopupLoopAsync(_phase6Lifetime.Token);
    }

    private void DisposePhase6Parity()
    {
        ModsList.SelectionChanged -= Phase6ModSelectionChanged;
        _phase6Lifetime?.Cancel();
        _phase6Lifetime?.Dispose();
        _phase6Lifetime = null;
        _phase6GalleryBitmap?.Dispose();
        _phase6GalleryBitmap = null;
        _phase6Storage = null;
        _phase6PreparedData = null;
    }

    private string P6(string french, string english) =>
        UiLocalization.IsEnglish(_preferences.Language) ? english : french;

    // ---------- Mods gallery ----------
    private async void Phase6ModSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        _phase6GalleryIndex = 0;
        await LoadPhase6ModGalleryAsync();
    }

    private IReadOnlyList<string> Phase6SelectedImages()
    {
        if (_selectedMod?.Remote is null) return Array.Empty<string>();
        var images = _selectedMod.Remote.ImageUrls?.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToArray()
            ?? Array.Empty<string>();
        if (images.Length > 0) return images;
        return string.IsNullOrWhiteSpace(_selectedMod.Remote.ThumbnailUrl)
            ? Array.Empty<string>()
            : new[] { _selectedMod.Remote.ThumbnailUrl };
    }

    private async Task LoadPhase6ModGalleryAsync()
    {
        var selectedId = _selectedMod?.Id;
        var images = Phase6SelectedImages();
        if (images.Count == 0)
        {
            ModGalleryCounter.Text = "0 / 0";
            PreviousModImageButton.IsEnabled = NextModImageButton.IsEnabled = OpenModImageButton.IsEnabled = false;
            return;
        }

        _phase6GalleryIndex = Math.Clamp(_phase6GalleryIndex, 0, images.Count - 1);
        ModGalleryCounter.Text = $"{_phase6GalleryIndex + 1} / {images.Count}";
        PreviousModImageButton.IsEnabled = NextModImageButton.IsEnabled = images.Count > 1;
        OpenModImageButton.IsEnabled = true;

        try
        {
            var cached = await _mediaCache.GetAsync(images[_phase6GalleryIndex], true);
            if (_selectedMod?.Id != selectedId) return;
            var bitmap = new Bitmap(cached);
            _phase6GalleryBitmap?.Dispose();
            _phase6GalleryBitmap = bitmap;
            ModPreview.Source = bitmap;
        }
        catch (Exception exception)
        {
            ModsStatus.Text = P6($"Image indisponible : {exception.Message}", $"Image unavailable: {exception.Message}");
        }
    }

    private async void PreviousModImagePhase6(object? sender, RoutedEventArgs args)
    {
        var images = Phase6SelectedImages();
        if (images.Count == 0) return;
        _phase6GalleryIndex = (_phase6GalleryIndex - 1 + images.Count) % images.Count;
        await LoadPhase6ModGalleryAsync();
    }

    private async void NextModImagePhase6(object? sender, RoutedEventArgs args)
    {
        var images = Phase6SelectedImages();
        if (images.Count == 0) return;
        _phase6GalleryIndex = (_phase6GalleryIndex + 1) % images.Count;
        await LoadPhase6ModGalleryAsync();
    }

    private async void OpenCurrentModImagePhase6(object? sender, RoutedEventArgs args)
    {
        var images = Phase6SelectedImages();
        if (images.Count == 0) return;
        try
        {
            var path = await _mediaCache.GetAsync(images[Math.Clamp(_phase6GalleryIndex, 0, images.Count - 1)], false);
            var bitmap = new Bitmap(path);
            var window = CreatePhase7Dialog(_selectedMod?.Name ?? "CubeShelf", 1100, 780, canResize: true);
            window.MinWidth = 640;
            window.MinHeight = 480;
            window.Content = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            window.Closed += (_, _) => bitmap.Dispose();
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            ModsStatus.Text = P6($"Image indisponible : {exception.Message}", $"Image unavailable: {exception.Message}");
        }
    }

    // ---------- Diagnostics ----------
    private async void OpenDiagnosticsPhase6(object? sender, RoutedEventArgs args)
    {
        var report = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 13,
            Text = BuildDiagnosticReportPhase6()
        };
        ScrollViewer.SetVerticalScrollBarVisibility(report, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(report, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var dialog = CreatePhase7Dialog(P7("CubeShelf • Diagnostic", "CubeShelf • Diagnostics"), 800, 650, canResize: true);
        dialog.MinWidth = 640;
        dialog.MinHeight = 480;
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.Children.Add(new TextBlock
        {
            Text = P6("Diagnostic CubeShelf", "CubeShelf diagnostics"),
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });
        Grid.SetRow(report, 1);
        root.Children.Add(report);
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(buttons, 2);
        var refresh = new Button { Content = P6("Actualiser", "Refresh") };
        refresh.Click += (_, _) => report.Text = BuildDiagnosticReportPhase6();
        var test = new Button { Content = P6("Tester l'installation", "Test installation") };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            report.Text = P6("Test en cours…", "Running tests…");
            try { report.Text = await BuildInstallationTestPhase6Async(); }
            catch (Exception ex) { report.Text = P6("Échec du test : ", "Test failed: ") + ex; }
            finally { test.IsEnabled = true; }
        };
        var copy = new Button { Content = P6("Copier le diagnostic", "Copy diagnostics") };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(report.Text ?? "");
        };
        var close = new Button { Content = P6("Fermer", "Close") };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(refresh); buttons.Children.Add(test); buttons.Children.Add(copy); buttons.Children.Add(close);
        root.Children.Add(buttons);
        dialog.Content = root;
        await dialog.ShowDialog(this);
    }

    private string BuildDiagnosticReportPhase6()
    {
        var lines = new List<string>();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var dolphin = _dolphinToolService?.GetStatus();
        var english = UiLocalization.IsEnglish(_preferences.Language);
        string L(string fr, string en) => english ? en : fr;
        lines.Add(L("Diagnostic CubeShelf", "CubeShelf Diagnostics"));
        lines.Add("====================");
        lines.Add($"{L("Launcher", "Launcher"),-16}: v{version}");
        lines.Add($"{L("Système", "OS"),-16}: {RuntimeInformation.OSDescription}");
        lines.Add($"{L("Architecture", "Architecture"),-16}: {RuntimeInformation.ProcessArchitecture}");
        lines.Add($"{L("Processus 64 bits", "64-bit process"),-16}: {Environment.Is64BitProcess}");
        lines.Add($"{L("Dossier app", "App directory"),-16}: {AppContext.BaseDirectory}");
        lines.Add($"{L("Dossier données", "Data directory"),-16}: {_paths.DataDirectory}");
        lines.Add($"DolphinTool      : {(dolphin?.Available == true ? "OK • " + dolphin.Path : L("Absent", "Missing"))}");
        lines.Add($"{L("Espace libre", "Free space"),-16}: {Phase6FreeSpaceText()}");
        lines.Add($"{L("File", "Queue"),-16}: {_portableQueue?.PendingCount ?? 0} {L("en attente", "pending")}");
        lines.Add("");
        lines.Add(L("Jeu sélectionné", "Selected game"));
        lines.Add("---------------");
        if (_selectedGame is null)
        {
            lines.Add(L("Aucun jeu sélectionné.", "No game selected."));
            return string.Join(Environment.NewLine, lines);
        }

        var game = _selectedGame;
        HydrateGamePathsPhase3(game);
        var runtime = _installer.GetStatus(game.Id);
        var compatibility = DiscImageService.Inspect(game.DiscImage, game.SupportedDiscIds, english);
        lines.Add($"{L("Titre", "Title"),-16}: {game.Title}");
        lines.Add($"ID              : {game.Id}");
        lines.Add($"{L("Favori", "Favorite"),-16}: {(game.IsFavorite ? L("Oui", "Yes") : L("Non", "No"))}");
        lines.Add($"{L("Lancements", "Launches"),-16}: {game.PlayCount}");
        lines.Add($"{L("Temps de jeu", "Play time"),-16}: {FormatPlayTime(game.TotalPlaySeconds)}");
        lines.Add($"{L("Dernier jeu", "Last played"),-16}: {game.LastPlayedAt?.ToLocalTime().ToString("g") ?? "—"}");
        lines.Add($"Runtime         : {(runtime.IsInstalled ? L("Installé", "Installed") : runtime.NeedsRepair ? L("À réparer", "Needs repair") : L("Non installé", "Not installed"))}");
        lines.Add($"{L("Données jeu", "Game data"),-16}: {(_gameData.IsPrepared(game.Id, game.Executable) ? L("Prêtes", "Ready") : _phase6PreparedData?.HasData(game.Id) == true ? L("Conservées", "Preserved") : L("À préparer", "Not prepared"))}");
        lines.Add($"Executable      : {game.Executable}");
        lines.Add($"Runtime version : {runtime.Version}");
        lines.Add($"{L("Image disque", "Disc image"),-16}: {(File.Exists(game.DiscImage) ? game.DiscImage : L("Aucune", "None"))}");
        lines.Add($"{L("Compatibilité", "Compatibility"),-16}: {compatibility.Message}");
        lines.Add($"{L("Mods installés", "Installed mods"),-16}: {_modManager?.GetInstalled().Count ?? 0}");
        if (_gameUpdateSnapshots.TryGetValue(game.Id, out var update))
        {
            lines.Add($"{L("Source locale", "Local source"),-16}: {(update.LocalSource.Present ? update.LocalSource.Path : L("absente", "missing"))}");
            lines.Add($"{L("Source gérée", "Managed source"),-16}: {update.LocalSource.ManagedByCubeShelf}");
            lines.Add($"{L("Commit source", "Source commit"),-16}: {update.LatestSource?.Sha ?? "—"}");
            lines.Add($"{L("Commit release", "Release commit"),-16}: {update.RuntimeRelease.Commit}");
            lines.Add($"{L("Mise à jour", "Update"),-16}: {(update.RuntimeUpdateAvailable ? L("Disponible", "Available") : update.RuntimeUpdatePendingBuild ? L("Build en attente", "Build pending") : L("Non", "No"))}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<string> BuildInstallationTestPhase6Async()
    {
        var lines = new List<string>();
        void Add(bool ok, string text) => lines.Add($"{(ok ? "✓" : "✕")} {text}");
        lines.Add(P6("CubeShelf • Test de l'installation", "CubeShelf • Installation test"));
        lines.Add("================================");
        lines.Add($"Date : {DateTime.Now:g}");
        lines.Add("");

        var writeTest = Path.Combine(_paths.DataDirectory, $"write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            await File.WriteAllTextAsync(writeTest, "CubeShelf");
            File.Delete(writeTest);
            Add(true, P6("Écriture dans le dossier de données CubeShelf", "CubeShelf data directory is writable"));
        }
        catch (Exception ex) { Add(false, P6("Écriture des données : ", "Data write: ") + ex.Message); }

        try
        {
            var root = Path.GetPathRoot(_paths.DataDirectory);
            var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
            var enough = drive is not null && drive.AvailableFreeSpace >= 2L * 1024 * 1024 * 1024;
            Add(enough, drive is null
                ? P6("Espace disque indisponible", "Disk space unavailable")
                : P6($"Espace libre : {FormatBytes(drive.AvailableFreeSpace)} (2 Go minimum recommandés)", $"Free space: {FormatBytes(drive.AvailableFreeSpace)} (2 GB recommended minimum)"));
        }
        catch (Exception ex) { Add(false, P6("Lecture espace disque : ", "Disk space: ") + ex.Message); }

        if (_selectedGame is null)
        {
            lines.Add("");
            lines.Add(P6("Sélectionne un jeu pour tester son installation.", "Select a game to test its installation."));
            return string.Join(Environment.NewLine, lines);
        }

        var game = _selectedGame;
        HydrateGamePathsPhase3(game);
        var runtime = _installer.GetStatus(game.Id);
        Add(runtime.IsInstalled, P6("PartyBoard présent", "PartyBoard present"));
        var exeDir = runtime.IsInstalled ? Path.GetDirectoryName(runtime.ExecutablePath)! : "";
        Add(runtime.IsInstalled && Directory.Exists(Path.Combine(exeDir, "res")), P6("Ressources PartyBoard (res) présentes", "PartyBoard resources (res) present"));
        var prepared = _gameData.IsPrepared(game.Id, game.Executable);
        Add(prepared || _phase6PreparedData?.HasData(game.Id) == true,
            prepared ? P6("Données de jeu préparées présentes", "Prepared game data present") : P6("Données préparées conservées pour réinstallation", "Prepared game data preserved for reinstall"));
        Add(File.Exists(game.DiscImage), File.Exists(game.DiscImage)
            ? P6($"Image disque présente : {Path.GetFileName(game.DiscImage)}", $"Disc image present: {Path.GetFileName(game.DiscImage)}")
            : P6("Aucune image ISO/GCM/RVZ sélectionnée", "No ISO/GCM/RVZ selected"));
        if (File.Exists(game.DiscImage))
        {
            var compatibility = DiscImageService.Inspect(game.DiscImage, game.SupportedDiscIds);
            Add(compatibility.Recognized && compatibility.Supported, P6("Compatibilité disque : ", "Disc compatibility: ") + compatibility.Message);
            if (Path.GetExtension(game.DiscImage).Equals(".rvz", StringComparison.OrdinalIgnoreCase))
                Add(_dolphinToolService?.IsAvailable() == true, P6("DolphinTool disponible pour le RVZ", "DolphinTool available for RVZ"));
        }
        try
        {
            var release = _gameUpdateService is null ? null : await _gameUpdateService.GetRuntimeReleaseAsync(game);
            Add(release?.Available == true, P6("Release PartyBoard compatible disponible", "Compatible PartyBoard release available"));
        }
        catch (Exception ex) { Add(false, P6("Accès release : ", "Release access: ") + ex.Message); }

        return string.Join(Environment.NewLine, lines);
    }

    private string Phase6FreeSpaceText()
    {
        try
        {
            var root = Path.GetPathRoot(_paths.DataDirectory);
            return string.IsNullOrWhiteSpace(root) ? P7("indisponible", "unavailable") : FormatBytes(new DriveInfo(root).AvailableFreeSpace);
        }
        catch { return P7("indisponible", "unavailable"); }
    }

    // ---------- Storage / uninstall ----------
    private void RefreshStoragePhase6()
    {
        if (_phase6Storage is null || StorageSummaryText is null) return;
        var summary = _phase6Storage.Measure(_catalogGames);
        StorageSummaryText.Text = P6(
            $"PartyBoard : {FormatBytes(summary.RuntimeBytes)} • Données jeu : {FormatBytes(summary.GameDataBytes)} • Données conservées : {FormatBytes(summary.PreservedGameDataBytes)} • Dolphin : {FormatBytes(summary.DolphinBytes)} • Cache : {FormatBytes(summary.CacheBytes)} • Sauvegardes MAJ : {FormatBytes(summary.UpdaterBackupBytes)}",
            $"PartyBoard: {FormatBytes(summary.RuntimeBytes)} • Game data: {FormatBytes(summary.GameDataBytes)} • Preserved data: {FormatBytes(summary.PreservedGameDataBytes)} • Dolphin: {FormatBytes(summary.DolphinBytes)} • Cache: {FormatBytes(summary.CacheBytes)} • Update backups: {FormatBytes(summary.UpdaterBackupBytes)}");
    }

    private void RefreshStoragePhase6Click(object? sender, RoutedEventArgs args) => RefreshStoragePhase6();

    private void CleanCachePhase6(object? sender, RoutedEventArgs args)
    {
        if (_phase6Storage is null) return;
        ModPreview.Source = null;
        _phase6GalleryBitmap?.Dispose();
        _phase6GalleryBitmap = null;
        var released = _phase6Storage.CleanTransientCaches();
        RefreshStoragePhase6();
        ShowToastParity(P6("Cache nettoyé", "Cache cleaned"), P6(
            $"{FormatBytes(released)} libérés. Les jeux, ISO/RVZ et Dolphin n'ont pas été supprimés.",
            $"{FormatBytes(released)} freed. Games, ISO/RVZ and Dolphin were not removed."));
    }

    private async void UninstallGamePhase6(object? sender, RoutedEventArgs args)
    {
        var game = _selectedGame;
        if (game is null || _phase6PreparedData is null || _portableQueue is null) return;
        if (_sessions.IsRunning(game.Id))
        {
            ShowToastParity(P6("Jeu en cours", "Game is running"), P6("Arrête le jeu avant de le désinstaller.", "Stop the game before uninstalling it."));
            return;
        }

        var dialog = CreatePhase7Dialog("CubeShelf", 620, 360);
        var choice = 0;
        var stack = new StackPanel { Margin = new Thickness(26), Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = P6($"Désinstaller {game.Title}", $"Uninstall {game.Title}"), FontSize = 26, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Text = P6("CubeShelf ne supprime jamais ton ISO/GCM/RVZ original.", "CubeShelf never deletes your original ISO/GCM/RVZ."), TextWrapping = TextWrapping.Wrap });
        var runtimeOnly = new Button { Content = P6("Supprimer PartyBoard uniquement — conserver les données préparées", "Remove PartyBoard only — keep prepared game data"), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        runtimeOnly.Click += (_, _) => { choice = 1; dialog.Close(true); };
        var everything = new Button { Content = P6("Supprimer PartyBoard + données préparées", "Remove PartyBoard + prepared game data"), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        everything.Click += (_, _) => { choice = 2; dialog.Close(true); };
        var cancel = new Button { Content = P6("Annuler", "Cancel"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        cancel.Click += (_, _) => dialog.Close(false);
        stack.Children.Add(runtimeOnly); stack.Children.Add(everything); stack.Children.Add(cancel);
        dialog.Content = stack;
        if (!await dialog.ShowDialog<bool>(this) || choice == 0) return;

        HydrateGamePathsPhase3(game);
        var runtime = _installer.GetStatus(game.Id);
        var preparedVault = _phase6PreparedData;
        var kept = false;

        var item = _portableQueue.Enqueue(
            $"uninstall:{game.Id}",
            P6($"Désinstallation — {game.Title}", $"Uninstall — {game.Title}"),
            "UNINSTALL",
            choice == 1
                ? P6("Suppression de PartyBoard • données préparées conservées", "Remove PartyBoard • keep prepared game data")
                : P6("Suppression de PartyBoard et des données préparées", "Remove PartyBoard and prepared game data"),
            async (progress, cancellationToken) =>
            {
                progress.Report(.03);

                if (choice == 1 && runtime.IsInstalled)
                {
                    kept = preparedVault.DetachFromRuntime(game.Id, runtime.ExecutablePath);
                    progress.Report(kept ? .22 : .14);
                }
                else
                {
                    progress.Report(.14);
                }

                // UninstallAsync reports the real file-removal progress. Map it
                // into the remainder of this queue operation so the user sees a
                // continuous progress bar rather than an instant UI jump.
                var deleteProgress = new InlinePhase6Progress(value =>
                    progress.Report(.14 + Math.Clamp(value, 0, 1) * .82));
                await _installer.UninstallAsync(game.Id, deleteProgress, cancellationToken);

                if (choice == 2)
                    preparedVault.Delete(game.Id);

                progress.Report(1);
            },
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    game.Executable = "";
                    PersistSelection();
                    RefreshGameState();
                    RefreshLibraryParity();
                    RefreshStoragePhase6();
                    ShowToastParity(P6("Jeu désinstallé", "Game uninstalled"), choice == 1
                        ? P6(kept
                            ? "PartyBoard supprimé. Les données préparées ont été conservées pour la prochaine installation."
                            : "PartyBoard supprimé. Aucune donnée préparée n'était présente. L'ISO/RVZ est intact.",
                            kept
                                ? "PartyBoard removed. Prepared game data was preserved for the next installation."
                                : "PartyBoard removed. No prepared game data was present. ISO/RVZ is untouched.")
                        : P6("PartyBoard et les données préparées ont été supprimés. L'ISO/RVZ original est intact.",
                            "PartyBoard and prepared game data were removed. The original ISO/RVZ is untouched."));
                });
                return Task.CompletedTask;
            },
            referenceId: game.Id,
            cancellable: false);

        if (item is null)
        {
            ShowToastParity(P6("Désinstallation", "Uninstall"), P6(
                "Une désinstallation de ce jeu est déjà en cours.",
                "An uninstall operation for this game is already running."));
            return;
        }

        _activeGameQueueItem = item;
        ActionStatus.Text = P6("Désinstallation ajoutée à la file d’opérations.", "Uninstall added to the operation queue.");
        RefreshPortableQueueView();
        ParityShowDownloads(this, new RoutedEventArgs());
    }

    private void RestorePreparedGameDataPhase6(GameCatalogEntry game, string runtimeExecutable)
    {
        if (_phase6PreparedData?.RestoreToRuntime(game.Id, runtimeExecutable) == true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshGameState();
                RefreshStoragePhase6();
                ShowToastParity(P6("Données restaurées", "Game data restored"), P6("Les données préparées conservées ont été rattachées au nouveau runtime.", "Preserved prepared data was attached to the new runtime."));
            });
        }
    }

    // ---------- Custom games ----------
    private async void AddCustomGamePhase6(object? sender, RoutedEventArgs args)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = P6("Ajouter un jeu / port PC", "Add a game / PC port"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(P6("Exécutable", "Executable"))
                {
                    Patterns = OperatingSystem.IsWindows() ? new[] { "*.exe" } : new[] { "*" }
                }
            }
        });
        var executable = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return;

        var id = "CUSTOM_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var game = new GameCatalogEntry
        {
            Id = id,
            Title = Path.GetFileNameWithoutExtension(executable),
            Genre = "GameCube",
            Description = P6("Jeu ajouté manuellement. Les intégrations GitHub/GameBanana restent optionnelles.", "Manually added game. GitHub/GameBanana integrations remain optional."),
            Executable = Path.GetFullPath(executable),
            GameRoot = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? "",
            Covers = new List<GameCover>
            {
                new() { Label = "Default", Front = "Assets/Covers/Common/placeholder.png", Back = "Assets/Covers/Common/placeholder.png", Spine = "Assets/Covers/Common/placeholder.png" }
            }
        };
        _catalogGames.Add(game);
        _catalogService.Save(_catalogGames);
        RefreshLibraryParity();
        SelectCatalogGame(game);
        ShowToastParity(P6("Jeu ajouté", "Game added"), P6("Le jeu a été ajouté à ta bibliothèque locale.", "The game was added to your local library."));
    }

    // ---------- Runtime update popup ----------
    private async Task Phase6UpdatePopupLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { _ = MaybeShowRuntimeUpdatePopupPhase6Async(); });
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private sealed class InlinePhase6Progress(Action<double> callback) : IProgress<double>
    {
        private readonly Action<double> _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        public void Report(double value) => _callback(value);
    }

    private async Task MaybeShowRuntimeUpdatePopupPhase6Async()
    {
        if (!_preferences.ShowGameUpdatePopup) return;
        var candidate = _catalogGames
            .Select(game => (game, snapshot: _gameUpdateSnapshots.TryGetValue(game.Id, out var value) ? value : null))
            .FirstOrDefault(pair => pair.snapshot?.RuntimeUpdateAvailable == true && !_phase6UpdateNotices.Contains($"{pair.game.Id}:{pair.snapshot.RuntimeRelease.Version}"));
        if (candidate.game is null || candidate.snapshot is null) return;

        var key = $"{candidate.game.Id}:{candidate.snapshot.RuntimeRelease.Version}";
        _phase6UpdateNotices.Add(key);
        var dialog = CreatePhase7Dialog(P7("Mise à jour de jeu disponible", "Game update available"), 590, 315);
        var stack = new StackPanel { Margin = new Thickness(26), Spacing = 13 };
        stack.Children.Add(new TextBlock { Text = candidate.game.Title, FontSize = 25, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock { Text = P6($"Une nouvelle version jouable de PartyBoard est disponible ({candidate.snapshot.RuntimeRelease.Version}).", $"A new playable PartyBoard build is available ({candidate.snapshot.RuntimeRelease.Version})."), TextWrapping = TextWrapping.Wrap });
        if (candidate.snapshot.LatestSource is not null)
            stack.Children.Add(new TextBlock { Text = candidate.snapshot.LatestSource.Message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var update = new Button { Content = P6("Mettre à jour maintenant", "Update now") };
        update.Classes.Add("primary");
        update.Click += (_, _) => dialog.Close(true);
        var later = new Button { Content = P6("Plus tard", "Later") };
        later.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(update); buttons.Children.Add(later); stack.Children.Add(buttons); dialog.Content = stack;
        if (await dialog.ShowDialog<bool>(this))
        {
            SelectCatalogGame(candidate.game);
            EnqueueRuntimeInstallationPhase3(repair: false, candidate.game);
            ShowParityView(DownloadsView);
            RefreshPortableQueueView();
        }
    }
}
