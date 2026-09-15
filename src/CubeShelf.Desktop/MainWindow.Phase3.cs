using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;
using CubeShelf.Core.Updates;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private PortableDownloadQueue? _portableQueue;
    private GitHubGameUpdateService? _gameUpdateService;
    private readonly ConcurrentDictionary<string, GameUpdateSnapshot> _gameUpdateSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _phase3Lifetime;
    private PortableDownloadQueueItem? _activeGameQueueItem;

    private void InitializePhase3Parity()
    {
        _portableQueue = new PortableDownloadQueue(_downloads, SynchronizationContext.Current);
        _portableQueue.Changed += PortableQueueChanged;
        _gameUpdateService = new GitHubGameUpdateService(_paths);
        _phase3Lifetime = new CancellationTokenSource();
        RefreshPortableQueueView();

        if (_preferences.CheckGamesOnStartup)
            _ = RefreshAllGameUpdatesPhase3Async(showToast: false);

        _ = PeriodicGameUpdateLoopAsync(_phase3Lifetime.Token);
    }

    private void DisposePhase3Parity()
    {
        if (_portableQueue is not null)
            _portableQueue.Changed -= PortableQueueChanged;
        _phase3Lifetime?.Cancel();
        _phase3Lifetime?.Dispose();
        _phase3Lifetime = null;
        _portableQueue?.Dispose();
        _portableQueue = null;
        _gameUpdateService?.Dispose();
        _gameUpdateService = null;
    }

    private async Task PeriodicGameUpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
                await RefreshAllGameUpdatesPhase3Async(showToast: false, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void PortableQueueChanged(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(RefreshPortableQueueView);

    private void RefreshPortableQueueView()
    {
        if (_portableQueue is null || DownloadsList is null) return;
        var items = _portableQueue.Items;
        DownloadsList.ItemsSource = items;
        var pending = _portableQueue.PendingCount;
        var failed = items.Count(item => item.State is DownloadActivityState.Failed or DownloadActivityState.Interrupted);
        QueueSidebarText.Text = pending > 0
            ? pending == 1
                ? P7("1 opération en cours", "1 operation in progress")
                : P7($"{pending} opérations en cours", $"{pending} operations in progress")
            : failed > 0
                ? failed == 1
                    ? P7("1 opération en erreur", "1 failed operation")
                    : P7($"{failed} opérations en erreur", $"{failed} failed operations")
                : P7("Aucune opération", "No operation");
    }

    private void ClearPortableDownloads(object? sender, RoutedEventArgs args)
    {
        _portableQueue?.ClearFinished();
        RefreshPortableQueueView();
    }

    private void CancelPortableDownload(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: PortableDownloadQueueItem item })
            _portableQueue?.Cancel(item);
    }

    private void RetryPortableDownload(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: PortableDownloadQueueItem item })
            _portableQueue?.Retry(item);
    }

    private void RemovePortableDownload(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: PortableDownloadQueueItem item })
            _portableQueue?.Remove(item);
    }

    private void CancelActiveGameQueuePhase3()
    {
        if (_activeGameQueueItem is not null)
            _portableQueue?.Cancel(_activeGameQueueItem);
    }

    private PortableDownloadQueueItem? EnqueueRuntimeInstallationPhase3(
        bool repair,
        GameCatalogEntry? targetGame = null)
    {
        var game = targetGame ?? _selectedGame;
        if (game is null || _portableQueue is null || _gameUpdateService is null) return null;
        CubeShelf.Core.Releases.PartyBoardInstallResult? result = null;
        PartyBoardReleaseSnapshot release = new(false, "", "", "");

        var item = _portableQueue.Enqueue(
            $"runtime:{game.Id}",
            $"{game.RuntimeName} — {game.Title}",
            "RUNTIME",
            repair ? P7("Réparation vérifiée du runtime", "Verified runtime repair") : P7("Installation / mise à jour vérifiée du runtime", "Verified runtime installation / update"),
            async (progress, cancellationToken) =>
            {
                release = await _gameUpdateService.GetRuntimeReleaseAsync(game, cancellationToken);
                var source = CubeShelf.Core.Releases.GameRuntimeSource.FromCatalog(game);
                result = repair
                    ? await _installer.RepairLatestAsync(source, game.Id, progress, cancellationToken)
                    : await _installer.InstallLatestAsync(source, game.Id, progress, cancellationToken);
                if (release.Available &&
                    string.Equals(release.Version, result.Version, StringComparison.OrdinalIgnoreCase))
                {
                    _gameUpdateService.RecordInstalledRuntime(game, release);
                }
                else
                {
                    var installedRelease = await _gameUpdateService.GetRuntimeReleaseAsync(game, cancellationToken);
                    if (installedRelease.Available &&
                        string.Equals(installedRelease.Version, result.Version, StringComparison.OrdinalIgnoreCase))
                        _gameUpdateService.RecordInstalledRuntime(game, installedRelease);
                }
            },
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (result is not null)
                    {
                        game.Executable = result.ExecutablePath;
                        RestorePreparedGameDataPhase6(game, result.ExecutablePath);
                        if (_selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            PersistSelection();
                            RefreshGameState();
                            ShowToastParity(
                                P7($"{game.RuntimeName} prêt", $"{game.RuntimeName} ready"),
                                result.Verified
                                    ? P7($"Version {result.Version} installée et vérifiée.",
                                         $"Version {result.Version} installed and verified.")
                                    : P7($"Version {result.Version} installée sans vérification : l’éditeur ne publie aucun checksum.",
                                         $"Version {result.Version} installed unverified: the publisher ships no checksum."));
                        }
                        _ = RefreshGameUpdatePhase3Async(game, forceUi: true);
                    }
                });
                return Task.CompletedTask;
            },
            referenceId: game.Id);

        if (item is null)
        {
            ActionStatus.Text = P7($"Cette opération {game.RuntimeName} est déjà dans la file.", $"This {game.RuntimeName} operation is already queued.");
            return null;
        }

        _activeGameQueueItem = item;
        ActionStatus.Text = repair
            ? P7("Réparation ajoutée à la file de téléchargements.", "Repair added to the download queue.")
            : P7("Installation ajoutée à la file de téléchargements.", "Installation added to the download queue.");
        ShowToastParity(P7("Téléchargements", "Downloads"), ActionStatus.Text);
        RefreshPortableQueueView();
        return item;
    }

    private PortableDownloadQueueItem? EnqueueGameDataPreparationPhase3(GameCatalogEntry? targetGame = null)
    {
        var game = targetGame ?? _selectedGame;
        if (game is null || _portableQueue is null) return null;
        HydrateGamePathsPhase3(game);
        var executable = game.Executable;
        var discImage = game.DiscImage;

        var item = _portableQueue.Enqueue(
            $"gamedata:{game.Id}",
            P7($"Données de jeu — {game.Title}", $"Game data — {game.Title}"),
            "GAME DATA",
            P7("Préparation transactionnelle ISO/GCM/RVZ", "Transactional ISO/GCM/RVZ preparation"),
            (progress, cancellationToken) =>
                _gameData.PrepareAsync(game.Id, executable, discImage, game.SupportedDiscIds, progress, cancellationToken),
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        RefreshGameState();
                        ShowToastParity(P7("Données prêtes", "Game data ready"), P7("Les données de jeu ont été préparées. L’image originale est conservée.", "Game data was prepared. The original image was preserved."));
                    }
                });
                return Task.CompletedTask;
            },
            referenceId: game.Id);

        if (item is null)
        {
            ActionStatus.Text = P7("La préparation des données est déjà dans la file.", "Game data preparation is already queued.");
            return null;
        }

        _activeGameQueueItem = item;
        ActionStatus.Text = P7("Préparation ajoutée à la file de téléchargements.", "Preparation added to the download queue.");
        ShowToastParity(P7("Préparation", "Preparation"), ActionStatus.Text);
        RefreshPortableQueueView();
        return item;
    }

    private void HydrateGamePathsPhase3(GameCatalogEntry game)
    {
        var saved = _profileStore.Load(game.Id);
        if (saved is not null)
        {
            game.Executable = saved.Executable;
            game.DiscImage = saved.DiscImage;
        }

        var managedRuntime = _installer.GetStatus(game.Id);
        if (managedRuntime.IsInstalled)
            game.Executable = managedRuntime.ExecutablePath;

        game.Executable = ResolveApplicationPath(game.Executable);
        game.DiscImage = ResolveApplicationPath(game.DiscImage);
    }

    private void ResumePortableDownload(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: PortableDownloadQueueItem item } ||
            _portableQueue is null || !item.CanResume) return;

        var game = _catalogGames.FirstOrDefault(candidate =>
            candidate.Id.Equals(item.ReferenceId, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            ShowToastParity(P7("Reprise impossible", "Unable to resume"), P7("Le jeu associé à cette opération n’est plus dans la bibliothèque.", "The game associated with this operation is no longer in the library."));
            return;
        }

        PortableDownloadQueueItem? replacement = null;
        if (item.Kind.Equals("RUNTIME", StringComparison.OrdinalIgnoreCase))
            replacement = EnqueueRuntimeInstallationPhase3(repair: false, game);
        else if (item.Kind.Equals("GAME DATA", StringComparison.OrdinalIgnoreCase))
            replacement = EnqueueGameDataPreparationPhase3(game);

        if (replacement is null) return;
        _portableQueue.Remove(item);
        RefreshPortableQueueView();
    }

    private async Task QueueModInstallationPhase3Async()
    {
        if (_selectedMod?.Remote is null || _modManager is null || _portableQueue is null) return;
        var selected = _selectedMod;
        var manager = _modManager;

        ModInstallPlan plan;
        try
        {
            ModsStatus.Text = P7("Analyse des dépendances…", "Analyzing dependencies…");
            plan = await manager.PlanInstallAsync(selected.Remote, _gameBanana);
        }
        catch (Exception exception)
        {
            ModsStatus.Text = P7($"Plan impossible : {exception.Message}", $"Unable to create install plan: {exception.Message}");
            return;
        }

        if (!await ConfirmModPlanAsync(plan))
        {
            ModsStatus.Text = P7("Installation annulée avant toute modification.", "Installation cancelled before any changes were made.");
            return;
        }

        var item = _portableQueue.Enqueue(
            $"mod:{plan.RootModId}",
            selected.Name,
            "MOD",
            P7($"Installation transactionnelle • {plan.Packages.Count} paquet(s)", $"Transactional installation • {plan.Packages.Count} package(s)"),
            async (progress, cancellationToken) =>
            {
                await manager.InstallPlanAsync(plan, allowIncompatible: true, progress, cancellationToken);
            },
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ModsStatus.Text = P7($"{plan.Packages.Count} mod(s) installé(s) et activé(s).", $"{plan.Packages.Count} mod(s) installed and enabled.");
                    RefreshModItems();
                    ShowToastParity("Mods", ModsStatus.Text);
                });
                return Task.CompletedTask;
            },
            referenceId: $"{plan.GameId}:{plan.RootModId}");

        if (item is null)
        {
            ModsStatus.Text = P7("Ce mod est déjà dans la file.", "This mod is already queued.");
            return;
        }

        ModsStatus.Text = P7("Installation ajoutée à la file de téléchargements.", "Installation added to the download queue.");
        RefreshPortableQueueView();
    }

    private async void CheckGameUpdatePhase3(object? sender, RoutedEventArgs args) =>
        await RefreshSelectedGameUpdatePhase3Async(showToast: true);

    private async Task RefreshSelectedGameUpdatePhase3Async(bool showToast = false)
    {
        if (_selectedGame is null) return;
        await RefreshGameUpdatePhase3Async(_selectedGame, forceUi: true, showToast: showToast);
    }

    private async Task RefreshAllGameUpdatesPhase3Async(
        bool showToast,
        CancellationToken cancellationToken = default)
    {
        if (_gameUpdateService is null) return;
        var runtimeUpdates = 0;
        var pendingBuilds = 0;
        var failedChecks = 0;

        foreach (var game in _catalogGames.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var runtime = _installer.GetStatus(game.Id);
                var snapshot = await _gameUpdateService.CheckAsync(
                    game,
                    runtime.IsInstalled,
                    runtime.Version,
                    cancellationToken);
                _gameUpdateSnapshots[game.Id] = snapshot;
                if (snapshot.RuntimeUpdateAvailable) runtimeUpdates++;
                if (snapshot.RuntimeUpdatePendingBuild) pendingBuilds++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                if (IsConnectivityFailurePhase7(exception))
                {
                    failedChecks++;
                    MarkConnectivityIssuePhase7(exception.Message);
                }
                // A malformed/unsupported remote manifest is a release problem,
                // not an offline state. Keep the local launcher usable without
                // incorrectly switching the whole UI to “Mode local”.
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RefreshLibraryParity();
            if (_selectedGame is not null && _gameUpdateSnapshots.TryGetValue(_selectedGame.Id, out var selected))
                ApplyGameUpdateSnapshotPhase3(selected);

            LibraryUpdatesText.Text = runtimeUpdates > 0
                ? P7($"{runtimeUpdates} mise(s) à jour", $"{runtimeUpdates} update(s)")
                : pendingBuilds > 0
                    ? P7($"{pendingBuilds} build(s) en attente", $"{pendingBuilds} build(s) pending")
                    : P7($"{_catalogGames.Count} jeu(x) • à jour", $"{_catalogGames.Count} game(s) • up to date");

            if (failedChecks == 0) MarkConnectivityHealthyPhase7();

            if (showToast)
                ShowToastParity(P7("Mises à jour", "Updates"), runtimeUpdates > 0
                    ? P7($"{runtimeUpdates} mise(s) à jour PartyBoard disponible(s).", $"{runtimeUpdates} PartyBoard update(s) available.")
                    : pendingBuilds > 0
                        ? P7("Le code source est plus récent mais le runtime public est encore en construction.", "The source is newer but the public runtime is still being built.")
                        : P7("Aucune mise à jour PartyBoard disponible.", "No PartyBoard update available."));
        });
    }

    private async Task RefreshGameUpdatePhase3Async(
        GameCatalogEntry game,
        bool forceUi,
        bool showToast = false,
        CancellationToken cancellationToken = default)
    {
        if (_gameUpdateService is null) return;
        try
        {
            if (forceUi && _selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
            {
                GitHubUpdateStatusText.Text = P7("Vérification GitHub et release PartyBoard…", "Checking GitHub and PartyBoard release…");
                CheckGameUpdateButton.IsEnabled = false;
            }

            var runtime = _installer.GetStatus(game.Id);
            var snapshot = await _gameUpdateService.CheckAsync(
                game,
                runtime.IsInstalled,
                runtime.Version,
                cancellationToken);
            _gameUpdateSnapshots[game.Id] = snapshot;
            MarkConnectivityHealthyPhase7();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshLibraryParity();
                if (_selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
                    ApplyGameUpdateSnapshotPhase3(snapshot);
                if (showToast)
                    ShowToastParity(P7("Mises à jour", "Updates"), snapshot.RuntimeUpdateAvailable
                        ? P7("Une nouvelle version jouable de PartyBoard est disponible.", "A new playable PartyBoard build is available.")
                        : snapshot.RuntimeUpdatePendingBuild
                            ? P7("Le code est plus récent, mais le build PartyBoard public n’est pas encore publié.", "The source is newer, but the public PartyBoard build is not published yet.")
                            : P7("PartyBoard est à jour.", "PartyBoard is up to date."));
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
                    GitHubUpdateStatusText.Text = P7($"Vérification indisponible : {exception.Message}", $"Check unavailable: {exception.Message}");
                if (IsConnectivityFailurePhase7(exception))
                    MarkConnectivityIssuePhase7(exception.Message);
                else
                    MarkConnectivityHealthyPhase7();
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_selectedGame?.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase) == true)
                    CheckGameUpdateButton.IsEnabled = true;
            });
        }
    }

    private void ApplyGameUpdateSnapshotPhase3(GameUpdateSnapshot snapshot)
    {
        var source = snapshot.LatestSource;
        var release = snapshot.RuntimeRelease;
        var local = snapshot.LocalSource;

        GameUpdateStateText.Text = snapshot.RuntimeUpdateAvailable
            ? P7("⬇ Mise à jour PartyBoard disponible", "⬇ PartyBoard update available")
            : snapshot.RuntimeUpdatePendingBuild
                ? P7("◷ Nouveau code • build en attente", "◷ New source • build pending")
                : release.Available
                    ? P7("✓ PartyBoard à jour", "✓ PartyBoard up to date")
                    : P7("○ Aucun runtime public détecté", "○ No public runtime detected");

        var latestSha = ShortSha(source?.Sha);
        var releaseSha = ShortSha(release.Commit);
        var localSha = ShortSha(local.Commit);
        GitHubCommitText.Text =
            $"Source : {(latestSha.Length > 0 ? latestSha : "—")}   •   " +
            $"Runtime public : {(releaseSha.Length > 0 ? releaseSha : release.Version.Length > 0 ? release.Version : "—")}   •   " +
            P7($"Source locale : {(localSha.Length > 0 ? localSha : local.Present ? "présente" : "absente")}", $"Local source: {(localSha.Length > 0 ? localSha : local.Present ? "present" : "missing")}");

        GitHubUpdateStatusText.Text = snapshot.RuntimeUpdateAvailable
            ? P7("Une release PartyBoard jouable plus récente est disponible.", "A newer playable PartyBoard release is available.")
            : snapshot.RuntimeUpdatePendingBuild
                ? P7("Le dépôt contient du code plus récent que la dernière release jouable. CubeShelf n’installera pas un build inexistant.", "The repository contains newer code than the latest playable release. CubeShelf will not install a build that does not exist.")
                : snapshot.SourceUpdateAvailable
                    ? P7("La source locale peut être actualisée; le runtime jouable est déjà à jour.", "The local source can be updated; the playable runtime is already current.")
                    : P7("Runtime et source publiés vérifiés.", "Published runtime and source verified.");

        GitHubChangesText.Text = snapshot.Changes.Count == 0
            ? P7("Aucun changement récent à afficher.", "No recent changes to display.")
            : string.Join("\n", snapshot.Changes.Take(8).Select(change =>
                $"• {ShortSha(change.Sha)}  {change.Message}"));

        DownloadSourceButton.Content = local.Present
            ? snapshot.SourceUpdateAvailable ? P7("Mettre à jour la source", "Update source") : P7("Source à jour", "Source up to date")
            : P7("Télécharger la source", "Download source");
        DeleteSourceButton.IsVisible = local.Present && local.ManagedByCubeShelf;
        SourceSafetyText.Text = local.Present && !local.ManagedByCubeShelf
            ? P7("Dépôt externe détecté : CubeShelf ne le supprimera jamais automatiquement.", "External repository detected: CubeShelf will never delete it automatically.")
            : local.Present
                ? P7("Source gérée par CubeShelf.", "Source managed by CubeShelf.")
                : P7("La source est optionnelle : elle n’est pas nécessaire pour jouer.", "Source code is optional: it is not required to play.");
    }

    private void DownloadGameSourcePhase3(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null || _portableQueue is null || _gameUpdateService is null) return;
        var game = _selectedGame;
        var item = _portableQueue.Enqueue(
            $"source:{game.Id}",
            $"Source — {game.Title}",
            "SOURCE",
            $"{game.GitHubOwner}/{game.GitHubRepo} • {game.GitHubBranch}",
            async (progress, cancellationToken) =>
            {
                _ = await _gameUpdateService.DownloadLatestSourceAsync(game, progress, cancellationToken);
            },
            after: () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ShowToastParity(P7("Source GitHub", "GitHub source"), P7("Source téléchargée dans l’espace géré CubeShelf.", "Source downloaded into CubeShelf-managed storage."));
                    _ = RefreshGameUpdatePhase3Async(game, forceUi: true);
                });
                return Task.CompletedTask;
            },
            referenceId: game.Id);

        if (item is null)
        {
            GitHubUpdateStatusText.Text = P7("Le téléchargement de cette source est déjà dans la file.", "This source download is already queued.");
            return;
        }
        GitHubUpdateStatusText.Text = P7("Source ajoutée à la file de téléchargements.", "Source added to the download queue.");
        RefreshPortableQueueView();
    }

    private void DeleteGameSourcePhase3(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null || _gameUpdateService is null) return;
        try
        {
            if (_gameUpdateService.DeleteManagedSource(_selectedGame))
            {
                ShowToastParity(P7("Source supprimée", "Source removed"), P7("Seule la copie gérée par CubeShelf a été supprimée.", "Only the CubeShelf-managed copy was removed."));
                _ = RefreshSelectedGameUpdatePhase3Async();
            }
        }
        catch (Exception exception)
        {
            GitHubUpdateStatusText.Text = exception.Message;
        }
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "" : sha[..Math.Min(8, sha.Length)];
}
