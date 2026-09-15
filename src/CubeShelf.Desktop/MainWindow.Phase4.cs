using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Library;
using CubeShelf.Core.Releases;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private DolphinToolService? _dolphinToolService;
    private ILauncherUpdateInstaller? _launcherUpdateInstaller;
    private CancellationTokenSource? _phase4Lifetime;
    private string _lastLauncherUpdateNotice = "";

    private void InitializePhase4Parity()
    {
        _dolphinToolService = new DolphinToolService(_paths);
        _launcherUpdateInstaller = LauncherUpdateInstallerFactory.Create(_launcherUpdates, _paths);
        _phase4Lifetime = new CancellationTokenSource();
        RefreshDolphinStatusPhase4Core();
        _ = PeriodicLauncherUpdateLoopPhase4Async(_phase4Lifetime.Token);
    }

    private void DisposePhase4Parity()
    {
        _phase4Lifetime?.Cancel();
        _phase4Lifetime?.Dispose();
        _phase4Lifetime = null;
        _dolphinToolService = null;
        _launcherUpdateInstaller = null;
    }

    private async Task PeriodicLauncherUpdateLoopPhase4Async(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                await CheckLauncherUpdatePhase4Async(interactive: false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void RefreshDolphinStatusPhase4(object? sender, RoutedEventArgs args) =>
        RefreshDolphinStatusPhase4Core();

    private void RefreshDolphinStatusPhase4Core()
    {
        if (_dolphinToolService is null || DolphinToolStatusText is null) return;
        var status = _dolphinToolService.GetStatus();
        DolphinToolStatusText.Text = status.Available
            ? P7($"✓ Disponible • {status.Source}\n{status.Path}", $"✓ Available • {status.Source}\n{status.Path}")
            : status.AutoInstallSupported
                ? P7("Non détecté • installation automatique officielle disponible sous Windows.", "Not detected • official automatic installation is available on Windows.")
                : P7("Non détecté • installe Dolphin Emulator sur cette plateforme.", "Not detected • install Dolphin Emulator on this platform.");
        InstallDolphinToolButton.IsEnabled = status.AutoInstallSupported && !status.Available;
    }

    private void InstallDolphinToolPhase4(object? sender, RoutedEventArgs args)
    {
        if (_dolphinToolService is null || _portableQueue is null) return;
        var item = _portableQueue.Enqueue(
            "tool:dolphin",
            "DolphinTool",
            "TOOL",
            P7("Téléchargement officiel + vérification SHA-256 + installation locale", "Official download + SHA-256 verification + local installation"),
            async (progress, cancellationToken) =>
            {
                _ = await _dolphinToolService.EnsureAvailableAsync(true, progress, cancellationToken);
            },
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshDolphinStatusPhase4Core();
                    ShowToastParity("DolphinTool", P7("DolphinTool est installé et prêt pour les fichiers RVZ.", "DolphinTool is installed and ready for RVZ files."));
                });
                return Task.CompletedTask;
            },
            referenceId: "dolphin");

        if (item is null)
            ShowToastParity("DolphinTool", P7("L’installation est déjà dans la file.", "The installation is already queued."));
        RefreshPortableQueueView();
    }

    private void OpenToolsFolderPhase4(object? sender, RoutedEventArgs args)
    {
        if (_dolphinToolService is null) return;
        Directory.CreateDirectory(_dolphinToolService.ManagedToolsRoot);
        _processLauncher.OpenDirectory(_dolphinToolService.ManagedToolsRoot);
    }

    private async Task QueueGameDataPreparationPhase4Async()
    {
        var game = _selectedGame;
        if (game is null || _portableQueue is null || _dolphinToolService is null) return;
        HydrateGamePathsPhase3(game);
        var executable = game.Executable;
        var discImage = game.DiscImage;
        var allowDolphinDownload = false;

        if (Path.GetExtension(discImage).Equals(".rvz", StringComparison.OrdinalIgnoreCase) &&
            !_dolphinToolService.IsAvailable())
        {
            var status = _dolphinToolService.GetStatus();
            if (!status.AutoInstallSupported)
            {
                ActionStatus.Text = P7("DolphinTool est requis pour un RVZ et n’est pas installé.", "DolphinTool is required for RVZ and is not installed.");
                ShowToastParity("RVZ", ActionStatus.Text);
                return;
            }

            var dialog = CreatePhase7Dialog(P7("DolphinTool requis", "DolphinTool required"), 560, 280);
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = P7("CubeShelf doit convertir le RVZ avant de préparer le jeu.", "CubeShelf must convert the RVZ before preparing the game."), FontSize = 21, FontWeight = Avalonia.Media.FontWeight.Bold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = P7("Télécharger automatiquement Dolphin 2606a depuis le site officiel, vérifier son SHA-256 puis utiliser DolphinTool ? L’image RVZ originale ne sera jamais supprimée.", "Automatically download Dolphin 2606a from the official site, verify its SHA-256, then use DolphinTool? The original RVZ image will never be deleted."), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    CreateDialogButton(P7("Télécharger et continuer", "Download and continue"), (_, _) => dialog.Close(true)),
                    CreateDialogButton(P7("Annuler", "Cancel"), (_, _) => dialog.Close(false))
                }
            };
            allowDolphinDownload = await dialog.ShowDialog<bool>(this);
            if (!allowDolphinDownload) return;
        }

        var item = _portableQueue.Enqueue(
            $"gamedata:{game.Id}",
            P7($"Données de jeu — {game.Title}", $"Game data — {game.Title}"),
            "GAME DATA",
            P7("Préparation transactionnelle ISO/GCM/RVZ", "Transactional ISO/GCM/RVZ preparation"),
            (progress, cancellationToken) =>
                _gameData.PrepareAsync(game.Id, executable, discImage, progress, cancellationToken, allowDolphinDownload),
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshGameState();
                    RefreshDolphinStatusPhase4Core();
                    ShowToastParity(P7("Données prêtes", "Game data ready"), P7("Les données sont prêtes. L’image ISO/GCM/RVZ originale est conservée.", "Game data is ready. The original ISO/GCM/RVZ image was preserved."));
                });
                return Task.CompletedTask;
            },
            referenceId: game.Id);

        if (item is null)
            ActionStatus.Text = P7("La préparation des données est déjà dans la file.", "Game data preparation is already queued.");
        else
        {
            _activeGameQueueItem = item;
            ActionStatus.Text = P7("Préparation ajoutée à la file de téléchargements.", "Preparation added to the download queue.");
        }
        RefreshPortableQueueView();
    }

    private async Task CheckLauncherUpdatePhase4Async(
        bool interactive,
        CancellationToken cancellationToken = default)
    {
        if (_launcherUpdateInstaller is null) return;
        if (interactive)
        {
            CheckLauncherUpdateButton.IsEnabled = false;
            LauncherUpdateStatusText.Text = P7("Vérification de la dernière version…", "Checking the latest version…");
        }

        try
        {
            var update = await _launcherUpdates.CheckAsync(cancellationToken: cancellationToken);
            MarkConnectivityHealthyPhase7();
            if (!update.Available)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    LauncherUpdateStatusText.Text = P7($"CubeShelf {update.CurrentVersion} est à jour.", $"CubeShelf {update.CurrentVersion} is up to date."));
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
                LauncherUpdateStatusText.Text = P7($"CubeShelf {update.LatestVersion} est disponible.", $"CubeShelf {update.LatestVersion} is available."));

            if (!interactive)
            {
                if (_lastLauncherUpdateNotice != update.LatestVersion)
                {
                    _lastLauncherUpdateNotice = update.LatestVersion;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        ShowToastParity(P7("Mise à jour CubeShelf", "CubeShelf update"), P7($"Version {update.LatestVersion} disponible.", $"Version {update.LatestVersion} is available.")));
                }
                return;
            }

            var dialog = CreatePhase7Dialog(P7("Mise à jour CubeShelf", "CubeShelf update"), 500, 250);
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = P7($"Installer CubeShelf {update.LatestVersion} ?", $"Install CubeShelf {update.LatestVersion}?"), FontSize = 21, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = P7("Le paquet sera téléchargé en HTTPS et vérifié avec le SHA-256 du manifeste avant toute installation.", "The package will be downloaded over HTTPS and verified against the manifest SHA-256 before installation."), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    CreateDialogButton(P7("Télécharger", "Download"), (_, _) => dialog.Close(true)),
                    CreateDialogButton(P7("Plus tard", "Later"), (_, _) => dialog.Close(false))
                }
            };
            if (!await dialog.ShowDialog<bool>(this)) return;

            var progress = new Progress<double>(value =>
                LauncherUpdateStatusText.Text = P7($"Téléchargement vérifié… {value:P0}", $"Verified download… {value:P0}"));
            var archive = await _launcherUpdates.DownloadAsync(update, progress, cancellationToken);
            var result = _launcherUpdateInstaller.Launch(archive, update.LatestVersion, Environment.ProcessId);
            LauncherUpdateStatusText.Text = result.Message;
            if (result.ShutdownRequired) Close();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                LauncherUpdateStatusText.Text = P7(
                    $"Mise à jour indisponible : {exception.Message}",
                    $"Update unavailable: {exception.Message}"));
            if (IsConnectivityFailurePhase7(exception))
                MarkConnectivityIssuePhase7(exception.Message);
            else
                MarkConnectivityHealthyPhase7();
        }
        finally
        {
            if (interactive)
                await Dispatcher.UIThread.InvokeAsync(() => CheckLauncherUpdateButton.IsEnabled = true);
        }
    }
    private async void PrepareGameDataPhase4(object? sender, RoutedEventArgs args) =>
        await QueueGameDataPreparationPhase4Async();

    private async void CheckLauncherUpdatePhase4(object? sender, RoutedEventArgs args) =>
        await CheckLauncherUpdatePhase4Async(interactive: true);

}
