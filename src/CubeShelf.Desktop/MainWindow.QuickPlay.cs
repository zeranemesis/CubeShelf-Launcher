using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CubeShelf.Core.Downloads;
using CubeShelf.Core.Games;
using CubeShelf.Core.Library;
using CubeShelf.Core.Mods;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly GamePlayFlow _quickPlay = new();
    private readonly Dictionary<string, PlayIntent> _playIntents = new(StringComparer.OrdinalIgnoreCase);
    private bool _quickPlayClosing;

    private string PlayKey(GameCatalogEntry game) => GameVariants.BaseOf(_catalogGames, game).Id;
    private PlayIntent? PlayIntentFor(GameCatalogEntry game) => _playIntents.GetValueOrDefault(PlayKey(game));
    private GameCatalogEntry? RunningGameFor(GameCatalogEntry game) => _catalogGames.FirstOrDefault(candidate =>
        _sessions.IsRunning(candidate.Id) && PlayKey(candidate).Equals(PlayKey(game), StringComparison.OrdinalIgnoreCase));

    private async void QuickPlayLibraryGame(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: DesktopGameCard card })
            await RequestPlayAsync(GameVariants.Resolve(_catalogGames, card.Game, _paths));
    }

    private Task RequestPlayAsync(GameCatalogEntry game) => PrepareForPlayAsync(game, null);

    private async Task PrepareForPlayAsync(GameCatalogEntry game, Func<Task>? readyAction)
    {
        if (_quickPlayClosing || _portableQueue is null) return;
        var key = PlayKey(game);
        if (_playIntents.ContainsKey(key)) return;
        if (RunningGameFor(game) is { } running)
        {
            if (readyAction is not null) { await readyAction(); return; }
            try { await _sessions.StopAsync(running.Id); }
            catch (Exception ex) { ShowToastParity(P7("Jeu", "Game"), ex.Message); }
            RefreshQuickPlayUi();
            return;
        }

        var intent = new PlayIntent(game, P7("Préparation…", "Getting ready…"), readyAction is not null);
        _playIntents.Add(key, intent);
        RefreshQuickPlayUi();
        try
        {
            await _quickPlay.RunAsync(key, new GamePlayActions(
                ct => EnsurePlayDiscAsync(intent, ct),
                ct => EnsurePlayRuntimeAsync(intent, ct),
                ct => EnsurePlayDataAsync(intent, ct),
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!CanPlay(game)) throw new InvalidOperationException(P7(
                        "Le jeu n’est plus prêt. Vérifie son image dans les options avancées.",
                        "The game is no longer ready. Check its disc image in advanced options."));
                    if (readyAction is not null) return readyAction();
                    LaunchReadyGame(game);
                    return Task.CompletedTask;
                }), intent.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_quickPlayClosing)
                ShowToastParity(game.Title, P7("Lancement annulé. Les téléchargements restent accessibles dans leur onglet.",
                    "Launch cancelled. Downloads remain available in their tab."));
        }
        catch (Exception ex)
        {
            if (!_quickPlayClosing)
            {
                var message = P7($"Impossible de lancer {game.Title} : {ex.Message}", $"Could not start {game.Title}: {ex.Message}");
                if (_selectedGame?.Id == game.Id) ActionStatus.Text = message;
                ShowToastParity(P7("Préparation du jeu", "Game setup"), message);
            }
        }
        finally
        {
            _playIntents.Remove(key);
            intent.Cancellation.Dispose();
            if (!_quickPlayClosing)
            {
                RefreshQuickPlayUi();
                RefreshLibraryParity();
            }
        }
    }

    private async Task<bool> EnsurePlayDiscAsync(PlayIntent intent, CancellationToken ct)
    {
        var game = intent.Game;
        HydrateGamePathsPhase3(game);
        if (DiscImageService.Inspect(game.DiscImage, game).Supported) return true;
        SetPlayStatus(intent, P7("Choisir le fichier du jeu…", "Choose the game file…"));
        var picked = await PickGameDiscAsync(game);
        ct.ThrowIfCancellationRequested();
        return picked;
    }

    private async Task<bool> PickGameDiscAsync(GameCatalogEntry game)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = game.DiscPatch is { SourceDescription.Length: > 0 } patch
                ? P7($"{game.Title} : choisir {patch.SourceDescription}", $"{game.Title}: choose {patch.SourceDescription}")
                : P7($"Choisir le fichier de {game.Title}", $"Choose your {game.Title} file"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(P7("Images GameCube", "GameCube images"))
                { Patterns = game.DiscFormats.Select(format => "*" + format).ToArray() } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return false;
        var compatibility = DiscImageService.Inspect(path, game, UiLocalization.IsEnglish(_preferences.Language));
        if (!compatibility.Supported)
        {
            ShowToastParity(P7("Image incompatible", "Incompatible disc image"), compatibility.Message);
            return false;
        }
        game.DiscImage = Path.GetFullPath(path);
        SavePlayPaths(game);
        if (_selectedGame?.Id == game.Id) RefreshGameState();
        return true;
    }

    private async Task EnsurePlayRuntimeAsync(PlayIntent intent, CancellationToken ct)
    {
        var game = intent.Game;
        HydrateGamePathsPhase3(game);
        if (File.Exists(game.Executable)) return;
        SetPlayStatus(intent, P7("Installation avant lancement…", "Installing before launch…"));
        if (string.IsNullOrWhiteSpace(game.GitHubOwner) || string.IsNullOrWhiteSpace(game.GitHubRepo))
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = P7($"Choisir le programme de {game.Title}", $"Choose the program for {game.Title}"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(P7("Programme du jeu", "Game program"))
                    { Patterns = OperatingSystem.IsWindows() ? new[] { "*.exe" } : new[] { "*" } } }
            });
            ct.ThrowIfCancellationRequested();
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) throw new OperationCanceledException(ct);
            game.Executable = Path.GetFullPath(path);
        }
        else
        {
            var item = PendingPlayPrerequisite($"runtime:{game.Id}")
                ?? EnqueueRuntimeInstallationPhase3(repair: false, targetGame: game);
            await AwaitPlayPrerequisiteAsync(item, ct);
            var runtime = _installer.GetStatus(game.Id);
            if (!runtime.IsInstalled) throw new InvalidOperationException(P7("L’installation est incomplète.", "Installation is incomplete."));
            game.Executable = runtime.ExecutablePath;
            RestorePreparedGameDataPhase6(game, game.Executable);
        }
        SavePlayPaths(game);
    }

    private async Task EnsurePlayDataAsync(PlayIntent intent, CancellationToken ct)
    {
        var game = intent.Game;
        if (game.DataPreparation == GameDataPreparation.Runtime || _gameData.IsPrepared(game, game.Executable)) return;
        if (game.DataPreparation == GameDataPreparation.DiscPatch &&
            (game.DiscPatch is null || DiscPatchSource.FindDelta(_paths, game.ModLibraryId, game.DiscPatch) is null))
            throw new InvalidOperationException(P7("Réinstalle le mod depuis l’onglet Mods pour retrouver son patch.",
                "Reinstall the mod from the Mods tab to restore its patch."));

        SetPlayStatus(intent, P7("Préparation du jeu…", "Preparing the game…"));
        var rvz = Path.GetExtension(game.DiscImage).Equals(".rvz", StringComparison.OrdinalIgnoreCase);
        if (rvz && _dolphinToolService is { } tools && !tools.IsAvailable() && !tools.GetStatus().AutoInstallSupported)
            throw new InvalidOperationException(P7("Installe Dolphin Emulator pour préparer cette image RVZ sur ta plateforme.",
                "Install Dolphin Emulator to prepare this RVZ on your platform."));
        var executable = game.Executable;
        var disc = game.DiscImage;
        var item = PendingPlayPrerequisite($"gamedata:{game.Id}") ?? _portableQueue!.Enqueue(
            $"gamedata:{game.Id}", game.Title, "GAME DATA",
            P7("Préparation automatique avant de jouer", "Automatic preparation before playing"),
            (progress, token) => PrepareGameDataAsync(game, executable, disc, progress, token, allowDolphinDownload: true),
            referenceId: game.Id);
        await AwaitPlayPrerequisiteAsync(item, ct);
        if (!_gameData.IsPrepared(game, game.Executable))
            throw new InvalidOperationException(P7("Les données préparées sont introuvables.", "Prepared game data is missing."));
    }

    private PortableDownloadQueueItem? PendingPlayPrerequisite(string key) => _portableQueue?.Items.FirstOrDefault(item =>
        !item.IsTerminal && item.UniqueKey.Equals(key, StringComparison.OrdinalIgnoreCase));

    private async Task AwaitPlayPrerequisiteAsync(PortableDownloadQueueItem? item, CancellationToken ct)
    {
        if (item is null || _portableQueue is null) throw new InvalidOperationException(P7("Impossible de préparer ce jeu.", "Could not prepare this game."));
        var state = await _portableQueue.WaitForCompletionAsync(item, ct);
        if (state == DownloadActivityState.Cancelled) throw new OperationCanceledException(ct);
        if (state != DownloadActivityState.Completed) throw new InvalidOperationException(item.Status);
    }

    private void SavePlayPaths(GameCatalogEntry game)
    {
        _profileStore.Save(new GameProfileEntry(game.Id, game.Executable, game.DiscImage));
        PersistCatalog();
    }

    private void SetPlayStatus(PlayIntent intent, string text)
    {
        intent.Status = text;
        RefreshQuickPlayUi();
    }

    private string QuickPlayLabel(GameCatalogEntry game)
    {
        if (RunningGameFor(game) is not null) return P7("■ Arrêter", "■ Stop");
        if (PlayIntentFor(game) is { } intent) return intent.Status;
        if (!DiscImageService.Inspect(game.DiscImage, game).Supported) return P7("▶ Configurer et jouer", "▶ Set up and play");
        var runtime = _installer.GetStatus(game.Id);
        if (!runtime.IsInstalled && !File.Exists(ResolveApplicationPath(game.Executable)))
            return P7("↓ Installer et jouer", "↓ Install and play");
        if (game.DataPreparation != GameDataPreparation.Runtime && !_gameData.IsPrepared(game, game.Executable))
            return P7("▶ Préparer et jouer", "▶ Prepare and play");
        return P7("▶ Jouer", "▶ Play");
    }

    private string QuickPlayHint(GameCatalogEntry game)
    {
        if (RunningGameFor(game) is not null) return P7("En cours de jeu", "Playing now");
        if (PlayIntentFor(game) is { } intent) return intent.Status + (intent.OpensOnline
            ? P7(" Les salons s’ouvriront automatiquement.", " The room list will open automatically.")
            : P7(" Le jeu se lancera automatiquement.", " The game will start automatically."));
        if (!DiscImageService.Inspect(game.DiscImage, game).Supported)
            return P7("Choisis ton fichier de jeu une fois. CubeShelf s’occupe de la suite.", "Choose your game file once. CubeShelf handles the rest.");
        if (!CanPlay(game))
            return P7("Les composants manquants seront installés avant le lancement.", "Missing components will be installed before launch.");
        return game.DataPreparation != GameDataPreparation.Runtime && !_gameData.IsPrepared(game, game.Executable)
            ? P7("La préparation se fera automatiquement au premier lancement.", "First-time preparation will run automatically.")
            : P7("Prêt à jouer", "Ready to play");
    }

    private void RefreshQuickPlayUi()
    {
        if (_quickPlayClosing) return;
        foreach (var card in _parityCards) ApplyQuickPlayCard(card);
        RefreshBigPictureStatus();
        RefreshBigPictureResumeStatus();
        if (_selectedGame is not { } game) return;
        var busy = PlayIntentFor(game) is not null;
        PlayButton.Content = QuickPlayLabel(game);
        PlayButton.IsEnabled = !busy;
        CancelPlayLaunchButton.IsVisible = busy;
        SetupProgressText.Text = QuickPlayHint(game);
    }

    private void ApplyQuickPlayCard(DesktopGameCard card)
    {
        var game = GameVariants.Resolve(_catalogGames, card.Game, _paths);
        HydrateGamePathsPhase3(game);
        card.UpdatePlay(QuickPlayLabel(game), QuickPlayHint(game), PlayIntentFor(game) is null);
    }

    private void CancelQuickPlay(object? sender, RoutedEventArgs args)
    {
        var game = sender is Button { Tag: DesktopGameCard card }
            ? GameVariants.Resolve(_catalogGames, card.Game, _paths) : _selectedGame;
        if (game is not null) PlayIntentFor(game)?.Cancellation.Cancel();
    }

    private void DisposeQuickPlay()
    {
        _quickPlayClosing = true;
        foreach (var intent in _playIntents.Values) intent.Cancellation.Cancel();
    }

    private sealed class PlayIntent(GameCatalogEntry game, string status, bool opensOnline = false)
    {
        public bool OpensOnline { get; } = opensOnline;
        public GameCatalogEntry Game { get; } = game;
        public string Status { get; set; } = status;
        public CancellationTokenSource Cancellation { get; } = new();
    }
}
