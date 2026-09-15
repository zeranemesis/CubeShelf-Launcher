using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CubeShelf.Core.Library;
using CubeShelf.Core.Platform;
using CubeShelf.Core.Releases;
using CubeShelf.Core.Mods;
using System.Runtime.InteropServices;
using System.Text;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private readonly List<DesktopGameCard> _parityCards = new();
    private bool _favoritesOnly;
    private bool _wizardShown;
    private CancellationTokenSource? _toastCancellation;

    private async void OnParityWindowOpened(object? sender, EventArgs args)
    {
        if (LibrarySortCombo.SelectedIndex < 0)
            LibrarySortCombo.SelectedIndex = 1;

        RefreshLibraryParity();
        RefreshParityGameDetails();
        RefreshStorageParity();
        UpdateResponsiveParity();

        if (!_wizardShown && !_preferences.FirstRunCompleted)
        {
            _wizardShown = true;
            await ShowFirstRunWizardAsync();
        }
    }

    private void OnParityWindowSizeChanged(object? sender, SizeChangedEventArgs args) =>
        UpdateResponsiveParity();

    private void UpdateResponsiveParity()
    {
        if (GameCase is null || SelectedTitleText is null) return;
        var width = Bounds.Width;
        if (width < 1120)
        {
            GameCase.Width = 300;
            GameCase.Height = 375;
            SelectedTitleText.FontSize = 36;
        }
        else if (width < 1360)
        {
            GameCase.Width = 380;
            GameCase.Height = 475;
            SelectedTitleText.FontSize = 44;
        }
        else
        {
            GameCase.Width = 465;
            GameCase.Height = 580;
            SelectedTitleText.FontSize = 54;
        }
    }

    private void ParityShowLibrary(object? sender, RoutedEventArgs args)
    {
        _favoritesOnly = false;
        LibrarySectionTitle.Text = UiLocalization.Get(_preferences.Language, "LibraryTitle");
        RefreshLibraryParity();
        ShowParityView(LibraryView);
    }

    private void ParityShowFavorites(object? sender, RoutedEventArgs args)
    {
        _favoritesOnly = true;
        LibrarySectionTitle.Text = UiLocalization.Get(_preferences.Language, "FavoritesTitle");
        RefreshLibraryParity();
        ShowParityView(LibraryView);
    }

    private void ParityOpenMods(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null)
        {
            ParityShowLibrary(sender, args);
            return;
        }
        ShowMods(sender, args);
        GameView.IsVisible = false;
    }

    private void ParityShowDownloads(object? sender, RoutedEventArgs args)
    {
        ShowDownloads(sender, args);
        GameView.IsVisible = false;
    }

    private void ParityShowSettings(object? sender, RoutedEventArgs args)
    {
        ShowSettings(sender, args);
        GameView.IsVisible = false;
        RefreshStorageParity();
    }

    private void ParityBackToGame(object? sender, RoutedEventArgs args)
    {
        if (_selectedGame is null)
        {
            ParityShowLibrary(sender, args);
            return;
        }
        ShowParityView(GameView);
        RefreshParityGameDetails();
    }

    private void ShowParityView(Control view)
    {
        LibraryView.IsVisible = ReferenceEquals(view, LibraryView);
        GameView.IsVisible = ReferenceEquals(view, GameView);
        ModsView.IsVisible = ReferenceEquals(view, ModsView);
        DownloadsView.IsVisible = ReferenceEquals(view, DownloadsView);
        SettingsView.IsVisible = ReferenceEquals(view, SettingsView);
    }

    private void LibrarySearchChanged(object? sender, TextChangedEventArgs args) => RefreshLibraryParity();
    private void LibrarySortChanged(object? sender, SelectionChangedEventArgs args) => RefreshLibraryParity();

    private void RefreshLibraryParity()
    {
        if (LibraryCards is null) return;
        foreach (var card in _parityCards) card.Dispose();
        _parityCards.Clear();

        IEnumerable<GameCatalogEntry> query = _catalogGames;
        if (_favoritesOnly) query = query.Where(game => game.IsFavorite);

        var search = LibrarySearchBox?.Text?.Trim() ?? "";
        if (search.Length > 0)
        {
            query = query.Where(game =>
                game.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                game.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                game.Genre.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        query = (LibrarySortCombo?.SelectedIndex ?? 1) switch
        {
            0 => query.OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => query.OrderByDescending(game => game.TotalPlaySeconds)
                      .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(game => game.LastPlayedAt ?? DateTimeOffset.MinValue)
                      .ThenBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        foreach (var game in query)
            _parityCards.Add(BuildCard(game));

        LibraryCards.ItemsSource = _parityCards.ToArray();
        var english = UiLocalization.IsEnglish(_preferences.Language);
        LibraryUpdatesText.Text = _favoritesOnly
            ? english ? $"{_parityCards.Count} favorite(s)" : $"{_parityCards.Count} favori(s)"
            : english ? $"{_parityCards.Count} game(s)" : $"{_parityCards.Count} jeu(x)";
        EmptyLibraryText.IsVisible = _parityCards.Count == 0;
    }

    private DesktopGameCard BuildCard(GameCatalogEntry game)
    {
        var coverPath = ResolveCoverPath(game.Covers.FirstOrDefault()?.Front);
        var runtime = _installer.GetStatus(game.Id);
        var disc = ResolveApplicationPath(game.DiscImage);
        var executable = runtime.IsInstalled ? runtime.ExecutablePath : ResolveApplicationPath(game.Executable);
        var prepared = File.Exists(executable) && _gameData.IsPrepared(game.Id, executable);
        var english = UiLocalization.IsEnglish(_preferences.Language);
        var last = game.LastPlayedAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? (english ? "Never" : "Jamais");
        var update = _gameUpdateSnapshots.TryGetValue(game.Id, out var snapshot)
            ? snapshot.RuntimeUpdateAvailable
                ? (english ? "⬇ PartyBoard update" : "⬇ Mise à jour PartyBoard")
                : snapshot.RuntimeUpdatePendingBuild
                    ? (english ? "◷ Build pending" : "◷ Build en attente")
                    : snapshot.SourceUpdateAvailable
                        ? (english ? "↻ Newer source" : "↻ Source plus récente")
                        : (english ? "✓ Up to date" : "✓ À jour")
            : "";
        return new DesktopGameCard(
            game,
            LoadBitmap(coverPath),
            game.IsFavorite ? "★" : "",
            File.Exists(disc) ? "✓ ISO/RVZ" : "○ ISO/RVZ",
            runtime.IsInstalled || File.Exists(executable) ? "✓ PartyBoard" : "○ PartyBoard",
            prepared ? (english ? "✓ Data" : "✓ Données") : (english ? "○ Data" : "○ Données"),
            english
                ? $"{game.PlayCount} launches • {FormatPlayTime(game.TotalPlaySeconds)} • {last}"
                : $"{game.PlayCount} lancements • {FormatPlayTime(game.TotalPlaySeconds)} • {last}",
            update);
    }

    private void SelectLibraryGame(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DesktopGameCard card }) return;
        SelectCatalogGame(card.Game);
    }

    private void SelectCatalogGame(GameCatalogEntry game)
    {
        _selectedGame = game;
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
        GameTitle.Text = $"{game.Title} • {game.Id}";
        _modManager = new PortableModManager(_paths, game.Id);
        _remoteMods = Array.Empty<GameBananaMod>();
        _selectedMod = null;
        RefreshGameState();
        RefreshModItems();
        RefreshParityGameDetails();
        ShowParityView(GameView);
        _ = RefreshGameUpdatePhase3Async(game, forceUi: true);
    }

    private void RefreshParityGameDetails()
    {
        if (_selectedGame is null || SelectedTitleText is null) return;
        var game = _selectedGame;
        SelectedTitleText.Text = game.Title;
        SelectedMetaText.Text = string.Join("  •  ", new[]
        {
            game.Year > 0 ? game.Year.ToString() : null,
            string.IsNullOrWhiteSpace(game.Genre) ? null : game.Genre,
            string.IsNullOrWhiteSpace(game.Players) ? null : game.Players,
            string.IsNullOrWhiteSpace(game.Region) ? null : game.Region,
            game.Id
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        SelectedDescriptionText.Text = game.Description;

        var runtime = _installer.GetStatus(game.Id);
        var discPath = ResolveApplicationPath(game.DiscImage);
        var executable = runtime.IsInstalled ? runtime.ExecutablePath : ResolveApplicationPath(game.Executable);
        var hasRuntime = runtime.IsInstalled || File.Exists(executable);
        var hasDisc = File.Exists(discPath);
        var runtimeManagesDisc = game.DataPreparation == GameDataPreparation.Runtime;
        var prepared = !runtimeManagesDisc && hasRuntime && _gameData.IsPrepared(game.Id, executable);

        var english = UiLocalization.IsEnglish(_preferences.Language);
        OriginalGameStateText.Text = hasDisc
            ? (english ? "✓ Original game configured" : "✓ Jeu original configuré")
            : (english ? "○ Original game not configured" : "○ Jeu original non configuré");
        RuntimeStateText.Text = hasRuntime
            ? $"✓ {game.RuntimeName} {(english ? "installed" : "installé")}{(string.IsNullOrWhiteSpace(runtime.Version) ? "" : " • " + runtime.Version)}"
            : $"○ {game.RuntimeName} {(english ? "not installed" : "non installé")}";

        // A runtime that prepares the disc itself has no CubeShelf step to report, so the badge
        // says who does it rather than claiming there is work pending that will never happen.
        DataStateText.Text = runtimeManagesDisc
            ? (english ? $"◆ Disc prepared by {game.RuntimeName}" : $"◆ Disque préparé par {game.RuntimeName}")
            : prepared
                ? (english ? "✓ Game data ready" : "✓ Données prêtes")
                : (english ? "○ Game data to prepare" : "○ Données à préparer");
        SetupProgressText.Text = runtimeManagesDisc
            ? $"{(hasRuntime ? "✓" : "○")} {game.RuntimeName}   {(hasDisc ? "✓" : "○")} ISO/RVZ"
            : english
                ? $"{(hasRuntime ? "✓" : "○")} {game.RuntimeName}   {(hasDisc ? "✓" : "○")} ISO/RVZ   {(prepared ? "✓" : "○")} Game data"
                : $"{(hasRuntime ? "✓" : "○")} {game.RuntimeName}   {(hasDisc ? "✓" : "○")} ISO/RVZ   {(prepared ? "✓" : "○")} Données jeu";

        FavoriteButton.Content = game.IsFavorite
            ? (english ? "★ Favorite" : "★ Favori")
            : (english ? "☆ Add to favorites" : "☆ Ajouter aux favoris");
        var cover = game.Covers.FirstOrDefault();
        GameCase.SetCover(
            ResolveCoverPath(cover?.Front),
            ResolveCoverPath(cover?.Back),
            ResolveCoverPath(cover?.Spine));

        if (_gameUpdateSnapshots.TryGetValue(game.Id, out var updateSnapshot))
            ApplyGameUpdateSnapshotPhase3(updateSnapshot);
        else
        {
            GameUpdateStateText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "○ Update not checked"
                : "○ Mise à jour non vérifiée";
            GitHubUpdateStatusText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "Update not checked."
                : "Mise à jour non vérifiée.";
            GitHubCommitText.Text = "";
            GitHubChangesText.Text = "";
            SourceSafetyText.Text = UiLocalization.IsEnglish(_preferences.Language)
                ? "Source code is optional: it is not required to play."
                : "La source est optionnelle : elle n’est pas nécessaire pour jouer.";
            DeleteSourceButton.IsVisible = false;
        }
    }

    private void ToggleFavoriteParity(object? sender, RoutedEventArgs args)
    {
        ToggleFavorite(sender, args);
        RefreshParityGameDetails();
        RefreshLibraryParity();
        var english = UiLocalization.IsEnglish(_preferences.Language);
        ShowToastParity(english ? "Favorites" : "Favoris", _selectedGame?.IsFavorite == true
            ? (english ? "Game added to favorites." : "Jeu ajouté aux favoris.")
            : (english ? "Game removed from favorites." : "Jeu retiré des favoris."));
    }

    private void FlipGameCase(object? sender, RoutedEventArgs args) => GameCase.Flip();

    private string ResolveCoverPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try { return File.Exists(path) ? new Bitmap(path) : null; }
        catch { return null; }
    }

    private async void RunFirstStartWizard(object? sender, RoutedEventArgs args) =>
        await ShowFirstRunWizardAsync(force: true);

    private async Task ShowFirstRunWizardAsync(bool force = false)
    {
        if (!force && _preferences.FirstRunCompleted) return;
        var wizard = new FirstRunWizardWindow(_paths, _preferencesStore, _preferences);
        await wizard.ShowDialog(this);
        if (!wizard.Completed) return;

        _preferences = wizard.Preferences;
        ApplyPreferences();
        RefreshLibraryParity();
        if (wizard.OpenGameAfterFinish && _catalogGames.FirstOrDefault() is { } game)
            SelectCatalogGame(game);
    }


    private void RefreshStorageParity()
    {
        if (StorageSummaryText is null) return;
        var data = MeasureDirectory(_paths.DataDirectory);
        var cache = MeasureDirectory(_paths.CacheDirectory);
        StorageSummaryText.Text = UiLocalization.IsEnglish(_preferences.Language) ? $"CubeShelf data: {FormatBytes(data)} • Cache: {FormatBytes(cache)}" : $"Données CubeShelf : {FormatBytes(data)} • Cache : {FormatBytes(cache)}";
    }



    private string BuildDiagnosticsReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("CubeShelf Diagnostic");
        builder.AppendLine("====================");
        builder.AppendLine($"OS             : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture   : {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Dossier app    : {AppContext.BaseDirectory}");
        builder.AppendLine($"Dossier données: {_paths.DataDirectory}");
        builder.AppendLine($"Cache          : {FormatBytes(MeasureDirectory(_paths.CacheDirectory))}");
        builder.AppendLine();
        if (_selectedGame is null)
        {
            builder.AppendLine("Aucun jeu sélectionné.");
            return builder.ToString();
        }

        var game = _selectedGame;
        var runtime = _installer.GetStatus(game.Id);
        var compatibility = DiscImageService.Inspect(game.DiscImage, game.SupportedDiscIds);
        builder.AppendLine($"Jeu            : {game.Title} ({game.Id})");
        builder.AppendLine($"Favori         : {(game.IsFavorite ? "Oui" : "Non")}");
        builder.AppendLine($"Lancements     : {game.PlayCount}");
        builder.AppendLine($"Temps de jeu   : {FormatPlayTime(game.TotalPlaySeconds)}");
        builder.AppendLine($"PartyBoard     : {(runtime.IsInstalled ? "Installé" : runtime.NeedsRepair ? "À réparer" : "Non installé")}");
        builder.AppendLine($"Runtime version: {runtime.Version}");
        builder.AppendLine($"Executable     : {game.Executable}");
        builder.AppendLine($"Image disque   : {game.DiscImage}");
        builder.AppendLine($"Compatibilité  : {compatibility.Message}");
        builder.AppendLine($"Données prêtes : {_gameData.IsPrepared(game.Id, game.Executable)}");
        builder.AppendLine($"Mods installés : {_modManager?.GetInstalled().Count ?? 0}");
        builder.AppendLine($"Queue en attente: {_portableQueue?.PendingCount ?? 0}");
        if (_gameUpdateSnapshots.TryGetValue(game.Id, out var update))
        {
            builder.AppendLine($"Runtime update : {update.RuntimeUpdateAvailable}");
            builder.AppendLine($"Build en attente: {update.RuntimeUpdatePendingBuild}");
            builder.AppendLine($"Source locale  : {(update.LocalSource.Present ? update.LocalSource.Path : "absente")}");
            builder.AppendLine($"Source gérée   : {update.LocalSource.ManagedByCubeShelf}");
            builder.AppendLine($"Commit source  : {update.LatestSource?.Sha ?? ""}");
            builder.AppendLine($"Commit release : {update.RuntimeRelease.Commit}");
        }
        return builder.ToString();
    }

    private async void ShowToastParity(string title, string message)
    {
        _toastCancellation?.Cancel();
        _toastCancellation?.Dispose();
        _toastCancellation = new CancellationTokenSource();
        var token = _toastCancellation.Token;
        ToastTitle.Text = title;
        ToastMessage.Text = message;
        ToastHost.Opacity = 0;
        ToastHost.IsVisible = true;
        ToastHost.Opacity = 1;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            ToastHost.Opacity = 0;
            await Task.Delay(170, token);
            ToastHost.IsVisible = false;
        }
        catch (OperationCanceledException) { }
    }

    private async void HideToastParity(object? sender, RoutedEventArgs args)
    {
        _toastCancellation?.Cancel();
        ToastHost.Opacity = 0;
        await Task.Delay(170);
        ToastHost.IsVisible = false;
    }

    private static long MeasureDirectory(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return total;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public sealed class DesktopGameCard : IDisposable
    {
        public DesktopGameCard(GameCatalogEntry game, Bitmap? cover, string favorite, string disc, string runtime, string data, string stats, string update)
        {
            Game = game;
            Cover = cover;
            Favorite = favorite;
            Disc = disc;
            Runtime = runtime;
            Data = data;
            Stats = stats;
            Update = update;
        }

        public GameCatalogEntry Game { get; }
        public Bitmap? Cover { get; }
        public string Title => Game.Title;
        public string Subtitle => string.Join(" • ", new[] { Game.Year > 0 ? Game.Year.ToString() : "", Game.Genre }.Where(value => value.Length > 0));
        public string Favorite { get; }
        public string Disc { get; }
        public string Runtime { get; }
        public string Data { get; }
        public string Stats { get; }
        public string Update { get; }
        public void Dispose() => Cover?.Dispose();
    }
}
